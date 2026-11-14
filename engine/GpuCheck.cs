using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PixTune
{
    // Checks once whether each installed AI model really runs on the graphics card (DirectML): the model is
    // opened on the GPU and run on a dummy input; an error or invalid numbers mean it cannot. A model that
    // fails is not tried on the GPU again (it goes straight to the processor) until the check is run again
    // (Settings > Auto test GPU). A model that fails later while in use is marked the same way. Results are
    // remembered per model file, graphics card and runtime, so a new model, card or driver is checked again.
    public static class GpuCheck
    {
        public class Probe
        {
            public string Name, File, Output;           // File: relative to the AI folder; Output: null = the first
            public int Opt = 1;
            public Dictionary<string, long> Dims;       // the same fixed sizes the tool uses
            public Func<OrtSession, Tensor[]> Inputs;
            public bool Heavy;                          // Stable Diffusion: several GB, left out of test runs
        }

        public static string Root = "";                 // the AI folder
        public static bool AtLaunch = true;             // check new models when PixTune starts (off for test runs)
        public static bool SkipHeavy;                   // leave Stable Diffusion out (test runs)
        public static volatile bool Running;
        public static volatile string Working = "";
        public static event EventHandler Changed;       // results changed (raised on any thread)

        // relative file -> "signature|gpu" or "signature|cpu|reason"
        static readonly Dictionary<string, string> results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly object lk = new object();

        static string RuntimePath { get { return Path.Combine(Root, "onnxruntime.dll"); } }

        // ---------- speed of the image tools on this card ----------
        // A strong card runs the image tools several times faster than the processor; a weak integrated one (Intel
        // Iris / UHD, small AMD APUs) can be slower. Once per card, the same edits are timed both ways and the image
        // tools use the faster one (Gpu.ImageTools). Saved as "version@card|gpu or cpu|cpu ms|gpu ms".
        const string SpeedVersion = "1";
        public static string Speed = "";

        static string SpeedKey { get { return SpeedVersion + "@" + string.Join(",", Gpu.AdapterNames.ToArray()); } }
        // what each card managed, one line each, for the tooltip
        public static string Cards = "";

        public static void LoadSpeed(string saved)
        {
            Speed = saved ?? "";
            string[] p = Speed.Split('|');
            if (p.Length >= 2) Gpu.ImageTools = p[1] == "gpu";
            // the card the last test settled on, so the machine starts on it rather than on the biggest one
            if (p.Length >= 5 && p[4].Length > 0) Gpu.Preferred = p[4];
        }

        // not measured yet for this card (or for this version of the shaders)
        public static bool SpeedPending
        {
            get { return Gpu.Enabled && Gpu.Available && Speed.Split('|')[0] != SpeedKey; }
        }

        // "Image tools: graphics card (3.1x faster)" or "Image tools: processor (faster on this PC)"
        public static string SpeedSummary()
        {
            if (!Gpu.Enabled) return "Image tools: processor (GPU off)";
            string[] p = Speed.Split('|');
            double c, g;
            if (p.Length < 4 || !double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c)
                || !double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g) || g <= 0)
                return "Image tools: not measured yet";
            return p[1] == "gpu" ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "Image tools: GPU ({0:0.0}x faster)", c / g)
                                 : "Image tools: processor (faster here)";
        }

        // Times a slider frame (Tune with shadows, Details, Film, then the screen bitmap) on a preview-size picture,
        // median of 3 runs each way after a first run (shader compilation, buffers). Returns a sentence.
        public static string MeasureSpeed()
        {
            int w = 1600, h = 1067;
            Img im = new Img(w, h);
            float[] d = im.D;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float v = 0.5f + 0.3f * (float)Math.Sin(x * 0.021) * (float)Math.Cos(y * 0.017) + ((x * 7 + y * 13) % 17) * 0.004f;
                    d[j] = v; d[j + 1] = 0.3f + 0.4f * y / h; d[j + 2] = 1 - v * 0.8f;
                }
            Step tune = new Step("tune"); tune.Set("brightness", 15); tune.Set("shadows", 30);
            Step det = new Step("details"); det.Set("structure", 30);
            Step film = new Step("film"); film.Set("style", 8); film.Set("strength", 60);
            Step[] steps = { tune, det, film };
            Func<double> frame = () =>
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                Img cur = im;
                foreach (Step s in steps) cur = Pipeline.Apply(cur, s);
                using (System.Drawing.Bitmap b = cur.ToBitmap()) { }
                return sw.Elapsed.TotalMilliseconds;
            };
            Func<int, double> median = mode =>
            {
                Gpu.ForceOnThisThread = mode;
                try
                {
                    frame();
                    double[] t = { frame(), frame(), frame() };
                    Array.Sort(t);
                    return t[1];
                }
                finally { Gpu.ForceOnThisThread = 0; }
            };
            // Every card in the machine is timed on the same frames and the quickest is kept, so a laptop with
            // a built-in card and a discrete one beside it ends up on whichever is really faster here rather
            // than on whichever has the most memory. A machine with one card tries that one and stops.
            double cpuMs = median(1);
            double gpuMs = double.MaxValue; string bestCard = Gpu.Adapter; Cards = "";
            List<string> cards = new List<string>(Gpu.AdapterNames);
            if (cards.Count < 2) { gpuMs = median(2); Cards = Gpu.Adapter + ": " + gpuMs.ToString("0") + " ms"; }
            else
            {
                string started = Gpu.Adapter;
                foreach (string card in cards)
                {
                    Working = "Timing " + card;
                    string sep = Cards.Length > 0 ? Environment.NewLine : "";
                    if (!Gpu.SwitchTo(card)) { Cards += sep + card + ": cannot be used"; continue; }
                    double ms = median(2);
                    Cards += sep + card + ": " + ms.ToString("0") + " ms";
                    if (ms < gpuMs) { gpuMs = ms; bestCard = card; }
                }
                // settle on the winner (and leave the machine on it)
                if (bestCard != Gpu.Adapter) Gpu.SwitchTo(bestCard);
                if (!Gpu.Available) Gpu.SwitchTo(started);
                Working = "";
            }
            bool useGpu = gpuMs < cpuMs * 0.85;
            Speed = SpeedKey + "|" + (useGpu ? "gpu" : "cpu") + "|" + cpuMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "|" + gpuMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "|" + Gpu.Adapter;
            Gpu.ImageTools = useGpu;
            EventHandler hh = Changed;
            if (hh != null) hh(null, EventArgs.Empty);
            return useGpu ? string.Format("Image tools on the graphics card ({0:0} ms a frame, the processor takes {1:0} ms).", gpuMs, cpuMs)
                          : string.Format("Image tools on the processor: this graphics card is slower for them ({0:0} ms a frame against {1:0} ms).", gpuMs, cpuMs);
        }

        public static List<Probe> All()
        {
            List<Probe> l = new List<Probe>();
            l.Add(Image("Sky mask", "masks\\detectron2_semantic.onnx", 800, "sem_seg_logits"));
            l.Add(Image("Subject mask", "masks\\birefnet_lite_fp16.onnx", 1024, null));
            l.Add(Image("AI denoise", "denoise\\nafnet_denoise.onnx", Denoiser.Tile, null));
            l.Add(Image("High detail", "esrgan\\real_esrgan_x4plus.onnx", Upscaler.Tile, null));
            l.Add(Image("Head pose AI (appearance)", "liveportrait\\appearance_feature_extractor.onnx", LivePortrait.Net, null));
            l.Add(Image("Head pose AI (motion)", "liveportrait\\motion_extractor.onnx", LivePortrait.Net, "kp"));
            Probe lama = new Probe { Name = "AI healing (LaMa)", File = "lama_fp32.onnx", Opt = Lama.OptLevel };
            lama.Inputs = s =>
            {
                int n = Lama.Size;
                Tensor[] t = new Tensor[2];
                int im = s.InputNames[0].IndexOf("mask", StringComparison.OrdinalIgnoreCase) < 0 ? 0 : 1;
                t[im] = new Tensor(Pattern(3 * n * n), 1, 3, n, n);
                float[] m = new float[n * n];
                for (int y = n / 3; y < n / 2; y++) for (int x = n / 3; x < n / 2; x++) m[y * n + x] = 1;
                t[1 - im] = new Tensor(m, 1, 1, n, n);
                return t;
            };
            l.Add(lama);
            // Stable Diffusion, sized as Diffusion opens it (a small window keeps the check short)
            int seq = ClipTokenizer.Length;
            l.Add(new Probe
            {
                Name = "Generative fill (text)", File = "sd15-inpaint\\text_encoder\\model.onnx", Heavy = true,
                Dims = D("batch", 1, "sequence", seq),
                Inputs = s => { long[] ids = new long[seq]; ids[0] = 49406; for (int i = 1; i < seq; i++) ids[i] = 49407; return new[] { new Tensor(ids, 1, seq) }; }
            });
            l.Add(new Probe
            {
                Name = "Generative fill (encoder)", File = "sd15-inpaint\\vae_encoder\\model.onnx", Heavy = true,
                Dims = D("batch", 1, "channels", 3),
                Inputs = s => new[] { new Tensor(Pattern(3 * 256 * 256), 1, 3, 256, 256) }
            });
            l.Add(new Probe
            {
                Name = "Generative fill (decoder)", File = "sd15-inpaint\\vae_decoder\\model.onnx", Heavy = true,
                Dims = D("batch", 1, "channels", 4),
                Inputs = s => new[] { new Tensor(Pattern(4 * 32 * 32), 1, 4, 32, 32) }
            });
            l.Add(new Probe
            {
                Name = "Generative fill (UNet)", File = "sd15-inpaint\\unet\\model.onnx", Heavy = true,
                Dims = D("batch", 2, "channels", 9, "sequence", seq),
                Inputs = s =>
                {
                    int iSample = 0, iTime = 1, iText = 2;
                    for (int k = 0; k < s.InputNames.Length; k++)
                    {
                        string nm = s.InputNames[k].ToLowerInvariant();
                        if (nm.Contains("time")) iTime = k; else if (nm.Contains("encoder") || nm.Contains("hidden")) iText = k; else iSample = k;
                    }
                    int b = s.InputShapes[iSample].Length == 4 && s.InputShapes[iSample][0] != 1 ? 2 : 1, h = 32, w = 32;
                    Tensor[] t = new Tensor[3];
                    t[iSample] = new Tensor(Pattern(b * 9 * h * w), b, 9, h, w);
                    t[iText] = new Tensor(Pattern(b * seq * 768), b, seq, 768);
                    long[] ts = s.InputShapes[iTime];
                    if (ts.Length == 0) { t[iTime] = new Tensor(new long[] { 500 }); t[iTime].Shape = new long[0]; }
                    else { int tb = ts[0] > 1 || ts[0] < 0 ? b : 1; long[] v = new long[tb]; for (int i = 0; i < tb; i++) v[i] = 500; t[iTime] = new Tensor(v, tb); }
                    return t;
                }
            });
            return l;
        }

        static Probe Image(string name, string file, int size, string output)
        {
            return new Probe { Name = name, File = file, Output = output, Inputs = s => new[] { new Tensor(Pattern(3 * size * size), 1, 3, size, size) } };
        }

        static Dictionary<string, long> D(params object[] kv)
        {
            Dictionary<string, long> d = new Dictionary<string, long>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = Convert.ToInt64(kv[i + 1]);
            return d;
        }

        // smooth values in 0..1, like a photo, rather than zeros (some layers divide by the input's spread)
        static float[] Pattern(int n)
        {
            float[] a = new float[n];
            for (int i = 0; i < n; i++) a[i] = 0.5f + 0.4f * (float)Math.Sin(i * 0.013) * (float)Math.Cos(i * 0.0007);
            return a;
        }

        public static bool Installed(Probe p) { return Root.Length > 0 && File.Exists(Path.Combine(Root, p.File)) && File.Exists(RuntimePath); }

        static string Key(string path)
        {
            string full = Path.GetFullPath(path), root = Root.Length > 0 ? Path.GetFullPath(Root).TrimEnd('\\') + "\\" : "";
            return root.Length > 0 && full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        // what the result depends on: the model file, the graphics card and the runtime
        static string Signature(string path)
        {
            long len = File.Exists(path) ? new FileInfo(path).Length : 0, rt = File.Exists(RuntimePath) ? new FileInfo(RuntimePath).Length : 0;
            return len + "@" + Gpu.Adapter.Replace("|", "/") + "@" + rt;
        }

        // "gpu", "cpu" or "" (not checked for this model, card and runtime)
        public static string State(string path) { string reason; return State(path, out reason); }

        public static string State(string path, out string reason)
        {
            reason = "";
            string v;
            lock (lk) if (!results.TryGetValue(Key(path), out v)) return "";
            string[] parts = v.Split(new[] { '|' }, 3);
            if (parts.Length < 2 || parts[0] != Signature(path)) return "";
            if (parts.Length > 2) reason = parts[2];
            return parts[1];
        }

        public static bool CpuOnly(string path) { return State(path) == "cpu"; }

        static void Set(string path, string state, string reason)
        {
            lock (lk) results[Key(path)] = Signature(path) + "|" + state + (state == "cpu" ? "|" + (reason ?? "").Replace("\r", " ").Replace("\n", " ") : "");
            EventHandler h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        // Errors that say nothing about the model: the card was short of memory at that moment, or was reset.
        public static bool IsMemoryError(string msg)
        {
            string m = (msg ?? "").ToLowerInvariant();
            return m.Contains("out of memory") || m.Contains("outofmemory") || m.Contains("8007000e") || m.Contains("887a0005") || m.Contains("887a0006") || m.Contains("device removed");
        }

        // A model failed on the GPU while in use (or could not be opened there): use the CPU from now on.
        public static void Failed(string path, string reason)
        {
            if (IsMemoryError(reason) || Root.Length == 0) return;
            Set(path, "cpu", reason);
        }

        public static void Load(string[] saved)
        {
            lock (lk)
            {
                results.Clear();
                if (saved == null) return;
                foreach (string s in saved)
                {
                    int i = s.IndexOf('|');
                    if (i > 0) results[s.Substring(0, i)] = s.Substring(i + 1);
                }
            }
        }

        public static string[] Save()
        {
            lock (lk)
            {
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> kv in results) l.Add(kv.Key + "|" + kv.Value);
                l.Sort(StringComparer.OrdinalIgnoreCase);
                return l.ToArray();
            }
        }

        // installed models not checked yet for this card and runtime
        public static bool Pending
        {
            get
            {
                // Stable Diffusion is not checked at launch (several GB): it is decided when Generative fill first runs
                // (a failure on the card moves it to the processor, OrtSession) or by Auto test GPU
                if (SpeedPending) return true;
                foreach (Probe p in All()) if (Installed(p) && !p.Heavy && State(Path.Combine(Root, p.File)) == "") return true;
                return false;
            }
        }

        // "5 on the graphics card, 1 on the processor"
        public static string Summary()
        {
            int g = 0, c = 0, u = 0;
            foreach (Probe p in All())
            {
                if (!Installed(p)) continue;
                string s = State(Path.Combine(Root, p.File));
                if (s == "gpu") g++; else if (s == "cpu") c++; else u++;
            }
            if (g + c + u == 0) return "no AI model installed";
            List<string> parts = new List<string>();
            if (g > 0) parts.Add(g + " on the GPU");
            if (c > 0) parts.Add(c + " on the CPU");
            if (u > 0) parts.Add(u + " not tested");
            return string.Join(", ", parts.ToArray());
        }

        // one line per installed model
        public static string Details()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Probe p in All())
            {
                if (!Installed(p)) continue;
                string reason, s = State(Path.Combine(Root, p.File), out reason);
                sb.Append(p.Name).Append(": ").Append(s == "gpu" ? "graphics card" : s == "cpu" ? "processor" : "not tested yet");
                if (s == "cpu" && reason.Length > 0) sb.Append(" (" + (reason.Length > 90 ? reason.Substring(0, 90) + "..." : reason) + ")");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        // Checks the installed models in the background, one at a time: all of them (again) when all is true,
        // otherwise only those not checked yet; only: model files to limit the check to (null: every model).
        // progress gets "name (i of n)"; done gets a summary line. Returns false when a check is already running.
        public static bool Start(bool all, string[] only, Action<string> progress, Action<string> done)
        {
            lock (lk) { if (Running) return false; Running = true; }
            Thread t = new Thread(() =>
            {
                string summary = "";
                try { summary = RunAll(all, only, progress); }
                catch (Exception ex) { summary = "GPU check failed: " + ex.Message; }
                finally { Working = ""; Running = false; }
                if (done != null) done(summary);
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
            return true;
        }

        static string RunAll(bool all, string[] only, Action<string> progress)
        {
            if (!Gpu.Enabled) return "GPU acceleration is off: everything runs on the processor.";
            // the image tools first: they need no AI files
            string speed = "";
            if (only == null && Gpu.Available && (all || SpeedPending))
            {
                Working = "speed of the image tools";
                if (progress != null) progress("speed of the image tools");
                speed = MeasureSpeed();
            }
            if (!File.Exists(RuntimePath)) return speed;
            Ort.Init(RuntimePath);
            if (!Ort.HasDirectML || !Gpu.Available) return (speed + " No graphics card usable by the AI models: they run on the processor.").Trim();
            List<Probe> todo = new List<Probe>();
            foreach (Probe p in All())
            {
                // Stable Diffusion only when asked (Auto test GPU), never at launch
                if (!Installed(p) || (p.Heavy && (SkipHeavy || !all)) || (only != null && Array.IndexOf(only, p.File) < 0)) continue;
                if (all || State(Path.Combine(Root, p.File)) == "") todo.Add(p);
            }
            if (todo.Count == 0) return speed;
            List<string> gpu = new List<string>(), cpu = new List<string>();
            for (int i = 0; i < todo.Count; i++)
            {
                Probe p = todo[i];
                Working = p.Name;
                if (progress != null) progress(p.Name + " (" + (i + 1) + " of " + todo.Count + ")");
                string path = Path.Combine(Root, p.File), err = Test(p, path);
                if (err == null) { Set(path, "gpu", null); gpu.Add(p.Name); }
                else
                {
                    // short of memory counts as a failure too: retrying at every launch is what makes a PC with a
                    // small or shared-memory card slow (Auto test GPU tries again)
                    Set(path, "cpu", IsMemoryError(err) ? "not enough graphics memory (" + err + ")" : err);
                    cpu.Add(p.Name);
                }
                GC.Collect();
            }
            string s = "AI models checked: " + gpu.Count + " run on the graphics card";
            if (cpu.Count > 0) s += ", " + cpu.Count + " on the processor (" + string.Join(", ", cpu.ToArray()) + ")";
            return (speed + " " + s + ".").Trim();
        }

        // null when the model runs on the GPU and gives valid numbers, otherwise the reason it cannot
        static string Test(Probe p, string path)
        {
            try
            {
                using (OrtSession s = OrtSession.OpenGpu(path, p.Opt, p.Dims))
                {
                    Tensor[] inputs = p.Inputs(s);
                    float[] o = (p.Output == null ? s.Run(inputs) : s.Run(p.Output, inputs)).F;
                    for (int i = 0; i < o.Length; i++)
                        if (float.IsNaN(o[i]) || float.IsInfinity(o[i])) return "invalid results on the graphics card";
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}

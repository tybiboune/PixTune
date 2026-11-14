using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PixTune
{
    // CLIP's byte-level BPE tokenizer (vocab.json + merges.txt), as used by Stable Diffusion's text encoder.
    public class ClipTokenizer
    {
        public const int Length = 77, Start = 49406, End = 49407;
        readonly Dictionary<string, int> vocab = new Dictionary<string, int>();
        readonly Dictionary<string, int> ranks = new Dictionary<string, int>();
        readonly char[] byteChar = new char[256];
        static readonly Regex Pattern = new Regex(@"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+", RegexOptions.IgnoreCase);

        public ClipTokenizer(string vocabPath, string mergesPath)
        {
            System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> v = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(vocabPath, Encoding.UTF8));
            foreach (KeyValuePair<string, object> kv in v) vocab[kv.Key] = Convert.ToInt32(kv.Value);
            string[] lines = File.ReadAllLines(mergesPath, Encoding.UTF8);
            int r = 0;
            for (int i = 1; i < lines.Length; i++)   // the first line is a version header
            {
                if (lines[i].Length == 0) continue;
                ranks[lines[i]] = r++;
            }
            // GPT-2 byte to unicode table: printable bytes map to themselves, the others above 255
            List<int> bs = new List<int>();
            for (int b = '!'; b <= '~'; b++) bs.Add(b);
            for (int b = 0xA1; b <= 0xAC; b++) bs.Add(b);
            for (int b = 0xAE; b <= 0xFF; b++) bs.Add(b);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (bs.Contains(b)) byteChar[b] = (char)b;
                else byteChar[b] = (char)(256 + n++);
            }
        }

        public long[] Encode(string text)
        {
            List<long> ids = new List<long>();
            ids.Add(Start);
            text = Regex.Replace((text ?? "").Trim(), @"\s+", " ").ToLowerInvariant();
            foreach (Match m in Pattern.Matches(text))
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in Encoding.UTF8.GetBytes(m.Value)) sb.Append(byteChar[b]);
                foreach (string piece in Bpe(sb.ToString()))
                {
                    int id;
                    if (vocab.TryGetValue(piece, out id)) ids.Add(id);
                }
            }
            if (ids.Count > Length - 1) ids.RemoveRange(Length - 1, ids.Count - (Length - 1));
            ids.Add(End);
            while (ids.Count < Length) ids.Add(End);
            return ids.ToArray();
        }

        List<string> Bpe(string token)
        {
            List<string> word = new List<string>();
            for (int i = 0; i < token.Length; i++) word.Add(i == token.Length - 1 ? token[i] + "</w>" : token[i].ToString());
            while (word.Count > 1)
            {
                int best = int.MaxValue, at = -1;
                for (int i = 0; i < word.Count - 1; i++)
                {
                    int rk;
                    if (ranks.TryGetValue(word[i] + " " + word[i + 1], out rk) && rk < best) { best = rk; at = i; }
                }
                if (at < 0) break;
                string a = word[at], b = word[at + 1];
                List<string> merged = new List<string>();
                for (int i = 0; i < word.Count; i++)
                {
                    if (i < word.Count - 1 && word[i] == a && word[i + 1] == b) { merged.Add(a + b); i++; }
                    else merged.Add(word[i]);
                }
                word = merged;
            }
            return word;
        }
    }

    // Optional generative fill with Stable Diffusion 1.5 inpainting (CreativeML OpenRAIL-M; ONNX fp16 export),
    // run through ONNX Runtime on the GPU with DirectML. Used by Expand to invent what lies past the frame.
    public static class Diffusion
    {
        public static string Dir = "";        // folder with text_encoder, unet, vae_encoder, vae_decoder, tokenizer
        public static string RuntimePath = ""; // onnxruntime.dll (shared with LaMa)
        public static string Device = "";
        public static string LastError = "";
        public static int Steps = 18;
        public static float Guidance = 7.5f;
        public static volatile float Progress; // 0..1 while generating
        public static volatile string Status = ""; // what the progress is about when not the main generation
        public static string LastTiming = "";
        // describes what the extension should look like when the user gives no prompt; the negative prompt
        // keeps the model from drawing frames, borders or text along the new edge
        public static string DefaultPrompt = "";
        // how much of the starting picture is replaced: 1 starts from pure noise, lower keeps more of the pre-fill
        public static float Strength = 0.9f;
        public static string NegativePrompt = "frame, border, text, watermark, signature, blurry, low quality, distorted";

        const float LatentScale = 0.18215f;
        static volatile bool tried, ok;
        static readonly object lk = new object(), loadLk = new object(); // lk: one generation at a time
        static OrtSession textEncoder, unet, vaeEncoder, vaeDecoder;
        static ClipTokenizer tokenizer;
        static readonly Dictionary<string, float[]> embedCache = new Dictionary<string, float[]>();
        public static void Forget() { lock (embedCache) embedCache.Clear(); }

        static string P(string sub) { return Path.Combine(Dir, sub); }
        public static readonly string[] Files = {
            "text_encoder\\model.onnx", "unet\\model.onnx", "vae_encoder\\model.onnx", "vae_decoder\\model.onnx",
            "tokenizer\\vocab.json", "tokenizer\\merges.txt" };

        public static bool Installed
        {
            get
            {
                if (Dir.Length == 0 || !File.Exists(RuntimePath)) return false;
                foreach (string f in Files) if (!File.Exists(P(f))) return false;
                return true;
            }
        }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }

        public static void WarmUp()
        {
            if (!tried && Installed) System.Threading.Tasks.Task.Factory.StartNew(EnsureLoaded);
        }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                // marked as tried only once loading is over: a caller arriving meanwhile waits on the lock
                // instead of seeing "not available" and silently falling back
                try
                {
                    if (!Installed) throw new Exception("the generative fill files are not installed");
                    Ort.Init(RuntimePath);
                    tokenizer = new ClipTokenizer(P("tokenizer\\vocab.json"), P("tokenizer\\merges.txt"));
                    // fixed batch, channel and sequence sizes keep shape arithmetic off the CPU; height and
                    // width stay free so each window keeps the photo's proportions
                    textEncoder = OrtSession.Open(P("text_encoder\\model.onnx"), true, 1, Dims("batch", 1, "sequence", ClipTokenizer.Length));
                    vaeEncoder = OrtSession.Open(P("vae_encoder\\model.onnx"), true, 1, Dims("batch", 1, "channels", 3));
                    vaeDecoder = OrtSession.Open(P("vae_decoder\\model.onnx"), true, 1, Dims("batch", 1, "channels", 4));
                    unet = OrtSession.Open(P("unet\\model.onnx"), true, 1, Dims("batch", 2, "channels", 9, "sequence", ClipTokenizer.Length));
                    if (unet.InputNames.Length != 3) throw new Exception("unexpected UNet inputs");
                    Device = unet.Device;
                    LastError = "";
                    ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // Frees the models' video memory (the next generation loads them again). High detail calls this:
        // on an 8 GB card the upscaler runs ten times slower while these are still resident.
        public static void Unload()
        {
            lock (lk)
                lock (loadLk)
                {
                    foreach (OrtSession s in new[] { textEncoder, unet, vaeEncoder, vaeDecoder }) if (s != null) s.Dispose();
                    textEncoder = unet = vaeEncoder = vaeDecoder = null;
                    embedCache.Clear();
                    ok = false; tried = false;
                }
        }
        static Dictionary<string, long> Dims(params object[] kv)
        {
            Dictionary<string, long> d = new Dictionary<string, long>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = Convert.ToInt64(kv[i + 1]);
            return d;
        }

        // Text embedding [1, 77, 768] for a prompt, cached.
        static float[] Embed(string prompt)
        {
            float[] e;
            if (embedCache.TryGetValue(prompt, out e)) return e;
            long[] ids = tokenizer.Encode(prompt);
            e = textEncoder.Run(new Tensor(ids, 1, ClipTokenizer.Length)).F;
            embedCache[prompt] = e;
            return e;
        }

        // Fills the hole (true = generate) of an image whose sides are multiples of 64 (values 0..1).
        // Returns the generated image (whole frame), or null when the model is unavailable.
        public static Img Inpaint(Img im, bool[] hole, string prompt, int seed) { return Inpaint(im, hole, prompt, seed, null); }

        // init: the same image with the hole already roughly filled (content-aware). Generation then starts
        // from it, partly noised, so the new area keeps the scene's colours and layout instead of becoming
        // an unrelated picture next to the photo, a known weakness of inpainting models when outpainting.
        public static Img Inpaint(Img im, bool[] hole, string prompt, int seed, Img init) { return Inpaint(im, hole, prompt, seed, init, Strength); }

        public static Img Inpaint(Img im, bool[] hole, string prompt, int seed, Img init, float strength)
        {
            if (im.W % 64 != 0 || im.H % 64 != 0) throw new ArgumentException("sides must be multiples of 64");
            if (!Available) return null;
            lock (lk)
            {
                Progress = 0;
                System.Diagnostics.Stopwatch tw = System.Diagnostics.Stopwatch.StartNew(); StringBuilder tl = new StringBuilder();
                int W = im.W, H = im.H, w = W / 8, h = H / 8, n = W * H, ln = w * h;
                float[] cond = Embed(string.IsNullOrEmpty(prompt) ? DefaultPrompt : prompt);
                if (cond == null) throw new Exception("text encoder failed");
                float[] uncond = Embed(NegativePrompt);
                tl.Append("text " + tw.ElapsedMilliseconds); tw.Restart();

                // the masked photo (hole set to grey) encoded to latents
                float[] px = new float[3 * n];
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < 3; c++) px[c * n + i] = hole[i] ? 0f : im.D[i * 3 + c] * 2f - 1f;
                Tensor enc = vaeEncoder.Run(new Tensor(px, 1, 3, H, W));
                float[] masked = new float[4 * ln];
                // some exports return the latent distribution's mean and log-variance (8 channels): take the mean
                for (int i = 0; i < 4 * ln; i++) masked[i] = enc.F[i] * LatentScale;
                tl.Append(" vae-enc " + tw.ElapsedMilliseconds); tw.Restart();

                // the hole at latent size: a latent cell is regenerated when any of its pixels is in the hole
                float[] mask = new float[ln];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (hole[y * W + x]) mask[(y / 8) * w + x / 8] = 1;

                // DDIM schedule over the 1000 training steps (scaled linear betas, as SD 1.5 was trained)
                double[] acp = new double[1000];
                double prod = 1;
                for (int t = 0; t < 1000; t++)
                {
                    double b = Math.Sqrt(0.00085) + (Math.Sqrt(0.012) - Math.Sqrt(0.00085)) * t / 999.0;
                    prod *= 1 - b * b; acp[t] = prod;
                }
                int steps = Math.Max(4, Steps), ratio = 1000 / steps;
                Random rnd = new Random(seed);
                float[] lat = new float[4 * ln];
                for (int i = 0; i < lat.Length; i++) lat[i] = Gauss(rnd);
                int first = 0;
                if (init != null && strength < 0.999f)
                {
                    // start part-way: the pre-filled picture noised to the first timestep used
                    first = Math.Min(steps - 1, steps - (int)Math.Round(steps * strength));
                    float[] ipx = new float[3 * n];
                    for (int i = 0; i < n; i++)
                        for (int c = 0; c < 3; c++) ipx[c * n + i] = init.D[i * 3 + c] * 2f - 1f;
                    float[] il = vaeEncoder.Run(new Tensor(ipx, 1, 3, H, W)).F;
                    int t0 = (steps - 1 - first) * ratio + 1;
                    double a0 = Math.Sqrt(acp[t0]), b0 = Math.Sqrt(1 - acp[t0]);
                    for (int i = 0; i < lat.Length; i++) lat[i] = (float)(a0 * il[i] * LatentScale + b0 * lat[i]);
                }

                bool batched = unet.InputShapes[0].Length == 4 && unet.InputShapes[0][0] != 1;
                int iSample = 0, iTime = 1, iText = 2;
                for (int k = 0; k < unet.InputNames.Length; k++)
                {
                    string nm = unet.InputNames[k].ToLowerInvariant();
                    if (nm.Contains("time")) iTime = k; else if (nm.Contains("encoder") || nm.Contains("hidden")) iText = k; else iSample = k;
                }
                for (int s = first; s < steps; s++)
                {
                    Pipeline.CheckCancel();
                    int t = (steps - 1 - s) * ratio + 1;
                    float[] x = new float[9 * ln];
                    Array.Copy(lat, 0, x, 0, 4 * ln);
                    Array.Copy(mask, 0, x, 4 * ln, ln);
                    Array.Copy(masked, 0, x, 5 * ln, 4 * ln);
                    float[] eu, ec;
                    if (batched)
                    {
                        float[] x2 = new float[18 * ln]; Array.Copy(x, x2, x.Length); Array.Copy(x, 0, x2, x.Length, x.Length);
                        float[] e2 = new float[uncond.Length * 2]; Array.Copy(uncond, e2, uncond.Length); Array.Copy(cond, 0, e2, uncond.Length, cond.Length);
                        float[] o = RunUnet(iSample, iTime, iText, new Tensor(x2, 2, 9, h, w), t, 2, new Tensor(e2, 2, ClipTokenizer.Length, cond.Length / ClipTokenizer.Length));
                        eu = new float[4 * ln]; ec = new float[4 * ln];
                        Array.Copy(o, 0, eu, 0, 4 * ln); Array.Copy(o, 4 * ln, ec, 0, 4 * ln);
                    }
                    else
                    {
                        long[] es = { 1, ClipTokenizer.Length, cond.Length / ClipTokenizer.Length };
                        eu = RunUnet(iSample, iTime, iText, new Tensor(x, 1, 9, h, w), t, 1, new Tensor(uncond, es));
                        ec = RunUnet(iSample, iTime, iText, new Tensor(x, 1, 9, h, w), t, 1, new Tensor(cond, es));
                    }
                    double at = acp[t], ap = t - ratio >= 0 ? acp[t - ratio] : acp[0];
                    double sa = Math.Sqrt(at), s1 = Math.Sqrt(1 - at), sp = Math.Sqrt(ap), s1p = Math.Sqrt(1 - ap);
                    for (int i = 0; i < lat.Length; i++)
                    {
                        double eps = eu[i] + Guidance * (ec[i] - eu[i]);
                        double x0 = (lat[i] - s1 * eps) / sa;
                        lat[i] = (float)(sp * x0 + s1p * eps);
                    }
                    Progress = (s + 1f - first) / (steps - first);
                }

                tl.Append(" unet " + tw.ElapsedMilliseconds); tw.Restart();
                for (int i = 0; i < lat.Length; i++) lat[i] /= LatentScale;
                Tensor dec = vaeDecoder.Run(new Tensor(lat, 1, 4, h, w));
                Img r = new Img(W, H);
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < 3; c++) r.D[i * 3 + c] = Math.Max(0f, Math.Min(1f, dec.F[c * n + i] * 0.5f + 0.5f));
                tl.Append(" vae-dec " + tw.ElapsedMilliseconds); LastTiming = tl.ToString();
                Progress = 0;
                return r;
            }
        }

        static float[] RunUnet(int iSample, int iTime, int iText, Tensor sample, int t, int batch, Tensor text)
        {
            long[] ts = unet.InputShapes[iTime];
            Tensor time = ts.Length == 0 ? new Tensor(new long[] { t })
                : new Tensor(Fill(t, ts[0] > 1 || ts[0] < 0 ? batch : 1), ts[0] > 1 || ts[0] < 0 ? batch : 1);
            if (ts.Length == 0) time.Shape = new long[0];
            Tensor[] inputs = new Tensor[3];
            inputs[iSample] = sample; inputs[iTime] = time; inputs[iText] = text;
            return unet.Run(inputs).F;
        }

        static long[] Fill(long v, int n) { long[] a = new long[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        static float Gauss(Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }
}

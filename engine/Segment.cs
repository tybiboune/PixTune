using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // AI one-tap masks, run through ONNX Runtime on the GPU (DirectML) when possible:
    //  - subject: BiRefNet-lite (Zheng et al., "Bilateral Reference for High-Resolution Dichotomous Image
    //    Segmentation", MIT licence; ONNX export by onnx-community), the main subject with fine edges;
    //  - sky: the semantic head of Detectron2 Panoptic FPN (Facebook, Apache-2.0; ONNX export by Qualcomm
    //    AI Hub), class "sky" of COCO panoptic.
    // Masks are computed once per photo at a fixed working size and reused (preview, sliders, export).
    public static class Segmenter
    {
        public static string Dir = "";          // folder holding the two models
        public static string RuntimePath = "";
        public static string Device = "", LastError = "";
        public static volatile string Working = ""; // shown in the status line while a mask is computed
        const int SubjectSize = 1024, SkySize = 800, SkyClass = 40, StuffClasses = 54;
        const int WorkSize = 1024;              // masks are made from the photo fitted in this size
        static volatile bool tried, ok;
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession subject, sky;

        public static string SubjectModel { get { return Path.Combine(Dir, "birefnet_lite_fp16.onnx"); } }
        public static string SkyModel { get { return Path.Combine(Dir, "detectron2_semantic.onnx"); } }
        public static bool Installed
        {
            get
            {
                return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(SubjectModel) && File.Exists(SkyModel)
                    && File.Exists(Path.Combine(Dir, "proposal_generator.data"));
            }
        }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }
        public static void WarmUp() { if (!tried && Installed) Task.Factory.StartNew(EnsureLoaded); }

        // The runtime starts once; each model is loaded the first time its kind of mask is asked for (a Sky mask
        // does not wait for the larger subject model, and an unused model takes no graphics memory).
        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the AI mask models are not installed");
                    Ort.Init(RuntimePath);
                    LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        static OrtSession Session(bool forSky)
        {
            lock (loadLk)
            {
                if (forSky) { if (sky == null) sky = OrtSession.Open(SkyModel, true, 1); Device = sky.Device; return sky; }
                if (subject == null) subject = OrtSession.Open(SubjectModel, true, 1);
                Device = subject.Device;
                return subject;
            }
        }

        class Entry { public string Kind; public float[] Print; public int W, H; public float[] Mask; }
        static readonly List<Entry> cache = new List<Entry>();
        public static void Forget() { lock (cache) cache.Clear(); }

        // Mask of the given kind ("subject", "sky") for the working-size picture, cached by content.
        // Returns null when the models are not available.
        public static float[] Mask(Img work, string kind)
        {
            if (!Available) return null;
            float[] print = Print(work);
            lock (cache)
                foreach (Entry e in cache)
                    if (e.Kind == kind && Same(e.Print, print))
                        return e.W == work.W && e.H == work.H ? e.Mask : Ops.ResizePlane(e.Mask, e.W, e.H, work.W, work.H);
            float[] m;
            lock (runLk)
            {
                // the window and the renderer often ask for the same mask at once: the one that waited takes the
                // other's result instead of running the model a second time
                lock (cache)
                    foreach (Entry e in cache)
                        if (e.Kind == kind && Same(e.Print, print))
                            return e.W == work.W && e.H == work.H ? e.Mask : Ops.ResizePlane(e.Mask, e.W, e.H, work.W, work.H);
                Working = kind == "sky" ? "Finding the sky" : "Finding the subject";
                try { m = kind == "sky" ? Sky(work) : Subject(work); }
                catch (Exception ex) { LastError = ex.Message; return null; }
                finally { Working = ""; }
            }
            lock (cache)
            {
                cache.Add(new Entry { Kind = kind, Print = print, W = work.W, H = work.H, Mask = m });
                if (cache.Count > 8) cache.RemoveAt(0);
            }
            return m;
        }

        public static int Work { get { return WorkSize; } }

        static float[] Subject(Img im)
        {
            int n = SubjectSize, N = n * n;
            Img r = Ops.Resize(im, n, n);
            float[] x = new float[3 * N];
            float[] mean = { 0.485f, 0.456f, 0.406f }, std = { 0.229f, 0.224f, 0.225f };
            for (int i = 0; i < N; i++)
                for (int c = 0; c < 3; c++) x[c * N + i] = (r.D[i * 3 + c] - mean[c]) / std[c];
            float[] o = Session(false).Run(new Tensor(x, 1, 3, n, n)).F;
            float[] p = new float[N];
            for (int i = 0; i < N; i++) p[i] = 1f / (1f + (float)Math.Exp(-o[i]));
            return Ops.ResizePlane(p, n, n, im.W, im.H);
        }

        static float[] Sky(Img im)
        {
            int n = SkySize, N = n * n;
            Img r = Ops.Resize(im, n, n);
            float[] x = new float[3 * N];
            for (int i = 0; i < N; i++)
                for (int c = 0; c < 3; c++) x[c * N + i] = r.D[i * 3 + c];
            Tensor t = Session(true).Run("sem_seg_logits", new Tensor(x, 1, 3, n, n));
            int sw = (int)t.Shape[3], sh = (int)t.Shape[2], S = sw * sh;
            float[] p = new float[S];
            for (int i = 0; i < S; i++)
            {
                float mx = float.MinValue;
                for (int k = 0; k < StuffClasses; k++) mx = Math.Max(mx, t.F[k * S + i]);
                double den = 0;
                for (int k = 0; k < StuffClasses; k++) den += Math.Exp(t.F[k * S + i] - mx);
                p[i] = (float)(Math.Exp(t.F[SkyClass * S + i] - mx) / den);
            }
            return Ops.ResizePlane(p, sw, sh, im.W, im.H);
        }

        // 8x8 average colours: the same photo from the preview or the original gives the same print
        static float[] Print(Img im)
        {
            float[] p = new float[8 * 8 * 3];
            int[] cnt = new int[64];
            for (int y = 0; y < im.H; y++)
                for (int x = 0; x < im.W; x++)
                {
                    int cell = (y * 8 / im.H) * 8 + x * 8 / im.W, i = (y * im.W + x) * 3;
                    cnt[cell]++;
                    for (int c = 0; c < 3; c++) p[cell * 3 + c] += im.D[i + c];
                }
            for (int i = 0; i < p.Length; i++) p[i] /= Math.Max(1, cnt[i / 3]);
            return p;
        }

        static bool Same(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 0.015f) return false;
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // "Pick" masks: the objects under clicks, found by MobileSAM (Zhang et al., "Faster Segment Anything",
    // Apache-2.0, a distilled Segment Anything; ONNX export by Qualcomm AI Hub). The image encoder runs once per
    // picture (its embedding is kept), each click then costs one small decoder run. Clicked parts are joined;
    // parts clicked to remove are cut out.
    public static class SubjectPicker
    {
        public static string Dir = "", RuntimePath = "", Device = "", LastError = "";
        public static volatile string Working = "";
        const int Size = 1024, Low = 256;
        static volatile bool tried, ok;
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession encoder, decoder;

        public static bool Installed
        {
            get
            {
                if (Dir.Length == 0 || !File.Exists(RuntimePath)) return false;
                foreach (string f in new[] { "encoder.onnx", "encoder.data", "decoder.onnx", "decoder.data" })
                    if (!File.Exists(Path.Combine(Dir, f))) return false;
                return true;
            }
        }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }
        public static void WarmUp() { if (!tried && Installed) Task.Factory.StartNew(EnsureLoaded); }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the subject picker model is not installed");
                    Ort.Init(RuntimePath);
                    // DirectML refuses this encoder (one of its operators): it is opened on the processor directly
                    // instead of failing on the graphics card first (half a second saved)
                    encoder = OrtSession.Open(Path.Combine(Dir, "encoder.onnx"), false, 1);
                    decoder = OrtSession.Open(Path.Combine(Dir, "decoder.onnx"), false, 1);
                    Device = encoder.Device; LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // ---------------- image embedding (once per picture) ----------------
        class Emb { public Img Im; public float[] E; public Img Work; }
        static readonly List<Emb> embs = new List<Emb>();
        public static void Forget() { lock (embs) embs.Clear(); lock (kept) { kept.Clear(); big = null; bigFrom = null; } }

        static Emb Embed(Img im)
        {
            lock (embs) foreach (Emb e in embs) if (e.Im == im) return e;
            // longest side to 1024, padded right and bottom (Segment Anything's layout)
            float s = (float)Size / Math.Max(im.W, im.H);
            int w = Math.Max(1, (int)Math.Round(im.W * s)), h = Math.Max(1, (int)Math.Round(im.H * s));
            Img r = (w == im.W && h == im.H) ? im : Ops.Resize(im, w, h);
            float[] x = new float[3 * Size * Size];
            for (int y = 0; y < h; y++)
                for (int xx = 0; xx < w; xx++)
                {
                    int j = (y * w + xx) * 3, o = y * Size + xx;
                    x[o] = r.D[j]; x[Size * Size + o] = r.D[j + 1]; x[2 * Size * Size + o] = r.D[j + 2];
                }
            Working = "Finding the objects";
            float[] emb;
            try { emb = encoder.Run(new Tensor(x, 1, 3, Size, Size)).F; }
            finally { Working = ""; }
            Emb n = new Emb { Im = im, E = emb, Work = r };
            lock (embs) { embs.Add(n); while (embs.Count > 2) embs.RemoveAt(0); }
            return n;
        }

        // ---------------- masks ----------------
        class Kept { public Img Im; public string Pts; public float[] M; public int W, H; }
        static readonly List<Kept> kept = new List<Kept>();
        // The last full-size copy of a mask. The model works at 1024 on its longest side, so a mask for the whole
        // photo used to be stretched to the photo's size and four of them kept: at 24 megapixels that is 96 MB
        // each, 380 MB held for masks that a part of the photo samples from anyway. One is enough.
        static float[] bigFrom, big; static int bigW, bigH;

        static Kept Find(Img im, string pts)
        {
            lock (kept) foreach (Kept k in kept) if (k.Im == im && k.Pts == pts) return k;
            return null;
        }

        // The mask at the size the model works at, with that size, for a caller that is going to sample or scale
        // it anyway: a part of the photo at 100% takes what it needs straight from this.
        public static float[] MaskSmall(Img im, string pts, out int mw, out int mh)
        {
            mw = im.W; mh = im.H;
            List<float[]> clicks = ParsePoints(pts);
            if (clicks.Count == 0 || !Available) return null;
            Kept hit = Find(im, pts);
            if (hit == null)
                lock (runLk)
                {
                    // asked by the window and the renderer at once: the one that waited takes the other's result
                    hit = Find(im, pts);
                    if (hit == null)
                    {
                        try
                        {
                            Emb e = Embed(im);
                            Pipeline.CheckCancel();
                            hit = new Kept { Im = im, Pts = pts, M = Decode(e, clicks), W = e.Work.W, H = e.Work.H };
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { LastError = ex.Message; return null; }
                        lock (kept) { kept.Add(hit); while (kept.Count > 4) kept.RemoveAt(0); }
                    }
                }
            mw = hit.W; mh = hit.H;
            return hit.M;
        }

        // pts: "x,y;x,y" in 0..1 of the picture. Soft mask at the picture's size, or null when the model is not
        // available. Callers must not change the result.
        public static float[] Mask(Img im, string pts)
        {
            int mw, mh;
            float[] m = MaskSmall(im, pts, out mw, out mh);
            if (m == null || (mw == im.W && mh == im.H)) return m;
            lock (kept) if (big != null && bigFrom == m && bigW == im.W && bigH == im.H) return big;
            float[] r = Ops.ResizePlane(m, mw, mh, im.W, im.H);
            lock (kept) { bigFrom = m; big = r; bigW = im.W; bigH = im.H; }
            return r;
        }

        // parts clicked to add are joined, then parts clicked to remove are cut out of them
        static float[] Decode(Emb e, List<float[]> clicks)
        {
            Img r = e.Work;
            int w = r.W, h = r.H;
            // the decoder's 256 x 256 logits cover the padded 1024 frame: the picture is its top-left part
            int lw = Math.Max(1, (int)Math.Round(w * (float)Low / Size)), lh = Math.Max(1, (int)Math.Round(h * (float)Low / Size));
            float[] union = new float[w * h], cut = new float[w * h];
            foreach (float[] c in clicks)
            {
                float[] into = c[2] > 0 ? union : cut;
                Tensor[] inputs = new Tensor[3];
                inputs[decoder.InputIndex("image_embeddings")] = new Tensor(e.E, 1, 256, 64, 64);
                inputs[decoder.InputIndex("point_coords")] = new Tensor(new float[] { c[0] * w, c[1] * h }, 1, 1, 2);
                inputs[decoder.InputIndex("point_labels")] = new Tensor(new float[] { 1f }, 1, 1);
                float[] lo = decoder.Run("masks", inputs).F;
                float[] crop = new float[lw * lh];
                for (int y = 0; y < lh; y++) Array.Copy(lo, y * Low, crop, y * lw, lw);
                float[] up = Ops.ResizePlane(crop, lw, lh, w, h);
                for (int i = 0; i < up.Length; i++)
                {
                    float p = 1f / (1f + (float)Math.Exp(-up[i]));
                    if (p > into[i]) into[i] = p;
                }
            }
            for (int i = 0; i < union.Length; i++) union[i] *= 1 - cut[i];
            // snapped to the real edges of the photo; it stays at the size the model works at, and whoever wants
            // it at the photo's size says so
            float[] L = Ops.Luma(r);
            float[] m = Ops.Guided(L, union, w, h, Math.Max(2, Math.Min(w, h) / 200), 0.0006f);
            for (int i = 0; i < m.Length; i++) m[i] = Ops.SmoothStep(0.3f, 0.7f, m[i]);
            return m;
        }

        // "x,y" adds the part under that point, "x,y,0" removes it; result: { x, y, 1 or 0 }
        public static List<float[]> ParsePoints(string pts)
        {
            List<float[]> l = new List<float[]>();
            if (string.IsNullOrEmpty(pts)) return l;
            foreach (string p in pts.Split(';'))
            {
                string[] a = p.Split(',');
                float x, y;
                if (a.Length >= 2 && float.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    && float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    l.Add(new float[] { Ops.C01(x), Ops.C01(y), a.Length >= 3 && a[2].Trim() == "0" ? 0f : 1f });
            }
            return l;
        }
    }
}

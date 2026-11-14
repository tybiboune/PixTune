using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // How far away everything in the photo is, from the photo alone: Depth Anything V2 Small (Apache-2.0, ONNX
    // export from onnx-community), which gives relative inverse depth - bigger means nearer. Lens Blur's depth
    // mode uses it to decide what is in focus and what is not.
    //
    // Why this model and not a bigger one. Depth Anything 3 Small was tried beside it on the same photos: on a
    // close portrait it returns the subject as a near-flat white cut-out against a blotchy background, which is
    // a segmentation, not a depth field, and it makes hair into a hard silhouette - the look of a bad phone
    // portrait mode. V2 keeps the strands, and keeps a real gradient from hand to face to shoulder, which is
    // what a lens actually does. V2 is also half the download and quicker. The input size matters more than the
    // model's size here: the same model at 1036 across holds hair that it loses at 518, so the longest side is
    // set from what the card can hold rather than left at the trained 518.
    public static class Depther
    {
        public static string Dir = "", RuntimePath = "", Device = "", LastError = "";
        public static volatile string Working = "";
        static volatile bool tried, ok, warming;
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession session;
        static int openedAt;                    // the side the open session was fixed at

        public static string ModelPath { get { return Path.Combine(Dir, "depth_anything_v2_small_fp16.onnx"); } }
        public static bool Installed { get { return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(ModelPath); } }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }

        // Longest side given to the model, a whole number of 14-pixel patches. 1036 holds hair that 518 loses;
        // 1540 holds a little more again and costs four times as much; 2044 ran the card out of memory.
        public static int Side
        {
            get
            {
                long vram = Gpu.VideoMemory;
                return vram >= (6L << 30) ? 1540 : 1036;
            }
        }
        static int Patches(double v) { int n = (int)Math.Round(v / 14.0); return Math.Max(1, n) * 14; }

        public static void WarmUp()
        {
            if (!Installed || warming) return;
            warming = true;
            Task.Factory.StartNew(() => { EnsureLoaded(); if (ok) try { Session(Side); } catch (Exception) { } });
        }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the depth model is not installed");
                    Ort.Init(RuntimePath);
                    LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // The model takes any size, so the session is fixed to the one this photo needs: with the sizes left
        // open DirectML works the shapes out on the processor at every run.
        static OrtSession Session(int side)
        {
            lock (loadLk)
            {
                if (session != null && openedAt == side) { Device = session.Device; return session; }
                if (session != null) { session.Dispose(); session = null; }
                Dictionary<string, long> dims = new Dictionary<string, long>();
                dims["batch_size"] = 1;
                session = OrtSession.Open(ModelPath, true, 1, dims);
                openedAt = side;
                Device = session.Device;
                return session;
            }
        }

        // last few maps, by what the picture looks like rather than which object it is: the renderer hands over
        // a new picture for every frame, and the map would be made again for each one
        class Kept { public float[] Print; public float[] M; public int W, H; }
        static readonly List<Kept> kept = new List<Kept>();
        public static void Forget() { lock (kept) kept.Clear(); }

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
        static Kept Find(float[] print)
        {
            lock (kept) foreach (Kept k in kept) if (Same(k.Print, print)) return k;
            return null;
        }

        // 0 far, 1 near, at the size the model worked at; null when the model is not there. Callers must not
        // change the result: it is kept.
        public static float[] Map(Img im, out int mw, out int mh)
        {
            mw = 0; mh = 0;
            if (!Available) return null;
            float[] print = Print(im);
            Kept hit = Find(print);
            if (hit == null)
                lock (runLk)
                {
                    hit = Find(print);
                    if (hit == null)
                    {
                        Working = "Working out the depth";
                        try { hit = Build(im, print); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { LastError = ex.Message; return null; }
                        finally { Working = ""; }
                        lock (kept) { kept.Add(hit); while (kept.Count > 3) kept.RemoveAt(0); }
                    }
                }
            mw = hit.W; mh = hit.H;
            return hit.M;
        }

        static Kept Build(Img im, float[] print)
        {
            int side = Side, w, h;
            if (im.W >= im.H) { w = Patches(side); h = Patches(side * im.H / (double)im.W); }
            else { h = Patches(side); w = Patches(side * im.W / (double)im.H); }
            Img r = Ops.Resize(im, w, h);
            int n = w * h;
            float[] x = new float[3 * n];
            // what the model was trained on: the ImageNet mean and deviation, one plane per colour
            float[] mean = { 0.485f, 0.456f, 0.406f }, sd = { 0.229f, 0.224f, 0.225f };
            float[] rd = r.D;
            Ops.For(0, h, yy =>
            {
                for (int xx = 0; xx < w; xx++)
                {
                    int i = yy * w + xx, j = i * 3;
                    x[i] = (rd[j] - mean[0]) / sd[0];
                    x[n + i] = (rd[j + 1] - mean[1]) / sd[1];
                    x[2 * n + i] = (rd[j + 2] - mean[2]) / sd[2];
                }
            });
            Pipeline.CheckCancel();
            float[] d = Session(side).Run("predicted_depth", new Tensor(x, 1, 3, h, w)).F;
            Pipeline.CheckCancel();
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < d.Length; i++) { if (d[i] < mn) mn = d[i]; if (d[i] > mx) mx = d[i]; }
            float k = 1f / (mx - mn > 1e-6f ? mx - mn : 1f);
            float[] m = new float[n];
            for (int i = 0; i < n; i++) m[i] = (d[i] - mn) * k;
            // the model's edges land near the real ones but not on them: the photo's own edges pull them into
            // place, the same filter the one-tap masks use
            float[] L = Ops.Luma(r);
            m = Ops.Guided(L, m, w, h, Math.Max(2, Math.Min(w, h) / 160), 0.0008f);
            Ops.Done(L);
            r.Recycle();
            return new Kept { Print = print, M = m, W = w, H = h };
        }

        // what the depth is at one spot, given in fractions of the picture
        public static float At(float[] map, int mw, int mh, double fx, double fy)
        {
            if (map == null || mw <= 0) return 0.5f;
            int x = (int)Math.Round(Ops.C01((float)fx) * (mw - 1)), y = (int)Math.Round(Ops.C01((float)fy) * (mh - 1));
            return map[y * mw + x];
        }
    }
}

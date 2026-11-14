using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // Finding faces with SCRFD, which Windows' own detector cannot do well enough: that one only sees faces
    // looking more or less at the camera, so a head turned away, tipped down or half behind a hand is simply
    // not there, and every tool that needs a face does nothing. SCRFD finds those, and hands back five points
    // with each face - the two eyes, the nose and the corners of the mouth - which is worth as much as the box
    // itself: when the 468-point mesh cannot fit a turned face, those five still say where its parts are.
    public static class FaceDetect
    {
        public static string Dir = "", RuntimePath = "", Device = "", LastError = "";
        static volatile bool tried, ok, warming;
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession session;

        public static string ModelPath { get { return Path.Combine(Dir, "scrfd_10g.onnx"); } }
        public static bool Installed { get { return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(ModelPath); } }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }
        public static void WarmUp()
        {
            if (!Installed || warming) return;
            warming = true;
            Task.Factory.StartNew(() => EnsureLoaded());
        }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the face detector is not installed");
                    Ort.Init(RuntimePath);
                    // the sizes fixed, so DirectML plans the whole graph once instead of working shapes out
                    // on the processor at every run
                    Dictionary<string, long> dims = new Dictionary<string, long>();
                    dims["height"] = Side; dims["width"] = Side;
                    session = OrtSession.Open(ModelPath, true, 1, dims);
                    Device = session.Device; LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // one face: where it is in the picture, and the five points, all in fractions of the picture
        public class Found
        {
            public float X, Y, W, H, Score;
            public float[] Pts;     // x,y of the right eye, left eye, nose, right mouth corner, left mouth corner
        }

        // how sure it has to be, and how much two boxes may overlap before they count as the same face
        public static float MinScore = 0.5f, MaxOverlap = 0.4f;
        public static int Side = 640;          // the long side given to the model, a whole number of 32s

        class Kept { public float[] Print; public int W, H; public List<Found> F; }
        static readonly List<Kept> kept = new List<Kept>();
        public static void Forget() { lock (kept) kept.Clear(); }

        public static List<Found> Detect(Img im)
        {
            if (!Available) return null;
            float[] print = Print(im);
            lock (kept)
                foreach (Kept k in kept)
                {
                    if (k.W != im.W || k.H != im.H) continue;
                    bool same = true;
                    for (int i = 0; i < print.Length && same; i++) if (Math.Abs(k.Print[i] - print[i]) > 0.004f) same = false;
                    if (same) return k.F;
                }
            List<Found> f;
            lock (runLk)
            {
                try { f = Run(im); }
                catch (Exception ex) { LastError = ex.Message; return null; }
            }
            lock (kept) { kept.Add(new Kept { Print = print, W = im.W, H = im.H, F = f }); while (kept.Count > 3) kept.RemoveAt(0); }
            return f;
        }

        static int Up32(int v) { return ((v + 31) / 32) * 32; }

        static List<Found> Run(Img im)
        {
            // the picture inside a square the model's size, top left, the rest left black: the scale back out
            // is then one number, and nothing is squashed
            // Always the same square, whatever shape the photo is. The model will take any size that divides
            // by 32, but then every photo shape is a different set of shapes inside it, and DirectML fails on
            // the reshapes partway through. One fixed square costs a little wasted black and nothing else.
            float scale = Side / (float)Math.Max(im.W, im.H);
            int rw = Math.Max(1, (int)Math.Round(im.W * scale)), rh = Math.Max(1, (int)Math.Round(im.H * scale));
            int tw = Side, th = Side;
            Img r = Ops.Resize(im, rw, rh);
            int n = tw * th;
            float[] x = new float[3 * n];
            // what it was trained on: the colours about zero, a hundred and twenty-eight to a unit
            for (int i = 0; i < 3 * n; i++) x[i] = -127.5f / 128f;      // the black border, in the same terms
            float[] rd = r.D;
            Ops.For(0, rh, yy =>
            {
                for (int xx = 0; xx < rw; xx++)
                {
                    int src = (yy * rw + xx) * 3, dst = yy * tw + xx;
                    x[dst] = (rd[src] * 255f - 127.5f) / 128f;
                    x[n + dst] = (rd[src + 1] * 255f - 127.5f) / 128f;
                    x[2 * n + dst] = (rd[src + 2] * 255f - 127.5f) / 128f;
                }
            });
            r.Recycle();

            Tensor t = new Tensor(x, 1, 3, th, tw);
            // nine ways out, in threes: how sure, where the edges are, and where the five points are, each for
            // a coarser grid than the last
            string[] names = session.OutputNames;
            float[][] outs = new float[9][];
            for (int i = 0; i < 9; i++) outs[i] = session.Run(names[i], t).F;

            List<Found> all = new List<Found>();
            int[] strides = { 8, 16, 32 };
            for (int k = 0; k < 3; k++)
            {
                int s = strides[k];
                int gw = tw / s, gh = th / s;
                float[] score = outs[k], box = outs[k + 3], kps = outs[k + 6];
                int perCell = 2;                       // two guesses at every place on the grid
                for (int gy = 0; gy < gh; gy++)
                    for (int gx = 0; gx < gw; gx++)
                        for (int a = 0; a < perCell; a++)
                        {
                            int idx = (gy * gw + gx) * perCell + a;
                            if (idx >= score.Length || score[idx] < MinScore) continue;
                            float cx = gx * s, cy = gy * s;
                            float x1 = cx - box[idx * 4] * s, y1 = cy - box[idx * 4 + 1] * s;
                            float x2 = cx + box[idx * 4 + 2] * s, y2 = cy + box[idx * 4 + 3] * s;
                            Found f = new Found { Score = score[idx] };
                            f.X = x1 / scale / im.W; f.Y = y1 / scale / im.H;
                            f.W = (x2 - x1) / scale / im.W; f.H = (y2 - y1) / scale / im.H;
                            if (kps != null && (idx * 10 + 9) < kps.Length)
                            {
                                f.Pts = new float[10];
                                for (int q = 0; q < 5; q++)
                                {
                                    f.Pts[q * 2] = (cx + kps[idx * 10 + q * 2] * s) / scale / im.W;
                                    f.Pts[q * 2 + 1] = (cy + kps[idx * 10 + q * 2 + 1] * s) / scale / im.H;
                                }
                            }
                            all.Add(f);
                        }
            }
            return Trim(all);
        }

        // the same face found several times over: the surest one is kept and the ones sitting on top of it go
        static List<Found> Trim(List<Found> all)
        {
            all.Sort((p, q) => q.Score.CompareTo(p.Score));
            List<Found> keep = new List<Found>();
            foreach (Found f in all)
            {
                bool covered = false;
                foreach (Found g in keep)
                {
                    float x1 = Math.Max(f.X, g.X), y1 = Math.Max(f.Y, g.Y);
                    float x2 = Math.Min(f.X + f.W, g.X + g.W), y2 = Math.Min(f.Y + f.H, g.Y + g.H);
                    float over = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
                    float both = f.W * f.H + g.W * g.H - over;
                    if (both > 1e-9f && over / both > MaxOverlap) { covered = true; break; }
                }
                if (!covered) keep.Add(f);
                if (keep.Count >= 12) break;
            }
            return keep;
        }

        // the faces as the rest of PixTune writes them down: "x,y,w,h;x,y,w,h"
        public static string AsText(List<Found> f)
        {
            if (f == null || f.Count == 0) return "";
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            List<string> parts = new List<string>();
            foreach (Found g in f)
                parts.Add(string.Format(inv, "{0},{1},{2},{3}", Ops.C01(g.X), Ops.C01(g.Y), Ops.C01(g.W), Ops.C01(g.H)));
            return string.Join(";", parts.ToArray());
        }

        static float[] Print(Img im)
        {
            float[] p = new float[48];
            int[] cnt = new int[16];
            for (int y = 0; y < im.H; y++)
                for (int x = 0; x < im.W; x++)
                {
                    int cell = (y * 4 / im.H) * 4 + x * 4 / im.W, i = (y * im.W + x) * 3;
                    cnt[cell]++;
                    for (int c = 0; c < 3; c++) p[cell * 3 + c] += im.D[i + c];
                }
            for (int i = 0; i < p.Length; i++) p[i] /= Math.Max(1, cnt[i / 3]);
            return p;
        }
    }
}

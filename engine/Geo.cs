using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace PixTune
{
    public static class Geo
    {
        // ---------------- Crop ----------------
        // Crop & Rotate: optional straighten / quarter turns / flips first, then the crop frame.
        // While the tool is open ("nocrop") only the rotation is applied, so the frame can be placed.
        public static Img Crop(Img src, Step s)
        {
            if (Math.Abs(s.F("angle")) > 0.01f || s.I("rot90", 0) != 0 || s.I("flipH", 0) != 0 || s.I("flipV", 0) != 0)
                src = Rotate(src, s);
            if (s.I("nocrop", 0) == 1) return src.Clone();
            float x0 = Ops.C01(s.F("x0", 0)), y0 = Ops.C01(s.F("y0", 0)), x1 = Ops.C01(s.F("x1", 1)), y1 = Ops.C01(s.F("y1", 1));
            if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
            int ix = (int)Math.Round(x0 * src.W), iy = (int)Math.Round(y0 * src.H);
            int iw = Math.Max(1, Math.Min(src.W - ix, (int)Math.Round((x1 - x0) * src.W)));
            int ih = Math.Max(1, Math.Min(src.H - iy, (int)Math.Round((y1 - y0) * src.H)));
            Img o = new Img(iw, ih);
            for (int y = 0; y < ih; y++) Array.Copy(src.D, ((iy + y) * src.W + ix) * 3, o.D, y * iw * 3, iw * 3);
            return o;
        }

        // ---------------- Rotate ----------------
        public static Img Rot90(Img src)
        {
            Img o = new Img(src.H, src.W);
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int sx = y, sy = src.H - 1 - x;
                    int j = (y * o.W + x) * 3, k = (sy * src.W + sx) * 3;
                    o.D[j] = src.D[k]; o.D[j + 1] = src.D[k + 1]; o.D[j + 2] = src.D[k + 2];
                }
            });
            return o;
        }

        // Turns a picture as an EXIF orientation says (1 as stored, 3 upside down, 6 a quarter turn clockwise, 8 a
        // quarter turn counter-clockwise; 2, 4, 5, 7 the mirrored ones).
        public static Img Orient(Img im, int o)
        {
            switch (o)
            {
                case 2: return Flip(im, true);
                case 3: return Rot90(Rot90(im));
                case 4: return Flip(im, false);
                case 5: return Rot90(Rot90(Rot90(Flip(im, true))));
                case 6: return Rot90(im);
                case 7: return Rot90(Flip(im, true));
                case 8: return Rot90(Rot90(Rot90(im)));
                default: return im;
            }
        }

        public static Img Flip(Img src, bool horiz)
        {
            Img o = new Img(src.W, src.H);
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int sx = horiz ? src.W - 1 - x : x, sy = horiz ? y : src.H - 1 - y;
                    int j = (y * o.W + x) * 3, k = (sy * src.W + sx) * 3;
                    o.D[j] = src.D[k]; o.D[j + 1] = src.D[k + 1]; o.D[j + 2] = src.D[k + 2];
                }
            });
            return o;
        }

        public static Img Rotate(Img src, Step s)
        {
            Img a = src;
            int k = ((s.I("rot90", 0) % 4) + 4) % 4;
            for (int i = 0; i < k; i++) a = Rot90(a);
            if (s.I("flipH", 0) == 1) a = Flip(a, true);
            if (s.I("flipV", 0) == 1) a = Flip(a, false);
            float ang = s.F("angle");
            if (Math.Abs(ang) < 0.01f) return a == src ? src.Clone() : a;
            double th = Math.Abs(ang) * Math.PI / 180;
            double sn = Math.Sin(th), cs = Math.Cos(th);
            double sc = Math.Min(a.H / (a.W * sn + a.H * cs), a.W / (a.W * cs + a.H * sn));
            int ow = Math.Max(1, (int)Math.Floor(a.W * sc)), oh = Math.Max(1, (int)Math.Floor(a.H * sc));
            Img o = new Img(ow, oh);
            double t = -ang * Math.PI / 180;
            float c = (float)Math.Cos(t), si = (float)Math.Sin(t);
            float acx = a.W / 2f, acy = a.H / 2f, ocx = ow / 2f, ocy = oh / 2f;
            Img src2 = a;
            Ops.For(0, oh, y =>
            {
                for (int x = 0; x < ow; x++)
                {
                    float dx = x + 0.5f - ocx, dy = y + 0.5f - ocy;
                    float sx = dx * c - dy * si + acx - 0.5f, sy = dx * si + dy * c + acy - 0.5f;
                    float r, g, b; src2.Sample(sx, sy, out r, out g, out b);
                    int j = (y * ow + x) * 3; o.D[j] = r; o.D[j + 1] = g; o.D[j + 2] = b;
                }
            });
            return o;
        }

        // Estimates the tilt of the horizon/verticals in degrees (the correction to apply).
        public static double AutoStraighten(Img im)
        {
            Img sm = im.FitInside(600);
            float[] L = Ops.Gauss(Ops.Luma(sm), sm.W, sm.H, 1.0f);
            int w = sm.W, h = sm.H;
            double[] hist = new double[161]; // -20..20 deg, 0.25 steps
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    float gx = (L[i + 1] - L[i - 1]) * 2 + L[i - w + 1] - L[i - w - 1] + L[i + w + 1] - L[i + w - 1];
                    float gy = (L[i + w] - L[i - w]) * 2 + L[i + w - 1] - L[i - w - 1] + L[i + w + 1] - L[i - w + 1];
                    float mag = gx * gx + gy * gy;
                    if (mag < 0.02f) continue;
                    double ang = Math.Atan2(gy, gx) * 180 / Math.PI + 90;
                    double dev = ((ang % 90) + 90) % 90;
                    if (dev > 45) dev -= 90;
                    if (Math.Abs(dev) > 20) continue;
                    int bin = (int)Math.Round((dev + 20) * 4);
                    hist[bin] += Math.Sqrt(mag);
                }
            int best = 80; double bv = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                double v = hist[i] + (i > 0 ? hist[i - 1] * 0.5 : 0) + (i < 160 ? hist[i + 1] * 0.5 : 0);
                if (v > bv) { bv = v; best = i; }
            }
            double tilt = best / 4.0 - 20;
            return Math.Round(-tilt, 2);
        }

        // ---------------- Perspective ----------------
        static double[] Solve(double[,] A, double[] b)
        {
            int n = b.Length;
            double[,] M = (double[,])A.Clone();
            double[] v = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int p = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r;
                for (int k = 0; k < n; k++) { double t = M[c, k]; M[c, k] = M[p, k]; M[p, k] = t; }
                { double t = v[c]; v[c] = v[p]; v[p] = t; }
                double d = M[c, c];
                if (Math.Abs(d) < 1e-12) d = 1e-12;
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = M[r, c] / d;
                    if (f == 0) continue;
                    for (int k = c; k < n; k++) M[r, k] -= f * M[c, k];
                    v[r] -= f * v[c];
                }
            }
            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = v[i] / M[i, i];
            return x;
        }

        // Homography mapping points a[i] -> b[i] (arrays of 4 x,y pairs).
        public static double[] Homography(double[] a, double[] b)
        {
            double[,] A = new double[8, 8];
            double[] B = new double[8];
            for (int i = 0; i < 4; i++)
            {
                double x = a[i * 2], y = a[i * 2 + 1], u = b[i * 2], v = b[i * 2 + 1];
                A[i * 2, 0] = x; A[i * 2, 1] = y; A[i * 2, 2] = 1; A[i * 2, 6] = -x * u; A[i * 2, 7] = -y * u; B[i * 2] = u;
                A[i * 2 + 1, 3] = x; A[i * 2 + 1, 4] = y; A[i * 2 + 1, 5] = 1; A[i * 2 + 1, 6] = -x * v; A[i * 2 + 1, 7] = -y * v; B[i * 2 + 1] = v;
            }
            double[] hh = Solve(A, B);
            return new double[] { hh[0], hh[1], hh[2], hh[3], hh[4], hh[5], hh[6], hh[7], 1 };
        }

        // Destination corners (normalized TL, TR, BR, BL) for the perspective parameters.
        public static double[] PerspectiveCorners(Step s)
        {
            double[] c = { 0, 0, 1, 0, 1, 1, 0, 1 };
            double tv = s.F("tiltv") / 100.0 * 0.22, th = s.F("tilth") / 100.0 * 0.22;
            if (tv > 0) { c[0] += tv; c[2] -= tv; } else { c[6] -= tv; c[4] += tv; }
            if (th > 0) { c[1] += th; c[7] -= th; } else { c[3] -= th; c[5] += th; }
            double ang = s.F("prot") * Math.PI / 180, sc = 1 + s.F("pscale") / 100.0 * 0.5;
            double ca = Math.Cos(ang), sa = Math.Sin(ang);
            for (int i = 0; i < 4; i++)
            {
                double x = c[i * 2] - 0.5, y = c[i * 2 + 1] - 0.5;
                c[i * 2] = 0.5 + (x * ca - y * sa) * sc;
                c[i * 2 + 1] = 0.5 + (x * sa + y * ca) * sc;
                c[i * 2] += s.F("c" + i + "x");
                c[i * 2 + 1] += s.F("c" + i + "y");
            }
            return c;
        }

        public static Img Perspective(Img src, Step s)
        {
            int w = src.W, h = src.H;
            double[] dn = PerspectiveCorners(s);
            double[] dst = new double[8], sp = { 0, 0, w, 0, w, h, 0, h };
            for (int i = 0; i < 4; i++) { dst[i * 2] = dn[i * 2] * w; dst[i * 2 + 1] = dn[i * 2 + 1] * h; }
            double[] Hm = Homography(dst, sp); // output pixel -> source pixel
            Img o = new Img(w, h);
            float[] valid = new float[w * h];
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double z = Hm[6] * px + Hm[7] * py + 1;
                    double sx = (Hm[0] * px + Hm[1] * py + Hm[2]) / z, sy = (Hm[3] * px + Hm[4] * py + Hm[5]) / z;
                    int j = (y * w + x) * 3;
                    if (sx >= 0 && sy >= 0 && sx <= w && sy <= h && z > 0)
                    {
                        float r, g, b; src.Sample((float)sx - 0.5f, (float)sy - 0.5f, out r, out g, out b);
                        o.D[j] = r; o.D[j + 1] = g; o.D[j + 2] = b; valid[y * w + x] = 1;
                    }
                }
            });
            return Fill(o, valid, s.I("fill", 0));
        }

        public static Img Fill(Img o, float[] valid, int mode)
        {
            if (mode == 1 || mode == 2)
            {
                float v = mode == 1 ? 1f : 0f;
                for (int i = 0; i < valid.Length; i++) if (valid[i] < 0.5f) { o.D[i * 3] = v; o.D[i * 3 + 1] = v; o.D[i * 3 + 2] = v; }
                return o;
            }
            Img pp = Ops.PushPull(o, valid);
            // add a little texture so the filled area is not flat
            int w = o.W;
            float sc = o.Scale;
            for (int i = 0; i < valid.Length; i++)
            {
                if (valid[i] >= 0.5f) continue;
                int x = i % w, y = i / w;
                float n = (Ops.Fbm(x / (6f * sc), y / (6f * sc), 77, 3) - 0.5f) * 0.05f;
                o.D[i * 3] = pp.D[i * 3] + n; o.D[i * 3 + 1] = pp.D[i * 3 + 1] + n; o.D[i * 3 + 2] = pp.D[i * 3 + 2] + n;
            }
            return o;
        }

        // ---------------- Expand ----------------
        static int Reflect(int v, int n)
        {
            if (n == 1) return 0;
            int p = 2 * (n - 1);
            v = ((v % p) + p) % p;
            return v < n ? v : p - v;
        }

        public static Img Expand(Img src, Step s)
        {
            // each side in percent of the photo's width or height, at most a quarter (generative fill stays
            // convincing when it continues a large part of the photo; the tool also allows one side at a time)
            Func<string, int, int> side = (k, dim) => (int)(Math.Max(0f, Math.Min(MaxExpand, s.F(k))) / 100f * dim);
            int l = side("left", src.W), r = side("right", src.W), t = side("top", src.H), b = side("bottom", src.H);
            if (l + r + t + b == 0) return src.Clone();
            int mode = s.I("fill", 0);
            if (mode == 1 || mode == 2) { float[] valid; Img o = ExpandCanvas(src, l, t, r, b, out valid); return Fill(o, valid, mode); }
            // 0 smart: content-aware completion; 3: LaMa; 4: generative (Stable Diffusion); the AI modes fall
            // back to the next simpler engine when their files are missing
            Inpaint.Engine engine = mode == 4 ? Inpaint.Engine.Generative : (mode == 3 ? Inpaint.Engine.Lama : Inpaint.Engine.Patches);
            string key = engine == Inpaint.Engine.Generative
                ? string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}", s.F("left"), s.F("right"), s.F("top"), s.F("bottom"), s.I("seed", 0), s.Str("prompt", ""))
                : null;
            return ExpandSmart(src, l, t, r, b, engine, s.Str("prompt", ""), 2001 + s.I("seed", 0), key, s.I("hd", 1) == 1);
        }

        public const float MaxExpand = 25f;

        // Completion works on at most this many pixels: larger photos are filled at a reduced size and
        // only the new border is scaled up, keeping export time close to the preview's.
        const double ExpandWorkPixels = 3000000;
        // Generative fill always runs with the photo at this size, so the preview and the export are
        // given the same picture and the export reuses the border generated for the preview.
        const int GenerativeLongSide = 1536;

        class GenCache { public string Key; public float[] Print; public Img Canvas; }
        static readonly List<GenCache> genCache = new List<GenCache>();

        static Img ExpandSmart(Img src, int l, int t, int r, int b, Inpaint.Engine engine, string prompt, int seed, string key, bool hd)
        {
            float[] valid;
            Img o = ExpandCanvas(src, l, t, r, b, out valid);
            double k = engine == Inpaint.Engine.Generative
                ? Math.Min(1.0, (double)GenerativeLongSide / Math.Max(src.W, src.H))
                : Math.Min(1.0, Math.Sqrt(ExpandWorkPixels / o.N));
            if (k >= 0.999)
            {
                if (key == null) { Inpaint.FillOutside(o, valid, seed, engine, prompt); return o; }
                float[] print = Fingerprint(src);
                lock (genCache)
                    foreach (GenCache g in genCache)
                        if (g.Key == key && SamePrint(g.Print, print))
                            return g.Canvas.W == o.W && g.Canvas.H == o.H ? g.Canvas.Clone() : Ops.Resize(g.Canvas, o.W, o.H);
                Inpaint.FillOutside(o, valid, seed, engine, prompt);
                lock (genCache)
                {
                    genCache.Add(new GenCache { Key = key, Print = print, Canvas = o.Clone() });
                    if (genCache.Count > 6) genCache.RemoveAt(0);
                }
                return o;
            }
            Func<int, int> sc = v => (int)Math.Round(v * k);
            Img srcSmall = Ops.Resize(src, Math.Max(1, sc(src.W)), Math.Max(1, sc(src.H)));
            Img small = ExpandSmart(srcSmall, sc(l), sc(t), sc(r), sc(b), engine, prompt, seed, key, false);
            // the new border is scaled up; along the seam the fill also retouched the photo's edge (blending),
            // which is carried over as a difference so the photo keeps its full-resolution detail
            float[] smallValid;
            Img before = ExpandCanvas(srcSmall, sc(l), sc(t), sc(r), sc(b), out smallValid);
            Img delta = new Img(small.W, small.H);
            for (int i = 0; i < small.N; i++)
                if (smallValid[i] >= 0.5f) for (int c = 0; c < 3; c++) delta.D[i * 3 + c] = small.D[i * 3 + c] - before.D[i * 3 + c];
            Img up = Ops.Resize(small, o.W, o.H), dUp = Ops.Resize(delta, o.W, o.H);
            Img photo = hd && key != null && 1 / k >= HighDetailFrom ? o.Clone() : null;
            for (int i = 0; i < o.N; i++)
                for (int c = 0; c < 3; c++)
                    o.D[i * 3 + c] = valid[i] < 0.5f ? up.D[i * 3 + c] : o.D[i * 3 + c] + dUp.D[i * 3 + c];
            if (photo != null && Diffusion.Available) AddHighDetail(o, photo, valid, small, smallValid, src, key, prompt, seed);
            return o;
        }

        // High detail starts when the result is shown or exported this much larger than it was generated
        const float HighDetailFrom = 1.5f;
        class HdCache { public string Key; public float[] Print; public int W, H, X, Y; public Img Strip; }
        static readonly List<HdCache> hdCache = new List<HdCache>();

        // Replaces the scaled-up new border by an AI-upscaled, refined and grain-matched one (HighDetail).
        static void AddHighDetail(Img o, Img photo, float[] valid, Img small, float[] smallValid, Img src, string key, string prompt, int seed)
        {
            // the new area plus a margin of photo, in the generated picture and at output size
            int sx0 = small.W, sy0 = small.H, sx1 = -1, sy1 = -1;
            for (int y = 0; y < small.H; y++)
                for (int x = 0; x < small.W; x++)
                    if (smallValid[y * small.W + x] < 0.5f) { if (x < sx0) sx0 = x; if (x > sx1) sx1 = x; if (y < sy0) sy0 = y; if (y > sy1) sy1 = y; }
            if (sx1 < 0) return;
            const int margin = 48;
            sx0 = Math.Max(0, sx0 - margin); sy0 = Math.Max(0, sy0 - margin);
            sx1 = Math.Min(small.W - 1, sx1 + margin); sy1 = Math.Min(small.H - 1, sy1 + margin);
            float rx = (float)o.W / small.W, ry = (float)o.H / small.H;
            int fx0 = (int)(sx0 * rx), fy0 = (int)(sy0 * ry);
            int fx1 = Math.Min(o.W, (int)Math.Ceiling((sx1 + 1) * rx)), fy1 = Math.Min(o.H, (int)Math.Ceiling((sy1 + 1) * ry));
            int fw = fx1 - fx0, fh = fy1 - fy0;
            float[] print = Fingerprint(src);
            Img strip = null;
            lock (hdCache)
                foreach (HdCache c in hdCache)
                    if (c.Key == key && c.W == o.W && c.H == o.H && SamePrint(c.Print, print)) { strip = c.Strip; break; }
            bool[] isNew = new bool[fw * fh];
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++) isNew[y * fw + x] = valid[(fy0 + y) * o.W + fx0 + x] < 0.5f;
            if (strip == null)
            {
                // fine texture is taken from the real photo next to the new area, up to twice its thickness away
                int vx0 = o.W, vy0 = o.H, vx1 = -1, vy1 = -1, mx0 = o.W, my0 = o.H, mx1 = -1, my1 = -1;
                for (int y = 0; y < o.H; y++)
                    for (int x = 0; x < o.W; x++)
                    {
                        if (valid[y * o.W + x] >= 0.5f) { if (x < vx0) vx0 = x; if (x > vx1) vx1 = x; if (y < vy0) vy0 = y; if (y > vy1) vy1 = y; }
                        else { if (x < mx0) mx0 = x; if (x > mx1) mx1 = x; if (y < my0) my0 = y; if (y > my1) my1 = y; }
                    }
                int reach = 2 * Math.Min(mx1 - mx0 + 1, my1 - my0 + 1);
                int qx0 = Math.Max(vx0, mx0 - reach), qy0 = Math.Max(vy0, my0 - reach);
                int qx1 = Math.Min(vx1, mx1 + reach), qy1 = Math.Min(vy1, my1 + reach);
                Img source = CropImg(photo, qx0, qy0, qx1 - qx0 + 1, qy1 - qy0 + 1);
                strip = HighDetail.Enhance(CropImg(small, sx0, sy0, sx1 - sx0 + 1, sy1 - sy0 + 1), CropImg(photo, fx0, fy0, fw, fh), isNew, source, seed);
                lock (hdCache)
                {
                    hdCache.Add(new HdCache { Key = key, Print = print, W = o.W, H = o.H, X = fx0, Y = fy0, Strip = strip });
                    if (hdCache.Count > 2) hdCache.RemoveAt(0);
                }
            }
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    if (!isNew[y * fw + x]) continue;
                    int gi = ((fy0 + y) * o.W + fx0 + x) * 3, si = (y * fw + x) * 3;
                    o.D[gi] = strip.D[si]; o.D[gi + 1] = strip.D[si + 1]; o.D[gi + 2] = strip.D[si + 2];
                }
        }

        static Img CropImg(Img im, int x0, int y0, int w, int h)
        {
            Img c = new Img(w, h);
            for (int y = 0; y < h; y++) Array.Copy(im.D, ((y0 + y) * im.W + x0) * 3, c.D, y * w * 3, w * 3);
            return c;
        }

        // 8x8 average colours: recognises the same photo whether it came from the preview or the original.
        static float[] Fingerprint(Img im)
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

        static bool SamePrint(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 0.015f) return false;
            return true;
        }

        // The photo on its larger canvas, the border pre-filled by mirroring; valid marks the photo's pixels.
        static Img ExpandCanvas(Img src, int l, int t, int r, int b, out float[] valid)
        {
            int ow = src.W + l + r, oh = src.H + t + b;
            Img o = new Img(ow, oh);
            float[] v = new float[ow * oh];
            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    int sx = x - l, sy = y - t;
                    int j = (y * ow + x) * 3;
                    bool inside = sx >= 0 && sy >= 0 && sx < src.W && sy < src.H;
                    if (inside) v[y * ow + x] = 1;
                    int k = (Reflect(sx, src.W) + Reflect(sy, src.H) * src.W) * 3;
                    o.D[j] = src.D[k]; o.D[j + 1] = src.D[k + 1]; o.D[j + 2] = src.D[k + 2];
                }
            valid = v;
            return o;
        }

        // ---------------- Healing ----------------
        // Healed results of the last strokes, so adding a stroke only fills that stroke.
        // Keyed by the input image object (the preview cache hands the same object back while the
        // edits before Healing are unchanged) and by the strokes so far.
        class HealCache { public Img Src; public string Key; public Img Out; }
        static readonly List<HealCache> healCache = new List<HealCache>();
        public static void Forget()
        {
            lock (genCache) genCache.Clear();
            lock (hdCache) hdCache.Clear();
            lock (healCache) healCache.Clear();
        }

        public static Img Healing(Img src, Step s)
        {
            bool legacy = s.I("legacy", 0) == 1; // single-patch method, kept only for comparisons
            bool cacheable = !legacy && src.N <= 4000000;
            // longest stroke prefix already healed for this input
            List<string> keys = new List<string>();
            string key = "";
            foreach (Stroke st in s.Strokes)
            {
                key += "|" + st.Ch + st.V + "/" + st.R.ToString("R") + ":" + st.Pts.Count;
                foreach (System.Drawing.PointF p in st.Pts) key +="," + p.X.ToString("R") + "," + p.Y.ToString("R");
                keys.Add(key);
            }
            Img cur = null; int start = 0;
            if (cacheable)
                lock (healCache)
                    for (int k = keys.Count - 1; k >= 0 && cur == null; k--)
                        foreach (HealCache c in healCache)
                            if (ReferenceEquals(c.Src, src) && c.Key == keys[k]) { cur = c.Out.Clone(); start = k + 1; break; }
            if (cur == null) cur = src.Clone();
            for (int k = start; k < s.Strokes.Count; k++)
            {
                Stroke st = s.Strokes[k];
                if (st.Pts.Count == 0) continue;
                Stroke big = st.Clone(); big.R = st.R * 1.2f; big.Hard = 0.55f;
                float[] cov = Raster.Coverage(big, cur.W, cur.H);
                if (legacy) HealOne(cur, cov);
                else Inpaint.Fill(cur, cov, 1001 + k, st.V >= 2); // stroke value 2 = painted in AI mode
            }
            if (cacheable && keys.Count > 0)
                lock (healCache)
                {
                    HealCache c = new HealCache(); c.Src = src; c.Key = keys[keys.Count - 1]; c.Out = cur.Clone();
                    healCache.Add(c);
                    while (healCache.Count > 3) healCache.RemoveAt(0);
                }
            return cur;
        }

        static void HealOne(Img im, float[] cov)
        {
            int w = im.W, h = im.H;
            int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (cov[y * w + x] > 0.01f) { if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (y < by0) by0 = y; if (y > by1) by1 = y; }
            if (bx1 < 0) return;
            int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
            int mg = Math.Max(4, (int)(0.3f * Math.Max(bw, bh)));
            int ex0 = Math.Max(0, bx0 - mg), ey0 = Math.Max(0, by0 - mg), ex1 = Math.Min(w - 1, bx1 + mg), ey1 = Math.Min(h - 1, by1 + mg);
            int ew = ex1 - ex0 + 1, eh = ey1 - ey0 + 1;
            // ring samples: known pixels around the hole
            List<int> ring = new List<int>();
            for (int y = ey0; y <= ey1; y++)
                for (int x = ex0; x <= ex1; x++)
                    if (cov[y * w + x] <= 0.01f) ring.Add(y * w + x);
            if (ring.Count == 0) return;
            int stride = Math.Max(1, ring.Count / 700);
            List<int> rs = new List<int>();
            for (int i = 0; i < ring.Count; i += stride) rs.Add(ring[i]);
            int[] RS = rs.ToArray();
            int R = (int)(Math.Max(bw, bh) * 2.5f) + 12;
            Func<int, int, double> cost = (dx, dy) =>
            {
                if (ex0 + dx < 0 || ey0 + dy < 0 || ex1 + dx >= w || ey1 + dy >= h) return double.MaxValue;
                if (Math.Abs(dx) < bw + mg && Math.Abs(dy) < bh + mg) return double.MaxValue;
                double e = 0;
                for (int k = 0; k < RS.Length; k++)
                {
                    int p = RS[k], q = p + dy * w + dx;
                    float d0 = im.D[p * 3] - im.D[q * 3], d1 = im.D[p * 3 + 1] - im.D[q * 3 + 1], d2 = im.D[p * 3 + 2] - im.D[q * 3 + 2];
                    e += d0 * d0 + d1 * d1 + d2 * d2;
                }
                // mild preference for nearby sources
                return e / RS.Length + (dx * dx + dy * dy) * 1e-7;
            };
            int step = Math.Max(1, Math.Max(bw, bh) / 5);
            double best = double.MaxValue; int bdx = 0, bdy = 0;
            object lk = new object();
            Ops.For(0, (2 * R) / step + 1, iy =>
            {
                int dy = -R + iy * step;
                for (int dx = -R; dx <= R; dx += step)
                {
                    double c = cost(dx, dy);
                    if (c < best) lock (lk) { if (c < best) { best = c; bdx = dx; bdy = dy; } }
                }
            });
            if (best == double.MaxValue)
            {
                // no clean source around: plain smooth fill
                bdx = 0; bdy = 0;
            }
            else
            {
                while (step > 1)
                {
                    step = Math.Max(1, step / 2);
                    int cx = bdx, cy = bdy;
                    for (int dy = -step; dy <= step; dy += step)
                        for (int dx = -step; dx <= step; dx += step)
                        {
                            double c = cost(cx + dx, cy + dy);
                            if (c < best) { best = c; bdx = cx + dx; bdy = cy + dy; }
                        }
                }
            }
            // local correction: difference between target and source on the ring, smoothly interpolated inside
            Img diff = new Img(ew, eh);
            float[] wt = new float[ew * eh];
            for (int y = 0; y < eh; y++)
                for (int x = 0; x < ew; x++)
                {
                    int p = (y + ey0) * w + (x + ex0);
                    if (cov[p] > 0.01f) continue;
                    int q = p + bdy * w + bdx;
                    int j = (y * ew + x) * 3;
                    if (bdx == 0 && bdy == 0) { diff.D[j] = im.D[p * 3]; diff.D[j + 1] = im.D[p * 3 + 1]; diff.D[j + 2] = im.D[p * 3 + 2]; }
                    else { diff.D[j] = im.D[p * 3] - im.D[q * 3]; diff.D[j + 1] = im.D[p * 3 + 1] - im.D[q * 3 + 1]; diff.D[j + 2] = im.D[p * 3 + 2] - im.D[q * 3 + 2]; }
                    wt[y * ew + x] = 1;
                }
            Img corr = Ops.PushPull(diff, wt);
            float[] outv = new float[ew * eh * 3];
            for (int y = 0; y < eh; y++)
                for (int x = 0; x < ew; x++)
                {
                    int p = (y + ey0) * w + (x + ex0);
                    int q = p + bdy * w + bdx;
                    int j = (y * ew + x) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = (bdx == 0 && bdy == 0) ? corr.D[j + c] : im.D[q * 3 + c] + corr.D[j + c];
                        outv[j + c] = v;
                    }
                }
            for (int y = 0; y < eh; y++)
                for (int x = 0; x < ew; x++)
                {
                    int p = (y + ey0) * w + (x + ex0);
                    float a = cov[p];
                    if (a <= 0) continue;
                    int j = (y * ew + x) * 3;
                    for (int c = 0; c < 3; c++) im.D[p * 3 + c] = Ops.Lerp(im.D[p * 3 + c], outv[j + c], a);
                }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace PixTune
{
    public static class Adjust
    {
        static float Clip(float v, float lim) { return v / (1 + Math.Abs(v) / lim); }

        // ---------------- Tune Image ----------------
        // auto = 1 adds the automatic correction measured on the step's input (as the looks do), the sliders act on top.
        public static Img Tune(Img src, Step s)
        {
            double[] a = s.I("auto", 0) == 1 ? AutoTune(src) : new double[5];
            return ToneCore(src, (float)(s.F("brightness") + a[0]) / 100f, s.F("ambiance") / 100f, (float)(s.F("contrast") + a[1]) / 100f,
                (float)(s.F("saturation") + a[2]) / 100f, (float)(s.F("shadows") + a[3]) / 100f, (float)(s.F("highlights") + a[4]) / 100f,
                s.F("warmth") / 100f, 0f, s.F("vibrance") / 100f);
        }

        public static Img ToneCore(Img src, float br, float am, float co, float sa, float sh, float hi, float wa, float expo)
        {
            return ToneCore(src, br, am, co, sa, sh, hi, wa, expo, 0f);
        }

        // vi: vibrance. Saturation moves every colour by as much, so the colours that already had plenty are the
        // first to go to poster paint, and a face turns orange long before a flat sky comes to life. Vibrance
        // weighs each pixel by how little colour it has to begin with, and holds back on skin.
        public static Img ToneCore(Img src, float br, float am, float co, float sa, float sh, float hi, float wa, float expo, float vi)
        {
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB s0 = j.Source, lb = null;
                if (sh != 0 || hi != 0 || am != 0)
                {
                    Gpu.GB L = j.Luma(s0);
                    int rr = Math.Max(2, (int)(14 * src.Scale));
                    lb = j.Keep("/tonebase:" + rr, L, () => j.K("Mix2", 1, new float[] { 0.65f, 0.35f }, null, j.Guided(L, rr, 0.012f), j.FastGauss(L, 40 * src.Scale)));
                }
                float gm = (float)Math.Pow(2, -br * 1.15), ekk = (float)Math.Pow(2, expo);
                return j.K("ToneCore", 3, new float[] { br, am, co, sa, sh, hi, wa, expo, gm, ekk, vi }, new int[] { lb != null ? 1 : 0 }, s0, lb);
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            int w = o.W, h = o.H;
            float[] Lb = null, lum = null;
            if (sh != 0 || hi != 0 || am != 0)
            {
                lum = Ops.Luma(src);
                int r = Math.Max(2, (int)(14 * src.Scale));
                // the local brightness the shadows, highlights and ambiance read: no slider changes it, so it is
                // kept for a stable input (Memo)
                Func<float[]> make = () =>
                {
                    float[] g = Ops.Guided(lum, lum, w, h, r, 0.012f);
                    float[] big = Ops.FastGauss(lum, w, h, 40 * src.Scale);
                    float[] lb = Pool.Rent(lum.Length);
                    for (int i = 0; i < lum.Length; i++) lb[i] = 0.65f * g[i] + 0.35f * big[i];
                    Ops.Done(g, big);
                    return lb;
                };
                Memo.Info li = Memo.InfoOf(lum);
                Lb = li != null ? Memo.Get(li.Key + "/tonebase:" + r + ":" + (40 * src.Scale).ToString("R"), li, make) : make();
            }
            float gamma = (float)Math.Pow(2, -br * 1.15);
            float[] gt = Ops.PowTable(gamma);
            float ek = (float)Math.Pow(2, expo);
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float r = D[j] * ek, g = D[j + 1] * ek, b = D[j + 2] * ek;
                    float l = Ops.Lum(r, g, b);
                    float t = l;
                    if (expo > 0 && t > 0.8f) { t = 0.8f + Clip(t - 0.8f, 0.2f); }
                    if (Lb != null)
                    {
                        float lb = Lb[i] * (expo != 0 ? ek : 1f);
                        if (sh != 0)
                        {
                            float ws = Ops.SmoothStep(0.7f, 0.0f, lb);
                            t += sh * 0.42f * ws * (sh > 0 ? (1 - t) * (0.35f + 0.65f * (1 - t)) : t);
                        }
                        if (hi != 0)
                        {
                            float wh = Ops.SmoothStep(0.3f, 1.0f, lb);
                            t += hi * 0.38f * wh * (hi > 0 ? (1 - t) : t);
                        }
                        if (am != 0)
                        {
                            float bal = (0.5f - lb);
                            t += am * 0.32f * bal * (1 - Math.Abs(2 * t - 1) * 0.6f);
                            t += am * 0.6f * (l - lb) * 0.5f;
                        }
                    }
                    if (br != 0) t = Ops.PowAt(gt, t, gamma);
                    if (co != 0) t = Ops.Contrast(t, co);
                    if (t < 0) t = 0;
                    Ops.SetLum(ref r, ref g, ref b, l, t);
                    float f = 1 + sa + am * 0.35f;
                    if (f != 1)
                    {
                        float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                        float cs = mx > 1e-4f ? (mx - mn) / mx : 0;
                        float ff = f > 1 ? 1 + (f - 1) * (1 - 0.55f * cs) : f;
                        Ops.Saturate(ref r, ref g, ref b, ff);
                    }
                    if (vi != 0)
                    {
                        float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                        float cs = mx > 1e-4f ? (mx - mn) / mx : 0;
                        // what already has colour hardly moves; what has none moves most
                        float wgt = (1 - cs) * (1 - cs);
                        // skin is the first thing a colour slider ruins, so it is held back: orange hues, where
                        // green sits about half way between blue and red, and only where there is light to see
                        if (r > g && g > b && mx > 0.15f)
                        {
                            float t2 = r - b > 1e-4f ? (g - b) / (r - b) : 0f;
                            wgt *= 1 - 0.7f * (1 - Math.Min(1f, Math.Abs(t2 - 0.5f) / 0.3f));
                        }
                        Ops.Saturate(ref r, ref g, ref b, 1 + vi * wgt);
                    }
                    if (wa != 0)
                    {
                        float l0 = Ops.Lum(r, g, b);
                        r += wa * 0.10f * (0.4f + l0); g += wa * 0.018f; b -= wa * 0.11f * (0.4f + l0);
                        float l1 = Ops.Lum(r, g, b);
                        float d = l0 - l1; r += d; g += d; b += d;
                    }
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            Ops.Done(lum, Lb);
            return o;
        }

        // Returns brightness, contrast, saturation, shadows, highlights for an automatic correction.
        public static double[] AutoTune(Img im)
        {
            int[] hst = ImageIO.Histogram(im, -1);
            long tot = 0; foreach (int c in hst) tot += c;
            Func<double, float> pct = q =>
            {
                long acc = 0, lim = (long)(tot * q);
                for (int i = 0; i < 256; i++) { acc += hst[i]; if (acc >= lim) return i / 255f; }
                return 1f;
            };
            float p2 = pct(0.02), p50 = pct(0.5), p98 = pct(0.98);
            double bright = Math.Max(-60, Math.Min(60, (0.47 - p50) * 160));
            double contrast = Math.Max(-30, Math.Min(45, (0.88 - (p98 - p2)) * 90));
            double shadows = p2 < 0.04 ? 25 : (p2 < 0.1 ? 12 : 0);
            double highlights = p98 > 0.97 ? -30 : (p98 > 0.92 ? -12 : 0);
            return new double[] { Math.Round(bright), Math.Round(contrast), 12, shadows, highlights };
        }

        // ---------------- Details ----------------
        public static Img Details(Img src, Step s)
        {
            return DetailsCore(src, s.F("structure") / 100f, s.F("sharpening") / 100f);
        }

        public static Img DetailsCore(Img src, float st, float sh)
        {
            if (st == 0 && sh == 0) return src.Clone();
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB s0 = j.Source, gl = j.Luma(s0), g1 = null, g2 = null, gs = null;
                if (st != 0)
                {
                    g1 = j.Guided(gl, Math.Max(2, (int)(12 * src.Scale)), 0.006f);
                    g2 = j.Guided(gl, Math.Max(1, (int)(3 * src.Scale)), 0.004f);
                }
                if (sh != 0) gs = j.Gauss(gl, Math.Max(0.7f, 1.1f * src.Scale));
                return j.K("Details", 3, new float[] { st, sh }, new int[] { st != 0 ? 1 : 0, sh != 0 ? 1 : 0 }, s0, gl, g1 ?? gl, g2 ?? gl, gs ?? gl);
            });
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            float[] b1 = null, b2 = null, bs = null;
            if (st != 0)
            {
                b1 = Ops.Guided(L, L, w, h, Math.Max(2, (int)(12 * src.Scale)), 0.006f);
                b2 = Ops.Guided(L, L, w, h, Math.Max(1, (int)(3 * src.Scale)), 0.004f);
            }
            if (sh != 0) bs = Ops.Gauss(L, w, h, Math.Max(0.7f, 1.1f * src.Scale));
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float l = L[i], t = l;
                    if (st > 0)
                    {
                        float d = (l - b1[i]) * 0.7f + (l - b2[i]) * 0.5f;
                        t += Clip(d * st * 2.6f, 0.25f);
                    }
                    else if (st < 0)
                    {
                        t = l + (b1[i] - l) * (-st);
                    }
                    if (bs != null) t += Clip((l - bs[i]) * sh * 2.4f, 0.2f);
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float dd = t - l;
                    D[j] = r + dd; D[j + 1] = g + dd; D[j + 2] = b + dd;
                }
            });
            Ops.Done(L, b1, b2, bs);
            return o;
        }

        // ---------------- Curves ----------------
        public static List<float[]> ParsePoints(string s)
        {
            List<float[]> r = new List<float[]>();
            if (string.IsNullOrEmpty(s)) return r;
            foreach (string p in s.Split(';'))
            {
                string[] a = p.Split(',');
                if (a.Length < 2) continue;
                float x, y;
                if (float.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    r.Add(new float[] { Ops.C01(x), Ops.C01(y) });
            }
            r.Sort((u, v) => u[0].CompareTo(v[0]));
            return r;
        }

        public static bool IsIdentity(string s)
        {
            List<float[]> p = ParsePoints(s);
            foreach (float[] q in p) if (Math.Abs(q[0] - q[1]) > 0.002f) return false;
            return true;
        }

        // Monotone cubic interpolation (Fritsch-Carlson).
        public static float[] Lut(string pts, int n)
        {
            List<float[]> p = ParsePoints(pts);
            if (p.Count == 0 || p[0][0] > 0.0001f) p.Insert(0, new float[] { 0, p.Count == 0 ? 0 : p[0][1] * 0 });
            if (p[p.Count - 1][0] < 0.9999f) p.Add(new float[] { 1, p.Count == 1 ? 1 : 1 });
            int k = p.Count;
            float[] lut = new float[n];
            if (k == 2)
            {
                for (int i = 0; i < n; i++)
                {
                    float x = i / (float)(n - 1);
                    float t = (x - p[0][0]) / Math.Max(1e-6f, p[1][0] - p[0][0]);
                    lut[i] = Ops.C01(p[0][1] + (p[1][1] - p[0][1]) * Ops.C01(t));
                }
                return lut;
            }
            float[] dx = new float[k - 1], dy = new float[k - 1], m = new float[k - 1];
            for (int i = 0; i < k - 1; i++) { dx[i] = Math.Max(1e-6f, p[i + 1][0] - p[i][0]); dy[i] = p[i + 1][1] - p[i][1]; m[i] = dy[i] / dx[i]; }
            float[] c1 = new float[k];
            c1[0] = m[0]; c1[k - 1] = m[k - 2];
            for (int i = 1; i < k - 1; i++)
            {
                if (m[i - 1] * m[i] <= 0) c1[i] = 0;
                else { float common = dx[i - 1] + dx[i]; c1[i] = 3 * common / ((common + dx[i]) / m[i - 1] + (common + dx[i - 1]) / m[i]); }
            }
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)(n - 1);
                int seg = 0;
                if (x <= p[0][0]) { lut[i] = p[0][1]; continue; }
                if (x >= p[k - 1][0]) { lut[i] = p[k - 1][1]; continue; }
                while (seg < k - 2 && x > p[seg + 1][0]) seg++;
                float hh = dx[seg], t = (x - p[seg][0]) / hh;
                float t2 = t * t, t3 = t2 * t;
                float h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
                lut[i] = Ops.C01(h00 * p[seg][1] + h10 * hh * c1[seg] + h01 * p[seg + 1][1] + h11 * hh * c1[seg + 1]);
            }
            return lut;
        }

        static float L(float[] lut, float v)
        {
            int n = lut.Length - 1;
            float f = Ops.C01(v) * n;
            int i = (int)f; if (i >= n) return lut[n];
            float a = f - i;
            return lut[i] * (1 - a) + lut[i + 1] * a;
        }

        public static Img Curves(Img src, Step s)
        {
            string[] keys = { "curve_rgb", "curve_r", "curve_g", "curve_b", "curve_lum" };
            float[][] luts = new float[5][];
            for (int k = 0; k < 5; k++)
            {
                string v = s.Str(keys[k], "");
                if (v.Length > 0 && !IsIdentity(v)) luts[k] = Lut(v, 1024);
            }
            Img gpu = Gpu.RunPointJob(src, j =>
            {
                float[] all = new float[5 * 1024];
                int used = 0;
                for (int k = 0; k < 5; k++) if (luts[k] != null) { Array.Copy(luts[k], 0, all, k * 1024, 1024); used |= 1 << k; }
                return j.K("Curves", 3, null, new int[] { used }, j.Source, j.Values(all));
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            int w = o.W;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    if (luts[0] != null) { r = L(luts[0], r); g = L(luts[0], g); b = L(luts[0], b); }
                    if (luts[1] != null) r = L(luts[1], r);
                    if (luts[2] != null) g = L(luts[2], g);
                    if (luts[3] != null) b = L(luts[3], b);
                    if (luts[4] != null)
                    {
                        float l = Ops.Lum(r, g, b);
                        Ops.SetLum(ref r, ref g, ref b, l, L(luts[4], l));
                    }
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            return o;
        }

        // ---------------- White balance ----------------
        static void WbGains(float t, float n, out float gr, out float gg, out float gb)
        {
            gr = 1 + 0.22f * t + 0.06f * n;
            gg = 1 - 0.12f * n;
            gb = 1 - 0.24f * t + 0.06f * n;
            float l = 0.299f * gr + 0.587f * gg + 0.114f * gb;
            gr /= l; gg /= l; gb /= l;
        }

        public static Img WbCore(Img src, float t, float n)
        {
            if (t == 0 && n == 0) return src.Clone();
            float gr, gg, gb;
            WbGains(t, n, out gr, out gg, out gb);
            Img gpu = Gpu.RunPointJob(src, j => j.K("Wb", 3, new float[] { gr, gg, gb }, null, j.Source));
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                int a = y * o.W * 3, e = a + o.W * 3;
                for (int j = a; j < e; j += 3) { D[j] *= gr; D[j + 1] *= gg; D[j + 2] *= gb; }
            });
            return o;
        }

        public static Img WhiteBalance(Img src, Step s) { return WbCore(src, s.F("temperature") / 100f, s.F("tint") / 100f); }

        // Temperature / tint (-100..100) that make the given colour neutral.
        public static double[] PickWB(float r, float g, float b)
        {
            double best = 1e9; double bt = 0, bn = 0;
            for (int ti = -100; ti <= 100; ti += 2)
                for (int ni = -100; ni <= 100; ni += 2)
                {
                    float gr, gg, gb;
                    WbGains(ti / 100f, ni / 100f, out gr, out gg, out gb);
                    float rr = r * gr, g2 = g * gg, bb = b * gb;
                    float m = (rr + g2 + bb) / 3;
                    double e = (rr - m) * (rr - m) + (g2 - m) * (g2 - m) + (bb - m) * (bb - m);
                    if (e < best) { best = e; bt = ti; bn = ni; }
                }
            return new double[] { bt, bn };
        }

        public static double[] AutoWB(Img im)
        {
            double sr = 0, sg = 0, sb = 0; int cnt = 0;
            int step = Math.Max(1, im.N / 200000);
            for (int i = 0; i < im.N; i += step)
            {
                float r = im.D[i * 3], g = im.D[i * 3 + 1], b = im.D[i * 3 + 2];
                float l = Ops.Lum(r, g, b);
                if (l < 0.08f || l > 0.95f) continue;
                float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                if (mx - mn > 0.35f) continue; // ignore strongly coloured pixels
                sr += r; sg += g; sb += b; cnt++;
            }
            if (cnt == 0) return new double[] { 0, 0 };
            double[] v = PickWB((float)(sr / cnt), (float)(sg / cnt), (float)(sb / cnt));
            v[0] = Math.Round(v[0] * 0.8); v[1] = Math.Round(v[1] * 0.8);
            return v;
        }

        // ---------------- Raw develop ----------------
        public static Img RawDevelop(Img src, Step s)
        {
            // AI denoise first, before exposure and shadows lift the noise (blend: strength 0..100)
            float dn = s.F("denoise") / 100f;
            if (dn > 0)
            {
                Img d = Denoiser.Denoise(src);
                if (d != null)
                {
                    Img m = src.Clone();
                    for (int i = 0; i < m.D.Length; i++) m.D[i] += (d.D[i] - m.D[i]) * dn;
                    src = m;
                }
            }
            Img a = WbCore(src, s.F("temperature") / 100f, s.F("tint") / 100f);
            a = ToneCore(a, 0, 0, s.F("contrast") / 100f, s.F("saturation") / 100f, s.F("shadows") / 100f,
                s.F("highlights") / 100f, 0, s.F("exposure") / 100f * 2f);
            float st = s.F("structure") / 100f;
            if (st != 0) a = DetailsCore(a, st, 0);
            return a;
        }

        // ---------------- Selective ----------------
        // A control point's zone is an ellipse: R across, RY along its height, turned by Ang degrees.
        // Older edits store 7 values (a circle).
        public class CtlPoint
        {
            public float X, Y, R = 0.15f, RY = 0.15f, Ang, Br, Co, Sa, St;
            public override string ToString()
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                return string.Join(",", new string[] { X.ToString("0.#####", c), Y.ToString("0.#####", c), R.ToString("0.####", c),
                    Br.ToString("0.##", c), Co.ToString("0.##", c), Sa.ToString("0.##", c), St.ToString("0.##", c),
                    RY.ToString("0.####", c), Ang.ToString("0.#", c) });
            }
        }

        public static List<CtlPoint> ParseCtl(string s)
        {
            List<CtlPoint> l = new List<CtlPoint>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (string p in s.Split('|'))
            {
                string[] a = p.Split(',');
                if (a.Length < 7) continue;
                int n = a.Length >= 9 ? 9 : 7;
                float[] v = new float[9];
                bool ok = true;
                for (int i = 0; i < n; i++) ok &= float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                if (!ok) continue;
                CtlPoint c = new CtlPoint();
                c.X = v[0]; c.Y = v[1]; c.R = v[2]; c.Br = v[3]; c.Co = v[4]; c.Sa = v[5]; c.St = v[6];
                c.RY = n == 9 ? v[7] : v[2]; c.Ang = v[8];
                l.Add(c);
            }
            return l;
        }

        public static string JoinCtl(List<CtlPoint> l)
        {
            List<string> s = new List<string>();
            foreach (CtlPoint c in l) s.Add(c.ToString());
            return string.Join("|", s.ToArray());
        }

        // Mask of one control point: spatial falloff times colour similarity to the tapped colour.
        public static float[] CtlMask(Img src, CtlPoint c)
        {
            int w = src.W, h = src.H;
            float cx = c.X * w, cy = c.Y * h;
            float sr = 0, sg = 0, sb = 0; int n = 0;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                {
                    float r, g, b; src.Get((int)cx + dx, (int)cy + dy, out r, out g, out b);
                    sr += r; sg += g; sb += b; n++;
                }
            sr /= n; sg /= n; sb /= n;
            float sl = Ops.Lum(sr, sg, sb);
            float sx = Math.Max(4f, c.R * src.MinDim * 0.5f), sy = Math.Max(4f, c.RY * src.MinDim * 0.5f);
            float isx = 1f / sx, isy = 1f / sy;
            float ca = (float)Math.Cos(c.Ang * Math.PI / 180), sa = (float)Math.Sin(c.Ang * Math.PI / 180);
            float[] m = new float[w * h];
            float[] D = src.D;
            Ops.For(0, h, y =>
            {
                float ddy = y - cy;
                for (int x = 0; x < w; x++)
                {
                    float ddx = x - cx;
                    // in the zone's own axes, in units of its spread
                    float u = (ddx * ca + ddy * sa) * isx, v = (-ddx * sa + ddy * ca) * isy;
                    float d2 = u * u + v * v;
                    if (d2 > 9) continue;
                    float sp = (float)Math.Exp(-0.5f * d2);
                    int j = (y * w + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float l = Ops.Lum(r, g, b);
                    float cr = (r - l) - (sr - sl), cg = (g - l) - (sg - sl), cb = (b - l) - (sb - sl);
                    float cd = (cr * cr + cg * cg + cb * cb) * 3.0f + (l - sl) * (l - sl) * 1.2f;
                    float cs = (float)Math.Exp(-cd / (2 * 0.018f));
                    m[y * w + x] = sp * (0.15f + 0.85f * cs);
                }
            });
            return Ops.Gauss(m, w, h, Math.Max(0.8f, 1.5f * src.Scale));
        }

        public static Img Selective(Img src, Step s)
        {
            List<CtlPoint> pts = ParseCtl(s.Str("points", ""));
            Img o = src.Clone();
            if (pts.Count == 0) return o;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            float[] bs = null;
            foreach (CtlPoint c in pts) if (c.St != 0) { bs = Ops.Guided(L, L, w, h, Math.Max(2, (int)(10 * src.Scale)), 0.008f); break; }
            float[] D = o.D;
            foreach (CtlPoint c in pts)
            {
                if (c.Br == 0 && c.Co == 0 && c.Sa == 0 && c.St == 0) continue;
                float[] m = CtlMask(src, c);
                float gamma = (float)Math.Pow(2, -c.Br / 100f * 1.2f);
                float[] gt = Ops.PowTable(gamma);
                float co = c.Co / 100f, sa = 1 + c.Sa / 100f, st = c.St / 100f;
                Ops.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        float a = m[i];
                        if (a < 0.003f) continue;
                        int j = i * 3;
                        float r = D[j], g = D[j + 1], b = D[j + 2];
                        float l = Ops.Lum(r, g, b), t = l;
                        if (gamma != 1) t = Ops.PowAt(gt, t, gamma);
                        if (co != 0) t = Ops.Contrast(t, co);
                        if (st != 0 && bs != null) t += Clip((L[i] - bs[i]) * st * 2.5f, 0.25f);
                        float r2 = r, g2 = g, b2 = b;
                        Ops.SetLum(ref r2, ref g2, ref b2, l, Math.Max(0, t));
                        if (sa != 1) Ops.Saturate(ref r2, ref g2, ref b2, sa);
                        D[j] = r + (r2 - r) * a; D[j + 1] = g + (g2 - g) * a; D[j + 2] = b + (b2 - b) * a;
                    }
                });
            }
            return o;
        }

        // ---------------- Brush ----------------
        public static Img Brush(Img src, Step s)
        {
            Img o = src.Clone();
            int w = src.W, h = src.H;
            float[] md = Raster.Has(s.Strokes, "dodge") ? Raster.Channel(s.Strokes, "dodge", src, null) : null;
            float[] me = Raster.Has(s.Strokes, "exposure") ? Raster.Channel(s.Strokes, "exposure", src, null) : null;
            float[] mt = Raster.Has(s.Strokes, "temp") ? Raster.Channel(s.Strokes, "temp", src, null) : null;
            float[] ms = Raster.Has(s.Strokes, "sat") ? Raster.Channel(s.Strokes, "sat", src, null) : null;
            if (md == null && me == null && mt == null && ms == null) return o;
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    if (me != null && me[i] != 0)
                    {
                        float k = (float)Math.Pow(2, me[i] / 10f);
                        r *= k; g *= k; b *= k;
                    }
                    if (md != null && md[i] != 0)
                    {
                        float v = md[i] / 10f, l = Ops.Lum(r, g, b), t;
                        if (v > 0) t = 1 - (float)Math.Pow(Math.Max(0, 1 - l), 1 + v * 1.2f);
                        else t = (float)Math.Pow(Math.Max(0, l), 1 - v * 1.2f);
                        Ops.SetLum(ref r, ref g, ref b, l, t);
                    }
                    if (mt != null && mt[i] != 0)
                    {
                        float v = mt[i] / 10f;
                        float l0 = Ops.Lum(r, g, b);
                        r += v * 0.14f; b -= v * 0.16f;
                        float d = l0 - Ops.Lum(r, g, b); r += d; g += d; b += d;
                    }
                    if (ms != null && ms[i] != 0) Ops.Saturate(ref r, ref g, ref b, 1 + ms[i] / 10f);
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            return o;
        }

        // ---------------- HDR Scape ----------------
        public static Img Hdr(Img src, Step s)
        {
            int style = s.I("style", 0);
            float[] rad = { 25, 45, 9, 18 }, det = { 1.0f, 0.55f, 1.35f, 1.8f }, comp = { 0.55f, 0.4f, 0.4f, 0.75f }, satb = { 0.25f, 0.05f, 0.15f, 0.4f };
            if (style < 0 || style > 3) style = 0;
            float f = s.F("strength", 50) / 100f;
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB s0 = j.Source, gl = j.Luma(s0), glg = j.Keep("/log", gl, () => j.K("LogP", 1, new float[] { 0.02f }, null, gl));
                Gpu.GB gb = j.Guided(glg, Math.Max(2, (int)(rad[style] * src.Scale)), 0.12f);
                float[] bl = j.Download(gb);
                double sum = 0; for (int i = 0; i < bl.Length; i += 7) sum += bl[i];
                float mean0 = (float)(sum / ((bl.Length + 6) / 7));
                float cm = comp[style] * f, dt0 = det[style] * f * 1.6f, br0 = s.F("brightness") / 100f, sa0 = s.F("saturation") / 100f;
                return j.K("Hdr", 3, new float[] { mean0, cm, dt0, f, br0, (float)Math.Pow(2, -br0), (1 + satb[style] * f) * (1 + sa0) }, null, s0, gl, glg, gb);
            });
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            // the log luminance and its smoothing do not depend on the sliders: kept for a stable input (Memo)
            Func<float[]> makeLog = () =>
            {
                float[] a = Pool.Rent(L.Length);
                for (int i = 0; i < L.Length; i++) a[i] = (float)Math.Log(L[i] + 0.02f);
                return a;
            };
            Memo.Info li = Memo.InfoOf(L);
            float[] lg = li != null ? Memo.Get(li.Key + "/log", li, makeLog) : makeLog();
            float[] baseL = Ops.Guided(lg, lg, w, h, Math.Max(2, (int)(rad[style] * src.Scale)), 0.12f);
            double mean = 0; for (int i = 0; i < baseL.Length; i += 7) mean += baseL[i];
            float mn = (float)(mean / ((baseL.Length + 6) / 7));
            float cmp = comp[style] * f, dt = det[style] * f * 1.6f;
            float br = s.F("brightness") / 100f, sa = s.F("saturation") / 100f;
            float gamma = (float)Math.Pow(2, -br);
            float[] gt = Ops.PowTable(gamma);
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float nl = mn + (baseL[i] - mn) * (1 - cmp) + (lg[i] - baseL[i]) * (1 + dt) + cmp * 0.12f;
                    float t = (float)Math.Exp(nl) - 0.02f;
                    t = Ops.Lerp(t, Ops.Contrast(Ops.C01(t), 0.25f * f), 0.6f);
                    if (br != 0) t = Ops.PowAt(gt, t, gamma);
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    Ops.SetLum(ref r, ref g, ref b, L[i], Ops.C01(t));
                    Ops.Saturate(ref r, ref g, ref b, (1 + satb[style] * f) * (1 + sa));
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            Ops.Done(L, lg, baseL);
            return o;
        }

        // ---------------- Glamour Glow ----------------
        public static Img Glamour(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(5, s.I("style", 0)));
            float[] rad = { 10, 14, 7, 18, 12, 22 }, boost = { 1.0f, 1.1f, 0.85f, 1.2f, 1.0f, 1.35f };
            float[] ssat = { 0, -0.1f, 0.15f, 0.05f, -0.35f, 0.1f }, swarm = { 0, 0.12f, 0, 0.2f, 0, -0.1f };
            float glow = s.F("glow", 50) / 100f, sa = s.F("saturation") / 100f + ssat[st], wa = s.F("warmth") / 100f + swarm[st];
            Img gpu = Gpu.RunJob(src, j =>
                j.K("Glamour", 3, new float[] { glow * 0.85f * boost[st], sa, wa }, null, j.Source, j.FastGaussRgb(j.Source, rad[st] * src.Scale)));
            if (gpu != null) return gpu;
            Img blur = Ops.FastGaussImg(src, rad[st] * src.Scale);
            Img o = src.Clone();
            float[] D = o.D, B = blur.D;
            float k = glow * 0.85f * boost[st];
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int j = (y * o.W + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float br2 = B[j], bg = B[j + 1], bb = B[j + 2];
                    float sr = Ops.Screen(r, br2 * 0.9f), sg = Ops.Screen(g, bg * 0.9f), sb = Ops.Screen(b, bb * 0.9f);
                    sr = Ops.Lerp(sr, Ops.SoftLight(sr, br2), 0.5f); sg = Ops.Lerp(sg, Ops.SoftLight(sg, bg), 0.5f); sb = Ops.Lerp(sb, Ops.SoftLight(sb, bb), 0.5f);
                    r = Ops.Lerp(r, sr, k); g = Ops.Lerp(g, sg, k); b = Ops.Lerp(b, sb, k);
                    Ops.Saturate(ref r, ref g, ref b, 1 + sa);
                    if (wa != 0) { float l0 = Ops.Lum(r, g, b); r += wa * 0.09f; b -= wa * 0.1f; float d = l0 - Ops.Lum(r, g, b); r += d; g += d; b += d; }
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            blur.Recycle();
            return o;
        }

        // ---------------- Tonal Contrast ----------------
        public static Img Tonal(Img src, Step s)
        {
            float hi = s.F("high", 25) / 100f, mid = s.F("mid", 25) / 100f, lo = s.F("low", 25) / 100f;
            float ps = s.F("protshadows") / 100f, ph = s.F("prothighlights") / 100f;
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB s0 = j.Source, gl = j.Luma(s0);
                return j.K("Tonal", 3, new float[] { hi, mid, lo, ps, ph }, null, s0, gl,
                    j.Guided(gl, Math.Max(1, (int)(3 * src.Scale)), 0.003f), j.Guided(gl, Math.Max(2, (int)(10 * src.Scale)), 0.008f), j.FastGauss(gl, 30 * src.Scale));
            });
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            float[] b1 = Ops.Guided(L, L, w, h, Math.Max(1, (int)(3 * src.Scale)), 0.003f);
            float[] b2 = Ops.Guided(L, L, w, h, Math.Max(2, (int)(10 * src.Scale)), 0.008f);
            float[] b3 = Ops.FastGauss(L, w, h, 30 * src.Scale);
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float l = L[i], t = b2[i];
                    float wl = (1 - t) * (1 - t), wh = t * t, wm = 4 * t * (1 - t);
                    float amt = hi * wh * 1.4f + mid * wm + lo * wl * 1.4f;
                    float d = (l - b1[i]) * 0.6f + (l - b2[i]) * 1.0f + (l - b3[i]) * 0.5f;
                    float prot = 1 - ps * Ops.SmoothStep(0.25f, 0.0f, l) - ph * Ops.SmoothStep(0.75f, 1.0f, l);
                    float nl = l + Clip(d * amt * 3.2f * Math.Max(0, prot), 0.3f);
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float dd = nl - l;
                    D[j] = r + dd; D[j + 1] = g + dd; D[j + 2] = b + dd;
                }
            });
            Ops.Done(L, b1, b2, b3);
            return o;
        }

        // ---------------- Drama ----------------
        public static Img Drama(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(5, s.I("style", 0)));
            float[] rad = { 14, 26, 8, 14, 26, 8 }, amt = { 0.8f, 1.0f, 1.25f, 0.9f, 1.1f, 1.35f };
            float[] tone = { 0.15f, 0.1f, 0.05f, -0.2f, -0.3f, -0.15f }, desat = { 0.2f, 0.3f, 0.4f, 0.25f, 0.35f, 0.5f };
            float f = s.F("strength", 50) / 100f, sa = s.F("saturation") / 100f;
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB s0 = j.Source, gl = j.Luma(s0);
                return j.K("Drama", 3, new float[] { amt[st], f, tone[st] * f, (1 - desat[st] * f) * (1 + sa) }, null, s0, gl,
                    j.Guided(gl, Math.Max(2, (int)(rad[st] * src.Scale)), 0.015f), j.Guided(gl, Math.Max(1, (int)(3 * src.Scale)), 0.004f));
            });
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            float[] bl = Ops.Guided(L, L, w, h, Math.Max(2, (int)(rad[st] * src.Scale)), 0.015f);
            float[] bl2 = Ops.Guided(L, L, w, h, Math.Max(1, (int)(3 * src.Scale)), 0.004f);
            Img o = src.Clone();
            float[] D = o.D;
            float tn = tone[st] * f;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float l = L[i];
                    float d = (l - bl[i]) + (l - bl2[i]) * 0.6f;
                    float t = l + Clip(d * amt[st] * f * 4.2f, 0.35f);
                    t = Ops.C01(t);
                    if (tn > 0) t = 1 - (float)Math.Pow(1 - t, 1 + tn * 2.5f);
                    else if (tn < 0) t = (float)Math.Pow(t, 1 - tn * 2.5f);
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    Ops.SetLum(ref r, ref g, ref b, l, t);
                    Ops.Saturate(ref r, ref g, ref b, (1 - desat[st] * f) * (1 + sa));
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            return o;
        }

        // ---------------- Vignette ----------------
        public static Img Vignette(Img src, Step s)
        {
            float cx = src.PX(s.F("cx", 0.5)), cy = src.PY(s.F("cy", 0.5));
            float size = s.F("size", 50) / 100f;
            float outer = s.F("outer", -50) / 100f, inner = s.F("inner", 0) / 100f;
            float hd = src.Diagonal * 0.5f;
            float r0 = hd * (0.15f + size * 0.85f);
            Img gpu = Gpu.RunPointJob(src, j => j.K("Vignette", 3, new float[] { cx, cy, r0, outer, inner }, null, j.Source));
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / r0;
                    float m = Ops.SmoothStep(0.45f, 1.35f, d);
                    float k = (float)Math.Pow(2, outer * 1.6f * m + inner * 0.9f * (1 - m));
                    int j = (y * o.W + x) * 3;
                    D[j] *= k; D[j + 1] *= k; D[j + 2] *= k;
                }
            });
            return o;
        }

        // ---------------- Lens Blur ----------------
        public static bool InShape(int shape, float x, float y)
        {
            float ax = Math.Abs(x), ay = Math.Abs(y);
            switch (shape)
            {
                case 1: return ay <= 0.866f && ax * 0.866f + ay * 0.5f <= 0.866f;                       // hexagon
                case 2: return ax <= 0.92f && ay <= 0.92f && ax + ay <= 1.25f;                           // octagon
                case 3:                                                                                   // triangle
                    return y <= 0.5f && y >= -1f + 0f && (0.5f - y) >= 0 && ax <= (y + 1f) * 0.577f;
                case 4:                                                                                   // star
                    {
                        double th = Math.Atan2(y, x), r = Math.Sqrt(x * x + y * y);
                        double a = ((th + Math.PI / 2) % (2 * Math.PI / 5) + 2 * Math.PI / 5) % (2 * Math.PI / 5) - Math.PI / 5;
                        double lim = 0.42 + 0.58 * Math.Pow(Math.Abs(Math.Cos(a * 2.5)), 6);
                        return r <= lim;
                    }
                case 5:                                                                                   // heart
                    {
                        // The curve runs from the cusp at -1 to the top of the lobes at about 1.25, so it is
                        // fitted to that and not to a circle: scaled as if it were symmetric, the lobes fall
                        // outside the kernel and come back with their tops cut off square.
                        float hx = x * 1.15f, hy = -y * 1.12f + 0.12f;
                        float q = hx * hx + hy * hy - 1;
                        return q * q * q - hx * hx * hy * hy * hy <= 0;
                    }
                // a mirror lens draws a thin bright ring, not a thick doughnut: the hole is most of it
                case 6: { float r2 = x * x + y * y; return r2 <= 1 && r2 >= 0.72f; }                      // ring (mirror lens)
                default: return x * x + y * y <= 1;                                                       // circle
            }
        }

        // How far out of focus each pixel is, 0 sharp to 1 fully blurred. Elliptical and linear draw a shape on
        // the picture; depth asks how far away each pixel really is and compares that with whatever the focus
        // point is sitting on, so the blur follows the scene instead of a drawn shape.
        public static float[] FocusMask(Img src, Step s)
        {
            int shape = s.I("shape", 0);
            if (shape == 2) return DepthMask(src, s);
            float cx = src.PX(s.F("cx", 0.5)), cy = src.PY(s.F("cy", 0.5));
            float rx = Math.Max(2, s.F("rx", 0.25) * src.MinDim), ry = Math.Max(2, s.F("ry", 0.25) * src.MinDim);
            float ang = s.F("angle") * (float)Math.PI / 180f;
            float trans = 0.08f + s.F("transition", 30) / 100f * 1.8f;
            float ca = (float)Math.Cos(ang), sn = (float)Math.Sin(ang);
            float[] m = Pool.Rent(src.N);
            int w = src.W;
            Ops.For(0, src.H, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float u = (dx * ca + dy * sn) / rx, v = (-dx * sn + dy * ca) / ry;
                    float d = shape == 1 ? Math.Abs(v) : (float)Math.Sqrt(u * u + v * v);
                    m[y * w + x] = Ops.SmoothStep(1f, 1f + trans, d);
                }
            });
            return m;
        }

        // Depth mode. The depth map is worked out once per photo and kept; everything here is arithmetic on it,
        // so dragging the focus point across the photo costs one pass over the pixels and nothing else.
        // In front of the focus and behind it are separate: a lens blurs both, but what is nearer blurs faster,
        // which is what keeps a portrait's background soft while the hands stay nearly sharp.
        static float[] DepthMask(Img src, Step s)
        {
            int mw, mh;
            // always the whole picture: depth is relative, so a part of the photo measured on its own would put
            // its own nearest thing at 1 and the focus would mean something different at every zoom. Pipeline
            // keeps depth mode off the region path for that reason.
            float[] map = Depther.Map(src, out mw, out mh);
            float[] m = Pool.Rent(src.N);
            if (map == null)
            {
                // no model: everything sharp rather than a blur that means nothing
                return m;
            }
            // The focus point picks a depth off the map, so the user points at a face rather than at a number,
            // and everything at that depth comes into focus together: a plane through the scene, not a spot.
            float focus = Depther.At(map, mw, mh, s.F("cx", 0.5), s.F("cy", 0.5));
            float dof = 0.02f + s.F("depthrange", 30) / 100f * 0.45f;      // how deep a slice stays sharp
            float ramp = 1.25f + s.F("transition", 30) / 100f * 2.6f;      // how quickly the blur comes on past it
            int w = src.W, h = src.H;
            float sx = (mw - 1) / (float)Math.Max(1, w - 1), sy = (mh - 1) / (float)Math.Max(1, h - 1);
            Ops.For(0, h, y =>
            {
                float my = y * sy;
                int y0 = (int)my; if (y0 > mh - 1) y0 = mh - 1;
                int y1 = Math.Min(mh - 1, y0 + 1);
                float ay = my - y0;
                for (int x = 0; x < w; x++)
                {
                    float mx = x * sx;
                    int x0 = (int)mx; if (x0 > mw - 1) x0 = mw - 1;
                    int x1 = Math.Min(mw - 1, x0 + 1);
                    float ax = mx - x0;
                    float d = (map[y0 * mw + x0] * (1 - ax) + map[y0 * mw + x1] * ax) * (1 - ay)
                            + (map[y1 * mw + x0] * (1 - ax) + map[y1 * mw + x1] * ax) * ay;
                    // What the model gives is inverse depth - big near, small far - and that is the quantity a
                    // real lens works in: the circle of confusion of a point at distance d, focused at s, goes
                    // as |1/d - 1/s|. So the same step either side of the focus blurs the same amount here,
                    // and the asymmetry everyone knows from photographs - the background going soft long before
                    // the foreground does, in metres - falls out of inverse depth on its own rather than being
                    // put in by hand. It used to be weighted the other way by mistake, which left what was in
                    // front of the focus nearly sharp and made it look as though only the background blurred.
                    float t = Math.Abs(d - focus) / dof;
                    m[y * w + x] = Ops.SmoothStep(1f, ramp, t);
                }
            });
            return m;
        }

        // false gathers every tap of the shape on its own (slower, and the yardstick for the run-by-run way)
        public static bool GatherRuns = true;

        // The blurred copy depends on the blur strength and the lens shape, and on nothing else: where the focus
        // sits only decides how much of it each pixel takes. So it is kept, and dragging the focus point across
        // the photo costs the mixing pass alone instead of gathering the whole shape again. Preview-sized
        // pictures only: a full-resolution copy is 12 bytes a pixel, and full renders are one-offs anyway.
        // (false does the work every time, which is the yardstick this was measured against.)
        public static bool KeepBlur = true;
        class Blurred { public float[] Print; public int W, H; public float Strength; public int Bokeh; public bool Depth; public Img[] B; }
        static readonly List<Blurred> blurs = new List<Blurred>();
        public static void ForgetBlurs() { lock (blurs) blurs.Clear(); }
        static float[] BlurPrint(Img im)
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
        static Img[] KeptBlur(Img src, float strength, int bokeh, bool depth, float[] print)
        {
            lock (blurs)
                foreach (Blurred b in blurs)
                {
                    if (b.W != src.W || b.H != src.H || b.Bokeh != bokeh || b.Depth != depth || Math.Abs(b.Strength - strength) > 1e-4f) continue;
                    bool same = true;
                    for (int i = 0; i < print.Length && same; i++) if (Math.Abs(b.Print[i] - print[i]) > 0.004f) same = false;
                    if (same) return b.B;
                }
            return null;
        }

        // One blurred copy of the whole picture at one radius, with the lens shape as its kernel. Pulled out so
        // depth mode can make several at different radii: a lens does not blur everything out of focus by the
        // same amount, and mixing one maximum blur in by a fraction gives a ghost of the sharp picture rather
        // than something softly out of focus.
        static Img BlurAt(Img src, float maxR, int bokeh, float thrPct)
        {
            if (maxR < 1f) return src.Clone();
                // How many pixels across the lens shape itself is drawn. The blur runs on a shrunk copy so the
            // kernel stays small, and eight pixels of radius was enough while every shape was a circle or a
            // hexagon: a heart or a star at seventeen pixels across is a staircase, stretched up to whatever
            // the blur is. A wider blur gets a bigger kernel, where there is something to see; the work goes
            // up with it, which is why it is capped and why the blurred copy is kept between frames.
            float detail = Math.Min(24f, Math.Max(14f, maxR / 2.5f));
            float f = Math.Max(1f, maxR / detail);
            int sw = Math.Max(1, (int)(src.W / f)), sh = Math.Max(1, (int)(src.H / f));
            Img small = f > 1 ? Ops.Resize(src, sw, sh) : src.Clone();
            float kr = maxR / f;
            // Emphasise highlights so they bloom into bokeh shapes. With a small blur this picture is the
            // whole photo, so the pass runs on every core and reads its power from a table.
            // Threshold says how bright a pixel has to be to count as a highlight: low and much of the photo
            // turns into lens shapes, high and only true speculars do, which is where the shape reads best.
            float thr = 0.95f - thrPct / 100f * 0.55f;
            float[] SD = small.D;
            float[] up = Ops.PowTable(2.2f);
            Ops.For(0, sh, y =>
            {
                for (int j = y * sw * 3, e = j + sw * 3; j < e; j += 3)
                {
                    float l = Ops.Lum(SD[j], SD[j + 1], SD[j + 2]);
                    float boost = 1 + 5 * Ops.SmoothStep(thr, Math.Min(1f, thr + 0.28f), l);
                    for (int c = 0; c < 3; c++) SD[j + c] = Ops.PowAt(up, SD[j + c], 2.2f) * boost;
                }
            });
            // The taps are gathered - each pixel reads its neighbours - which lays the shape down turned
            // through half a turn, so the offsets are negated. A circle or a hexagon never showed it; a
            // heart came out upside down and a triangle pointed the wrong way.
            List<int> ox = new List<int>(), oy = new List<int>();
            int ik = (int)Math.Ceiling(kr);
            for (int dy = -ik; dy <= ik; dy++)
                for (int dx = -ik; dx <= ik; dx++)
                    if (InShape(bokeh, -dx / kr, -dy / kr)) { ox.Add(dx); oy.Add(dy); }
            if (ox.Count == 0) { ox.Add(0); oy.Add(0); }
            int[] OX = ox.ToArray(), OY = oy.ToArray();
            // The same taps as runs of neighbouring pixels, one row of the shape at a time (every shape here
            // gives a handful of runs per row). With a running sum along each row a run costs two reads, so a
            // disc of radius 8 costs about 17 pairs of reads instead of 200 single ones.
            List<int> rn = new List<int>();
            int taps = 0;
            for (int dy = -ik; dy <= ik; dy++)
            {
                int start = int.MinValue;
                for (int dx = -ik; dx <= ik + 1; dx++)
                {
                    bool inside = dx <= ik && InShape(bokeh, -dx / kr, -dy / kr);
                    if (inside) { if (start == int.MinValue) start = dx; }
                    else if (start != int.MinValue) { rn.Add(dy); rn.Add(start); rn.Add(dx - 1); taps += dx - start; start = int.MinValue; }
                }
            }
            if (taps != OX.Length) { rn.Clear(); rn.Add(0); rn.Add(0); rn.Add(0); taps = 1; }
            int[] RN = rn.ToArray();
            Img bs = Img.Unfilled(sw, sh);
            float[] BD = bs.D;
            float inv = 1f / OX.Length;
            // the discs are averaged in linear light, so the way back runs over 0..6 (a boosted highlight)
            float[] down = Ops.PowTable(1f / 2.2f, 6f);
            float[] gpuOut = Gpu.Use(sw * sh * OX.Length / 40) ? Gpu.Gather(SD, sw, sh, OX, OY) : null;
            if (gpuOut != null) bs.D = gpuOut;
            else if (!GatherRuns) Ops.For(0, sh, y =>
            {
                for (int x = 0; x < sw; x++)
                {
                    float r = 0, g = 0, b = 0;
                    for (int k = 0; k < OX.Length; k++)
                    {
                        int xx = x + OX[k], yy = y + OY[k];
                        if (xx < 0) xx = 0; else if (xx >= sw) xx = sw - 1;
                        if (yy < 0) yy = 0; else if (yy >= sh) yy = sh - 1;
                        int j = (yy * sw + xx) * 3;
                        r += SD[j]; g += SD[j + 1]; b += SD[j + 2];
                    }
                    int o = (y * sw + x) * 3;
                    BD[o] = Ops.PowAt(down, r * inv, 1f / 2.2f, 1f / 6f);
                    BD[o + 1] = Ops.PowAt(down, g * inv, 1f / 2.2f, 1f / 6f);
                    BD[o + 2] = Ops.PowAt(down, b * inv, 1f / 2.2f, 1f / 6f);
                }
            });
            else
            {
                int pw = sw + 1;
                float[] P = Pool.Rent(pw * sh * 3);          // running sum along each row
                Ops.For(0, sh, y =>
                {
                    int pb = y * pw * 3, sb = y * sw * 3;
                    for (int x = 0; x < sw; x++)
                    {
                        P[pb + (x + 1) * 3] = P[pb + x * 3] + SD[sb + x * 3];
                        P[pb + (x + 1) * 3 + 1] = P[pb + x * 3 + 1] + SD[sb + x * 3 + 1];
                        P[pb + (x + 1) * 3 + 2] = P[pb + x * 3 + 2] + SD[sb + x * 3 + 2];
                    }
                });
                int last = sw - 1, lastY = sh - 1;
                Ops.For(0, sh, y =>
                {
                    for (int x = 0; x < sw; x++)
                    {
                        float r = 0, g = 0, b = 0;
                        for (int k = 0; k < RN.Length; k += 3)
                        {
                            int yy = y + RN[k]; if (yy < 0) yy = 0; else if (yy > lastY) yy = lastY;
                            int lo = x + RN[k + 1], hi = x + RN[k + 2];
                            int pb = yy * pw * 3, sb = yy * sw * 3;
                            if (hi < 0) { int c = hi - lo + 1; r += c * SD[sb]; g += c * SD[sb + 1]; b += c * SD[sb + 2]; continue; }
                            if (lo > last) { int c = hi - lo + 1, e = sb + last * 3; r += c * SD[e]; g += c * SD[e + 1]; b += c * SD[e + 2]; continue; }
                            if (lo < 0) { r += -lo * SD[sb]; g += -lo * SD[sb + 1]; b += -lo * SD[sb + 2]; lo = 0; }
                            if (hi > last) { int c = hi - last, e = sb + last * 3; r += c * SD[e]; g += c * SD[e + 1]; b += c * SD[e + 2]; hi = last; }
                            int a0 = pb + lo * 3, a1 = pb + (hi + 1) * 3;
                            r += P[a1] - P[a0]; g += P[a1 + 1] - P[a0 + 1]; b += P[a1 + 2] - P[a0 + 2];
                        }
                        int o = (y * sw + x) * 3;
                        BD[o] = Ops.PowAt(down, r * inv, 1f / 2.2f, 1f / 6f);
                        BD[o + 1] = Ops.PowAt(down, g * inv, 1f / 2.2f, 1f / 6f);
                        BD[o + 2] = Ops.PowAt(down, b * inv, 1f / 2.2f, 1f / 6f);
                    }
                });
                Ops.Done(P);
            }
            Img done = f > 1 ? Ops.Resize(bs, src.W, src.H) : bs;
            if (f > 1) bs.Recycle();
            small.Recycle();
            return done;
        }

        public static Img LensBlur(Img src, Step s)
        {
            float strength = s.F("strength", 50) / 100f;
            float vig = s.F("vignette", 30) / 100f;
            int bokeh = s.I("bokeh", 0);
            float thr = s.F("bokehthreshold", 40);
            bool depth = s.I("shape", 0) == 2;
            float[] m = FocusMask(src, s);
            float maxR = strength * 0.02f * src.Diagonal;
            bool keep = KeepBlur && (long)src.W * src.H <= 6000000;
            float[] bprint = keep ? BlurPrint(src) : null;
            Img[] lv = keep ? KeptBlur(src, strength, bokeh, depth, bprint) : null;
            bool fromKeep = lv != null;
            if (lv == null)
            {
                if (maxR < 1f) lv = new Img[] { src };
                // A lens does not blur everything that is out of focus by the same amount: how far out of focus
                // a thing is decides how big its circle of confusion is. Depth mode builds a few blurs and
                // reads between them, so a face a little off the plane is a little soft and the far trees are
                // a wash. One blur mixed in by a fraction cannot do that - it leaves a ghost of the sharp
                // picture behind everything, which is the look that says "software", not "lens".
                else if (depth) lv = new Img[] { BlurAt(src, maxR * 0.34f, bokeh, thr), BlurAt(src, maxR * 0.66f, bokeh, thr), BlurAt(src, maxR, bokeh, thr) };
                else lv = new Img[] { BlurAt(src, maxR, bokeh, thr) };
                if (keep && maxR >= 1f)
                {
                    foreach (Img b in lv) b.Stable = true;     // held for the next frame, out of the pool's reach
                    lock (blurs)
                    {
                        blurs.Add(new Blurred { Print = bprint, W = src.W, H = src.H, Strength = strength, Bokeh = bokeh, Depth = depth, B = lv });
                        while (blurs.Count > 2) blurs.RemoveAt(0);
                    }
                    fromKeep = true;
                }
            }
            Img outImg = src.Clone();
            float[] D = outImg.D;
            int n = lv.Length;
            if (n == 1)
            {
                float[] BB = lv[0].D;
                Ops.For(0, src.H, y =>
                {
                    for (int x = 0; x < src.W; x++)
                    {
                        int i = y * src.W + x, j = i * 3;
                        float a = m[i];
                        float k = 1 - vig * 0.65f * a;
                        D[j] = Ops.Lerp(D[j], BB[j], a) * k;
                        D[j + 1] = Ops.Lerp(D[j + 1], BB[j + 1], a) * k;
                        D[j + 2] = Ops.Lerp(D[j + 2], BB[j + 2], a) * k;
                    }
                });
            }
            else
            {
                // the sharp picture, then each blur in turn: how far out of focus a pixel is picks the place
                // between two of them
                float[][] st = new float[n + 1][];
                st[0] = src.D;
                for (int k = 0; k < n; k++) st[k + 1] = lv[k].D;
                Ops.For(0, src.H, y =>
                {
                    for (int x = 0; x < src.W; x++)
                    {
                        int i = y * src.W + x, j = i * 3;
                        float a = m[i];
                        float u = a * n;
                        int i0 = (int)u; if (i0 > n - 1) i0 = n - 1;
                        float fr = u - i0;
                        float[] lo = st[i0], hi = st[i0 + 1];
                        float vk = 1 - vig * 0.65f * a;
                        D[j] = (lo[j] + (hi[j] - lo[j]) * fr) * vk;
                        D[j + 1] = (lo[j + 1] + (hi[j + 1] - lo[j + 1]) * fr) * vk;
                        D[j + 2] = (lo[j + 2] + (hi[j + 2] - lo[j + 2]) * fr) * vk;
                    }
                });
            }
            Ops.Done(m);
            if (!fromKeep) foreach (Img b in lv) if (b != src) b.Recycle();
            return outImg;
        }
    }
}

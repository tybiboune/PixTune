using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PixTune
{
    // Colour grade used by the film/vintage style families.
    public class Grade
    {
        public float[] Sh = { 0, 0, 0 }, Hi = { 0, 0, 0 };
        public float Fade, Sat = 1, Con, Bright, Crush;
        // this grade's brightness curve, sampled once here: Apply runs on every pixel
        readonly float gm; readonly float[] gt;
        public Grade(float shr, float shg, float shb, float hir, float hig, float hib, float fade, float sat, float con, float bright)
        {
            Sh = new float[] { shr, shg, shb }; Hi = new float[] { hir, hig, hib };
            Fade = fade; Sat = sat; Con = con; Bright = bright;
            gm = (float)Math.Pow(2, -bright); gt = Ops.PowTable(gm);
        }

        public void Apply(ref float r, ref float g, ref float b, float k)
        {
            float l = Ops.Lum(r, g, b);
            float t = Ops.SmoothStep(0f, 1f, l);
            float or_ = r, og = g, ob = b;
            r += Ops.Lerp(Sh[0], Hi[0], t) * 1.6f; g += Ops.Lerp(Sh[1], Hi[1], t) * 1.6f; b += Ops.Lerp(Sh[2], Hi[2], t) * 1.6f;
            if (Bright != 0) { r = Ops.PowAt(gt, r, gm); g = Ops.PowAt(gt, g, gm); b = Ops.PowAt(gt, b, gm); }
            if (Con != 0) { r = Ops.Contrast(r, Con); g = Ops.Contrast(g, Con); b = Ops.Contrast(b, Con); }
            Ops.Saturate(ref r, ref g, ref b, Sat);
            if (Fade > 0) { r = Fade + r * (1 - Fade * 0.85f); g = Fade + g * (1 - Fade * 0.85f); b = Fade + b * (1 - Fade * 0.85f); }
            r = Ops.Lerp(or_, r, k); g = Ops.Lerp(og, g, k); b = Ops.Lerp(ob, b, k);
        }
    }

    public static class Style
    {
        static Grade[] vintage = {
            new Grade(0.05f,0.02f,-0.05f, 0.06f,0.04f,-0.04f, 0.08f,0.80f,0.05f,0),
            new Grade(0.06f,0.02f,-0.06f, 0.08f,0.03f,-0.06f, 0.06f,0.35f,0.10f,0),
            new Grade(-0.02f,0.04f,0.00f, 0.04f,0.05f,-0.03f, 0.10f,0.70f,0.00f,0),
            new Grade(-0.04f,0.02f,0.06f, 0.07f,0.03f,-0.04f, 0.05f,0.85f,0.10f,0),
            new Grade(0.05f,-0.02f,0.05f, 0.04f,0.00f,0.02f, 0.12f,0.75f,0.00f,0),
            new Grade(0.08f,0.03f,-0.06f, 0.06f,0.02f,-0.08f, 0.03f,1.10f,0.20f,0),
            new Grade(-0.03f,0.00f,0.07f, -0.01f,0.02f,0.05f, 0.06f,0.70f,0.05f,0),
            new Grade(0.01f,0.01f,0.00f, 0.02f,0.02f,0.00f, 0.18f,0.45f,-0.05f,0),
            new Grade(0.08f,-0.02f,-0.02f, 0.03f,0.04f,-0.05f, 0.04f,0.90f,0.25f,0),
            new Grade(0.02f,0.05f,-0.05f, 0.05f,0.06f,-0.06f, 0.07f,0.80f,0.08f,0),
            new Grade(0.06f,0.02f,0.05f, 0.05f,0.03f,0.03f, 0.15f,0.60f,-0.05f,0.05f),
            new Grade(0.04f,0.01f,-0.04f, 0.03f,0.01f,-0.05f, 0.05f,0.50f,0.30f,-0.12f),
        };

        static Grade[] film = {
            new Grade(0.00f,0.01f,0.02f, 0.02f,0.01f,-0.01f, 0.03f,0.95f,0.10f,0),
            new Grade(0.02f,0.00f,-0.02f, 0.04f,0.02f,-0.03f, 0.05f,0.85f,0.05f,0),
            new Grade(-0.02f,0.02f,0.04f, 0.03f,0.02f,-0.02f, 0.04f,0.90f,0.15f,0),
            new Grade(0.03f,0.02f,0.00f, 0.05f,0.04f,0.01f, 0.08f,0.70f,0.00f,0.05f),
            new Grade(0.00f,0.00f,0.00f, 0.00f,0.00f,0.00f, 0.02f,1.20f,0.25f,0),
            new Grade(0.04f,0.00f,-0.03f, 0.02f,0.03f,-0.04f, 0.10f,0.65f,-0.05f,0),
            new Grade(-0.03f,0.01f,0.05f, -0.02f,0.01f,0.03f, 0.03f,0.80f,0.20f,-0.05f),
            new Grade(0.02f,0.03f,-0.02f, 0.04f,0.04f,-0.04f, 0.06f,0.75f,0.10f,0),
            new Grade(0.00f,0.00f,0.00f, 0.00f,0.00f,0.00f, 0.00f,0.00f,0.20f,0),
            new Grade(0.00f,0.00f,0.00f, 0.00f,0.00f,0.00f, 0.08f,0.00f,0.05f,0.05f),
            new Grade(0.05f,0.02f,-0.03f, 0.03f,0.01f,-0.02f, 0.00f,0.00f,0.30f,-0.05f),
            new Grade(0.06f,0.01f,0.02f, 0.03f,0.04f,0.00f, 0.05f,1.05f,0.10f,0),
            new Grade(-0.01f,0.03f,0.02f, 0.05f,0.02f,-0.02f, 0.07f,0.85f,0.00f,0.03f),
            new Grade(0.03f,-0.01f,0.04f, 0.05f,0.03f,-0.01f, 0.09f,0.80f,0.05f,0),
            new Grade(0.00f,0.02f,0.04f, 0.03f,0.03f,0.02f, 0.02f,0.60f,0.30f,-0.08f),
            new Grade(0.07f,0.03f,-0.05f, 0.04f,0.03f,-0.06f, 0.04f,1.00f,0.18f,0.02f),
        };

        static Grade[] retro = {
            new Grade(0.06f,0.03f,-0.04f, 0.05f,0.03f,-0.05f, 0.10f,0.80f,0.10f,0),
            new Grade(-0.03f,0.03f,0.05f, 0.06f,0.03f,-0.03f, 0.08f,0.75f,0.15f,0),
            new Grade(0.08f,0.00f,0.02f, 0.04f,0.03f,-0.02f, 0.12f,0.70f,0.05f,0),
            new Grade(0.02f,0.05f,-0.04f, 0.06f,0.06f,-0.05f, 0.09f,0.65f,0.10f,0),
            new Grade(0.05f,0.02f,-0.06f, 0.08f,0.04f,-0.08f, 0.06f,0.40f,0.20f,0),
            new Grade(-0.02f,0.00f,0.06f, 0.02f,0.02f,0.03f, 0.14f,0.60f,0.00f,0.05f),
            new Grade(0.07f,0.02f,0.04f, 0.06f,0.04f,0.02f, 0.10f,0.85f,0.05f,0),
            new Grade(0.04f,0.04f,0.00f, 0.03f,0.02f,-0.04f, 0.05f,1.10f,0.25f,-0.05f),
            new Grade(0.00f,0.00f,0.00f, 0.03f,0.02f,0.00f, 0.15f,0.30f,0.10f,0),
            new Grade(0.05f,-0.02f,0.03f, 0.07f,0.05f,-0.02f, 0.07f,0.90f,0.15f,0),
            new Grade(-0.04f,0.02f,0.03f, 0.03f,0.01f,-0.04f, 0.05f,0.70f,0.30f,-0.08f),
            new Grade(0.09f,0.04f,-0.02f, 0.06f,0.05f,0.00f, 0.13f,0.75f,-0.05f,0.06f),
            new Grade(0.03f,0.01f,-0.02f, 0.02f,0.02f,-0.03f, 0.04f,0.55f,0.35f,-0.1f),
        };

        public static int VintageCount { get { return vintage.Length; } }
        public static int FilmCount { get { return film.Length; } }
        public static int RetroCount { get { return retro.Length; } }

        static void VignetteMul(Img o, float amount, float cx, float cy, float size)
        {
            if (amount == 0) return;
            float hd = o.Diagonal * 0.5f, px = o.PX(cx), py = o.PY(cy);
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    float dx = x - px, dy = y - py;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / hd;
                    float k = 1 - amount * Ops.SmoothStep(size * 0.5f, size * 1.25f, d);
                    int j = (y * o.W + x) * 3; D[j] *= k; D[j + 1] *= k; D[j + 2] *= k;
                }
            });
        }

        static void AddGrain(Img o, float amount, float size, int seed, bool mono)
        {
            if (amount <= 0) return;
            float sz = Math.Max(0.6f, size * o.Scale);
            float[] g = Ops.Grain(o, sz, seed);
            float[] gc = mono ? null : Ops.Grain(o, sz, seed + 5);
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int i = y * o.W + x, j = i * 3;
                    float l = Ops.Lum(D[j], D[j + 1], D[j + 2]);
                    float wgt = 0.35f + 2.6f * l * (1 - l);
                    float n = g[i] * amount * 0.09f * wgt;
                    float cn = gc != null ? gc[i] * amount * 0.02f * wgt : 0;
                    D[j] += n + cn; D[j + 1] += n; D[j + 2] += n - cn;
                }
            });
        }

        // ---------------- Vintage ----------------
        public static Img Vintage(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(vintage.Length - 1, s.I("style", 0)));
            float k = s.F("strength", 50) / 100f;
            float br = s.F("brightness") / 100f, sa = s.F("saturation") / 100f, vig = s.F("vignette", 50) / 100f, blur = s.F("blur") / 100f;
            float cx = s.F("cx", 0.5), cy = s.F("cy", 0.5);
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB c = j.Source;
                if (blur > 0)
                {
                    Gpu.GB b = j.FastGaussRgb(c, 12 * src.Scale * (0.4f + blur));
                    c = j.K("VintageBlur", 3, new float[] { src.PX(cx), src.PY(cy), src.Diagonal * 0.5f, Math.Min(1f, blur * 1.5f) }, null, c, b);
                    j.Free(b);
                }
                c = j.Then(c, j.K("Grade", 3, new float[] { k, (float)Math.Pow(2, -br), sa, src.Scale }, new int[] { 1, br != 0 ? 1 : 0, 11 + st, src.FX, src.FY },
                    c, j.Values(Film.GradeValues(vintage[st]))));
                return j.Then(c, Film.VigMulGpu(j, c, src, vig * 0.7f, 0.5f, 0.5f, 1.05f * 0.5f, 1.05f * 1.25f));
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            if (blur > 0)
            {
                Img b = Ops.FastGaussImg(src, 12 * src.Scale * (0.4f + blur));
                float hd = o.Diagonal * 0.5f, px = o.PX(cx), py = o.PY(cy);
                for (int y = 0; y < o.H; y++)
                    for (int x = 0; x < o.W; x++)
                    {
                        float dx = x - px, dy = y - py;
                        float a = Ops.SmoothStep(0.25f, 0.9f, (float)Math.Sqrt(dx * dx + dy * dy) / hd) * Math.Min(1f, blur * 1.5f);
                        int j = (y * o.W + x) * 3;
                        for (int c = 0; c < 3; c++) o.D[j + c] = Ops.Lerp(o.D[j + c], b.D[j + c], a);
                    }
            }
            Grade gr = vintage[st];
            float gamma = (float)Math.Pow(2, -br);
            float[] gt = Ops.PowTable(gamma);
            float[] D = o.D;
            int w = o.W;
            float sc = o.Scale;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    gr.Apply(ref r, ref g, ref b, k);
                    if (br != 0) { r = Ops.PowAt(gt, r, gamma); g = Ops.PowAt(gt, g, gamma); b = Ops.PowAt(gt, b, gamma); }
                    Ops.Saturate(ref r, ref g, ref b, 1 + sa);
                    // faint paper texture
                    float tx = (Ops.Fbm((x + o.FX) / (40f * sc), (y + o.FY) / (40f * sc), 11 + st, 4) - 0.5f) * 0.06f * k;
                    D[j] = r + tx; D[j + 1] = g + tx; D[j + 2] = b + tx * 0.6f;
                }
            });
            VignetteMul(o, vig * 0.7f, 0.5f, 0.5f, 1.05f);
            return o;
        }

        // ---------------- Grainy Film ----------------
        public static Img Grainy(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(film.Length - 1, s.I("style", 0)));
            float k = s.F("strength", 50) / 100f, grain = s.F("grain", 25) / 100f;
            Img gpu = Gpu.RunJob(src, j =>
            {
                Gpu.GB c = j.K("Grade", 3, new float[] { k }, new int[] { 0 }, j.Source, j.Values(Film.GradeValues(film[st])));
                return Film.AddGrainGpu(j, c, src, grain * 1.4f, 1.6f + (st % 4) * 0.4f, 1234, film[st].Sat == 0);
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            Grade gr = film[st];
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int j = (y * o.W + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    gr.Apply(ref r, ref g, ref b, k);
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            AddGrain(o, grain * 1.4f, 1.6f + (st % 4) * 0.4f, 1234, gr.Sat == 0);
            return o;
        }

        // ---------------- Retrolux ----------------
        // false works each leak out in one piece on every pixel (slower, and the yardstick for the row-by-column way)
        public static bool LeakRowCol = true;

        public static Img Retrolux(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(retro.Length - 1, s.I("style", 0)));
            int seed = s.I("seed", 1);
            float k = s.F("strength", 50) / 100f;
            float br = s.F("brightness") / 100f, co = s.F("contrast") / 100f, sa = s.F("saturation") / 100f;
            float scratch = s.F("scratches", 50) / 100f, leaks = s.F("leaks", 50) / 100f;
            Grade gr = retro[st];
            int w = src.W, h = src.H;
            float sc = src.Scale;
            Random rnd = new Random(seed * 7919 + st * 31);
            // light leaks: big soft warm blobs near the borders
            int nl = 2 + rnd.Next(3);
            float[][] lk = new float[nl][];
            float[][] lpal = { new float[] { 1f, 0.45f, 0.1f }, new float[] { 1f, 0.2f, 0.15f }, new float[] { 1f, 0.8f, 0.3f }, new float[] { 0.9f, 0.3f, 0.6f } };
            for (int i = 0; i < nl; i++)
            {
                bool side = rnd.Next(2) == 0;
                float px = side ? (rnd.Next(2) == 0 ? -0.05f : 1.05f) : (float)rnd.NextDouble();
                float py = side ? (float)rnd.NextDouble() : (rnd.Next(2) == 0 ? -0.05f : 1.05f);
                float[] c = lpal[rnd.Next(lpal.Length)];
                lk[i] = new float[] { px * w, py * h, (0.25f + (float)rnd.NextDouble() * 0.35f) * Math.Max(w, h), c[0], c[1], c[2] };
            }
            float hd = (float)Math.Sqrt(w * w + h * h) * 0.5f;
            float gamma = (float)Math.Pow(2, -br);
            float[] gt = Ops.PowTable(gamma);
            Img o = Gpu.RunJob(src, j =>
            {
                float[] fl = new float[nl * 6];
                for (int i = 0; i < nl; i++) Array.Copy(lk[i], 0, fl, i * 6, 6);
                return j.K("Retrolux", 3, new float[] { k, gamma, co, sa, leaks, hd, w, h },
                    new int[] { nl, br != 0 ? 1 : 0 }, j.Source, j.Values(Film.GradeValues(gr)), j.Values(fl));
            });
            bool onGpu = o != null;
            if (o == null) o = src.Clone();
            // A leak's falloff is exp(-(dx^2 + dy^2)/c), which is exp(-dx^2/c) times exp(-dy^2/c): the first
            // depends only on the column and the second only on the row, so each is worked out once instead of
            // two to four exponentials on every pixel.
            float[][] lx = null;
            if (!onGpu && leaks > 0 && LeakRowCol)
            {
                lx = new float[lk.Length][];
                for (int i = 0; i < lk.Length; i++)
                {
                    float c = 2 * lk[i][2] * lk[i][2] * 0.25f;
                    float[] col = new float[w];
                    for (int x = 0; x < w; x++) { float dx = x - lk[i][0]; col[x] = (float)Math.Exp(-dx * dx / c); }
                    lx[i] = col;
                }
            }
            if (!onGpu)
            {
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                float[] ly = null;
                if (leaks > 0 && LeakRowCol)
                {
                    ly = new float[lk.Length];
                    for (int i = 0; i < lk.Length; i++)
                    {
                        float c = 2 * lk[i][2] * lk[i][2] * 0.25f, dy = y - lk[i][1];
                        ly[i] = (float)Math.Exp(-dy * dy / c) * leaks * 0.85f;
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    gr.Apply(ref r, ref g, ref b, k);
                    if (br != 0) { r = Ops.PowAt(gt, r, gamma); g = Ops.PowAt(gt, g, gamma); b = Ops.PowAt(gt, b, gamma); }
                    if (co != 0) { r = Ops.Contrast(r, co); g = Ops.Contrast(g, co); b = Ops.Contrast(b, co); }
                    Ops.Saturate(ref r, ref g, ref b, 1 + sa);
                    if (leaks > 0)
                        for (int i = 0; i < lk.Length; i++)
                        {
                            float a;
                            if (LeakRowCol) { a = lx[i][x] * ly[i]; if (a < 1e-4f) continue; }
                            else
                            {
                                float dx = x - lk[i][0], dy2 = y - lk[i][1];
                                a = (float)Math.Exp(-(dx * dx + dy2 * dy2) / (2 * lk[i][2] * lk[i][2] * 0.25f)) * leaks * 0.85f;
                            }
                            r = Ops.Screen(r, lk[i][3] * a); g = Ops.Screen(g, lk[i][4] * a); b = Ops.Screen(b, lk[i][5] * a);
                        }
                    float dd = (float)Math.Sqrt((x - w / 2f) * (x - w / 2f) + (y - h / 2f) * (y - h / 2f)) / hd;
                    float v = 1 - 0.45f * k * Ops.SmoothStep(0.55f, 1.3f, dd);
                    D[j] = r * v; D[j + 1] = g * v; D[j + 2] = b * v;
                }
            });
            }
            if (scratch > 0)
            {
                // Film damage, drawn on the processor (a picture left on the card comes back for it). Three kinds,
                // because that is what an old print shows: long scratches down the frame where grit in the gate
                // dragged on the film, hairs caught in the gate, and dust. A scratch cuts the emulsion away, so
                // it lets light through and prints bright, with a thin dark ridge of displaced emulsion beside it;
                // it weaves as the film moves, fades in and out along its length, and often cuts only one dye
                // layer, which tints it. Even brightness down a ruler-straight line is what made these look drawn.
                float[] D = o.D;
                int ns = (int)(3 + scratch * 13);
                for (int i = 0; i < ns; i++)
                {
                    // spread across the frame rather than left to chance, which bunches them against one edge;
                    // grit sits at its own places in the gate, so they also come in close pairs
                    float x0 = (i + 0.15f + 0.7f * (float)rnd.NextDouble()) / ns * w;
                    if (i > 0 && rnd.Next(4) == 0) x0 -= (0.5f + (float)rnd.NextDouble() * 2.5f) * sc;
                    bool full = rnd.Next(4) == 0;                      // a few run the whole frame
                    float y0 = full ? 0 : (float)rnd.NextDouble() * h * 0.75f;
                    float len = full ? h : (0.12f + (float)rnd.NextDouble() * 0.55f) * h;
                    float drift = ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                    float wd = Math.Max(0.5f, (0.3f + (float)rnd.NextDouble() * 1.0f) * sc);
                    bool light = rnd.Next(5) > 0;                      // the emulsion is gone: it prints bright
                    float amp = (light ? 0.34f : -0.26f) * (0.45f + (float)rnd.NextDouble() * 0.9f);
                    float ridge = rnd.Next(2) == 0 ? 1f : -1f;         // the side the displaced emulsion fell on
                    int sd = seed * 131 + i * 17;
                    // one dye layer cut: warm where the yellow went, cool where the cyan did
                    int tint = rnd.Next(4);
                    float tr = tint == 1 ? 1f : tint == 2 ? 0.82f : 1f;
                    float tg = tint == 1 ? 0.93f : tint == 2 ? 0.96f : 1f;
                    float tb = tint == 1 ? 0.76f : tint == 2 ? 1f : 1f;
                    int ya = (int)y0, yb = (int)Math.Min(h, y0 + len);
                    for (int y = ya; y < yb; y++)
                    {
                        float t = (y - y0) / Math.Max(1f, len);
                        // it fades in and out, and now and then lifts off the film altogether
                        float env = Ops.SmoothStep(0.2f, 0.62f, Ops.VNoise(y / (190f * sc), i * 2.3f, sd + 5));
                        if (!full) env *= Ops.SmoothStep(0f, 0.14f, t) * Ops.SmoothStep(0f, 0.14f, 1 - t);
                        if (env <= 0.01f) continue;
                        // the weave of the film, and a hair of jitter so the line is not suspiciously smooth
                        float cx = x0 + (y - y0) * drift
                                 + (Ops.VNoise(y / (85f * sc), i * 3.1f, sd) - 0.5f) * 5f * sc
                                 + (Ops.VNoise(y / (2.5f * sc), i * 5.7f, sd + 9) - 0.5f) * 0.45f * sc;
                        float ww = wd * (0.5f + 1.0f * Ops.VNoise(y / (150f * sc), i * 7.7f, sd + 2));
                        float a0 = amp * env * scratch;
                        float rw = Math.Max(0.6f, 1.1f * sc);
                        int xa = (int)(cx - ww - rw - 1), xb = (int)(cx + ww + rw + 1);
                        for (int x = xa; x <= xb; x++)
                        {
                            if (x < 0 || x >= w) continue;
                            float dx = x - cx;
                            float core = 1 - Ops.SmoothStep(ww * 0.3f, ww + 0.55f, Math.Abs(dx));
                            // the ridge: a thinner, darker line hard against one side of the cut
                            float rd = (dx * ridge - ww - 0.6f * sc) / rw;
                            float side = (1 - Ops.SmoothStep(0f, 1f, Math.Abs(rd))) * 0.4f;
                            float v = a0 * (core - side);
                            if (v == 0) continue;
                            int j = (y * w + x) * 3;
                            D[j] += v * tr; D[j + 1] += v * tg; D[j + 2] += v * tb;
                        }
                    }
                }
                // hairs caught in the gate: thin, dark, curved, and never straight
                int nh = (int)(scratch * 3.4f);
                for (int i = 0; i < nh; i++)
                {
                    float hx = (float)rnd.NextDouble() * w, hy = (float)rnd.NextDouble() * h;
                    float ang = (float)(rnd.NextDouble() * Math.PI * 2);
                    float len = (0.10f + (float)rnd.NextDouble() * 0.35f) * Math.Max(w, h);
                    float curl = ((float)rnd.NextDouble() - 0.5f) * 0.012f;
                    float hw = Math.Max(0.55f, 0.75f * sc);
                    float amp = -0.5f * (0.5f + (float)rnd.NextDouble() * 0.7f) * scratch;
                    int sd = seed * 613 + i * 29;
                    float step = 0.5f * sc;
                    for (float d = 0; d < len; d += step)
                    {
                        float t = d / len;
                        // it bends as it lies, it does not corkscrew: a slow turn, not a turn at every step
                        ang += curl * step / sc + (Ops.VNoise(d / (70f * sc), i * 1.7f, sd) - 0.5f) * 0.018f;
                        hx += (float)Math.Cos(ang) * step; hy += (float)Math.Sin(ang) * step;
                        if (hx < -4 || hy < -4 || hx > w + 4 || hy > h + 4) break;
                        // thinner and fainter towards both ends, as a hair lies
                        float e = Ops.SmoothStep(0f, 0.12f, t) * Ops.SmoothStep(0f, 0.18f, 1 - t);
                        float a0 = amp * e;
                        int xa = (int)(hx - hw - 1), xb = (int)(hx + hw + 1);
                        int ya = (int)(hy - hw - 1), yb = (int)(hy + hw + 1);
                        for (int y = ya; y <= yb; y++)
                        {
                            if (y < 0 || y >= h) continue;
                            for (int x = xa; x <= xb; x++)
                            {
                                if (x < 0 || x >= w) continue;
                                float dd = (float)Math.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
                                float a = (1 - Ops.SmoothStep(hw * 0.3f, hw + 0.5f, dd)) * a0 * 0.5f;
                                if (a == 0) continue;
                                int j = (y * w + x) * 3;
                                for (int c = 0; c < 3; c++) D[j + c] += a;
                            }
                        }
                    }
                }
                // dust: dirt on the print prints dark, dust on the negative prints white, and neither is a disc
                int nd = (int)(scratch * 230);
                for (int i = 0; i < nd; i++)
                {
                    float px = (float)rnd.NextDouble() * w, py = (float)rnd.NextDouble() * h;
                    float rr = (0.45f + (float)rnd.NextDouble() * 1.5f) * sc;
                    if (rnd.Next(12) == 0) rr *= 2.8f;                 // the odd bigger clump
                    bool dark = rnd.Next(3) > 0;
                    float amp = (dark ? -0.5f : 0.55f) * (0.45f + (float)rnd.NextDouble() * 0.8f) * scratch * 0.75f;
                    int sd = seed * 977 + i * 13;
                    float inv = 1.7f / Math.Max(0.5f, rr);
                    int xa = (int)(px - rr * 1.6f - 1), xb = (int)(px + rr * 1.6f + 1);
                    int ya = (int)(py - rr * 1.6f - 1), yb = (int)(py + rr * 1.6f + 1);
                    for (int y = ya; y <= yb; y++)
                    {
                        if (y < 0 || y >= h) continue;
                        for (int x = xa; x <= xb; x++)
                        {
                            if (x < 0 || x >= w) continue;
                            float dx = x - px, dy = y - py;
                            float dd = (float)Math.Sqrt(dx * dx + dy * dy);
                            // the edge wobbles: a speck of dirt has no radius
                            float rad = rr * (0.55f + 0.9f * Ops.VNoise(dx * inv + 8f, dy * inv + 8f, sd));
                            float a = (1 - Ops.SmoothStep(rad * 0.35f, rad, dd)) * amp;
                            if (a == 0) continue;
                            int j = (y * w + x) * 3;
                            for (int c = 0; c < 3; c++) D[j + c] += a;
                        }
                    }
                }
            }
            AddGrain(o, 0.35f * k, 1.8f, seed + 99, false);
            return o;
        }

        // ---------------- Grunge ----------------
        public static Img Grunge(Img src, Step s)
        {
            int style = s.I("style", 0), tex = Math.Max(0, Math.Min(4, s.I("texture", 0)));
            float br = s.F("brightness") / 100f, co = s.F("contrast") / 100f, ts = s.F("texstrength", 50) / 100f, sa = s.F("saturation") / 100f;
            float cx = s.F("cx", 0.5), cy = s.F("cy", 0.5);
            Random rnd = new Random(style * 1013 + 7);
            // style -> a two-tone colour grade picked from the hue wheel
            double hue = rnd.NextDouble() * Math.PI * 2;
            float tr = (float)Math.Cos(hue) * 0.07f, tg = (float)Math.Cos(hue + 2.1) * 0.07f, tb = (float)Math.Cos(hue + 4.2) * 0.07f;
            float desat = 0.35f + (float)rnd.NextDouble() * 0.45f;
            int w = src.W, h = src.H;
            float sc = src.Scale;
            Img o = src.Clone();
            float[] D = o.D;
            float hd = (float)Math.Sqrt(w * w + h * h) * 0.5f;
            float gamma = (float)Math.Pow(2, -br);
            float[] gt = Ops.PowTable(gamma);
            int ts0 = 300 + tex * 17;
            int speckG = Math.Max(1, (int)(3 * sc));
            Img gpu = Gpu.RunJob(src, j => j.K("Grunge", 3,
                new float[] { 0.35f + co, desat * (1 + sa), tr, tg, tb, ts * 0.35f, sc, hd, cx * w, cy * h, gamma },
                new int[] { tex, ts0, speckG, br != 0 ? 1 : 0 }, j.Source));
            if (gpu != null) return gpu;
            // the texture's fractal noise, on a grid instead of every pixel (its own size along each axis)
            float per = 90f * sc;
            Ops.NoiseField nf;
            switch (tex)
            {
                case 1: nf = Ops.FbmField(w, h, 0, 0, per / 1.7f, ts0, 5); break;
                case 2: nf = Ops.FbmField(w, h, 0, 0, per, ts0, 3); break;
                case 3: nf = Ops.FbmField(w, h, 0, 0, per / 0.25f, per / 4f, ts0, 4); break;
                case 4: nf = Ops.FbmField(w, h, 0, 0, per / 3f, ts0, 5); break;
                default: nf = Ops.FbmField(w, h, 0, 0, per / 0.6f, ts0, 6); break;
            }
            int speck = Math.Max(1, (int)(3 * sc));
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float l = Ops.Lum(r, g, b);
                    // structure punch typical of the grunge look
                    r = Ops.Contrast(r, 0.35f + co); g = Ops.Contrast(g, 0.35f + co); b = Ops.Contrast(b, 0.35f + co);
                    Ops.Saturate(ref r, ref g, ref b, desat * (1 + sa));
                    float t = Ops.SmoothStep(0, 1, l);
                    r += tr * (1.2f - t); g += tg * (1.2f - t); b += tb * (1.2f - t);
                    if (br != 0) { r = Ops.PowAt(gt, r, gamma); g = Ops.PowAt(gt, g, gamma); b = Ops.PowAt(gt, b, gamma); }
                    // texture
                    float tv;
                    switch (tex)
                    {
                        case 1: tv = 1 - Math.Abs(nf.At(x, y) * 2 - 1); float t2 = tv * tv, t3 = t2 * tv; tv = t3 * t3 * 1.4f - 0.15f; break; // cracks
                        case 2: tv = Ops.Hash(x / speck, y / speck, ts0) > 0.985f ? 1f : (nf.At(x, y) - 0.5f) * 0.5f; break; // specks
                        case 3: tv = (nf.At(x, y) - 0.5f) * 1.6f; break; // scratched / brushed
                        case 4: tv = (nf.At(x, y) - 0.5f) * 1.2f; break; // paper
                        default: tv = (nf.At(x, y) - 0.5f) * 1.8f; break; // stains
                    }
                    float a = tv * ts * 0.35f;
                    r = Ops.SoftLight(Ops.C01(r), Ops.C01(0.5f + a)); g = Ops.SoftLight(Ops.C01(g), Ops.C01(0.5f + a)); b = Ops.SoftLight(Ops.C01(b), Ops.C01(0.5f + a * 0.8f));
                    // centre of interest stays brighter
                    float dx = x - cx * w, dy = y - cy * h;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) / hd;
                    float v = 1.08f - 0.55f * Ops.SmoothStep(0.15f, 1.2f, d);
                    D[j] = r * v; D[j + 1] = g * v; D[j + 2] = b * v;
                }
            });
            return o;
        }

        // ---------------- Black & White ----------------
        static float[][] bwFilters = {
            new float[] { 0.299f, 0.587f, 0.114f },  // neutral
            new float[] { 0.80f, 0.20f, 0.00f },     // red
            new float[] { 0.60f, 0.35f, 0.05f },     // orange
            new float[] { 0.45f, 0.48f, 0.07f },     // yellow
            new float[] { 0.20f, 0.70f, 0.10f },     // green
            new float[] { 0.10f, 0.25f, 0.65f },     // blue
        };

        public static float BwMix(float r, float g, float b, int filter)
        {
            float[] f = bwFilters[Math.Max(0, Math.Min(5, filter))];
            return r * f[0] + g * f[1] + b * f[2];
        }

        public static Img BlackWhite(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(5, s.I("style", 0)));
            int filter = s.I("filter", 0);
            // neutral, contrast, bright, dark, film, darken sky
            float[] scon = { 0, 0.35f, 0.05f, 0.2f, 0.25f, 0.3f }, sbr = { 0, 0, 0.35f, -0.4f, 0, -0.05f }, sgr = { 0, 0, 0, 0, 0.35f, 0 };
            if (st == 5 && filter == 0) filter = 1;
            float co = s.F("contrast") / 100f + scon[st], br = s.F("brightness") / 100f + sbr[st], grain = s.F("grain") / 100f + sgr[st];
            float gamma = (float)Math.Pow(2, -br * 1.1f);
            float[] gt = Ops.PowTable(gamma);
            float[] fw = bwFilters[Math.Max(0, Math.Min(5, filter))];
            Img gpu = Gpu.RunPointJob(src, jb =>
            {
                Gpu.GB c = jb.K("BlackWhite", 3, new float[] { fw[0], fw[1], fw[2], gamma, co }, new int[] { st == 5 ? 1 : 0 }, jb.Source);
                return Film.AddGrainGpu(jb, c, src, grain * 1.2f, 1.7f, 4321, true);
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int j = (y * o.W + x) * 3;
                    float v = BwMix(D[j], D[j + 1], D[j + 2], filter);
                    if (st == 5)
                    {
                        // extra darkening of blue/cyan areas (sky)
                        float bl = Ops.C01((D[j + 2] - (D[j] + D[j + 1]) * 0.5f) * 3f);
                        v *= 1 - 0.45f * bl;
                    }
                    v = Ops.PowAt(gt, v, gamma);
                    v = Ops.Contrast(v, co);
                    D[j] = v; D[j + 1] = v; D[j + 2] = v;
                }
            });
            AddGrain(o, grain * 1.2f, 1.7f, 4321, true);
            return o;
        }

        // ---------------- Noir ----------------
        public static Img Noir(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(13, s.I("style", 0)));
            float br = s.F("brightness") / 100f, wash = s.F("wash", 25) / 100f, k = s.F("strength", 50) / 100f, grain = s.F("grain", 25) / 100f;
            Random rnd = new Random(st * 97 + 3);
            float con = 0.3f + (float)rnd.NextDouble() * 0.5f, vig = 0.3f + (float)rnd.NextDouble() * 0.5f;
            float tone = ((float)rnd.NextDouble() - 0.5f) * 0.5f;
            float[] warm = { 0, 0, 0.03f, 0, 0, -0.02f, 0, 0.04f, 0, 0, 0.02f, 0, -0.03f, 0 };
            int filter = st % 6;
            float[] nf = bwFilters[Math.Max(0, Math.Min(5, filter))];
            Img gpu = Gpu.RunJob(src, jb =>
            {
                Gpu.GB c = jb.K("Noir", 3, new float[] { nf[0], nf[1], nf[2], (float)Math.Pow(2, -br - tone), con * k * 1.6f, Math.Min(1f, 0.3f + k), warm[st] }, null, jb.Source);
                if (wash > 0)
                {
                    Gpu.GB b = jb.FastGaussRgb(c, 10 * src.Scale);
                    c = jb.Then(c, jb.K("WashScreen", 3, new float[] { wash * 0.9f }, null, c, b));
                    jb.Free(b);
                }
                c = jb.Then(c, Film.VigMulGpu(jb, c, src, vig * k, 0.5f, 0.5f, 0.5f, 1.25f));
                c = jb.Then(c, jb.K("FbmAdd", 3, new float[] { src.Scale, 0.08f * k }, new int[] { 900 + st, src.FX, src.FY }, c));
                return jb.Then(c, Film.AddGrainGpu(jb, c, src, grain * 1.5f, 2.0f, 2468 + st, true));
            });
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            Img o = src.Clone();
            float[] D = o.D;
            float gamma = (float)Math.Pow(2, -br - tone);
            float[] gt = Ops.PowTable(gamma);
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 3;
                    float v = BwMix(D[j], D[j + 1], D[j + 2], filter);
                    float l0 = v;
                    v = Ops.PowAt(gt, v, gamma);
                    v = Ops.Contrast(v, con * k * 1.6f);
                    v = Ops.Lerp(l0, v, Math.Min(1f, 0.3f + k));
                    D[j] = v + warm[st]; D[j + 1] = v; D[j + 2] = v - warm[st];
                }
            });
            if (wash > 0)
            {
                // wash: a bright halation that lifts and softens
                Img b = Ops.FastGaussImg(o, 10 * o.Scale);
                for (int i = 0; i < o.D.Length; i++) o.D[i] = Ops.Lerp(o.D[i], Ops.Screen(o.D[i], b.D[i] * 0.85f), wash * 0.9f);
            }
            VignetteMul(o, vig * k, 0.5f, 0.5f, 1.0f);
            // light film damage
            float[] Dd = o.D;
            float sc = o.Scale;
            int sd = 900 + st;
            Ops.NoiseField dmg = Ops.FbmField(w, h, o.FX, o.FY, 120f * sc, sd, 4);
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float n = dmg.At(x, y) - 0.5f;
                    int j = (y * w + x) * 3;
                    float a = n * 0.08f * k;
                    Dd[j] += a; Dd[j + 1] += a; Dd[j + 2] += a;
                }
            });
            AddGrain(o, grain * 1.5f, 2.0f, 2468 + st, true);
            return o;
        }

        // ---------------- Double Exposure ----------------
        static readonly Dictionary<string, Img> overlayCache = new Dictionary<string, Img>();
        public static void Forget() { lock (overlayCache) overlayCache.Clear(); }

        public static Img Overlay(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (overlayCache)
            {
                Img im;
                if (overlayCache.TryGetValue(path, out im)) return im;
                try
                {
                    using (Bitmap b = ImageIO.Load(path)) im = Img.FromBitmap(b).FitInside(3000);
                    overlayCache[path] = im;
                    return im;
                }
                catch { return null; }
            }
        }

        public static float Blend(int mode, float a, float b)
        {
            switch (mode)
            {
                case 1: return Math.Max(a, b);                // lighten
                case 2: return Math.Min(a, b);                // darken
                case 3: return a + b;                         // add
                case 4: return a - b;                         // subtract
                case 5: return a * b;                         // multiply
                case 6: return Ops.Overlay(Ops.C01(a), Ops.C01(b));
                case 7: return Ops.Screen(Ops.C01(a), Ops.C01(b));
                default: return b;                            // normal
            }
        }

        public static Img DoubleExposure(Img src, Step s)
        {
            Img ov = Overlay(s.Str("path", ""));
            if (ov == null) return src.Clone();
            int mode = s.I("mode", 0);
            float op = s.F("opacity", 50) / 100f;
            float ox = s.F("ox", 0.5) * src.W, oy = s.F("oy", 0.5) * src.H;
            float cover = Math.Max((float)src.W / ov.W, (float)src.H / ov.H);
            float sc = cover * (float)Math.Pow(2, s.F("dscale") / 100f * 1.5f);
            double rot = -s.F("drot") * Math.PI / 180;
            float c = (float)Math.Cos(rot), sn = (float)Math.Sin(rot);
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, src.H, y =>
            {
                for (int x = 0; x < src.W; x++)
                {
                    float dx = x - ox, dy = y - oy;
                    float u = (dx * c - dy * sn) / sc + ov.W / 2f, v = (dx * sn + dy * c) / sc + ov.H / 2f;
                    if (u < 0 || v < 0 || u > ov.W - 1 || v > ov.H - 1) continue;
                    float r, g, b; ov.Sample(u, v, out r, out g, out b);
                    int j = (y * src.W + x) * 3;
                    D[j] = Ops.Lerp(D[j], Blend(mode, D[j], r), op);
                    D[j + 1] = Ops.Lerp(D[j + 1], Blend(mode, D[j + 1], g), op);
                    D[j + 2] = Ops.Lerp(D[j + 2], Blend(mode, D[j + 2], b), op);
                }
            });
            return o;
        }

        // ---------------- Text ----------------
        public static readonly Color[] Palette = {
            Color.White, Color.FromArgb(20,20,20), Color.FromArgb(128,128,128), Color.FromArgb(230,57,70), Color.FromArgb(244,140,40),
            Color.FromArgb(250,215,60), Color.FromArgb(160,220,70), Color.FromArgb(46,160,90), Color.FromArgb(30,150,150), Color.FromArgb(80,200,230),
            Color.FromArgb(40,90,200), Color.FromArgb(120,70,190), Color.FromArgb(210,60,160), Color.FromArgb(250,170,190), Color.FromArgb(120,80,50)
        };

        // font, style, uppercase, decoration
        public class TextStyle
        {
            public string Font; public FontStyle FS; public bool Upper; public int Deco; public float Track;
            public TextStyle(string f, FontStyle fs, bool up, int deco, float track) { Font = f; FS = fs; Upper = up; Deco = deco; Track = track; }
        }

        public static readonly TextStyle[] TextStyles = {
            new TextStyle("Segoe UI", FontStyle.Regular, false, 0, 0),
            new TextStyle("Segoe UI Black", FontStyle.Regular, true, 0, 0),
            new TextStyle("Georgia", FontStyle.Italic, false, 0, 0),
            new TextStyle("Impact", FontStyle.Regular, true, 3, 0),
            new TextStyle("Segoe Script", FontStyle.Regular, false, 0, 0),
            new TextStyle("Bahnschrift", FontStyle.Regular, true, 2, 0.3f),
            new TextStyle("Times New Roman", FontStyle.Regular, true, 2, 0.25f),
            new TextStyle("Arial Black", FontStyle.Regular, true, 1, 0),
            new TextStyle("Courier New", FontStyle.Bold, false, 7, 0),
            new TextStyle("Gabriola", FontStyle.Regular, false, 0, 0),
            new TextStyle("Segoe UI Light", FontStyle.Regular, true, 6, 0.4f),
            new TextStyle("Palatino Linotype", FontStyle.Bold | FontStyle.Italic, false, 4, 0),
            new TextStyle("Ink Free", FontStyle.Regular, false, 0, 0),
            new TextStyle("Segoe UI", FontStyle.Bold, true, 5, 0.1f),
            new TextStyle("Constantia", FontStyle.Regular, true, 7, 0.2f),
            new TextStyle("Trebuchet MS", FontStyle.Bold, false, 4, 0),
            new TextStyle("Verdana", FontStyle.Bold, true, 1, 0.1f),
            new TextStyle("Segoe Print", FontStyle.Bold, false, 6, 0),
            new TextStyle("Consolas", FontStyle.Regular, true, 3, 0.2f),
            new TextStyle("Georgia", FontStyle.Bold, true, 5, 0.15f),
            new TextStyle("Impact", FontStyle.Regular, true, 1, 0),
            new TextStyle("Comic Sans MS", FontStyle.Bold, false, 3, 0),
            new TextStyle("Candara", FontStyle.Italic, false, 2, 0.1f),
            new TextStyle("Bahnschrift", FontStyle.Bold, true, 8, 0.2f),
        };

        static Font MakeFont(TextStyle ts, float px)
        {
            try
            {
                FontFamily ff = new FontFamily(ts.Font);
                FontStyle fs = ts.FS;
                if (!ff.IsStyleAvailable(fs)) fs = FontStyle.Regular;
                return new Font(ff, px, fs, GraphicsUnit.Pixel);
            }
            catch { return new Font(FontFamily.GenericSansSerif, px, ts.FS, GraphicsUnit.Pixel); }
        }

        static string Track(string t, float track)
        {
            if (track <= 0) return t;
            string sep = track > 0.3f ? "  " : " ";
            List<string> lines = new List<string>();
            foreach (string line in t.Split('\n'))
            {
                char[] ch = line.ToCharArray();
                List<string> parts = new List<string>();
                foreach (char c in ch) parts.Add(c.ToString());
                lines.Add(string.Join(track > 0.15f ? sep : " ", parts.ToArray()));
            }
            return string.Join("\n", lines.ToArray());
        }

        // Renders the text layer's coverage (alpha) at the image size.
        public static float[] TextAlpha(int w, int h, Step s)
        {
            string text = s.Str("text", "Double tap\nto edit text");
            if (text.Length == 0) return new float[w * h];
            TextStyle ts = TextStyles[Math.Max(0, Math.Min(TextStyles.Length - 1, s.I("style", 0)))];
            if (ts.Upper) text = text.ToUpperInvariant();
            text = Track(text.Replace("\r", ""), ts.Track);
            float px = Math.Max(6f, s.F("tsize", 40) / 100f * 0.22f * Math.Min(w, h));
            float[] a = new float[w * h];
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (Font f = MakeFont(ts, px))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.TranslateTransform(s.F("tx", 0.5) * w, s.F("ty", 0.5) * h);
                g.RotateTransform(s.F("trot"));
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                SizeF sz = g.MeasureString(text, f, new SizeF(w * 4f, h * 4f), sf);
                RectangleF box = new RectangleF(-sz.Width / 2, -sz.Height / 2, sz.Width, sz.Height);
                float pad = px * 0.35f;
                RectangleF big = RectangleF.Inflate(box, pad, pad * 0.6f);
                using (SolidBrush wb = new SolidBrush(Color.White))
                using (Pen pen = new Pen(Color.White, Math.Max(1f, px * 0.06f)))
                {
                    switch (ts.Deco)
                    {
                        case 1: // solid banner with the text knocked out
                            g.FillRectangle(wb, big);
                            g.CompositingMode = CompositingMode.SourceCopy;
                            using (GraphicsPath gp = new GraphicsPath())
                            {
                                gp.AddString(text, f.FontFamily, (int)f.Style, f.Size, box, sf);
                                using (SolidBrush cb = new SolidBrush(Color.Transparent)) g.FillPath(cb, gp);
                            }
                            g.CompositingMode = CompositingMode.SourceOver;
                            break;
                        case 2: // rules above and below
                            g.DrawString(text, f, wb, box, sf);
                            g.DrawLine(pen, big.Left, big.Top, big.Right, big.Top);
                            g.DrawLine(pen, big.Left, big.Bottom, big.Right, big.Bottom);
                            break;
                        case 3: // outline only
                            using (GraphicsPath gp = new GraphicsPath())
                            {
                                gp.AddString(text, f.FontFamily, (int)f.Style, f.Size, box, sf);
                                using (Pen p2 = new Pen(Color.White, Math.Max(1.5f, px * 0.05f))) { p2.LineJoin = LineJoin.Round; g.DrawPath(p2, gp); }
                            }
                            break;
                        case 4: // soft shadow under the text
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                                g.DrawString(text, f, sb, new RectangleF(box.X + px * 0.06f, box.Y + px * 0.06f, box.Width, box.Height), sf);
                            g.DrawString(text, f, wb, box, sf);
                            break;
                        case 5: // circle badge
                            {
                                float d = Math.Max(big.Width, big.Height) * 1.05f;
                                using (Pen p2 = new Pen(Color.White, Math.Max(1.5f, px * 0.08f))) g.DrawEllipse(p2, -d / 2, -d / 2, d, d);
                                g.DrawString(text, f, wb, box, sf);
                            }
                            break;
                        case 6: // underline bar
                            g.DrawString(text, f, wb, box, sf);
                            g.FillRectangle(wb, box.Left + box.Width * 0.3f, big.Bottom, box.Width * 0.4f, Math.Max(2f, px * 0.08f));
                            break;
                        case 7: // framed
                            g.DrawRectangle(pen, big.X, big.Y, big.Width, big.Height);
                            g.DrawString(text, f, wb, box, sf);
                            break;
                        case 8: // tag with brackets
                            g.DrawString(text, f, wb, box, sf);
                            g.DrawLines(pen, new PointF[] { new PointF(big.Left + pad, big.Top), new PointF(big.Left, big.Top), new PointF(big.Left, big.Bottom), new PointF(big.Left + pad, big.Bottom) });
                            g.DrawLines(pen, new PointF[] { new PointF(big.Right - pad, big.Top), new PointF(big.Right, big.Top), new PointF(big.Right, big.Bottom), new PointF(big.Right - pad, big.Bottom) });
                            break;
                        default:
                            g.DrawString(text, f, wb, box, sf);
                            break;
                    }
                }
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] buf = new byte[bd.Stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                int stride = bd.Stride;
                bmp.UnlockBits(bd);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) a[y * w + x] = buf[y * stride + x * 4 + 3] / 255f;
            }
            return a;
        }

        public static Img Text(Img src, Step s)
        {
            float[] a = TextAlpha(src.W, src.H, s);
            Color c = Palette[Math.Max(0, Math.Min(Palette.Length - 1, s.I("color", 0)))];
            float cr = c.R / 255f, cg = c.G / 255f, cb = c.B / 255f;
            float op = s.F("opacity", 100) / 100f;
            bool inv = s.I("invert", 0) == 1;
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, src.H, y =>
            {
                for (int x = 0; x < src.W; x++)
                {
                    int i = y * src.W + x, j = i * 3;
                    float t = (inv ? 1 - a[i] : a[i]) * op;
                    D[j] = Ops.Lerp(D[j], cr, t); D[j + 1] = Ops.Lerp(D[j + 1], cg, t); D[j + 2] = Ops.Lerp(D[j + 2], cb, t);
                }
            });
            return o;
        }

        // ---------------- Frames ----------------
        public static int FrameCount { get { return 23; } }

        // Signed distance to a rounded window (positive = in the frame).
        static float WindowSD(float x, float y, float l, float t, float r, float b, float rad)
        {
            float cx = (l + r) / 2, cy = (t + b) / 2, hw = (r - l) / 2, hh = (b - t) / 2;
            rad = Math.Min(rad, Math.Min(hw, hh));
            float qx = Math.Abs(x - cx) - (hw - rad), qy = Math.Abs(y - cy) - (hh - rad);
            float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - rad;
        }

        public static Img Frames(Img src, Step s)
        {
            int st = Math.Max(0, Math.Min(FrameCount - 1, s.I("style", 0)));
            float wv = s.F("fwidth", 50) / 100f;
            int w = src.W, h = src.H;
            float md = src.MinDim, sc = src.Scale;
            float fw = Math.Max(1f, (0.01f + wv * 0.11f) * md);
            float l = fw, t = fw, r = w - fw, b = h - fw, rad = 0, soft = 0.7f, noiseAmp = 0, noiseScale = 20;
            float[] col = { 1, 1, 1 };
            float[] black = { 0.04f, 0.04f, 0.04f }, white = { 0.98f, 0.98f, 0.97f }, cream = { 0.93f, 0.89f, 0.80f };
            switch (st)
            {
                case 0: col = white; break;
                case 1: col = black; break;
                case 2: col = white; rad = fw * 2.5f; break;
                case 3: col = black; rad = fw * 2.5f; break;
                case 4: col = white; break;
                case 5: col = black; break;
                case 6: col = white; b = h - fw * 3.6f; break;
                case 7: col = black; t = fw * 1.7f; b = h - fw * 1.7f; l = fw * 0.4f; r = w - fw * 0.4f; break;
                case 8: col = white; noiseAmp = fw * 0.6f; noiseScale = 6; break;
                case 9: col = black; noiseAmp = fw * 0.6f; noiseScale = 6; break;
                case 10: col = white; noiseAmp = fw * 0.9f; noiseScale = 40; break;
                case 11: col = white; soft = fw * 2.2f; l += fw; t += fw; r -= fw; b -= fw; break;
                case 12: col = black; soft = fw * 2.2f; l += fw; t += fw; r -= fw; b -= fw; break;
                case 13: col = white; break;
                case 14: col = cream; noiseAmp = fw * 0.25f; noiseScale = 2.5f; break;
                case 15: col = new float[] { 0.55f, 0.55f, 0.55f }; rad = fw * 1.4f; l = fw * 1.3f; r = w - fw * 1.3f; t = fw * 1.3f; b = h - fw * 1.3f; break;
                case 16: col = black; noiseAmp = fw * 1.1f; noiseScale = 3; break;
                case 17: col = white; break;
                case 18: col = white; break;
                case 19: col = black; rad = fw * 5f; soft = fw * 1.2f; break;
                case 20: col = new float[] { 0.08f, 0.08f, 0.09f }; b = h - fw * 3.6f; break;
                case 21: col = black; soft = fw * 2.5f; break;
                case 22: col = white; l = fw * 1.6f; r = w - fw * 1.6f; t = fw * 1.6f; b = h - fw * 1.6f; break;
            }
            Img o = src.Clone();
            float[] D = o.D;
            int seed = 50 + st;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float sd = WindowSD(x + 0.5f, y + 0.5f, l, t, r, b, rad);
                    if (noiseAmp > 0)
                    {
                        float n = Ops.Fbm(x / (noiseScale * sc + 0.5f), y / (noiseScale * sc + 0.5f), seed, 4) - 0.5f;
                        sd += n * noiseAmp * 2.2f;
                    }
                    if (st == 18) sd += (float)Math.Sin((x + y) / (fw * 0.8f)) * fw * 0.25f;
                    float a = Ops.SmoothStep(-soft, soft, sd);
                    float cr = col[0], cg = col[1], cb = col[2];
                    if (st == 4 || st == 5 || st == 22)
                    {
                        // a thin contrasting rule inside the border
                        float lw = Math.Max(1f, fw * 0.08f);
                        float ld = Math.Abs(sd - fw * 0.35f);
                        float la = 1 - Ops.SmoothStep(lw * 0.5f, lw, ld);
                        float[] lc = st == 5 ? white : (st == 22 ? new float[] { 0.6f, 0.6f, 0.6f } : black);
                        if (sd > 0) { cr = Ops.Lerp(cr, lc[0], la); cg = Ops.Lerp(cg, lc[1], la); cb = Ops.Lerp(cb, lc[2], la); }
                    }
                    if (st == 7 && a > 0.5f)
                    {
                        // film sprocket holes along the long edges
                        float pitch = fw * 1.4f;
                        float inY = y < fw * 1.7f ? fw * 0.85f : (y > h - fw * 1.7f ? h - fw * 0.85f : -1);
                        if (inY > 0)
                        {
                            float px = ((x % pitch) + pitch) % pitch - pitch / 2;
                            float hs = WindowSD(px, y - inY, -fw * 0.32f, -fw * 0.3f, fw * 0.32f, fw * 0.3f, fw * 0.08f);
                            float ha = 1 - Ops.SmoothStep(-0.7f, 0.7f, hs);
                            cr = Ops.Lerp(cr, 0.92f, ha); cg = Ops.Lerp(cg, 0.9f, ha); cb = Ops.Lerp(cb, 0.85f, ha);
                        }
                    }
                    if (st == 17 && sd > 0)
                    {
                        float pitch = fw * 0.9f;
                        float px = ((x % pitch) + pitch) % pitch - pitch / 2, py = ((y % pitch) + pitch) % pitch - pitch / 2;
                        float dd = (float)Math.Sqrt(px * px + py * py);
                        float da = 1 - Ops.SmoothStep(fw * 0.18f, fw * 0.26f, dd);
                        cr = Ops.Lerp(cr, 0.75f, da); cg = Ops.Lerp(cg, 0.75f, da); cb = Ops.Lerp(cb, 0.75f, da);
                    }
                    if (st == 21) { float dots = Ops.Hash(x / 3, y / 3, 5) < a ? 1 : 0; a = Ops.Lerp(a, dots, 0.6f * (1 - a) * a * 4); }
                    int j = (y * w + x) * 3;
                    if (st == 13 && sd < 0)
                    {
                        // inner shadow cast by the mount
                        float sh = (1 - Ops.SmoothStep(-fw * 0.6f, 0, sd)) * 0;
                        float k = 1 - 0.35f * Ops.SmoothStep(-fw * 0.6f, 0, sd);
                        D[j] *= k; D[j + 1] *= k; D[j + 2] *= k;
                        sh += 0;
                    }
                    if (a <= 0) continue;
                    D[j] = Ops.Lerp(D[j], cr, a); D[j + 1] = Ops.Lerp(D[j + 1], cg, a); D[j + 2] = Ops.Lerp(D[j + 2], cb, a);
                }
            });
            return o;
        }
    }
}

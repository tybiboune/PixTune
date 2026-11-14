using System;
using System.Threading.Tasks;

namespace PixTune
{
    // Film stock emulation and the film-type effects (bloom, halation, grain, dehaze, colour bands).
    public class FilmStock
    {
        public string Name;
        public Grade G;
        public bool Mono;
        public float[] MonoMix = { 0.3f, 0.59f, 0.11f };
        public float[] Mix;            // optional 3x3 channel mix (row major)
        public float Grain = 0.2f, GrainSize = 1.6f, Halation, Vignette;
        public FilmStock(string name, Grade g) { Name = name; G = g; }
    }

    public static class Film
    {
        static FilmStock S(string n, Grade g, float grain, float gsize, float hal, float vig)
        {
            FilmStock f = new FilmStock(n, g); f.Grain = grain; f.GrainSize = gsize; f.Halation = hal; f.Vignette = vig; return f;
        }
        static FilmStock M(string n, float[] mix, float con, float bright, float fade, float grain, float gsize)
        {
            FilmStock f = new FilmStock(n, new Grade(0, 0, 0, 0, 0, 0, fade, 1, con, bright));
            f.Mono = true; f.MonoMix = mix; f.Grain = grain; f.GrainSize = gsize; return f;
        }

        public static readonly FilmStock[] Stocks = Build();

        static FilmStock[] Build()
        {
            FilmStock tech = S("Technicolor", new Grade(-0.02f, 0.01f, 0.03f, 0.03f, 0.01f, -0.02f, 0.02f, 1.15f, 0.22f, 0), 0.12f, 1.4f, 0.1f, 0.1f);
            tech.Mix = new float[] { 1.25f, -0.15f, -0.10f, -0.10f, 1.20f, -0.10f, -0.05f, -0.20f, 1.25f };
            FilmStock velvia = S("Fujifilm Velvia 50", new Grade(0.0f, 0.0f, 0.02f, 0.01f, 0.0f, -0.01f, 0.0f, 1.45f, 0.3f, -0.03f), 0.08f, 1.2f, 0, 0.12f);
            velvia.Mix = new float[] { 1.08f, -0.04f, -0.04f, -0.05f, 1.10f, -0.05f, -0.03f, -0.05f, 1.08f };
            FilmStock kodachrome = S("Kodachrome 64", new Grade(0.02f, 0.0f, -0.01f, 0.03f, 0.01f, -0.03f, 0.0f, 1.2f, 0.28f, -0.04f), 0.1f, 1.3f, 0, 0.1f);
            kodachrome.Mix = new float[] { 1.12f, -0.07f, -0.05f, -0.03f, 1.05f, -0.02f, -0.02f, -0.08f, 1.10f };
            return new FilmStock[] {
                S("Kodak Portra 160", new Grade(0.02f, 0.01f, -0.01f, 0.03f, 0.02f, -0.02f, 0.04f, 0.88f, -0.05f, 0.03f), 0.12f, 1.3f, 0.05f, 0.05f),
                S("Kodak Portra 400", new Grade(0.03f, 0.01f, -0.02f, 0.04f, 0.02f, -0.03f, 0.05f, 0.9f, 0.0f, 0.04f), 0.2f, 1.5f, 0.08f, 0.06f),
                S("Kodak Portra 800", new Grade(0.03f, 0.0f, -0.01f, 0.05f, 0.02f, -0.03f, 0.06f, 0.95f, 0.05f, 0.02f), 0.32f, 1.8f, 0.12f, 0.08f),
                S("Kodak Gold 200", new Grade(0.04f, 0.02f, -0.04f, 0.06f, 0.04f, -0.06f, 0.04f, 1.12f, 0.1f, 0.03f), 0.22f, 1.6f, 0.1f, 0.1f),
                S("Kodak Ultramax 400", new Grade(0.04f, 0.01f, -0.03f, 0.05f, 0.03f, -0.05f, 0.03f, 1.18f, 0.16f, 0.02f), 0.28f, 1.7f, 0.1f, 0.1f),
                S("Kodak Ektar 100", new Grade(-0.01f, 0.0f, 0.02f, 0.03f, 0.01f, -0.02f, 0.0f, 1.3f, 0.2f, 0), 0.08f, 1.2f, 0.04f, 0.06f),
                S("Kodak E200", new Grade(-0.02f, 0.0f, 0.04f, 0.0f, 0.01f, 0.01f, 0.0f, 1.15f, 0.3f, -0.04f), 0.14f, 1.4f, 0.05f, 0.12f),
                kodachrome,
                S("Kodak Vision3 500T", new Grade(-0.05f, 0.01f, 0.05f, 0.04f, 0.01f, -0.03f, 0.05f, 0.85f, 0.08f, 0), 0.26f, 1.8f, 0.3f, 0.12f),
                M("Kodak Tri-X 400", new float[] { 0.35f, 0.55f, 0.10f }, 0.35f, 0, 0.0f, 0.45f, 2.0f),
                M("Kodak T-Max 100", new float[] { 0.3f, 0.6f, 0.1f }, 0.15f, 0.02f, 0.0f, 0.12f, 1.2f),
                S("Fujifilm Superia 200", new Grade(-0.02f, 0.03f, 0.01f, 0.02f, 0.03f, -0.02f, 0.04f, 1.05f, 0.08f, 0.02f), 0.2f, 1.5f, 0.06f, 0.08f),
                S("Fujifilm Superia 800", new Grade(-0.03f, 0.03f, 0.02f, 0.02f, 0.03f, -0.01f, 0.06f, 1.0f, 0.1f, 0.0f), 0.36f, 1.9f, 0.1f, 0.1f),
                S("Fujifilm Pro 400H", new Grade(-0.02f, 0.03f, 0.03f, 0.01f, 0.03f, 0.01f, 0.08f, 0.82f, -0.08f, 0.06f), 0.2f, 1.5f, 0.04f, 0.04f),
                S("Fujifilm Pro 160NS", new Grade(-0.01f, 0.02f, 0.02f, 0.01f, 0.02f, 0.0f, 0.06f, 0.85f, -0.06f, 0.05f), 0.12f, 1.3f, 0.03f, 0.04f),
                S("Fujicolor C200", new Grade(-0.01f, 0.03f, 0.0f, 0.03f, 0.03f, -0.03f, 0.05f, 1.0f, 0.06f, 0.03f), 0.24f, 1.6f, 0.06f, 0.08f),
                velvia,
                S("Fujifilm Provia 100F", new Grade(0.0f, 0.0f, 0.01f, 0.0f, 0.0f, 0.0f, 0.0f, 1.12f, 0.18f, 0), 0.08f, 1.2f, 0, 0.06f),
                M("Fujifilm Acros 100", new float[] { 0.28f, 0.6f, 0.12f }, 0.2f, 0.02f, 0.0f, 0.1f, 1.1f),
                S("Agfa Optima 200", new Grade(0.04f, 0.0f, -0.02f, 0.05f, 0.02f, -0.03f, 0.06f, 1.05f, 0.08f, 0.02f), 0.24f, 1.6f, 0.08f, 0.1f),
                S("Agfa Vista 200", new Grade(0.05f, -0.01f, -0.01f, 0.04f, 0.02f, -0.04f, 0.04f, 1.15f, 0.12f, 0.02f), 0.24f, 1.6f, 0.08f, 0.1f),
                M("Agfa Scala 200", new float[] { 0.32f, 0.58f, 0.10f }, 0.45f, -0.02f, 0.0f, 0.22f, 1.5f),
                M("Ilford HP5 Plus 400", new float[] { 0.3f, 0.58f, 0.12f }, 0.28f, 0.0f, 0.02f, 0.4f, 1.9f),
                M("Ilford Delta 3200", new float[] { 0.33f, 0.56f, 0.11f }, 0.3f, 0.03f, 0.05f, 0.75f, 2.6f),
                M("Ilford FP4 Plus 125", new float[] { 0.3f, 0.6f, 0.1f }, 0.18f, 0.0f, 0.0f, 0.16f, 1.3f),
                S("Polaroid 600", new Grade(-0.03f, 0.02f, 0.04f, 0.05f, 0.03f, -0.02f, 0.14f, 0.82f, 0.12f, 0.04f), 0.14f, 1.4f, 0.05f, 0.22f),
                S("Polaroid SX-70", new Grade(0.04f, 0.02f, -0.02f, 0.07f, 0.04f, -0.04f, 0.16f, 0.7f, 0.05f, 0.03f), 0.12f, 1.4f, 0.06f, 0.25f),
                S("Polaroid 669", new Grade(-0.03f, 0.02f, 0.05f, 0.02f, 0.03f, 0.02f, 0.18f, 0.72f, 0.0f, 0.04f), 0.14f, 1.4f, 0.04f, 0.2f),
                tech,
                S("CineStill 800T", new Grade(-0.06f, 0.01f, 0.06f, 0.02f, 0.01f, -0.02f, 0.04f, 0.92f, 0.1f, 0), 0.32f, 1.9f, 0.75f, 0.1f),
                S("Lomography 400", new Grade(0.0f, 0.03f, 0.04f, 0.06f, 0.04f, -0.05f, 0.02f, 1.3f, 0.25f, -0.02f), 0.3f, 1.8f, 0.1f, 0.35f),
            };
        }

        // ---------- GPU versions of the shared pieces (used inside a Gpu job; frame: the job's input picture) ----------
        public static Gpu.GB GlowGpu(Gpu.Job j, Gpu.GB o, Img frame, float threshold, float radius, float strength, float[] tint, bool edgeOnly)
        {
            float th = Math.Min(0.98f, threshold);
            float[] p = { th, th + (1 - th) * 0.6f + 0.02f, tint != null ? tint[0] : 0, tint != null ? tint[1] : 0, tint != null ? tint[2] : 0 };
            Gpu.GB bright = j.K("GlowBright", 3, p, new int[] { tint != null ? 1 : 0 }, o);
            float sig = Math.Max(1f, radius * frame.Scale);
            Gpu.GB g = j.FastGaussRgb(bright, sig);
            if (edgeOnly)
            {
                Gpu.GB inner = j.FastGaussRgb(bright, sig * 0.25f);
                Gpu.GB e = j.K("GlowEdge", 3, null, null, g, inner);
                j.Free(g); j.Free(inner);
                g = e;
            }
            j.Free(bright);
            Gpu.GB r = j.K("GlowScreen", 3, new float[] { strength * 2.2f }, null, o, g);
            j.Free(g);
            return r;
        }

        public static Gpu.GB AddGrainGpu(Gpu.Job j, Gpu.GB o, Img frame, float amount, float size, int seed, bool mono)
        {
            if (amount <= 0) return o;
            float sz = Math.Max(0.6f, size * frame.Scale);
            Gpu.GB g = j.Grain(sz, seed), gc = mono ? null : j.Grain(sz, seed + 5);
            Gpu.GB r = j.K("GrainAdd", 3, new float[] { amount }, new int[] { mono ? 0 : 1 }, o, g, gc ?? g);
            j.Free(g); j.Free(gc);
            return r;
        }

        // 1 - amount * smoothstep(e0, e1, distance from (cx, cy) / half diagonal)
        public static Gpu.GB VigMulGpu(Gpu.Job j, Gpu.GB o, Img frame, float amount, float cx, float cy, float e0, float e1)
        {
            if (amount == 0) return o;
            return j.K("VigMul", 3, new float[] { frame.PX(cx), frame.PY(cy), frame.Diagonal * 0.5f, amount, e0, e1 }, null, o);
        }

        // a grade's settings as the shaders read them
        public static float[] GradeValues(Grade g)
        {
            return new float[] { g.Sh[0], g.Sh[1], g.Sh[2], g.Hi[0], g.Hi[1], g.Hi[2], g.Fade, g.Sat, g.Con, g.Bright, (float)Math.Pow(2, -g.Bright) };
        }

        // Bright-area glow, the base of bloom and halation. tint null = keep colour.
        // false builds a glow at full size (slower, and the yardstick the smaller one is measured against)
        public static bool GlowSmall = true;

        public static Img Glow(Img src, float threshold, float radius, float strength, float[] tint, bool edgeOnly)
        {
            Img gpu = Gpu.RunJob(src, j => GlowGpu(j, j.Source, src, threshold, radius, strength, tint, edgeOnly));
            if (gpu != null) return gpu;
            int w = src.W, h = src.H;
            float[] S = src.D;
            float th = Math.Min(0.98f, threshold);
            float sig = Math.Max(1f, radius * src.Scale);
            // A glow is a wide blur of the brightest parts: it holds nothing fine, so it is built on a copy
            // shrunk by ds and stretched back, which is where nearly all of this tool's time used to go. The
            // blocks are counted from the photo's own corner (FX, FY), so a part of the photo rendered on its
            // own lines up with the whole one. A narrow glow (ds = 1) is still built at full size. Where the
            // picture runs out, a blur of block averages and a full-size blur part company by about a level over
            // the outermost pixels: inside a part that band is thrown away with the margin, and at the edge of the
            // photo it is one approximation of a wide glow against another.
            int ds = 1; while (GlowSmall && ds < 4 && sig / (ds * 2) >= 8f) ds *= 2;
            while (ds > 1 && (src.FX % ds != 0 || src.FY % ds != 0)) ds /= 2;   // a part whose corner does not land on the grid
            int sw = (w + ds - 1) / ds, sh = (h + ds - 1) / ds;
            Img bright = new Img(sw, sh);
            float[] B = bright.D;
            Ops.For(0, sh, by =>
            {
                int y0 = by * ds, y1 = Math.Min(y0 + ds, h);
                for (int bx = 0; bx < sw; bx++)
                {
                    int x0 = bx * ds, x1 = Math.Min(x0 + ds, w);
                    float sr = 0, sg = 0, sb = 0; int n = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int j = (y * w + x) * 3;
                            float l = Ops.Lum(S[j], S[j + 1], S[j + 2]);
                            float k = Ops.SmoothStep(th, th + (1 - th) * 0.6f + 0.02f, l);
                            if (tint != null) { sr += l * k * tint[0]; sg += l * k * tint[1]; sb += l * k * tint[2]; }
                            else { sr += S[j] * k; sg += S[j + 1] * k; sb += S[j + 2] * k; }
                            n++;
                        }
                    int o2 = (by * sw + bx) * 3;
                    B[o2] = sr / n; B[o2 + 1] = sg / n; B[o2 + 2] = sb / n;
                }
            });
            Img g = Ops.FastGaussImg(bright, sig / ds);
            if (edgeOnly)
            {
                // halation sits around the light source, not on top of it
                Img inner = Ops.FastGaussImg(bright, sig * 0.25f / ds);
                float[] gd = g.D, idd = inner.D;
                for (int i = 0; i < gd.Length; i++) gd[i] = Math.Max(0, gd[i] * 1.6f - idd[i] * 0.8f);
                inner.Recycle();
            }
            bright.Recycle();
            Img o = src.Clone();
            float[] D = o.D, G = g.D;
            float mul = strength * 2.2f;
            if (ds == 1)
            {
                Ops.For(0, h, y =>
                {
                    int a = y * w * 3, e = a + w * 3;
                    for (int j = a; j < e; j++) D[j] = Ops.Screen(Ops.C01(D[j]), Ops.C01(G[j] * mul));
                });
                g.Recycle();
                return o;
            }
            // the glow stretched back, the weights of each column worked out once
            float inv = 1f / ds;
            int[] cx0 = new int[w], cx1 = new int[w]; float[] cax = new float[w];
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) * inv - 0.5f;
                if (fx < 0) fx = 0; else if (fx > sw - 1) fx = sw - 1;
                int x0 = (int)fx; cx0[x] = x0; cx1[x] = x0 + 1 < sw ? x0 + 1 : x0; cax[x] = fx - x0;
            }
            Ops.For(0, h, y =>
            {
                float fy = (y + 0.5f) * inv - 0.5f;
                if (fy < 0) fy = 0; else if (fy > sh - 1) fy = sh - 1;
                int y0 = (int)fy, y1 = y0 + 1 < sh ? y0 + 1 : y0;
                float ay = fy - y0;
                int r0 = y0 * sw, r1 = y1 * sw, row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = cx0[x], x1 = cx1[x]; float ax = cax[x];
                    int a00 = (r0 + x0) * 3, a01 = (r0 + x1) * 3, a10 = (r1 + x0) * 3, a11 = (r1 + x1) * 3;
                    int j = (row + x) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float gv = (G[a00 + c] * (1 - ax) + G[a01 + c] * ax) * (1 - ay) + (G[a10 + c] * (1 - ax) + G[a11 + c] * ax) * ay;
                        D[j + c] = Ops.Screen(Ops.C01(D[j + c]), Ops.C01(gv * mul));
                    }
                }
            });
            g.Recycle();
            return o;
        }

        public static Img ApplyFilm(Img src, Step s)
        {
            FilmStock fs = Stocks[Math.Max(0, Math.Min(Stocks.Length - 1, s.I("style", 0)))];
            float k = s.F("strength", 100) / 100f;
            float grain = s.F("grain", 50) / 50f, hal = s.F("halation", 50) / 50f;
            float br = s.F("brightness") / 100f, wa = s.F("warmth") / 100f;
            Img gpu = Gpu.RunJob(src, j =>
            {
                float[] v = new float[23];
                Array.Copy(GradeValues(fs.G), v, 11);
                if (fs.Mix != null) Array.Copy(fs.Mix, 0, v, 11, 9);
                Array.Copy(fs.MonoMix, 0, v, 20, 3);
                Gpu.GB c = j.K("FilmColor", 3, new float[] { k, (float)Math.Pow(2, -br), wa },
                    new int[] { fs.Mix != null ? 1 : 0, fs.Mono ? 1 : 0, br != 0 ? 1 : 0 }, j.Source, j.Values(v));
                if (fs.Halation * hal * k > 0.01f && !fs.Mono)
                    c = j.Then(c, GlowGpu(j, c, src, 0.72f, 14, fs.Halation * hal * k * 0.6f, new float[] { 1f, 0.28f, 0.12f }, true));
                if (fs.Vignette > 0) c = j.Then(c, VigMulGpu(j, c, src, fs.Vignette * k, 0.5f, 0.5f, 0.55f, 1.3f));
                return j.Then(c, AddGrainGpu(j, c, src, fs.Grain * grain * k, fs.GrainSize, 777 + s.I("style", 0), fs.Mono));
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            float gamma = (float)Math.Pow(2, -br);
            float[] gt = Ops.PowTable(gamma);
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int j = (y * o.W + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float r0 = r, g0 = g, b0 = b;
                    if (fs.Mix != null)
                    {
                        float[] m = fs.Mix;
                        float nr = m[0] * r + m[1] * g + m[2] * b, ng = m[3] * r + m[4] * g + m[5] * b, nb = m[6] * r + m[7] * g + m[8] * b;
                        r = nr; g = ng; b = nb;
                    }
                    if (fs.Mono)
                    {
                        float v = r * fs.MonoMix[0] + g * fs.MonoMix[1] + b * fs.MonoMix[2];
                        r = g = b = v;
                    }
                    fs.G.Apply(ref r, ref g, ref b, 1f);
                    // film toe and shoulder
                    r = Ops.Lerp(r, Ops.SmoothStep(-0.05f, 1.05f, r), 0.25f);
                    g = Ops.Lerp(g, Ops.SmoothStep(-0.05f, 1.05f, g), 0.25f);
                    b = Ops.Lerp(b, Ops.SmoothStep(-0.05f, 1.05f, b), 0.25f);
                    r = Ops.Lerp(r0, r, k); g = Ops.Lerp(g0, g, k); b = Ops.Lerp(b0, b, k);
                    if (br != 0) { r = Ops.PowAt(gt, r, gamma); g = Ops.PowAt(gt, g, gamma); b = Ops.PowAt(gt, b, gamma); }
                    if (wa != 0) { float l0 = Ops.Lum(r, g, b); r += wa * 0.09f; b -= wa * 0.1f; float d = l0 - Ops.Lum(r, g, b); r += d; g += d; b += d; }
                    D[j] = r; D[j + 1] = g; D[j + 2] = b;
                }
            });
            if (fs.Halation * hal * k > 0.01f && !fs.Mono)
            {
                Img was = o;
                o = Glow(o, 0.72f, 14, fs.Halation * hal * k * 0.6f, new float[] { 1f, 0.28f, 0.12f }, true);
                if (o != was) was.Recycle();     // the halation works on a copy; the picture it started from is done
            }
            if (fs.Vignette > 0)
            {
                float hd = o.Diagonal * 0.5f, px = o.PX(0.5), py = o.PY(0.5);
                float vk = fs.Vignette * k;
                Ops.For(0, o.H, y =>
                {
                    for (int x = 0; x < o.W; x++)
                    {
                        float dx = x - px, dy = y - py;
                        float m = 1 - vk * Ops.SmoothStep(0.55f, 1.3f, (float)Math.Sqrt(dx * dx + dy * dy) / hd);
                        int j = (y * o.W + x) * 3; o.D[j] *= m; o.D[j + 1] *= m; o.D[j + 2] *= m;
                    }
                });
            }
            AddGrain(o, fs.Grain * grain * k, fs.GrainSize, 777 + s.I("style", 0), fs.Mono);
            return o;
        }

        public static void AddGrain(Img o, float amount, float size, int seed, bool mono)
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

        public static Img Bloom(Img src, Step s)
        {
            return Glow(src, s.F("threshold", 60) / 100f, 6 + s.F("radius", 50) * 0.5f, s.F("strength", 50) / 100f, null, false);
        }

        public static Img Halation(Img src, Step s)
        {
            float hue = s.F("hue") / 100f; // -1 orange .. 0 red .. 1 magenta
            float[] tint = { 1f, Math.Max(0.05f, 0.25f - hue * 0.2f), Math.Max(0.05f, 0.12f + hue * 0.35f) };
            return Glow(src, s.F("threshold", 65) / 100f, 4 + s.F("radius", 40) * 0.35f, s.F("strength", 50) / 100f, tint, true);
        }

        public static Img GrainTool(Img src, Step s)
        {
            Img gpu = Gpu.RunPointJob(src, j => AddGrainGpu(j, j.Source, src, s.F("amount", 40) / 100f * 1.4f, 0.8f + s.F("size", 40) / 100f * 3f, 4242, s.I("mono", 0) == 1));
            if (gpu != null) return gpu;
            Img o = src.Clone();
            AddGrain(o, s.F("amount", 40) / 100f * 1.4f, 0.8f + s.F("size", 40) / 100f * 3f, 4242, s.I("mono", 0) == 1);
            return o;
        }

        // the side of the copy the haze colour is read from (int.MaxValue reads every pixel)
        public static int HazeSide = 1024;

        // Dark channel prior: positive removes haze, negative adds it.
        public static Img Dehaze(Img src, Step s)
        {
            float amt = s.F("amount", 0) / 100f;
            if (amt == 0) return src.Clone();
            int w = src.W, h = src.H;
            float[] dark = Pool.Rent(w * h);
            float[] D = src.D;
            Ops.For(0, h, y =>
            {
                for (int i = y * w, e = i + w; i < e; i++) dark[i] = Math.Min(D[i * 3], Math.Min(D[i * 3 + 1], D[i * 3 + 2]));
            });
            // Atmospheric light: the mean colour of the haziest 0.2% of the photo. It is an average over a fifth
            // of a percent of the picture, so a small copy gives the same colour: ranking all 24 million values
            // of a full-size photo took 3.4 s of every slider move, and no slider changes this colour.
            Img ai = src.W > HazeSide || src.H > HazeSide ? src.FitInside(HazeSide) : src;
            float[] AD = ai.D;
            float[] adark = new float[ai.W * ai.H];
            for (int i = 0; i < adark.Length; i++) adark[i] = Math.Min(AD[i * 3], Math.Min(AD[i * 3 + 1], AD[i * 3 + 2]));
            float[] sorted = (float[])adark.Clone();
            Array.Sort(sorted);
            float lim = sorted[(int)(sorted.Length * 0.998)];
            double ar = 0, ag = 0, ab = 0; int n = 0;
            for (int i = 0; i < adark.Length; i++) if (adark[i] >= lim) { ar += AD[i * 3]; ag += AD[i * 3 + 1]; ab += AD[i * 3 + 2]; n++; }
            float Ar = (float)(ar / n), Ag = (float)(ag / n), Ab = (float)(ab / n);
            float Am = Math.Max(0.3f, (Ar + Ag + Ab) / 3);
            // local minimum, then refined along edges
            float[] mn = Ops.Box(dark, w, h, Math.Max(2, (int)(7 * src.Scale)));
            Ops.For(0, h, y =>
            {
                for (int i = y * w, e = i + w; i < e; i++) mn[i] = Math.Min(mn[i], dark[i]);
            });
            float[] L = Ops.Luma(src);
            float[] t = Ops.Guided(L, mn, w, h, Math.Max(4, (int)(20 * src.Scale)), 0.001f);
            Img o = src.Clone();
            float[] O = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    if (amt > 0)
                    {
                        float tr = Math.Max(0.15f, 1 - 0.92f * amt * t[i] / Am);
                        O[j] = (D[j] - Ar) / tr + Ar; O[j + 1] = (D[j + 1] - Ag) / tr + Ag; O[j + 2] = (D[j + 2] - Ab) / tr + Ab;
                    }
                    else
                    {
                        float k = -amt * 0.6f * (0.4f + 0.6f * L[i]);
                        O[j] = Ops.Lerp(D[j], Ar, k); O[j + 1] = Ops.Lerp(D[j + 1], Ag, k); O[j + 2] = Ops.Lerp(D[j + 2], Ab, k);
                    }
                }
            });
            Ops.Done(dark, mn, L, t);
            if (ai != src) ai.Recycle();
            return o;
        }

        // ---------------- Color (per-band hue / saturation / luminance) ----------------
        public static readonly string[] Bands = { "red", "orange", "yellow", "green", "aqua", "blue", "purple", "magenta" };
        static readonly float[] centers = { 0, 30, 60, 120, 180, 230, 275, 320 };

        static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            v = mx; s = mx > 1e-5f ? d / mx : 0;
            if (d < 1e-5f) { h = 0; return; }
            if (mx == r) h = 60 * (((g - b) / d) % 6);
            else if (mx == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            h = ((h % 360) + 360) % 360;
            float c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            float rr, gg, bb;
            if (h < 60) { rr = c; gg = x; bb = 0; } else if (h < 120) { rr = x; gg = c; bb = 0; }
            else if (h < 180) { rr = 0; gg = c; bb = x; } else if (h < 240) { rr = 0; gg = x; bb = c; }
            else if (h < 300) { rr = x; gg = 0; bb = c; } else { rr = c; gg = 0; bb = x; }
            r = rr + m; g = gg + m; b = bb + m;
        }

        public static Img ColorBands(Img src, Step s)
        {
            int nb = Bands.Length;
            float[] dh = new float[nb], ds = new float[nb], dl = new float[nb];
            bool any = false;
            for (int i = 0; i < nb; i++)
            {
                dh[i] = s.F("h_" + Bands[i]) / 100f * 30f;
                ds[i] = s.F("s_" + Bands[i]) / 100f;
                dl[i] = s.F("l_" + Bands[i]) / 100f;
                any |= dh[i] != 0 || ds[i] != 0 || dl[i] != 0;
            }
            if (!any) return src.Clone();
            Img gpu = Gpu.RunPointJob(src, j =>
            {
                float[] v = new float[24];
                Array.Copy(dh, 0, v, 0, 8); Array.Copy(ds, 0, v, 8, 8); Array.Copy(dl, 0, v, 16, 8);
                return j.K("ColorBands", 3, null, null, j.Source, j.Values(v));
            });
            if (gpu != null) return gpu;
            Img o = src.Clone();
            float[] D = o.D;
            Ops.For(0, o.H, y =>
            {
                for (int x = 0; x < o.W; x++)
                {
                    int j = (y * o.W + x) * 3;
                    float r = D[j], g = D[j + 1], b = D[j + 2];
                    float hh, ss, vv;
                    RgbToHsv(Ops.C01(r), Ops.C01(g), Ops.C01(b), out hh, out ss, out vv);
                    if (ss < 0.02f) continue;
                    float sh = 0, sf = 0, sl = 0, wsum = 0;
                    for (int i = 0; i < nb; i++)
                    {
                        float d = Math.Abs(hh - centers[i]); if (d > 180) d = 360 - d;
                        float half = (i == 0 || i == 1 || i == 2) ? 30 : 45;
                        float wgt = Math.Max(0, 1 - d / half);
                        wgt = wgt * wgt * (3 - 2 * wgt);
                        if (wgt <= 0) continue;
                        sh += dh[i] * wgt; sf += ds[i] * wgt; sl += dl[i] * wgt; wsum += wgt;
                    }
                    if (wsum <= 0) continue;
                    float k = Math.Min(1f, ss * 2.5f); // greys stay untouched
                    float ns = Ops.C01(ss * (1 + sf * k));
                    float r2, g2, b2;
                    HsvToRgb(hh + sh * k, ns, vv, out r2, out g2, out b2);
                    float l0 = Ops.Lum(r2, g2, b2);
                    Ops.SetLum(ref r2, ref g2, ref b2, l0, Math.Max(0, l0 * (1 + sl * 0.6f * k)));
                    D[j] = r2; D[j + 1] = g2; D[j + 2] = b2;
                }
            });
            return o;
        }
    }
}

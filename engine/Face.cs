using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace PixTune
{
    public static class Face
    {
        // Faces are stored as "x,y,w,h;..." normalized boxes (from the Windows face detector or placed by hand).
        public static List<float[]> ParseFaces(string s)
        {
            List<float[]> l = new List<float[]>();
            if (string.IsNullOrEmpty(s)) return l;
            foreach (string p in s.Split(';'))
            {
                string[] a = p.Split(',');
                if (a.Length < 4) continue;
                float[] v = new float[4];
                bool ok = true;
                for (int i = 0; i < 4; i++) ok &= float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                if (ok && v[2] > 0 && v[3] > 0) l.Add(v);
            }
            return l;
        }

        static float SkinScore(float r, float g, float b)
        {
            float cb = 128 - 37.797f * r - 74.203f * g + 112f * b;
            float cr = 128 + 112f * r - 93.786f * g - 18.214f * b;
            float dcb = (cb - 110f) / 16f, dcr = (cr - 152f) / 13f;
            float l = Ops.Lum(r, g, b);
            float lw = Ops.SmoothStep(0.06f, 0.18f, l) * (1 - Ops.SmoothStep(0.94f, 1f, l));
            return (float)Math.Exp(-(dcb * dcb + dcr * dcr) * 0.5f) * lw;
        }

        // Ellipse weight around each face (1 inside, falling off outside), max over faces.
        static float[] FaceRegion(Img src, List<float[]> faces, float grow, float soft)
        {
            int w = src.W, h = src.H;
            float[] m = new float[w * h];
            foreach (float[] f in faces)
            {
                float cx = (f[0] + f[2] / 2) * w, cy = (f[1] + f[3] * 0.55f) * h;
                float rx = f[2] * w * 0.5f * grow, ry = f[3] * h * 0.6f * grow;
                Ops.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x - cx) / rx, dy = (y - cy) / ry;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        float v = 1 - Ops.SmoothStep(1f, 1f + soft, d);
                        int i = y * w + x;
                        if (v > m[i]) m[i] = v;
                    }
                });
            }
            return m;
        }

        public static float[] SkinMask(Img src, List<float[]> faces)
        {
            int w = src.W, h = src.H;
            float[] reg = faces.Count > 0 ? FaceRegion(src, faces, 1.5f, 0.6f) : null;
            float[] m = new float[w * h];
            float[] D = src.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float sk = SkinScore(D[j], D[j + 1], D[j + 2]);
                    m[i] = reg != null ? sk * reg[i] : sk;
                }
            });
            return Ops.Gauss(m, w, h, Math.Max(1f, 2.5f * src.Scale));
        }

        public static Img SmoothSkin(Img src, float faceSizePx)
        {
            int r = Math.Max(2, (int)(faceSizePx * 0.035f));
            float[][] c = Ops.Split(src);
            float[][] o = new float[3][];
            for (int k = 0; k < 3; k++) o[k] = Ops.Guided(c[k], c[k], src.W, src.H, r, 0.0028f);
            return Ops.Merge(o, src.W, src.H);
        }

        static float TypicalFace(Img src, List<float[]> faces)
        {
            if (faces.Count == 0) return src.MinDim * 0.3f;
            float s = 0; foreach (float[] f in faces) s += f[2] * src.W;
            return s / faces.Count;
        }

        // Local clarity for eyes: weighted local contrast, a touch of sharpening and brightening.
        static void EyeClarity(Img o, Img src, float[] wgt, float amount, float faceSize)
        {
            if (amount <= 0) return;
            int w = src.W, h = src.H;
            float[] L = Ops.Luma(src);
            float[] bl = Ops.Gauss(L, w, h, Math.Max(1f, faceSize * 0.012f));
            float[] bs = Ops.Gauss(L, w, h, Math.Max(0.7f, faceSize * 0.004f));
            float[] D = o.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = wgt[i] * amount;
                    if (a < 0.002f) continue;
                    float d = (L[i] - bl[i]) * 2.4f + (L[i] - bs[i]) * 2.0f + 0.04f;
                    int j = i * 3;
                    D[j] += d * a; D[j + 1] += d * a; D[j + 2] += d * a;
                }
            });
        }

        static float[] EyeWeights(Img src, List<float[]> faces)
        {
            int w = src.W, h = src.H;
            float[] m = new float[w * h];
            foreach (float[] f in faces)
            {
                float fw = f[2] * w;
                float[][] eyes = { new float[] { (f[0] + f[2] * 0.31f) * w, (f[1] + f[3] * 0.40f) * h }, new float[] { (f[0] + f[2] * 0.69f) * w, (f[1] + f[3] * 0.40f) * h } };
                float rr = fw * 0.13f;
                foreach (float[] e in eyes)
                {
                    int x0 = Math.Max(0, (int)(e[0] - rr * 2)), x1 = Math.Min(w - 1, (int)(e[0] + rr * 2));
                    int y0 = Math.Max(0, (int)(e[1] - rr * 2)), y1 = Math.Min(h - 1, (int)(e[1] + rr * 2));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float dx = (x - e[0]) / rr, dy = (y - e[1]) / (rr * 0.7f);
                            float v = (float)Math.Exp(-(dx * dx + dy * dy) * 0.7f);
                            if (v > m[y * w + x]) m[y * w + x] = v;
                        }
                }
            }
            return m;
        }

        // ---------------- Portrait ----------------
        public static Img Portrait(Img src, Step s)
        {
            List<float[]> faces = ParseFaces(s.Str("faces", ""));
            float spot = s.F("spotlight", 25) / 100f, smooth = s.F("smoothing", 25) / 100f, eyes = s.F("eyes", 25) / 100f;
            int tone = s.I("skintone", 0);
            float fs = TypicalFace(src, faces);
            int w = src.W, h = src.H;
            Img o = src.Clone();
            float[] skin = SkinMask(src, faces);
            if (smooth > 0)
            {
                Img sm = SmoothSkin(src, fs);
                for (int i = 0; i < o.N; i++)
                {
                    float a = Math.Min(1f, skin[i] * 1.4f) * smooth * 0.92f;
                    for (int c = 0; c < 3; c++) o.D[i * 3 + c] = Ops.Lerp(o.D[i * 3 + c], sm.D[i * 3 + c], a);
                }
            }
            if (tone != 0)
            {
                // skin tone presets: 1 pale, 2 light, 3 medium/warm, 4 deep, 5 rosy
                float[] tr = { 0, 0.02f, 0.04f, 0.05f, 0.0f, 0.05f }, tg = { 0, 0.01f, 0.015f, 0.0f, -0.03f, -0.01f }, tb = { 0, 0.03f, -0.01f, -0.04f, -0.06f, 0.01f };
                float[] tl = { 0, 0.06f, 0.03f, 0, -0.05f, 0.01f };
                int t = Math.Max(0, Math.Min(5, tone));
                for (int i = 0; i < o.N; i++)
                {
                    float a = skin[i];
                    o.D[i * 3] += (tr[t] + tl[t]) * a; o.D[i * 3 + 1] += (tg[t] + tl[t]) * a; o.D[i * 3 + 2] += (tb[t] + tl[t]) * a;
                }
            }
            if (spot > 0)
            {
                float[] reg = faces.Count > 0 ? FaceRegion(src, faces, 1.25f, 1.2f) : null;
                float cx = w / 2f, cy = h * 0.42f, rr = src.MinDim * 0.45f;
                Ops.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        float m;
                        if (reg != null) m = reg[i];
                        else { float dx = (x - cx) / rr, dy = (y - cy) / rr; m = 1 - Ops.SmoothStep(0.5f, 1.4f, (float)Math.Sqrt(dx * dx + dy * dy)); }
                        float k = 1 + spot * 0.32f * m - spot * 0.28f * (1 - m);
                        o.D[i * 3] *= k; o.D[i * 3 + 1] *= k; o.D[i * 3 + 2] *= k;
                    }
                });
            }
            if (eyes > 0 && faces.Count > 0) EyeClarity(o, src, EyeWeights(src, faces), eyes, fs);
            return o;
        }

        // ---------------- Head Pose ----------------
        // AI mode ranges at full slider: degrees of pitch and yaw, and LivePortrait's smile amount
        public static float AiPitch = 15f, AiYaw = 20f, AiSmile = 1.1f;

        public static Img HeadPose(Img src, Step s)
        {
            List<float[]> faces = ParseFaces(s.Str("faces", ""));
            if (faces.Count == 0) return src.Clone();
            float hr = s.F("hrot") / 100f, vr = s.F("vrot") / 100f, pupil = s.F("pupil") / 100f, smile = s.F("smile") / 100f, focal = s.F("focal") / 100f;
            Img cur = src;
            foreach (float[] f in faces)
            {
                // a 3D face mesh fitted on the photo when the model can run (FaceMesh); the simple warp below otherwise
                float[] lm = FaceMesh.Landmarks(cur, f);
                // AI mode: LivePortrait re-renders turn, tilt and smile; pupils and focal length then use the mesh
                if (lm != null && s.I("ai", 0) == 1 && (hr != 0 || vr != 0 || smile != 0) && LivePortrait.Available)
                {
                    Img a = cur.Clone();
                    // LivePortrait's pitch is positive towards the chest
                    if (LivePortrait.Pose(cur, a, lm, -vr * AiPitch, hr * AiYaw, smile * AiSmile))
                    {
                        cur = a;
                        if (pupil == 0 && focal == 0) continue;
                        float[] lm2 = FaceMesh.Landmarks(cur, f);
                        if (lm2 != null) { Img m2 = cur.Clone(); if (FaceMesh.Pose(cur, m2, lm2, 0, 0, 0, pupil, focal)) cur = m2; }
                        continue;
                    }
                }
                if (lm != null)
                {
                    Img m = cur.Clone();
                    if (FaceMesh.Pose(cur, m, lm, hr, vr, smile, pupil, focal)) { cur = m; continue; }
                }
                int w = cur.W, h = cur.H;
                float cx = (f[0] + f[2] / 2) * w, cy = (f[1] + f[3] / 2) * h;
                float rx = f[2] * w * 0.62f, ry = f[3] * h * 0.75f;
                float[][] eyes = { new float[] { (f[0] + f[2] * 0.31f) * w, (f[1] + f[3] * 0.40f) * h }, new float[] { (f[0] + f[2] * 0.69f) * w, (f[1] + f[3] * 0.40f) * h } };
                float er = f[2] * w * 0.075f;
                float[][] mouth = { new float[] { (f[0] + f[2] * 0.30f) * w, (f[1] + f[3] * 0.78f) * h }, new float[] { (f[0] + f[2] * 0.70f) * w, (f[1] + f[3] * 0.78f) * h } };
                float mr = f[2] * w * 0.12f;
                Img o = cur.Clone();
                Img inp = cur;
                Ops.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x - cx) / rx, v = (y - cy) / ry;
                        float d2 = u * u + v * v;
                        if (d2 >= 1.6f) continue;
                        float fall = Ops.SmoothStep(1.6f, 0.4f, d2);
                        float sx = x, sy = y;
                        // turning: content in the middle of the face slides, the outline stays
                        sx -= hr * 0.18f * rx * (1 - Math.Min(1, u * u)) * fall;
                        sy -= vr * 0.14f * ry * (1 - Math.Min(1, v * v)) * fall;
                        // focal length: perspective bulge of the face
                        if (focal != 0)
                        {
                            float k = focal * 0.12f * (1 - Math.Min(1, d2)) * fall;
                            sx = cx + (sx - cx) * (1 + k); sy = cy + (sy - cy) * (1 + k);
                        }
                        if (pupil != 0)
                            foreach (float[] e in eyes)
                            {
                                float ex = (x - e[0]) / er, ey = (y - e[1]) / er;
                                float dd = (float)Math.Sqrt(ex * ex + ey * ey);
                                if (dd < 1)
                                {
                                    float k = 1 - pupil * 0.3f * (1 - dd) * (1 - dd);
                                    sx = e[0] + (sx - e[0]) * k; sy = e[1] + (sy - e[1]) * k;
                                }
                            }
                        if (smile != 0)
                            foreach (float[] m in mouth)
                            {
                                float mx = (x - m[0]) / mr, my = (y - m[1]) / mr;
                                float g = (float)Math.Exp(-(mx * mx + my * my) * 1.2f);
                                sy += smile * 0.28f * mr * g;
                                sx += (m[0] < cx ? 1 : -1) * smile * 0.08f * mr * g;
                            }
                        float r, gg, b; inp.Sample(sx, sy, out r, out gg, out b);
                        int j = (y * w + x) * 3;
                        o.D[j] = r; o.D[j + 1] = gg; o.D[j + 2] = b;
                    }
                });
                cur = o;
            }
            return cur == src ? src.Clone() : cur;
        }

        // ---------------- Face Enhance (brush based) ----------------
        public static Img FaceEnhance(Img src, Step s)
        {
            int w = src.W, h = src.H;
            Img o = src.Clone();
            float fs = src.MinDim * 0.3f;
            if (Raster.Has(s.Strokes, "skin"))
            {
                float[] m = Raster.Channel(s.Strokes, "skin", src, null);
                Img sm = SmoothSkin(src, fs);
                for (int i = 0; i < o.N; i++)
                {
                    float a = Ops.C01(m[i] / 10f) * 0.9f;
                    if (a <= 0) continue;
                    for (int c = 0; c < 3; c++) o.D[i * 3 + c] = Ops.Lerp(o.D[i * 3 + c], sm.D[i * 3 + c], a);
                }
            }
            if (Raster.Has(s.Strokes, "spot"))
            {
                float[] m = Raster.Channel(s.Strokes, "spot", src, null);
                m = Ops.Gauss(m, w, h, 6 * src.Scale);
                for (int i = 0; i < o.N; i++)
                {
                    float k = 1 + Ops.C01(m[i] / 10f) * 0.35f;
                    o.D[i * 3] *= k; o.D[i * 3 + 1] *= k; o.D[i * 3 + 2] *= k;
                }
            }
            if (Raster.Has(s.Strokes, "eyes"))
            {
                float[] m = Raster.Channel(s.Strokes, "eyes", src, null);
                for (int i = 0; i < m.Length; i++) m[i] = Ops.C01(m[i] / 10f);
                EyeClarity(o, src, m, 0.9f, fs * 0.6f);
            }
            return o;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // AI mode of Head Pose: LivePortrait (Guo et al., Kuaishou / Kling, MIT licence; ONNX export by FasterLivePortrait,
    // MIT). A neural network re-renders the face with a new head angle or expression instead of warping its pixels,
    // so a turn can show what was hidden. Follows the official image retargeting pipeline:
    // crop -> appearance features + implicit 3D keypoints -> new pose / smile -> stitching -> warping + SPADE decoder
    // (512 x 512) -> pasted back through LivePortrait's soft face mask.
    public static class LivePortrait
    {
        public static string Dir = "", RuntimePath = "", MaskPath = "", Device = "", LastError = "";
        public const int Crop = 512, Net = 256, Kp = 21;
        const float CropScale = 2.3f, CropShiftY = -0.125f;   // LivePortrait's crop_config defaults
        static volatile bool tried, ok;
        public static volatile bool Working;   // a face is being redrawn (shown in the status bar)
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession appearance, motion, stitching, warping;
        static float[] mask;

        public static bool Installed
        {
            get
            {
                if (Dir.Length == 0 || !File.Exists(RuntimePath) || !File.Exists(MaskPath)) return false;
                foreach (string f in new[] { "appearance_feature_extractor.onnx", "motion_extractor.onnx", "stitching.onnx", "warping_spade.onnx" })
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
                    if (!Installed) throw new Exception("the AI head pose models are not installed");
                    Ort.Init(RuntimePath);
                    appearance = OrtSession.Open(Path.Combine(Dir, "appearance_feature_extractor.onnx"), true, 1);
                    motion = OrtSession.Open(Path.Combine(Dir, "motion_extractor.onnx"), true, 1);
                    stitching = OrtSession.Open(Path.Combine(Dir, "stitching.onnx"), false, 1);
                    // its 3D grid sampling is not supported by DirectML: the CPU runs it
                    warping = OrtSession.Open(Path.Combine(Dir, "warping_spade.onnx"), false, 99);
                    using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(MaskPath))
                    {
                        Img m = Img.FromBitmap(b);
                        if (m.W != Crop || m.H != Crop) m = Ops.Resize(m, Crop, Crop);
                        mask = new float[Crop * Crop];
                        for (int i = 0; i < mask.Length; i++) mask[i] = m.D[i * 3];
                    }
                    Device = appearance.Device + " + CPU";
                    LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // ---------------- source analysis (once per face, cached) ----------------
        class Source
        {
            public Img Img; public string Key;
            public float A, B, Tx, C, D, Ty;      // original -> crop (affine)
            public float[] Feature, Kp, Exp, T, XS; public float Scale, Pitch, Yaw, Roll;
        }
        static readonly List<Source> cache = new List<Source>();
        public static void Forget() { lock (cache) cache.Clear(); }

        static Source Prepare(Img im, float[] L)
        {
            string key = L[33 * 3] + "," + L[33 * 3 + 1] + "," + L[263 * 3] + "," + L[152 * 3 + 1];
            lock (cache) foreach (Source c in cache) if (c.Img == im && c.Key == key) return c;
            Source s = new Source { Img = im, Key = key };
            Align(L, s);
            // the crop (512) then the network input (256)
            Img crop = new Img(Crop, Crop);
            float det = s.A * s.D - s.B * s.C;
            Ops.For(0, Crop, v =>
            {
                for (int u = 0; u < Crop; u++)
                {
                    // crop -> original: inverse of the affine
                    float du = u - s.Tx, dv = v - s.Ty;
                    float x = (s.D * du - s.B * dv) / det, y = (-s.C * du + s.A * dv) / det;
                    float r, g, b; im.Sample(x, y, out r, out g, out b);
                    int i = (v * Crop + u) * 3;
                    crop.D[i] = r; crop.D[i + 1] = g; crop.D[i + 2] = b;
                }
            });
            Img small = Ops.Resize(crop, Net, Net);
            int N = Net * Net;
            float[] x0 = new float[3 * N];
            for (int i = 0; i < N; i++)
                for (int c = 0; c < 3; c++) x0[c * N + i] = Math.Max(0f, Math.Min(1f, small.D[i * 3 + c]));
            Tensor t = new Tensor(x0, 1, 3, Net, Net);
            s.Feature = appearance.Run(t).F;
            s.Pitch = Degrees(motion.Run("pitch", t).F); s.Yaw = Degrees(motion.Run("yaw", t).F); s.Roll = Degrees(motion.Run("roll", t).F);
            s.T = motion.Run("t", t).F; s.Exp = motion.Run("exp", t).F; s.Scale = motion.Run("scale", t).F[0]; s.Kp = motion.Run("kp", t).F;
            s.XS = Keypoints(s.Kp, Rotation(s.Pitch, s.Yaw, s.Roll), s.Exp, s.Scale, s.T, false);
            lock (cache) { cache.Add(s); if (cache.Count > 3) cache.RemoveAt(0); }
            return s;
        }

        // LivePortrait's crop: a square around the face, upright (eye centre to mouth centre vertical), 2.3 times the
        // face's size and shifted up by 12.5 % to include the forehead. MediaPipe points stand for its own landmarks.
        static readonly int[] CropPts = {
            234, 93, 132, 58, 172, 136, 150, 149, 176, 148, 152, 377, 400, 378, 379, 365, 397, 288, 361, 323, 454,  // jaw
            70, 63, 105, 66, 107, 336, 296, 334, 293, 300,                                                        // brows
            1, 2, 98, 327, 168,                                                                                   // nose
            61, 185, 40, 39, 37, 0, 267, 269, 270, 409, 291, 375, 321, 405, 314, 17, 84, 181, 91, 146 };           // lips
        static void Align(float[] L, Source s)
        {
            float ex = 0, ey = 0, n = 0;
            foreach (int i in FaceMesh.RightEye) { ex += L[i * 3]; ey += L[i * 3 + 1]; n++; }
            foreach (int i in FaceMesh.LeftEye) { ex += L[i * 3]; ey += L[i * 3 + 1]; n++; }
            ex /= n; ey /= n;
            float lx = (L[0 * 3] + L[17 * 3]) / 2, ly = (L[0 * 3 + 1] + L[17 * 3 + 1]) / 2;
            float uyx = lx - ex, uyy = ly - ey, len = (float)Math.Sqrt(uyx * uyx + uyy * uyy);
            if (len < 1e-3f) { uyx = 0; uyy = 1; } else { uyx /= len; uyy /= len; }
            float uxx = uyy, uxy = -uyx;
            float c0x = 0, c0y = 0;
            foreach (int i in CropPts) { c0x += L[i * 3]; c0y += L[i * 3 + 1]; }
            c0x /= CropPts.Length; c0y /= CropPts.Length;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (int i in CropPts)
            {
                float dx = L[i * 3] - c0x, dy = L[i * 3 + 1] - c0y;
                float rx = dx * uxx + dy * uxy, ry = dx * uyx + dy * uyy;
                x0 = Math.Min(x0, rx); x1 = Math.Max(x1, rx); y0 = Math.Min(y0, ry); y1 = Math.Max(y1, ry);
            }
            float size = Math.Max(x1 - x0, y1 - y0) * CropScale;
            float c1x = (x0 + x1) / 2, c1y = (y0 + y1) / 2;
            float cx = c0x + uxx * c1x + uyx * c1y + uyx * (CropShiftY * size);
            float cy = c0y + uxy * c1x + uyy * c1y + uyy * (CropShiftY * size);
            float angle = (float)Math.Acos(Math.Max(-1, Math.Min(1, uxx)));
            if (uxy < 0) angle = -angle;
            float sc = Crop / size, ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
            s.A = sc * ca; s.B = sc * sa; s.Tx = Crop / 2f - sc * (ca * cx + sa * cy);
            s.C = -sc * sa; s.D = sc * ca; s.Ty = Crop / 2f - sc * (-sa * cx + ca * cy);
        }

        // head angle bins (66 x 3 degrees) to degrees, as LivePortrait does
        static float Degrees(float[] logits)
        {
            float mx = float.MinValue; foreach (float v in logits) mx = Math.Max(mx, v);
            double sum = 0, wsum = 0;
            for (int i = 0; i < logits.Length; i++) { double e = Math.Exp(logits[i] - mx); sum += e; wsum += e * i; }
            return (float)(wsum / sum * 3 - 97.5);
        }

        // get_rotation_matrix: (Rz Ry Rx) transposed, so that row vectors are rotated by kp @ R
        static float[] Rotation(float pitch, float yaw, float roll)
        {
            double x = pitch * Math.PI / 180, y = yaw * Math.PI / 180, z = roll * Math.PI / 180;
            double[,] rx = { { 1, 0, 0 }, { 0, Math.Cos(x), -Math.Sin(x) }, { 0, Math.Sin(x), Math.Cos(x) } };
            double[,] ry = { { Math.Cos(y), 0, Math.Sin(y) }, { 0, 1, 0 }, { -Math.Sin(y), 0, Math.Cos(y) } };
            double[,] rz = { { Math.Cos(z), -Math.Sin(z), 0 }, { Math.Sin(z), Math.Cos(z), 0 }, { 0, 0, 1 } };
            double[,] r = Mul(Mul(rz, ry), rx);
            float[] t = new float[9];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) t[i * 3 + j] = (float)r[j, i];
            return t;
        }
        static double[,] Mul(double[,] a, double[,] b)
        {
            double[,] c = new double[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) c[i, j] += a[i, k] * b[k, j];
            return c;
        }

        // s * (kp @ R + exp) + t (the source uses only t's x and y, the edited pose all three, as in the official code)
        static float[] Keypoints(float[] kp, float[] R, float[] exp, float scale, float[] t, bool tz)
        {
            float[] o = new float[Kp * 3];
            for (int k = 0; k < Kp; k++)
                for (int j = 0; j < 3; j++)
                {
                    float v = kp[k * 3] * R[j] + kp[k * 3 + 1] * R[3 + j] + kp[k * 3 + 2] * R[6 + j] + exp[k * 3 + j];
                    o[k * 3 + j] = v * scale + (j < 2 || tz ? t[j] : 0);
                }
            return o;
        }

        // ---------------- edit ----------------
        // Re-renders the face with pitch / yaw changed by the given degrees and a smile (0..~1.3), and pastes it into
        // dst. L: MediaPipe points of the face in src. Returns false when the models are not available.
        public static bool Pose(Img src, Img dst, float[] L, float dPitch, float dYaw, float smile)
        {
            if (!Available) return false;
            lock (runLk)
            {
                Working = true;
                try { Redraw(src, dst, L, dPitch, dYaw, smile); }
                finally { Working = false; }
                return true;
            }
        }

        static void Redraw(Img src, Img dst, float[] L, float dPitch, float dYaw, float smile)
        {
            Source s = Prepare(src, L);
            Pipeline.CheckCancel();
            float[] exp = (float[])s.Exp.Clone();
            if (smile != 0)
            {
                // update_delta_new_smile
                exp[20 * 3 + 1] += smile * -0.01f; exp[14 * 3 + 1] += smile * -0.02f;
                exp[17 * 3 + 1] += smile * 0.0065f; exp[17 * 3 + 2] += smile * 0.003f;
                exp[13 * 3 + 1] += smile * -0.00275f; exp[16 * 3 + 1] += smile * -0.00275f;
                exp[3 * 3 + 1] += smile * -0.0035f; exp[7 * 3 + 1] += smile * -0.0035f;
            }
            float[] xd = Keypoints(s.Kp, Rotation(s.Pitch + dPitch, s.Yaw + dYaw, s.Roll), exp, s.Scale, s.T, true);
            // stitching keeps the face joined to the unchanged surroundings
            float[] feat = new float[Kp * 6];
            Array.Copy(s.XS, feat, Kp * 3); Array.Copy(xd, 0, feat, Kp * 3, Kp * 3);
            float[] delta = stitching.Run(new Tensor(feat, 1, Kp * 6)).F;
            for (int i = 0; i < Kp * 3; i++) xd[i] += delta[i];
            for (int k = 0; k < Kp; k++) { xd[k * 3] += delta[Kp * 3]; xd[k * 3 + 1] += delta[Kp * 3 + 1]; }
            Pipeline.CheckCancel();
            Tensor[] inputs = new Tensor[3];
            inputs[warping.InputIndex("feature_3d")] = new Tensor(s.Feature, 1, 32, 16, 64, 64);
            inputs[warping.InputIndex("kp_driving")] = new Tensor(xd, 1, Kp, 3);
            inputs[warping.InputIndex("kp_source")] = new Tensor(s.XS, 1, Kp, 3);
            float[] o = warping.Run(inputs).F;
            PasteBack(src, dst, s, o);
        }

        // the 512 face, through the soft mask, back into the photo
        static void PasteBack(Img src, Img dst, Source s, float[] o)
        {
            int W = dst.W, H = dst.H, N = Crop * Crop;
            float det = s.A * s.D - s.B * s.C;
            // the crop's corners in the photo bound the work
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (float[] c in new[] { new float[] { 0, 0 }, new float[] { Crop, 0 }, new float[] { 0, Crop }, new float[] { Crop, Crop } })
            {
                float du = c[0] - s.Tx, dv = c[1] - s.Ty;
                float x = (s.D * du - s.B * dv) / det, y = (-s.C * du + s.A * dv) / det;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            }
            int ix0 = Math.Max(0, (int)x0), ix1 = Math.Min(W - 1, (int)Math.Ceiling(x1)), iy0 = Math.Max(0, (int)y0), iy1 = Math.Min(H - 1, (int)Math.Ceiling(y1));
            if (ix1 < ix0 || iy1 < iy0) return;
            int rw = ix1 - ix0 + 1, rh = iy1 - iy0 + 1;
            float[] mk = new float[rw * rh];
            Ops.For(iy0, iy1 + 1, y =>
            {
                for (int x = ix0; x <= ix1; x++)
                {
                    float u = s.A * x + s.B * y + s.Tx, v = s.C * x + s.D * y + s.Ty;
                    if (u < 0 || v < 0 || u > Crop - 1 || v > Crop - 1) continue;
                    int ui = (int)u, vi = (int)v; float fu = u - ui, fv = v - vi;
                    int u1 = Math.Min(Crop - 1, ui + 1), v1 = Math.Min(Crop - 1, vi + 1);
                    float m = Bil(mask, ui, vi, u1, v1, fu, fv, 0);
                    if (m <= 0.002f) continue;
                    mk[(y - iy0) * rw + x - ix0] = m;
                    int j = (y * W + x) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float val = Math.Max(0f, Math.Min(1f, Bil(o, ui, vi, u1, v1, fu, fv, c * N)));
                        dst.D[j + c] = m * val + (1 - m) * src.D[j + c];
                    }
                }
            });
            MatchSharpness(src, dst, mk, ix0, iy0, rw, rh, 1.6f / (float)Math.Sqrt(Math.Abs(det)));
        }

        // The network draws the face on a 256 grid: on larger photos the new face is softer than the rest. An unsharp
        // mask at the network's pixel size brings its fine detail back to the level the original face had.
        static void MatchSharpness(Img src, Img dst, float[] mk, int ix0, int iy0, int rw, int rh, float sigma)
        {
            if (sigma < 0.8f) return;
            int W = dst.W, n = rw * rh;
            float[][] d = new float[3][];
            float[] ls = new float[n], ld = new float[n];
            for (int c = 0; c < 3; c++) d[c] = new float[n];
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    int i = y * rw + x, j = ((y + iy0) * W + x + ix0) * 3;
                    for (int c = 0; c < 3; c++) d[c][i] = dst.D[j + c];
                    ls[i] = 0.3f * src.D[j] + 0.59f * src.D[j + 1] + 0.11f * src.D[j + 2];
                    ld[i] = 0.3f * d[0][i] + 0.59f * d[1][i] + 0.11f * d[2][i];
                }
            float[] bs = Ops.Gauss(ls, rw, rh, sigma), bd = Ops.Gauss(ld, rw, rh, sigma);
            double es = 0, ed = 0;
            for (int i = 0; i < n; i++)
            {
                float w = mk[i] > 0.9f ? 1f : 0f;
                float a = ls[i] - bs[i], b = ld[i] - bd[i];
                es += w * a * a; ed += w * b * b;
            }
            if (ed <= 1e-9) return;
            float k = Math.Max(0f, Math.Min(1.5f, (float)Math.Sqrt(es / ed) - 1f));
            if (k < 0.05f) return;
            Pipeline.CheckCancel();
            for (int c = 0; c < 3; c++)
            {
                float[] b = Ops.Gauss(d[c], rw, rh, sigma), dc = d[c];
                for (int y = 0; y < rh; y++)
                    for (int x = 0; x < rw; x++)
                    {
                        int i = y * rw + x;
                        if (mk[i] <= 0) continue;
                        dst.D[((y + iy0) * W + x + ix0) * 3 + c] = Math.Max(0f, Math.Min(1f, dc[i] + mk[i] * k * (dc[i] - b[i])));
                    }
            }
        }

        static float Bil(float[] p, int u0, int v0, int u1, int v1, float fu, float fv, int off)
        {
            float a = p[off + v0 * Crop + u0], b = p[off + v0 * Crop + u1], c = p[off + v1 * Crop + u0], d = p[off + v1 * Crop + u1];
            return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
        }
    }
}

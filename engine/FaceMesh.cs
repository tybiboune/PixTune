using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PixTune
{
    // 3D face mesh for Head Pose: Google's MediaPipe Face Mesh (Apache 2.0; ONNX export by Qualcomm AI Hub) finds
    // 468 points with depth on each face; the head is then turned, tilted, given a smile or a different focal
    // length by moving those points in 3D, and the photo is re-rendered through the mesh triangle by triangle.
    public static class FaceMesh
    {
        public static string ModelPath = "";     // native\face_landmark_detector.onnx (its weights in .data next to it)
        public static string RuntimePath = "";   // native\onnxruntime.dll
        public static string LastError = "";
        const int Size = 192;
        static volatile bool tried, ok;
        static readonly object lk = new object();
        static OrtSession session;

        public static bool Available
        {
            get
            {
                if (tried) return ok;
                lock (lk)
                {
                    if (tried) return ok;
                    try
                    {
                        if (!File.Exists(ModelPath)) throw new Exception("the face mesh model is missing");
                        Ort.Init(RuntimePath);
                        session = OrtSession.Open(ModelPath, false, 1); // tiny model: the CPU is quickest
                        ok = true;
                    }
                    catch (Exception ex) { LastError = ex.Message; ok = false; }
                    finally { tried = true; }
                    return ok;
                }
            }
        }

        // ---------------- topology (MediaPipe canonical face model, 468 points) ----------------
        public static readonly int[] RightEye = { 33, 246, 161, 160, 159, 158, 157, 173, 133, 155, 154, 153, 145, 144, 163, 7 };   // the eye on the image's left
        public static readonly int[] LeftEye = { 263, 466, 388, 387, 386, 385, 384, 398, 362, 382, 381, 380, 374, 373, 390, 249 };
        public const int MouthLeft = 61, MouthRight = 291, NoseTip = 1, Chin = 152, Forehead = 10;
        // face outline, in order around the face
        static readonly int[] Oval = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };
        // the rings around it, as multiples of the outline's distance from the face centre, and how much the first follows
        const float Ring1 = 1.45f, Ring2 = 2.6f, Follow = 0.7f;
        static int[] tris;
        public static int[] Triangles { get { return tris ?? (tris = ParseTris()); } }
        static int[] ParseTris()
        {
            string[] t = TrisText.Split(';');
            int[] r = new int[t.Length * 3];
            for (int i = 0; i < t.Length; i++)
            {
                string[] a = t[i].Split(',');
                for (int k = 0; k < 3; k++) r[i * 3 + k] = int.Parse(a[k]);
            }
            return r;
        }

        // ---------------- landmarks ----------------
        // The 468 points (x, y in pixels of im, z in the same scale, smaller = nearer) for the face in box
        // (normalized x, y, w, h), or null. Two passes: the face box, then a crop aligned with the eyes.
        public static float[] Landmarks(Img im, float[] box)
        {
            if (!Available) return null;
            string key = string.Join(",", Array.ConvertAll(box, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            lock (cache) foreach (Cached c in cache) if (c.Img == im && c.Box == key) return c.Points;
            float[] pts = Fit(im, box);
            if (pts != null) lock (cache) { cache.Add(new Cached { Img = im, Box = key, Points = pts }); if (cache.Count > 6) cache.RemoveAt(0); }
            return pts;
        }
        class Cached { public Img Img; public string Box; public float[] Points; }
        static readonly List<Cached> cache = new List<Cached>();
        public static void Forget() { lock (cache) cache.Clear(); }

        static float[] Fit(Img im, float[] box)
        {
            float bw = box[2] * im.W, bh = box[3] * im.H;
            float cx = (box[0] + box[2] / 2) * im.W, cy = (box[1] + box[3] / 2) * im.H;
            float[] p = Run(im, cx, cy, Math.Max(bw, bh) * 1.7f, 0, true);
            if (p == null) return null;
            for (int pass = 0; pass < 2; pass++)
            {
                // re-centre on the found face, rotated so the eyes are level, about 1.5 times its size
                float ang = (float)Math.Atan2(p[263 * 3 + 1] - p[33 * 3 + 1], p[263 * 3] - p[33 * 3]);
                float ca = (float)Math.Cos(-ang), sa = (float)Math.Sin(-ang);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, mx = 0, my = 0;
                for (int i = 0; i < 468; i++) { mx += p[i * 3]; my += p[i * 3 + 1]; }
                mx /= 468; my /= 468;
                for (int i = 0; i < 468; i++)
                {
                    float dx = p[i * 3] - mx, dy = p[i * 3 + 1] - my;
                    float rx = dx * ca - dy * sa, ry = dx * sa + dy * ca;
                    x0 = Math.Min(x0, rx); x1 = Math.Max(x1, rx); y0 = Math.Min(y0, ry); y1 = Math.Max(y1, ry);
                }
                float ccx = (x0 + x1) / 2, ccy = (y0 + y1) / 2;
                float ccxI = mx + ccx * (float)Math.Cos(ang) - ccy * (float)Math.Sin(ang);
                float ccyI = my + ccx * (float)Math.Sin(ang) + ccy * (float)Math.Cos(ang);
                float[] q = Run(im, ccxI, ccyI, Math.Max(x1 - x0, y1 - y0) * 1.5f, ang, false);
                if (q == null) break;
                p = q;
            }
            return p;
        }

        // one model run on a square crop (centre cx, cy, side, rotated by ang), points mapped back to the image
        static float[] Run(Img im, float cx, float cy, float side, float ang, bool loose)
        {
            int N = Size * Size;
            float[] x = new float[3 * N];
            float c = (float)Math.Cos(ang), s = (float)Math.Sin(ang), k = side / Size;
            Ops.For(0, Size, v =>
            {
                for (int u = 0; u < Size; u++)
                {
                    float dx = (u + 0.5f - Size / 2f) * k, dy = (v + 0.5f - Size / 2f) * k;
                    float r, g, b; im.Sample(cx + dx * c - dy * s, cy + dx * s + dy * c, out r, out g, out b);
                    int i = v * Size + u;
                    x[i] = r; x[N + i] = g; x[2 * N + i] = b;
                }
            });
            Tensor t = new Tensor(x, 1, 3, Size, Size);
            float score;
            float[] o;
            lock (lk)
            {
                score = session.Run("scores", t).F[0];
                o = session.Run("landmarks", t).F;
            }
            if (score < (loose ? 0.2f : 0.4f)) return null;
            float[] p = new float[468 * 3];
            for (int i = 0; i < 468; i++)
            {
                float dx = (o[i * 3] - 0.5f) * side, dy = (o[i * 3 + 1] - 0.5f) * side;
                p[i * 3] = cx + dx * c - dy * s;
                p[i * 3 + 1] = cy + dx * s + dy * c;
                p[i * 3 + 2] = o[i * 3 + 2] * side;
            }
            return p;
        }

        // ---------------- head pose ----------------
        // hr, vr: turn left/right and up/down (-1..1); smile, pupil, focal: 0..1.
        // Returns false when the face cannot be fitted (the caller then uses the simple warp).
        public static bool Pose(Img src, Img dst, float[] L, float hr, float vr, float smile, float pupil, float focal)
        {
            int W = src.W, H = src.H, n = 468;
            // face frame: centre, size, roll (eyes level)
            float roll = (float)Math.Atan2(L[263 * 3 + 1] - L[33 * 3 + 1], L[263 * 3] - L[33 * 3]);
            float cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
            float cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < n; i++) { cx += L[i * 3]; cy += L[i * 3 + 1]; cz += L[i * 3 + 2]; }
            cx /= n; cy /= n; cz /= n;
            float faceW = Dist(L, 234, 454), faceH = Dist(L, Forehead, Chin), mouthW = Dist(L, MouthLeft, MouthRight);
            if (faceW < 8 || faceH < 8) return false;
            // points in the face frame (x right, y down, z away from the camera), centred
            float[] F = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                float dx = L[i * 3] - cx, dy = L[i * 3 + 1] - cy;
                F[i * 3] = dx * cr + dy * sr; F[i * 3 + 1] = -dx * sr + dy * cr; F[i * 3 + 2] = L[i * 3 + 2] - cz;
            }
            float[] D = (float[])F.Clone();
            // smile: mouth corners up and out, cheeks lifted a little, falling off with distance
            if (smile > 0)
            {
                for (int i = 0; i < n; i++)
                    foreach (int m in new[] { MouthLeft, MouthRight })
                    {
                        float dx = F[i * 3] - F[m * 3], dy = F[i * 3 + 1] - F[m * 3 + 1];
                        float d2 = (dx * dx + dy * dy) / (mouthW * mouthW);
                        float wgt = (float)Math.Exp(-d2 / (2 * 0.09f));
                        float side = m == MouthLeft ? -1 : 1;
                        D[i * 3] += side * smile * 0.07f * mouthW * wgt;
                        D[i * 3 + 1] -= smile * 0.13f * mouthW * wgt;
                        // cheeks: a softer, wider lift above the corners
                        float wc = (float)Math.Exp(-d2 / (2 * 0.35f)) * (dy < 0 ? 1f : 0.3f);
                        D[i * 3 + 1] -= smile * 0.035f * mouthW * wc;
                    }
            }
            // focal length: nearer parts (nose) grow as with a closer, wider camera
            if (focal > 0)
                for (int i = 0; i < n; i++)
                {
                    float k = 1 + focal * 0.55f * (-D[i * 3 + 2] / faceW);
                    D[i * 3] *= k; D[i * 3 + 1] *= k;
                }
            // turn and tilt around a pivot inside the head
            float yaw = hr * 0.34f, pitch = -vr * 0.22f, pz = faceW * 0.55f;
            float cyw = (float)Math.Cos(yaw), syw = (float)Math.Sin(yaw), cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
            for (int i = 0; i < n; i++)
            {
                float x = D[i * 3], y = D[i * 3 + 1], z = D[i * 3 + 2] - pz;
                float x1 = x * cyw + z * syw, z1 = -x * syw + z * cyw;
                float y2 = y * cp - z1 * sp, z2 = y * sp + z1 * cp;
                D[i * 3] = x1; D[i * 3 + 1] = y2; D[i * 3 + 2] = z2 + pz;
            }
            // back to image coordinates
            float[] S = L, T = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                float x = D[i * 3], y = D[i * 3 + 1];
                T[i * 3] = cx + x * cr - y * sr; T[i * 3 + 1] = cy + x * sr + y * cr; T[i * 3 + 2] = D[i * 3 + 2];
            }

            // The surroundings join the mesh: two rings of points around the face outline. The first follows the
            // face at about half strength (hair, ears, neck), the second stays put (background). Everything inside
            // the outer ring is then one continuous mesh, so a turned face uncovers what is beside it instead of
            // repeating its own edge.
            int no = Oval.Length, nt = n + 2 * no;
            float[] S2 = new float[nt * 3], T2 = new float[nt * 3];
            Array.Copy(S, S2, n * 3); Array.Copy(T, T2, n * 3);
            const float behind = float.MaxValue / 8;   // ring triangles are drawn behind every face triangle
            for (int k = 0; k < no; k++)
            {
                int o = Oval[k], r1 = n + k, r2 = n + no + k;
                float ux = S[o * 3] - cx, uy = S[o * 3 + 1] - cy, dx = T[o * 3] - S[o * 3], dy = T[o * 3 + 1] - S[o * 3 + 1];
                S2[r1 * 3] = cx + ux * Ring1; S2[r1 * 3 + 1] = cy + uy * Ring1;
                T2[r1 * 3] = S2[r1 * 3] + dx * Follow; T2[r1 * 3 + 1] = S2[r1 * 3 + 1] + dy * Follow;
                S2[r2 * 3] = cx + ux * Ring2; S2[r2 * 3 + 1] = cy + uy * Ring2;
                T2[r2 * 3] = S2[r2 * 3]; T2[r2 * 3 + 1] = S2[r2 * 3 + 1];
                T2[r1 * 3 + 2] = behind; T2[r2 * 3 + 2] = behind;
            }
            List<int> tr = new List<int>(Triangles);
            for (int k = 0; k < no; k++)
            {
                int k2 = (k + 1) % no;
                int a = Oval[k], b = Oval[k2], a1 = n + k, b1 = n + k2, a2 = n + no + k, b2 = n + no + k2;
                tr.AddRange(new[] { a, b, b1, a, b1, a1, a1, b1, b2, a1, b2, a2 });
            }
            // region: what the outer ring encloses
            float fx0 = float.MaxValue, fx1 = float.MinValue, fy0 = float.MaxValue, fy1 = float.MinValue;
            for (int i = n + no; i < nt; i++)
            {
                fx0 = Math.Min(fx0, T2[i * 3]); fx1 = Math.Max(fx1, T2[i * 3]); fy0 = Math.Min(fy0, T2[i * 3 + 1]); fy1 = Math.Max(fy1, T2[i * 3 + 1]);
            }
            int rx0 = Math.Max(0, (int)Math.Floor(fx0)), rx1 = Math.Min(W - 1, (int)Math.Ceiling(fx1));
            int ry0 = Math.Max(0, (int)Math.Floor(fy0)), ry1 = Math.Min(H - 1, (int)Math.Ceiling(fy1));
            int rw = rx1 - rx0 + 1, rh = ry1 - ry0 + 1;
            if (rw < 4 || rh < 4) return false;
            // every pixel of each moved triangle takes the matching place of the original one; nearer triangles
            // win (z-buffer), so the far side of a turned face folds away correctly
            float[] mapX = new float[rw * rh], mapY = new float[rw * rh], zb = new float[rw * rh];
            bool[] covered = new bool[rw * rh];
            for (int i = 0; i < zb.Length; i++) zb[i] = float.MaxValue;
            for (int t = 0; t < tr.Count; t += 3)
            {
                int a = tr[t], b = tr[t + 1], c = tr[t + 2];
                float ax = T2[a * 3] - rx0, ay = T2[a * 3 + 1] - ry0, bx = T2[b * 3] - rx0, by = T2[b * 3 + 1] - ry0, qx = T2[c * 3] - rx0, qy = T2[c * 3 + 1] - ry0;
                float den = (by - qy) * (ax - qx) + (qx - bx) * (ay - qy);
                if (Math.Abs(den) < 1e-6f) continue;
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, qx)))), x1 = Math.Min(rw - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, qx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, qy)))), y1 = Math.Min(rh - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, qy))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = ((by - qy) * (px - qx) + (qx - bx) * (py - qy)) / den;
                        float l1 = ((qy - ay) * (px - qx) + (ax - qx) * (py - qy)) / den;
                        float l2 = 1 - l0 - l1;
                        if (l0 < -1e-4f || l1 < -1e-4f || l2 < -1e-4f) continue;
                        float z = l0 * T2[a * 3 + 2] + l1 * T2[b * 3 + 2] + l2 * T2[c * 3 + 2];
                        int i = y * rw + x;
                        if (z >= zb[i]) continue;
                        zb[i] = z; covered[i] = true;
                        mapX[i] = l0 * S2[a * 3] + l1 * S2[b * 3] + l2 * S2[c * 3];
                        mapY[i] = l0 * S2[a * 3 + 1] + l1 * S2[b * 3 + 1] + l2 * S2[c * 3 + 1];
                    }
            }
            Ops.For(0, rh, y =>
            {
                for (int x = 0; x < rw; x++)
                {
                    int i = y * rw + x;
                    if (!covered[i]) continue;
                    float r, gg, b; src.Sample(mapX[i], mapY[i], out r, out gg, out b);
                    int j = ((y + ry0) * W + x + rx0) * 3;
                    dst.D[j] = r; dst.D[j + 1] = gg; dst.D[j + 2] = b;
                }
            });            // 3. pupils: the iris grows inside the eye opening only, so the eyelids do not move
            if (pupil > 0) Pupils(src, dst, S, T, pupil);
            return true;
        }

        static float Dist(float[] p, int a, int b)
        {
            float dx = p[a * 3] - p[b * 3], dy = p[a * 3 + 1] - p[b * 3 + 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static void Pupils(Img src, Img dst, float[] S, float[] T, float amount)
        {
            int W = dst.W, H = dst.H;
            Img before = dst.Clone();
            foreach (int[] eye in new[] { RightEye, LeftEye })
            {
                // eye opening in the result, and its bounding box
                float ex0 = float.MaxValue, ex1 = float.MinValue, ey0 = float.MaxValue, ey1 = float.MinValue;
                PointF2[] poly = new PointF2[eye.Length];
                for (int k = 0; k < eye.Length; k++)
                {
                    poly[k] = new PointF2(T[eye[k] * 3], T[eye[k] * 3 + 1]);
                    ex0 = Math.Min(ex0, poly[k].X); ex1 = Math.Max(ex1, poly[k].X); ey0 = Math.Min(ey0, poly[k].Y); ey1 = Math.Max(ey1, poly[k].Y);
                }
                float eyeW = ex1 - ex0;
                if (eyeW < 4) continue;
                int x0 = Math.Max(0, (int)(ex0 - 2)), x1 = Math.Min(W - 1, (int)(ex1 + 2)), y0 = Math.Max(0, (int)(ey0 - 2)), y1 = Math.Min(H - 1, (int)(ey1 + 2));
                // iris centre: where the opening is darkest
                double sx = 0, sy = 0, sw = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!Inside(poly, x + 0.5f, y + 0.5f)) continue;
                        int j = (y * W + x) * 3;
                        float l = Ops.Lum(before.D[j], before.D[j + 1], before.D[j + 2]);
                        double wgt = Math.Pow(Math.Max(0, 1 - l), 6);
                        sx += wgt * x; sy += wgt * y; sw += wgt;
                    }
                if (sw <= 0) continue;
                float icx = (float)(sx / sw) + 0.5f, icy = (float)(sy / sw) + 0.5f, R = eyeW * 0.3f;
                float k0 = 1 + amount * 0.6f;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float m = InsideSoft(poly, px, py, Math.Max(1f, eyeW * 0.04f));
                        if (m <= 0) continue;
                        float dx = px - icx, dy = py - icy, r = (float)Math.Sqrt(dx * dx + dy * dy);
                        float sc = 1 + (k0 - 1) * (1 - Ops.SmoothStep(R * 0.2f, R * 1.7f, r));
                        float r0, g0, b0; before.Sample(icx + dx / sc - 0.5f, icy + dy / sc - 0.5f, out r0, out g0, out b0);
                        int j = (y * W + x) * 3;
                        dst.D[j] = Ops.Lerp(dst.D[j], r0, m); dst.D[j + 1] = Ops.Lerp(dst.D[j + 1], g0, m); dst.D[j + 2] = Ops.Lerp(dst.D[j + 2], b0, m);
                    }
            }
        }

        struct PointF2 { public float X, Y; public PointF2(float x, float y) { X = x; Y = y; } }

        static bool Inside(PointF2[] p, float x, float y)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if (((p[i].Y > y) != (p[j].Y > y)) && (x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)) c = !c;
            return c;
        }

        // 1 inside the polygon, fading to 0 within 'soft' pixels of its edge
        static float InsideSoft(PointF2[] p, float x, float y, float soft)
        {
            if (!Inside(p, x, y)) return 0;
            float best = float.MaxValue;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                float vx = p[i].X - p[j].X, vy = p[i].Y - p[j].Y, wx = x - p[j].X, wy = y - p[j].Y;
                float t = Math.Max(0, Math.Min(1, (wx * vx + wy * vy) / Math.Max(1e-6f, vx * vx + vy * vy)));
                float dx = wx - t * vx, dy = wy - t * vy;
                best = Math.Min(best, (float)Math.Sqrt(dx * dx + dy * dy));
            }
            return Ops.SmoothStep(0f, soft, best);
        }

        // MediaPipe canonical face model triangles (Apache 2.0), vertex indices
        const string TrisText = "173,155,133;246,33,7;382,398,362;263,466,249;308,415,324;78,95,191;356,389,264;127,34,162;368,264,389;139,162,34;267,0,302;37,72,0;11,302,0;11,0,72;349,451,350;120,121,231;452,350,451;232,231,121;267,302,269;37,39,72;303,269,302;73,72,39;357,343,350;128,121,114;277,350,343;47,114,121;350,452,357;121,128,232;453,357,452;233,232,128;299,333,297;69,67,104;332,297,333;103,104,67;175,152,396;175,171,152;377,396,152;148,152,171;381,384,382;154,155,157;398,382,384;173,157,155;280,347,330;50,101,118;348,330,347;119,118,101;269,303,270;39,40,73;304,270,303;74,73,40;9,336,151;9,151,107;337,151,336;108,107,151;344,278,360;115,131,48;279,360,278;49,48,131;262,431,418;32,194,211;424,418,431;204,211,194;304,408,270;74,40,184;409,270,408;185,184,40;272,310,407;42,183,80;415,407,310;191,80,183;322,270,410;92,186,40;409,410,270;185,40,186;347,449,348;118,119,229;450,348,449;230,229,119;434,432,430;214,210,212;422,430,432;202,212,210;313,314,18;83,18,84;17,18,314;17,84,18;307,375,306;77,76,146;291,306,375;61,146,76;259,387,260;29,30,160;388,260,387;161,160,30;286,414,384;56,157,190;398,384,414;173,190,157;418,424,406;194,182,204;335,406,424;106,204,182;367,416,364;138,135,192;434,364,416;214,192,135;391,423,327;165,98,203;358,327,423;129,203,98;298,301,284;68,54,71;251,284,301;21,71,54;4,275,5;4,5,45;281,5,275;51,45,5;254,373,253;24,23,144;374,253,373;145,144,23;320,321,307;90,77,91;375,307,321;146,91,77;280,425,411;50,187,205;427,411,425;207,205,187;421,313,200;201,200,83;18,200,313;18,83,200;335,321,406;106,182,91;405,406,321;181,91,182;405,321,404;181,180,91;320,404,321;90,91,180;17,314,16;17,16,84;315,16,314;85,84,16;425,266,426;205,206,36;423,426,266;203,36,206;369,396,400;140,176,171;377,400,396;148,171,176;391,269,322;165,92,39;270,322,269;40,39,92;417,465,413;193,189,245;464,413,465;244,245,189;257,258,386;27,159,28;385,386,258;158,28,159;260,388,467;30,247,161;466,467,388;246,161,247;248,456,419;3,196,236;399,419,456;174,236,196;333,298,332;104,103,68;284,332,298;54,68,103;285,8,417;55,193,8;168,417,8;168,8,193;340,261,346;111,117,31;448,346,261;228,31,117;285,417,441;55,221,193;413,441,417;189,193,221;327,460,326;98,97,240;328,326,460;99,240,97;277,355,329;47,100,126;371,329,355;142,126,100;309,392,438;79,218,166;439,438,392;219,166,218;381,382,256;154,26,155;341,256,382;112,155,26;360,279,420;131,198,49;429,420,279;209,49,198;365,364,379;136,150,135;394,379,364;169,135,150;355,277,437;126,217,47;343,437,277;114,47,217;443,444,282;223,52,224;283,282,444;53,224,52;281,275,363;51,134,45;440,363,275;220,45,134;431,262,395;211,170,32;369,395,262;140,32,170;337,299,338;108,109,69;297,338,299;67,69,109;335,273,321;106,91,43;375,321,273;146,43,91;348,450,349;119,120,230;451,349,450;231,230,120;467,359,342;247,113,130;446,342,359;226,130,113;282,283,334;52,105,53;293,334,283;63,53,105;250,458,462;20,242,238;461,462,458;241,238,242;276,353,300;46,70,124;383,300,353;156,124,70;325,292,324;96,95,62;308,324,292;78,62,95;283,276,293;53,63,46;300,293,276;70,46,63;447,264,345;227,116,34;372,345,264;143,34,116;352,345,346;123,117,116;340,346,345;111,116,117;1,19,274;1,44,19;354,274,19;125,19,44;248,281,456;3,236,51;363,456,281;134,51,236;425,426,427;205,207,206;436,427,426;216,206,207;380,381,252;153,22,154;256,252,381;26,154,22;391,393,269;165,39,167;267,269,393;37,167,39;199,428,200;199,200,208;421,200,428;201,208,200;330,329,266;101,36,100;371,266,329;142,100,36;422,432,273;202,43,212;287,273,432;57,212,43;290,250,328;60,99,20;462,328,250;242,20,99;258,286,385;28,158,56;384,385,286;157,56,158;342,446,353;113,124,226;265,353,446;35,226,124;257,386,259;27,29,159;387,259,386;160,159,29;430,422,431;210,211,202;424,431,422;204,202,211;445,342,276;225,46,113;353,276,342;124,113,46;424,422,335;204,106,202;273,335,422;43,202,106;306,292,307;76,77,62;325,307,292;96,62,77;366,447,352;137,123,227;345,352,447;116,227,123;302,268,303;72,73,38;271,303,268;41,38,73;371,358,266;142,36,129;423,266,358;203,129,36;327,294,460;98,240,64;455,460,294;235,64,240;294,331,278;64,48,102;279,278,331;49,102,48;303,271,304;73,74,41;272,304,271;42,41,74;427,436,434;207,214,216;432,434,436;212,216,214;304,272,408;74,184,42;407,408,272;183,42,184;394,430,395;169,170,210;431,395,430;211,210,170;395,369,378;170,149,140;400,378,369;176,140,149;296,334,299;66,69,105;333,299,334;104,105,69;417,168,351;193,122,168;6,351,168;6,168,122;280,411,352;50,123,187;376,352,411;147,187,123;319,320,325;89,96,90;307,325,320;77,90,96;285,295,336;55,107,65;296,336,295;66,65,107;404,320,403;180,179,90;319,403,320;89,90,179;330,348,329;101,100,119;349,329,348;120,119,100;334,293,333;105,104,63;298,333,293;68,63,104;323,454,366;93,137,234;447,366,454;227,234,137;16,315,15;16,15,85;316,15,315;86,85,15;429,279,358;209,129,49;331,358,279;102,49,129;15,316,14;15,14,86;317,14,316;87,86,14;8,285,9;8,9,55;336,9,285;107,55,9;329,349,277;100,47,120;350,277,349;121,120,47;252,253,380;22,153,23;374,380,253;145,23,153;402,403,318;178,88,179;319,318,403;89,179,88;351,6,419;122,196,6;197,419,6;197,6,196;324,318,325;95,96,88;319,325,318;89,88,96;397,367,365;172,136,138;364,365,367;135,138,136;288,435,397;58,172,215;367,397,435;138,215,172;438,439,344;218,115,219;278,344,439;48,219,115;271,311,272;41,42,81;310,272,311;80,81,42;5,281,195;5,195,51;248,195,281;3,51,195;273,287,375;43,146,57;291,375,287;61,57,146;396,428,175;171,175,208;199,175,428;199,208,175;268,312,271;38,41,82;311,271,312;81,82,41;444,445,283;224,53,225;276,283,445;46,225,53;254,339,373;24,144,110;390,373,339;163,110,144;295,282,296;65,66,52;334,296,282;105,52,66;346,448,347;117,118,228;449,347,448;229,228,118;454,356,447;234,227,127;264,447,356;34,127,227;336,296,337;107,108,66;299,337,296;69,66,108;151,337,10;151,10,108;338,10,337;109,108,10;278,439,294;48,64,219;455,294,439;235,219,64;407,415,292;183,62,191;308,292,415;78,191,62;358,371,429;129,209,142;355,429,371;126,142,209;345,372,340;116,111,143;265,340,372;35,143,111;388,390,466;161,246,163;249,466,390;7,163,246;352,346,280;123,50,117;347,280,346;118,117,50;295,442,282;65,52,222;443,282,442;223,222,52;19,94,354;19,125,94;370,354,94;141,94,125;295,285,442;65,222,55;441,442,285;221,55,222;419,197,248;196,3,197;195,248,197;195,197,3;359,263,255;130,25,33;249,255,263;7,33,25;275,274,440;45,220,44;457,440,274;237,44,220;300,383,301;70,71,156;368,301,383;139,156,71;417,351,465;193,245,122;412,465,351;188,122,245;466,263,467;246,247,33;359,467,263;130,33,247;389,251,368;162,139,21;301,368,251;71,21,139;374,386,380;145,153,159;385,380,386;158,159,153;379,394,378;150,149,169;395,378,394;170,169,149;351,419,412;122,188,196;399,412,419;174,196,188;426,322,436;206,216,92;410,436,322;186,92,216;387,373,388;160,161,144;390,388,373;163,144,161;393,326,164;167,164,97;2,164,326;2,97,164;354,370,461;125,241,141;462,461,370;242,141,241;0,267,164;0,164,37;393,164,267;167,37,164;11,12,302;11,72,12;268,302,12;38,12,72;386,374,387;159,160,145;373,387,374;144,145,160;12,13,268;12,38,13;312,268,13;82,13,38;293,300,298;63,68,70;301,298,300;71,70,68;340,265,261;111,31,35;446,261,265;226,35,31;380,385,381;153,154,158;384,381,385;157,158,154;280,330,425;50,205,101;266,425,330;36,101,205;423,391,426;203,206,165;322,426,391;92,165,206;429,355,420;209,198,126;437,420,355;217,126,198;391,327,393;165,167,98;326,393,327;97,98,167;457,438,440;237,220,218;344,440,438;115,218,220;382,362,341;155,112,133;463,341,362;243,133,112;457,461,459;237,239,241;458,459,461;238,241,239;434,430,364;214,135,210;394,364,430;169,210,135;414,463,398;190,173,243;362,398,463;133,243,173;262,428,369;32,140,208;396,369,428;171,208,140;457,274,461;237,241,44;354,461,274;125,44,241;316,403,317;86,87,179;402,317,403;178,179,87;315,404,316;85,86,180;403,316,404;179,180,86;314,405,315;84,85,181;404,315,405;180,181,85;313,406,314;83,84,182;405,314,406;181,182,84;418,406,421;194,201,182;313,421,406;83,182,201;366,401,323;137,93,177;361,323,401;132,177,93;408,407,306;184,76,183;292,306,407;62,183,76;408,306,409;184,185,76;291,409,306;61,76,185;410,409,287;186,57,185;291,287,409;61,185,57;436,410,432;216,212,186;287,432,410;57,186,212;434,416,427;214,207,192;411,427,416;187,192,207;264,368,372;34,143,139;383,372,368;156,139,143;457,459,438;237,218,239;309,438,459;79,239,218;352,376,366;123,137,147;401,366,376;177,147,137;4,1,275;4,45,1;274,275,1;44,1,45;428,262,421;208,201,32;418,421,262;194,32,201;327,358,294;98,64,129;331,294,358;102,129,64;367,435,416;138,192,215;433,416,435;213,215,192;455,439,289;235,59,219;392,289,439;166,219,59;328,462,326;99,97,242;370,326,462;141,242,97;326,370,2;97,2,141;94,2,370;94,141,2;460,455,305;240,75,235;289,305,455;59,235,75;448,339,449;228,229,110;254,449,339;24,110,229;261,446,255;31,25,226;359,255,446;130,226,25;449,254,450;229,230,24;253,450,254;23,24,230;450,253,451;230,231,23;252,451,253;22,23,231;451,252,452;231,232,22;256,452,252;26,22,232;256,341,452;26,232,112;453,452,341;233,112,232;413,464,414;189,190,244;463,414,464;243,244,190;441,413,286;221,56,189;414,286,413;190,189,56;441,286,442;221,222,56;258,442,286;28,56,222;442,258,443;222,223,28;257,443,258;27,28,223;444,443,259;224,29,223;257,259,443;27,223,29;259,260,444;29,224,30;445,444,260;225,30,224;260,467,445;30,225,247;342,445,467;113,247,225;250,309,458;20,238,79;459,458,309;239,79,238;290,305,392;60,166,75;289,392,305;59,75,166;460,305,328;240,99,75;290,328,305;60,75,99;376,433,401;147,177,213;435,401,433;215,213,177;250,290,309;20,79,60;392,309,290;166,60,79;411,416,376;187,147,192;433,376,416;213,192,147;341,463,453;112,233,243;464,453,463;244,243,233;453,464,357;233,128,244;465,357,464;245,244,128;412,343,465;188,245,114;357,465,343;128,114,245;437,343,399;217,174,114;412,399,343;188,114,174;363,440,360;134,131,220;344,360,440;115,220,131;456,420,399;236,174,198;437,399,420;217,198,174;456,363,420;236,198,134;360,420,363;131,134,198;361,401,288;132,58,177;435,288,401;215,177,58;353,265,383;124,156,35;372,383,265;143,35,156;255,249,339;25,110,7;390,339,249;163,7,110;261,255,448;31,228,25;339,448,255;110,25,228;14,317,13;14,13,87;312,13,317;82,87,13;317,402,312;87,82,178;311,312,402;81,178,82;402,318,311;178,81,88;310,311,318;80,88,81;318,324,310;88,80,95;415,310,324;191,95,80";
    }
}

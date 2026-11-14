using System;
using System.Collections.Generic;

namespace PixTune
{
    // Where the skin is, which part of the face it belongs to, and what colour this face's skin actually is.
    // Everything the per-defect skin work needs sits on top of this: a blemish map, a redness map and a line map
    // are all "this pixel, against what this face's skin normally looks like, in this part of the face".
    //
    // The regions are worked out from the few landmarks that can be relied on - the eye rings, the mouth corners,
    // the nose tip, the chin and the top of the forehead - and from the outline of all 468 points, rather than
    // from remembered index lists for lips and brows. An index list that is subtly wrong puts the mouth cut-out
    // over a cheek, and nothing about the result would look obviously broken.
    //
    // The colour model is fitted to the face in front of it, not to a table of what skin is supposed to look
    // like: the middle of this face's own cheeks and forehead, and how far that colour usually strays. A fixed
    // threshold has to be loose enough for every skin tone, which makes it loose enough to call a wooden door
    // skin; a fitted one can be tight, and it is tight in the right place for the face it was fitted to.
    public static class SkinAtlas
    {
        public class Atlas
        {
            public int W, H;
            public float[] Skin;            // 0 none, 1 certainly skin, with eyes, brows, lips and nostrils cut out
            public float[] Forehead, Cheeks, UnderEye, Nasolabial, NoseChin;
            public float CbMid, CrMid;      // the middle of this face's skin colour
            public float CbSpread, CrSpread;
            public float FaceSize;          // across, in pixels
            public bool FromMesh;           // false when it fell back to colour and an ellipse
            public string Note = "";       // what it found while building, for when it finds nothing
        }

        // ---------------- the colour axes ----------------
        // Cb and Cr: brightness is taken out, so a cheek in shade and the same cheek in sun sit together, and
        // redness is a move along Cr alone, which is what makes it something a slider can undo.
        public static void Chroma(float r, float g, float b, out float cb, out float cr)
        {
            cb = 128 - 37.797f * r - 74.203f * g + 112f * b;
            cr = 128 + 112f * r - 93.786f * g - 18.214f * b;
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }
        // how far the middle half strays: a median of distances from the median, which a few bright spots or a
        // stray hair cannot drag about the way an average can
        static float Mad(List<float> v, float mid)
        {
            if (v.Count == 0) return 1;
            List<float> d = new List<float>(v.Count);
            for (int i = 0; i < v.Count; i++) d.Add(Math.Abs(v[i] - mid));
            d.Sort();
            return Math.Max(1.5f, d[d.Count / 2] * 1.4826f);
        }

        class Anchors
        {
            public float EyeLX, EyeLY, EyeRX, EyeRY, EyeR;      // the two eyes and how big one is
            public float MouthX, MouthY, MouthHalf;
            public float NoseX, NoseY, ChinY, BrowY, Size;
            public float[] Hull;                                 // the face outline, x,y pairs
            public float Wind;                                   // which way round that outline runs
        }

        // Where the parts of a face sit inside its box, when the landmarks cannot be had: a face detector gives
        // a box and nothing else, and these proportions are close enough for regions that are themselves soft.
        // Rough, so everything built on it is kept generous; the landmarks replace this wherever they work.
        static Anchors FromBox(float[] f, Img im)
        {
            float x = f[0] * im.W, y = f[1] * im.H, w = f[2] * im.W, h = f[3] * im.H;
            Anchors a = new Anchors();
            a.EyeRX = x + w * 0.31f; a.EyeLX = x + w * 0.69f;
            a.EyeRY = a.EyeLY = y + h * 0.42f;
            a.EyeR = Math.Max(2f, w * 0.085f);
            a.MouthX = x + w * 0.5f; a.MouthY = y + h * 0.75f; a.MouthHalf = Math.Max(3f, w * 0.19f);
            a.NoseX = x + w * 0.5f; a.NoseY = y + h * 0.60f;
            a.ChinY = y + h * 1.02f;
            a.BrowY = y + h * 0.33f;
            a.Size = w;
            // an oval the shape of a head, as the outline the regions are cut from
            int n = 24;
            a.Hull = new float[n * 2];
            float cx = x + w * 0.5f, cy = y + h * 0.52f, rx = w * 0.52f, ry = h * 0.58f;
            for (int i = 0; i < n; i++)
            {
                // anticlockwise in screen coordinates, to match the hull the landmarks give
                double t = -2 * Math.PI * i / n;
                a.Hull[i * 2] = cx + (float)Math.Cos(t) * rx;
                a.Hull[i * 2 + 1] = cy + (float)Math.Sin(t) * ry;
            }
            a.Wind = Winding(a.Hull);
            return a;
        }

        // Anchors from the five points a detector gives with each face: the two eyes, the nose, and the
        // corners of the mouth. Everything else here is worked out from those, which is enough for regions
        // that are soft at their edges anyway.
        static Anchors FromFive(float[] box, Img im)
        {
            List<FaceDetect.Found> got = FaceDetect.Installed ? FaceDetect.Detect(im) : null;
            if (got == null) return null;
            // the detected face that matches this box best
            FaceDetect.Found best = null;
            float bestOver = 0;
            foreach (FaceDetect.Found g in got)
            {
                float x1 = Math.Max(box[0], g.X), y1 = Math.Max(box[1], g.Y);
                float x2 = Math.Min(box[0] + box[2], g.X + g.W), y2 = Math.Min(box[1] + box[3], g.Y + g.H);
                float over = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
                if (over > bestOver) { bestOver = over; best = g; }
            }
            if (best == null || best.Pts == null) return null;
            float[] P = best.Pts;
            Anchors a = new Anchors();
            a.EyeRX = P[0] * im.W; a.EyeRY = P[1] * im.H;
            a.EyeLX = P[2] * im.W; a.EyeLY = P[3] * im.H;
            float span = (float)Math.Sqrt((a.EyeLX - a.EyeRX) * (a.EyeLX - a.EyeRX) + (a.EyeLY - a.EyeRY) * (a.EyeLY - a.EyeRY));
            span = Math.Max(span, best.W * im.W * 0.25f);          // a face in profile has its eyes close together
            a.EyeR = Math.Max(2f, span * 0.22f);
            a.NoseX = P[4] * im.W; a.NoseY = P[5] * im.H;
            a.MouthX = (P[6] + P[8]) * 0.5f * im.W; a.MouthY = (P[7] + P[9]) * 0.5f * im.H;
            a.MouthHalf = Math.Max(3f, Math.Abs(P[6] - P[8]) * im.W * 0.5f + span * 0.12f);
            a.BrowY = (a.EyeLY + a.EyeRY) * 0.5f - span * 0.28f;
            a.ChinY = a.MouthY + (a.MouthY - a.NoseY) * 1.6f;
            a.Size = Math.Max(span * 2.6f, best.W * im.W);
            // an oval over the face the detector marked, a little taller than the box to take in the chin
            int n = 24;
            a.Hull = new float[n * 2];
            float cx = (best.X + best.W * 0.5f) * im.W, cy = (best.Y + best.H * 0.55f) * im.H;
            float rx = best.W * im.W * 0.56f, ry = best.H * im.H * 0.62f;
            for (int i = 0; i < n; i++)
            {
                double t = -2 * Math.PI * i / n;
                a.Hull[i * 2] = cx + (float)Math.Cos(t) * rx;
                a.Hull[i * 2 + 1] = cy + (float)Math.Sin(t) * ry;
            }
            a.Wind = Winding(a.Hull);
            return a;
        }

        static Anchors Find(float[] L, Img im)
        {
            if (L == null || L.Length < 468 * 3) return null;
            Anchors a = new Anchors();
            float lx = 0, ly = 0, rx = 0, ry = 0;
            foreach (int i in FaceMesh.LeftEye) { lx += L[i * 3]; ly += L[i * 3 + 1]; }
            foreach (int i in FaceMesh.RightEye) { rx += L[i * 3]; ry += L[i * 3 + 1]; }
            a.EyeLX = lx / FaceMesh.LeftEye.Length; a.EyeLY = ly / FaceMesh.LeftEye.Length;
            a.EyeRX = rx / FaceMesh.RightEye.Length; a.EyeRY = ry / FaceMesh.RightEye.Length;
            float eyeSpan = (float)Math.Sqrt((a.EyeLX - a.EyeRX) * (a.EyeLX - a.EyeRX) + (a.EyeLY - a.EyeRY) * (a.EyeLY - a.EyeRY));
            a.EyeR = Math.Max(2f, eyeSpan * 0.22f);
            float mlx = L[FaceMesh.MouthLeft * 3], mly = L[FaceMesh.MouthLeft * 3 + 1];
            float mrx = L[FaceMesh.MouthRight * 3], mry = L[FaceMesh.MouthRight * 3 + 1];
            a.MouthX = (mlx + mrx) * 0.5f; a.MouthY = (mly + mry) * 0.5f;
            a.MouthHalf = Math.Max(3f, (float)Math.Sqrt((mlx - mrx) * (mlx - mrx) + (mly - mry) * (mly - mry)) * 0.5f);
            a.NoseX = L[FaceMesh.NoseTip * 3]; a.NoseY = L[FaceMesh.NoseTip * 3 + 1];
            a.ChinY = L[FaceMesh.Chin * 3 + 1];
            // the brow line sits a little above the eyes, by a share of how far apart they are
            a.BrowY = (a.EyeLY + a.EyeRY) * 0.5f - eyeSpan * 0.28f;
            a.Size = eyeSpan * 2.6f;
            // the outline of every point, which follows the real shape of the face closer than a box does
            float[] xs = new float[468], ys = new float[468];
            for (int i = 0; i < 468; i++) { xs[i] = L[i * 3]; ys[i] = L[i * 3 + 1]; }
            a.Hull = Hull(xs, ys);
            a.Wind = Winding(a.Hull);
            return a;
        }

        // the outline around a set of points, anticlockwise (a monotone chain)
        static float[] Hull(float[] xs, float[] ys)
        {
            int n = xs.Length;
            int[] idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Array.Sort(idx, (p, q) => xs[p] != xs[q] ? xs[p].CompareTo(xs[q]) : ys[p].CompareTo(ys[q]));
            int[] st = new int[2 * n + 1];
            int k = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = k;
                for (int t = 0; t < n; t++)
                {
                    int i = pass == 0 ? idx[t] : idx[n - 1 - t];
                    while (k >= start + 2 && Cross(xs[st[k - 2]], ys[st[k - 2]], xs[st[k - 1]], ys[st[k - 1]], xs[i], ys[i]) <= 0) k--;
                    st[k++] = i;
                }
                k--;
            }
            float[] r = new float[k * 2];
            for (int i = 0; i < k; i++) { r[i * 2] = xs[st[i]]; r[i * 2 + 1] = ys[st[i]]; }
            return r;
        }
        static float Cross(float ax, float ay, float bx, float by, float cx, float cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        // which way round a ring of points runs: positive one way, negative the other. Both the outline of the
        // landmarks and the oval stood in for a face have to be read the same way, and they are not built the
        // same way, so the direction is measured rather than assumed. Getting this wrong puts every point
        // outside the face and the whole atlas comes back empty.
        static float Winding(float[] hull)
        {
            int n = hull.Length / 2;
            float a = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += hull[i * 2] * hull[j * 2 + 1] - hull[j * 2] * hull[i * 2 + 1];
            }
            return a >= 0 ? 1f : -1f;
        }

        // how far inside the outline a point is, in pixels (negative outside)
        static float InHull(float[] hull, float x, float y, float wind)
        {
            int n = hull.Length / 2;
            float worst = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ax = hull[i * 2], ay = hull[i * 2 + 1], bx = hull[j * 2], by = hull[j * 2 + 1];
                float ex = bx - ax, ey = by - ay;
                float len = (float)Math.Sqrt(ex * ex + ey * ey);
                if (len < 1e-3f) continue;
                // the edge crossed into the point: positive inside, once the direction the ring runs is taken
                // out. The other way round put the middle of a face 169 pixels outside it.
                float d = ((y - ay) * ex - (x - ax) * ey) / len * wind;
                if (d < worst) worst = d;
            }
            return worst == float.MaxValue ? 0 : worst;
        }

        static float Soft(float v, float a, float b) { return Ops.SmoothStep(a, b, v); }
        static float Blob(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / Math.Max(1e-3f, rx), dy = (y - cy) / Math.Max(1e-3f, ry);
            return 1 - Ops.SmoothStep(0.75f, 1.15f, (float)Math.Sqrt(dx * dx + dy * dy));
        }

        // ---------------- the atlas ----------------
        class Kept { public float[] Print; public string Faces; public int W, H; public Atlas A; }
        static readonly List<Kept> kept = new List<Kept>();
        public static void Forget() { lock (kept) kept.Clear(); }

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

        public static Atlas Build(Img src, List<float[]> faces, string facesKey)
        {
            float[] print = Print(src);
            lock (kept)
                foreach (Kept k in kept)
                {
                    if (k.W != src.W || k.H != src.H || k.Faces != facesKey) continue;
                    bool same = true;
                    for (int i = 0; i < print.Length && same; i++) if (Math.Abs(k.Print[i] - print[i]) > 0.004f) same = false;
                    if (same) return k.A;
                }
            Atlas a = Make(src, faces);
            lock (kept) { kept.Add(new Kept { Print = print, Faces = facesKey, W = src.W, H = src.H, A = a }); while (kept.Count > 2) kept.RemoveAt(0); }
            return a;
        }

        static Atlas Make(Img src, List<float[]> faces)
        {
            int w = src.W, h = src.H, n = w * h;
            Atlas a = new Atlas { W = w, H = h };
            a.Skin = new float[n];
            a.Forehead = new float[n]; a.Cheeks = new float[n]; a.UnderEye = new float[n];
            a.Nasolabial = new float[n]; a.NoseChin = new float[n];
            List<Anchors> all = new List<Anchors>();
            int fromMesh = 0;
            if (faces != null)
                foreach (float[] f in faces)
                {
                    float[] L = FaceMesh.Available ? FaceMesh.Landmarks(src, f) : null;
                    Anchors an = Find(L, src);
                    if (an != null) fromMesh++;
                    // No landmarks - a face turned too far, or hidden behind a hand. The detector's own five
                    // points say where the eyes, nose and mouth corners really are, which beats guessing them
                    // from the box; failing those, the box's proportions, which is still far better than
                    // treating the whole photograph as skin.
                    else an = FromFive(f, src) ?? FromBox(f, src);
                    all.Add(an);
                }
            a.FromMesh = fromMesh > 0;
            a.Note = string.Format("faces {0}, anchors {1}, from mesh {2}", faces == null ? 0 : faces.Count, all.Count, fromMesh);
            if (all.Count > 0)
            {
                Anchors a0 = all[0];
                a.Note += string.Format("; hull {0} points, winding {1}, centre eye {2:N0},{3:N0} size {4:N0}, inside at the nose {5:N1}",
                    a0.Hull.Length / 2, a0.Wind, a0.EyeLX, a0.EyeLY, a0.Size, InHull(a0.Hull, a0.NoseX, a0.NoseY, a0.Wind));
            }
            a.FaceSize = all.Count > 0 ? all[0].Size : (faces != null && faces.Count > 0 ? faces[0][2] * w : src.MinDim * 0.3f);

            // where the regions are, before any colour is looked at
            float[] reg = new float[n];     // anywhere on the face
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float face = 0, fore = 0, cheek = 0, under = 0, naso = 0, nose = 0, cut = 0;
                    foreach (Anchors an in all)
                    {
                        float inside = InHull(an.Hull, x, y, an.Wind);
                        // a little way inside the outline, so the hairline and the jaw edge are left alone
                        float f = Soft(inside, -an.Size * 0.02f, an.Size * 0.06f);
                        if (f <= 0) continue;
                        face = Math.Max(face, f);
                        float eyeMid = (an.EyeLY + an.EyeRY) * 0.5f;
                        // eyes, brows, lips and nostrils are not skin and must never be smoothed or healed
                        cut = Math.Max(cut, Blob(x, y, an.EyeLX, an.EyeLY, an.EyeR * 1.5f, an.EyeR * 1.1f));
                        cut = Math.Max(cut, Blob(x, y, an.EyeRX, an.EyeRY, an.EyeR * 1.5f, an.EyeR * 1.1f));
                        cut = Math.Max(cut, Blob(x, y, an.EyeLX, an.BrowY, an.EyeR * 1.6f, an.EyeR * 0.75f));
                        cut = Math.Max(cut, Blob(x, y, an.EyeRX, an.BrowY, an.EyeR * 1.6f, an.EyeR * 0.75f));
                        cut = Math.Max(cut, Blob(x, y, an.MouthX, an.MouthY, an.MouthHalf * 1.25f, an.MouthHalf * 0.8f));
                        cut = Math.Max(cut, Blob(x, y, an.NoseX, an.NoseY + an.Size * 0.02f, an.Size * 0.075f, an.Size * 0.045f));
                        // forehead: above the brows
                        fore = Math.Max(fore, f * (1 - Soft(y, an.BrowY - an.Size * 0.06f, an.BrowY)));
                        // cheeks: under the eyes, above the mouth, out to the sides of the nose
                        float side = Soft(Math.Abs(x - an.NoseX), an.Size * 0.08f, an.Size * 0.17f);
                        float band = Soft(y, eyeMid + an.EyeR * 0.6f, eyeMid + an.EyeR * 1.4f) * (1 - Soft(y, an.MouthY - an.MouthHalf * 0.6f, an.MouthY + an.MouthHalf * 0.3f));
                        cheek = Math.Max(cheek, f * side * band);
                        // under the eyes: a band right below each one
                        under = Math.Max(under, f * Math.Max(Blob(x, y, an.EyeLX, an.EyeLY + an.EyeR * 1.35f, an.EyeR * 1.5f, an.EyeR * 0.85f),
                                                             Blob(x, y, an.EyeRX, an.EyeRY + an.EyeR * 1.35f, an.EyeR * 1.5f, an.EyeR * 0.85f)));
                        // the folds from the sides of the nose down past the mouth corners
                        float nx1 = an.NoseX - an.Size * 0.09f, nx2 = an.NoseX + an.Size * 0.09f;
                        float ny = an.NoseY + an.Size * 0.03f;
                        naso = Math.Max(naso, f * Math.Max(Band(x, y, nx1, ny, an.MouthX - an.MouthHalf, an.MouthY + an.MouthHalf * 0.4f, an.Size * 0.05f),
                                                           Band(x, y, nx2, ny, an.MouthX + an.MouthHalf, an.MouthY + an.MouthHalf * 0.4f, an.Size * 0.05f)));
                        // the nose itself and the chin
                        nose = Math.Max(nose, f * Math.Max(Blob(x, y, an.NoseX, an.NoseY - an.Size * 0.06f, an.Size * 0.09f, an.Size * 0.13f),
                                                           Blob(x, y, an.MouthX, (an.MouthY + an.ChinY) * 0.5f, an.MouthHalf * 0.9f, Math.Abs(an.ChinY - an.MouthY) * 0.45f)));
                    }
                    float keep = 1 - cut;
                    reg[i] = face;
                    a.Forehead[i] = fore * keep; a.Cheeks[i] = cheek * keep; a.UnderEye[i] = under * keep;
                    a.Nasolabial[i] = naso * keep; a.NoseChin[i] = nose * keep;
                    a.Skin[i] = face * keep;        // colour narrows this next
                }
            });

            // this face's own skin colour, from its cheeks and forehead only: not the lips, not the eyes, and
            // not whatever is behind the head
            List<float> cbs = new List<float>(), crs = new List<float>();
            int step = Math.Max(1, (int)Math.Sqrt(n / 20000.0));
            for (int y = 0; y < h; y += step)
                for (int x = 0; x < w; x += step)
                {
                    int i = y * w + x;
                    if (a.Cheeks[i] + a.Forehead[i] < 0.6f) continue;
                    int j = i * 3;
                    float l = Ops.Lum(src.D[j], src.D[j + 1], src.D[j + 2]);
                    if (l < 0.08f || l > 0.97f) continue;    // deep shadow and blown highlights say nothing about colour
                    float cb, cr;
                    Chroma(src.D[j], src.D[j + 1], src.D[j + 2], out cb, out cr);
                    cbs.Add(cb); crs.Add(cr);
                }
            if (cbs.Count >= 50)
            {
                a.CbMid = Median(cbs); a.CrMid = Median(crs);
                a.CbSpread = Mad(cbs, a.CbMid); a.CrSpread = Mad(crs, a.CrMid);
            }
            else
            {
                // no usable face: the old fixed idea of skin colour, so the tool still does something sane
                a.CbMid = 110f; a.CrMid = 152f; a.CbSpread = 16f; a.CrSpread = 13f;
                a.FromMesh = false;
            }

            // narrow the skin by how close each pixel is to that colour. Generous across Cr, because redness is
            // a move along Cr and a blemish is still skin - cutting those out would hide the very things the
            // defect maps are looking for.
            float icb = 1f / Math.Max(2f, a.CbSpread * 2.6f), icr = 1f / Math.Max(2f, a.CrSpread * 3.4f);
            float[] skin = a.Skin;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (skin[i] <= 0) continue;
                    int j = i * 3;
                    float cb, cr;
                    Chroma(src.D[j], src.D[j + 1], src.D[j + 2], out cb, out cr);
                    float dcb = (cb - a.CbMid) * icb, dcr = (cr - a.CrMid) * icr;
                    float l = Ops.Lum(src.D[j], src.D[j + 1], src.D[j + 2]);
                    float lw = Ops.SmoothStep(0.05f, 0.14f, l) * (1 - Ops.SmoothStep(0.96f, 1f, l));
                    skin[i] *= (float)Math.Exp(-(dcb * dcb + dcr * dcr) * 0.5) * lw;
                }
            });
            // a hair's width of blur so the edges of all this are not stepped
            float soft = Math.Max(1f, a.FaceSize * 0.006f);
            a.Skin = Ops.Gauss(a.Skin, w, h, soft);
            a.Forehead = Ops.Gauss(a.Forehead, w, h, soft);
            a.Cheeks = Ops.Gauss(a.Cheeks, w, h, soft);
            a.UnderEye = Ops.Gauss(a.UnderEye, w, h, soft);
            a.Nasolabial = Ops.Gauss(a.Nasolabial, w, h, soft);
            a.NoseChin = Ops.Gauss(a.NoseChin, w, h, soft);
            return a;
        }

        // a soft band along the line from one point to another
        static float Band(float x, float y, float ax, float ay, float bx, float by, float half)
        {
            float ex = bx - ax, ey = by - ay;
            float len2 = ex * ex + ey * ey;
            if (len2 < 1e-3f) return 0;
            float t = ((x - ax) * ex + (y - ay) * ey) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            float px = ax + ex * t, py = ay + ey * t;
            float d = (float)Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            return 1 - Ops.SmoothStep(half * 0.6f, half * 1.6f, d);
        }
    }
}

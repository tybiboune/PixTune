using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PixTune
{
    // One-tap masks: subject, background or sky, computed from the photo itself (no network, no model files).
    // Subject: colour contrast against the photo's border colours, weighted towards the centre, kept as the
    // main connected region. Sky: smooth, bright or blue areas connected to the top edge.
    // Both are refined with an edge-aware filter so the mask follows real edges.
    public static class SmartMask
    {
        // The same picture asks for the same mask on every frame while a tool is used: the last few are kept
        // (by picture, so a changed photo or earlier edit computes a new one). Callers must not change the result.
        class Kept { public Img Im; public string Kind; public float[] M; }
        static readonly List<Kept> kept = new List<Kept>();

        static readonly object computeLk = new object();

        public static float[] Compute(Img im, string kind)
        {
            float[] got = Kept1(im, kind);
            if (got != null) return got;
            // one mask at a time: the window and the renderer often ask for the same one at once, and the one that
            // waited then takes the other's result
            lock (computeLk)
            {
                got = Kept1(im, kind);
                if (got != null) return got;
                float[] m = ComputeNew(im, kind);
                // a stand-in made while the AI models are installed (still loading, or interrupted) is not kept
                if (lastWasRules && Segmenter.Installed) return m;
                lock (kept)
                {
                    kept.Add(new Kept { Im = im, Kind = kind, M = m });
                    while (kept.Count > 4) kept.RemoveAt(0);
                }
                return m;
            }
        }
        static float[] Kept1(Img im, string kind)
        {
            lock (kept) foreach (Kept k in kept) if (k.Im == im && k.Kind == kind) return k.M;
            return null;
        }

        public static void Forget() { lock (kept) kept.Clear(); }

        [ThreadStatic] static bool lastWasRules;

        static float[] ComputeNew(Img im, string kind)
        {
            // AI models when installed (Segmenter); the colour and position rules below otherwise
            float[] ai = ComputeAi(im, kind);
            lastWasRules = ai == null;
            if (ai != null) return ai;
            Img s = im.FitInside(480);
            int w = s.W, h = s.H;
            float[] m;
            if (kind == "sky") m = Sky(s);
            else m = Subject(s);
            if (kind == "background") for (int i = 0; i < m.Length; i++) m[i] = 1 - m[i];
            float[] L = Ops.Luma(s);
            m = Ops.Guided(L, m, w, h, Math.Max(2, Math.Min(w, h) / 70), 0.0015f);
            for (int i = 0; i < m.Length; i++) m[i] = Ops.SmoothStep(0.25f, 0.75f, m[i]);
            return (w == im.W && h == im.H) ? m : Ops.ResizePlane(m, w, h, im.W, im.H);
        }

        // The AI mask at working size, refined with an edge-aware filter on the photo (so it follows the
        // horizon, hair or foliage closely), then scaled to the picture asked for.
        static float[] ComputeAi(Img im, string kind)
        {
            Img s = im.FitInside(Segmenter.Work);
            float[] m = Segmenter.Mask(s, kind == "sky" ? "sky" : "subject");
            if (m == null) return null;
            m = (float[])m.Clone();
            int w = s.W, h = s.H;
            float[] L = Ops.Luma(s);
            if (kind == "sky")
            {
                // the model's sky map is coarse (a quarter of its input): the filter snaps it to the real edges
                m = Ops.Guided(L, m, w, h, Math.Max(2, Math.Min(w, h) / 120), 0.0008f);
                for (int i = 0; i < m.Length; i++) m[i] = Ops.SmoothStep(0.3f, 0.7f, m[i]);
            }
            else
            {
                // the subject model already has fine edges: only a light clean-up
                m = Ops.Guided(L, m, w, h, 2, 0.0004f);
                for (int i = 0; i < m.Length; i++) m[i] = Ops.SmoothStep(0.15f, 0.85f, m[i]);
                if (kind == "background") for (int i = 0; i < m.Length; i++) m[i] = 1 - m[i];
            }
            return (w == im.W && h == im.H) ? m : Ops.ResizePlane(m, w, h, im.W, im.H);
        }

        // Sky: region grown from the top edge through smooth areas, stepping only between neighbours
        // whose colours are close. Gradients (blue skies, sunsets, overcast) pass; the horizon, trees
        // and buildings stop it because they are textured or change colour abruptly.
        static float[] Sky(Img s)
        {
            int w = s.W, h = s.H, n = w * h;
            Img sm = Ops.GaussImg(s, 1.2f);
            float[] D = sm.D;
            float[] L = Ops.Luma(s);
            float[] bl = Ops.Gauss(L, w, h, 1.5f);
            float[] tex = new float[n];
            for (int i = 0; i < n; i++) tex[i] = Math.Abs(L[i] - bl[i]);
            tex = Ops.Gauss(tex, w, h, 2.5f);
            float texLim = 0.018f;
            float step = 0.035f;          // max colour change between neighbours
            float[] m = new float[n];
            Queue<int> q = new Queue<int>();
            for (int x = 0; x < w; x++) if (tex[x] < texLim) { m[x] = 1; q.Enqueue(x); }
            int maxY = Math.Min((int)(h * 0.85f), Horizon(L, w, h));
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w, y = i / w;
                int[] nb = { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < maxY ? i + w : -1 };
                foreach (int k in nb)
                {
                    if (k < 0 || m[k] != 0 || tex[k] > texLim) continue;
                    float dr = D[i * 3] - D[k * 3], dg = D[i * 3 + 1] - D[k * 3 + 1], db = D[i * 3 + 2] - D[k * 3 + 2];
                    if (dr * dr + dg * dg + db * db > step * step) continue;
                    m[k] = 1; q.Enqueue(k);
                }
            }
            // a sky covering almost nothing is noise from the top edge
            int cnt = 0; foreach (float v in m) if (v > 0) cnt++;
            if (cnt < n * 0.02f) return new float[n];
            return m;
        }

        static float[] Subject(Img s)
        {
            int w = s.W, h = s.H, n = w * h;
            Img sm = Ops.GaussImg(s, 1.5f);
            float[] D = sm.D;
            // colour model of the border (8 clusters, a few k-means rounds)
            List<float[]> border = new List<float[]>();
            int mg = Math.Max(2, Math.Min(w, h) / 25);
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                    if (x < mg || y < mg || x >= w - mg || y >= h - mg)
                    {
                        int j = (y * w + x) * 3;
                        border.Add(new float[] { D[j], D[j + 1], D[j + 2] });
                    }
            int K = Math.Min(8, Math.Max(1, border.Count));
            float[][] c = new float[K][];
            for (int k = 0; k < K; k++) c[k] = (float[])border[k * border.Count / K].Clone();
            for (int it = 0; it < 6; it++)
            {
                double[][] acc = new double[K][]; int[] cnt = new int[K];
                for (int k = 0; k < K; k++) acc[k] = new double[3];
                foreach (float[] p in border)
                {
                    int bk = 0; float bd = float.MaxValue;
                    for (int k = 0; k < K; k++) { float d = Dist2(p, c[k]); if (d < bd) { bd = d; bk = k; } }
                    acc[bk][0] += p[0]; acc[bk][1] += p[1]; acc[bk][2] += p[2]; cnt[bk]++;
                }
                for (int k = 0; k < K; k++) if (cnt[k] > 0) c[k] = new float[] { (float)(acc[k][0] / cnt[k]), (float)(acc[k][1] / cnt[k]), (float)(acc[k][2] / cnt[k]) };
            }
            // saliency: distance to the nearest border colour, favouring the centre of the frame
            float[] sal = new float[n];
            float cx = w / 2f, cy = h / 2f, sx = w * 0.38f, sy = h * 0.42f;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    float[] p = { D[j], D[j + 1], D[j + 2] };
                    float bd = float.MaxValue;
                    for (int k = 0; k < K; k++) { float d = Dist2(p, c[k]); if (d < bd) bd = d; }
                    float dx = (x - cx) / sx, dy = (y - cy) / sy;
                    float center = (float)Math.Exp(-(dx * dx + dy * dy) * 0.5f);
                    sal[i] = (float)Math.Sqrt(bd) * (0.35f + 0.65f * center);
                }
            });
            sal = Ops.Gauss(sal, w, h, 2f);
            // Otsu threshold
            float mx = 1e-6f; foreach (float v in sal) if (v > mx) mx = v;
            int[] hist = new int[64];
            foreach (float v in sal) hist[Math.Min(63, (int)(v / mx * 63))]++;
            double sum = 0; for (int i = 0; i < 64; i++) sum += i * hist[i];
            double sumB = 0, wB = 0, best = -1; int th = 32;
            for (int i = 0; i < 64; i++)
            {
                wB += hist[i]; if (wB == 0) continue;
                double wF = n - wB; if (wF == 0) break;
                sumB += i * hist[i];
                double mB = sumB / wB, mF = (sum - sumB) / wF, between = wB * wF * (mB - mF) * (mB - mF);
                if (between > best) { best = between; th = i; }
            }
            float thr = (th + 0.5f) / 63f * mx;
            byte[] bin = new byte[n];
            for (int i = 0; i < n; i++) bin[i] = (byte)(sal[i] > thr ? 1 : 0);
            // keep the main region(s), then fill holes
            int[] lab = new int[n]; List<int> sizes = new List<int> { 0 };
            for (int i = 0; i < n; i++)
            {
                if (bin[i] == 0 || lab[i] != 0) continue;
                int id = sizes.Count, size = 0;
                Queue<int> q = new Queue<int>(); q.Enqueue(i); lab[i] = id;
                while (q.Count > 0)
                {
                    int p = q.Dequeue(); size++;
                    int x = p % w, y = p / w;
                    if (x > 0 && bin[p - 1] == 1 && lab[p - 1] == 0) { lab[p - 1] = id; q.Enqueue(p - 1); }
                    if (x < w - 1 && bin[p + 1] == 1 && lab[p + 1] == 0) { lab[p + 1] = id; q.Enqueue(p + 1); }
                    if (y > 0 && bin[p - w] == 1 && lab[p - w] == 0) { lab[p - w] = id; q.Enqueue(p - w); }
                    if (y < h - 1 && bin[p + w] == 1 && lab[p + w] == 0) { lab[p + w] = id; q.Enqueue(p + w); }
                }
                sizes.Add(size);
            }
            int biggest = 0; foreach (int z in sizes) biggest = Math.Max(biggest, z);
            float[] m = new float[n];
            for (int i = 0; i < n; i++) if (lab[i] > 0 && sizes[lab[i]] >= biggest * 0.25f) m[i] = 1;
            // holes: background is what the border can reach
            byte[] outside = new byte[n];
            Queue<int> fq = new Queue<int>();
            for (int x = 0; x < w; x++) { Seed(fq, outside, m, x); Seed(fq, outside, m, (h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Seed(fq, outside, m, y * w); Seed(fq, outside, m, y * w + w - 1); }
            while (fq.Count > 0)
            {
                int p = fq.Dequeue(), x = p % w, y = p / w;
                if (x > 0) Seed(fq, outside, m, p - 1);
                if (x < w - 1) Seed(fq, outside, m, p + 1);
                if (y > 0) Seed(fq, outside, m, p - w);
                if (y < h - 1) Seed(fq, outside, m, p + w);
            }
            for (int i = 0; i < n; i++) if (outside[i] == 0) m[i] = 1;
            return m;
        }

        // Row of a clear horizon (strongest horizontal edge between 25% and 80% of the height, if it
        // stands out); the sky is not allowed below it, so reflections in water stay out of the mask.
        static int Horizon(float[] L, int w, int h)
        {
            float[] rowEdge = new float[h];
            for (int y = 1; y < h - 1; y++)
            {
                float s = 0;
                for (int x = 0; x < w; x++) s += Math.Abs(L[(y + 1) * w + x] - L[(y - 1) * w + x]);
                rowEdge[y] = s / w;
            }
            int y0 = (int)(h * 0.25f), y1 = (int)(h * 0.8f), best = -1; float bv = 0;
            List<float> vals = new List<float>();
            for (int y = y0; y < y1; y++) { vals.Add(rowEdge[y]); if (rowEdge[y] > bv) { bv = rowEdge[y]; best = y; } }
            if (best < 0) return h;
            vals.Sort();
            float median = vals[vals.Count / 2];
            return bv > median * 2.5f && bv > 0.02f ? best + 2 : h;
        }

        static void Seed(Queue<int> q, byte[] seen, float[] m, int i) { if (seen[i] == 0 && m[i] < 0.5f) { seen[i] = 1; q.Enqueue(i); } }
        static float Dist2(float[] a, float[] b) { float dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2]; return dr * dr * 0.9f + dg * dg * 1.2f + db * db * 0.9f; }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PixTune
{
    // Content-aware fill for the Healing tool: multi-scale patch-based image completion
    // (Wexler, Shechtman & Irani 2007) driven by PatchMatch nearest-neighbour search
    // (Barnes, Shechtman, Finkelstein & Goldman 2009) - the approach behind content-aware fill
    // in common photo editors. Written from the published algorithms.
    //
    // Outline: crop the area around the hole, build an image pyramid down to a level where the hole
    // is only a few pixels, fill it smoothly there, then at each finer level alternate
    //   1. PatchMatch: for every patch touching the hole, find the most similar patch outside the hole;
    //   2. voting: each hole pixel becomes the weighted average of what the matched patches say it is,
    // and carry the result and the matches up to the next level.
    public static class Inpaint
    {
        const int PR = 3;                 // patch radius: 7x7 patches
        const int PN = (2 * PR + 1) * (2 * PR + 1);
        const long HugePixels = 2000000;  // levels larger than this get fewer, cheaper refinement passes

        // Fills the area covered by cov (0..1 per pixel) in place. seed makes the result repeatable.
        // ai: use the LaMa model when it is installed (falls back to content-aware fill otherwise).
        public static void Fill(Img im, float[] cov, int seed) { Fill(im, cov, seed, false); }

        public static void Fill(Img im, float[] cov, int seed, bool ai) { Fill(im, cov, seed, ai ? Engine.Lama : Engine.Patches, null, null); }

        public enum Engine { Patches, Lama, Generative }

        // focus: when given, only these pixels (a part of the hole) are written and the window is placed
        // around them; the rest of the hole stays unknown so it is never copied from.
        static void Fill(Img im, float[] cov, int seed, Engine engine, bool[] focus, string prompt)
        {
            if (engine == Engine.Generative && !Diffusion.Available) engine = Engine.Lama;
            bool ai = engine == Engine.Lama && Lama.Available;
            int W = im.W, H = im.H;
            int x0 = W, y0 = H, x1 = -1, y1 = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (focus != null ? focus[y * W + x] : cov[y * W + x] > 0.02f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) return;
            int hs = Math.Max(x1 - x0 + 1, y1 - y0 + 1);
            int cx0, cy0, cw, ch;
            if (engine == Engine.Generative)
            {
                // about twice the hole: the model gets as much photo as new area, at roughly its native 512 px
                int side = (int)Math.Max(hs * 2f + 32, 384);
                cw = Math.Min(side, W); ch = Math.Min(side, H);
                cx0 = Math.Max(0, Math.Min(W - cw, (x0 + x1) / 2 - cw / 2));
                cy0 = Math.Max(0, Math.Min(H - ch, (y0 + y1) / 2 - ch / 2));
            }
            else if (ai)
            {
                // square window about twice the hole: LaMa sees the surroundings it needs at 512x512
                int side = (int)Math.Max(hs * 2.2f + 32, 256);
                cw = Math.Min(side, W); ch = Math.Min(side, H);
                cx0 = Math.Max(0, Math.Min(W - cw, (x0 + x1) / 2 - cw / 2));
                cy0 = Math.Max(0, Math.Min(H - ch, (y0 + y1) / 2 - ch / 2));
            }
            else
            {
                // context around the hole the fill may copy from
                int mg = Math.Max(24, (int)(hs * 1.6f));
                cx0 = Math.Max(0, x0 - mg); cy0 = Math.Max(0, y0 - mg);
                cw = Math.Min(W - 1, x1 + mg) - cx0 + 1; ch = Math.Min(H - 1, y1 + mg) - cy0 + 1;
            }
            Img crop = new Img(cw, ch);
            bool[] hole = new bool[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                Array.Copy(im.D, ((cy0 + y) * W + cx0) * 3, crop.D, y * cw * 3, cw * 3);
                for (int x = 0; x < cw; x++) hole[y * cw + x] = cov[(cy0 + y) * W + cx0 + x] > 0.02f;
            }
            hole = Dilate(hole, cw, ch, 1);
            Img done = null;
            if (engine == Engine.Generative) done = RunDiffusion(crop, hole, prompt, seed, Complete(crop, hole, seed));
            else if (ai)
            {
                Img aiOut = RunLama(crop, hole);
                // used as is: refining it with patches was tested and made large removals worse
                if (aiOut != null) done = aiOut;
            }
            if (done == null) done = Complete(crop, hole, seed);
            // blend back, keeping the soft edge of the brush
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int ci = y * cw + x, gi = (cy0 + y) * W + cx0 + x;
                    if (focus != null && !focus[gi]) continue;
                    float a = hole[ci] ? Math.Max(Ops.SmoothStep(0f, 0.5f, cov[gi]), cov[gi] > 0.02f ? 1f : 0.5f) : 0f;
                    if (a <= 0) continue;
                    for (int c = 0; c < 3; c++) im.D[gi * 3 + c] = Ops.Lerp(im.D[gi * 3 + c], done.D[ci * 3 + c], a);
                }
        }

        // Fills every pixel with valid < 0.5 (the new border of an expanded photo), in square chunks of
        // about the border's width, nearest to the photo first, so each chunk sees real or already
        // completed surroundings.
        public static void FillOutside(Img im, float[] valid, int seed, Engine engine, string prompt)
        {
            int W = im.W, H = im.H;
            float[] cov = new float[W * H];
            int bx0 = W, by0 = H, bx1 = -1, by1 = -1, missing = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (valid[i] < 0.5f) { cov[i] = 1; missing++; continue; }
                    if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (y < by0) by0 = y; if (y > by1) by1 = y;
                }
            if (missing == 0 || bx1 < 0) return;
            if (engine == Engine.Generative && Diffusion.Available) { GenerateOutside(im, cov, bx0, by0, bx1, by1, seed, prompt); return; }
            int pad = Math.Max(Math.Max(bx0, W - 1 - bx1), Math.Max(by0, H - 1 - by1));
            // LaMa sees a wider window per run, so fewer, larger chunks
            int c = Math.Max(48, engine == Engine.Lama && Lama.Available ? pad * 2 : pad);
            int nx = (W + c - 1) / c, ny = (H + c - 1) / c;
            List<int[]> cells = new List<int[]>();
            for (int gy = 0; gy < ny; gy++)
                for (int gx = 0; gx < nx; gx++)
                {
                    int x0 = gx * c, y0 = gy * c, x1 = Math.Min(W, x0 + c), y1 = Math.Min(H, y0 + c);
                    int dx = Math.Max(0, Math.Max(bx0 - x1, x0 - bx1)), dy = Math.Max(0, Math.Max(by0 - y1, y0 - by1));
                    cells.Add(new int[] { x0, y0, x1, y1, dx * dx + dy * dy, cells.Count });
                }
            cells.Sort((a, b) => a[4] != b[4] ? a[4].CompareTo(b[4]) : a[5].CompareTo(b[5]));
            bool[] focus = new bool[W * H];
            foreach (int[] cl in cells)
            {
                bool any = false;
                Array.Clear(focus, 0, focus.Length);
                for (int y = cl[1]; y < cl[3]; y++)
                    for (int x = cl[0]; x < cl[2]; x++)
                        if (cov[y * W + x] > 0) { focus[y * W + x] = true; any = true; }
                if (!any) continue;
                Pipeline.CheckCancel();
                Fill(im, cov, seed + cl[5], engine, focus, prompt);
                for (int i = 0; i < focus.Length; i++) if (focus[i]) cov[i] = 0;
            }
        }

        // Generative expansion: each side is generated in as few windows as possible (a generation costs
        // tens of seconds). A window spans up to 1024 px along the side and holds the new strip plus about as
        // much photo, so the model continues what it sees. Left and right go first, then top and bottom,
        // which also finish the corners.
        const int GenMaxAlong = 1024;
        static void GenerateOutside(Img im, float[] cov, int bx0, int by0, int bx1, int by1, int seed, string prompt)
        {
            int W = im.W, H = im.H, run = 0;
            int[] pads = { bx0, W - 1 - bx1, by0, H - 1 - by1 }; // left, right, top, bottom
            for (int side = 0; side < 4; side++)
            {
                int pad = pads[side];
                if (pad <= 0) continue;
                bool vertical = side < 2;                         // a left or right strip runs top to bottom
                int stripLen = vertical ? H : W, acrossMax = vertical ? W : H;
                int n = (stripLen + GenMaxAlong - 1) / GenMaxAlong;
                int along = (stripLen + n - 1) / n;
                int across = Math.Min(acrossMax, Math.Max((int)(pad * 2.2f), (int)(along * 0.6f)));
                for (int k = 0; k < n; k++)
                {
                    Pipeline.CheckCancel();
                    int a0 = n == 1 ? 0 : (int)((long)(stripLen - along) * k / (n - 1));
                    int c0 = side == 0 || side == 2 ? 0 : acrossMax - across;
                    int rx = vertical ? c0 : a0, ry = vertical ? a0 : c0, rw = vertical ? across : along, rh = vertical ? along : across;
                    Img crop = new Img(rw, rh);
                    bool[] hole = new bool[rw * rh];
                    bool any = false;
                    for (int y = 0; y < rh; y++)
                    {
                        Array.Copy(im.D, ((ry + y) * W + rx) * 3, crop.D, y * rw * 3, rw * 3);
                        for (int x = 0; x < rw; x++) if (cov[(ry + y) * W + rx + x] > 0) { hole[y * rw + x] = true; any = true; }
                    }
                    if (!any) continue;
                    // content-aware pre-fill: the starting point of the generation (noised to about 90 %, so its
                    // colours and layout carry over); a smooth colour fill was tried and led to invented objects
                    Img pre = Complete(crop, Dilate(hole, rw, rh, 1), seed);
                    Pipeline.CheckCancel();
                    // the model also redraws a thin band of the photo along the seam, which is then cross-faded,
                    // so a slight difference in tone does not show as a line
                    int band = Math.Max(8, Math.Min(96, pad / 4));
                    int[] ring = new int[rw * rh];
                    bool[] grown = hole;
                    for (int d = 1; d <= band; d++)
                    {
                        grown = Dilate(grown, rw, rh, 1);
                        for (int i = 0; i < ring.Length; i++) if (grown[i] && !hole[i] && ring[i] == 0) ring[i] = d;
                    }
                    Img done = RunDiffusion(crop, grown, prompt, seed + run++, pre);
                    if (done == null) done = pre;
                    else
                    {
                        // tone match: the difference between photo and generation measured in the redrawn band,
                        // spread smoothly over the new area and added to it
                        Img diff = new Img(rw, rh);
                        float[] dw = new float[rw * rh];
                        for (int i = 0; i < dw.Length; i++)
                        {
                            if (ring[i] == 0) continue;
                            dw[i] = 1;
                            for (int c = 0; c < 3; c++) diff.D[i * 3 + c] = crop.D[i * 3 + c] - done.D[i * 3 + c];
                        }
                        // only the broad tone: push-pull alone would copy the photo exactly into the band
                        // and leave nothing for the seam blend to work with
                        Img fix = Ops.FastGaussImg(Ops.PushPull(diff, dw), Math.Max(4f, band * 0.75f));
                        for (int i = 0; i < dw.Length; i++)
                            if (grown[i]) for (int c = 0; c < 3; c++) done.D[i * 3 + c] += fix.D[i * 3 + c];
                    }
                    Img mixed = SeamBlend(crop, done, hole, ring, side, band, seed + 7 * run);
                    for (int y = 0; y < rh; y++)
                        for (int x = 0; x < rw; x++)
                        {
                            int ci = y * rw + x, gi = (ry + y) * W + rx + x;
                            if (!hole[ci] && ring[ci] == 0) continue;
                            im.D[gi * 3] = mixed.D[ci * 3]; im.D[gi * 3 + 1] = mixed.D[ci * 3 + 1]; im.D[gi * 3 + 2] = mixed.D[ci * 3 + 2];
                            cov[gi] = 0;
                        }
                }
            }
        }

        // Joins the generated strip to the photo inside the band the model redrew, so no straight line shows:
        //  1. the seam follows the path where generation and photo differ least (minimum-error boundary cut,
        //     as in image quilting, by dynamic programming along the side), so it slips along natural edges;
        //  2. fractal noise is added to that path's cost, so even in flat sky it meanders instead of running straight;
        //  3. two-band mix: broad tones blend over a wide soft mask, fine detail over a narrow blurred one, which
        //     hides the join without ghosting.
        // side: 0 left, 1 right, 2 top, 3 bottom (where the new strip is); ring: depth into the photo (1..band).
        static Img SeamBlend(Img orig, Img gen, bool[] hole, int[] ring, int side, int band, int seed)
        {
            int w = orig.W, h = orig.H;
            bool vertical = side < 2;
            int along = vertical ? h : w, across = vertical ? w : h;
            bool fromStart = side == 0 || side == 2;
            // pixel index for a position along the seam and a depth measured from the outer (new) edge
            Func<int, int, int> at = (a, c) =>
            {
                int cc = fromStart ? c : across - 1 - c;
                return vertical ? a * w + cc : cc * w + a;
            };
            int[] edge = new int[along];
            for (int a = 0; a < along; a++)
            {
                int c = 0;
                while (c < across && hole[at(a, c)]) c++;
                edge[a] = c;
            }
            // 1 + 2: cheapest path through the band, one depth per position, moving at most one pixel per step
            int span = Math.Max(1, band);
            float[] acc = new float[along * span];
            int[] from = new int[along * span];
            float ns = Math.Max(6f, band * 0.7f);
            for (int a = 0; a < along; a++)
                for (int o = 0; o < span; o++)
                {
                    int c = edge[a] + o;
                    float cost;
                    if (c >= across) cost = 1e3f;
                    else
                    {
                        int i = at(a, c);
                        float d = Math.Abs(orig.D[i * 3] - gen.D[i * 3]) + Math.Abs(orig.D[i * 3 + 1] - gen.D[i * 3 + 1]) + Math.Abs(orig.D[i * 3 + 2] - gen.D[i * 3 + 2]);
                        float t = (float)o / span;
                        // keep away from both ends of the band, leaving room on each side for the soft mix
                        float ends = Math.Max(0f, 0.3f - t) + Math.Max(0f, t - 0.7f);
                        cost = d + 0.12f * Ops.Fbm(a / ns, o / ns, seed, 3) + 0.4f * ends;
                    }
                    int best = o; float bv = 0;
                    if (a > 0)
                    {
                        bv = float.MaxValue;
                        for (int q = Math.Max(0, o - 1); q <= Math.Min(span - 1, o + 1); q++)
                            if (acc[(a - 1) * span + q] < bv) { bv = acc[(a - 1) * span + q]; best = q; }
                    }
                    acc[a * span + o] = cost + bv; from[a * span + o] = best;
                }
            int[] seam = new int[along];
            {
                int o = 0; float bv = float.MaxValue;
                for (int q = 0; q < span; q++) if (acc[(along - 1) * span + q] < bv) { bv = acc[(along - 1) * span + q]; o = q; }
                for (int a = along - 1; a >= 0; a--) { seam[a] = edge[a] + o; o = from[a * span + o]; }
            }
            // 3: masks around the seam (positive towards the generated side)
            float fine = Math.Max(1.5f, band * 0.06f), broad = Math.Max(3f, band * 0.3f);
            float[] aFine = new float[w * h], aBroad = new float[w * h];
            for (int a = 0; a < along; a++)
                for (int c = 0; c < across; c++)
                {
                    int i = at(a, c);
                    float s = seam[a] - c;
                    aFine[i] = Ops.SmoothStep(-fine, fine, s);
                    aBroad[i] = Ops.SmoothStep(-broad, broad, s);
                }
            aFine = Ops.Gauss(aFine, w, h, fine * 0.7f);
            float sigma = Math.Max(2f, band * 0.12f);
            Img bo = Ops.FastGaussImg(orig, sigma), bg = Ops.FastGaussImg(gen, sigma);
            Img o2 = orig.Clone();
            for (int i = 0; i < w * h; i++)
            {
                // only the band and the new strip change; the new strip is always fully generated
                if (hole[i]) { for (int c = 0; c < 3; c++) o2.D[i * 3 + c] = gen.D[i * 3 + c]; continue; }
                if (ring[i] == 0) continue;
                float fa = aFine[i], ba = aBroad[i];
                // fade both masks out towards the inner end of the band
                float keep = 1f - Ops.SmoothStep(band * 0.8f, band, ring[i]);
                fa *= keep; ba *= keep;
                for (int c = 0; c < 3; c++)
                {
                    int k = i * 3 + c;
                    float baseV = Ops.Lerp(bo.D[k], bg.D[k], ba);
                    float detail = Ops.Lerp(orig.D[k] - bo.D[k], gen.D[k] - bg.D[k], fa);
                    o2.D[k] = baseV + detail;
                }
            }
            return o2;
        }

        // Stable Diffusion on a window, scaled to about 512x512 with sides that are multiples of 64.
        // Returns null when the model could not run (the caller then completes with patches).
        static Img RunDiffusion(Img crop, bool[] hole, string prompt, int seed, Img init)
        {
            double k = Math.Sqrt(512.0 * 512.0 / ((double)crop.W * crop.H));
            int sw = Math.Max(256, Math.Min(896, (int)Math.Round(crop.W * k / 64) * 64));
            int sh = Math.Max(256, Math.Min(896, (int)Math.Round(crop.H * k / 64) * 64));
            Img small = Ops.Resize(crop, sw, sh);
            bool[] m = new bool[sw * sh];
            float sx = (float)crop.W / sw, sy = (float)crop.H / sh;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    int cx = Math.Min(crop.W - 1, (int)((x + 0.5f) * sx)), cy = Math.Min(crop.H - 1, (int)((y + 0.5f) * sy));
                    m[y * sw + x] = hole[cy * crop.W + cx];
                }
            m = Dilate(m, sw, sh, 2);
            Img o;
            try { o = Diffusion.Inpaint(small, m, prompt, seed, init == null ? null : Ops.Resize(init, sw, sh)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Diffusion.LastError = ex.Message; return null; }
            if (o == null) return null;
            return Ops.Resize(o, crop.W, crop.H);
        }

        // LaMa on a window: scaled to 512x512 (hole mask slightly grown to swallow edge halos), run,
        // scaled back. Returns null when the model could not run.
        static Img RunLama(Img crop, bool[] hole)
        {
            int n = Lama.Size;
            Img small = Ops.Resize(crop, n, n);
            float[] m = new float[n * n];
            float sx = (float)crop.W / n, sy = (float)crop.H / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cx = Math.Min(crop.W - 1, (int)((x + 0.5f) * sx)), cy = Math.Min(crop.H - 1, (int)((y + 0.5f) * sy));
                    if (hole[cy * crop.W + cx]) m[y * n + x] = 1;
                }
            bool[] mb = new bool[n * n];
            for (int i = 0; i < mb.Length; i++) mb[i] = m[i] > 0;
            mb = Dilate(mb, n, n, 3);
            for (int i = 0; i < mb.Length; i++) m[i] = mb[i] ? 1f : 0f;
            Img o;
            try { o = Lama.Run(small, m); }
            catch (Exception ex) { Lama.LastError = ex.Message; return null; }
            if (o == null) return null;
            o.Clamp();
            return Ops.Resize(o, crop.W, crop.H);
        }

        // ---------------- pyramid completion ----------------
        static Img Complete(Img img, bool[] hole, int seed)
        {
            List<Img> imgs = new List<Img>(); List<bool[]> holes = new List<bool[]>();
            imgs.Add(img.Clone()); holes.Add(hole);
            while (true)
            {
                Img cur = imgs[imgs.Count - 1]; bool[] hl = holes[holes.Count - 1];
                // stop once every hole pixel is within a couple of patches of real pixels: deeper levels
                // would only keep less of the surroundings to copy from (long thin holes need few levels)
                int depth = HoleDepth(hl, cur.W, cur.H);
                if (depth <= 2 * PR || Math.Min(cur.W, cur.H) < 8 * (2 * PR + 1)) break;
                Img down; bool[] dh;
                Downsample(cur, hl, out down, out dh);
                imgs.Add(down); holes.Add(dh);
            }
            int top = imgs.Count - 1;
            {
                // coarsest level: smooth fill, then peel the hole from its edge inwards copying real pixels
                Img c = imgs[top]; bool[] h = holes[top];
                float[] wt = new float[c.N];
                for (int i = 0; i < c.N; i++) wt[i] = h[i] ? 0f : 1f;
                Img pp = Ops.PushPull(c, wt);
                for (int i = 0; i < c.N; i++) if (h[i]) { c.D[i * 3] = pp.D[i * 3]; c.D[i * 3 + 1] = pp.D[i * 3 + 1]; c.D[i * 3 + 2] = pp.D[i * 3 + 2]; }
                OnionFill(c, h);
            }
            int[] nnf = null; int prevW = 0, prevH = 0;
            for (int L = top; L >= 0; L--)
            {
                Img c = imgs[L]; bool[] h = holes[L];
                int w = c.W, hgt = c.H;
                if (L < top)
                {
                    // start from the coarser solution, upsampled
                    Img up = imgs[L + 1];
                    for (int y = 0; y < hgt; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (!h[i]) continue;
                            float r, g, b; up.Sample((x - 0.5f) / 2f, (y - 0.5f) / 2f, out r, out g, out b);
                            c.D[i * 3] = r; c.D[i * 3 + 1] = g; c.D[i * 3 + 2] = b;
                        }
                }
                bool[] src = SourceMask(h, w, hgt);
                List<int> srcList = new List<int>();
                for (int i = 0; i < src.Length; i++) if (src[i]) srcList.Add(i);
                if (srcList.Count == 0) continue; // nothing intact to copy from: keep the smooth fill
                bool[] tgt = Dilate(h, w, hgt, PR);   // patches that touch the hole
                float[] conf = Confidence(h, w, hgt);
                int depth = HoleDepth(h, w, hgt);
                nnf = InitNnf(nnf, prevW, prevH, w, hgt, tgt, src, srcList, seed + L * 7919);
                // very large levels only refine the matches carried up from the level below
                bool huge = (long)w * hgt > HugePixels;
                int iters = L == top ? 6 : (huge ? 2 : (L == 0 ? 3 : 4));
                for (int it = 0; it < iters; it++)
                {
                    float[] dist = PatchMatch(c, conf, tgt, src, srcList, nnf, w, hgt, seed + L * 131 + it * 17, it, Math.Max(16f, 3f * depth));
                    // the last pass at full resolution votes sharply to keep fine texture
                    Vote(c, h, tgt, nnf, dist, w, hgt, L == 0 && it == iters - 1 ? 0.25f : 0.5f);
                }
                prevW = w; prevH = hgt;
            }
            return imgs[0];
        }

        // How much a pixel can be trusted when comparing patches: 1 for real pixels, decreasing
        // with the distance into the hole for guessed ones (Wexler et al.).
        static float[] Confidence(bool[] h, int w, int hgt)
        {
            float[] conf = new float[w * hgt];
            int[] d = new int[w * hgt];
            Queue<int> q = new Queue<int>();
            for (int i = 0; i < d.Length; i++) { if (h[i]) d[i] = int.MaxValue; else { d[i] = 0; q.Enqueue(i); } }
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= hgt) continue;
                    int j = ny * w + nx;
                    if (d[j] > d[i] + 1) { d[j] = d[i] + 1; q.Enqueue(j); }
                }
            }
            // floor: deep inside large holes 1.3^-d would underflow to zero and those patches could no
            // longer be compared at all (it showed as a ring artefact on full-resolution removals)
            for (int i = 0; i < conf.Length; i++) conf[i] = d[i] == 0 ? 1f : Math.Max(0.02f, (float)Math.Pow(1.3, -Math.Min(d[i], 60)));
            return conf;
        }

        // Coarsest level: fill the hole ring by ring from its edge, each pixel copied from the source
        // patch that best matches the already known part of its neighbourhood (exhaustive search,
        // the image is tiny at this level).
        static void OnionFill(Img c, bool[] hole)
        {
            int w = c.W, h = c.H;
            bool[] known = new bool[hole.Length];
            for (int i = 0; i < hole.Length; i++) known[i] = !hole[i];
            bool[] src = SourceMask(hole, w, h);
            List<int> srcList = new List<int>();
            for (int i = 0; i < src.Length; i++) if (src[i]) srcList.Add(i);
            if (srcList.Count == 0) return;
            float[] D = c.D;
            while (true)
            {
                List<int> ring = new List<int>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (known[i]) continue;
                        if ((x > 0 && known[i - 1]) || (x < w - 1 && known[i + 1]) || (y > 0 && known[i - w]) || (y < h - 1 && known[i + w])) ring.Add(i);
                    }
                if (ring.Count == 0) break;
                int[] pick = new int[ring.Count];
                Ops.For(0, ring.Count, k =>
                {
                    int p = ring[k], px = p % w, py = p / w;
                    float best = float.MaxValue; int bq = srcList[0];
                    foreach (int q in srcList)
                    {
                        int qx = q % w, qy = q / w;
                        float s = 0, n = 0;
                        for (int dy = -PR; dy <= PR && s < best * Math.Max(1f, n); dy++)
                        {
                            int ty = py + dy; if (ty < 0 || ty >= h) continue;
                            for (int dx = -PR; dx <= PR; dx++)
                            {
                                int tx = px + dx; if (tx < 0 || tx >= w) continue;
                                int t = ty * w + tx;
                                if (!known[t]) continue;
                                int a = t * 3, b = ((qy + dy) * w + qx + dx) * 3;
                                float d0 = D[a] - D[b], d1 = D[a + 1] - D[b + 1], d2 = D[a + 2] - D[b + 2];
                                s += d0 * d0 + d1 * d1 + d2 * d2; n++;
                            }
                        }
                        if (n == 0) continue;
                        float v = s / n;
                        if (v < best) { best = v; bq = q; }
                    }
                    pick[k] = bq;
                });
                for (int k = 0; k < ring.Count; k++)
                {
                    int p = ring[k], q = pick[k];
                    D[p * 3] = D[q * 3]; D[p * 3 + 1] = D[q * 3 + 1]; D[p * 3 + 2] = D[q * 3 + 2];
                }
                foreach (int p in ring) known[p] = true;
            }
        }

        // Largest distance from a hole pixel to the nearest real pixel.
        static int HoleDepth(bool[] h, int w, int hgt)
        {
            int[] d = new int[w * hgt];
            Queue<int> q = new Queue<int>();
            for (int i = 0; i < d.Length; i++) { if (h[i]) d[i] = int.MaxValue; else q.Enqueue(i); }
            int mx = 0;
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= hgt) continue;
                    int j = ny * w + nx;
                    if (d[j] > d[i] + 1) { d[j] = d[i] + 1; if (d[j] > mx) mx = d[j]; q.Enqueue(j); }
                }
            }
            return mx;
        }


        static void Downsample(Img c, bool[] h, out Img d, out bool[] dh)
        {
            int w = (c.W + 1) / 2, hgt = (c.H + 1) / 2;
            d = new Img(w, hgt); dh = new bool[w * hgt];
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float r = 0, g = 0, b = 0; int n = 0; bool anyHole = false;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = Math.Min(c.W - 1, x * 2 + dx), sy = Math.Min(c.H - 1, y * 2 + dy), si = sy * c.W + sx;
                            if (h[si]) { anyHole = true; continue; }
                            r += c.D[si * 3]; g += c.D[si * 3 + 1]; b += c.D[si * 3 + 2]; n++;
                        }
                    int i = y * w + x;
                    dh[i] = anyHole;
                    if (n > 0) { d.D[i * 3] = r / n; d.D[i * 3 + 1] = g / n; d.D[i * 3 + 2] = b / n; }
                }
        }

        static bool[] Dilate(bool[] m, int w, int h, int r)
        {
            if (r <= 0) return (bool[])m.Clone();
            bool[] t = new bool[m.Length], o = new bool[m.Length];
            for (int y = 0; y < h; y++)
            {
                int last = -100000;
                for (int x = 0; x < w; x++) if (m[y * w + x]) { for (int k = Math.Max(Math.Max(0, x - r), last + 1); k <= Math.Min(w - 1, x + r); k++) t[y * w + k] = true; last = Math.Min(w - 1, x + r); }
            }
            for (int x = 0; x < w; x++)
            {
                int last = -100000;
                for (int y = 0; y < h; y++) if (t[y * w + x]) { for (int k = Math.Max(Math.Max(0, y - r), last + 1); k <= Math.Min(h - 1, y + r); k++) o[k * w + x] = true; last = Math.Min(h - 1, y + r); }
            }
            return o;
        }

        // Centres whose whole patch is inside the image and outside the hole.
        static bool[] SourceMask(bool[] h, int w, int hgt)
        {
            bool[] near = Dilate(h, w, hgt, PR);
            bool[] s = new bool[w * hgt];
            for (int y = PR; y < hgt - PR; y++)
                for (int x = PR; x < w - PR; x++) s[y * w + x] = !near[y * w + x];
            return s;
        }

        static int[] InitNnf(int[] prev, int pw, int ph, int w, int h, bool[] tgt, bool[] src, List<int> srcList, int seed)
        {
            int[] nnf = new int[w * h];
            Random rnd = new Random(seed);
            for (int i = 0; i < nnf.Length; i++)
            {
                nnf[i] = -1;
                if (!tgt[i]) continue;
                int cand = -1;
                if (prev != null)
                {
                    // the match found one level coarser, scaled up
                    int x = i % w, y = i / w;
                    int px = Math.Min(pw - 1, x / 2), py = Math.Min(ph - 1, y / 2);
                    int pm = prev[py * pw + px];
                    if (pm >= 0)
                    {
                        int sx = (pm % pw) * 2 + (x & 1), sy = (pm / pw) * 2 + (y & 1);
                        if (sx < w && sy < h && src[sy * w + sx]) cand = sy * w + sx;
                    }
                }
                nnf[i] = cand >= 0 ? cand : srcList[rnd.Next(srcList.Count)];
            }
            return nnf;
        }

        // Sum of squared differences between the patches centred on p (target) and q (source);
        // stops early once it exceeds 'best'.
        static float Dist(Img c, float[] conf, int w, int h, int p, int q, float best)
        {
            int px = p % w, py = p / w, qx = q % w, qy = q / w;
            float s = 0, n = 0;
            float[] D = c.D;
            for (int dy = -PR; dy <= PR; dy++)
            {
                int ty = py + dy; if (ty < 0 || ty >= h) continue;
                int sy = qy + dy;
                for (int dx = -PR; dx <= PR; dx++)
                {
                    int tx = px + dx; if (tx < 0 || tx >= w) continue;
                    int t = ty * w + tx, a = t * 3, b = (sy * w + qx + dx) * 3;
                    float d0 = D[a] - D[b], d1 = D[a + 1] - D[b + 1], d2 = D[a + 2] - D[b + 2];
                    float cw = conf[t];
                    s += (d0 * d0 + d1 * d1 + d2 * d2) * cw; n += cw;
                }
                if (s > best) return float.MaxValue; // the normalised result can only be larger
            }
            return n <= 0 ? float.MaxValue : s * PN / n;
        }

        static float[] PatchMatch(Img c, float[] conf, bool[] tgt, bool[] src, List<int> srcList, int[] nnf, int w, int h, int seed, int iter, float near)
        {
            // matches are compared on patch difference plus a mild preference for nearby sources
            // (texture next to the hole is usually the right one); "near" is the distance where it starts to count
            float penK = 0.004f * PN / (near * near);
            Func<int, int, float, float> eval = (p, q, best) =>
            {
                float dx = (p % w) - (q % w), dy = (p / w) - (q / w);
                float pen = penK * (dx * dx + dy * dy);
                if (pen >= best) return float.MaxValue;
                float d = Dist(c, conf, w, h, p, q, best - pen);
                return d == float.MaxValue ? d : d + pen;
            };
            float[] dist = new float[w * h];
            for (int i = 0; i < dist.Length; i++) if (tgt[i]) dist[i] = eval(i, nnf[i], float.MaxValue);
            // rows are processed in parallel bands; inside a band the scan propagates good matches
            int bands = Math.Max(1, Math.Min(Environment.ProcessorCount * 2, h / 8));
            int radius0 = Math.Max(w, h);
            int passes = (long)w * h > HugePixels ? 1 : 2;
            for (int pass = 0; pass < passes; pass++)
            {
                bool fwd = ((iter + pass) & 1) == 0;
                Ops.For(0, bands, bi =>
                {
                    Random rnd = new Random(seed * 31 + pass * 977 + bi);
                    int yA = bi * h / bands, yB = (bi + 1) * h / bands;
                    for (int yy = yA; yy < yB; yy++)
                    {
                        int y = fwd ? yy : (yA + yB - 1 - yy);
                        for (int xx = 0; xx < w; xx++)
                        {
                            int x = fwd ? xx : w - 1 - xx;
                            int i = y * w + x;
                            if (!tgt[i]) continue;
                            int best = nnf[i]; float bd = dist[i];
                            // propagation: try the neighbours' matches, shifted by one pixel
                            int dxn = fwd ? -1 : 1;
                            int nx = x + dxn;
                            if (nx >= 0 && nx < w && tgt[y * w + nx])
                            {
                                int m = nnf[y * w + nx]; int cand = m - dxn;
                                if (m >= 0 && cand >= 0 && cand < src.Length && (cand % w) == (m % w) - dxn && src[cand]) { float d = eval(i, cand, bd); if (d < bd) { bd = d; best = cand; } }
                            }
                            int ny = y + (fwd ? -1 : 1);
                            if (ny >= 0 && ny < h && tgt[ny * w + x])
                            {
                                int m = nnf[ny * w + x]; int cand = m - (fwd ? -w : w);
                                if (m >= 0 && cand >= 0 && cand < src.Length && src[cand]) { float d = eval(i, cand, bd); if (d < bd) { bd = d; best = cand; } }
                            }
                            // random search around the current best, shrinking the window
                            int bx = best % w, by = best / w;
                            for (int r = radius0; r >= 1; r /= 2)
                            {
                                int sx = bx + rnd.Next(-r, r + 1), sy = by + rnd.Next(-r, r + 1);
                                if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
                                int cand = sy * w + sx;
                                if (!src[cand]) continue;
                                float d = eval(i, cand, bd);
                                if (d < bd) { bd = d; best = cand; bx = sx; by = sy; }
                            }
                            // an occasional fully random candidate avoids getting stuck
                            {
                                int cand = srcList[rnd.Next(srcList.Count)];
                                float d = eval(i, cand, bd);
                                if (d < bd) { bd = d; best = cand; }
                            }
                            nnf[i] = best; dist[i] = bd;
                        }
                    }
                });
            }
            return dist;
        }

        // Each hole pixel takes the weighted average of the values proposed by all matched patches covering it.
        static void Vote(Img c, bool[] hole, bool[] tgt, int[] nnf, float[] dist, int w, int h, float pct)
        {
            // weights: patches that match well count more (scale from the typical match distance)
            List<float> ds = new List<float>();
            for (int i = 0; i < dist.Length; i += 3) if (tgt[i] && dist[i] < float.MaxValue) ds.Add(dist[i]);
            if (ds.Count == 0) return;
            ds.Sort();
            float sigma2 = Math.Max(1e-5f, ds[(int)(ds.Count * pct)]);
            float[] wgt = new float[w * h];
            for (int i = 0; i < wgt.Length; i++) if (tgt[i]) wgt[i] = (float)Math.Exp(-dist[i] / (2 * sigma2));
            float[] outD = new float[c.D.Length];
            float[] D = c.D;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!hole[i]) continue;
                    float r = 0, g = 0, b = 0, sw = 0;
                    for (int dy = -PR; dy <= PR; dy++)
                    {
                        int py = y - dy; if (py < 0 || py >= h) continue;
                        for (int dx = -PR; dx <= PR; dx++)
                        {
                            int px = x - dx; if (px < 0 || px >= w) continue;
                            int p = py * w + px;
                            if (!tgt[p]) continue;
                            int q = nnf[p];
                            int qs = q + dy * w + dx;
                            float wt = wgt[p] + 1e-6f;
                            r += D[qs * 3] * wt; g += D[qs * 3 + 1] * wt; b += D[qs * 3 + 2] * wt; sw += wt;
                        }
                    }
                    if (sw > 0) { outD[i * 3] = r / sw; outD[i * 3 + 1] = g / sw; outD[i * 3 + 2] = b / sw; }
                    else { outD[i * 3] = D[i * 3]; outD[i * 3 + 1] = D[i * 3 + 1]; outD[i * 3 + 2] = D[i * 3 + 2]; }
                }
            });
            for (int i = 0; i < hole.Length; i++) if (hole[i]) { D[i * 3] = outD[i * 3]; D[i * 3 + 1] = outD[i * 3 + 1]; D[i * 3 + 2] = outD[i * 3 + 2]; }
        }
    }
}

using System;
using System.Collections.Generic;

namespace PixTune
{
    // What is wrong with the skin, one map per kind of thing, so each can have its own slider and none of them
    // has to be a single "smooth everything" that takes the pores with it.
    //
    // Everything here is measured against the face itself: how much redder than this face's own skin, how much
    // darker than the skin immediately around it, how much finer than the shading it sits on. A fixed threshold
    // works on one photograph and on no others, because a blemish on a pale face in shade and a blemish on a
    // dark face in sun have nothing in common in absolute terms - only in how far each stands out from what
    // surrounds it.
    public static class SkinDefects
    {
        public class Maps
        {
            public int W, H;
            public float[] Spots;      // small blobs darker or lighter than the skin around them
            public float[] Redness;    // redder than this face's own skin, over an area
            public float[] Lines;      // thin dark valleys: wrinkles, creases, the folds beside the nose
            public float[] Shine;      // bright and colourless: light bounced off the skin
            public float[] UnderEye;   // darker than the cheek below it, under the eyes
            public float[] Texture;    // the fine detail that is none of the above, and has to survive the rest
            // What the corrections work from. These depend on the photograph and not on any setting, so they
            // are worked out once with the maps: moving a slider is then one pass over the pixels.
            public float[] Blob;       // the blemish-sized ups and downs of the brightness
            public float[] Smoothed;   // brightness with the thin valleys taken out, for the lines
            public float[] Around;     // brightness over a wide area: what shine is above and a shadow below
            public float[] Fine;       // the finest detail, which is what gets put back at the end
            public SkinAtlas.Atlas A;
            public string Note = "";
        }

        class Kept { public float[] Print; public string Faces; public int W, H; public Maps M; }
        static readonly List<Kept> kept = new List<Kept>();
        public static void Forget() { lock (kept) kept.Clear(); }

        public static Maps Build(Img src, List<float[]> faces, string facesKey)
        {
            float[] print = Print(src);
            lock (kept)
                foreach (Kept k in kept)
                {
                    if (k.W != src.W || k.H != src.H || k.Faces != facesKey) continue;
                    bool same = true;
                    for (int i = 0; i < print.Length && same; i++) if (Math.Abs(k.Print[i] - print[i]) > 0.004f) same = false;
                    if (same) return k.M;
                }
            Maps m = Make(src, faces, facesKey);
            lock (kept) { kept.Add(new Kept { Print = print, Faces = facesKey, W = src.W, H = src.H, M = m }); while (kept.Count > 2) kept.RemoveAt(0); }
            return m;
        }

        static Maps Make(Img src, List<float[]> faces, string facesKey)
        {
            SkinAtlas.Atlas a = SkinAtlas.Build(src, faces, facesKey);
            int w = src.W, h = src.H, n = w * h;
            Maps M = new Maps { W = w, H = h, A = a };
            float fs = Math.Max(24f, a.FaceSize);
            float[] L = Ops.Luma(src);
            float[] D = src.D;

            // How much the skin varies locally, which everything below is measured in. A blemish is not "darker
            // than some number", it is darker than the skin beside it by more than that skin usually varies.
            float fine = Math.Max(1f, fs * 0.010f);        // about the width of a blemish
            float coarse = Math.Max(2f, fs * 0.055f);      // the shading across a face, which is not a blemish
            float[] lFine = Ops.Gauss(L, w, h, fine);
            float[] lCoarse = Ops.Gauss(L, w, h, coarse);
            float[] band = new float[n];
            for (int i = 0; i < n; i++) band[i] = lFine[i] - lCoarse[i];
            float[] absBand = new float[n];
            for (int i = 0; i < n; i++) absBand[i] = Math.Abs(band[i]);
            float[] spread = Ops.Gauss(absBand, w, h, Math.Max(3f, fs * 0.09f));

            // ---- spots ----
            // Dark counts for more than light: a mole or a spot is darker, while a light blob of the same size
            // is usually a highlight on the skin rather than something anyone wants taken out.
            M.Spots = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sk = a.Skin[i];
                if (sk <= 0.01f) continue;
                float sp = Math.Max(0.004f, spread[i]);
                float t = band[i] / (sp * 1.6f);
                float v = t < 0 ? -t : t * 0.55f;
                M.Spots[i] = sk * Ops.C01(Ops.SmoothStep(0.55f, 1.8f, v));
            }

            // ---- redness ----
            // How far along the red axis a patch sits from this face's own middle, over an area wide enough to
            // be a flush or a patch of irritation rather than one noisy pixel.
            float[] cr = new float[n];
            for (int i = 0; i < n; i++)
            {
                float cb, crv;
                SkinAtlas.Chroma(D[i * 3], D[i * 3 + 1], D[i * 3 + 2], out cb, out crv);
                cr[i] = crv;
            }
            float[] crBlur = Ops.Gauss(cr, w, h, Math.Max(2f, fs * 0.03f));
            M.Redness = new float[n];
            float crScale = Math.Max(2f, a.CrSpread);
            for (int i = 0; i < n; i++)
            {
                float sk = a.Skin[i];
                if (sk <= 0.01f) continue;
                // A patch a spread and a half above the middle is as red as this face gets, so that is where
                // the map reads full: asking for two spreads meant a full slider never finished the job.
                float over = (crBlur[i] - a.CrMid) / crScale;
                M.Redness[i] = sk * Ops.C01(Ops.SmoothStep(0.3f, 1.5f, over));
            }

            // ---- lines ----
            // A wrinkle is a thin dark valley, so it is looked for at two widths and only the dark side taken.
            // Where a face folds anyway - beside the nose, under the eyes, across the forehead - it counts for
            // more, which is what keeps the edge of an eyebrow from being read as a crease.
            M.Lines = new float[n];
            float[] scales = { Math.Max(1f, fs * 0.006f), Math.Max(1.5f, fs * 0.013f) };
            foreach (float sc in scales)
            {
                float[] s1 = Ops.Gauss(L, w, h, sc * 0.6f);
                float[] s2 = Ops.Gauss(L, w, h, sc * 2.2f);
                for (int i = 0; i < n; i++)
                {
                    float sk = a.Skin[i];
                    if (sk <= 0.01f) continue;
                    float dark = s2[i] - s1[i];                     // positive where the middle is the darker
                    if (dark <= 0) continue;
                    float sp = Math.Max(0.004f, spread[i]);
                    float v = Ops.C01(Ops.SmoothStep(0.45f, 1.6f, dark / (sp * 1.5f)));
                    float where = 0.45f + 0.55f * Ops.C01(a.Nasolabial[i] + a.UnderEye[i] * 0.8f + a.Forehead[i] * 0.6f);
                    float val = sk * v * where;
                    if (val > M.Lines[i]) M.Lines[i] = val;
                }
                Ops.Done(s1, s2);
            }

            // ---- shine ----
            // Light bounced off the skin rather than coming out of it: bright, and with the colour washed out,
            // which is what tells it apart from skin that is simply pale.
            M.Shine = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sk = a.Skin[i];
                if (sk <= 0.01f) continue;
                int j = i * 3;
                float mx = Math.Max(D[j], Math.Max(D[j + 1], D[j + 2])), mn = Math.Min(D[j], Math.Min(D[j + 1], D[j + 2]));
                float sat = mx > 1e-4f ? (mx - mn) / mx : 0;
                M.Shine[i] = sk * Ops.SmoothStep(0.62f, 0.92f, L[i]) * (1 - Ops.SmoothStep(0.12f, 0.34f, sat));
            }

            // ---- under the eyes ----
            // Darker than the face around it, inside the band under each eye.
            float[] cheekRef = Ops.Gauss(L, w, h, Math.Max(3f, fs * 0.12f));
            M.UnderEye = new float[n];
            for (int i = 0; i < n; i++)
            {
                float r = a.UnderEye[i];
                if (r <= 0.01f) continue;
                float deficit = (cheekRef[i] - L[i]) / Math.Max(0.02f, cheekRef[i]);
                M.UnderEye[i] = r * Ops.C01(Ops.SmoothStep(0.015f, 0.16f, deficit));
            }

            // ---- texture ----
            // Everything fine that is none of the faults above: pores, the grain of the skin, stubble. This is
            // the map that says what still has to be there afterwards, and it is the whole reason a result can
            // avoid looking like plastic - the others say what to take away, this one says what to put back.
            float[] lSmall = Ops.Gauss(L, w, h, Math.Max(0.8f, fs * 0.004f));
            float[] hiPass = new float[n];
            for (int i = 0; i < n; i++) hiPass[i] = Math.Abs(L[i] - lSmall[i]);
            float[] texture = Ops.Gauss(hiPass, w, h, Math.Max(2f, fs * 0.02f));
            M.Texture = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sk = a.Skin[i];
                if (sk <= 0.01f) continue;
                float claimed = Math.Max(M.Spots[i], Math.Max(M.Lines[i], M.Shine[i]));
                float sp = Math.Max(0.004f, spread[i]);
                M.Texture[i] = sk * Ops.C01(texture[i] / (sp * 1.4f)) * (1 - claimed);
            }

            M.Blob = band;            // keeps the array: it is the blemish signal itself
            M.Smoothed = Ops.Gauss(L, w, h, Math.Max(1.2f, fs * 0.010f));
            M.Around = cheekRef;
            M.Fine = new float[n];
            for (int i = 0; i < n; i++) M.Fine[i] = L[i] - lSmall[i];
            M.Note = string.Format("face {0:N0} px, skin {1:P1} of the picture", fs, Share(a.Skin) );
            Ops.Done(L, lFine, lCoarse, absBand, spread, cr, crBlur, hiPass, lSmall, texture);
            return M;
        }


        // ---------------- putting it right ----------------
        // Each setting works only through its own map, so turning one up cannot quietly do another's job. The
        // corrections are all of the same kind: move this pixel part of the way towards what the skin around it
        // is doing, by as much as its map says and no more.
        //
        // Colour is changed without touching brightness, and brightness without touching colour. That is what
        // keeps a face from going grey as the redness comes out of it, or chalky as a shine is brought down.
        public static Img Apply(Img src, Maps M, float spots, float redness, int rednessWay, float lines, float shine, float underEye, float keepTexture)
        {
            if (M == null) return src.Clone();
            int w = src.W, h = src.H;
            Img o = src.Clone();
            float[] D = o.D, S = src.D;
            float[] A = M.A.Skin;
            float cbMid = M.A.CbMid, crMid = M.A.CrMid;
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = i * 3;
                    if (A[i] <= 0.01f) continue;
                    float r = S[j], g = S[j + 1], b = S[j + 2];
                    // brightness and colour apart, so each correction touches only what it means to
                    float yy = 16f + 65.481f * r + 128.553f * g + 24.966f * b;
                    float cb = 128f - 37.797f * r - 74.203f * g + 112f * b;
                    float cr = 128f + 112f * r - 93.786f * g - 18.214f * b;
                    float y0 = yy;

                    // a blemish is a blob of brightness at its own size: take that blob away and the skin
                    // around it closes over, while anything finer than the blob stays where it was
                    if (spots > 0) yy -= 219f * M.Blob[i] * M.Spots[i] * spots;
                    // a line is a thin valley: fill it part of the way back up to the skin beside it
                    if (lines > 0)
                    {
                        float lNow = (y0 - 16f) / 219f;   // what it was before any of this
                        float lFlat = M.Smoothed[i];
                        yy += 219f * (lFlat - lNow) * M.Lines[i] * lines;
                    }
                    // shine is brightness above what the face is doing around it; a shadow under the eye is
                    // brightness below it. Both are moved back towards that, never past it.
                    if (shine > 0)
                    {
                        float lNow = (yy - 16f) / 219f;
                        float over = lNow - M.Around[i];
                        if (over > 0) yy -= 219f * over * M.Shine[i] * shine * 0.85f;
                    }
                    if (underEye > 0)
                    {
                        float lNow = (yy - 16f) / 219f;
                        float under = M.Around[i] - lNow;
                        if (under > 0) yy += 219f * under * M.UnderEye[i] * underEye * 0.8f;
                    }
                    // Redness comes out along the colour axes alone, with the brightness left exactly as it
                    // is: correct a flush by darkening it and the face goes grey, which is the usual giveaway.
                    // Where it goes is a choice, because there is no single right answer:
                    //   0  to this face's own skin - the red patch ends up the colour of the cheek beside it
                    //   1  only part of the way, so a flush is calmed rather than erased
                    //   2  to the skin colour but keeping a little warmth, for faces that look dead without it
                    //   3  the red taken out and nothing put back, which is the coldest and most even
                    if (redness > 0)
                    {
                        // The map already carries the skin weight, and the whole correction is faded by that
                        // same weight at the end: counting it twice is what kept a full slider from ever
                        // arriving. Here only the confidence in the patch itself is wanted.
                        float k = (A[i] > 0.05f ? M.Redness[i] / A[i] : 0f) * redness;
                        if (k > 1f) k = 1f;
                        float toCr = crMid, toCb = cbMid, cbShare = 0.35f;
                        if (rednessWay == 1) k *= 0.5f;
                        else if (rednessWay == 2) { toCr = crMid + M.A.CrSpread * 1.0f; cbShare = 0.15f; }
                        else if (rednessWay == 3) { toCr = crMid - M.A.CrSpread * 0.6f; cbShare = 0.7f; }
                        cr -= (cr - toCr) * k;
                        cb -= (cb - toCb) * k * cbShare;
                    }
                    // and the skin's own grain goes back on top of whatever was done to it
                    if (keepTexture > 0) yy += 219f * M.Fine[i] * M.Texture[i] * keepTexture;

                    float yn = (yy - 16f) * 0.00456621f;
                    float nr = yn + 0.00625893f * (cr - 128f);
                    float ng = yn - 0.00153632f * (cb - 128f) - 0.00318811f * (cr - 128f);
                    float nb = yn + 0.00791071f * (cb - 128f);
                    // only as far as the map says, so the edge of a correction is never a visible border
                    float m = A[i];
                    D[j] = Ops.Lerp(r, nr, m);
                    D[j + 1] = Ops.Lerp(g, ng, m);
                    D[j + 2] = Ops.Lerp(b, nb, m);
                }
            });
            return o;
        }

        static float Share(float[] m)
        {
            double s = 0;
            for (int i = 0; i < m.Length; i++) s += m[i];
            return (float)(s / Math.Max(1, m.Length));
        }

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
    }
}

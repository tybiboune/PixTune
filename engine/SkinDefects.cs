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
                float over = (crBlur[i] - a.CrMid) / crScale;
                M.Redness[i] = sk * Ops.C01(Ops.SmoothStep(0.4f, 2.0f, over));
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

            M.Note = string.Format("face {0:N0} px, skin {1:P1} of the picture", fs, Share(a.Skin) );
            Ops.Done(L, lFine, lCoarse, band, absBand, spread, cr, crBlur, cheekRef, hiPass, lSmall, texture);
            return M;
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

using System;
using System.Collections.Generic;
using System.IO;

namespace PixTune
{
    // Real-ESRGAN x4plus (Wang et al., BSD-3-Clause; ONNX export by Qualcomm AI Hub, 128x128 -> 512x512),
    // run on the GPU through DirectML in overlapping tiles.
    public static class Upscaler
    {
        public const int Tile = 128, Scale = 4, Overlap = 16;
        public static string Dir = "";        // folder holding real_esrgan_x4plus.onnx and its .data file
        public static string RuntimePath = "";
        public static string Device = "", LastError = "";
        static volatile bool tried, ok;
        static readonly object loadLk = new object();
        static OrtSession session;

        public static string ModelPath { get { return Path.Combine(Dir, "real_esrgan_x4plus.onnx"); } }
        public static bool Installed
        {
            get { return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(ModelPath) && File.Exists(Path.Combine(Dir, "real_esrgan_x4plus.data")); }
        }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the upscaler is not installed");
                    Ort.Init(RuntimePath);
                    session = OrtSession.Open(ModelPath, true, 1);
                    Device = session.Device; LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // Upscales im to outW x outH (normally 1x to 4x). Each 4x tile is scaled to its place in the output
        // right away, so a large result never exists at 4x in memory. Falls back to plain resizing.
        public static string LastTiming = "";
        public static Img UpscaleTo(Img im, int outW, int outH)
        {
            if (!Available) return Ops.Resize(im, outW, outH);
            System.Diagnostics.Stopwatch swr = new System.Diagnostics.Stopwatch(); int tiles = 0;
            float fx = (float)outW / im.W, fy = (float)outH / im.H;
            float[] acc = new float[outW * outH * 3], wsum = new float[outW * outH];
            int step = Tile - 2 * Overlap;
            int n = Tile * Tile;
            float[] buf = new float[3 * n];
            for (int ty = 0; ; ty += step)
            {
                int y0 = Math.Min(ty, Math.Max(0, im.H - Tile));
                for (int tx = 0; ; tx += step)
                {
                    Pipeline.CheckCancel();
                    int x0 = Math.Min(tx, Math.Max(0, im.W - Tile));
                    // tile pixels, edges repeated when the picture is smaller than a tile
                    for (int y = 0; y < Tile; y++)
                        for (int x = 0; x < Tile; x++)
                        {
                            int sx = Math.Min(im.W - 1, x0 + x), sy = Math.Min(im.H - 1, y0 + y), si = (sy * im.W + sx) * 3, i = y * Tile + x;
                            buf[i] = im.D[si]; buf[n + i] = im.D[si + 1]; buf[2 * n + i] = im.D[si + 2];
                        }
                    swr.Start(); float[] o = session.Run(new Tensor(buf, 1, 3, Tile, Tile)).F; swr.Stop(); tiles++;
                    int T4 = Tile * Scale, n4 = T4 * T4;
                    // place the tile: output pixels whose source falls in this tile, weighted to fade at the overlaps
                    int ox0 = (int)Math.Floor(x0 * fx), oy0 = (int)Math.Floor(y0 * fy);
                    int ox1 = Math.Min(outW, (int)Math.Ceiling(Math.Min(im.W, x0 + Tile) * fx));
                    int oy1 = Math.Min(outH, (int)Math.Ceiling(Math.Min(im.H, y0 + Tile) * fy));
                    for (int oy = oy0; oy < oy1; oy++)
                    {
                        float sy = (oy + 0.5f) / fy - y0;            // position in tile pixels
                        if (sy < 0 || sy >= Tile) continue;
                        float wy = Ramp(sy, y0 == 0, y0 + Tile >= im.H);
                        for (int ox = ox0; ox < ox1; ox++)
                        {
                            float sx = (ox + 0.5f) / fx - x0;
                            if (sx < 0 || sx >= Tile) continue;
                            float wgt = wy * Ramp(sx, x0 == 0, x0 + Tile >= im.W);
                            if (wgt <= 0) continue;
                            // bilinear sample of the 4x tile
                            float px = sx * Scale - 0.5f, py = sy * Scale - 0.5f;
                            int ix = Math.Max(0, Math.Min(T4 - 2, (int)Math.Floor(px))), iy = Math.Max(0, Math.Min(T4 - 2, (int)Math.Floor(py)));
                            float ax = Math.Max(0f, Math.Min(1f, px - ix)), ay = Math.Max(0f, Math.Min(1f, py - iy));
                            int oi = oy * outW + ox;
                            for (int c = 0; c < 3; c++)
                            {
                                int b = c * n4 + iy * T4 + ix;
                                float v = (o[b] * (1 - ax) + o[b + 1] * ax) * (1 - ay) + (o[b + T4] * (1 - ax) + o[b + T4 + 1] * ax) * ay;
                                acc[oi * 3 + c] += v * wgt;
                            }
                            wsum[oi] += wgt;
                        }
                    }
                    if (x0 + Tile >= im.W) break;
                }
                if (y0 + Tile >= im.H) break;
            }
            LastTiming = string.Format("{0} tiles, model {1} ms, in {2}x{3} out {4}x{5}", tiles, swr.ElapsedMilliseconds, im.W, im.H, outW, outH);
            Img r = new Img(outW, outH);
            for (int i = 0; i < wsum.Length; i++)
            {
                float w = Math.Max(1e-6f, wsum[i]);
                for (int c = 0; c < 3; c++) r.D[i * 3 + c] = Math.Max(0f, Math.Min(1f, acc[i * 3 + c] / w));
            }
            return r;
        }

        // weight across a tile: fades over the overlap, except at the picture's own edges
        static float Ramp(float p, bool atStart, bool atEnd)
        {
            float w = 1f;
            if (!atStart) w = Math.Min(w, Math.Max(0.001f, (p - 2f) / (2f * Overlap - 4f)));
            if (!atEnd) w = Math.Min(w, Math.Max(0.001f, (Tile - 2f - p) / (2f * Overlap - 4f)));
            return Math.Min(1f, w);
        }
    }

    // "High detail" for generated areas shown or exported larger than they were generated: an AI upscale for
    // clean edges, then the finest texture borrowed from the photo itself (detail transfer), then grain.
    // A low-strength diffusion pass at twice the size was tried for the texture: it was far slower and
    // invented structures that were not there, so the photo's own texture is used instead.
    public static class HighDetail
    {
        public static string LastTiming = "";
        // small: the region as generated (photo margin plus new area); photo: that region at output size
        // (its new pixels are ignored); isNew: new pixels at output size; source: real photo at output size
        // near the new area, where the fine texture is taken from. Returns the region at output size.
        public static Img Enhance(Img small, Img photo, bool[] isNew, Img source, int seed)
        {
            int outW = photo.W, outH = photo.H;
            float f = (float)outW / small.W;
            Diffusion.Status = "Adding detail";
            try
            {
                // the generation is done (and cached): give its video memory to the upscaler
                Diffusion.Unload();
                Diffusion.Progress = 0.05f;
                System.Diagnostics.Stopwatch sw0 = System.Diagnostics.Stopwatch.StartNew();
                Img result = Upscaler.UpscaleTo(small, outW, outH);
                long tUp = sw0.ElapsedMilliseconds;
                Pipeline.CheckCancel();
                Diffusion.Progress = 0.35f;
                TransferDetail(result, isNew, source, f, seed);
                long tTr = sw0.ElapsedMilliseconds;
                Diffusion.Progress = 0.95f;
                MatchGrain(result, source, isNew, seed);
                LastTiming = string.Format("upscale {0} ms, transfer {1} ms, grain {2} ms", tUp, tTr - tUp, sw0.ElapsedMilliseconds - tTr);
                return result;
            }
            finally { Diffusion.Status = ""; Diffusion.Progress = 0; }
        }

        // Detail transfer ("image analogies"): the new area is matched patch by patch against the photo at a
        // coarse scale, where both are alike, and receives the photo's fine detail from the matching places.
        // f: how much the generated area was enlarged; detail finer than that is what it lacks.
        const int MatchScale = 4, PatchR = 2;
        const float Coherence = 1.15f, RandomGain = 0.8f;
        static void TransferDetail(Img target, bool[] isNew, Img source, float f, int seed)
        {
            int tw = target.W, th = target.H, sw = source.W, sh = source.H;
            if (sw < 4 * MatchScale * (2 * PatchR + 1) || sh < 4 * MatchScale * (2 * PatchR + 1)) return;
            float sigma = Math.Max(1f, 0.6f * f);
            // the photo's fine detail (what the generated area lacks) and both pictures' coarse appearance
            Img sLow = Ops.FastGaussImg(source, sigma);
            Img tLow = Ops.FastGaussImg(target, sigma);
            int qtw = Math.Max(1, tw / MatchScale), qth = Math.Max(1, th / MatchScale);
            int qsw = Math.Max(1, sw / MatchScale), qsh = Math.Max(1, sh / MatchScale);
            float[] ft = Ops.Resize(tLow, qtw, qth).D, fs = Ops.Resize(sLow, qsw, qsh).D;
            int[] nnf = PatchMatch(ft, qtw, qth, fs, qsw, qsh, seed);
            Pipeline.CheckCancel();
            Diffusion.Progress = 0.75f;
            // copy the detail from the match of the nearest coarse cell: PatchMatch keeps neighbouring matches
            // coherent, so each area receives one continuous piece of photo texture (blending several matches
            // per pixel was tried and made the copies interfere into a woven pattern)
            float[] hf = new float[tw * th * 3];
            float rx = (float)qtw / tw, ry = (float)qth / th, qx = (float)sw / qsw, qy = (float)sh / qsh;
            Ops.For(0, th, y =>
            {
                int cj = Math.Max(0, Math.Min(qth - 1, (int)(y * ry)));
                for (int x = 0; x < tw; x++)
                {
                    int ti = y * tw + x;
                    if (!isNew[ti]) continue;
                    int ci = Math.Max(0, Math.Min(qtw - 1, (int)(x * rx)));
                    int m = nnf[cj * qtw + ci];
                    // the same position relative to the matched cell, in source pixels
                    int sx = (int)((m % qsw) * qx + (x - ci / rx)), sy = (int)((m / qsw) * qy + (y - cj / ry));
                    sx = Math.Max(0, Math.Min(sw - 1, sx)); sy = Math.Max(0, Math.Min(sh - 1, sy));
                    int si = (sy * sw + sx) * 3;
                    for (int c = 0; c < 3; c++) hf[ti * 3 + c] = source.D[si + c] - sLow.D[si + c];
                }
            });
            // as strong as the photo's own fine detail
            double es = 0, et = 0; long ns = 0, nt = 0;
            for (int i = 0; i < sw * sh * 3; i += 7) { float d = source.D[i] - sLow.D[i]; es += d * d; ns++; }
            for (int i = 0; i < tw * th; i++)
                if (isNew[i]) for (int c = 0; c < 3; c++) { et += hf[i * 3 + c] * hf[i * 3 + c]; nt++; }
            if (nt == 0 || et <= 0) return;
            float gain = (float)Math.Max(0.7, Math.Min(1.5, Math.Sqrt((es / ns) / (et / nt))));
            for (int i = 0; i < tw * th; i++)
                if (isNew[i])
                    for (int c = 0; c < 3; c++) target.D[i * 3 + c] = Math.Max(0f, Math.Min(1f, target.D[i * 3 + c] + gain * hf[i * 3 + c]));
        }

        // PatchMatch (Barnes et al. 2009) between two pictures: for every target pixel, the source pixel whose
        // (2r+1)^2 neighbourhood looks most alike. Rows are processed in parallel bands. Returns source indices.
        static int[] PatchMatch(float[] ft, int tw, int th, float[] fs, int sw, int sh, int seed)
        {
            int r = PatchR;
            int[] nnf = new int[tw * th];
            float[] cost = new float[tw * th];
            int lo = r, hiX = sw - r - 1, hiY = sh - r - 1;
            Func<int, int, int, float, float> dist = (tx, ty, s, limit) =>
            {
                int sx = s % sw, sy = s / sw;
                float d = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    int ty2 = Math.Max(0, Math.Min(th - 1, ty + dy)), sy2 = sy + dy;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int a = (ty2 * tw + Math.Max(0, Math.Min(tw - 1, tx + dx))) * 3, b = (sy2 * sw + sx + dx) * 3;
                        float e0 = ft[a] - fs[b], e1 = ft[a + 1] - fs[b + 1], e2 = ft[a + 2] - fs[b + 2];
                        d += e0 * e0 + e1 * e1 + e2 * e2;
                    }
                    if (d >= limit) return d;
                }
                return d;
            };
            Ops.For(0, th, y =>
            {
                Random rnd = new Random(seed * 7919 + y);
                for (int x = 0; x < tw; x++)
                {
                    int s = rnd.Next(lo, hiY + 1) * sw + rnd.Next(lo, hiX + 1);
                    nnf[y * tw + x] = s; cost[y * tw + x] = dist(x, y, s, float.MaxValue);
                }
            });
            const int band = 24;
            int bands = (th + band - 1) / band;
            for (int it = 0; it < 5; it++)
            {
                Pipeline.CheckCancel();
                bool fwd = it % 2 == 0;
                int iter = it;
                Ops.For(0, bands, bi =>
                {
                    Random rnd = new Random(seed + 104729 * iter + bi);
                    int y0 = bi * band, y1 = Math.Min(th, y0 + band);
                    for (int yy = 0; yy < y1 - y0; yy++)
                    {
                        int y = fwd ? y0 + yy : y1 - 1 - yy;
                        for (int xx = 0; xx < tw; xx++)
                        {
                            int x = fwd ? xx : tw - 1 - xx, i = y * tw + x;
                            int best = nnf[i]; float bc = cost[i];
                            // propagation: a neighbour's match, shifted by one pixel. It is kept even when marginally worse and a
                            // random candidate must be clearly better: in flat areas many places match equally well, and without
                            // this bias neighbouring cells copy from unrelated places, which shows as a mosaic
                            int step = fwd ? -1 : 1;
                            int nx = x + step, ny = y + step;
                            if (nx >= 0 && nx < tw)
                            {
                                int s = nnf[y * tw + nx] - step, sx = s % sw;
                                if (sx >= lo && sx <= hiX) { float c = dist(x, y, s, bc * Coherence); if (c < bc * Coherence) { bc = c; best = s; } }
                            }
                            if (ny >= y0 && ny < y1)
                            {
                                int s = nnf[ny * tw + x] - step * sw, sy = s / sw;
                                if (sy >= lo && sy <= hiY) { float c = dist(x, y, s, bc * Coherence); if (c < bc * Coherence) { bc = c; best = s; } }
                            }
                            // random search in shrinking windows around the best match so far
                            for (int rad = Math.Max(sw, sh); rad >= 1; rad /= 2)
                            {
                                int bx = best % sw, by = best / sw;
                                int cx = Math.Max(lo, Math.Min(hiX, bx + rnd.Next(-rad, rad + 1)));
                                int cy = Math.Max(lo, Math.Min(hiY, by + rnd.Next(-rad, rad + 1)));
                                int s = cy * sw + cx;
                                float c = dist(x, y, s, bc * RandomGain);
                                if (c < bc * RandomGain) { bc = c; best = s; }
                            }
                            nnf[i] = best; cost[i] = bc;
                        }
                    }
                });
            }
            return nnf;
        }

        // Adds fine luminance grain to the new pixels until their noise level matches the photo's.
        static void MatchGrain(Img result, Img source, bool[] isNew, int seed)
        {
            int w = result.W, h = result.H;
            float sp = NoiseLevel(source, null), sg = NoiseLevel(result, isNew);
            if (sp <= 0 || sg >= sp) return;
            float add = (float)Math.Sqrt(sp * sp - sg * sg);
            Random r = new Random(seed);
            float[] nz = new float[w * h];
            for (int i = 0; i < nz.Length; i++) nz[i] = isNew[i] ? (float)(r.NextDouble() * 2 - 1) : 0f;
            // slightly soft, like real film or sensor grain, then brought back to unit strength
            float[] soft = Ops.Gauss(nz, w, h, 0.6f);
            double s2 = 0; int cnt = 0;
            for (int i = 0; i < soft.Length; i++) if (isNew[i]) { s2 += soft[i] * soft[i]; cnt++; }
            float norm = cnt > 0 ? (float)(1.0 / Math.Sqrt(Math.Max(1e-12, s2 / cnt))) : 0f;
            for (int i = 0; i < soft.Length; i++)
            {
                if (!isNew[i]) continue;
                float v = soft[i] * norm * add;
                for (int c = 0; c < 3; c++) result.D[i * 3 + c] = Math.Max(0f, Math.Min(1f, result.D[i * 3 + c] + v));
            }
        }

        // Robust noise estimate (median absolute high-pass) of the selected pixels (all when sel is null).
        static float NoiseLevel(Img im, bool[] sel)
        {
            float[] L = Ops.Luma(im);
            float[] b = Ops.Gauss(L, im.W, im.H, 1.2f);
            List<float> v = new List<float>();
            int stride = Math.Max(1, L.Length / 300000);
            for (int i = 0; i < L.Length; i += stride)
                if (sel == null || sel[i]) v.Add(Math.Abs(L[i] - b[i]));
            if (v.Count < 100) return 0;
            v.Sort();
            return 1.4826f * v[v.Count / 2];
        }
    }
}
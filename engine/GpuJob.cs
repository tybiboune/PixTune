using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PixTune
{
    // Whole tools on the graphics card. A tool's GPU version opens a job on its input picture: the picture is sent
    // once (as three planes, R, G, B), the tool's blurs and its own per-pixel shader (GpuKernels.cs) run on the card
    // with every intermediate plane staying there, and only the result comes back. Any failure returns null and the
    // tool runs its CPU code instead, which gives the same pixels (tests\GpuToolTest.ps1).
    public static partial class Gpu
    {
        // A plane (P.Length == 1) or an RGB picture (3 planes) on the card.
        public sealed class GB
        {
            internal GBuf[] P; public int W, H;
            public bool IsRgb { get { return P.Length == 3; } }
            // a result derived from a stable picture without the sliders, kept on the card between frames (the
            // planes belong to the kept results, never to a job); its key names what it was derived from
            internal string MemoKey;
        }

        // ---------- results kept on the card for stable pictures (the GPU side of Memo) ----------
        class GMemo { public GB Value; public WeakReference Owner; public long Bytes, Stamp; }
        static readonly Dictionary<string, GMemo> gmemo = new Dictionary<string, GMemo>();
        static long gmemoBytes;
        static long GMemoBudget { get { return ResidentBudget / 2; } }

        // results of pictures gone go back to the pool; beyond the budget the least recently used too, except those
        // used since 'from' (a job still holds them)
        static void TrimGMemo(long from)
        {
            List<string> drop = new List<string>();
            foreach (KeyValuePair<string, GMemo> kv in gmemo) if (!kv.Value.Owner.IsAlive) drop.Add(kv.Key);
            if (gmemoBytes > GMemoBudget)
            {
                List<KeyValuePair<string, GMemo>> all = new List<KeyValuePair<string, GMemo>>(gmemo);
                all.Sort((a, b) => a.Value.Stamp.CompareTo(b.Value.Stamp));
                long over = gmemoBytes - GMemoBudget;
                foreach (KeyValuePair<string, GMemo> kv in all)
                {
                    if (over <= 0) break;
                    if (kv.Value.Stamp >= from || drop.Contains(kv.Key)) continue;
                    drop.Add(kv.Key); over -= kv.Value.Bytes;
                }
            }
            foreach (string k in drop) { GMemo m = gmemo[k]; gmemo.Remove(k); gmemoBytes -= m.Bytes; foreach (GBuf g in m.Value.P) Put(g); }
        }

        static void ForgetGMemo()
        {
            foreach (GMemo m in gmemo.Values) foreach (GBuf g in m.Value.P) Put(g);
            gmemo.Clear(); gmemoBytes = 0;
        }

        public sealed class Job
        {
            internal readonly List<GBuf> bufs = new List<GBuf>();
            public readonly Img Src;
            public readonly int W, H;
            GB src;
            WeakReference srcOwner;          // the stable picture the job's input comes from (null: not stable)
            readonly long started = ++stamp; // kept results used from here on are not let go during the job
            internal Job(Img s) { Src = s; W = s.W; H = s.H; }

            // Keeps make()'s result for this job's stable input, under the name of what it is derived from (from)
            // and how (suffix); a later frame on the same input gets it back without computing it.
            public GB Keep(string suffix, GB from, Func<GB> make)
            {
                if (from == null || from.MemoKey == null || srcOwner == null) return make();
                string key = from.MemoKey + suffix;
                GMemo m;
                if (gmemo.TryGetValue(key, out m) && m.Owner.IsAlive) { m.Stamp = ++stamp; Memo.Hits++; return m.Value; }
                GB v = make();
                foreach (GBuf b in v.P) if (Array.IndexOf(from.P, b) >= 0 || (src != null && Array.IndexOf(src.P, b) >= 0)) return v;   // its input, unchanged
                foreach (GBuf b in v.P) bufs.Remove(b);   // the kept results own the planes from now on
                v.MemoKey = key;
                m = new GMemo { Value = v, Owner = srcOwner, Bytes = v.P.Length * (long)v.P[0].N * 4, Stamp = ++stamp };
                gmemo[key] = m; gmemoBytes += m.Bytes; Memo.Misses++;
                TrimGMemo(started);
                return v;
            }

            GBuf New(int n) { GBuf g = Get(n); bufs.Add(g); return g; }

            // An intermediate plane no longer needed: back to the pool at once, so a tool needs only a few planes at a
            // time (the card runs the dispatches in order, so reusing it for a later output is safe). Never the
            // source picture or an input given to the job by its caller.
            public void Free(GB g)
            {
                if (g == null || g == src) return;
                foreach (GBuf b in g.P) if (bufs.Remove(b)) Put(b);
            }
            GB Planes(int w, int h, int count)
            {
                GB r = new GB(); r.W = w; r.H = h; r.P = new GBuf[count];
                for (int k = 0; k < count; k++) r.P[k] = New(w * h);
                return r;
            }

            // a chain of steps: next replaces old, whose planes go back to the pool (the source picture is kept)
            public GB Then(GB old, GB next) { if (next != old) Free(old); return next; }

            // The tool's input picture: its copy on the card when it has one (made by an earlier GPU tool, or sent for
            // an earlier frame), otherwise sent now and kept with the picture, so the next frame of a slider on the
            // same input does not send it again.
            public GB Source
            {
                get
                {
                    if (src != null) return src;
                    if (Src.Dev != null)
                    {
                        src = new GB(); src.W = W; src.H = H; src.P = Src.Dev.P;
                        Src.Dev.Stamp = ++stamp;
                    }
                    else
                    {
                        src = UploadRgb(this, Src);
                        foreach (GBuf b in src.P) bufs.Remove(b);
                        Attach(Src, src.P);
                    }
                    Memo.Info mi = Memo.InfoOf(Src);
                    if (mi != null) { src.MemoKey = mi.Key; srcOwner = mi.Owner; }
                    return src;
                }
            }
            internal GB SourceIfUsed { get { return src; } }

            internal GB NewRgb() { return Planes(W, H, 3); }

            // Runs a shader of GpuKernels.cs over the picture's size; inputs are planes and pictures (a picture
            // brings its 3 planes), in that order; outputs: 1 (a plane) or 3 (a picture). f: PF(0..15), n: PI(0..7).
            public GB K(string kernel, int outPlanes, float[] f, int[] n, params GB[] ins) { return KAt(kernel, W, H, outPlanes, f, n, ins); }

            public GB KAt(string kernel, int w, int h, int outPlanes, float[] f, int[] n, params GB[] ins)
            {
                List<GBuf> flat = new List<GBuf>();
                foreach (GB g in ins) if (g != null) flat.AddRange(g.P);
                GB o = Planes(w, h, outPlanes);
                RunCb(kernel, w, h, 0, 0, 0, 0, null, f, n, flat.ToArray(), o.P);
                return o;
            }

            // a small buffer of values for a shader (curves, grade settings)
            public GB Values(float[] v)
            {
                GB g = new GB(); g.W = v.Length; g.H = 1; g.P = new GBuf[] { New(v.Length) };
                Upload(g.P[0], v);
                return g;
            }

            // the filters the tools build on, kept for a stable input (Keep)
            public GB Luma(GB rgb) { return Keep("/L", rgb, () => KAt("Luma", rgb.W, rgb.H, 1, null, null, rgb)); }
            public GB Gauss(GB p, float sigma) { return Keep("/g" + sigma.ToString("R"), p, () => GaussRaw(p, sigma)); }
            public GB FastGauss(GB p, float sigma) { return Keep("/fg" + sigma.ToString("R"), p, () => FastGaussRaw(p, sigma)); }
            public GB FastGaussRgb(GB rgb, float sigma) { return Keep("/fgi" + sigma.ToString("R"), rgb, () => FastGaussRgbRaw(rgb, sigma)); }
            public GB Guided(GB I, int r, float eps) { return Keep("/G" + r + ":" + eps.ToString("R") + "/" + Ops.GuidedSubsample(r), I, () => GuidedRaw(I, r, eps)); }

            // Ops.Gauss on the card (three box blurs of the same radii)
            GB GaussRaw(GB p, float sigma)
            {
                if (sigma < 0.35f) return p;
                double wIdeal = Math.Sqrt(12 * sigma * sigma / 3.0 + 1);
                int wl = (int)Math.Floor(wIdeal); if (wl % 2 == 0) wl--;
                int wu = wl + 2;
                double mIdeal = (12 * sigma * sigma - 3 * wl * wl - 4 * 3 * wl - 3 * 3) / (-4.0 * wl - 4);
                int m = (int)Math.Round(mIdeal);
                GB cur = p;
                for (int i = 0; i < 3; i++)
                {
                    GB next = Box(cur, Math.Max(1, ((i < m ? wl : wu) - 1) / 2));
                    if (cur != p) Free(cur);
                    cur = next;
                }
                return cur;
            }

            public GB Box(GB p, int r)
            {
                if (r < 1) return p;
                GB t = Planes(p.W, p.H, 1), o = Planes(p.W, p.H, 1);
                Run("BoxH", p.W, p.H, r, 0, 0, 0, p.P, t.P);
                RunBoxV(p.W, p.H, r, t.P, o.P);
                Free(t);
                return o;
            }

            // Ops.ResizePlane on the card: a blur against aliasing when shrinking, then bilinear samples
            public GB Resize(GB p, int nw, int nh)
            {
                float sx = (float)p.W / nw, sy = (float)p.H / nh;
                GB s = sx > 1.5f || sy > 1.5f ? GaussRaw(p, 0.45f * Math.Max(sx, sy)) : p;
                GB o = Planes(nw, nh, 1);
                RunCb("Sample", nw, nh, 0, 0, sx, sy, new int[] { s.W, s.H }, null, null, s.P, o.P);
                if (s != p) Free(s);
                return o;
            }

            // Ops.FastGauss: large blurs on a reduced plane
            GB FastGaussRaw(GB p, float sigma)
            {
                int f = (int)(sigma / 6f);
                if (f < 2) return GaussRaw(p, sigma);
                int sw = Math.Max(1, p.W / f), sh = Math.Max(1, p.H / f);
                GB small = Resize(p, sw, sh), blurred = GaussRaw(small, sigma / f);
                if (blurred != small) Free(small);
                GB o = Resize(blurred, p.W, p.H);
                Free(blurred);
                return o;
            }

            // Ops.FastGaussImg: each channel of a picture
            GB FastGaussRgbRaw(GB rgb, float sigma)
            {
                GB o = new GB(); o.W = rgb.W; o.H = rgb.H; o.P = new GBuf[3];
                for (int c = 0; c < 3; c++) o.P[c] = FastGaussRaw(Plane(rgb, c), sigma).P[0];
                return o;
            }

            public static GB Plane(GB rgb, int c) { GB g = new GB(); g.W = rgb.W; g.H = rgb.H; g.P = new GBuf[] { rgb.P[c] }; return g; }

            // Ops.Guided with I = p (edge-preserving smoothing of a plane by itself)
            GB GuidedRaw(GB I, int r, float eps)
            {
                int s = Ops.GuidedSubsample(r);
                if (s > 1)
                {
                    // the fast guided filter, as Ops does it: coefficients on a copy shrunk s times, brought back
                    int sw = (I.W + s - 1) / s, sh = (I.H + s - 1) / s, rs = Math.Max(1, (int)Math.Round(r / (double)s));
                    GB small = KAt("DownBox", sw, sh, 1, null, new int[] { s, I.W, I.H, 0 }, I);
                    GB smallSq = KAt("DownBox", sw, sh, 1, null, new int[] { s, I.W, I.H, 1 }, I);
                    GB c = GuidedCoefs(small, rs, eps, smallSq);
                    Free(small); Free(smallSq);
                    GB A = Planes(I.W, I.H, 1), B = Planes(I.W, I.H, 1);
                    RunCb("Sample", I.W, I.H, 0, 0, 1f / s, 1f / s, new int[] { sw, sh }, null, null, new GBuf[] { c.P[0] }, A.P);
                    RunCb("Sample", I.W, I.H, 0, 0, 1f / s, 1f / s, new int[] { sw, sh }, null, null, new GBuf[] { c.P[1] }, B.P);
                    Free(c);
                    GB qf = KAt("GuidedQ", I.W, I.H, 1, null, null, A, I, B);
                    Free(A); Free(B);
                    return qf;
                }
                GB co = GuidedCoefs(I, r, eps, null);
                GB q = KAt("GuidedQ", I.W, I.H, 1, null, null, Plane(co, 0), I, Plane(co, 1));
                Free(co);
                return q;
            }

            // the guided filter's smoothed coefficients: plane 0 the slope, plane 1 the offset
            // sq: the squares of I already worked out (the fast filter's block averages of the full-size squares)
            GB GuidedCoefs(GB I, int r, float eps, GB sq)
            {
                int w = I.W, h = I.H;
                GB ii = sq != null ? sq : KAt("Mul", w, h, 1, null, null, I, I);
                GB mI = Box(I, r), mII = Box(ii, r);
                if (sq == null) Free(ii);
                GB ab = Planes(w, h, 2);
                RunCb("GuidedAB", w, h, 0, 0, eps, 0, null, null, null, new GBuf[] { mI.P[0], mI.P[0], mII.P[0], mII.P[0] }, ab.P);
                Free(mI); Free(mII);
                GB ma = Box(Plane(ab, 0), r), mb = Box(Plane(ab, 1), r);
                Free(ab);
                GB c = new GB(); c.W = w; c.H = h; c.P = new GBuf[] { ma.P[0], mb.P[0] };
                return c;
            }

            // Ops.Grain: noise at this picture's place in the photo, blurred to the grain size
            public GB Grain(float size, int seed)
            {
                GB g = Planes(W, H, 1);
                RunCb("Noise", W, H, 0, 0, 0, 0, new int[] { seed, Src.FX, Src.FY }, null, null, new GBuf[0], g.P);
                if (size > 0.6f)
                {
                    g = GaussRaw(g, size * 0.5f);
                    g = K("Scale", 1, new float[] { (float)Math.Sqrt(size) * 1.4f }, null, g);
                }
                return g;
            }

            public float[] Download(GB p) { return Down(p.P[0]); }
        }

        // Simple per-pixel tools (White Balance, Curves, Vignette, Color, Black & White, Grain): with a trip to the card
        // and back for each of them they were slower than the CPU (6-30 ms against ~15 ms of transfers); with pictures
        // kept on the card they cost a dispatch. Off: they run on the CPU.
        public static bool PointJobs = true;

        public static Img RunPointJob(Img src, Func<Job, GB> body) { return PointJobs ? RunJob(src, body) : null; }

        // Runs a tool's GPU version on src; null when the GPU is not used for this size or the job failed (the
        // tool then runs on the CPU). body returns the result picture (3 planes, src's size).
        public static Img RunJob(Img src, Func<Job, GB> body)
        {
            if (src == null || !Use(src.N)) return null;
            lock (lk)
            {
                if (!ok) return null;
                TrimGMemo(long.MaxValue);
                Job j = new Job(src);
                try
                {
                    GB o = body(j);
                    if (o == null) return null;
                    Img r;
                    if (j.SourceIfUsed != null && o.P[0] == j.SourceIfUsed.P[0])
                    {
                        // the tool left the picture as it was: an ordinary copy (two pictures never share planes)
                        r = new Img(src.W, src.H);
                        DownloadPlanes(o.P, src.W, src.H, r.Raw);
                    }
                    else
                    {
                        // the result stays on the card until something reads it on the processor
                        r = new Img(src.W, src.H, true);
                        foreach (GBuf b in o.P) j.bufs.Remove(b);
                        Attach(r, o.P);
                    }
                    r.CopyFrame(src);
                    Sweep();
                    Budget(r);
                    return r;
                }
                catch (Exception ex)
                {
                    // a shader that cannot be used (even when it reaches here wrapped by a caller): that tool runs
                    // on the CPU and the GPU stays in use; anything else stops the GPU for the session
                    if (ex.GetBaseException() is ShaderException) { LastError = ex.GetBaseException().Message; return null; }
                    Fail(ex); return null;
                }
                finally { foreach (GBuf g in j.bufs) Put(g); }
            }
        }

        // ---------- moving pictures to and from the card in chunks of rows ----------
        // The picture is interleaved (RGBRGB...) in memory and kept as planes on the card: rows go through one
        // reused buffer and a shader splits (or joins) the channels, so no large array is made on either side.
        const int ChunkPixels = 1 << 21;
        static GBuf chunk;
        static IntPtr chunkStaging;

        static GBuf Chunk()
        {
            if (chunk != null) return chunk;
            chunk = Get(ChunkPixels * 3);
            BufferDesc d = new BufferDesc();
            d.ByteWidth = (uint)(ChunkPixels * 3 * 4); d.Usage = 3; d.CPUAccessFlags = 0x20000; d.MiscFlags = 0x40; d.StructureByteStride = 4;
            if (createBuffer(dev, ref d, IntPtr.Zero, out chunkStaging) != 0) { Put(chunk); chunk = null; throw new Exception("GPU staging buffer failed"); }
            return chunk;
        }

        static void ForgetChunk()
        {
            if (chunk != null) { Release(chunk.Uav); Release(chunk.Srv); Release(chunk.Buf); chunk = null; }
            if (chunkStaging != IntPtr.Zero) { Release(chunkStaging); chunkStaging = IntPtr.Zero; }
        }

        static GB UploadRgb(Job j, Img im)
        {
            GB o = j.NewRgb();
            int w = im.W, h = im.H, rows = Math.Max(1, ChunkPixels / w);
            GBuf c = Chunk();
            GCHandle hd = GCHandle.Alloc(im.Raw, GCHandleType.Pinned);
            IntPtr box = Marshal.AllocHGlobal(24);
            try
            {
                IntPtr basePtr = hd.AddrOfPinnedObject();
                for (int y0 = 0; y0 < h; y0 += rows)
                {
                    int n = Math.Min(rows, h - y0);
                    int[] b = { 0, 0, 0, n * w * 3 * 4, 1, 1 };   // left, top, front, right (bytes), bottom, back
                    Marshal.Copy(b, 0, box, 6);
                    updateSub(ctx, c.Buf, 0, box, new IntPtr(basePtr.ToInt64() + (long)y0 * w * 12), 0, 0);
                    RunCb("Deint", w, n, 0, 0, 0, 0, new int[] { y0 * w }, null, null, new GBuf[] { c }, o.P);
                }
            }
            finally { Marshal.FreeHGlobal(box); hd.Free(); }
            return o;
        }

        static void DownloadPlanes(GBuf[] planes, int w, int h, float[] dst)
        {
            int rows = Math.Max(1, ChunkPixels / w);
            GBuf c = Chunk();
            for (int y0 = 0; y0 < h; y0 += rows)
            {
                int n = Math.Min(rows, h - y0);
                RunCb("Inter", w, n, 0, 0, 0, 0, new int[] { y0 * w }, null, null, planes, new GBuf[] { c });
                copyResource(ctx, chunkStaging, c.Buf);
                Mapped m;
                if (map(ctx, chunkStaging, 0, 1, 0, out m) != 0) throw new Exception("GPU readback failed");
                try { Marshal.Copy(m.pData, dst, y0 * w * 3, n * w * 3); }
                finally { unmap(ctx, chunkStaging, 0); }
            }
        }

        // ---------- pictures kept on the card ----------
        // Each picture with planes on the card is listed (weakly: a picture nobody uses any more gives its planes
        // back to the pool at the next job). Within a budget of a quarter of the card's memory (at most 2 GB), the
        // pictures used least recently go back to the processor.
        internal sealed class Resident { public GBuf[] P; public long Bytes, Stamp; public WeakReference Owner; }
        static readonly List<Resident> residents = new List<Resident>();
        static long residentBytes, stamp;
        // an integrated GPU borrows the PC's RAM: a sixteenth of it (1 GB on a 16 GB PC)
        static long ResidentBudget
        {
            get
            {
                return Integrated ? Math.Max(256L << 20, Math.Min(1024L << 20, SystemMemory / 16))
                                  : Math.Max(384L << 20, Math.Min(2048L << 20, VideoMemory / 4));
            }
        }

        static void Attach(Img im, GBuf[] planes)
        {
            Resident r = new Resident();
            r.P = planes; r.Bytes = planes.Length * (long)planes[0].N * 4; r.Stamp = ++stamp; r.Owner = new WeakReference(im);
            im.Dev = r;
            residents.Add(r); residentBytes += r.Bytes;
        }

        static void ReleaseResident(Resident r)
        {
            if (!residents.Remove(r)) return;
            residentBytes -= r.Bytes;
            foreach (GBuf g in r.P) Put(g);
        }

        // Img.D: the picture is brought back from the card if it is only there, and the card's copy is dropped.
        public static void Settle(Img im)
        {
            lock (lk)
            {
                Resident r = im.Dev;
                if (r == null) { if (im.Raw == null) im.Raw = new float[im.N * 3]; return; }
                if (im.Raw == null)
                {
                    float[] d = new float[im.N * 3];
                    DownloadPlanes(r.P, im.W, im.H, d);
                    im.Raw = d;
                }
                im.Dev = null;
                ReleaseResident(r);
            }
        }

        // the CPU pixels were replaced: the card's copy is out of date
        public static void Drop(Img im)
        {
            lock (lk) { Resident r = im.Dev; im.Dev = null; if (r != null) ReleaseResident(r); }
        }

        static void Sweep()
        {
            for (int i = residents.Count - 1; i >= 0; i--)
            {
                if (residents[i].Owner.IsAlive) continue;
                Resident r = residents[i];
                residents.RemoveAt(i); residentBytes -= r.Bytes;
                foreach (GBuf g in r.P) Put(g);
            }
        }

        static void Budget(Img keep)
        {
            if (residentBytes <= ResidentBudget) return;
            List<Resident> byAge = new List<Resident>(residents);
            byAge.Sort((a, b) => a.Stamp.CompareTo(b.Stamp));
            foreach (Resident r in byAge)
            {
                if (residentBytes <= ResidentBudget) break;
                Img o = r.Owner.Target as Img;
                if (o == null) { ReleaseResident(r); continue; }
                if (o != keep) Settle(o);
            }
        }

        // Every picture back on the processor and the card's copies freed (a new photo, or the GPU stopping).
        static void SettleAll()
        {
            foreach (Resident r in new List<Resident>(residents))
            {
                Img o = r.Owner.Target as Img;
                try { if (o != null) Settle(o); else ReleaseResident(r); }
                catch (Exception) { ReleaseResident(r); }
            }
        }

        // ---------- the last steps of every tool, on the card when the picture is there ----------
        // Pipeline.Apply: values kept in 0..1
        public static bool ClampOnGpu(Img im)
        {
            lock (lk)
            {
                if (!ok || im.Dev == null) return false;
                try
                {
                    GBuf[] o = { Get(im.N), Get(im.N), Get(im.N) };
                    RunCb("Clamp3", im.W, im.H, 0, 0, 0, 0, null, null, null, im.Dev.P, o);
                    foreach (GBuf g in im.Dev.P) Put(g);
                    im.Dev.P = o;
                    return true;
                }
                catch (Exception ex) { if (!(ex.GetBaseException() is ShaderException)) Fail(ex); return false; }
            }
        }

        // Pipeline.Apply with a mask: o = src + (o - src) * m
        public static bool BlendOnGpu(Img src, Img o, float[] m)
        {
            if (o.Dev == null || src.W != o.W || src.H != o.H) return false;
            lock (lk)
            {
                if (!ok || o.Dev == null) return false;
                Job j = new Job(src);
                try
                {
                    GB a = j.Source;
                    GB mask = new GB(); mask.W = o.W; mask.H = o.H; mask.P = new GBuf[] { Get(o.N) };
                    j.bufs.Add(mask.P[0]);
                    Upload(mask.P[0], m);
                    GBuf[] r = { Get(o.N), Get(o.N), Get(o.N) };
                    List<GBuf> ins = new List<GBuf>(a.P); ins.AddRange(o.Dev.P); ins.Add(mask.P[0]);
                    RunCb("MaskMix", o.W, o.H, 0, 0, 0, 0, null, null, null, ins.ToArray(), r);
                    foreach (GBuf g in o.Dev.P) Put(g);
                    o.Dev.P = r;
                    return true;
                }
                catch (Exception ex) { if (!(ex.GetBaseException() is ShaderException)) Fail(ex); return false; }
                finally { foreach (GBuf g in j.bufs) Put(g); }
            }
        }

        // Img.ToBitmap for a picture on the card: converted to 8-bit there, one value per pixel comes back
        public static System.Drawing.Bitmap ToBitmap(Img im)
        {
            float[] packed;
            lock (lk)
            {
                if (!ok || im.Dev == null) return null;
                try
                {
                    GBuf p = Get(im.N);
                    RunCb("Pack", im.W, im.H, 0, 0, 0, 0, null, null, null, im.Dev.P, new GBuf[] { p });
                    packed = Down(p, Scratch.Floats(im.N));
                    Put(p);
                }
                catch (Exception ex) { if (!(ex.GetBaseException() is ShaderException)) Fail(ex); return null; }
            }
            int n = im.N;
            int[] px = Scratch.Ints(n);
            Ops.For(0, im.H, y =>
            {
                for (int i = y * im.W, e = i + im.W; i < e; i++) px[i] = (int)((uint)packed[i] | 0xFF000000u);
            });
            System.Drawing.Bitmap b = new System.Drawing.Bitmap(im.W, im.H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData bd = b.LockBits(new System.Drawing.Rectangle(0, 0, im.W, im.H), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { Marshal.Copy(px, 0, bd.Scan0, n); }
            finally { b.UnlockBits(bd); }
            return b;
        }
    }
}

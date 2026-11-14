using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PixTune
{
    // Floating point RGB image, interleaved, values nominally in [0,1].
    // A picture made by a tool on the graphics card stays there (Dev) until CPU code reads D: it is then brought
    // back once, and the card's copy is dropped (the reader may change the pixels). So a chain of GPU tools, the
    // preview cache and the frame shown never travel through the processor (GpuJob.cs). A picture handed to a tool
    // must not be changed afterwards through an array taken from it earlier (the card may hold a copy of it).
    public sealed class Img
    {
        public int W, H;
        float[] d;
        internal Gpu.Resident Dev;   // its copy on the graphics card, if any

        public float[] D
        {
            get { if (Dev != null || d == null) Gpu.Settle(this); return d; }
            set { d = value; if (Dev != null) Gpu.Drop(this); }
        }
        // the CPU array as it is, without bringing the picture back from the card (GPU code only)
        internal float[] Raw { get { return d; } set { d = value; } }
        public bool OnGpu { get { return Dev != null; } }

        // A picture that will not change any more (the photo, the preview, the stack's cached results): what the
        // tools derive from it without their sliders (luminance, edge-aware smoothing, wide blurs) is computed once
        // and kept (Memo), so a slider moving over it only redoes the tool's own per-pixel work.
        public bool Stable;
        internal long StableId;

        // how many pictures a render makes and what they weigh: a full-resolution render used to allocate
        // several gigabytes, which forced a memory collection that froze the window
        public static long Made, MadeBytes;

        public Img(int w, int h) { W = w; H = h; d = Fresh(w, h); }
        // a picture whose pixels are only on the graphics card (Gpu)
        internal Img(int w, int h, bool onGpu) { W = w; H = h; if (!onGpu) d = Fresh(w, h); }

        // for a picture every pixel of which the caller fills: takes its memory from the pool as it comes,
        // without the clearing a fresh one would carry
        internal static Img Unfilled(int w, int h)
        {
            Img r = new Img(w, h, true);
            int n = w * h * 3;
            r.d = Pool.Rent(n);
            System.Threading.Interlocked.Increment(ref Made);
            System.Threading.Interlocked.Add(ref MadeBytes, n * 4L);
            return r;
        }

        static float[] Fresh(int w, int h)
        {
            int n = w * h * 3;
            System.Threading.Interlocked.Increment(ref Made);
            System.Threading.Interlocked.Add(ref MadeBytes, n * 4L);
            return new float[n];
        }
        public int N { get { return W * H; } }

        // Frame: while a slider moves, only the part of the photo on screen is computed. Such a picture is that
        // part (FX, FY: its top-left corner) of the whole photo at FW x FH; sizes and positions the tools derive
        // from the photo (radii, centres, strokes) are then those of the whole photo. FW = 0: a whole picture.
        public int FX, FY, FW, FH;
        public int RefW { get { return FW > 0 ? FW : W; } }
        public int RefH { get { return FH > 0 ? FH : H; } }
        public bool Framed { get { return FW > 0; } }
        // pixel position in this picture of a point given as a fraction of the whole photo
        public float PX(double nx) { return (float)(nx * RefW - FX); }
        public float PY(double ny) { return (float)(ny * RefH - FY); }
        public void CopyFrame(Img from) { FX = from.FX; FY = from.FY; FW = from.FW; FH = from.FH; }

        public float MinDim { get { return Math.Min(RefW, RefH); } }
        // Radii in the engine are expressed per 1000 px of the short side, so a preview
        // and a full resolution export look the same.
        public float Scale { get { return Math.Max(0.05f, Math.Min(RefW, RefH) / 1000f); } }
        public float Diagonal { get { return (float)Math.Sqrt(RefW * (double)RefW + RefH * (double)RefH); } }

        public Img Clone()
        {
            float[] src = D;
            Img r = new Img(W, H, true);
            // every value is copied over, so this one may come from the pool without being cleared first
            r.d = Pool.Rent(src.Length);
            System.Threading.Interlocked.Increment(ref Made);
            System.Threading.Interlocked.Add(ref MadeBytes, src.Length * 4L);
            Buffer.BlockCopy(src, 0, r.d, 0, src.Length * 4);
            r.CopyFrame(this);
            return r;
        }

        // Hands this picture's pixels back for the next one to use. Only the render loop does this, and only
        // for a big picture it alone holds: a full-resolution render used to allocate a gigabyte and a half of
        // fresh buffers, which is what forced the collection that froze the window. Anything smaller than the
        // size kept results are remembered at is left alone, so nothing that may be looked up again is touched.
        public const long RecycleAbove = 6000000;
        public void Recycle()
        {
            float[] a = d;
            if (a == null || Stable || Dev != null || Memo.InfoOf(this) != null) return;
            d = null;
            Pool.Give(a);
        }

        // the part x, y, w, h of this picture, framed in the same whole photo
        public Img Region(int x, int y, int w, int h)
        {
            Img r = Unfilled(w, h);
            for (int yy = 0; yy < h; yy++) Buffer.BlockCopy(D, ((y + yy) * W + x) * 12, r.D, yy * w * 12, w * 12);
            r.FX = FX + x; r.FY = FY + y; r.FW = RefW; r.FH = RefH;
            return r;
        }

        public static Img FromBitmap(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Img im = new Img(w, h);
            Bitmap b = src;
            bool own = false;
            if (src.PixelFormat != PixelFormat.Format32bppArgb)
            {
                b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.White);
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, w, h, GraphicsUnit.Pixel);
                }
                own = true;
            }
            BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] buf = new byte[stride * h];
            Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            b.UnlockBits(bd);
            if (own) b.Dispose();
            float inv = 1f / 255f;
            float[] D = im.D;
            Ops.For(0, h, y =>
            {
                int o = y * stride, p = y * w * 3;
                for (int x = 0; x < w; x++)
                {
                    float a = buf[o + 3] * inv;
                    // composite transparent pixels on white
                    D[p] = buf[o + 2] * inv * a + (1 - a);
                    D[p + 1] = buf[o + 1] * inv * a + (1 - a);
                    D[p + 2] = buf[o] * inv * a + (1 - a);
                    p += 3; o += 4;
                }
            });
            return im;
        }

        public Bitmap ToBitmap()
        {
            // a picture on the card is converted there: 4 bytes per pixel come back instead of 12
            if (Dev != null) { Bitmap g = Gpu.ToBitmap(this); if (g != null) return g; }
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData bd = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = bd.Stride;
            byte[] buf = Scratch.Bytes(stride * H);
            float[] Dd = D; int w = W;
            Ops.For(0, H, y =>
            {
                int o = y * stride, p = y * w * 3;
                for (int x = 0; x < w; x++)
                {
                    buf[o] = Ops.B(Dd[p + 2]);
                    buf[o + 1] = Ops.B(Dd[p + 1]);
                    buf[o + 2] = Ops.B(Dd[p]);
                    buf[o + 3] = 255;
                    p += 3; o += 4;
                }
            });
            Marshal.Copy(buf, 0, bd.Scan0, stride * H);
            b.UnlockBits(bd);
            return b;
        }

        public Img FitInside(int maxSide)
        {
            int m = Math.Max(W, H);
            if (m <= maxSide) return Clone();
            float k = (float)maxSide / m;
            int nw = Math.Max(1, (int)Math.Round(W * k)), nh = Math.Max(1, (int)Math.Round(H * k));
            return k <= 0.5f ? AreaDownscale(nw, nh) : Ops.Resize(this, nw, nh);
        }

        // Averages the source pixels under each output pixel: one pass, no blur needed, ideal for
        // large reductions such as previews and thumbnails.
        public Img AreaDownscale(int nw, int nh)
        {
            Img o = new Img(nw, nh);
            double sx = (double)W / nw, sy = (double)H / nh;
            float[] S = D, O = o.D;
            int w = W;
            Ops.For(0, nh, y =>
            {
                int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(H, (int)((y + 1) * sy)));
                for (int x = 0; x < nw; x++)
                {
                    int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(w, (int)((x + 1) * sx)));
                    float r = 0, g = 0, b = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int p = (yy * w + x0) * 3;
                        for (int xx = x0; xx < x1; xx++) { r += S[p]; g += S[p + 1]; b += S[p + 2]; p += 3; }
                    }
                    float inv = 1f / ((y1 - y0) * (x1 - x0));
                    int q = (y * nw + x) * 3;
                    O[q] = r * inv; O[q + 1] = g * inv; O[q + 2] = b * inv;
                }
            });
            return o;
        }

        public void Get(int x, int y, out float r, out float g, out float b)
        {
            if (x < 0) x = 0; else if (x >= W) x = W - 1;
            if (y < 0) y = 0; else if (y >= H) y = H - 1;
            int i = (y * W + x) * 3;
            r = D[i]; g = D[i + 1]; b = D[i + 2];
        }

        public void Sample(float fx, float fy, out float r, out float g, out float b)
        {
            if (fx < 0) fx = 0; else if (fx > W - 1) fx = W - 1;
            if (fy < 0) fy = 0; else if (fy > H - 1) fy = H - 1;
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = x0 + 1 < W ? x0 + 1 : x0, y1 = y0 + 1 < H ? y0 + 1 : y0;
            float ax = fx - x0, ay = fy - y0;
            int i00 = (y0 * W + x0) * 3, i10 = (y0 * W + x1) * 3, i01 = (y1 * W + x0) * 3, i11 = (y1 * W + x1) * 3;
            float w00 = (1 - ax) * (1 - ay), w10 = ax * (1 - ay), w01 = (1 - ax) * ay, w11 = ax * ay;
            r = D[i00] * w00 + D[i10] * w10 + D[i01] * w01 + D[i11] * w11;
            g = D[i00 + 1] * w00 + D[i10 + 1] * w10 + D[i01 + 1] * w01 + D[i11 + 1] * w11;
            b = D[i00 + 2] * w00 + D[i10 + 2] * w10 + D[i01 + 2] * w01 + D[i11 + 2] * w11;
        }

        public void Clamp()
        {
            if (Dev != null && Gpu.ClampOnGpu(this)) return;
            float[] Dd = D;
            Ops.For(0, H, y =>
            {
                int s = y * W * 3, e = s + W * 3;
                for (int i = s; i < e; i++) { float v = Dd[i]; Dd[i] = v < 0 ? 0 : (v > 1 ? 1 : v); }
            });
        }
    }

    // Results the tools derive from a stable picture without their sliders (Img.Stable): the luminance, its
    // edge-aware smoothing and its blurs. While a slider moves the tool's input stays the same picture, so these are
    // made on the first frame and then reused (or before it: Pipeline.Prepare). A result is kept only when everything
    // it comes from is stable, and kept results are only read, never changed. Least recently used results go first
    // beyond the budget; a result goes with the picture it came from.
    public static class Memo
    {
        public sealed class Info { public string Key; public WeakReference Owner; }
        class Entry { public Info Info; public Lazy<object> Value; public long Bytes, Stamp; }
        static readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Info> infoOf = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Info>();
        static readonly object lk = new object();
        static long stamp, total, ids;
        public static bool Enabled = true;
        public static long Hits, Misses;

        // a quarter of a GB, more on PCs with plenty of memory (at most 1 GB)
        static long Budget { get { long ram = Gpu.SystemMemory; return Math.Max(256L << 20, Math.Min(1024L << 20, ram / 32)); } }

        // the key and owner of a stable picture or of a result kept from one; null for anything else
        public static Info InfoOf(Img im)
        {
            // full-resolution pictures (exports, 100% renders) are made once: keeping their results would only push
            // the preview's out of the budget
            if (!Enabled || im == null || !im.Stable || im.Framed || (long)im.W * im.H > 6000000) return null;
            Info i;
            if (infoOf.TryGetValue(im, out i)) return i;
            lock (lk)
            {
                if (infoOf.TryGetValue(im, out i)) return i;
                if (im.StableId == 0) im.StableId = ++ids;
                i = new Info { Key = "i" + im.StableId, Owner = new WeakReference(im) };
                infoOf.Add(im, i);
                return i;
            }
        }
        public static Info InfoOf(float[] a)
        {
            Info i;
            return Enabled && a != null && infoOf.TryGetValue(a, out i) ? i : null;
        }

        // The value for key, made once (a thread asking while it is made waits for it), kept while its owner lives.
        public static T Get<T>(string key, Info owner, Func<T> make) where T : class
        {
            Entry e;
            lock (lk)
            {
                if (!map.TryGetValue(key, out e))
                {
                    e = new Entry { Info = new Info { Key = key, Owner = owner.Owner }, Value = new Lazy<object>(() => make(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication) };
                    map[key] = e;
                    Misses++;
                }
                else Hits++;
                e.Stamp = ++stamp;
            }
            object v;
            try { v = e.Value.Value; }
            catch { lock (lk) { Entry cur; if (map.TryGetValue(key, out cur) && cur == e) map.Remove(key); } throw; }
            lock (lk)
            {
                if (e.Bytes == 0)
                {
                    float[] fa = v as float[]; Img im = v as Img;
                    e.Bytes = fa != null ? fa.Length * 4L : im != null ? im.N * 12L : 64;
                    total += e.Bytes;
                    Info dummy;
                    if (!infoOf.TryGetValue(v, out dummy)) infoOf.Add(v, e.Info);
                    if (im != null) im.Stable = true;
                    Trim();
                }
            }
            return (T)v;
        }

        static void Trim()
        {
            // results of pictures gone first, then the least recently used
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, Entry> kv in map) if (!kv.Value.Info.Owner.IsAlive) dead.Add(kv.Key);
            foreach (string k in dead) { total -= map[k].Bytes; map.Remove(k); }
            if (total <= Budget) return;
            List<KeyValuePair<string, Entry>> all = new List<KeyValuePair<string, Entry>>(map);
            all.Sort((a, b) => a.Value.Stamp.CompareTo(b.Value.Stamp));
            foreach (KeyValuePair<string, Entry> kv in all)
            {
                if (total <= Budget) break;
                if (kv.Value.Bytes == 0) continue;   // still being made
                total -= kv.Value.Bytes; map.Remove(kv.Key);
            }
        }

        public static void Forget() { lock (lk) { map.Clear(); total = 0; } }
    }

    // Temporary planes reused instead of allocated: the filters need several planes of the picture's size for a few
    // milliseconds each, and allocating 7 to 100 MB arrays at every frame makes .NET stop the program to clean up
    // (felt as small freezes on a modest PC). Only planes a filter keeps to itself go through here, never results.
    // Buffers for the screen bitmaps, kept per thread: a slider frame converts a picture of the same size again and
    // again, and a fresh array each time is a large allocation that makes the memory collector pause the window.
    // Only pictures of 6 MP or less (the screen sizes) keep theirs; a full-size export allocates as before.
    public static class Scratch
    {
        const int Keep = 6 * 1024 * 1024;
        [ThreadStatic] static byte[] bytes;
        [ThreadStatic] static int[] ints;
        [ThreadStatic] static float[] floats;
        public static long Reused, Made;

        public static byte[] Bytes(int n)
        {
            if (n > Keep * 4) { Made++; return new byte[n]; }
            if (bytes != null && bytes.Length >= n) { Reused++; return bytes; }
            Made++; bytes = new byte[n]; return bytes;
        }
        public static int[] Ints(int n)
        {
            if (n > Keep) { Made++; return new int[n]; }
            if (ints != null && ints.Length >= n) { Reused++; return ints; }
            Made++; ints = new int[n]; return ints;
        }
        public static float[] Floats(int n)
        {
            if (n > Keep) { Made++; return new float[n]; }
            if (floats != null && floats.Length >= n) { Reused++; return floats; }
            Made++; floats = new float[n]; return floats;
        }
    }

    public static class Pool
    {
        static readonly Dictionary<int, Stack<float[]>> free = new Dictionary<int, Stack<float[]>>();
        static long bytes;
        // Enough to hold several planes of a full-size photo: at 24 megapixels a plane is 96 MB, so the old
        // ceiling of 256 MB held two and the rest were allocated afresh on every render. Allocations that big go
        // on the heap's large-object list, and filling that is what forces the collection that freezes the window.
        static long Max { get { return Math.Max(192L << 20, Math.Min(1536L << 20, Gpu.SystemMemory / 16)); } }
        public static long Reused, Made;

        // Once nothing is being rendered the planes of a full-size photo are no longer worth holding: the
        // screenshots and bitmaps the window makes come from memory outside the collector's reach, and keeping
        // half a gigabyte of planes for a photo nobody is editing starved them.
        public static void Trim(long keep)
        {
            lock (free)
            {
                if (bytes <= keep) return;
                List<int> sizes = new List<int>(free.Keys);
                sizes.Sort(); sizes.Reverse();          // the biggest planes go first
                foreach (int n in sizes)
                {
                    Stack<float[]> st = free[n];
                    while (st.Count > 0 && bytes > keep) { st.Pop(); bytes -= n * 4L; }
                    if (bytes <= keep) break;
                }
            }
        }

        // a plane of n values, its content left over (the caller writes every value)
        public static float[] Rent(int n)
        {
            lock (free)
            {
                Stack<float[]> st;
                if (free.TryGetValue(n, out st) && st.Count > 0) { bytes -= n * 4L; Reused++; return st.Pop(); }
                Made++;
            }
            return new float[n];
        }

        public static void Give(float[] a)
        {
            if (a == null) return;
            lock (free)
            {
                Stack<float[]> st;
                if (!free.TryGetValue(a.Length, out st)) { st = new Stack<float[]>(); free[a.Length] = st; }
                if (st.Count >= 6 || bytes + a.Length * 4L > Max) return;
                st.Push(a); bytes += a.Length * 4L;
            }
        }

        public static void Forget() { lock (free) { free.Clear(); bytes = 0; } }
    }

    public static class Ops
    {
        // ---- how many cores the tools may take ----
        // A render that takes every core leaves none to draw a pan or a zoom with, and on a four-core computer
        // that showed as the window freezing in bursts while a picture was being worked on. Interactive work
        // therefore leaves a core free; an export takes the lot, since nothing is waiting on the window then.
        public static int SpareCores = 1;
        public static bool AllCores;        // set while exporting (and by the tests)
        static readonly ParallelOptions everything = new ParallelOptions();
        static ParallelOptions kept; static int keptFor = -1;

        public static ParallelOptions Par
        {
            get
            {
                int n = Environment.ProcessorCount - SpareCores;
                if (AllCores || SpareCores <= 0 || n >= Environment.ProcessorCount || n < 1) return everything;
                if (keptFor != n) { kept = new ParallelOptions(); kept.MaxDegreeOfParallelism = n; keptFor = n; }
                return kept;
            }
        }

        // every per-pixel loop in the tools goes through this, so the limit is in one place
        public static void For(int from, int to, Action<int> body) { Parallel.For(from, to, Par, body); }

        public static byte B(float v)
        {
            int i = (int)(v * 255f + 0.5f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }
        public static float C01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        // ---- a power curve read from a table ----
        // Raising every pixel to the same power is one of the slowest things the tools do. The curve is
        // sampled once per render into a table, and each pixel then costs a square root, a lookup and a blend.
        // The table is spaced by the square root of the value, so its entries crowd where the curve bends
        // most, in the deep shadows: evenly spaced, brightening shadows (power 0.45) would be 2.4 levels out,
        // this way 0.002. Values above 1 are rare in a float picture and use the exact power.
        const int PowN = 2048;
        static readonly Dictionary<float, float[]> powTables = new Dictionary<float, float[]>();

        // A tool hands a working plane back when it is finished with it. A plane that was kept to be used again
        // (Memo) is left alone: handing that one back would let the next tool write over something still being
        // read. Memo registers everything it keeps, so asking it is exact, at any size.
        public static void Done(params float[][] planes)
        {
            if (planes == null) return;
            for (int i = 0; i < planes.Length; i++)
            {
                float[] a = planes[i];
                if (a != null && Memo.InfoOf(a) == null) Pool.Give(a);
            }
        }

        public static float[] PowTable(float g)
        {
            lock (powTables)
            {
                float[] t;
                if (powTables.TryGetValue(g, out t)) return t;
                t = new float[PowN + 1];
                for (int i = 0; i <= PowN; i++) { double u = i / (double)PowN; t[i] = (float)Math.Pow(u * u, g); }
                if (powTables.Count < 64) powTables[g] = t;
                return t;
            }
        }

        public static float PowAt(float[] t, float v, float g)
        {
            if (v <= 0) return 0;
            if (v >= 1f) return v == 1f ? t[PowN] : (float)Math.Pow(v, g);
            float f = (float)Math.Sqrt(v) * PowN;
            int k = (int)f;
            return t[k] + (t[k + 1] - t[k]) * (f - k);
        }

        // the same over 0..max, for values a tool lifts above 1 (bokeh highlights); keyed by the pair
        static readonly Dictionary<long, float[]> powTablesMax = new Dictionary<long, float[]>();

        public static float[] PowTable(float g, float max)
        {
            long key = ((long)BitConverter.ToInt32(BitConverter.GetBytes(g), 0) << 32) ^ (uint)BitConverter.ToInt32(BitConverter.GetBytes(max), 0);
            lock (powTablesMax)
            {
                float[] t;
                if (powTablesMax.TryGetValue(key, out t)) return t;
                t = new float[PowN + 1];
                for (int i = 0; i <= PowN; i++) { double u = i / (double)PowN; t[i] = (float)Math.Pow(max * u * u, g); }
                if (powTablesMax.Count < 64) powTablesMax[key] = t;
                return t;
            }
        }

        // invMax: 1 / the max the table was built for
        public static float PowAt(float[] t, float v, float g, float invMax)
        {
            if (v <= 0) return 0;
            float u = v * invMax;
            if (u >= 1f) return u == 1f ? t[PowN] : (float)Math.Pow(v, g);
            float f = (float)Math.Sqrt(u) * PowN;
            int k = (int)f;
            return t[k] + (t[k + 1] - t[k]) * (f - k);
        }

        public static void ForgetPowTables() { lock (powTables) powTables.Clear(); lock (powTablesMax) powTablesMax.Clear(); }
        public static float SmoothStep(float e0, float e1, float x)
        {
            float t = C01((x - e0) / (e1 - e0));
            return t * t * (3 - 2 * t);
        }
        public static float Lum(float r, float g, float b) { return 0.299f * r + 0.587f * g + 0.114f * b; }

        // Soft S-curve around mid grey. c in [-1,1], monotonic.
        public static float Contrast(float x, float c)
        {
            if (c == 0) return x;
            float xc = C01(x);
            float d = (xc - 0.5f) * (1 - Math.Abs(2 * xc - 1));
            float y = xc + 0.95f * c * d * 2f;
            if (c > 0)
            {
                // a second pass gives the punch of a real contrast slider
                float d2 = (y - 0.5f) * (1 - Math.Abs(2 * C01(y) - 1));
                y = y + 0.95f * c * d2 * 0.9f;
            }
            return y + (x - xc);
        }

        // Change luminance l0 -> l1 keeping hue as much as possible.
        public static void SetLum(ref float r, ref float g, ref float b, float l0, float l1)
        {
            if (l1 <= l0)
            {
                float k = l0 > 1e-5f ? l1 / l0 : 0;
                r *= k; g *= k; b *= k;
            }
            else
            {
                float k = l0 > 1e-4f ? l1 / l0 : 1e4f;
                float rr = r * k, gg = g * k, bb = b * k;
                float mx = Math.Max(rr, Math.Max(gg, bb));
                if (mx > 1f || l0 < 0.02f)
                {
                    float d = l1 - l0;
                    float ar = r + d, ag = g + d, ab = b + d;
                    float t = l0 < 0.02f ? 1f : C01((mx - 1f) * 2f);
                    rr = Lerp(rr, ar, t); gg = Lerp(gg, ag, t); bb = Lerp(bb, ab, t);
                }
                r = rr; g = gg; b = bb;
            }
        }

        public static void Saturate(ref float r, ref float g, ref float b, float f)
        {
            float l = Lum(r, g, b);
            r = l + (r - l) * f; g = l + (g - l) * f; b = l + (b - l) * f;
        }

        // ---------- the filters the tools build on: kept for stable pictures (Memo) ----------
        public static float[] Luma(Img im)
        {
            Memo.Info a = Memo.InfoOf(im);
            if (a == null) return LumaRaw(im);
            return Memo.Get(a.Key + "/L", a, () => LumaRaw(im));
        }

        public static float[] Gauss(float[] a, int w, int h, float sigma)
        {
            Memo.Info i = Memo.InfoOf(a);
            if (i == null) return GaussRaw(a, w, h, sigma);
            return Memo.Get(i.Key + "/g" + w + "x" + h + ":" + sigma.ToString("R"), i, () => GaussRaw(a, w, h, sigma));
        }

        public static float[] FastGauss(float[] a, int w, int h, float sigma)
        {
            Memo.Info i = Memo.InfoOf(a);
            if (i == null) return FastGaussRaw(a, w, h, sigma);
            return Memo.Get(i.Key + "/fg" + w + "x" + h + ":" + sigma.ToString("R"), i, () => FastGaussRaw(a, w, h, sigma));
        }

        public static Img FastGaussImg(Img im, float sigma)
        {
            Memo.Info i = Memo.InfoOf(im);
            if (i == null) return FastGaussImgRaw(im, sigma);
            return Memo.Get(i.Key + "/fgi:" + sigma.ToString("R"), i, () => FastGaussImgRaw(im, sigma));
        }

        // The smaller grid the guided filter will really use, so a kept result is never mistaken for the other
        // kind. Two different pictures (Dehaze's transmission, the smart masks) take it too, but not while the
        // card is doing this one: there the exact filter is already quick and the picture stays on the card.
        public static int GuidedSubsampleFor(float[] I, float[] p, int r)
        {
            int sub = GuidedSubsample(r);
            return sub > 1 && p != I && Gpu.Use(I.Length) ? 1 : sub;
        }

        public static float[] Guided(float[] I, float[] p, int w, int h, int r, float eps)
        {
            Memo.Info a = Memo.InfoOf(I), b = p == I ? a : Memo.InfoOf(p);
            if (a == null || b == null) return GuidedRaw(I, p, w, h, r, eps);
            string key = a.Key + "/G" + (p == I ? "" : "(" + b.Key + ")") + w + "x" + h + ":" + r + ":" + eps.ToString("R") + "/" + GuidedSubsampleFor(I, p, r);
            return Memo.Get(key, a, () => GuidedRaw(I, p, w, h, r, eps));
        }

        // A plane every value of which is written below may come from the pool rather than be allocated: at
        // 24 megapixels a plane is 86 MB, and a tool that works out five of them allocated nearly half a
        // gigabyte on every render. The tools hand them back with Done when they are finished.
        static float[] LumaRaw(Img im)
        {
            float[] L = Pool.Rent(im.N);
            float[] D = im.D; int w = im.W;
            Ops.For(0, im.H, y =>
            {
                int p = y * w, e = p + w;
                for (int i = p; i < e; i++) L[i] = 0.299f * D[i * 3] + 0.587f * D[i * 3 + 1] + 0.114f * D[i * 3 + 2];
            });
            return L;
        }

        public static float[][] Split(Img im)
        {
            int n = im.N;
            float[] r = Pool.Rent(n), g = Pool.Rent(n), b = Pool.Rent(n);
            float[] D = im.D;
            Ops.For(0, im.H, y =>
            {
                int s = y * im.W, e = s + im.W;
                for (int i = s; i < e; i++) { r[i] = D[i * 3]; g[i] = D[i * 3 + 1]; b[i] = D[i * 3 + 2]; }
            });
            return new float[][] { r, g, b };
        }

        public static Img Merge(float[][] c, int w, int h)
        {
            Img im = Img.Unfilled(w, h);
            float[] D = im.D;
            float[] r = c[0], g = c[1], b = c[2];
            Ops.For(0, h, y =>
            {
                int s = y * w, e = s + w;
                for (int i = s; i < e; i++) { D[i * 3] = r[i]; D[i * 3 + 1] = g[i]; D[i * 3 + 2] = b[i]; }
            });
            return im;
        }

        // ---------- blurs ----------
        static void BoxH(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            Ops.For(0, h, y =>
            {
                int row = y * w;
                float acc = 0;
                for (int k = -r; k <= r; k++) { int xx = k < 0 ? 0 : (k >= w ? w - 1 : k); acc += s[row + xx]; }
                for (int x = 0; x < w; x++)
                {
                    d[row + x] = acc * inv;
                    int xo = x - r; if (xo < 0) xo = 0;
                    int xi = x + r + 1; if (xi >= w) xi = w - 1;
                    acc += s[row + xi] - s[row + xo];
                }
            });
        }

        static void BoxV(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            int blk = 16;
            int nb = (w + blk - 1) / blk;
            Ops.For(0, nb, bi =>
            {
                int x0 = bi * blk, x1 = Math.Min(w, x0 + blk);
                float[] acc = new float[blk];
                for (int x = x0; x < x1; x++)
                {
                    float a = 0;
                    for (int k = -r; k <= r; k++) { int yy = k < 0 ? 0 : (k >= h ? h - 1 : k); a += s[yy * w + x]; }
                    acc[x - x0] = a;
                }
                for (int y = 0; y < h; y++)
                {
                    int yo = y - r; if (yo < 0) yo = 0;
                    int yi = y + r + 1; if (yi >= h) yi = h - 1;
                    int ro = yo * w, ri = yi * w, rr = y * w;
                    for (int x = x0; x < x1; x++)
                    {
                        d[rr + x] = acc[x - x0] * inv;
                        acc[x - x0] += s[ri + x] - s[ro + x];
                    }
                }
            });
        }

        public static float[] Box(float[] a, int w, int h, int r)
        {
            float[] o = Pool.Rent(a.Length);
            if (r < 1) { Array.Copy(a, o, a.Length); return o; }
            if (Gpu.Use(a.Length)) { float[] g = Gpu.BoxChain(a, w, h, new int[] { r }); if (g != null) return g; }
            float[] t = Pool.Rent(a.Length);
            BoxH(a, t, w, h, r);
            BoxV(t, o, w, h, r);
            Pool.Give(t);
            return o;
        }

        static float[] GaussRaw(float[] a, int w, int h, float sigma)
        {
            float[] o = Pool.Rent(a.Length);
            if (sigma < 0.35f) { Array.Copy(a, o, a.Length); return o; }
            double wIdeal = Math.Sqrt(12 * sigma * sigma / 3.0 + 1);
            int wl = (int)Math.Floor(wIdeal); if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - 3 * wl * wl - 4 * 3 * wl - 3 * 3) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            int[] radii = new int[3];
            for (int i = 0; i < 3; i++) radii[i] = Math.Max(1, ((i < m ? wl : wu) - 1) / 2);
            if (Gpu.Use(a.Length)) { float[] g = Gpu.BoxChain(a, w, h, radii); if (g != null) return g; }
            float[] t = Pool.Rent(a.Length);
            float[] cur = a;
            for (int i = 0; i < 3; i++)
            {
                int r = radii[i];
                BoxH(cur, t, w, h, r);
                BoxV(t, o, w, h, r);
                if (i < 2) { if (cur == a) cur = Pool.Rent(a.Length); Array.Copy(o, cur, o.Length); }
            }
            Pool.Give(t); if (cur != a) Pool.Give(cur);
            return o;
        }

        public static Img GaussImg(Img im, float sigma)
        {
            if (sigma < 0.35f) return im.Clone();
            float[][] c = Split(im);
            float[][] o = new float[3][];
            for (int i = 0; i < 3; i++) o[i] = Gauss(c[i], im.W, im.H, sigma);
            return Merge(o, im.W, im.H);
        }

        // Large blurs are computed on a reduced image for speed.
        static float[] FastGaussRaw(float[] a, int w, int h, float sigma)
        {
            int f = (int)(sigma / 6f);
            if (f < 2) return Gauss(a, w, h, sigma);
            int sw = Math.Max(1, w / f), sh = Math.Max(1, h / f);
            float[] small = ResizePlane(a, w, h, sw, sh);
            float[] sb = Gauss(small, sw, sh, sigma / f);
            float[] big = ResizePlane(sb, sw, sh, w, h);
            Done(small, sb);
            return big;
        }

        static Img FastGaussImgRaw(Img im, float sigma)
        {
            float[][] c = Split(im);
            float[][] o = new float[3][];
            for (int i = 0; i < 3; i++) o[i] = FastGauss(c[i], im.W, im.H, sigma);
            Img r = Merge(o, im.W, im.H);
            // the picture carries the result now: the six working planes go back
            Done(c[0], c[1], c[2], o[0], o[1], o[2]);
            return r;
        }

        // Fast guided filter (He and Sun, 2015). A plane smoothed by itself with a wide radius (Tune's local
        // brightness, Details' structure, Drama, Tonal, HDR, Selective) is computed on a copy shrunk s times (block
        // averages) with the radius shrunk s times, and its two coefficients are brought back by bilinear
        // interpolation: s*s fewer pixels, results within a small fraction of a level (they are smooth by nature).
        // s is a power of two (2 from a radius of 8, 4 from 16, 8 from 32); parts of the photo computed alone start
        // on multiples of 8 so their shrunk grid lines up with the whole photo's. Masks snapped to a photo (p != I)
        // stay exact.
        public static bool FastGuided = true;
        public static int GuidedSubsample(int r)
        {
            if (!FastGuided || r < 8) return 1;
            int s = 2;
            while (s < 8 && s * 8 <= r) s *= 2;
            return s;
        }

        // block averages of s x s pixels (the last ones at the right and bottom edges may be smaller)
        public static float[] DownBox(float[] a, int w, int h, int s, int sw, int sh) { return DownBox(a, w, h, s, sw, sh, null); }

        // b: the block averages of a times b instead of a (the fine texture inside a block counts in the variance);
        // pass a itself for the squares
        public static float[] DownBox(float[] a, int w, int h, int s, int sw, int sh, float[] b)
        {
            float[] o = Pool.Rent(sw * sh);
            Ops.For(0, sh, j =>
            {
                int y0 = j * s, y1 = Math.Min(y0 + s, h);
                for (int i = 0; i < sw; i++)
                {
                    int x0 = i * s, x1 = Math.Min(x0 + s, w);
                    float sum = 0; int n = 0;
                    if (b != null) for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { int q = yy * w + xx; sum += a[q] * b[q]; n++; }
                    else for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { sum += a[yy * w + xx]; n++; }
                    o[j * sw + i] = sum / n;
                }
            });
            return o;
        }

        // Edge-preserving smoothing (He et al.), O(n) in radius.
        static float[] GuidedRaw(float[] I, float[] p, int w, int h, int r, float eps)
        {
            int n = I.Length;
            int sub = GuidedSubsampleFor(I, p, r);
            if (sub > 1) return GuidedFast(I, p, w, h, r, eps, sub);
            if (Gpu.Use(n)) { float[] g = Gpu.Guided(I, p, w, h, r, eps); if (g != null) return g; }
            float[] ma, mb;
            GuidedCoefs(I, p, w, h, r, eps, out ma, out mb);
            float[] q = Pool.Rent(n);
            Ops.For(0, h, y =>
            {
                int s = y * w, e = s + w;
                for (int i = s; i < e; i++) q[i] = ma[i] * I[i] + mb[i];
            });
            Pool.Give(ma); Pool.Give(mb);
            return q;
        }

        // the guided filter's smoothed coefficients (rented planes: the caller gives them back)
        static void GuidedCoefs(float[] I, float[] p, int w, int h, int r, float eps, out float[] ma, out float[] mb)
        {
            GuidedCoefs(I, p, w, h, r, eps, null, null, out ma, out mb);
        }

        // sqII, sqIp: the products already worked out (the fast filter's block averages of the full-size ones)
        static void GuidedCoefs(float[] I, float[] p, int w, int h, int r, float eps, float[] sqII, float[] sqIp, out float[] ma, out float[] mb)
        {
            int n = w * h;
            bool same = p == I, given = sqII != null;
            float[] II, Ip;
            if (given) { II = sqII; Ip = same ? sqII : sqIp; }
            else
            {
                II = Pool.Rent(n); Ip = same ? II : Pool.Rent(n);
                for (int i = 0; i < n; i++) { II[i] = I[i] * I[i]; if (!same) Ip[i] = I[i] * p[i]; }
            }
            float[] mI = BoxT(I, w, h, r), mp = same ? mI : BoxT(p, w, h, r);
            float[] mII = BoxT(II, w, h, r), mIp = same ? mII : BoxT(Ip, w, h, r);
            float[] a = Pool.Rent(n), bb = Pool.Rent(n);
            Ops.For(0, h, y =>
            {
                int s = y * w, e = s + w;
                for (int i = s; i < e; i++)
                {
                    float varI = mII[i] - mI[i] * mI[i];
                    float cov = mIp[i] - mI[i] * mp[i];
                    float aa = cov / (varI + eps);
                    a[i] = aa; bb[i] = mp[i] - aa * mI[i];
                }
            });
            ma = BoxT(a, w, h, r); mb = BoxT(bb, w, h, r);
            if (!given) { Pool.Give(II); if (!same) Pool.Give(Ip); }
            if (!same) { Pool.Give(mp); Pool.Give(mIp); }
            Pool.Give(mI); Pool.Give(mII); Pool.Give(a); Pool.Give(bb);
        }

        static float[] GuidedFast(float[] I, float[] p, int w, int h, int r, float eps, int s)
        {
            int sw = (w + s - 1) / s, sh = (h + s - 1) / s, rs = Math.Max(1, (int)Math.Round(r / (double)s));
            bool same = p == I;
            float[] small = DownBox(I, w, h, s, sw, sh);
            float[] smallP = same ? small : DownBox(p, w, h, s, sw, sh);
            float[] smallSq = DownBox(I, w, h, s, sw, sh, I);
            float[] smallIp = same ? smallSq : DownBox(I, w, h, s, sw, sh, p);
            float[] ma, mb;
            GuidedCoefs(small, smallP, sw, sh, rs, eps, smallSq, smallIp, out ma, out mb);
            Pool.Give(small); Pool.Give(smallSq);
            if (!same) { Pool.Give(smallP); Pool.Give(smallIp); }
            float[] q = Pool.Rent(w * h);
            float inv = 1f / s;
            // the bilinear weights of each column, worked out once (the same arithmetic as SamplePlane and the GPU)
            int[] cx0 = new int[w], cx1 = new int[w]; float[] cax = new float[w];
            for (int x = 0; x < w; x++)
            {
                float fx = (x + 0.5f) * inv - 0.5f;
                if (fx < 0) fx = 0; else if (fx > sw - 1) fx = sw - 1;
                int x0 = (int)fx; cx0[x] = x0; cx1[x] = x0 + 1 < sw ? x0 + 1 : x0; cax[x] = fx - x0;
            }
            Ops.For(0, h, y =>
            {
                float fy = (y + 0.5f) * inv - 0.5f;
                if (fy < 0) fy = 0; else if (fy > sh - 1) fy = sh - 1;
                int y0 = (int)fy, y1 = y0 + 1 < sh ? y0 + 1 : y0;
                float ay = fy - y0;
                int r0 = y0 * sw, r1 = y1 * sw, row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int x0 = cx0[x], x1 = cx1[x]; float ax = cax[x];
                    float A = (ma[r0 + x0] * (1 - ax) + ma[r0 + x1] * ax) * (1 - ay) + (ma[r1 + x0] * (1 - ax) + ma[r1 + x1] * ax) * ay;
                    float B = (mb[r0 + x0] * (1 - ax) + mb[r0 + x1] * ax) * (1 - ay) + (mb[r1 + x0] * (1 - ax) + mb[r1 + x1] * ax) * ay;
                    q[row + x] = A * I[row + x] + B;
                }
            });
            Pool.Give(ma); Pool.Give(mb);
            return q;
        }

        // a box blur into a rented plane (for temporaries: the caller gives it back)
        static float[] BoxT(float[] a, int w, int h, int r)
        {
            float[] o = Pool.Rent(a.Length);
            if (r < 1) { Array.Copy(a, o, a.Length); return o; }
            if (Gpu.Use(a.Length)) { float[] g = Gpu.BoxChain(a, w, h, new int[] { r }); if (g != null) { Pool.Give(o); return g; } }
            float[] t = Pool.Rent(a.Length);
            BoxH(a, t, w, h, r);
            BoxV(t, o, w, h, r);
            Pool.Give(t);
            return o;
        }

        // ---------- resampling ----------
        public static Img Resize(Img src, int nw, int nh)
        {
            float sx = (float)src.W / nw, sy = (float)src.H / nh;
            Img s = src;
            if (sx > 1.5f || sy > 1.5f) s = GaussImg(src, 0.45f * Math.Max(sx, sy));
            Img o = Img.Unfilled(nw, nh);
            bool mine = s != src;
            float[] D = o.D;
            Ops.For(0, nh, y =>
            {
                float fy = (y + 0.5f) * sy - 0.5f;
                int p = y * nw * 3;
                for (int x = 0; x < nw; x++)
                {
                    float r, g, b;
                    s.Sample((x + 0.5f) * sx - 0.5f, fy, out r, out g, out b);
                    D[p] = r; D[p + 1] = g; D[p + 2] = b; p += 3;
                }
            });
            if (mine) s.Recycle();            // the softening made on the way down was this call's own
            return o;
        }

        public static float SamplePlane(float[] a, int w, int h, float fx, float fy)
        {
            if (fx < 0) fx = 0; else if (fx > w - 1) fx = w - 1;
            if (fy < 0) fy = 0; else if (fy > h - 1) fy = h - 1;
            int x0 = (int)fx, y0 = (int)fy;
            int x1 = x0 + 1 < w ? x0 + 1 : x0, y1 = y0 + 1 < h ? y0 + 1 : y0;
            float ax = fx - x0, ay = fy - y0;
            return (a[y0 * w + x0] * (1 - ax) + a[y0 * w + x1] * ax) * (1 - ay) + (a[y1 * w + x0] * (1 - ax) + a[y1 * w + x1] * ax) * ay;
        }

        public static float[] ResizePlane(float[] a, int w, int h, int nw, int nh)
        {
            float sx = (float)w / nw, sy = (float)h / nh;
            float[] s = a;
            if (sx > 1.5f || sy > 1.5f) s = Gauss(a, w, h, 0.45f * Math.Max(sx, sy));
            float[] o = Pool.Rent(nw * nh);
            bool mine = s != a;
            Ops.For(0, nh, y =>
            {
                float fy = (y + 0.5f) * sy - 0.5f;
                for (int x = 0; x < nw; x++) o[y * nw + x] = SamplePlane(s, w, h, (x + 0.5f) * sx - 0.5f, fy);
            });
            if (mine) Done(s);          // the softening made on the way down was this call's own
            return o;
        }

        // the part of a whole-photo plane (w x h) that the framed picture part covers, at part's size
        public static float[] FramePart(float[] whole, int w, int h, Img part)
        {
            float sx = (float)w / part.RefW, sy = (float)h / part.RefH;
            float[] o = new float[part.W * part.H];
            int pw = part.W, fx = part.FX, fy = part.FY;
            Ops.For(0, part.H, y =>
            {
                float wy = (y + fy + 0.5f) * sy - 0.5f;
                for (int x = 0; x < pw; x++) o[y * pw + x] = SamplePlane(whole, w, h, (x + fx + 0.5f) * sx - 0.5f, wy);
            });
            return o;
        }

        // ---------- noise ----------
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        public static float VNoise(float x, float y, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        public static float Fbm(float x, float y, int seed, int oct)
        {
            float a = 0.5f, s = 0, f = 1, norm = 0;
            for (int i = 0; i < oct; i++) { s += a * VNoise(x * f, y * f, seed + i * 31); norm += a; f *= 2.03f; a *= 0.5f; }
            return s / norm;
        }

        // Fractal noise over a whole picture, worked out on a coarse grid and blended between its points.
        // The noise is smooth by construction: its finest octave is a fraction of the pattern's size, so a grid
        // a sixth of that fine carries it. Grid points sit at fixed places in the photo, so a part of the photo
        // rendered on its own gets the same noise as the whole one. With a pattern small enough the grid is
        // every pixel and the values are the exact ones.
        public sealed class NoiseField
        {
            readonly float[] g; readonly int gw, sx, sy; readonly float ivx, ivy; readonly int ox, oy;
            public readonly bool Exact;
            public NoiseField(float[] grid, int gridW, int stepX, int stepY, int offX, int offY)
            {
                g = grid; gw = gridW; sx = stepX; sy = stepY; ivx = 1f / stepX; ivy = 1f / stepY;
                ox = offX; oy = offY; Exact = stepX == 1 && stepY == 1;
            }
            public float At(int x, int y)
            {
                if (Exact) return g[(y + oy) * gw + (x + ox)];
                float fx = (x + ox) * ivx, fy = (y + oy) * ivy;
                int x0 = (int)fx, y0 = (int)fy;
                float ax = fx - x0, ay = fy - y0;
                int r0 = y0 * gw + x0, r1 = r0 + gw;
                float a = g[r0], b = g[r0 + 1], c = g[r1], d = g[r1 + 1];
                return (a + (b - a) * ax) * (1 - ay) + (c + (d - c) * ax) * ay;
            }
        }

        // false works the noise out at every pixel (slower, and the yardstick the grid is measured against)
        public static bool FbmGrid = true;

        // period: the size in pixels of the pattern's coarsest feature; fx, fy: where this picture sits in the photo
        public static NoiseField FbmField(int w, int h, int fx, int fy, float period, int seed, int oct)
        {
            return FbmField(w, h, fx, fy, period, period, seed, oct);
        }

        // a pattern stretched one way has its own size along each axis
        public static NoiseField FbmField(int w, int h, int fx, int fy, float periodX, float periodY, int seed, int oct)
        {
            float fine = (float)Math.Pow(2.03, oct - 1);
            int stepX = 1, stepY = 1;
            if (FbmGrid) { stepX = Math.Max(1, (int)(periodX / fine / 6)); stepY = Math.Max(1, (int)(periodY / fine / 6)); }
            int x0 = (fx / stepX) * stepX, y0 = (fy / stepY) * stepY;
            int gw = (fx + w - 1 - x0) / stepX + 2, gh = (fy + h - 1 - y0) / stepY + 2;
            float[] g = new float[gw * gh];
            Ops.For(0, gh, j =>
            {
                float yy = (y0 + j * stepY) / periodY;
                for (int i = 0; i < gw; i++) g[j * gw + i] = Fbm((x0 + i * stepX) / periodX, yy, seed, oct);
            });
            return new NoiseField(g, gw, stepX, stepY, fx - x0, fy - y0);
        }

        // Roughly gaussian noise, zero mean, unit-ish variance.
        public static float GNoise(int x, int y, int seed)
        {
            return (Hash(x, y, seed) + Hash(x, y, seed + 7) + Hash(x, y, seed + 13) - 1.5f) * 2f;
        }

        // Film grain plane: blurred noise whose cell size follows the grain size.
        public static float[] Grain(int w, int h, float size, int seed) { return Grain(w, h, size, seed, 0, 0); }

        // the grain of the picture im: a part of the photo (live view) gets the same grain as that part of the photo
        public static float[] Grain(Img im, float size, int seed) { return Grain(im.W, im.H, size, seed, im.FX, im.FY); }

        static float[] Grain(int w, int h, float size, int seed, int ox, int oy)
        {
            float[] g = Gpu.Use(w * h) ? Gpu.Noise(w, h, seed, ox, oy) : null;
            if (g == null)
            {
                g = new float[w * h];
                Ops.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++) g[y * w + x] = GNoise(x + ox, y + oy, seed);
                });
            }
            if (size > 0.6f)
            {
                g = Gauss(g, w, h, size * 0.5f);
                float k = (float)Math.Sqrt(size) * 1.4f;
                for (int i = 0; i < g.Length; i++) g[i] *= k;
            }
            return g;
        }

        // ---------- hole filling ----------
        // Push-pull: fills pixels whose weight is 0 with a smooth interpolation of valid ones.
        public static Img PushPull(Img im, float[] wgt)
        {
            float[][] c = Split(im);
            int w = im.W, h = im.H;
            float[][] pre = new float[3][];
            for (int k = 0; k < 3; k++) { pre[k] = new float[w * h]; for (int i = 0; i < w * h; i++) pre[k][i] = c[k][i] * wgt[i]; }
            float[][] outc = PushPullRec(pre, (float[])wgt.Clone(), w, h);
            return Merge(outc, w, h);
        }

        static float[][] PushPullRec(float[][] c, float[] wt, int w, int h)
        {
            int n = w * h;
            float[][] res = new float[3][];
            for (int k = 0; k < 3; k++) res[k] = new float[n];
            if (w <= 2 && h <= 2)
            {
                float sw = 0; float[] s = new float[3];
                for (int i = 0; i < n; i++) { sw += wt[i]; for (int k = 0; k < 3; k++) s[k] += c[k][i]; }
                for (int i = 0; i < n; i++) for (int k = 0; k < 3; k++) res[k][i] = sw > 1e-6f ? s[k] / sw : 0.5f;
                return res;
            }
            int cw = (w + 1) / 2, ch = (h + 1) / 2;
            float[][] cc = new float[3][];
            for (int k = 0; k < 3; k++) cc[k] = new float[cw * ch];
            float[] cwt = new float[cw * ch];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int ci = (y / 2) * cw + x / 2, i = y * w + x;
                    cwt[ci] += wt[i];
                    for (int k = 0; k < 3; k++) cc[k][ci] += c[k][i];
                }
            // keep weights as "how much is known", capped at 1 per coarse pixel
            for (int i = 0; i < cw * ch; i++)
            {
                if (cwt[i] > 1f) { float s = 1f / cwt[i]; for (int k = 0; k < 3; k++) cc[k][i] *= s; cwt[i] = 1f; }
            }
            float[][] coarse = PushPullRec(cc, cwt, cw, ch);
            Ops.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = Math.Min(1f, wt[i]);
                    float fx = (x - 0.5f) * 0.5f, fy = (y - 0.5f) * 0.5f;
                    for (int k = 0; k < 3; k++)
                    {
                        float up = SamplePlane(coarse[k], cw, ch, fx, fy);
                        float known = wt[i] > 1e-6f ? c[k][i] / wt[i] : 0;
                        res[k][i] = known * a + up * (1 - a);
                    }
                }
            });
            return res;
        }

        public static float[] PushPullPlane(float[] v, float[] wgt, int w, int h)
        {
            Img im = new Img(w, h);
            for (int i = 0; i < w * h; i++) { im.D[i * 3] = v[i]; im.D[i * 3 + 1] = v[i]; im.D[i * 3 + 2] = v[i]; }
            Img o = PushPull(im, wgt);
            float[] r = new float[w * h];
            for (int i = 0; i < w * h; i++) r[i] = o.D[i * 3];
            return r;
        }

        // Blend helpers used by several tools (all inputs in [0,1]).
        public static float Screen(float a, float b) { return 1 - (1 - a) * (1 - b); }
        public static float Overlay(float a, float b) { return a < 0.5f ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b); }
        public static float SoftLight(float a, float b)
        {
            return b < 0.5f ? a - (1 - 2 * b) * a * (1 - a) : a + (2 * b - 1) * ((a < 0.25f ? ((16 * a - 12) * a + 4) * a : (float)Math.Sqrt(a)) - a);
        }
    }

    // Brush strokes, stored in normalized coordinates so they render at any resolution.
    public class Stroke
    {
        public string Ch = "";
        public float V;          // value painted (channel specific scale)
        public float R = 0.03f;  // radius as a fraction of the short side
        public float Hard = 0.5f;
        public List<PointF> Pts = new List<PointF>();
        public Stroke Clone()
        {
            Stroke s = new Stroke();
            s.Ch = Ch; s.V = V; s.R = R; s.Hard = Hard; s.Pts = new List<PointF>(Pts);
            return s;
        }
    }

    public static class Raster
    {
        public static float[] Coverage(Stroke s, int w, int h)
        {
            float[] cov = new float[w * h];
            int[] bb;
            float[] box = BoxCoverage(s, w, h, 0, 0, 0, 0, out bb);
            if (box == null) return cov;
            int bw = bb[2] - bb[0] + 1;
            for (int y = bb[1]; y <= bb[3]; y++) Array.Copy(box, (y - bb[1]) * bw, cov, y * w + bb[0], bw);
            return cov;
        }

        // The stroke's coverage inside its bounding box only (bb = x0, y0, x1, y1), or null when off the picture.
        // Stamps are R/5 apart: points closer than that (a slow mouse) add nothing and are skipped.
        // fw, fh, fx, fy: the whole photo's size and this picture's place in it when it is only a part (Img frame)
        static float[] BoxCoverage(Stroke s, int w, int h, int fx, int fy, int fw, int fh, out int[] bb)
        {
            bb = null;
            if (fw <= 0) { fw = w; fh = h; fx = 0; fy = 0; }
            float R = Math.Max(1f, s.R * Math.Min(fw, fh));
            float inner = R * Ops.C01(s.Hard), gap = Math.Max(1f, R * 0.2f);
            List<PointF> pts = new List<PointF>();
            PointF last = PointF.Empty;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < s.Pts.Count; i++)
            {
                PointF p = new PointF(s.Pts[i].X * fw - fx, s.Pts[i].Y * fh - fy);
                if (pts.Count > 0)
                {
                    float dx = p.X - last.X, dy = p.Y - last.Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < gap && i < s.Pts.Count - 1) continue;
                    int steps = (int)(len / gap);
                    for (int k = 1; k <= steps; k++) pts.Add(new PointF(last.X + dx * k / (steps + 1), last.Y + dy * k / (steps + 1)));
                }
                pts.Add(p); last = p;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            if (pts.Count == 0) return null;
            int x0 = Math.Max(0, (int)(minX - R - 1)), x1 = Math.Min(w - 1, (int)(maxX + R + 1));
            int y0 = Math.Max(0, (int)(minY - R - 1)), y1 = Math.Min(h - 1, (int)(maxY + R + 1));
            if (x1 < x0 || y1 < y0) return null;
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            float[] cov = new float[bw * bh];
            float r2 = R * R;
            foreach (PointF c in pts)
            {
                int sx0 = Math.Max(x0, (int)(c.X - R - 1)), sx1 = Math.Min(x1, (int)(c.X + R + 1));
                int sy0 = Math.Max(y0, (int)(c.Y - R - 1)), sy1 = Math.Min(y1, (int)(c.Y + R + 1));
                for (int y = sy0; y <= sy1; y++)
                {
                    float ddy = y + 0.5f - c.Y;
                    int row = (y - y0) * bw - x0;
                    for (int x = sx0; x <= sx1; x++)
                    {
                        float ddx = x + 0.5f - c.X;
                        float d2 = ddx * ddx + ddy * ddy;
                        if (d2 >= r2) continue;
                        int i = row + x;
                        if (cov[i] >= 1f) continue;
                        float d = (float)Math.Sqrt(d2);
                        float a = d <= inner ? 1f : 1f - Ops.SmoothStep(inner, R, d);
                        if (a > cov[i]) cov[i] = a;
                    }
                }
            }
            bb = new int[] { x0, y0, x1, y1 };
            return cov;
        }

        // moves m toward the stroke's value where it covers
        static void Compose(float[] m, Stroke s, int w, int h, int[] frame)
        {
            int[] bb;
            float[] cov = frame == null ? BoxCoverage(s, w, h, 0, 0, 0, 0, out bb) : BoxCoverage(s, w, h, frame[0], frame[1], frame[2], frame[3], out bb);
            if (cov == null) return;
            int x0 = bb[0], y0 = bb[1], bw = bb[2] - bb[0] + 1;
            float v = s.V;
            Ops.For(y0, bb[3] + 1, y =>
            {
                int a = y * w + x0, c = (y - y0) * bw;
                for (int k = 0; k < bw; k++) { float t = cov[c + k]; if (t > 0) m[a + k] += (v - m[a + k]) * t; }
            });
        }

        // While a stroke is painted, every frame redraws the same finished strokes: their result is kept and only
        // the stroke in progress (the last one) is added again.
        class Composed { public string Key; public float[] M; }
        static readonly List<Composed> composed = new List<Composed>();
        public static void Forget() { lock (composed) composed.Clear(); }

        static string StrokeKey(Stroke s)
        {
            PointF a = s.Pts[0], b = s.Pts[s.Pts.Count - 1];
            return s.Ch + "/" + s.V + "/" + s.R + "/" + s.Hard + "/" + s.Pts.Count + "/" + a.X + "," + a.Y + "/" + b.X + "," + b.Y;
        }

        static string StartKey(float[] start)
        {
            if (start == null) return "0";
            double sum = 0;
            int step = Math.Max(1, start.Length / 97);
            for (int i = 0; i < start.Length; i += step) sum += start[i] * (1 + (i % 7));
            return start.Length + ":" + sum.ToString("R");
        }

        // A channel mask: each stroke moves the mask toward its value where it covers.
        public static float[] Channel(List<Stroke> strokes, string ch, int w, int h) { return Channel(strokes, ch, w, h, null); }

        // start: an initial mask the strokes are painted over (null = empty)
        public static float[] Channel(List<Stroke> strokes, string ch, int w, int h, float[] start) { return Channel(strokes, ch, w, h, start, null); }

        // the mask of the picture im (a part of the photo when it is framed)
        public static float[] Channel(List<Stroke> strokes, string ch, Img im, float[] start)
        {
            return Channel(strokes, ch, im.W, im.H, start, im.Framed ? new int[] { im.FX, im.FY, im.FW, im.FH } : null);
        }

        static float[] Channel(List<Stroke> strokes, string ch, int w, int h, float[] start, int[] frame)
        {
            if (start != null && start.Length != w * h) start = null;
            List<Stroke> mine = new List<Stroke>();
            if (strokes != null) foreach (Stroke s in strokes) if (s.Ch == ch && s.Pts.Count > 0) mine.Add(s);
            if (mine.Count == 0) return start != null ? (float[])start.Clone() : new float[w * h];
            // key of everything but the last stroke (which may still be growing)
            System.Text.StringBuilder key = new System.Text.StringBuilder();
            key.Append(w).Append('x').Append(h).Append('|').Append(ch).Append('|').Append(StartKey(start));
            if (frame != null) key.Append("|f").Append(frame[0]).Append(',').Append(frame[1]).Append(',').Append(frame[2]).Append(',').Append(frame[3]);
            string[] keys = new string[mine.Count];
            for (int i = 0; i < mine.Count; i++) { keys[i] = key.ToString(); key.Append('|').Append(StrokeKey(mine[i])); }
            bool keep = (long)w * h <= 6000000;
            float[] m = null; int from = 0;
            if (keep)
                lock (composed)
                    for (int i = mine.Count - 1; i >= 1 && m == null; i--)
                        foreach (Composed c in composed) if (c.Key == keys[i]) { m = (float[])c.M.Clone(); from = i; break; }
            if (m == null) m = start != null ? (float[])start.Clone() : new float[w * h];
            for (int i = from; i < mine.Count; i++)
            {
                if (keep && i == mine.Count - 1 && i > from)
                    lock (composed)
                    {
                        composed.RemoveAll(c => c.Key == keys[i]);
                        composed.Add(new Composed { Key = keys[i], M = (float[])m.Clone() });
                        while (composed.Count > 6) composed.RemoveAt(0);
                    }
                Compose(m, mine[i], w, h, frame);
            }
            return m;
        }

        public static bool Has(List<Stroke> strokes, string ch)
        {
            if (strokes == null) return false;
            foreach (Stroke s in strokes) if (s.Ch == ch && s.Pts.Count > 0) return true;
            return false;
        }
    }

    public static class ImageIO
    {
        // A photo as a picture: camera RAW files are developed by LibRaw at 16-bit precision, everything else
        // (and RAW files LibRaw cannot read) through the Windows imaging codecs.
        // Order for RAW files: LibRaw; then, for what LibRaw cannot read, Adobe DNG Converter when installed
        // (real RAW quality); then the Windows codec; and last the full-size JPEG the camera stored in the file.
        const string DngHint = " Install the free Adobe DNG Converter to develop these files at full RAW quality in PixTune.";

        public static Img LoadImg(string path)
        {
            RawDecoder.Notice = "";
            if (RawDecoder.IsRaw(path) && RawDecoder.Available)
            {
                bool variant = false;
                try { return RawDecoder.Decode(path); }
                catch (RawVariantException ex) { RawDecoder.LastError = ex.Message; variant = true; }
                catch (Exception ex) { RawDecoder.LastError = ex.Message; }
                if (DngConverter.Installed)
                {
                    try
                    {
                        Img dev = RawDecoder.Decode(DngConverter.Convert(path));
                        RawDecoder.Notice = "Developed through Adobe DNG Converter (converted once, then kept in PixTune's cache).";
                        return dev;
                    }
                    catch (Exception ex) { RawDecoder.LastError = ex.Message; }
                }
                if (variant)
                {
                    // the Windows codec is built on LibRaw too and would show the same garbage: use the camera's JPEG
                    Img jpg = RawDecoder.TurnLikeRaw(RawDecoder.EmbeddedPreview(path), path);
                    if (jpg == null) throw new Exception("This RAW variant cannot be opened yet (for example Nikon \"High Efficiency\" raw), and the file has no full-size preview." + DngHint);
                    RawDecoder.Notice = "This RAW variant cannot be developed yet: opened the full-size JPEG the camera stored in it (" + jpg.W + " x " + jpg.H + ")." + (DngConverter.Installed ? "" : DngHint);
                    return jpg;
                }
            }
            try { using (Bitmap b = LoadWindows(path)) return Img.FromBitmap(b); }
            catch (Exception)
            {
                if (!RawDecoder.IsRaw(path)) throw;
                // a camera newer than both decoders: its own full-size JPEG, if the file holds one
                Img jpg = RawDecoder.TurnLikeRaw(RawDecoder.ScanForJpeg(path), path);
                if (jpg == null) throw new Exception("This camera's RAW files are not supported yet (" + RawDecoder.LastError + "), and the file holds no full-size preview." + DngHint);
                RawDecoder.Notice = "This camera's RAW files are not supported yet: opened the full-size JPEG the camera stored in it (" + jpg.W + " x " + jpg.H + ")." + (DngConverter.Installed ? "" : DngHint);
                return jpg;
            }
        }

        // Uses the Windows imaging codecs, so any format with an installed codec opens
        // (JPEG, PNG, TIFF, BMP, GIF, and HEIC/WebP/RAW when their extensions are installed).
        // RAW files go through LibRaw first (LoadImg); this is the fallback and the path for small previews.
        public static Bitmap Load(string path)
        {
            if (RawDecoder.IsRaw(path)) return LoadImg(path).ToBitmap();
            return LoadWindows(path);
        }

        static Bitmap LoadWindows(string path)
        {
            int orient = 1;
            Bitmap bmp = null;
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    System.Windows.Media.Imaging.BitmapDecoder dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs,
                        System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    System.Windows.Media.Imaging.BitmapFrame fr = dec.Frames[0];
                    try
                    {
                        System.Windows.Media.Imaging.BitmapMetadata md = fr.Metadata as System.Windows.Media.Imaging.BitmapMetadata;
                        if (md != null)
                        {
                            object o = md.GetQuery("System.Photo.Orientation");
                            if (o != null) orient = Convert.ToInt32(o);
                        }
                    }
                    catch { }
                    System.Windows.Media.Imaging.FormatConvertedBitmap conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        fr, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                    byte[] px = new byte[stride * h];
                    conv.CopyPixels(px, stride, 0);
                    bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(px, y * stride, bd.Scan0 + y * bd.Stride, stride);
                    bmp.UnlockBits(bd);
                }
            }
            catch
            {
                using (Image im = Image.FromFile(path))
                {
                    bmp = new Bitmap(im);
                    try { foreach (PropertyItem pi in im.PropertyItems) if (pi.Id == 0x0112) orient = BitConverter.ToUInt16(pi.Value, 0); } catch { }
                }
            }
            RotateFlipType t = RotateFlipType.RotateNoneFlipNone;
            switch (orient)
            {
                case 2: t = RotateFlipType.RotateNoneFlipX; break;
                case 3: t = RotateFlipType.Rotate180FlipNone; break;
                case 4: t = RotateFlipType.Rotate180FlipX; break;
                case 5: t = RotateFlipType.Rotate90FlipX; break;
                case 6: t = RotateFlipType.Rotate90FlipNone; break;
                case 7: t = RotateFlipType.Rotate270FlipX; break;
                case 8: t = RotateFlipType.Rotate270FlipNone; break;
            }
            if (t != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(t);
            return bmp;
        }

        static ImageCodecInfo Codec(string mime)
        {
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == mime) return c;
            return null;
        }

        // format: jpg | png | tif | bmp ; exifFrom: original file to copy metadata from (may be null)
        public static void Save(Img im, string path, string format, int quality, string exifFrom)
        {
            using (Bitmap b = im.ToBitmap())
            {
                if (!string.IsNullOrEmpty(exifFrom) && File.Exists(exifFrom))
                {
                    try
                    {
                        using (Image src = Image.FromFile(exifFrom))
                        {
                            foreach (PropertyItem pi in src.PropertyItems)
                            {
                                if (pi.Id == 0x0112) { pi.Value = BitConverter.GetBytes((ushort)1); pi.Len = 2; }
                                if (pi.Id == 0x5001 || pi.Id == 0x5002 || pi.Id == 0x501B) continue; // thumbnails
                                b.SetPropertyItem(pi);
                            }
                        }
                    }
                    catch { }
                }
                string f = format.ToLowerInvariant();
                if (f == "png") b.Save(path, ImageFormat.Png);
                else if (f == "bmp") b.Save(path, ImageFormat.Bmp);
                else if (f == "tif" || f == "tiff") b.Save(path, ImageFormat.Tiff);
                else
                {
                    EncoderParameters ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(1, Math.Min(100, quality)));
                    using (Bitmap rgb = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
                    {
                        foreach (PropertyItem pi in b.PropertyItems) { try { rgb.SetPropertyItem(pi); } catch { } }
                        using (Graphics g = Graphics.FromImage(rgb)) g.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height));
                        rgb.Save(path, Codec("image/jpeg"), ep);
                    }
                }
            }
        }

        public static List<string> ExifSummary(string path)
        {
            List<string> r = new List<string>();
            try
            {
                using (Image im = Image.FromFile(path))
                {
                    Dictionary<int, string> names = new Dictionary<int, string>();
                    names[0x010F] = "Camera make"; names[0x0110] = "Camera model"; names[0x0132] = "Date";
                    names[0x9003] = "Date taken"; names[0x829A] = "Exposure time"; names[0x829D] = "F-number";
                    names[0x8827] = "ISO"; names[0x920A] = "Focal length"; names[0xA434] = "Lens"; names[0x0131] = "Software";
                    names[0xA405] = "Focal length (35mm)"; names[0x9209] = "Flash";
                    foreach (PropertyItem pi in im.PropertyItems)
                    {
                        string n;
                        if (!names.TryGetValue(pi.Id, out n)) continue;
                        string v = "";
                        if (pi.Type == 2) v = System.Text.Encoding.ASCII.GetString(pi.Value).Trim('\0', ' ');
                        else if (pi.Type == 3) v = BitConverter.ToUInt16(pi.Value, 0).ToString();
                        else if (pi.Type == 4) v = BitConverter.ToUInt32(pi.Value, 0).ToString();
                        else if (pi.Type == 5 || pi.Type == 10)
                        {
                            long num = pi.Type == 5 ? (long)BitConverter.ToUInt32(pi.Value, 0) : BitConverter.ToInt32(pi.Value, 0);
                            long den = pi.Type == 5 ? (long)BitConverter.ToUInt32(pi.Value, 4) : BitConverter.ToInt32(pi.Value, 4);
                            if (den == 0) v = num.ToString();
                            else if (pi.Id == 0x829A && num < den) v = "1/" + Math.Round((double)den / num).ToString();
                            else v = Math.Round((double)num / den, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if (pi.Id == 0x829D) v = "f/" + v;
                            if (pi.Id == 0x920A) v = v + " mm";
                            if (pi.Id == 0x829A) v = v + " s";
                        }
                        if (v.Length > 0) r.Add(n + "\t" + v);
                    }
                }
            }
            catch { }
            return r;
        }

        // 256-bin luminance histogram, used by Curves and the info panel.
        public static int[] Histogram(Img im, int channel)
        {
            int[] hst = new int[256];
            int step = Math.Max(1, im.N / 400000);
            for (int i = 0; i < im.N; i += step)
            {
                float v = channel < 0 ? Ops.Lum(im.D[i * 3], im.D[i * 3 + 1], im.D[i * 3 + 2]) : im.D[i * 3 + channel];
                hst[Ops.B(v)]++;
            }
            return hst;
        }
    }
}

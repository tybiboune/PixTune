using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PixTune
{
    // GPU compute through Direct3D 11 (part of Windows). Shaders are compiled at runtime with the
    // Windows shader compiler. Every entry point falls back to the CPU when the GPU is unavailable.
    // GpuJob.cs: whole tools run on the card (pictures kept there between their steps); GpuKernels.cs: their shaders.
    public static partial class Gpu
    {
        public static bool Enabled = true;
        public static int MinPixels = 120000;     // smaller images are faster on the CPU
        // The largest single plane the card is asked to hold. Direct3D guarantees a card of this level at least
        // 128 MB per resource and allows far more when it has the memory, so 96 MB turned the card away from
        // any photo over 25 megapixels and the tools fell back to the processor: a 26 megapixel Film went from
        // 198 ms to 1.1 s at that step. A tenth of the card's memory, kept between 128 MB and 512 MB, lets a
        // 45 megapixel photo through; a card that still refuses falls back as it always did.
        public static long MaxPlaneBytes = 96L * 1024 * 1024;
        public static int MaxTilePixels { get { return (int)(MaxPlaneBytes / 4); } }

        // Worked out once the card is known: a tenth of its memory, never below the old 96 MB nor above 512 MB.
        // A photo bigger than this is cut into parts that fit (Pipeline.ApplyTiled) rather than handed to the
        // processor whole.
        // Not simply as much as the card can hold: several pictures have to sit there at once for a stack to
        // run without being fetched back and sent again. Handed a 54 megapixel photo whole, this card spent
        // 7.1 s on Film against 2.6 s on the processor, because each picture is 648 MB and they no longer fit
        // together. In parts of about this size the same photo keeps the card's advantage.
        static void SizeLimits()
        {
            // Measured on an 8 GB card, each figure the quicker of two runs of the same edit:
            //   24 MP  card 169 ms, processor  504 ms      36 MP  card 265 ms, processor  905 ms
            //   31 MP  card 234 ms, processor  635 ms      54 MP  card 677 ms and worse, processor 969 ms
            // The card keeps its lead to about 40 megapixels and then loses it, because a picture that size is
            // 648 MB and several no longer sit on the card together. A fortieth of the card's memory lands at
            // 200 MB a plane, which is 50 megapixels: kept to 160 MB (40 megapixels) to stay the right side of
            // that. A photo bigger than this goes to the processor, as it always did.
            MaxPlaneBytes = Math.Max(96L << 20, Math.Min(160L << 20, VideoMemory / 40));
        }
        public static string Adapter = "";
        // every graphics card the machine has, as "name (memory, kind)", in the order Windows lists them
        public static readonly List<string> Adapters = new List<string>();
        // the card the speed test settled on (its name, kept in the settings); empty = the one with most memory
        public static string Preferred = "";
        public static readonly List<string> AdapterNames = new List<string>();
        public static int AdapterIndex = -1;   // DXGI adapter number of the chosen GPU (also used for DirectML)
        public static long VideoMemory;        // its dedicated memory in bytes
        // An integrated GPU (Intel Iris / UHD, AMD APUs): little or no memory of its own, it borrows the PC's RAM.
        public static bool Integrated { get { return VideoMemory < (512L << 20); } }
        public static string LastError = "";

        // The PC's memory, in bytes (0 if unknown).
        public static long SystemMemory { get { if (sysMem < 0) sysMem = ReadSystemMemory(); return sysMem; } }
        static long sysMem = -1;
        [StructLayout(LayoutKind.Sequential)]
        class MemStatus { public uint Length = 64, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MemStatus s);
        static long ReadSystemMemory() { try { MemStatus s = new MemStatus(); return GlobalMemoryStatusEx(s) ? (long)s.TotalPhys : 0; } catch { return 0; } }

        // For testing on a fast machine how PixTune behaves with a slow graphics card: PIXTUNE_GPU=software runs the
        // image shaders on Windows' software GPU (WARP), much slower than any real card.
        static bool SoftwareGpu { get { return string.Equals(Environment.GetEnvironmentVariable("PIXTUNE_GPU"), "software", StringComparison.OrdinalIgnoreCase); } }
        // PIXTUNE_GPU_ADAPTER=<number> uses that adapter instead of the one with the most memory, so each card
        // in a machine can be timed on its own (on a laptop, the built-in one as well as the discrete one).
        static int WantedAdapter
        {
            get
            {
                int n; string v = Environment.GetEnvironmentVariable("PIXTUNE_GPU_ADAPTER");
                return !string.IsNullOrEmpty(v) && int.TryParse(v, out n) ? n : -1;
            }
        }
        static volatile bool tried, ok;
        static readonly object lk = new object();

        public static bool Available { get { EnsureInit(); return ok; } }

        // Starts the device and shader setup in the background so the window opens without waiting.
        public static void WarmUp()
        {
            if (Enabled && !tried) System.Threading.Tasks.Task.Factory.StartNew(EnsureInit);
        }

        public static bool Ready { get { return tried; } }

        // Image tools on the card: off when the speed test found this card slower than the processor for them (GpuSpeed);
        // the AI models are decided apart (GpuCheck).
        public static bool ImageTools = true;
        [ThreadStatic] static int forced;
        // The speed test times both paths on its own thread without changing what the rest of PixTune uses:
        // 1 = this thread's image tools on the CPU, 2 = on the card, 0 = as ImageTools says.
        public static int ForceOnThisThread { get { return forced; } set { forced = value; } }

        public static bool Use(int pixels)
        {
            if (!Enabled || forced == 1 || (forced != 2 && !ImageTools) || pixels < MinPixels) return false;
            if (pixels * 4L > MaxPlaneBytes) return false;      // a plane has to fit in one buffer on the card
            return Available;
        }

        // ---------------- interop ----------------
        [StructLayout(LayoutKind.Sequential)]
        struct BufferDesc { public uint ByteWidth; public int Usage; public uint BindFlags; public uint CPUAccessFlags; public uint MiscFlags; public uint StructureByteStride; }
        [StructLayout(LayoutKind.Sequential)]
        struct Mapped { public IntPtr pData; public uint RowPitch; public uint DepthPitch; }

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, int[] levels, uint numLevels, uint sdk, out IntPtr device, out int level, out IntPtr context);
        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        static extern int D3DCompile(byte[] src, IntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target, uint f1, uint f2, out IntPtr code, out IntPtr errors);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BlobPtrFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdaptersFn(IntPtr self, uint i, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescFn(IntPtr self, IntPtr desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBufferFn(IntPtr self, ref BufferDesc d, IntPtr init, out IntPtr buf);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateViewFn(IntPtr self, IntPtr res, IntPtr desc, out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateCsFn(IntPtr self, IntPtr code, IntPtr len, IntPtr linkage, out IntPtr cs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int MapFn(IntPtr self, IntPtr res, uint sub, int type, uint flags, out Mapped m);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void UnmapFn(IntPtr self, IntPtr res, uint sub);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void DispatchFn(IntPtr self, uint x, uint y, uint z);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResourceFn(IntPtr self, IntPtr dst, IntPtr src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void UpdateSubFn(IntPtr self, IntPtr dst, uint sub, IntPtr box, IntPtr src, uint rp, uint dp);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SetViewsFn(IntPtr self, uint start, uint num, IntPtr[] views);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SetUavsFn(IntPtr self, uint start, uint num, IntPtr[] uavs, IntPtr counts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SetCsFn(IntPtr self, IntPtr cs, IntPtr inst, uint num);

        static Delegate Fn(IntPtr obj, int slot, Type t)
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), t);
        }
        static void Release(IntPtr p) { if (p != IntPtr.Zero) ((ReleaseFn)Fn(p, 2, typeof(ReleaseFn)))(p); }

        static IntPtr dev, ctx, cbuf;
        static CreateBufferFn createBuffer; static CreateViewFn createSrv, createUav; static CreateCsFn createCs;
        static MapFn map; static UnmapFn unmap; static DispatchFn dispatch; static CopyResourceFn copyResource; static UpdateSubFn updateSub;
        static SetViewsFn setSrvs, setCbs; static SetUavsFn setUavs; static SetCsFn setCs;
        static readonly Dictionary<string, IntPtr> shaders = new Dictionary<string, IntPtr>();

        // shared by every shader source: parameters (PF(k): 16 floats, PI(k): 8 ints for the tool kernels), up to 8
        // input and 3 output buffers, and the GUARD that gives each thread its pixel i
        const string Header = @"
cbuffer P : register(b0) { int W; int H; int R; int N; float A; float B; float C; float D; int I0; int I1; int I2; int I3; float4 Fv[4]; int4 Jv[2]; };
StructuredBuffer<float> In0 : register(t0);
StructuredBuffer<float> In1 : register(t1);
StructuredBuffer<float> In2 : register(t2);
StructuredBuffer<float> In3 : register(t3);
StructuredBuffer<float> In4 : register(t4);
StructuredBuffer<float> In5 : register(t5);
StructuredBuffer<float> In6 : register(t6);
StructuredBuffer<float> In7 : register(t7);
RWStructuredBuffer<float> Out0 : register(u0);
RWStructuredBuffer<float> Out1 : register(u1);
RWStructuredBuffer<float> Out2 : register(u2);

#define GUARD if ((int)id.x >= W || (int)id.y >= H) return; int i = (int)id.y * W + (int)id.x;
#define PF(k) Fv[(k) / 4][(k) % 4]
#define PI(k) Jv[(k) / 4][(k) % 4]
";

        const string Hlsl = @"
[numthreads(16,16,1)] void BoxH(uint3 id : SV_DispatchThreadID) {
    GUARD
    int row = (int)id.y * W; float s = 0;
    for (int k = -R; k <= R; k++) s += In0[row + clamp((int)id.x + k, 0, W - 1)];
    Out0[i] = s / (2 * R + 1);
}
// Each thread walks N rows of one column keeping a running sum, so a pixel costs two reads whatever the
// radius (the horizontal pass stays a plain sum: there, neighbouring threads read neighbouring pixels,
// which the cache serves, and a running sum would make them read N apart instead).
[numthreads(16,16,1)] void BoxV(uint3 id : SV_DispatchThreadID) {
    int x = (int)id.x, y0 = (int)id.y * N;
    if (x >= W || y0 >= H) return;
    int last = H - 1, y1 = min(y0 + N, H);
    float inv = 1.0 / (2 * R + 1), s = 0;
    for (int k = -R; k <= R; k++) s += In0[clamp(y0 + k, 0, last) * W + x];
    for (int y = y0; y < y1; y++) {
        Out0[y * W + x] = s * inv;
        s += In0[min(y + R + 1, last) * W + x] - In0[max(y - R, 0) * W + x];
    }
}
[numthreads(16,16,1)] void Mul(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = In0[i] * In1[i]; }
[numthreads(16,16,1)] void GuidedAB(uint3 id : SV_DispatchThreadID) {
    GUARD
    float mI = In0[i], mp = In1[i];
    float a = (In3[i] - mI * mp) / ((In2[i] - mI * mI) + A);
    Out0[i] = a; Out1[i] = mp - a * mI;
}
[numthreads(16,16,1)] void GuidedQ(uint3 id : SV_DispatchThreadID) { GUARD Out0[i] = In0[i] * In1[i] + In2[i]; }

float Hash(int x, int y, int seed) {
    uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return (h & 0xFFFFFF) / 16777215.0;
}
// I1, I2: where this picture sits in the whole photo (live view parts get the photo's own grain)
[numthreads(16,16,1)] void Noise(uint3 id : SV_DispatchThreadID) {
    GUARD
    int x = (int)id.x + I1, y = (int)id.y + I2;
    Out0[i] = (Hash(x, y, I0) + Hash(x, y, I0 + 7) + Hash(x, y, I0 + 13) - 1.5) * 2.0;
}
// Lens blur: In0 = rgb (interleaved, highlight-boosted), In1 = kernel offsets (x,y pairs), N taps.
[numthreads(16,16,1)] void Gather(uint3 id : SV_DispatchThreadID) {
    GUARD
    float3 s = 0;
    for (int k = 0; k < N; k++) {
        int x = clamp((int)id.x + (int)In1[k * 2], 0, W - 1), y = clamp((int)id.y + (int)In1[k * 2 + 1], 0, H - 1);
        int j = (y * W + x) * 3;
        s += float3(In0[j], In0[j + 1], In0[j + 2]);
    }
    s = pow(max(s / N, 0), 1.0 / 2.2);
    Out0[i * 3] = s.x; Out0[i * 3 + 1] = s.y; Out0[i * 3 + 2] = s.z;
}
";

        static void EnsureInit()
        {
            if (tried) return;
            lock (lk)
            {
                if (tried) return;
                // "tried" only once the device is ready: a render arriving during the start-up initialisation waits
                // for the graphics card instead of quietly running on the processor
                try { ok = Init(); }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // Picks the adapter with the most dedicated memory (the discrete GPU on hybrid laptops).
        static IntPtr BestAdapter()
        {
            Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            IntPtr fac;
            if (CreateDXGIFactory1(ref iid, out fac) != 0) return IntPtr.Zero;
            EnumAdaptersFn en = (EnumAdaptersFn)Fn(fac, 7, typeof(EnumAdaptersFn));
            IntPtr best = IntPtr.Zero; long bestMem = -1; string bestName = ""; int bestIdx = -1;
            IntPtr pref = IntPtr.Zero; long prefMem = 0; string prefName = ""; int prefIdx = -1;
            Adapters.Clear(); AdapterNames.Clear();
            IntPtr desc = Marshal.AllocHGlobal(512);
            for (uint i = 0; i < 16; i++)
            {
                IntPtr ad;
                if (en(fac, i, out ad) != 0) break;
                GetDescFn gd = (GetDescFn)Fn(ad, 8, typeof(GetDescFn));
                if (gd(ad, desc) == 0)
                {
                    string name = Marshal.PtrToStringUni(desc, 128).TrimEnd('\0');
                    int vendor = Marshal.ReadInt32(desc, 256);
                    long mem = Marshal.ReadInt64(desc, 272);
                    int zz = name.IndexOf('\0'); if (zz >= 0) name = name.Substring(0, zz);
                    if (vendor != 0x1414)
                    {
                        Adapters.Add(name + " (" + (mem >> 20) + " MB, " +
                            (mem < 512L * 1024 * 1024 ? "shares the PC's memory" : "its own memory") + ")");
                        AdapterNames.Add(name);
                    }
                    if (vendor != 0x1414) // 0x1414 = Microsoft Basic Render Driver
                    {
                        // the one the speed test settled on, or the one asked for by number, wins over memory
                        bool asked = WantedAdapter >= 0 ? (int)i == WantedAdapter
                                   : Preferred.Length > 0 && name == Preferred;
                        if (asked)
                        {
                            if (pref != IntPtr.Zero) Release(pref);
                            pref = ad; prefMem = mem; prefName = name; prefIdx = (int)i;
                            continue;
                        }
                        if (mem > bestMem)
                        {
                            if (best != IntPtr.Zero) Release(best);
                            best = ad; bestMem = mem; bestName = name; bestIdx = (int)i;
                            continue;
                        }
                    }
                }
                Release(ad);
            }
            Marshal.FreeHGlobal(desc);
            Release(fac);
            if (pref != IntPtr.Zero)
            {
                if (best != IntPtr.Zero) Release(best);
                best = pref; bestMem = prefMem; bestName = prefName; bestIdx = prefIdx;
            }
            AdapterIndex = bestIdx; VideoMemory = bestMem;
            int z = bestName.IndexOf('\0');
            Adapter = z >= 0 ? bestName.Substring(0, z) : bestName;
            return best;
        }

        static bool Init()
        {
            IntPtr adapter = BestAdapter();
            int driver = adapter == IntPtr.Zero ? 1 : 0;   // hardware, or the adapter given
            if (SoftwareGpu)
            {
                if (adapter != IntPtr.Zero) Release(adapter);
                adapter = IntPtr.Zero; driver = 5;           // WARP
                Adapter = "Software GPU (WARP, for tests)"; VideoMemory = 0;
            }
            int level;
            int[] levels = { 0xb000 }; // feature level 11_0 (compute shader 5.0)
            int hr = D3D11CreateDevice(adapter, driver, IntPtr.Zero, 0, levels, 1, 7, out dev, out level, out ctx);
            if (adapter != IntPtr.Zero) Release(adapter);
            if (hr != 0) { LastError = "D3D11CreateDevice failed 0x" + hr.ToString("X8"); return false; }
            SizeLimits();
            createBuffer = (CreateBufferFn)Fn(dev, 3, typeof(CreateBufferFn));
            createSrv = (CreateViewFn)Fn(dev, 7, typeof(CreateViewFn));
            createUav = (CreateViewFn)Fn(dev, 8, typeof(CreateViewFn));
            createCs = (CreateCsFn)Fn(dev, 18, typeof(CreateCsFn));
            map = (MapFn)Fn(ctx, 14, typeof(MapFn));
            unmap = (UnmapFn)Fn(ctx, 15, typeof(UnmapFn));
            dispatch = (DispatchFn)Fn(ctx, 41, typeof(DispatchFn));
            copyResource = (CopyResourceFn)Fn(ctx, 47, typeof(CopyResourceFn));
            updateSub = (UpdateSubFn)Fn(ctx, 48, typeof(UpdateSubFn));
            setSrvs = (SetViewsFn)Fn(ctx, 67, typeof(SetViewsFn));
            setUavs = (SetUavsFn)Fn(ctx, 68, typeof(SetUavsFn));
            setCs = (SetCsFn)Fn(ctx, 69, typeof(SetCsFn));
            setCbs = (SetViewsFn)Fn(ctx, 71, typeof(SetViewsFn));

            // the blurs are needed at once; the other shaders are compiled the first time they run (WarmShaders
            // compiles them in the background)
            foreach (string entry in new string[] { "BoxH", "BoxV" })
            {
                try { Shader(entry); }
                catch (Exception ex) { LastError = ex.Message; return false; }
            }
            BufferDesc cd = new BufferDesc();
            cd.ByteWidth = CbSize; cd.Usage = 0; cd.BindFlags = 0x4; // constant buffer
            if (createBuffer(dev, ref cd, IntPtr.Zero, out cbuf) != 0) { LastError = "constant buffer"; return false; }
            return true;
        }

        const int CbSize = 144;

        // A shader that does not compile: that tool runs on the CPU, the GPU stays in use for the rest.
        public class ShaderException : Exception { public ShaderException(string m) : base(m) { } }

        // the shader sources: each entry point is looked up in the source that defines it
        static string[] Sources { get { return new string[] { Hlsl, ToolHlsl }; } }

        // compiled on first use (callers hold lk, or run before the device is shared)
        static IntPtr Shader(string entry)
        {
            IntPtr cs;
            if (shaders.TryGetValue(entry, out cs)) return cs;
            string text = null;
            foreach (string s in Sources) if (s.Contains("void " + entry + "(")) { text = s; break; }
            if (text == null) throw new ShaderException("no shader " + entry);
            byte[] src = Encoding.ASCII.GetBytes(Header + text);
            IntPtr code, err;
            int hr = D3DCompile(src, (IntPtr)src.Length, "pixtune.hlsl", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 1u << 15 /* optimization level 3 */, 0, out code, out err);
            if (hr != 0)
            {
                string msg = err != IntPtr.Zero ? Marshal.PtrToStringAnsi(((BlobPtrFn)Fn(err, 3, typeof(BlobPtrFn)))(err)) : hr.ToString("X8");
                if (err != IntPtr.Zero) Release(err);
                throw new ShaderException("Shader " + entry + ": " + msg);
            }
            IntPtr ptr = ((BlobPtrFn)Fn(code, 3, typeof(BlobPtrFn)))(code);
            IntPtr len = ((BlobPtrFn)Fn(code, 4, typeof(BlobPtrFn)))(code);
            hr = createCs(dev, ptr, len, IntPtr.Zero, out cs);
            Release(code);
            if (hr != 0) throw new ShaderException("CreateComputeShader " + entry);
            shaders[entry] = cs;
            return cs;
        }

        // Compiles every shader in the background after start-up, one at a time, so a tool's first use does not
        // wait for its compilation.
        public static void WarmShaders()
        {
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                if (!Available) return;
                List<string> entries = new List<string>();
                foreach (string s in Sources)
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s, @"void (\w+)\(uint3"))
                        entries.Add(m.Groups[1].Value);
                foreach (string e in entries)
                    lock (lk)
                    {
                        if (!ok) return;
                        try { Shader(e); } catch (ShaderException ex) { LastError = ex.Message; }
                    }
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        }

        // ---------------- buffers ----------------
        internal class GBuf { public IntPtr Buf, Srv, Uav; public int N; }
        static readonly Dictionary<int, Stack<GBuf>> pool = new Dictionary<int, Stack<GBuf>>();
        static readonly Dictionary<int, IntPtr> staging = new Dictionary<int, IntPtr>();

        static GBuf Get(int n)
        {
            Stack<GBuf> st;
            if (pool.TryGetValue(n, out st) && st.Count > 0) return st.Pop();
            BufferDesc d = new BufferDesc();
            d.ByteWidth = (uint)(n * 4); d.Usage = 0; d.BindFlags = 0x8 | 0x80; d.MiscFlags = 0x40; d.StructureByteStride = 4;
            GBuf g = new GBuf(); g.N = n;
            if (createBuffer(dev, ref d, IntPtr.Zero, out g.Buf) != 0) throw new Exception("GPU buffer allocation failed");
            if (createSrv(dev, g.Buf, IntPtr.Zero, out g.Srv) != 0) throw new Exception("GPU view failed");
            if (createUav(dev, g.Buf, IntPtr.Zero, out g.Uav) != 0) throw new Exception("GPU view failed");
            return g;
        }

        static void Put(GBuf g)
        {
            Stack<GBuf> st;
            if (!pool.TryGetValue(g.N, out st)) { st = new Stack<GBuf>(); pool[g.N] = st; }
            // a tool on the GPU uses up to a dozen planes of the photo's size: keep them for the next frame, within a
            // quarter of the card's memory (at most 2 GB) per size, so full-resolution planes (96 MB at 24 MP) are
            // not made again for every frame
            // (an integrated GPU's memory is the PC's RAM: a share of the RAM instead)
            long budget = Integrated ? Math.Max(128L << 20, Math.Min(512L << 20, SystemMemory / 32))
                                     : Math.Max(256L << 20, Math.Min(2048L << 20, VideoMemory / 4));
            int keep = (int)Math.Max(4, Math.Min(24, budget / Math.Max(1L, g.N * 4L)));
            if (st.Count < keep) st.Push(g);
            else { Release(g.Uav); Release(g.Srv); Release(g.Buf); }
        }

        // Frees pooled GPU memory (called when a new photo is opened).
        public static void Trim()
        {
            if (!ok) return;
            lock (lk)
            {
                SettleAll();
                ForgetGMemo();
                foreach (Stack<GBuf> st in pool.Values) foreach (GBuf g in st) { Release(g.Uav); Release(g.Srv); Release(g.Buf); }
                pool.Clear();
                foreach (IntPtr p in staging.Values) Release(p);
                staging.Clear();
                ForgetChunk();
            }
        }

        // Starts again on another card. Pictures on the old one come back to the processor first, then every
        // piece of it is let go and the device is made afresh, so the speed test can try each card in turn.
        public static bool SwitchTo(string adapterName)
        {
            lock (lk)
            {
                if (ok)
                {
                    SettleAll();
                    ForgetGMemo();
                    foreach (Stack<GBuf> st in pool.Values) foreach (GBuf g in st) { Release(g.Uav); Release(g.Srv); Release(g.Buf); }
                    pool.Clear();
                    foreach (IntPtr p in staging.Values) Release(p);
                    staging.Clear();
                    ForgetChunk();
                    foreach (IntPtr sh in shaders.Values) Release(sh);
                    shaders.Clear();
                    Release(cbuf); cbuf = IntPtr.Zero;
                    Release(ctx); ctx = IntPtr.Zero;
                    Release(dev); dev = IntPtr.Zero;
                }
                Preferred = adapterName ?? "";
                ok = false; tried = false; LastError = "";
                try { ok = Init(); }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
                return ok;
            }
        }

        static void Upload(GBuf g, float[] data)
        {
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { updateSub(ctx, g.Buf, 0, IntPtr.Zero, h.AddrOfPinnedObject(), 0, 0); }
            finally { h.Free(); }
        }

        static GBuf Up(float[] data) { GBuf g = Get(data.Length); Upload(g, data); return g; }

        static float[] Down(GBuf g) { return Down(g, null); }

        // into: an array of at least g.N values to fill instead of a new one
        static float[] Down(GBuf g, float[] into)
        {
            IntPtr st;
            if (!staging.TryGetValue(g.N, out st))
            {
                BufferDesc d = new BufferDesc();
                d.ByteWidth = (uint)(g.N * 4); d.Usage = 3; d.CPUAccessFlags = 0x20000; d.MiscFlags = 0x40; d.StructureByteStride = 4;
                if (createBuffer(dev, ref d, IntPtr.Zero, out st) != 0) throw new Exception("GPU staging buffer failed");
                staging[g.N] = st;
            }
            copyResource(ctx, st, g.Buf);
            Mapped m;
            if (map(ctx, st, 0, 1, 0, out m) != 0) throw new Exception("GPU readback failed");
            float[] r = into != null && into.Length >= g.N ? into : new float[g.N];
            Marshal.Copy(m.pData, r, 0, g.N);
            unmap(ctx, st, 0);
            return r;
        }

        static readonly IntPtr[] nullSrv = new IntPtr[8], nullUav = new IntPtr[3];

        static void Run(string kernel, int w, int h, int r, int n, float a, int i0, GBuf[] ins, GBuf[] outs)
        {
            RunCb(kernel, w, h, r, n, a, 0, new int[] { i0 }, null, null, ins, outs);
        }

        // The vertical box pass: each thread walks a run of rows, long enough that priming its sum pays off.
        static void RunBoxV(int w, int h, int r, GBuf[] ins, GBuf[] outs)
        {
            int ch = 2 * r + 1; if (ch < 16) ch = 16; else if (ch > 128) ch = 128;
            RunCb("BoxV", w, h, r, ch, 0, 0, new int[] { 0 }, null, null, ins, outs, (h + ch - 1) / ch);
        }

        // One dispatch over w x h pixels. iv: I0..I3; f: PF(0..15); j: PI(0..7) (the tool kernels' parameters).
        // dispH: how many rows of threads to start, when a thread covers more than one row (0 = one per row)
        static void RunCb(string kernel, int w, int h, int r, int n, float a, float b, int[] iv, float[] f, int[] j, GBuf[] ins, GBuf[] outs, int dispH = 0)
        {
            if (ins.Length > 8 || outs.Length > 3) throw new ArgumentException("too many GPU buffers for " + kernel);
            byte[] cb = new byte[CbSize];
            Buffer.BlockCopy(BitConverter.GetBytes(w), 0, cb, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(h), 0, cb, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(r), 0, cb, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(n), 0, cb, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(a), 0, cb, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b), 0, cb, 20, 4);
            if (iv != null) Buffer.BlockCopy(iv, 0, cb, 32, Math.Min(4, iv.Length) * 4);
            if (f != null) Buffer.BlockCopy(f, 0, cb, 48, Math.Min(16, f.Length) * 4);
            if (j != null) Buffer.BlockCopy(j, 0, cb, 112, Math.Min(8, j.Length) * 4);
            GCHandle hc = GCHandle.Alloc(cb, GCHandleType.Pinned);
            try { updateSub(ctx, cbuf, 0, IntPtr.Zero, hc.AddrOfPinnedObject(), 0, 0); }
            finally { hc.Free(); }
            IntPtr[] srv = new IntPtr[8], uav = new IntPtr[3];
            for (int k = 0; k < ins.Length; k++) srv[k] = ins[k].Srv;
            for (int k = 0; k < outs.Length; k++) uav[k] = outs[k].Uav;
            setCs(ctx, Shader(kernel), IntPtr.Zero, 0);
            setCbs(ctx, 0, 1, new IntPtr[] { cbuf });
            setUavs(ctx, 0, 3, nullUav, IntPtr.Zero);
            setSrvs(ctx, 0, 8, srv);
            setUavs(ctx, 0, 3, uav, IntPtr.Zero);
            dispatch(ctx, (uint)((w + 15) / 16), (uint)(((dispH > 0 ? dispH : h) + 15) / 16), 1);
            setUavs(ctx, 0, 3, nullUav, IntPtr.Zero);
            setSrvs(ctx, 0, 8, nullSrv);
        }

        static void Fail(Exception ex)
        {
            LastError = ex.Message;
            // pictures kept on the card come back first, then the CPU path takes over for this session
            SettleAll();
            ForgetGMemo();
            ok = false;
        }

        // ---------------- public operations (null = use the CPU) ----------------

        // Successive box blurs (H then V for each radius): a separable gaussian approximation.
        public static float[] BoxChain(float[] a, int w, int h, int[] radii)
        {
            lock (lk)
            {
                if (!ok) return null;
                try
                {
                    GBuf x = Up(a), t = Get(a.Length), y = Get(a.Length);
                    foreach (int r in radii)
                    {
                        Run("BoxH", w, h, r, 0, 0, 0, new GBuf[] { x }, new GBuf[] { t });
                        RunBoxV(w, h, r, new GBuf[] { t }, new GBuf[] { y });
                        GBuf s = x; x = y; y = s;
                    }
                    float[] res = Down(x);
                    Put(x); Put(t); Put(y);
                    return res;
                }
                catch (Exception ex) { Fail(ex); return null; }
            }
        }

        static GBuf BoxOnGpu(GBuf src, int w, int h, int r, GBuf tmp)
        {
            GBuf o = Get(src.N);
            Run("BoxH", w, h, r, 0, 0, 0, new GBuf[] { src }, new GBuf[] { tmp });
            RunBoxV(w, h, r, new GBuf[] { tmp }, new GBuf[] { o });
            return o;
        }

        // Guided filter entirely on the GPU: one upload, one download.
        public static float[] Guided(float[] I, float[] p, int w, int h, int r, float eps)
        {
            lock (lk)
            {
                if (!ok) return null;
                try
                {
                    int n = I.Length;
                    GBuf gi = Up(I), gp = p == I ? gi : Up(p), tmp = Get(n);
                    GBuf ii = Get(n), ip = Get(n);
                    Run("Mul", w, h, 0, 0, 0, 0, new GBuf[] { gi, gi }, new GBuf[] { ii });
                    Run("Mul", w, h, 0, 0, 0, 0, new GBuf[] { gi, gp }, new GBuf[] { ip });
                    GBuf mI = BoxOnGpu(gi, w, h, r, tmp), mp = p == I ? mI : BoxOnGpu(gp, w, h, r, tmp);
                    GBuf mII = BoxOnGpu(ii, w, h, r, tmp), mIp = BoxOnGpu(ip, w, h, r, tmp);
                    GBuf ga = Get(n), gb = Get(n);
                    Run("GuidedAB", w, h, 0, 0, eps, 0, new GBuf[] { mI, mp, mII, mIp }, new GBuf[] { ga, gb });
                    GBuf ma = BoxOnGpu(ga, w, h, r, tmp), mb = BoxOnGpu(gb, w, h, r, tmp);
                    GBuf q = Get(n);
                    Run("GuidedQ", w, h, 0, 0, 0, 0, new GBuf[] { ma, gi, mb }, new GBuf[] { q });
                    float[] res = Down(q);
                    foreach (GBuf g in new GBuf[] { gi, tmp, ii, ip, mI, mII, mIp, ga, gb, ma, mb, q }) Put(g);
                    if (p != I) { Put(gp); Put(mp); }
                    return res;
                }
                catch (Exception ex) { Fail(ex); return null; }
            }
        }

        public static float[] Noise(int w, int h, int seed) { return Noise(w, h, seed, 0, 0); }

        // ox, oy: where this picture sits in the whole photo
        public static float[] Noise(int w, int h, int seed, int ox, int oy)
        {
            lock (lk)
            {
                if (!ok) return null;
                try
                {
                    GBuf o = Get(w * h);
                    RunCb("Noise", w, h, 0, 0, 0, 0, new int[] { seed, ox, oy }, null, null, new GBuf[0], new GBuf[] { o });
                    float[] res = Down(o);
                    Put(o);
                    return res;
                }
                catch (Exception ex) { Fail(ex); return null; }
            }
        }

        // Shaped bokeh convolution. rgb is interleaved, offsets are x,y pairs.
        public static float[] Gather(float[] rgb, int w, int h, int[] ox, int[] oy)
        {
            lock (lk)
            {
                if (!ok) return null;
                try
                {
                    float[] off = new float[ox.Length * 2];
                    for (int k = 0; k < ox.Length; k++) { off[k * 2] = ox[k]; off[k * 2 + 1] = oy[k]; }
                    GBuf src = Up(rgb), go = Up(off), o = Get(rgb.Length);
                    Run("Gather", w, h, 0, ox.Length, 0, 0, new GBuf[] { src, go }, new GBuf[] { o });
                    float[] res = Down(o);
                    Put(src); Put(go); Put(o);
                    return res;
                }
                catch (Exception ex) { Fail(ex); return null; }
            }
        }
    }
}

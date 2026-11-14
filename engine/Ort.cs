using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PixTune
{
    // ONNX Runtime through its C API (onnxruntime.dll loaded at run time, no managed package needed).
    // Shared by the AI tools: LaMa (healing) and Stable Diffusion inpainting (expand).
    public static class Ort
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetApiBaseFn();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetApiFn(uint version);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr GetErrorMessageFn(IntPtr status);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void ReleaseFn(IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr CreateEnvFn(int level, [MarshalAs(UnmanagedType.LPStr)] string id, out IntPtr env);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr CreateSessionOptionsFn(out IntPtr opts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr OptsIntFn(IntPtr opts, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr OptsFn(IntPtr opts);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr CreateSessionFn(IntPtr env, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr opts, out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr CountFn(IntPtr session, out UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr GetAllocatorFn(out IntPtr allocator);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr NameFn(IntPtr session, UIntPtr index, IntPtr allocator, out IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr AllocatorFreeFn(IntPtr allocator, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr CreateCpuMemoryInfoFn(int allocType, int memType, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr CreateTensorFn(IntPtr info, IntPtr data, UIntPtr bytes, long[] shape, UIntPtr dims, int type, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr RunFn(IntPtr session, IntPtr runOptions, IntPtr[] inNames, IntPtr[] inputs, UIntPtr nIn, IntPtr[] outNames, UIntPtr nOut, IntPtr[] outputs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr GetDataFn(IntPtr value, out IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr TypeInfoFn(IntPtr session, UIntPtr index, out IntPtr typeInfo);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr CastFn(IntPtr typeInfo, out IntPtr tensorInfo);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr ElemTypeFn(IntPtr tensorInfo, out int type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr DimCountFn(IntPtr tensorInfo, out UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr DimsFn(IntPtr tensorInfo, long[] dims, UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr ValueShapeFn(IntPtr value, out IntPtr tensorInfo);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr SymbolicDimsFn(IntPtr tensorInfo, IntPtr[] names, UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate IntPtr FreeDimFn(IntPtr opts, [MarshalAs(UnmanagedType.LPStr)] string name, long value);

        // positions in the OrtApi function table, from onnxruntime_c_api.h (ORT_API_VERSION 24)
        const uint ApiVersion = 24;
        internal const int iGetErrorMessage = 2, iCreateEnv = 3, iCreateSession = 7, iRun = 9, iCreateSessionOptions = 10,
            iSetExecutionMode = 13, iDisableMemPattern = 17, iSetGraphOptLevel = 23, iSetIntraOpThreads = 24,
            iInputCount = 30, iOutputCount = 31, iInputTypeInfo = 33, iInputName = 36, iOutputName = 37, iCreateTensor = 49,
            iGetTensorData = 51, iCastTypeInfo = 55, iElemType = 60, iDimCount = 61, iDims = 62, iSymbolicDims = 63, iValueShape = 65,
            iFreeDimByName = 124,
            iCreateCpuMemoryInfo = 69, iAllocatorFree = 76, iGetAllocator = 78,
            iReleaseStatus = 93, iReleaseMemoryInfo = 94, iReleaseSession = 95, iReleaseValue = 96, iReleaseTypeInfo = 98,
            iReleaseShapeInfo = 99, iReleaseSessionOptions = 100;

        // tensor element types (ONNXTensorElementDataType)
        public const int Float = 1, Int32 = 6, Int64 = 7, Float16 = 10;

        internal static IntPtr api, env, memInfo, dmlAppend;
        static readonly object lk = new object();

        internal static T Fn<T>(int index) where T : class
        {
            IntPtr p = Marshal.ReadIntPtr(api, index * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        internal static void Check(IntPtr status)
        {
            if (status == IntPtr.Zero) return;
            string msg = Marshal.PtrToStringAnsi(Fn<GetErrorMessageFn>(iGetErrorMessage)(status));
            Fn<ReleaseFn>(iReleaseStatus)(status);
            throw new OrtException(msg);
        }

        // Loads the runtime once per process; throws with a readable reason on failure.
        public static void Init(string runtimePath)
        {
            lock (lk)
            {
                if (api != IntPtr.Zero) return;
                if (!File.Exists(runtimePath)) throw new Exception("onnxruntime.dll is not installed");
                // LOAD_WITH_ALTERED_SEARCH_PATH: dependencies are looked up next to the DLL first
                IntPtr mod = LoadLibraryEx(runtimePath, IntPtr.Zero, 0x8);
                if (mod == IntPtr.Zero) throw new Exception("onnxruntime.dll could not be loaded (" + Marshal.GetLastWin32Error() + ")");
                IntPtr getBase = GetProcAddress(mod, "OrtGetApiBase");
                if (getBase == IntPtr.Zero) throw new Exception("onnxruntime.dll is not a valid ONNX Runtime");
                IntPtr bas = ((GetApiBaseFn)Marshal.GetDelegateForFunctionPointer(getBase, typeof(GetApiBaseFn)))();
                GetApiFn getApi = (GetApiFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(bas, 0), typeof(GetApiFn));
                IntPtr a = getApi(ApiVersion);
                if (a == IntPtr.Zero) throw new Exception("unsupported ONNX Runtime version");
                api = a;
                Check(Fn<CreateEnvFn>(iCreateEnv)(LogLevel, "PixTune", out env));
                Check(Fn<CreateCpuMemoryInfoFn>(iCreateCpuMemoryInfo)(0, 0, out memInfo));
                dmlAppend = GetProcAddress(mod, "OrtSessionOptionsAppendExecutionProvider_DML");
            }
        }

        public static int LogLevel = 3; // 0 verbose .. 3 errors only; set before Init for diagnostics

        public static bool HasDirectML { get { return dmlAppend != IntPtr.Zero; } }

        // IEEE half precision conversions (round to nearest)
        public static ushort ToHalf(float f)
        {
            uint x = (uint)BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            uint sign = (x >> 16) & 0x8000;
            int exp = (int)((x >> 23) & 0xff) - 127 + 15;
            uint man = x & 0x7fffff;
            if (exp >= 31) return (ushort)(sign | 0x7c00);
            if (exp <= 0)
            {
                if (exp < -10) return (ushort)sign;
                man |= 0x800000;
                int shift = 14 - exp;
                uint h = man >> shift;
                if (((man >> (shift - 1)) & 1) != 0) h++;
                return (ushort)(sign | h);
            }
            uint r = sign | ((uint)exp << 10) | (man >> 13);
            if ((man & 0x1000) != 0) r++;
            return (ushort)r;
        }

        public static float FromHalf(ushort h)
        {
            uint sign = (uint)(h & 0x8000) << 16;
            int exp = (h >> 10) & 0x1f;
            uint man = (uint)(h & 0x3ff);
            uint bits;
            if (exp == 0)
            {
                if (man == 0) bits = sign;
                else
                {
                    exp = 1;
                    while ((man & 0x400) == 0) { man <<= 1; exp--; }
                    man &= 0x3ff;
                    bits = sign | ((uint)(exp - 15 + 127) << 23) | (man << 13);
                }
            }
            else if (exp == 31) bits = sign | 0x7f800000 | (man << 13);
            else bits = sign | ((uint)(exp - 15 + 127) << 23) | (man << 13);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
    }

    // An error reported by ONNX Runtime itself (not by PixTune's own checks).
    public class OrtException : Exception { public OrtException(string m) : base(m) { } }

    // A tensor handed to or returned by a session; values are kept as float (or long for integer inputs).
    public class Tensor
    {
        public long[] Shape;
        public float[] F;
        public long[] L;
        public Tensor(float[] data, params long[] shape) { F = data; Shape = shape; }
        public Tensor(long[] data, params long[] shape) { L = data; Shape = shape; }
    }

    // One loaded model. Inputs are converted to the element type the model declares (fp16, fp32, int32, int64).
    public class OrtSession : IDisposable
    {
        IntPtr session;
        public string Device = "";
        public string[] InputNames, OutputNames;
        public int[] InputTypes;
        public long[][] InputShapes;
        public string[][] InputDimNames;   // symbolic names of the dynamic dimensions ("" for fixed ones)
        IntPtr[] inNamePtrs, outNamePtrs;
        readonly object lk = new object();
        string path; int opt; Dictionary<string, long> fixedDims;
        bool gpuOnly;   // the GPU check's sessions: a failure is reported, not hidden by the CPU

        static string GpuName { get { return "GPU (DirectML" + (Gpu.Adapter.Length > 0 ? ", " + Gpu.Adapter : "") + ")"; } }

        // Opens the model on the GPU through DirectML when gpu is true and possible, otherwise on the CPU.
        public static OrtSession Open(string modelPath, bool gpu, int optLevel) { return Open(modelPath, gpu, optLevel, null); }

        // dims: values for named dynamic dimensions. Fixing them lets DirectML plan the whole graph on the GPU
        // instead of computing shapes on the CPU at every run.
        // A model the GPU check found unable to run on the graphics card goes straight to the CPU.
        public static OrtSession Open(string modelPath, bool gpu, int optLevel, Dictionary<string, long> dims)
        {
            OrtSession s = new OrtSession();
            s.path = modelPath; s.opt = optLevel; s.fixedDims = dims;
            string gpuError = null;
            if (gpu && Ort.HasDirectML && Gpu.Enabled && Gpu.Available && !GpuCheck.CpuOnly(modelPath))
            {
                try { s.session = Create(modelPath, true, optLevel, dims); s.Device = GpuName; }
                catch (Exception ex) { gpuError = ex.Message; GpuCheck.Failed(modelPath, ex.Message); }
            }
            if (s.session == IntPtr.Zero)
            {
                try { s.session = Create(modelPath, false, optLevel, dims); s.Device = "CPU"; }
                catch (Exception ex) { throw new Exception(gpuError != null ? "DirectML: " + gpuError + " / CPU: " + ex.Message : ex.Message); }
            }
            s.ReadSignature();
            return s;
        }

        // Opens the model on the GPU only (used by the GPU check); throws when DirectML cannot take it.
        public static OrtSession OpenGpu(string modelPath, int optLevel, Dictionary<string, long> dims)
        {
            if (!Ort.HasDirectML || !Gpu.Enabled || !Gpu.Available) throw new Exception("no graphics card usable by DirectML");
            OrtSession s = new OrtSession();
            s.path = modelPath; s.opt = optLevel; s.fixedDims = dims; s.gpuOnly = true;
            s.session = Create(modelPath, true, optLevel, dims);
            s.Device = GpuName;
            s.ReadSignature();
            return s;
        }

        static IntPtr Create(string modelPath, bool dml, int optLevel, Dictionary<string, long> dims)
        {
            IntPtr opts, sess;
            Ort.Check(Ort.Fn<Ort.CreateSessionOptionsFn>(Ort.iCreateSessionOptions)(out opts));
            try
            {
                Ort.Check(Ort.Fn<Ort.OptsIntFn>(Ort.iSetGraphOptLevel)(opts, optLevel));
                if (dims != null)
                    foreach (KeyValuePair<string, long> kv in dims) Ort.Check(Ort.Fn<Ort.FreeDimFn>(Ort.iFreeDimByName)(opts, kv.Key, kv.Value));
                if (dml)
                {
                    // DirectML wants sequential execution without the memory pattern optimisation
                    Ort.Check(Ort.Fn<Ort.OptsIntFn>(Ort.iSetExecutionMode)(opts, 0));
                    Ort.Check(Ort.Fn<Ort.OptsFn>(Ort.iDisableMemPattern)(opts));
                    Ort.Check(((Ort.OptsIntFn)Marshal.GetDelegateForFunctionPointer(Ort.dmlAppend, typeof(Ort.OptsIntFn)))(opts, Math.Max(0, Gpu.AdapterIndex)));
                }
                else Ort.Check(Ort.Fn<Ort.OptsIntFn>(Ort.iSetIntraOpThreads)(opts, Environment.ProcessorCount));
                Ort.Check(Ort.Fn<Ort.CreateSessionFn>(Ort.iCreateSession)(Ort.env, modelPath, opts, out sess));
                return sess;
            }
            finally { Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseSessionOptions)(opts); }
        }

        void ReadSignature()
        {
            IntPtr alloc;
            Ort.Check(Ort.Fn<Ort.GetAllocatorFn>(Ort.iGetAllocator)(out alloc));
            UIntPtr nIn, nOut;
            Ort.Check(Ort.Fn<Ort.CountFn>(Ort.iInputCount)(session, out nIn));
            Ort.Check(Ort.Fn<Ort.CountFn>(Ort.iOutputCount)(session, out nOut));
            int ni = (int)nIn.ToUInt32(), no = (int)nOut.ToUInt32();
            InputNames = new string[ni]; inNamePtrs = new IntPtr[ni]; InputTypes = new int[ni]; InputShapes = new long[ni][]; InputDimNames = new string[ni][];
            OutputNames = new string[no]; outNamePtrs = new IntPtr[no];
            for (int i = 0; i < ni; i++)
            {
                IntPtr p; Ort.Check(Ort.Fn<Ort.NameFn>(Ort.iInputName)(session, (UIntPtr)(uint)i, alloc, out p));
                InputNames[i] = Marshal.PtrToStringAnsi(p); inNamePtrs[i] = Marshal.StringToHGlobalAnsi(InputNames[i]);
                Ort.Fn<Ort.AllocatorFreeFn>(Ort.iAllocatorFree)(alloc, p);
                IntPtr ti, tsi;
                Ort.Check(Ort.Fn<Ort.TypeInfoFn>(Ort.iInputTypeInfo)(session, (UIntPtr)(uint)i, out ti));
                try
                {
                    Ort.Check(Ort.Fn<Ort.CastFn>(Ort.iCastTypeInfo)(ti, out tsi));
                    int et; Ort.Check(Ort.Fn<Ort.ElemTypeFn>(Ort.iElemType)(tsi, out et));
                    UIntPtr nd; Ort.Check(Ort.Fn<Ort.DimCountFn>(Ort.iDimCount)(tsi, out nd));
                    long[] dims = new long[nd.ToUInt32()];
                    if (dims.Length > 0) Ort.Check(Ort.Fn<Ort.DimsFn>(Ort.iDims)(tsi, dims, nd));
                    IntPtr[] sym = new IntPtr[dims.Length];
                    if (dims.Length > 0) Ort.Check(Ort.Fn<Ort.SymbolicDimsFn>(Ort.iSymbolicDims)(tsi, sym, nd));
                    string[] symNames = new string[dims.Length];
                    for (int k = 0; k < dims.Length; k++) symNames[k] = sym[k] == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(sym[k]);
                    InputTypes[i] = et; InputShapes[i] = dims; InputDimNames[i] = symNames;
                }
                finally { Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseTypeInfo)(ti); }
            }
            for (int i = 0; i < no; i++)
            {
                IntPtr p; Ort.Check(Ort.Fn<Ort.NameFn>(Ort.iOutputName)(session, (UIntPtr)(uint)i, alloc, out p));
                OutputNames[i] = Marshal.PtrToStringAnsi(p); outNamePtrs[i] = Marshal.StringToHGlobalAnsi(OutputNames[i]);
                Ort.Fn<Ort.AllocatorFreeFn>(Ort.iAllocatorFree)(alloc, p);
            }
        }

        public int InputIndex(string name)
        {
            for (int i = 0; i < InputNames.Length; i++) if (InputNames[i] == name) return i;
            return -1;
        }

        // Runs the model; inputs are given in the model's input order. Returns the first output.
        public Tensor Run(params Tensor[] inputs) { return RunOutput(0, inputs); }

        // Same, returning the named output (models with several outputs; only that one is computed).
        public Tensor Run(string output, params Tensor[] inputs)
        {
            int k = Array.IndexOf(OutputNames, output);
            if (k < 0) throw new ArgumentException("the model has no output named " + output);
            return RunOutput(k, inputs);
        }

        Tensor RunOutput(int outIndex, Tensor[] inputs)
        {
            if (inputs.Length != InputNames.Length) throw new ArgumentException("the model expects " + InputNames.Length + " inputs");
            try { return RunOnce(outIndex, inputs); }
            catch (OrtException ex)
            {
                if (gpuOnly || !Device.StartsWith("GPU") || GpuCheck.IsMemoryError(ex.Message)) throw;
                // the graphics card could not run the model: the processor takes over, now and from now on
                lock (lk)
                {
                    if (Device.StartsWith("GPU"))
                    {
                        IntPtr old = session;
                        session = Create(path, false, opt, fixedDims);
                        Device = "CPU";
                        Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseSession)(old);
                    }
                }
                GpuCheck.Failed(path, ex.Message);
                return RunOnce(outIndex, inputs);
            }
        }

        Tensor RunOnce(int outIndex, Tensor[] inputs)
        {
            List<GCHandle> pins = new List<GCHandle>();
            IntPtr[] values = new IntPtr[inputs.Length];
            IntPtr[] outputs = new IntPtr[1];
            Ort.ReleaseFn rel = Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseValue);
            lock (lk)
            {
                try
                {
                    Ort.CreateTensorFn create = Ort.Fn<Ort.CreateTensorFn>(Ort.iCreateTensor);
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        Tensor t = inputs[i];
                        int et = InputTypes[i];
                        Array buf; int bytes;
                        long n = 1; foreach (long d in t.Shape) n *= d;
                        if (et == Ort.Float16)
                        {
                            ushort[] h = new ushort[n];
                            for (long k = 0; k < n; k++) h[k] = Ort.ToHalf(t.F != null ? t.F[k] : t.L[k]);
                            buf = h; bytes = (int)n * 2;
                        }
                        else if (et == Ort.Float)
                        {
                            float[] f = t.F ?? Array.ConvertAll(t.L, v => (float)v);
                            buf = f; bytes = (int)n * 4;
                        }
                        else if (et == Ort.Int64)
                        {
                            long[] l = t.L ?? Array.ConvertAll(t.F, v => (long)Math.Round(v));
                            buf = l; bytes = (int)n * 8;
                        }
                        else if (et == Ort.Int32)
                        {
                            int[] l = t.L != null ? Array.ConvertAll(t.L, v => (int)v) : Array.ConvertAll(t.F, v => (int)Math.Round(v));
                            buf = l; bytes = (int)n * 4;
                        }
                        else throw new Exception("unsupported input type " + et + " for " + InputNames[i]);
                        GCHandle g = GCHandle.Alloc(buf, GCHandleType.Pinned); pins.Add(g);
                        Ort.Check(create(Ort.memInfo, g.AddrOfPinnedObject(), (UIntPtr)(uint)bytes, t.Shape, (UIntPtr)(uint)t.Shape.Length, et, out values[i]));
                    }
                    Ort.Check(Ort.Fn<Ort.RunFn>(Ort.iRun)(session, IntPtr.Zero, inNamePtrs, values, (UIntPtr)(uint)values.Length, new IntPtr[] { outNamePtrs[outIndex] }, (UIntPtr)1u, outputs));
                    return Read(outputs[0]);
                }
                finally
                {
                    foreach (IntPtr v in values) if (v != IntPtr.Zero) rel(v);
                    if (outputs[0] != IntPtr.Zero) rel(outputs[0]);
                    foreach (GCHandle g in pins) g.Free();
                }
            }
        }

        static Tensor Read(IntPtr value)
        {
            IntPtr info;
            Ort.Check(Ort.Fn<Ort.ValueShapeFn>(Ort.iValueShape)(value, out info));
            int et; long[] dims;
            try
            {
                Ort.Check(Ort.Fn<Ort.ElemTypeFn>(Ort.iElemType)(info, out et));
                UIntPtr nd; Ort.Check(Ort.Fn<Ort.DimCountFn>(Ort.iDimCount)(info, out nd));
                dims = new long[nd.ToUInt32()];
                if (dims.Length > 0) Ort.Check(Ort.Fn<Ort.DimsFn>(Ort.iDims)(info, dims, nd));
            }
            finally { Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseShapeInfo)(info); }
            long n = 1; foreach (long d in dims) n *= d;
            IntPtr data;
            Ort.Check(Ort.Fn<Ort.GetDataFn>(Ort.iGetTensorData)(value, out data));
            float[] o = new float[n];
            if (et == Ort.Float) Marshal.Copy(data, o, 0, (int)n);
            else if (et == Ort.Float16)
            {
                short[] h = new short[n];
                Marshal.Copy(data, h, 0, (int)n);
                for (long k = 0; k < n; k++) o[k] = Ort.FromHalf((ushort)h[k]);
            }
            else throw new Exception("unsupported output type " + et);
            return new Tensor(o, dims);
        }

        public void Dispose()
        {
            lock (lk)
            {
                if (session != IntPtr.Zero) { Ort.Fn<Ort.ReleaseFn>(Ort.iReleaseSession)(session); session = IntPtr.Zero; }
                if (inNamePtrs != null) foreach (IntPtr p in inNamePtrs) Marshal.FreeHGlobal(p);
                if (outNamePtrs != null) foreach (IntPtr p in outNamePtrs) Marshal.FreeHGlobal(p);
                inNamePtrs = outNamePtrs = null;
            }
        }
    }
}

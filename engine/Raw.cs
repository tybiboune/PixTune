using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PixTune
{
    // Camera RAW files through LibRaw (https://www.libraw.org, LGPL 2.1 / CDDL), the decoder used by darktable and
    // RawTherapee: over 1,000 cameras (Sony ARW, Nikon NEF, Canon CR2/CR3, Fujifilm RAF, Olympus ORF, Panasonic
    // RW2, Pentax PEF, DNG...). libraw.dll ships in PixTune's "native" folder and is called through its C API.
    // Development: camera white balance (as shot), sRGB, AHD demosaicing, 16 bits per channel.
    // RAW files LibRaw cannot read yet (Nikon "High Efficiency" HE / HE*, cameras newer than LibRaw) through
    // Adobe's free DNG Converter when it is installed: the file is converted once to a DNG holding the same raw
    // data (kept in a cache), which LibRaw then develops like any RAW file.
    public static class DngConverter
    {
        public static string ExePath = "";      // optional override; otherwise the usual install folders
        public static string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixTune\\dng-cache");
        public static int TimeoutSeconds = 180, CacheFiles = 40;

        public static string Find()
        {
            if (!string.IsNullOrEmpty(ExePath) && File.Exists(ExePath)) return ExePath;
            foreach (Environment.SpecialFolder f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                string p = Path.Combine(Environment.GetFolderPath(f), "Adobe\\Adobe DNG Converter\\Adobe DNG Converter.exe");
                if (File.Exists(p)) return p;
            }
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\Adobe DNG Converter.exe"))
                {
                    string p = k == null ? null : k.GetValue("") as string;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p.Trim('"'))) return p.Trim('"');
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool Installed { get { return Find() != null; } }

        // The DNG for a RAW file, converting it the first time. Throws when the conversion fails.
        public static string Convert(string raw)
        {
            string exe = Find();
            if (exe == null) throw new Exception("Adobe DNG Converter is not installed");
            FileInfo fi = new FileInfo(raw);
            string key;
            using (System.Security.Cryptography.SHA1 h = System.Security.Cryptography.SHA1.Create())
                key = BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks))).Replace("-", "").Substring(0, 20);
            string name = key + ".dng", dng = Path.Combine(CacheDir, name);
            if (File.Exists(dng)) { File.SetLastAccessTimeUtc(dng, DateTime.UtcNow); return dng; }
            Directory.CreateDirectory(CacheDir);
            // -c lossless compression, -p0 small preview, -d output folder, -o output name
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe,
                "-c -p0 -d \"" + CacheDir + "\" -o \"" + name + "\" \"" + fi.FullName + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
            {
                if (!p.WaitForExit(TimeoutSeconds * 1000)) { try { p.Kill(); } catch (Exception) { } throw new Exception("Adobe DNG Converter took too long"); }
            }
            if (!File.Exists(dng)) throw new Exception("Adobe DNG Converter could not convert this file (an update of the converter may be needed for this camera)");
            Prune();
            return dng;
        }

        // keeps the most recently used conversions only
        static void Prune()
        {
            try
            {
                FileInfo[] all = new DirectoryInfo(CacheDir).GetFiles("*.dng");
                Array.Sort(all, (a, b) => b.LastAccessTimeUtc.CompareTo(a.LastAccessTimeUtc));
                for (int i = CacheFiles; i < all.Length; i++) all[i].Delete();
            }
            catch (Exception) { }
        }
    }

    // a RAW variant LibRaw recognises but cannot develop correctly (e.g. Nikon "High Efficiency" raw)
    public class RawVariantException : Exception
    {
        public RawVariantException() : base("this RAW variant cannot be developed yet (LibRaw reports data errors)") { }
    }

    public static class RawDecoder
    {
        public static string LibPath = "";      // set by the script: <PixTune>\native\libraw.dll
        public static string LastError = "", Version = "";
        public static string Notice = "";      // set when a photo had to be opened another way (shown to the user)
        public static int Demosaic = 2; // PPG: as sharp as AHD at 100 %, about 2.5x faster. LibRaw quality: 0 linear, 1 VNG, 2 PPG, 3 AHD, 4 DCB, 11 DHT, 12 AAHD

        static readonly string[] Extensions = {
            ".3fr", ".ari", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw", ".dcr", ".dcs", ".dng", ".erf", ".fff", ".gpr",
            ".iiq", ".k25", ".kdc", ".mdc", ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf", ".ori", ".pef", ".ptx", ".pxn",
            ".r3d", ".raf", ".raw", ".rw2", ".rwl", ".rwz", ".sr2", ".srf", ".srw", ".sti", ".x3f" };
        public static string FilterPattern { get { return "*" + string.Join(";*", Extensions); } }

        public static bool IsRaw(string path)
        {
            string e = Path.GetExtension(path ?? "").ToLowerInvariant();
            return Array.IndexOf(Extensions, e) >= 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr InitFn(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int OpenWFn(IntPtr lr, [MarshalAs(UnmanagedType.LPWStr)] string path);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int LrFn(IntPtr lr);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LrVoidFn(IntPtr lr);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetIntFn(IntPtr lr, int v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetMulFn(IntPtr lr, int index, float v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate float GetMulFn(IntPtr lr, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr MakeImageFn(IntPtr lr, out int err);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr StrErrFn(int code);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr VersionFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DataErrorCb(IntPtr data, IntPtr file, int offset);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetErrorHandlerFn(IntPtr lr, DataErrorCb cb, IntPtr data);

        static IntPtr mod;
        static bool tried, ok;
        static readonly object lk = new object();
        static InitFn init; static OpenWFn openW; static LrFn unpack, process; static LrVoidFn close;
        static SetIntFn outputBps, outputColor, noAutoBright, demosaic; static SetMulFn userMul; static GetMulFn camMul;
        static LrFn unpackThumb; static MakeImageFn makeThumb;
        static MakeImageFn makeImage; static FreeFn clearMem; static StrErrFn strError; static SetErrorHandlerFn setErrorHandler;
        // a format LibRaw only half knows (Nikon "High Efficiency" raw) decodes to garbage while reporting data
        // errors: those count as a failure, and the Windows codec gets the file instead
        [ThreadStatic] static bool dataError;
        static readonly DataErrorCb onDataError = (d, f, o) => { dataError = true; };

        static T F<T>(string name) where T : class
        {
            IntPtr p = GetProcAddress(mod, name);
            if (p == IntPtr.Zero) throw new Exception("libraw.dll has no " + name);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        public static bool Available
        {
            get
            {
                lock (lk)
                {
                    if (tried) return ok;
                    tried = true;
                    try
                    {
                        if (!File.Exists(LibPath)) throw new Exception("libraw.dll is missing from the native folder");
                        // LOAD_WITH_ALTERED_SEARCH_PATH: its dependencies are looked up next to it first
                        mod = LoadLibraryEx(LibPath, IntPtr.Zero, 0x8);
                        if (mod == IntPtr.Zero)
                        {
                            int code = Marshal.GetLastWin32Error();
                            throw new Exception(code == 126
                                ? "the Microsoft Visual C++ runtime (x64) is needed to open RAW files: https://aka.ms/vs/17/release/vc_redist.x64.exe"
                                : "libraw.dll could not be loaded (" + code + ")");
                        }
                        init = F<InitFn>("libraw_init"); openW = F<OpenWFn>("libraw_open_wfile");
                        unpack = F<LrFn>("libraw_unpack"); process = F<LrFn>("libraw_dcraw_process"); close = F<LrVoidFn>("libraw_close");
                        outputBps = F<SetIntFn>("libraw_set_output_bps"); outputColor = F<SetIntFn>("libraw_set_output_color");
                        noAutoBright = F<SetIntFn>("libraw_set_no_auto_bright"); demosaic = F<SetIntFn>("libraw_set_demosaic");
                        userMul = F<SetMulFn>("libraw_set_user_mul"); camMul = F<GetMulFn>("libraw_get_cam_mul");
                        makeImage = F<MakeImageFn>("libraw_dcraw_make_mem_image"); clearMem = F<FreeFn>("libraw_dcraw_clear_mem");
                        strError = F<StrErrFn>("libraw_strerror"); setErrorHandler = F<SetErrorHandlerFn>("libraw_set_dataerror_handler");
                        unpackThumb = F<LrFn>("libraw_unpack_thumb"); makeThumb = F<MakeImageFn>("libraw_dcraw_make_mem_thumb");
                        Version = Marshal.PtrToStringAnsi(F<VersionFn>("libraw_version")());
                        ok = true;
                    }
                    catch (Exception ex) { LastError = ex.Message; ok = false; }
                    return ok;
                }
            }
        }

        // Develops a RAW file to a 16-bit-precision picture (values 0..1), already turned the way the camera was held.
        public static Img Decode(string path)
        {
            if (!Available) throw new Exception(LastError);
            IntPtr lr = init(0);
            if (lr == IntPtr.Zero) throw new Exception("LibRaw could not start");
            IntPtr mem = IntPtr.Zero;
            dataError = false;
            setErrorHandler(lr, onDataError, IntPtr.Zero);
            try
            {
                Check(openW(lr, path));
                Check(unpack(lr));
                if (dataError) throw new RawVariantException();
                // white balance as shot: the camera's multipliers (when the file has them)
                if (camMul(lr, 0) > 0 && camMul(lr, 1) > 0)
                    for (int i = 0; i < 4; i++) userMul(lr, i, camMul(lr, i));
                outputBps(lr, 16);
                outputColor(lr, 1);     // sRGB
                noAutoBright(lr, 0);    // LibRaw's usual exposure normalisation, like other RAW converters
                demosaic(lr, Demosaic);
                Check(process(lr));
                if (dataError) throw new RawVariantException();
                int err;
                mem = makeImage(lr, out err);
                Check(err);
                if (mem == IntPtr.Zero) throw new Exception("LibRaw returned no picture");
                // libraw_processed_image_t: int type; ushort height, width, colors, bits; uint data_size; data[]
                int h = (ushort)Marshal.ReadInt16(mem, 4), w = (ushort)Marshal.ReadInt16(mem, 6);
                int colors = Marshal.ReadInt16(mem, 8), bits = Marshal.ReadInt16(mem, 10);
                int n = w * h * colors;
                Img im = new Img(w, h);
                if (bits == 16)
                {
                    short[] px = new short[n];
                    Marshal.Copy(mem + 16, px, 0, n);
                    Ops.For(0, h, y =>
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int s = (y * w + x) * colors, d = (y * w + x) * 3;
                            for (int c = 0; c < 3; c++) im.D[d + c] = (ushort)px[s + Math.Min(c, colors - 1)] / 65535f;
                        }
                    });
                }
                else
                {
                    byte[] px = new byte[n];
                    Marshal.Copy(mem + 16, px, 0, n);
                    for (int i = 0; i < w * h; i++)
                        for (int c = 0; c < 3; c++) im.D[i * 3 + c] = px[i * colors + Math.Min(c, colors - 1)] / 255f;
                }
                return im;
            }
            finally
            {
                if (mem != IntPtr.Zero) clearMem(mem);
                close(lr);
            }
        }

        // The JPEG the camera stores inside the RAW file (full size on most recent bodies), for RAW variants
        // LibRaw cannot develop. Returns null when there is none, or only a small one.
        public static Img EmbeddedPreview(string path)
        {
            if (!Available) return null;
            IntPtr lr = init(0);
            if (lr == IntPtr.Zero) return null;
            IntPtr mem = IntPtr.Zero;
            try
            {
                if (openW(lr, path) != 0 || unpackThumb(lr) != 0) return null;
                int err;
                mem = makeThumb(lr, out err);
                if (err != 0 || mem == IntPtr.Zero) return null;
                int type = Marshal.ReadInt32(mem, 0), size = Marshal.ReadInt32(mem, 12);
                if (type != 1 || size <= 0) return null;    // 1: JPEG data
                byte[] jpg = new byte[size];
                Marshal.Copy(mem + 16, jpg, 0, size);
                using (MemoryStream ms = new MemoryStream(jpg))
                using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(ms))
                    return b.Width >= 1600 || b.Height >= 1600 ? Img.FromBitmap(b) : null;
            }
            catch (Exception) { return null; }
            finally
            {
                if (mem != IntPtr.Zero) clearMem(mem);
                close(lr);
            }
        }

        // How the camera was held, from the RAW file's TIFF header (tag 0x0112 of the first IFD; NEF, ARW, CR2, DNG,
        // ORF, RW2, PEF... are TIFF containers): the EXIF orientation 1..8, or 0 when unknown. The JPEGs a camera
        // stores in a RAW file are not turned: this says how to turn them.
        public static int TiffOrientation(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = new byte[8];
                    if (fs.Read(h, 0, 8) < 8) return 0;
                    bool le = h[0] == 'I' && h[1] == 'I', be = h[0] == 'M' && h[1] == 'M';
                    if (!le && !be) return 0;
                    long ifd = (long)U32(h, 4, le);
                    if (ifd < 8 || ifd >= fs.Length) return 0;
                    fs.Seek(ifd, SeekOrigin.Begin);
                    byte[] c = new byte[2];
                    if (fs.Read(c, 0, 2) < 2) return 0;
                    int n = U16(c, 0, le);
                    byte[] e = new byte[12];
                    for (int i = 0; i < n && i < 512; i++)
                    {
                        if (fs.Read(e, 0, 12) < 12) return 0;
                        if (U16(e, 0, le) == 0x0112)
                        {
                            int v = U16(e, 8, le);
                            return v >= 1 && v <= 8 ? v : 0;
                        }
                    }
                }
            }
            catch (Exception) { }
            return 0;
        }
        static int U16(byte[] b, int o, bool le) { return le ? b[o] | (b[o + 1] << 8) : (b[o] << 8) | b[o + 1]; }
        static uint U32(byte[] b, int o, bool le)
        {
            return le ? (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)) : (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        }

        // A camera JPEG taken from a RAW file, turned the way the camera was held (unless it already is).
        public static Img TurnLikeRaw(Img jpg, string path)
        {
            if (jpg == null) return null;
            int o = TiffOrientation(path);
            if (o >= 5 && jpg.H > jpg.W) return jpg;    // a preview the camera already stored upright
            return Geo.Orient(jpg, o);
        }

        // Last resort for files no decoder knows yet (cameras newer than LibRaw): RAW files are TIFF-like
        // containers that also hold the camera's JPEGs, so the largest JPEG found in the file is used.
        public static Img ScanForJpeg(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            System.Drawing.Bitmap best = null;
            try
            {
                for (int i = 0; i + 3 < b.Length; i++)
                {
                    if (b[i] != 0xFF || b[i + 1] != 0xD8 || b[i + 2] != 0xFF) continue;
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(b, i, b.Length - i))
                        using (System.Drawing.Bitmap c = new System.Drawing.Bitmap(ms))
                        {
                            if (best != null && c.Width * c.Height <= best.Width * best.Height) continue;
                            System.Drawing.Bitmap copy = new System.Drawing.Bitmap(c); // detached from the stream
                            if (best != null) best.Dispose();
                            best = copy;
                        }
                    }
                    catch (Exception) { }
                }
                return best != null && Math.Max(best.Width, best.Height) >= 1600 ? Img.FromBitmap(best) : null;
            }
            finally { if (best != null) best.Dispose(); }
        }

        static void Check(int code)
        {
            if (code != 0) throw new Exception("LibRaw: " + Marshal.PtrToStringAnsi(strError(code)));
        }
    }
}

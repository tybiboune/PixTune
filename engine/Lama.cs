using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PixTune
{
    // One-time download of the AI files, each checked against a pinned SHA-256 before it is kept.
    // Files come in packs (one per feature); the ONNX Runtime engine they all need comes with any of them.
    public class AiSetup
    {
        // one download: a plain file saved as Target, or a zip whose Entries are extracted
        class Item
        {
            public string Url, Sha, Stage, Target; public long Bytes; public string[][] Entries; // entry, target, sha
            public bool Installed(string dir)
            {
                if (Entries == null) return File.Exists(Path.Combine(dir, Target));
                foreach (string[] e in Entries) if (!File.Exists(Path.Combine(dir, e[1]))) return false;
                return true;
            }
        }
        static Item File1(string url, string target, string sha, long bytes, string stage) { return new Item { Url = url, Target = target, Sha = sha, Bytes = bytes, Stage = stage }; }
        static Item Zip(string url, string sha, long bytes, string stage, params string[][] entries) { return new Item { Url = url, Sha = sha, Bytes = bytes, Stage = stage, Entries = entries }; }
        const string Qai = "https://qaihub-public-assets.s3.us-west-2.amazonaws.com/qai-hub-models/models/";
        const string LpBase = "https://huggingface.co/warmshao/FasterLivePortrait/resolve/eb937f4bec7186598df2d1f68e1ddbb488ae1de5/liveportrait_onnx/";
        const string SdBase ="https://huggingface.co/RanaLLC/stable-diffusion-v1-5-inpainting-onnx-fp16/resolve/a612b433500340de363966b9305a204e734822f6/";

        // ONNX Runtime with DirectML 1.24.4 (nuget.org, Microsoft-signed DLL)
        static readonly Item Runtime = Zip("https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime.directml/1.24.4/microsoft.ml.onnxruntime.directml.1.24.4.nupkg",
            "57E9F11B73437BEF7A309496135D4C1F96B1A8E9DDBA60013FA27BFC1D788681", 12458649, "AI runtime",
            new[] { "runtimes/win-x64/native/onnxruntime.dll", "onnxruntime.dll", "E7EEDEC6A6F26DC39DC948276A75EF6D2BEE3FFF944D874CEED0BBD3B97BFF40" });

        static readonly System.Collections.Generic.Dictionary<string, Item[]> Packs = new System.Collections.Generic.Dictionary<string, Item[]>
        {
            // LaMa (Carve/LaMa-ONNX, Apache-2.0) for healing and Expand's AI fill
            { "lama", new[] { File1("https://huggingface.co/Carve/LaMa-ONNX/resolve/main/lama_fp32.onnx", "lama_fp32.onnx", "1FAEF5301D78DB7DDA502FE59966957EC4B79DD64E16F03ED96913C7A4EB68D6", 208044816, "AI model") } },
            // Stable Diffusion 1.5 inpainting (CreativeML OpenRAIL-M), ONNX fp16 export pinned to one revision, and
            // Real-ESRGAN x4plus for High detail (BSD-3-Clause, Qualcomm AI Hub export)
            { "generative", new[] {
                File1(SdBase + "tokenizer/vocab.json", "sd15-inpaint\\tokenizer\\vocab.json", "E089AD92BA36837A0D31433E555C8F45FE601AB5C221D4F607DED32D9F7A4349", 1059962, "generative fill (1/7)"),
                File1(SdBase + "tokenizer/merges.txt", "sd15-inpaint\\tokenizer\\merges.txt", "9FD691F7C8039210E0FCED15865466C65820D09B63988B0174BFE25DE299051A", 524619, "generative fill (2/7)"),
                File1(SdBase + "vae_encoder/model.onnx", "sd15-inpaint\\vae_encoder\\model.onnx", "F0DA9070D007DEF0D6A4E7C10A21462BB6172E460EF2587C3FE91191397B4CEA", 68430178, "generative fill (3/7)"),
                File1(SdBase + "vae_decoder/model.onnx", "sd15-inpaint\\vae_decoder\\model.onnx", "CE0FFCA3FCFD0A2729A88D8D90DABF35C66D237404997E0461D52E46AC7A91BC", 99093889, "generative fill (4/7)"),
                File1(SdBase + "text_encoder/model.onnx", "sd15-inpaint\\text_encoder\\model.onnx", "8AF516FB184866F8CAED192CA3BF0636EF9D40D4F7799C05600CAE431D18D8D1", 246368629, "generative fill (5/7)"),
                File1(SdBase + "unet/model.onnx", "sd15-inpaint\\unet\\model.onnx", "A3726E1944EC2FBC9596096CD001520AEA789367F329360EE34EB520C751DE16", 1720173976, "generative fill (6/7)"),
                Zip(Qai + "real_esrgan_x4plus/releases/v0.65.0/real_esrgan_x4plus-onnx-float.zip", "05AA31323C6A7E831493171CAC970E398697709CAE7E7AE8E0760E6DA4D64CA4", 62174027, "detail upscaler (7/7)",
                    new[] { "real_esrgan_x4plus-onnx-float/real_esrgan_x4plus.onnx", "esrgan\\real_esrgan_x4plus.onnx", "0C40D89889E621D4F1A9A0A6D728E4CCD612A67A90927CA8B394EB0917329ED9" },
                    new[] { "real_esrgan_x4plus-onnx-float/real_esrgan_x4plus.data", "esrgan\\real_esrgan_x4plus.data", "28FADA125730D3C87D504D48DD8332837F65811EC431A570EA4919BA3EEE3287" }) } },
            // one-tap masks: BiRefNet-lite (MIT, onnx-community export pinned to one revision) and the semantic
            // part of Detectron2 Panoptic FPN (Apache-2.0, Qualcomm AI Hub export)
            { "masks", new[] {
                File1("https://huggingface.co/onnx-community/BiRefNet_lite-ONNX/resolve/de15b22ba131738a16dff04aab8bdf8dc32e3ac1/onnx/model_fp16.onnx", "masks\\birefnet_lite_fp16.onnx", "D39B897CEB16AE654C1731F3DBA0CF9B368D9CAE74B5A57459B455CC8BFEC402", 114538221, "subject model (1/2)"),
                Zip(Qai + "detectron2_panoptic_seg/releases/v0.65.0/detectron2_panoptic_seg-onnx-float.zip", "B821109135955F6D009173D83C9C84312A0B0807650A3AB15B5177C9C873E79E", 170935479, "sky model (2/2)",
                    new[] { "detectron2_panoptic_seg-onnx-float/proposal_generator_onnx/proposal_generator.onnx", "masks\\detectron2_semantic.onnx", "7FADFB9D5456DF5D9496B8A9F7720D03AD95E44D59D96F669FEC1ADA62475260" },
                    new[] { "detectron2_panoptic_seg-onnx-float/proposal_generator_onnx/proposal_generator.data", "masks\\proposal_generator.data", "B1BB3923E7B5A4369C548465F07F9284796FB39A328D0FC766B31DAD49379B0A" }) } },
            // AI denoise: NAFNet trained on real camera noise (SIDD, MIT licence; Qualcomm AI Hub export)
            { "denoise", new[] {
                Zip(Qai + "nafnet_denoise/releases/v0.65.0/nafnet_denoise-onnx-float.zip", "D4278669AF02A04320389DBD8FD3D1A24C06F887701A2735D2B311FDFF27AC62", 430700768, "AI denoiser",
                    new[] { "nafnet_denoise-onnx-float/nafnet_denoise.onnx", "denoise\\nafnet_denoise.onnx", "96E6277A3587BCE54F94867430A8DA4B4AD2F28F540C3A237D76795B35C84212" },
                    new[] { "nafnet_denoise-onnx-float/nafnet_denoise.data", "denoise\\nafnet_denoise.data", "A41F74B64CEF10D9F92F9D657C9B2AFB3AF759799E488AE91DA5480DF6193539" }) } },
            // "Pick one" masks: MobileSAM (Apache-2.0, Qualcomm AI Hub export)
            { "pick", new[] {
                Zip(Qai + "mobilesam/releases/v0.66.0/mobilesam-onnx-float.zip", "396F8F66FB8FE3A6C88E8A1FE87C281FE7CD9BBB48CE710D826B032921C66C36", 40552355, "subject picker",
                    new[] { "mobilesam-onnx-float/encoder.onnx", "pick\\encoder.onnx", "468011D04547A04E1C45A3447D2A1ADC17A54D8E63B6BCC1CDF22A56E8E69923" },
                    new[] { "mobilesam-onnx-float/encoder.data", "pick\\encoder.data", "57C0F73C068F8C1F6845AB2965E4BDD8458C313B45F85FCCA76B29F0E4644C95" },
                    new[] { "mobilesam-onnx-float/decoder.onnx", "pick\\decoder.onnx", "140021AAADC4BD2512BC84D9DDE5E3438F3387F5B999974A545A368CC296ECA4" },
                    new[] { "mobilesam-onnx-float/decoder.data", "pick\\decoder.data", "A19306330820B9F05398ECDCE0EADDEED4777A12E3C5FDE67F8728CB450CDF2C" }) } },
            // Lens Blur's depth mode: Depth Anything V2 Small (Apache-2.0, onnx-community export pinned to one
            // revision). The small one on purpose, not for its size: the bigger families hand back the subject
            // as a near-flat cut-out, which turns hair into a hard silhouette, where this one keeps the strands
            // and a real gradient through the picture. engine\Depth.cs says what was compared.
            { "depth", new[] {
                File1("https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/4472b7362082ad9968fee890ca0f1e5aca36b93d/onnx/model_fp16.onnx",
                    "depth\\depth_anything_v2_small_fp16.onnx", "2DF6223F206B5164E21F664ACE61DABEB9BB6A49B8B5A3E00510B4807D0F5B04", 49642442, "depth model") } },
            // Head Pose AI mode: LivePortrait (MIT, ONNX export from warmshao/FasterLivePortrait pinned to one revision)
            { "headpose", new[] {
                File1(LpBase + "appearance_feature_extractor.onnx", "liveportrait\\appearance_feature_extractor.onnx", "D070AFCCCA7F528FFB0EF5052B21588B42225A996661833F7BDEDE562D1AB921", 3355896, "face model (1/4)"),
                File1(LpBase + "motion_extractor.onnx", "liveportrait\\motion_extractor.onnx", "219A46174297B2B411BB3C5DCE48D3A8C8E07A9D82120A0DA21F49F58B67FCA6", 112648514, "face model (2/4)"),
                File1(LpBase + "stitching.onnx", "liveportrait\\stitching.onnx", "8E33658425E3F1014DC35B28F60CA7459189E8F78F20540262A2FE5AC1DC6EC7", 182363, "face model (3/4)"),
                File1(LpBase + "warping_spade.onnx", "liveportrait\\warping_spade.onnx", "B0E7A566DB8FBA690C23523BCD2FAA4F0D13F05418DB84A779397A851062AD69", 421233096, "face model (4/4)") } },
        };

        // bytes still to download for a pack (0 when everything is installed)
        public static long MissingBytes(string dir, string pack)
        {
            long n = Runtime.Installed(dir) ? 0 : Runtime.Bytes;
            foreach (Item it in Packs[pack]) if (!it.Installed(dir)) n += it.Bytes;
            return n;
        }

        public volatile bool Cancel;
        public event EventHandler<AiSetupEventArgs> Progress; // raised on the UI thread

        public void Start(System.Windows.Forms.Control ui, string dir, string pack)
        {
            Item[] items = Packs[pack];
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                string err = null;
                try
                {
                    Directory.CreateDirectory(dir);
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                    string dll = Path.Combine(dir, "onnxruntime.dll");
                    if (!File.Exists(dll) || Sha(dll) != Runtime.Entries[0][2]) Fetch(ui, dir, Runtime);
                    foreach (Item it in items) if (!it.Installed(dir)) Fetch(ui, dir, it);
                }
                catch (Exception ex) { err = Cancel ? "cancelled" : ex.Message; }
                Raise(ui, new AiSetupEventArgs("done", 0, 0, true, err));
            });
        }

        // downloads one item, checks it, and keeps it (or the files extracted from it) only if it is the expected one
        void Fetch(System.Windows.Forms.Control ui, string dir, Item it)
        {
            if (it.Entries == null)
            {
                string path = Path.Combine(dir, it.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Download(ui, it.Url, path + ".part", it.Stage);
                if (Sha(path + ".part") != it.Sha) { File.Delete(path + ".part"); throw new Exception(Path.GetFileName(it.Target) + " is not the expected file"); }
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".part", path);
                return;
            }
            string zip = Path.Combine(dir, "download.zip.part");
            Download(ui, it.Url, zip, it.Stage);
            try
            {
                if (Sha(zip) != it.Sha) throw new Exception("the " + it.Stage + " download is not the expected file");
                using (System.IO.Compression.ZipArchive z = System.IO.Compression.ZipFile.OpenRead(zip))
                    foreach (string[] e in it.Entries)
                    {
                        string path = Path.Combine(dir, e[1]);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        System.IO.Compression.ZipArchiveEntry ze = z.GetEntry(e[0]);
                        if (ze == null) throw new Exception(Path.GetFileName(e[1]) + " missing from the package");
                        using (Stream s = ze.Open()) using (FileStream o = File.Create(path + ".part")) s.CopyTo(o);
                        if (Sha(path + ".part") != e[2]) { File.Delete(path + ".part"); throw new Exception(Path.GetFileName(e[1]) + " is not the expected file"); }
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(path + ".part", path);
                    }
            }
            finally { if (File.Exists(zip)) File.Delete(zip); }
        }
        void Download(System.Windows.Forms.Control ui, string url, string path, string stage)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "PixTune";
            req.AllowAutoRedirect = true;
            using (System.Net.WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream o = File.Create(path))
            {
                long total = resp.ContentLength, done = 0;
                byte[] buf = new byte[1 << 16];
                DateTime last = DateTime.MinValue;
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (Cancel) throw new Exception("cancelled");
                    o.Write(buf, 0, n); done += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 300) { last = DateTime.Now; Raise(ui, new AiSetupEventArgs(stage, done, total, false, null)); }
                }
            }
        }

        static string Sha(string path)
        {
            using (System.Security.Cryptography.SHA256 h = System.Security.Cryptography.SHA256.Create())
            using (FileStream f = File.OpenRead(path))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "");
        }

        void Raise(System.Windows.Forms.Control ui, AiSetupEventArgs a)
        {
            try { ui.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate { EventHandler<AiSetupEventArgs> h = Progress; if (h != null) h(this, a); }); }
            catch { }
        }
    }

    public class AiSetupEventArgs : EventArgs
    {
        public string Stage, Error; public long Done, Total; public bool Finished;
        public AiSetupEventArgs(string stage, long done, long total, bool finished, string error) { Stage = stage; Done = done; Total = total; Finished = finished; Error = error; }
    }

    // Optional AI inpainting with LaMa (Suvorov et al., "Resolution-robust Large Mask Inpainting with
    // Fourier Convolutions", Apache-2.0; ONNX export "Carve/LaMa-ONNX", 512x512). Runs through
    // ONNX Runtime (MIT, Microsoft) loaded from PixTune's AI folder and called through its C API,
    // on the GPU with DirectML when possible, otherwise on the CPU.
    public static class Lama
    {
        public const int Size = 512;
        public static string Dir = "";        // folder holding onnxruntime.dll and lama_fp32.onnx
        public static string Device = "";     // "GPU (DirectML)" or "CPU" once loaded
        public static string LastError = "";
        // DirectML (ONNX Runtime 1.24) rejected a MatMul in LaMa's Fourier-convolution layers at run time: the
        // GPU check (GpuCheck) finds out on each computer and keeps the model on the CPU when the GPU cannot run it.
        public static int OptLevel = 1;       // graph optimisation: basic loads twice as fast as "all", runs as fast

        // Loads the model in the background so the first stroke does not wait for it.
        public static void WarmUp()
        {
            if (!tried && Installed) System.Threading.Tasks.Task.Factory.StartNew(EnsureLoaded);
        }
        static volatile bool tried, ok;
        static readonly object lk = new object();
        static OrtSession session;
        static int imageInput;

        public static string RuntimePath { get { return Path.Combine(Dir, "onnxruntime.dll"); } }
        public static string ModelPath { get { return Path.Combine(Dir, "lama_fp32.onnx"); } }
        public static bool Installed { get { return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(ModelPath); } }
        public static bool Available { get { EnsureLoaded(); return ok; } }

        // Forget a failed load (e.g. after installing the files).
        public static void Reset() { lock (lk) { if (!ok) tried = false; } }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (lk)
            {
                if (tried) return;
                // "tried" is set only once loading is over: a stroke rendered while the model is still loading in
                // the background waits for it here, instead of seeing "tried, not ready" and silently using the
                // content-aware fill
                try { ok = Load(); }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        static bool Load()
        {
            if (!Installed) { LastError = "AI files are not installed"; return false; }
            Ort.Init(RuntimePath);
            session = OrtSession.Open(ModelPath, true, OptLevel);
            if (session.InputNames.Length != 2) { LastError = "unexpected model inputs"; return false; }
            imageInput = session.InputNames[0].IndexOf("mask", StringComparison.OrdinalIgnoreCase) < 0 ? 0 : 1;
            Device = session.Device;
            LastError = "";
            return true;
        }

        // Inpaints a 512x512 image (values 0..1) where mask = 1. Returns null if the model is unavailable.
        public static Img Run(Img im, float[] mask)
        {
            if (im.W != Size || im.H != Size) throw new ArgumentException("LaMa works on 512x512 images");
            if (!Available) return null;
            // a GPU that fails here hands the model to the CPU (OrtSession) and is not tried again
            Img r = RunOnce(im, mask);
            Device = session.Device;
            return r;
        }

        static Img RunOnce(Img im, float[] mask)
        {
            int n = Size * Size;
            float[] img = new float[3 * n], msk = new float[n];
            for (int i = 0; i < n; i++)
            {
                img[i] = im.D[i * 3]; img[n + i] = im.D[i * 3 + 1]; img[2 * n + i] = im.D[i * 3 + 2];
                msk[i] = mask[i] > 0.5f ? 1f : 0f;
            }
            Tensor[] inputs = new Tensor[2];
            inputs[imageInput] = new Tensor(img, 1, 3, Size, Size);
            inputs[1 - imageInput] = new Tensor(msk, 1, 1, Size, Size);
            float[] o = session.Run(inputs).F;
            // the output is in 0..255; values outside are clipped by the caller
            Img r = new Img(Size, Size);
            for (int i = 0; i < n; i++) { r.D[i * 3] = o[i] / 255f; r.D[i * 3 + 1] = o[n + i] / 255f; r.D[i * 3 + 2] = o[2 * n + i] / 255f; }
            return r;
        }
    }
}

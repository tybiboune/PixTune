using System;
using System.Collections.Generic;
using System.IO;

namespace PixTune
{
    // AI denoise for RAW Develop: NAFNet (Chen et al., "Simple Baselines for Image Restoration", MIT licence)
    // trained on SIDD, real smartphone and camera sensor noise; ONNX export by Qualcomm AI Hub, 256x256 tiles.
    // Runs on the GPU through DirectML when possible. The denoised picture is cached, so changing the
    // strength (a blend with the original) is instant.
    public static class Denoiser
    {
        public const int Tile = 256;
        // How much neighbouring tiles share. The model's own cost is what this tool spends (about 90 ms a tile on a
        // discrete card, eight times that on a processor), so the only way to make it quicker is to run fewer tiles.
        public static int Overlap = 16;
        // false marches by a fixed step and pushes the last tile back inside (the way this used to work)
        public static bool EvenTiles = true;
        // the level a dark tile is lifted to before the model sees it, and the most it may be lifted
        // (Comfort 0 leaves every tile as it is, the way this used to work)
        public static float Comfort = 0.9f, MaxLift = 8f;
        // what the model may return before its answer is taken as nonsense (a clean tile stays under 1.05),
        // and how many times a tile is shown again at half the level before it is given up on
        public static float Sane = 1.15f;
        public static int Retries = 3;
        public static int Refused;      // tiles the model never settled on, kept as they came in
        public static int Quieted;      // times a tile had to be shown again at a lower level
        // A card short of memory does not refuse the work: it returns numbers, and they are nonsense. Another
        // program using the card, or one of the bigger models loaded beside this one, is enough, and a whole
        // photo would come back untouched with nothing said. So a tile the card gives up on is shown to the
        // same model on the processor, and if the processor settles where the card did not, the card is not
        // asked again for the rest of this picture: it is about eight times slower a tile, but it answers.
        // (false leaves a tile the card gives up on as it came in, the way this used to work)
        public static bool CpuFallback = true;
        public static int OnCpu;        // tiles the processor had to take over
        // leaves the card out from the first tile, which is where a run ends up anyway once it gives up on one:
        // the way the test gets at the processor without a card that misbehaves
        public static bool SkipCard;
        public static string Dir = "", RuntimePath = "", Device = "", LastError = "";
        public static volatile float Progress;
        static volatile bool tried, ok;
        static readonly object loadLk = new object(), runLk = new object();
        static OrtSession session;
        static OrtSession cpuSession;
        static bool cpuTried;

        // The same model on the processor, opened only if the card ever gives up on a tile: its weights are
        // 460 MB, and nearly every picture goes through without it. Null when it cannot be opened either.
        static OrtSession CpuModel()
        {
            if (cpuTried) return cpuSession;
            cpuTried = true;
            try { cpuSession = OrtSession.Open(ModelPath, false, 1); }
            catch (Exception ex) { LastError = ex.Message; cpuSession = null; }
            return cpuSession;
        }

        public static string ModelPath { get { return Path.Combine(Dir, "nafnet_denoise.onnx"); } }
        public static bool Installed
        {
            get { return Dir.Length > 0 && File.Exists(RuntimePath) && File.Exists(ModelPath) && File.Exists(Path.Combine(Dir, "nafnet_denoise.data")); }
        }
        public static bool Available { get { EnsureLoaded(); return ok; } }
        public static void Reset() { lock (loadLk) { if (!ok) tried = false; } }
        public static void WarmUp() { if (!tried && Installed) System.Threading.Tasks.Task.Factory.StartNew(EnsureLoaded); }

        static void EnsureLoaded()
        {
            if (tried) return;
            lock (loadLk)
            {
                if (tried) return;
                try
                {
                    if (!Installed) throw new Exception("the AI denoiser is not installed");
                    Ort.Init(RuntimePath);
                    session = OrtSession.Open(ModelPath, true, 1);
                    Device = session.Device; LastError = ""; ok = true;
                }
                catch (Exception ex) { LastError = ex.Message; ok = false; }
                finally { tried = true; }
            }
        }

        // last results, keyed by the input picture object (the preview cache hands the same object back
        // while the edits before RAW Develop do not change) and its size
        static readonly List<KeyValuePair<Img, Img>> cache = new List<KeyValuePair<Img, Img>>();
        public static void Forget() { lock (cache) cache.Clear(); }

        // The denoised picture, or null when the model is not available.
        public static Img Denoise(Img im)
        {
            lock (cache) foreach (KeyValuePair<Img, Img> kv in cache) if (kv.Key == im) return kv.Value;
            if (!Available) return null;
            Img o;
            lock (runLk)
            {
                try { o = Run(im); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { LastError = ex.Message; return null; }
                finally { Progress = 0; }
            }
            lock (cache)
            {
                cache.Add(new KeyValuePair<Img, Img>(im, o));
                if (cache.Count > 2) cache.RemoveAt(0);
            }
            return o;
        }

        // overlapping tiles, each blended in with weights that fade over the overlap
        static Img Run(Img im)
        {
            int W = im.W, H = im.H, step = Tile - 2 * Overlap, n = Tile * Tile;
            float[] acc = new float[W * H * 3], wsum = new float[W * H];
            float[] buf = new float[3 * n];
            // The fewest tiles that cover the picture while neighbours still share at least the overlap, spread
            // evenly. Marching by a fixed step and pushing the last tile back in instead left it nearly on top of
            // its neighbour, so part of the picture went through the model twice for nothing.
            int nx = W <= Tile ? 1 : (int)Math.Ceiling((W - Tile) / (double)step) + 1;
            int ny = H <= Tile ? 1 : (int)Math.Ceiling((H - Tile) / (double)step) + 1;
            int done = 0;
            Refused = 0; Quieted = 0; OnCpu = 0;
            bool onCpu = false;   // the card gave up where the processor settled: the rest of this picture is its work
            for (int ty = 0; ty < ny; ty++)
                for (int tx = 0; tx < nx; tx++)
                {
                    Pipeline.CheckCancel();
                    int x0, y0;
                    if (EvenTiles)
                    {
                        x0 = nx == 1 ? 0 : (int)Math.Round(tx * (W - Tile) / (double)(nx - 1));
                        y0 = ny == 1 ? 0 : (int)Math.Round(ty * (H - Tile) / (double)(ny - 1));
                        if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
                    }
                    else { x0 = Math.Max(0, Math.Min(tx * step, W - Tile)); y0 = Math.Max(0, Math.Min(ty * step, H - Tile)); }
                    for (int y = 0; y < Tile; y++)
                        for (int x = 0; x < Tile; x++)
                        {
                            int sx = Math.Min(W - 1, x0 + x), sy = Math.Min(H - 1, y0 + y), si = (sy * W + sx) * 3, i = y * Tile + x;
                            buf[i] = im.D[si]; buf[n + i] = im.D[si + 1]; buf[2 * n + i] = im.D[si + 2];
                        }
                    // The model's numbers run away when what it is shown sits outside what it was trained on,
                    // and it then returns values many times brighter than white, which the clamp at the end
                    // turns into coloured mush. It happens at both ends: on a dark, flat tile, and on a tile
                    // noisier than any it was trained for. Both are cured by changing the tile's level before
                    // the model sees it and putting it back after. A dark tile is lifted to a comfortable
                    // level; a tile whose answer still comes back impossible is shown again, quieter, until
                    // the model settles. Scaling never clips, so it is undone exactly, and a tile the model
                    // handles the first time (nearly all of them) costs nothing.
                    float tmax = 0;
                    for (int i = 0; i < buf.Length; i++) if (buf[i] > tmax) tmax = buf[i];
                    float k = 1f;
                    if (tmax > 1e-4f && tmax < Comfort) k = Math.Min(MaxLift, Comfort / tmax);
                    if (k != 1f) for (int i = 0; i < buf.Length; i++) buf[i] *= k;
                    float[] o = OneTile(buf, ref k, ref onCpu);
                    // neither the card nor the processor settled: keep this tile as it came in, not nonsense
                    if (o == null) { o = (float[])buf.Clone(); Refused++; }
                    if (k != 1f) { float ik = 1f / k; for (int i = 0; i < o.Length; i++) o[i] *= ik; }
                    for (int y = 0; y < Tile && y0 + y < H; y++)
                    {
                        float wy = Ramp(y, y0 == 0, y0 + Tile >= H);
                        for (int x = 0; x < Tile && x0 + x < W; x++)
                        {
                            float w = wy * Ramp(x, x0 == 0, x0 + Tile >= W);
                            int gi = (y0 + y) * W + x0 + x, i = y * Tile + x;
                            acc[gi * 3] += w * o[i]; acc[gi * 3 + 1] += w * o[n + i]; acc[gi * 3 + 2] += w * o[2 * n + i];
                            wsum[gi] += w;
                        }
                    }
                    Progress = ++done / (float)(nx * ny);
                }
            Img r = new Img(W, H);
            for (int i = 0; i < W * H; i++)
            {
                float w = Math.Max(1e-6f, wsum[i]);
                for (int c = 0; c < 3; c++) r.D[i * 3 + c] = Math.Max(0f, Math.Min(1f, acc[i * 3 + c] / w));
            }
            return r;
        }

        // One tile: the card, and the processor when the card gives up on it. onCpu says the card has already
        // given up once on this picture, and is set when the processor settles where the card did not, so the
        // rest of the picture goes straight to the processor instead of being tried four times on the card.
        static float[] OneTile(float[] buf, ref float k, ref bool onCpu)
        {
            if (session.Device == "CPU") return Settle(session, buf, ref k);   // no card to fall back from
            float k0 = k;
            if (!onCpu && !SkipCard)
            {
                float[] o = Settle(session, buf, ref k);
                if (o != null || !CpuFallback) return o;
                // back to the level the card first saw, so the processor starts where the card did
                if (k != k0) { float back = k0 / k; for (int i = 0; i < buf.Length; i++) buf[i] *= back; k = k0; }
            }
            else if (!CpuFallback) return null;
            OrtSession cpu = CpuModel();
            if (cpu == null) return null;
            float[] c = Settle(cpu, buf, ref k);
            if (c != null) { OnCpu++; onCpu = true; }
            return c;
        }

        // Shows the tile to the model, half as bright each time the answer comes back impossible, and returns
        // the first answer it settles on, or null. buf and k are left where the last try put them.
        static float[] Settle(OrtSession s, float[] buf, ref float k)
        {
            for (int attempt = 0; attempt <= Retries; attempt++)
            {
                if (attempt > 0) { for (int i = 0; i < buf.Length; i++) buf[i] *= 0.5f; k *= 0.5f; Quieted++; }
                float[] o = s.Run(new Tensor(buf, 1, 3, Tile, Tile)).F;
                bool mad = false;
                for (int i = 0; i < o.Length; i++) if (!(o[i] > -Sane && o[i] < Sane)) { mad = true; break; }
                if (!mad) return o;
            }
            return null;
        }

        static float Ramp(int p, bool atStart, bool atEnd)
        {
            float w = 1f;
            if (!atStart) w = Math.Min(w, (p + 1f) / (2f * Overlap));
            if (!atEnd) w = Math.Min(w, (Tile - p) / (2f * Overlap));
            return Math.Max(0.001f, Math.Min(1f, w));
        }
    }
}

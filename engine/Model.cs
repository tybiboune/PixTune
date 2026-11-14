using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PixTune
{
    // One entry of the edit stack: a tool with its parameters, strokes and an optional stack-brush mask.
    public class Step
    {
        public string Tool = "";
        public Dictionary<string, double> P = new Dictionary<string, double>();
        public Dictionary<string, string> S = new Dictionary<string, string>();
        public List<Stroke> Strokes = new List<Stroke>();
        public List<Stroke> Mask = new List<Stroke>();
        public bool MaskOn;
        public bool MaskInvert;
        // switched off with the eye of the edit stack: kept, but skipped by every render (preview, export, batch)
        public bool Hidden;

        public Step() { }
        public Step(string tool) { Tool = tool; }

        public double Get(string k, double def) { double v; return P.TryGetValue(k, out v) ? v : def; }
        public float F(string k) { return (float)Get(k, 0); }
        public float F(string k, double def) { return (float)Get(k, def); }
        public int I(string k, int def) { return (int)Math.Round(Get(k, def)); }
        public string Str(string k, string def) { string v; return S.TryGetValue(k, out v) ? v : def; }
        public void Set(string k, double v) { P[k] = v; }
        public void SetS(string k, string v) { S[k] = v; }

        public Step Clone()
        {
            Step s = new Step(Tool);
            s.P = new Dictionary<string, double>(P);
            s.S = new Dictionary<string, string>(S);
            foreach (Stroke k in Strokes) s.Strokes.Add(k.Clone());
            foreach (Stroke k in Mask) s.Mask.Add(k.Clone());
            s.MaskOn = MaskOn; s.MaskInvert = MaskInvert; s.Hidden = Hidden;
            return s;
        }

        static object StrokesToList(List<Stroke> l)
        {
            List<object> r = new List<object>();
            foreach (Stroke s in l)
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["ch"] = s.Ch; d["v"] = s.V; d["r"] = s.R; d["hard"] = s.Hard;
                List<object> pts = new List<object>();
                foreach (PointF p in s.Pts) { pts.Add(Math.Round(p.X, 5)); pts.Add(Math.Round(p.Y, 5)); }
                d["pts"] = pts;
                r.Add(d);
            }
            return r;
        }

        static List<Stroke> StrokesFrom(object o)
        {
            List<Stroke> r = new List<Stroke>();
            // JavaScriptSerializer.DeserializeObject gives arrays as object[] (ArrayList with other entry points):
            // any list is read, or every stroke would be lost on undo, projects, copied edits and saved masks
            System.Collections.IList al = o as System.Collections.IList;
            if (al == null) return r;
            foreach (object e in al)
            {
                Dictionary<string, object> d = e as Dictionary<string, object>;
                if (d == null) continue;
                Stroke s = new Stroke();
                s.Ch = Convert.ToString(d["ch"]);
                s.V = Convert.ToSingle(d["v"], CultureInfo.InvariantCulture);
                s.R = Convert.ToSingle(d["r"], CultureInfo.InvariantCulture);
                if (d.ContainsKey("hard")) s.Hard = Convert.ToSingle(d["hard"], CultureInfo.InvariantCulture);
                System.Collections.IList pts = d["pts"] as System.Collections.IList;
                if (pts != null)
                    for (int i = 0; i + 1 < pts.Count; i += 2)
                        s.Pts.Add(new PointF(Convert.ToSingle(pts[i], CultureInfo.InvariantCulture), Convert.ToSingle(pts[i + 1], CultureInfo.InvariantCulture)));
                r.Add(s);
            }
            return r;
        }

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["tool"] = Tool;
            Dictionary<string, object> p = new Dictionary<string, object>();
            foreach (KeyValuePair<string, double> kv in P) p[kv.Key] = kv.Value;
            d["p"] = p;
            Dictionary<string, object> s = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> kv in S) s[kv.Key] = kv.Value;
            d["s"] = s;
            d["strokes"] = StrokesToList(Strokes);
            d["mask"] = StrokesToList(Mask);
            d["maskOn"] = MaskOn; d["maskInvert"] = MaskInvert;
            // only written when set: visible steps keep the signature they had (preview caches, saved projects)
            if (Hidden) d["hidden"] = true;
            return d;
        }

        public static Step FromDict(Dictionary<string, object> d)
        {
            Step st = new Step(Convert.ToString(d["tool"]));
            object o;
            if (d.TryGetValue("p", out o) && o is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)o) st.P[kv.Key] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
            if (d.TryGetValue("s", out o) && o is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)o) st.S[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            if (d.TryGetValue("strokes", out o)) st.Strokes = StrokesFrom(o);
            if (d.TryGetValue("mask", out o)) st.Mask = StrokesFrom(o);
            if (d.TryGetValue("maskOn", out o)) st.MaskOn = Convert.ToBoolean(o);
            if (d.TryGetValue("maskInvert", out o)) st.MaskInvert = Convert.ToBoolean(o);
            if (d.TryGetValue("hidden", out o)) st.Hidden = Convert.ToBoolean(o);
            return st;
        }

        static JavaScriptSerializer Ser() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

        public string Sig() { return Ser().Serialize(ToDict()); }

        public static string ListToJson(List<Step> steps)
        {
            List<object> l = new List<object>();
            foreach (Step s in steps) l.Add(s.ToDict());
            return Ser().Serialize(l);
        }

        public static List<Step> ListFromJson(string json)
        {
            List<Step> r = new List<Step>();
            object o = Ser().DeserializeObject(json);
            object[] arr = o as object[];
            if (arr == null) return r;
            foreach (object e in arr)
            {
                Dictionary<string, object> d = e as Dictionary<string, object>;
                if (d != null) r.Add(FromDict(d));
            }
            return r;
        }

        public static List<Step> CloneList(List<Step> l)
        {
            List<Step> r = new List<Step>();
            foreach (Step s in l) r.Add(s.Clone());
            return r;
        }
    }

    public static class Pipeline
    {
        // Set by the renderer on its worker thread: slow steps (fills, generative models) call CheckCancel
        // between chunks so an outdated render stops instead of finishing in the background.
        [ThreadStatic] public static CancellationToken Token;
        public static void CheckCancel() { Token.ThrowIfCancellationRequested(); }

        public static bool IsGeometric(string tool)
        {
            return tool == "crop" || tool == "rotate" || tool == "perspective" || tool == "expand";
        }

        // Tools that give the same pixels on a part of the photo (with a margin) as on the whole photo, so the live
        // view can compute only what is on screen. Checked by tests\LiveRegionTest.ps1 against whole-photo renders.
        // The others need the whole photo: global measures (HDR, Dehaze, auto Tune), faces found on the photo
        // (Portrait, Head Pose), colours sampled at a point (Selective), textures, text and frames laid out over
        // the photo (Retrolux, Grunge, Text, Frames, Double Exposure), fills (Healing), AI denoise (Raw Develop).
        static readonly HashSet<string> regionTools = new HashSet<string> {
            "tune", "details", "curves", "wb", "brush", "glamour", "tonal", "drama", "vignette", "lensblur",
            "vintage", "grainy", "bw", "noir", "film", "bloom", "halation", "grain", "color", "face" };
        static readonly HashSet<string> pointTools = new HashSet<string> { "curves", "wb", "color" };

        // Tools whose slider-independent maps (Memo) are worth preparing when the tool opens, and the settings that
        // make the tool compute all of them (Tune only reads its local brightness with shadows, highlights or
        // ambiance; Details its smoothing with structure and sharpening).
        static readonly HashSet<string> prepared = new HashSet<string> { "tune", "details", "tonal", "drama", "hdr", "glamour" };

        public static Step PrepareProbe(Step s)
        {
            if (s == null || !prepared.Contains(s.Tool)) return null;
            Step p = s.Clone();
            p.MaskOn = false; p.Hidden = false;   // only the tool's own maps: masks are prepared by the mask panel
            if (p.Tool == "tune" && p.F("shadows") == 0 && p.F("highlights") == 0 && p.F("ambiance") == 0) p.Set("shadows", 1);
            if (p.Tool == "details") { if (p.F("structure") == 0) p.Set("structure", 1); if (p.F("sharpening") == 0) p.Set("sharpening", 1); }
            return p;
        }

        public static bool RegionSafe(Step s)
        {
            if (!regionTools.Contains(s.Tool)) return false;
            if (s.Tool == "tune" && s.I("auto", 0) == 1) return false;
            return true;
        }

        // pixels around the part on screen that a step needs to compute it exactly (blur and filter reach)
        public static int RegionMargin(Step s, Img whole)
        {
            // one-tap masks are found on the whole photo and strokes drawn at their place: a mask needs no margin
            if (pointTools.Contains(s.Tool)) return 0;
            float sc = whole.Scale;
            float m = 150 * sc;   // the widest blur of any tool (3 sigma of Tune's 40 px shadow/highlight base)
            switch (s.Tool)
            {
                case "tune":
                    // brightness, contrast, saturation, warmth alone are per pixel; shadows, highlights and ambiance
                    // read a wide blur
                    if (s.F("shadows") == 0 && s.F("highlights") == 0 && s.F("ambiance") == 0) m = 0;
                    break;
                case "details":
                    // guided filters of radius 12 and 3 (two box passes each), sharpening blur of 1.1
                    m = s.F("structure") != 0 ? 30 * sc : 6 * sc;
                    break;
                case "vignette": m = 0; break;
                // per pixel plus grain: the grain's own blur (size up to 3.8 per 1000 px, 3 sigma of half of it)
                case "grain": case "grainy": m = 8 * sc; break;
                case "bw": m = 8 * sc; break;
                case "lensblur": m += 0.025f * whole.Diagonal; break;
            }
            return (int)Math.Ceiling(m) + (m > 0 ? 2 : 0);
        }

        // The picture one-tap masks (subject, background, sky, picked parts) are found on, set by whoever renders a
        // stack: the photo with only the framing steps before the step (crop, rotate, perspective). Colour and tone
        // edits made earlier then do not change a mask, and the AI models run once per photo and framing instead of
        // once per tool. null: the step's own input is used.
        [ThreadStatic] public static Img MaskBase;

        class MaskBaseEntry { public Img Root, Pic; public string Sig; }
        static readonly List<MaskBaseEntry> maskBases = new List<MaskBaseEntry>();

        // root with only the framing steps among 'before' applied (kept for the last few framings); null when an
        // Expand is among them (its fill would have to be made again just for the mask)
        public static Img GeometryBase(Img root, IList<Step> before)
        {
            if (root == null) return null;
            List<Step> geo = new List<Step>();
            foreach (Step s in before) if (IsGeometric(s.Tool) && !s.Hidden) { if (s.Tool == "expand") return null; geo.Add(s); }
            if (geo.Count == 0) return root;
            System.Text.StringBuilder sig = new System.Text.StringBuilder();
            foreach (Step s in geo) sig.Append(s.Sig()).Append('|');
            string key = sig.ToString();
            lock (maskBases) foreach (MaskBaseEntry e in maskBases) if (e.Root == root && e.Sig == key) return e.Pic;
            Img cur = root;
            foreach (Step s in geo) cur = Apply(cur, s);
            // full-resolution pictures (exports, 100% views) are made when needed, not kept: they are large
            if ((long)root.W * root.H > 6000000) return cur;
            lock (maskBases)
            {
                maskBases.Add(new MaskBaseEntry { Root = root, Pic = cur, Sig = key });
                while (maskBases.Count > 3) maskBases.RemoveAt(0);
            }
            return cur;
        }
        public static void ForgetMaskBases() { lock (maskBases) maskBases.Clear(); }

        // applies one step of a stack with the mask picture its masks need
        public static Img ApplyIn(Img cur, List<Step> steps, int i, Img root)
        {
            Step s = steps[i];
            if (s.Hidden) return cur;   // switched off in the edit stack: the next step starts from this one's input
            if (!s.MaskOn || IsGeometric(s.Tool)) return Apply(cur, s);
            Img prev = MaskBase;
            MaskBase = GeometryBase(root, steps.GetRange(0, i));
            try { return Apply(cur, s); }
            finally { MaskBase = prev; }
        }

        public static Img Apply(Img src, Step s)
        {
            if (s.Hidden) return src.Clone();
            Img o;
            switch (s.Tool)
            {
                case "tune": o = Adjust.Tune(src, s); break;
                case "details": o = Adjust.Details(src, s); break;
                case "curves": o = Adjust.Curves(src, s); break;
                case "wb": o = Adjust.WhiteBalance(src, s); break;
                case "rawdev": o = Adjust.RawDevelop(src, s); break;
                case "selective": o = Adjust.Selective(src, s); break;
                case "brush": o = Adjust.Brush(src, s); break;
                case "hdr": o = Adjust.Hdr(src, s); break;
                case "glamour": o = Adjust.Glamour(src, s); break;
                case "tonal": o = Adjust.Tonal(src, s); break;
                case "drama": o = Adjust.Drama(src, s); break;
                case "vignette": o = Adjust.Vignette(src, s); break;
                case "lensblur": o = Adjust.LensBlur(src, s); break;
                case "crop": o = Geo.Crop(src, s); break;
                case "rotate": o = Geo.Rotate(src, s); break;
                case "perspective": o = Geo.Perspective(src, s); break;
                case "expand": o = Geo.Expand(src, s); break;
                case "healing": o = Geo.Healing(src, s); break;
                case "portrait": o = Face.Portrait(src, s); break;
                case "headpose": o = Face.HeadPose(src, s); break;
                case "face": o = Face.FaceEnhance(src, s); break;
                case "vintage": o = Style.Vintage(src, s); break;
                case "grainy": o = Style.Grainy(src, s); break;
                case "retrolux": o = Style.Retrolux(src, s); break;
                case "grunge": o = Style.Grunge(src, s); break;
                case "bw": o = Style.BlackWhite(src, s); break;
                case "noir": o = Style.Noir(src, s); break;
                case "double": o = Style.DoubleExposure(src, s); break;
                case "text": o = Style.Text(src, s); break;
                case "frames": o = Style.Frames(src, s); break;
                case "look": o = ApplyAll(src, Step.ListFromJson(s.Str("steps", "[]"))); break;
                case "film": o = Film.ApplyFilm(src, s); break;
                case "bloom": o = Film.Bloom(src, s); break;
                case "halation": o = Film.Halation(src, s); break;
                case "grain": o = Film.GrainTool(src, s); break;
                case "dehaze": o = Film.Dehaze(src, s); break;
                case "color": o = Film.ColorBands(src, s); break;
                default: o = src.Clone(); break;
            }
            // a part of the photo stays that part for the next steps (live view)
            if (src.Framed && o.W == src.W && o.H == src.H && !o.Framed) o.CopyFrame(src);
            if (s.MaskOn && !IsGeometric(s.Tool) && o.W == src.W && o.H == src.H)
            {
                float[] m = EffectiveMask(src, s);
                // a result on the graphics card is mixed there
                if (Gpu.BlendOnGpu(src, o, m)) { o.Clamp(); return o; }
                float[] a = src.D, b = o.D;
                int w = src.W;
                Ops.For(0, src.H, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        float t = m[i];
                        int j = i * 3;
                        b[j] = a[j] + (b[j] - a[j]) * t;
                        b[j + 1] = a[j + 1] + (b[j + 1] - a[j + 1]) * t;
                        b[j + 2] = a[j + 2] + (b[j + 2] - a[j + 2]) * t;
                    }
                });
            }
            o.Clamp();
            return o;
        }

        // Where a step applies (before inversion): its one-tap masks if any ("smartmask": subject, background, sky,
        // "all" for the whole photo, "pick" for the parts clicked in "pickpts"; several joined with '+' add up),
        // then the strokes painted over them.
        public static float[] StepMask(Img src, Step s) { return Raster.Channel(s.Mask, "mask", src, Start(src, s, false)); }

        // what the strokes are painted over: the one-tap masks joined, and with extras also the saved masks the
        // step uses; null when there is nothing (painting then starts from an empty mask)
        static float[] Start(Img src, Step s, bool extras)
        {
            float[] start = null;
            foreach (string kind in s.Str("smartmask", "").Split('+'))
            {
                if (kind.Length == 0) continue;
                start = Join(start, KindMask(src, kind, s));
            }
            string extra = extras ? s.Str("maskextra", "") : "";
            if (extra.Length > 0) foreach (Step e in Step.ListFromJson(extra)) start = Join(start, EffectiveMask(src, e));
            return start;
        }

        static float[] Join(float[] a, float[] b)
        {
            if (b == null) return a;
            if (a == null) return (float[])b.Clone();
            for (int i = 0; i < a.Length; i++) if (b[i] > a[i]) a[i] = b[i];
            return a;
        }

        static float[] KindMask(Img src, string kind, Step s)
        {
            if (kind == "all") { float[] one = new float[src.W * src.H]; for (int i = 0; i < one.Length; i++) one[i] = 1f; return one; }
            if (src.Framed || (MaskBase != null && (MaskBase.W != src.W || MaskBase.H != src.H)))
            {
                // live view (a part of the photo, or the photo at screen size): subject, background, sky and picked
                // parts are always found on the whole photo, then its part or size is taken from that mask
                Img whole = MaskBase;
                if (whole == null) throw new InvalidOperationException("a framed picture needs the whole photo for its masks");
                float[] wm = KindMask(whole, kind, s);
                return wm == null ? null : Ops.FramePart(wm, whole.W, whole.H, src);
            }
            Img pic = MaskBase != null && MaskBase.W == src.W && MaskBase.H == src.H ? MaskBase : src;
            if (kind == "pick")
            {
                float[] p = SubjectPicker.Mask(pic, s.Str("pickpts", ""));
                // on a computer without the picker model, the main subject stands in
                if (p == null && !SubjectPicker.Installed) p = SmartMask.Compute(pic, "subject");
                return p;
            }
            return SmartMask.Compute(pic, kind);
        }

        // The share of the edit that reaches each pixel (shown in red while a mask is defined). Several masks can be
        // active at once: the one-tap masks and the saved masks the step uses ("maskextra": steps holding only a
        // mask, each complete with its own strokes and inversion) are joined, the step's strokes refine the whole,
        // then the step's inversion applies.
        public static float[] EffectiveMask(Img src, Step s)
        {
            float[] m = Raster.Channel(s.Mask, "mask", src, Start(src, s, true));
            if (s.MaskInvert) for (int i = 0; i < m.Length; i++) m[i] = 1 - m[i];
            return m;
        }

        // Runs every tool once on a tiny image in the background, so the first real use of a tool
        // (style thumbnails, first preview) does not pay the one-time code warm-up.
        public static void WarmUp()
        {
            Task.Factory.StartNew(() =>
            {
                string[] ids = { "tune", "details", "curves", "wb", "rawdev", "selective", "brush", "hdr", "glamour", "tonal", "drama", "vignette",
                    "lensblur", "crop", "rotate", "perspective", "expand", "healing", "portrait", "headpose", "face", "vintage", "grainy", "retrolux",
                    "grunge", "bw", "noir", "text", "frames", "film", "bloom", "halation", "grain", "dehaze", "color" };
                Img im = new Img(96, 64);
                for (int i = 0; i < im.D.Length; i++) im.D[i] = (i % 97) / 97f;
                foreach (string id in ids)
                {
                    try { Step s = new Step(id); s.Set("strength", 50); s.Set("brightness", 10); Apply(im, s); } catch { }
                }
            });
        }

        public static Img ApplyAll(Img src, List<Step> steps)
        {
            Img cur = src;
            for (int i = 0; i < steps.Count; i++) cur = ApplyIn(cur, steps, i, src);
            return cur == src ? src.Clone() : cur;
        }
    }

    // The open photo with its edit stack, history and preview caches.
    public class Document
    {
        public string Path = "";
        public Img Original;
        public Img Preview;
        public Img Thumb;
        public List<Step> Steps = new List<Step>();
        public Step Pending;            // tool being edited
        public int EditIndex = -1;      // -1: Pending is appended; otherwise it replaces Steps[EditIndex]
        public bool HidePending;        // e.g. crop shows the uncropped image while editing

        List<string> undo = new List<string>();
        List<string> redo = new List<string>();

        // Preview cache: output of each stack prefix, keyed by the signature chain.
        List<string> cacheKeys = new List<string>();
        List<Img> cacheImgs = new List<Img>();
        readonly object cacheLock = new object();

        public int PreviewSize = 1600;
        // The preview's long side for a photo of this size (null: PreviewSize). The window gives the size the photo
        // takes on screen at Fit, so no work is spent on pixels the screen cannot show.
        public Func<int, int, int> PreviewSizeFor;
        int EffectivePreviewSize { get { return PreviewSizeFor != null ? Math.Min(PreviewSize, PreviewSizeFor(Original.W, Original.H)) : PreviewSize; } }

        public void Load(string path)
        {
            Original = ImageIO.LoadImg(path);
            Path = path;
            Init();
        }

        public void LoadImg(Img im, string path)
        {
            Original = im; Path = path; Init();
        }

        void Init()
        {
            Preview = Original.FitInside(EffectivePreviewSize);
            Original.Stable = true; Preview.Stable = true;   // never changed: what the tools derive from them is kept (Memo)
            Thumb = Preview.FitInside(140);
            Steps.Clear(); Pending = null; EditIndex = -1; HidePending = false;
            undo.Clear(); redo.Clear();
            lock (cacheLock) { cacheKeys.Clear(); cacheImgs.Clear(); }
            ClearFullCache();
        }

        // back to the photo as opened: no edits, no history (the photo and what was computed from it stay)
        public void ClearEdits()
        {
            Steps.Clear(); Pending = null; EditIndex = -1; HidePending = false;
            undo.Clear(); redo.Clear();
        }

        // previews of the stack kept for quick slider changes
        public void ClearCaches()
        {
            lock (cacheLock) { cacheKeys.Clear(); cacheImgs.Clear(); }
            ClearFullCache();
        }

        public void SetPreviewSize(int px)
        {
            PreviewSize = px;
            RefreshPreview();
        }

        // the preview made again when its size should change (the setting, or the window resized); false if it stays
        public bool RefreshPreview()
        {
            if (Original == null) return false;
            int want = EffectivePreviewSize, now = Math.Max(Preview.W, Preview.H);
            if (want == now || (Math.Max(Original.W, Original.H) <= want && now == Math.Max(Original.W, Original.H))) return false;
            Preview = Original.FitInside(want);
            Preview.Stable = true;
            Thumb = Preview.FitInside(140);
            lock (cacheLock) { cacheKeys.Clear(); cacheImgs.Clear(); liveSmallKey = null; liveSmall = null; }
            Pipeline.ForgetMaskBases();
            return true;
        }

        public bool HasImage { get { return Original != null; } }

        // Steps actually shown: committed steps with Pending inserted or replacing.
        // The tone curve (the always-visible curve panel) stays last, after the tool being edited.
        public List<Step> EffectiveSteps(bool includePending)
        {
            List<Step> l = new List<Step>();
            Step tc = ToneCurve;
            for (int i = 0; i < Steps.Count; i++)
            {
                if (i == EditIndex && Pending != null) { if (includePending) l.Add(Pending); }
                else if (Steps[i] != tc || EditIndex >= 0 || Pending == null) l.Add(Steps[i]);
                if (i == EditIndex && Pending != null && !includePending) break;
            }
            if (EditIndex < 0 && Pending != null && includePending) { l.Add(Pending); if (tc != null) l.Add(tc); }
            return l;
        }

        // Steps before the one being edited (the input of the open tool; the tone curve comes after it).
        public List<Step> StepsBeforePending()
        {
            List<Step> l = new List<Step>();
            int end = EditIndex >= 0 ? EditIndex : Steps.Count;
            Step tc = EditIndex < 0 ? ToneCurve : null;
            for (int i = 0; i < end; i++) if (Steps[i] != tc) l.Add(Steps[i]);
            return l;
        }

        // ----- tone curve: a curves step marked "tonecurve", edited from the side panel, always last -----
        public Step ToneCurve
        {
            get { foreach (Step s in Steps) if (s.Tool == "curves" && s.Str("tonecurve", "") == "1") return s; return null; }
        }

        // Sets the tone curve points (an identity curve removes the step). snapshot: start of an edit, for undo.
        public void SetToneCurve(string points, bool snapshot)
        {
            if (snapshot) Snapshot();
            Step tc = ToneCurve;
            if (Adjust.IsIdentity(points)) { if (tc != null) Steps.Remove(tc); return; }
            if (tc == null) { tc = new Step("curves"); tc.SetS("tonecurve", "1"); Steps.Add(tc); }
            tc.SetS("curve_rgb", points);
            KeepToneCurveLast();
        }

        void KeepToneCurveLast()
        {
            Step tc = ToneCurve;
            if (tc != null && Steps[Steps.Count - 1] != tc) { Steps.Remove(tc); Steps.Add(tc); }
        }

        public Img RenderPreview(List<Step> steps, CancellationToken ct)
        {
            Img cur = Preview;
            string key = "";
            for (int i = 0; i < steps.Count; i++)
            {
                if (ct.IsCancellationRequested) return null;
                key = key + "|" + steps[i].Sig();
                Img hit = null;
                lock (cacheLock)
                {
                    if (i < cacheKeys.Count && cacheKeys[i] == key) hit = cacheImgs[i];
                }
                if (hit != null) { cur = hit; continue; }
                cur = Pipeline.ApplyIn(cur, steps, i, Preview);
                lock (cacheLock)
                {
                    while (cacheKeys.Count > i) { cacheKeys.RemoveAt(i); cacheImgs.RemoveAt(i); }
                    cacheKeys.Add(key); cacheImgs.Add(cur);
                    cur.Stable = true;   // a cached result is not changed any more (Memo)
                }
            }
            return cur;
        }

        public Img RenderPreviewSync(List<Step> steps) { return RenderPreview(steps, CancellationToken.None); }

        // Full resolution render for 100% views. Keeps the last few results (including the input of
        // the tool being edited) so changing a slider only recomputes that tool.
        List<string> fullKeys = new List<string>();
        List<Img> fullImgs = new List<Img>();

        // false keeps every step's picture until the collector takes it (the way this used to work)
        public static bool RecyclePictures = true;

        public Img RenderFullCached(List<Step> steps, CancellationToken ct)
        {
            string[] keys = new string[steps.Count];
            string key = "";
            for (int i = 0; i < steps.Count; i++) { key = key + "|" + steps[i].Sig(); keys[i] = key; }
            Img cur = Original;
            int start = 0;
            lock (cacheLock)
            {
                for (int i = steps.Count - 1; i >= 0; i--)
                {
                    int idx = fullKeys.IndexOf(keys[i]);
                    if (idx >= 0) { cur = fullImgs[idx]; start = i + 1; break; }
                }
            }
            for (int i = start; i < steps.Count; i++)
            {
                if (ct.IsCancellationRequested) return null;
                Img was = cur;
                cur = Pipeline.ApplyIn(cur, steps, i, Original);
                // the picture this step started from is finished with, unless it is the photo itself or a
                // cached result of an earlier step: its pixels go back for the next step to write into
                if (RecyclePictures && was != cur && was != Original && !was.Stable) was.Recycle();
                if (i >= steps.Count - 2)
                    lock (cacheLock)
                    {
                        int idx = fullKeys.IndexOf(keys[i]);
                        if (idx >= 0) { fullKeys.RemoveAt(idx); fullImgs.RemoveAt(idx); }
                        fullKeys.Add(keys[i]); fullImgs.Add(cur);
                        cur.Stable = true;
                        while (fullKeys.Count > 2) { fullKeys.RemoveAt(0); fullImgs.RemoveAt(0); }
                    }
            }
            return cur;
        }

        void ClearFullCache()
        {
            lock (cacheLock)
            {
                fullKeys.Clear(); fullImgs.Clear(); liveKey = null; liveImg = null; liveSmallKey = null; liveSmall = null;
                levelScales.Clear(); levelBases.Clear(); levelKeys.Clear(); levelImgs.Clear();
            }
        }

        // ----- live view: while a slider moves only what the screen shows is computed -----
        // the full-resolution input of the step being edited (kept apart from the 100% cache, which the final
        // render refills), and the screen-size input for frames of the whole photo
        string liveKey, liveSmallKey; Img liveImg, liveSmall;

        public LiveFrame RenderLive(List<Step> steps, int edit, LiveView view, CancellationToken ct)
        {
            string[] keys = new string[steps.Count];
            string key = "";
            for (int i = 0; i < steps.Count; i++) { key = key + "|" + steps[i].Sig(); keys[i] = key; }
            int p = edit >= 0 && edit < steps.Count ? edit : Math.Max(0, steps.Count - 1);
            bool region = view.Full && steps.Count > 0;
            for (int i = p; i < steps.Count && region; i++) region = steps[i].Hidden || (Pipeline.RegionSafe(steps[i]) && !Pipeline.IsGeometric(steps[i].Tool));
            if (region) return RenderRegion(steps, keys, p, view, ct);

            // the whole photo, at screen size when that is smaller than the preview (and smaller still when the
            // computer is slow: view.Quality)
            double sp = (view.Full ? 1 : view.DisplayScale * Original.W / Preview.W) * view.Quality;
            if (sp >= 0.9 || steps.Count == 0)
            {
                Img im = RenderPreview(steps, ct);
                return im == null ? null : new LiveFrame { Img = im, ToOriginal = Preview.W / (double)Original.W };
            }
            Img input = p == 0 ? Preview : RenderPreview(steps.GetRange(0, p), ct);
            if (input == null || ct.IsCancellationRequested) return null;
            int sw = Math.Max(16, (int)Math.Round(input.W * sp)), sh = Math.Max(16, (int)Math.Round(input.H * sp));
            string sk = (p == 0 ? "" : keys[p - 1]) + "@" + sw + "x" + sh;
            Img cur;
            lock (cacheLock) cur = liveSmallKey == sk ? liveSmall : null;
            if (cur == null)
            {
                cur = Ops.Resize(input, sw, sh);
                cur.Stable = true;
                lock (cacheLock) { liveSmallKey = sk; liveSmall = cur; }
            }
            for (int i = p; i < steps.Count; i++)
            {
                if (ct.IsCancellationRequested) return null;
                cur = Pipeline.ApplyIn(cur, steps, i, Preview);
            }
            return new LiveFrame { Img = cur, ToOriginal = sp * Preview.W / Original.W };
        }

        // ----- zoom levels -----
        // Between Fit and 100% the part on screen is computed at the nearest of these scales at or above the screen's
        // (each a square root of two apart), from a copy of the photo at that scale made once: no large crop of the
        // full-resolution photo to shrink at every zoom step. A part shown at 30% is computed at 35%.
        static readonly double[] levels = { 1, 0.70710678, 0.5, 0.35355339, 0.25, 0.1767767, 0.125, 0.08838835, 0.0625 };

        public static double ZoomLevel(double f)
        {
            double lv = 1;
            foreach (double l in levels) if (l >= f * 0.999) lv = l;
            return lv;
        }

        // the photo at zoom level lv (kept for the last few levels used)
        readonly List<double> levelScales = new List<double>();
        readonly List<Img> levelBases = new List<Img>();
        Img LevelOfOriginal(double lv)
        {
            lock (cacheLock)
            {
                int i = levelScales.IndexOf(lv);
                if (i >= 0) { Img hit = levelBases[i]; levelScales.RemoveAt(i); levelBases.RemoveAt(i); levelScales.Add(lv); levelBases.Add(hit); return hit; }
            }
            Img b = Original.AreaDownscale(Math.Max(1, (int)Math.Round(Original.W * lv)), Math.Max(1, (int)Math.Round(Original.H * lv)));
            b.Stable = true;
            lock (cacheLock)
            {
                levelScales.Add(lv); levelBases.Add(b);
                while (levelScales.Count > 3) { levelScales.RemoveAt(0); levelBases.RemoveAt(0); }
            }
            return b;
        }

        // the stack's result before step p at zoom level lv (lv 1: full resolution), kept for the last few
        readonly List<string> levelKeys = new List<string>();
        readonly List<Img> levelImgs = new List<Img>();
        static string LevelKey(string[] keys, int p, double lv) { return (p == 0 ? "" : keys[p - 1]) + "@" + lv.ToString("R"); }

        Img InputAt(List<Step> steps, string[] keys, int p, double lv, CancellationToken ct)
        {
            if (lv >= 0.999)
            {
                if (p == 0) return Original;
                string k = keys[p - 1];
                Img input = null;
                lock (cacheLock)
                {
                    int idx = fullKeys.IndexOf(k);
                    if (idx >= 0) input = fullImgs[idx];
                    else if (liveKey == k) input = liveImg;
                }
                if (input == null)
                {
                    input = RenderFullCached(steps.GetRange(0, p), ct);
                    if (input == null) return null;
                    input.Stable = true;
                    lock (cacheLock) { liveKey = k; liveImg = input; }
                }
                return input;
            }
            Img basis = LevelOfOriginal(lv);
            if (p == 0) return basis;
            string key = LevelKey(keys, p, lv);
            lock (cacheLock) { int i = levelKeys.IndexOf(key); if (i >= 0) return levelImgs[i]; }
            Img cur = basis;
            for (int i = 0; i < p; i++)
            {
                if (ct.IsCancellationRequested) return null;
                cur = Pipeline.ApplyIn(cur, steps, i, Preview);
            }
            cur.Stable = true;
            lock (cacheLock)
            {
                levelKeys.Add(key); levelImgs.Add(cur);
                while (levelKeys.Count > 4) { levelKeys.RemoveAt(0); levelImgs.RemoveAt(0); }
            }
            return cur;
        }

        // zoomed in: only the part on screen (and a margin the filters reach into), at the zoom level of the screen
        // (and lower while a slider moves on a slow computer: view.Quality); view.Ahead computes a little around it
        LiveFrame RenderRegion(List<Step> steps, string[] keys, int p, LiveView view, CancellationToken ct)
        {
            double lv = ZoomLevel(Math.Min(1.0, view.DisplayScale) * view.Quality);
            Img input = InputAt(steps, keys, p, lv, ct);
            if (input == null || ct.IsCancellationRequested) return null;
            // the whole stack is already done at this level: the whole photo is the part (any pan shows it)
            if (p == steps.Count && lv < 0.999) return new LiveFrame { Img = input, Patch = new RectangleF(0, 0, 1, 1), ToOriginal = lv };
            int W = input.W, H = input.H;
            RectangleF v = view.Visible;
            if (view.Ahead > 0)
            {
                float ax = v.Width * (float)view.Ahead, ay = v.Height * (float)view.Ahead;
                v = RectangleF.FromLTRB(Math.Max(0, v.Left - ax), Math.Max(0, v.Top - ay), Math.Min(1, v.Right + ax), Math.Min(1, v.Bottom + ay));
            }
            int vx = Math.Max(0, (int)Math.Floor(v.X * W)), vy = Math.Max(0, (int)Math.Floor(v.Y * H));
            int vr = Math.Min(W, (int)Math.Ceiling(v.Right * W)), vb = Math.Min(H, (int)Math.Ceiling(v.Bottom * H));
            if (vr - vx < 4 || vb - vy < 4) { vx = 0; vy = 0; vr = W; vb = H; }
            int m = 0;
            for (int i = p; i < steps.Count; i++) m = Math.Max(m, Pipeline.RegionMargin(steps[i], input));
            int x0 = Math.Max(0, vx - m), y0 = Math.Max(0, vy - m), x1 = Math.Min(W, vr + m), y1 = Math.Min(H, vb + m);
            // the part starts on a multiple of 8: the fast guided filter's shrunk grid then lines up with the whole
            // photo's (Ops.GuidedSubsample)
            x0 -= x0 % 8; y0 -= y0 % 8;
            Img cur = input.Region(x0, y0, x1 - x0, y1 - y0);
            cur.FX = x0; cur.FY = y0; cur.FW = W; cur.FH = H;
            for (int i = p; i < steps.Count; i++)
            {
                if (ct.IsCancellationRequested) return null;
                cur = Pipeline.ApplyIn(cur, steps, i, Preview);
            }
            // the margin is cut off: what remains is the part asked for
            int ix = vx - x0, iy = vy - y0, iw = Math.Min(cur.W - ix, vr - vx), ih = Math.Min(cur.H - iy, vb - vy);
            Img inner = iw > 0 && ih > 0 ? cur.Region(ix, iy, iw, ih) : cur;
            RectangleF rect = new RectangleF(vx / (float)W, vy / (float)H, (vr - vx) / (float)W, (vb - vy) / (float)H);
            return new LiveFrame { Img = inner, Patch = rect, ToOriginal = lv };
        }

        // Zoomed in, after a change: the part on screen at full quality, instead of the whole photo at full resolution
        // (24 MP takes seconds on a modest computer; the screen shows 2 MP of it at most). It starts from the latest
        // full-resolution result already kept from which every later step can work on a part (often the input of the
        // tool being edited); if none is kept, the earliest such point is computed once and kept.
        public LiveFrame RenderZoomedPart(List<Step> steps, LiveView view, CancellationToken ct)
        {
            string[] keys = new string[steps.Count];
            string key = "";
            for (int i = 0; i < steps.Count; i++) { key = key + "|" + steps[i].Sig(); keys[i] = key; }
            int p0 = steps.Count;
            while (p0 > 0 && (steps[p0 - 1].Hidden || (Pipeline.RegionSafe(steps[p0 - 1]) && !Pipeline.IsGeometric(steps[p0 - 1].Tool)))) p0--;
            double lv = ZoomLevel(Math.Min(1.0, view.DisplayScale) * view.Quality);
            int p = -1;
            lock (cacheLock)
                for (int k = steps.Count; k >= p0 && p < 0; k--)
                {
                    if (k == 0) { p = 0; break; }
                    bool kept = lv >= 0.999 ? fullKeys.Contains(keys[k - 1]) || liveKey == keys[k - 1] : levelKeys.Contains(LevelKey(keys, k, lv));
                    if (kept) p = k;
                }
            if (p < 0) p = p0;
            if (lv < 0.999 && p < steps.Count)
            {
                // when the part asked for (with what is computed around it) covers half the photo or more, the whole
                // photo at this level costs at most twice as much: it is made once and kept, and every pan and every
                // return to this zoom level is then free
                RectangleF v = view.Visible;
                double part = Math.Min(1, v.Width * (1 + 2 * view.Ahead)) * Math.Min(1, v.Height * (1 + 2 * view.Ahead));
                if (part >= 0.5) p = steps.Count;
            }
            return RenderRegion(steps, keys, p, view, ct);
        }

        public Img PendingInput() { return RenderPreviewSync(StepsBeforePending()); }

        public Img RenderFull() { return Pipeline.ApplyAll(Original, Steps); }

        // ----- history -----
        // the last operations that can be undone (and redone)
        public const int HistorySize = 10;

        public void Snapshot()
        {
            undo.Add(Step.ListToJson(Steps));
            if (undo.Count > HistorySize) undo.RemoveAt(0);
            redo.Clear();
        }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(Step.ListToJson(Steps));
            Steps = Step.ListFromJson(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
        }
        public void Redo()
        {
            if (redo.Count == 0) return;
            undo.Add(Step.ListToJson(Steps));
            Steps = Step.ListFromJson(redo[redo.Count - 1]);
            redo.RemoveAt(redo.Count - 1);
        }

        public void Commit()
        {
            if (Pending == null) return;
            Snapshot();
            if (EditIndex >= 0 && EditIndex < Steps.Count) Steps[EditIndex] = Pending;
            else Steps.Add(Pending);
            Pending = null; EditIndex = -1; HidePending = false;
            KeepToneCurveLast();
        }

        public void Cancel() { Pending = null; EditIndex = -1; HidePending = false; }

        public void RemoveStep(int i)
        {
            if (i < 0 || i >= Steps.Count) return;
            Snapshot();
            Steps.RemoveAt(i);
        }

        public void SetSteps(List<Step> l) { Snapshot(); Steps = l; KeepToneCurveLast(); }

        // the edit stack's eye: one operation of the history (undo shows the step again)
        public void SetHidden(int i, bool hidden)
        {
            if (i < 0 || i >= Steps.Count || Steps[i].Hidden == hidden) return;
            Snapshot();
            Steps[i].Hidden = hidden;
        }

        public string ProjectJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["app"] = "PixTune"; d["version"] = 1; d["source"] = Path;
            d["steps"] = Step.ListToJson(Steps);
            return new JavaScriptSerializer().Serialize(d);
        }

        public static string[] ParseProject(string json)
        {
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            return new string[] { Convert.ToString(d["source"]), Convert.ToString(d["steps"]) };
        }
    }

    // Everything the engine keeps about the current photo (masks, AI results, face fits, painted strokes), emptied
    // when another photo is opened and when PixTune closes. Files: DngConverter's converted copies (DeleteFiles).
    public static class Caches
    {
        public static void Clear()
        {
            SmartMask.Forget(); SubjectPicker.Forget(); Segmenter.Forget(); Denoiser.Forget(); Raster.Forget(); Pipeline.ForgetMaskBases(); Memo.Forget(); Pool.Forget(); Ops.ForgetPowTables();
            FaceMesh.Forget(); LivePortrait.Forget(); Geo.Forget(); Style.Forget(); Diffusion.Forget();
        }

        public static void DeleteFiles()
        {
            try { if (System.IO.Directory.Exists(DngConverter.CacheDir)) System.IO.Directory.Delete(DngConverter.CacheDir, true); }
            catch (Exception) { }
            // photos unpacked from projects saved with their photo (the project file still holds them)
            try { if (System.IO.Directory.Exists(App.ProjectPhotos)) System.IO.Directory.Delete(App.ProjectPhotos, true); }
            catch (Exception) { }
        }
    }

    // What the screen shows, for live frames: the visible part of the photo (fractions of it), screen pixels per
    // full-resolution pixel, and whether the view is zoomed past the preview's detail.
    // Quality: share of that resolution actually computed (below 1 while a slider moves on a slow computer).
    // Ahead: share of the visible width and height also computed on each side, so a small pan or zoom out shows
    // pixels already made (renders after a change; not while a slider moves).
    public class LiveView { public RectangleF Visible = new RectangleF(0, 0, 1, 1); public double DisplayScale = 1; public bool Full; public double Quality = 1; public double Ahead; }

    // A live frame: the whole photo (Patch empty) or the part Patch of it; ToOriginal: its pixels per full-size pixel.
    public class LiveFrame { public Img Img; public RectangleF Patch = RectangleF.Empty; public double ToOriginal = 1; }

    // Renders the preview on a worker thread, newest request wins, result is pushed to the UI thread.
    public class Renderer
    {
        public Document Doc;
        public Control Ui;
        public event EventHandler<RenderedEventArgs> Rendered;
        CancellationTokenSource cts;
        int seq;
        public volatile bool Busy;

        public Renderer(Document doc, Control ui) { Doc = doc; Ui = ui; }

        public void Request() { Start(false); }

        // Renders the current edits at the photo's full resolution (for 100% views).
        public void RequestFull() { Start(true); }

        // While painting: a request made during a render does not cancel it (the picture would never update
        // until the stroke ends); the newest state is rendered as soon as the current frame is shown.
        public void RequestLive(bool full)
        {
            if (running) { again = true; againFull = full; againView = null; return; }
            Start(full, null);
        }

        // While a slider moves (or a stroke is painted): a frame of only what the screen shows (LiveView), and like
        // RequestLive the frame under way is finished and shown before the newest state follows.
        public void RequestLiveView(LiveView view)
        {
            if (running) { again = true; againFull = false; againView = view; return; }
            Start(false, view);
        }
        volatile bool running, again, againFull;
        LiveView againView;

        // Zoomed in past the preview, after a change or a pan: the preview with every edit (the picture under the
        // view) and the part on screen at full quality over it, instead of the whole photo at full resolution.
        public void RequestZoomed(LiveView view) { Start(false, view, true); }

        void Start(bool full) { Start(full, null, false); }
        void Start(bool full, LiveView view) { Start(full, view, false); }

        void Start(bool full, LiveView view, bool zoomed)
        {
            if (Doc == null || !Doc.HasImage) return;
            again = false;
            List<Step> steps;
            int edit = -1;
            if (Doc.HidePending)
            {
                steps = Step.CloneList(Doc.StepsBeforePending());
                if (Doc.EditIndex < 0 && Doc.ToneCurve != null) steps.Add(Doc.ToneCurve.Clone());
            }
            else
            {
                List<Step> eff = Doc.EffectiveSteps(true);
                if (Doc.Pending != null) edit = eff.IndexOf(Doc.Pending);
                steps = Step.CloneList(eff);
            }
            if (cts != null) cts.Cancel();
            CancellationTokenSource my = new CancellationTokenSource();
            cts = my;
            int id = Interlocked.Increment(ref seq);
            Busy = true; running = true;
            Renders.Begin();
            Task.Factory.StartNew(() =>
            {
                Bitmap bmp = null, part = null; Img img = null; string err = null;
                LiveFrame lf = null;
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    Pipeline.Token = my.Token;
                    if (zoomed)
                    {
                        img = Doc.RenderPreview(steps, my.Token);
                        if (img != null && !my.IsCancellationRequested) bmp = img.ToBitmap();
                        lf = my.IsCancellationRequested ? null : Doc.RenderZoomedPart(steps, view, my.Token);
                        if (lf != null && !my.IsCancellationRequested) part = lf.Img.ToBitmap();
                    }
                    else
                    {
                        if (view != null)
                        {
                            lf = Doc.RenderLive(steps, edit, view, my.Token);
                            if (lf != null) img = lf.Img;
                        }
                        else img = full ? Doc.RenderFullCached(steps, my.Token) : Doc.RenderPreview(steps, my.Token);
                        if (img != null && !my.IsCancellationRequested) bmp = img.ToBitmap();
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                finally { Pipeline.Token = CancellationToken.None; Renders.End(); }
                long ms = clock.ElapsedMilliseconds;
                if (my.IsCancellationRequested || id != seq) { if (bmp != null) bmp.Dispose(); if (part != null) part.Dispose(); return; }
                try
                {
                    Ui.BeginInvoke((MethodInvoker)delegate
                    {
                        // a newer request may have been made while this result was on its way (a cached render
                        // finishes at once): only the newest one is shown, or an outdated picture would replace it
                        if (id != seq) { if (bmp != null) bmp.Dispose(); if (part != null) part.Dispose(); return; }
                        running = false;
                        bool more = again;
                        if (!more) Busy = false;
                        EventHandler<RenderedEventArgs> h = Rendered;
                        if (h != null)
                        {
                            RenderedEventArgs a = new RenderedEventArgs(bmp, img, err); a.Full = full; a.Ms = ms;
                            if (zoomed) { a.Zoomed = true; a.PartBitmap = part; if (lf != null) { a.Patch = lf.Patch; a.ToOriginal = lf.ToOriginal; } }
                            else if (lf != null) { a.Live = true; a.Patch = lf.Patch; a.ToOriginal = lf.ToOriginal; }
                            h(this, a);
                        }
                        if (more) Start(againFull, againView);
                    });
                }
                catch { running = false; }
            });
        }
    }

    // While any render is under way the memory collector avoids its long blocking passes. A render allocates
    // hundreds of megabytes a second, and one full collection in the middle of it froze every thread, the
    // window's included, for a sixth of a second: long enough to be felt as a stall while panning or zooming.
    // The usual mode comes back once the last render ends (renders can overlap, so they are counted).
    public static class Renders
    {
        static int running;
        static System.Runtime.GCLatencyMode before;
        public static void Begin()
        {
            if (Interlocked.Increment(ref running) != 1) return;
            try { before = System.Runtime.GCSettings.LatencyMode; System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; }
            catch { }
        }
        public static void End()
        {
            if (Interlocked.Decrement(ref running) != 0) return;
            try { System.Runtime.GCSettings.LatencyMode = before; } catch { }
            Pool.Trim(IdleKeep);
        }
        // How much scratch memory is still worth holding once nothing is being rendered: enough for the next
        // edit to start without allocating, not so much that the window is left short for its own bitmaps
        // (holding half a gigabyte of planes for a photo nobody was editing once starved a screenshot).
        public static long IdleKeep = 512L << 20;
    }

    // Applies one edit stack to many photos in the background and saves each result.
    public class BatchJob
    {
        public volatile bool Cancel;
        public event EventHandler<BatchEventArgs> Progress; // raised on the UI thread

        public void Start(Control ui, string[] files, List<Step> steps, string outDir, string format, int quality, int longSide, bool keepExif)
        {
            List<Step> work = Step.CloneList(steps);
            Task.Factory.StartNew(() =>
            {
                // an export takes every core: nothing is waiting on the window but a progress bar
                Ops.AllCores = true;
                try
                {
                for (int i = 0; i < files.Length && !Cancel; i++)
                {
                    string file = files[i], output = null, err = null;
                    try
                    {
                        Img im;
                        im = ImageIO.LoadImg(file);
                        Img res = Pipeline.ApplyAll(im, work);
                        if (longSide > 0 && Math.Max(res.W, res.H) > longSide) res = res.FitInside(longSide);
                        string ext = format == "png" ? ".png" : (format == "tif" ? ".tif" : ".jpg");
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(file) + "_edited";
                        output = System.IO.Path.Combine(outDir, baseName + ext);
                        for (int k = 2; System.IO.File.Exists(output); k++) output = System.IO.Path.Combine(outDir, baseName + " (" + k + ")" + ext);
                        ImageIO.Save(res, output, format, quality, keepExif ? file : null);
                    }
                    catch (Exception ex) { err = ex.Message; }
                    int done = i + 1;
                    try
                    {
                        ui.BeginInvoke((MethodInvoker)delegate
                        {
                            EventHandler<BatchEventArgs> h = Progress;
                            if (h != null) h(this, new BatchEventArgs(done, files.Length, file, output, err));
                        });
                    }
                    catch { }
                }
                }
                finally { Ops.AllCores = false; }
            });
        }
    }

    public class BatchEventArgs : EventArgs
    {
        public int Done, Total; public string File, Output, Error;
        public BatchEventArgs(int d, int t, string f, string o, string e) { Done = d; Total = t; File = f; Output = o; Error = e; }
    }

    public class RenderedEventArgs : EventArgs
    {
        public Bitmap Bitmap; public Img Image; public string Error; public bool Full;
        // a live frame: Patch (fractions of the photo) when only the part on screen was computed; ToOriginal: its
        // pixels per full-size pixel
        public bool Live; public RectangleF Patch = RectangleF.Empty; public double ToOriginal = 1;
        // zoomed in: Bitmap is the preview with every edit, PartBitmap the part on screen at full quality (at Patch)
        public bool Zoomed; public Bitmap PartBitmap;
        public long Ms;   // how long the render took
        public RenderedEventArgs(Bitmap b, Img i, string e) { Bitmap = b; Image = i; Error = e; }
    }
}

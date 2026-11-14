using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PixTune
{
    // The tool being used: opening, applying, cancelling, looks, its own undo history, and the optional AI downloads.
    public partial class App
    {
        // ---------------- tool lifecycle ----------------
        public void StartTool(string id, Step existing = null, int index = -1)
        {
            if (!Doc.HasImage) return;
            ToolDef def = Tools[id];
            Step step;
            // a step hidden in the edit stack is shown while it is edited (applying it keeps it shown; cancelling
            // leaves the stack's copy hidden)
            if (existing != null) { step = existing.Clone(); step.Hidden = false; }
            else
            {
                step = new Step(id);
                if (id != "selective" && id != "color") foreach (Prm p in def.Params) if (!p.K.StartsWith("_")) step.Set(p.K, p.Def);
                if (def.Styles != null) step.Set("style", 0);
            }
            Doc.Pending = step;
            Doc.EditIndex = index;
            Doc.HidePending = def.HideOutput;
            Step = step; ToolDef = def;
            Sliders = new Dictionary<string, ParamSlider>(); ParamKeys.Clear(); Active = 0;
            InputCache = null; ShowMask = false; Eraser = false; Dragging = false;
            MaskPaint = -1; MaskView = false; ToolCanvas = null;
            Mode = "tool";
            UpdateTopButtons();

            ImageCanvas c = Canvas;
            c.Faces.Clear(); c.ClearMask(); c.Hud = "";
            c.ShowEllipse = def.Ellipse;
            c.Center = new PointF(step.F("cx", 0.5f), step.F("cy", 0.5f));
            SetCanvasMode(def.Canvas);

            SetHeadTitle(StepTitle(step));
            Foot.Visible = true;
            ClearBody();
            Body.SuspendLayout();
            if (def.Styles != null)
            {
                AddSection("Styles");
                TileGroup grp = AddTiles(def.Styles.Select(n => T(n)).ToList(), step.I("style", 0), def.StyleW > 0 ? def.StyleW : 74, def.StyleH > 0 ? def.StyleH : 82,
                    (i, t) => { Step.Set("style", i); RequestRender(); });
                StartStyleThumbs(grp);
            }
            if (def.Params.Count > 0) AddSection("Adjust");
            foreach (Prm p in def.Params) AddSlider(p);
            if (def.Setup != null) def.Setup();
            Body.ResumeLayout();
            SyncMaskDock();
            if (!ToolRestoring) ResetToolHistory();
            if (ParamKeys.Count > 0) SetActive(ParamKeys[0]);
            RequestRender();
            // once the tool's first frame is shown, its slider-independent maps are made in the background
            PrepareAfterRender = Pipeline.PrepareProbe(step);
        }

        // Runs the open tool once in the background, on its input, with the settings that make it compute every map
        // its sliders do not change (Memo): the first frame of a drag then only does the tool's per-pixel work.
        public Step PrepareAfterRender;
        void PrepareTool()
        {
            Step probe = PrepareAfterRender;
            PrepareAfterRender = null;
            if (probe == null || Mode != "tool" || Doc.HidePending) return;
            List<Step> before = Step.CloneList(Doc.StepsBeforePending());
            Document doc = Doc;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                try
                {
                    Img input = doc.RenderPreview(before, System.Threading.CancellationToken.None);
                    if (input != null) Pipeline.Apply(input, probe);
                }
                catch (Exception) { }   // only a head start: the drag computes what is missing
            });
        }

        // Style thumbnails are prepared in the background, then fade in from top to bottom.
        void StartStyleThumbs(TileGroup grp)
        {
            List<Tile> tiles = new List<Tile>();
            List<List<Step>> jobs = new List<List<Step>>();
            int i = 0;
            foreach (Control t in grp.Controls)
            {
                Step s = Step.Clone(); s.Set("style", i); i++;
                tiles.Add((Tile)t); jobs.Add(new List<Step> { s });
            }
            Thumbs.Render(Form, ToolInput().FitInside(110), jobs, tiles, true);
        }

        public void ApplyTool()
        {
            if (Mode != "tool") return;
            if (ToolDef.Id == "look")
            {
                // like the mobile app: a look lands in the stack as its individual tools, each editable later
                string name = Step.Str("name", "");
                List<Step> all = Step.CloneList(Doc.Steps);
                List<Step> subs = LookSteps(Step);
                all.AddRange(subs);
                Doc.Cancel();
                Doc.SetSteps(all);
                EndTool();
                SetStatus("Look '" + name + "' added as " + subs.Count + " editable steps. Open the edit stack to fine-tune them.");
                return;
            }
            if (ToolDef.BeforeApply != null) ToolDef.BeforeApply();
            Doc.Commit();
            EndTool();
        }

        // The tools inside a look bundle, each tagged with the look it came from.
        public List<Step> LookSteps(Step bundle)
        {
            string name = bundle.Str("name", "");
            List<Step> subs = Step.ListFromJson(bundle.Str("steps", "[]"));
            foreach (Step s in subs) s.SetS("look", name);
            return subs;
        }

        public void CancelTool()
        {
            if (Mode != "tool") return;
            Doc.Cancel();
            EndTool();
        }

        void EndTool()
        {
            ImageCanvas c = Canvas;
            MaskPaint = -1; MaskView = false; ToolCanvas = null;
            ToolUndo.Clear(); ToolRedo.Clear(); ToolLast = null;
            SetCanvasMode("View");
            c.ClearMask(); c.Faces.Clear(); c.Hud = ""; c.ShowEllipse = false; c.BrushTarget = null;
            Step = null; InputCache = null;
            string back = ReturnTo;
            ReturnTo = null;
            if (back == "stack") ShowStack(); else ShowHome(null);
            SyncMaskDock();
            RequestRender();
        }

        // ---------------- looks ----------------
        public void StartLook(string name, List<Step> steps)
        {
            Step bundle = NewLookBundle(name, steps);
            ToolDef def = Tools["look"];
            Doc.Pending = bundle; Doc.EditIndex = -1; Doc.HidePending = false;
            Step = bundle; ToolDef = def;
            Sliders = new Dictionary<string, ParamSlider>(); ParamKeys.Clear(); InputCache = null;
            Mode = "tool";
            SyncMaskDock();
            ResetToolHistory();
            UpdateTopButtons();
            SetCanvasMode("View");
            Canvas.Hud = name;
            SetHeadTitle("Look: " + name);
            Foot.Visible = true;
            // the picker is the Looks tab itself, already built: clicking another look just swaps the preview
            ShowBody(GetHomePanel("looks"));
            RequestRender();
        }

        // Clicking a look tile: opens the picker from the Looks tab, or swaps the look while picking.
        public void SelectLook(string name, List<Step> steps)
        {
            if (Mode == "tool" && ToolDef.Id == "look")
            {
                Step b = NewLookBundle(name, steps);
                Doc.Pending = b; Step = b;
                Canvas.Hud = name; Canvas.Invalidate();
                HeadTitle.Text = "Look: " + name;
                RequestRender();
            }
            else StartLook(name, steps);
        }

        public void SaveCurrentLook()
        {
            if (!Doc.HasImage || Doc.Steps.Count == 0) { SetStatus("There are no edits to save as a look."); return; }
            string name = ReadText("Save look", "Name of the look:", "Look " + DateTime.Now.ToString("yyyy-MM-dd HHmm"));
            if (string.IsNullOrEmpty(name)) return;
            // what is seen is saved: steps hidden in the edit stack are left out of the look
            List<Step> steps = Doc.Steps.Where(s => !Pipeline.IsGeometric(s.Tool) && s.Tool != "healing" && !s.Hidden).ToList();
            string p = SaveUserLook(name, steps);
            if (!string.IsNullOrEmpty(p)) { SetStatus("Look saved: " + name); if (Mode == "home") ShowHome("looks"); }
        }

        public void ImportLookFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "PixTune look (*.json)|*.json";
                if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                try
                {
                    List<Step> steps = Step.ListFromJson(File.ReadAllText(dlg.FileName));
                    if (steps.Count == 0) throw new Exception("This file contains no edits.");
                    Directory.CreateDirectory(LooksDir());
                    File.Copy(dlg.FileName, Path.Combine(LooksDir(), Path.GetFileName(dlg.FileName)), true);
                    ShowHome("looks");
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        // ---------------- undo / redo ----------------
        // Undo / Redo (buttons, Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y): the last ten operations. Inside a tool they are the
        // tool's own changes; once a tool is applied, it counts as one operation of the photo.
        public void InvokeUndo()
        {
            if (Mode == "tool") { StepToolHistory(ToolUndo, ToolRedo, "Undone"); return; }
            if (Doc.CanUndo) { Doc.Undo(); RequestRender(); if (Mode == "stack") ShowStack(); else UpdateTopButtons(); }
        }
        public void InvokeRedo()
        {
            if (Mode == "tool") { StepToolHistory(ToolRedo, ToolUndo, "Redone"); return; }
            if (Doc.CanRedo) { Doc.Redo(); RequestRender(); if (Mode == "stack") ShowStack(); else UpdateTopButtons(); }
        }

        // A change becomes one operation once it is finished: when its picture is rendered with no mouse button held
        // (a slider let go, a stroke ended, a tile, a mask...), or when the crop frame is let go. The state is the
        // tool's step (its settings, strokes, points and mask) plus, for Crop, the frame.
        public class ToolState { public string Key, Json; public RectangleF Crop; }
        public readonly List<ToolState> ToolUndo = new List<ToolState>(), ToolRedo = new List<ToolState>();
        public ToolState ToolLast;
        public bool ToolRestoring;

        ToolState GetToolState()
        {
            string j = Step.ListToJson(new List<Step> { Step });
            RectangleF r = Canvas.CropRect;
            string c = ToolDef.Id == "crop" ? string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}", r.X, r.Y, r.Width, r.Height) : "";
            return new ToolState { Key = j + "#" + c, Json = j, Crop = r };
        }

        // a new tool session starts with an empty history
        void ResetToolHistory()
        {
            ToolUndo.Clear(); ToolRedo.Clear();
            ToolLast = Mode == "tool" && Step != null ? GetToolState() : null;
        }

        public void UpdateToolHistory()
        {
            if (Mode != "tool" || Step == null || ToolRestoring || ToolLast == null) return;
            if (Control.MouseButtons != MouseButtons.None) return;
            ToolState now = GetToolState();
            if (now.Key == ToolLast.Key) return;
            ToolUndo.Add(ToolLast);
            while (ToolUndo.Count > Document.HistorySize) ToolUndo.RemoveAt(0);
            ToolRedo.Clear();
            ToolLast = now;
            UpdateTopButtons();
        }

        // moves one state from one list to the other and puts the tool back in it
        void StepToolHistory(List<ToolState> from, List<ToolState> to, string what)
        {
            UpdateToolHistory();
            if (from.Count == 0) return;
            to.Add(GetToolState());
            ToolState st = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            List<Step> l = Step.ListFromJson(st.Json);
            string ret = ReturnTo; int idx = Doc.EditIndex, active = Active;
            ToolRestoring = true;
            try { StartTool(ToolDef.Id, l[0], idx); }
            finally { ToolRestoring = false; }
            ReturnTo = ret;
            if (ToolDef.Id == "crop") { Canvas.CropRect = st.Crop; UpdateCropHud(); }
            if (active < ParamKeys.Count) SetActive(ParamKeys[active]);
            ToolLast = GetToolState();
            UpdateTopButtons();
            SetStatus(what + " (" + from.Count + " more)");
        }

        // ---------------- optional AI downloads ----------------
        class AiPack { public string Title, Note; public string[] Lines; public Func<bool> Installed; }
        static readonly Dictionary<string, AiPack> AiPacks = new Dictionary<string, AiPack> {
            { "lama", new AiPack { Title = "AI fill", Installed = () => Lama.Installed, Lines = new[] { "the LaMa inpainting model (Apache 2.0 licence), from huggingface.co" } } },
            { "generative", new AiPack { Title = "Generative fill", Installed = () => Diffusion.Installed,
                Lines = new[] { "Stable Diffusion 1.5 inpainting (CreativeML OpenRAIL-M licence), ONNX version from huggingface.co", "Real-ESRGAN x4plus for High detail (BSD licence), from Qualcomm AI Hub" },
                Note = "It runs on the graphics card (DirectX 12, 6 GB or more recommended) and takes about 30 to 60 seconds per expansion." } },
            { "masks", new AiPack { Title = "AI masks", Installed = () => Segmenter.Installed,
                Lines = new[] { "BiRefNet (MIT licence) for Subject and Background, from huggingface.co", "Detectron2 panoptic segmentation (Apache 2.0 licence) for Sky, from Qualcomm AI Hub" },
                Note = "Without them, the masks use simple colour and position rules." } },
            { "denoise", new AiPack { Title = "AI denoise", Installed = () => Denoiser.Installed, Lines = new[] { "NAFNet trained on real camera noise (MIT licence), from Qualcomm AI Hub" } } },
            { "pick", new AiPack { Title = "Pick one subject", Installed = () => SubjectPicker.Installed,
                Lines = new[] { "MobileSAM, a light Segment Anything model (Apache 2.0 licence), from Qualcomm AI Hub" },
                Note = "Then click any person or object in the photo to mask just that one." } },
            { "headpose", new AiPack { Title = "AI head pose", Installed = () => LivePortrait.Installed,
                Lines = new[] { "LivePortrait (MIT licence), ONNX version from huggingface.co" },
                Note = "Each change of turn, tilt or smile then takes about 5 to 15 seconds." } },
            { "depth", new AiPack { Title = "Depth for Lens Blur", Installed = () => Depther.Installed,
                Lines = new[] { "Depth Anything V2 Small (Apache 2.0 licence), ONNX version from huggingface.co" },
                Note = "It works out how far away everything in the photo is, once, so the blur can follow the real depth. After that, moving the focus is instant." } },
        };
        public AiSetup AiSetupRunning;
        public bool MasksAsked;

        // Offers the one-time download of an optional AI pack, then calls done with true once it is installed.
        public void StartAiDownload(string pack, Action<bool> done)
        {
            if (AiSetupRunning != null) { SetStatus("The AI files are already downloading"); return; }
            AiPack p = AiPacks[pack];
            int mb = (int)(AiSetup.MissingBytes(Lama.Dir, pack) / 1048576);
            List<string> lines = new List<string>(p.Lines);
            lines.Add("the ONNX Runtime engine by Microsoft (MIT licence), from nuget.org, if not already installed");
            string msg = p.Title + " needs extra files (about " + mb + " MB still to download), downloaded once:\n\n" +
                string.Concat(lines.Select(l => "  - " + l + "\n")) + "\n" + (string.IsNullOrEmpty(p.Note) ? "" : p.Note + "\n\n") +
                "The files are checked against known fingerprints before use and stored in\n" + Lama.Dir + "\n\nDownload them now?";
            if (MessageBox.Show(Form, msg, "PixTune - " + p.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                if (done != null) done(false);
                return;
            }
            AiSetup s = new AiSetup();
            AiSetupRunning = s;
            s.Progress += (o, e) => Safe(() =>
            {
                if (!e.Finished)
                {
                    string pct = e.Total > 0 ? " " + (int)(100.0 * e.Done / e.Total) + "%" : " " + (int)(e.Done / 1048576) + " MB";
                    SetStatus("Downloading " + e.Stage + "..." + pct);
                    return;
                }
                AiSetupRunning = null;
                Lama.Reset(); Diffusion.Reset(); Upscaler.Reset(); Segmenter.Reset(); Denoiser.Reset(); LivePortrait.Reset(); SubjectPicker.Reset(); Depther.Reset(); SmartMask.Forget();
                bool ok = string.IsNullOrEmpty(e.Error) && p.Installed();
                SetStatus(ok ? "AI files installed" : "AI download failed: " + e.Error);
                if (done != null) done(ok);
            });
            SetStatus("Downloading the AI files...");
            s.Start(Form, Lama.Dir, pack);
        }

        // One-tap masks use AI models when installed: offered once per session, the simple built-in masks otherwise.
        public void RequestAiMasks()
        {
            if (Segmenter.Installed) { Segmenter.WarmUp(); return; }
            if (MasksAsked) return;
            MasksAsked = true;
            StartAiDownload("masks", ok => { if (ok && Mode == "tool") { UpdateBrushMask(); RequestRender(); } });
        }

        // ---------------- faces (the detector built into Windows) ----------------
        // The Windows Runtime face detector is reached through reflection, as the script did: no compile-time
        // reference to the Windows metadata is needed.
        static MethodInfo asTask;
        static object Await(object op, Type t)
        {
            object task = asTask.MakeGenericMethod(t).Invoke(null, new object[] { op });
            ((Task)task).Wait(-1);
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        // "x,y,w,h;..." of each face, as fractions of the picture; "" when none (or no detector)
        public string FindFaces(Img im)
        {
            try
            {
                Type tDet = Type.GetType("Windows.Media.FaceAnalysis.FaceDetector, Windows.Media.FaceAnalysis, ContentType=WindowsRuntime", true);
                Type tDec = Type.GetType("Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime", true);
                Type tSb = Type.GetType("Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType=WindowsRuntime", true);
                Type tFmt = Type.GetType("Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType=WindowsRuntime", true);
                Type tFace = Type.GetType("Windows.Media.FaceAnalysis.DetectedFace, Windows.Media.FaceAnalysis, ContentType=WindowsRuntime", true);
                Assembly rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                if (asTask == null)
                    asTask = rt.GetType("System.WindowsRuntimeSystemExtensions").GetMethods().First(m => m.Name == "AsTask" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                if (!(bool)tDet.GetProperty("IsSupported").GetValue(null, null)) return "";
                Img small = im.FitInside(1200);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (Bitmap bmp = small.ToBitmap()) bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    object ras = rt.GetType("System.IO.WindowsRuntimeStreamExtensions").GetMethod("AsRandomAccessStream").Invoke(null, new object[] { ms });
                    Type tStream = Type.GetType("Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime", true);
                    object dec = Await(tDec.GetMethod("CreateAsync", new[] { tStream }).Invoke(null, new[] { ras }), tDec);
                    object sb = Await(tDec.GetMethod("GetSoftwareBitmapAsync", Type.EmptyTypes).Invoke(dec, null), tSb);
                    object gray = tSb.GetMethod("Convert", new[] { tSb, tFmt }).Invoke(null, new[] { sb, Enum.Parse(tFmt, "Gray8") });
                    object det = Await(tDet.GetMethod("CreateAsync").Invoke(null, null), tDet);
                    Type tList = typeof(IList<>).MakeGenericType(tFace);
                    IEnumerable faces = (IEnumerable)Await(tDet.GetMethod("DetectFacesAsync", new[] { tSb }).Invoke(det, new[] { gray }), tList);
                    List<string> outp = new List<string>();
                    CultureInfo inv = CultureInfo.InvariantCulture;
                    foreach (object f in faces)
                    {
                        object b = f.GetType().GetProperty("FaceBox").GetValue(f, null);
                        Func<string, double> v = n => Convert.ToDouble(b.GetType().GetProperty(n).GetValue(b, null));
                        outp.Add(string.Format(inv, "{0},{1},{2},{3}", v("X") / small.W, v("Y") / small.H, v("Width") / small.W, v("Height") / small.H));
                    }
                    return string.Join(";", outp.ToArray());
                }
            }
            catch (Exception) { return ""; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace PixTune
{
    // The main window: top bar, photo canvas, side panel (header, tone curve, mask panel, scrolling body, footer).
    public partial class App
    {
        public static string ImageFilter = "Images and camera RAW|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif;*.heic;*.heif;*.webp;*.jxr;" + RawDecoder.FilterPattern + "|All files|*.*";

        // top bar
        public Panel TopBar;
        public IconButton BtnOpen, BtnMenu, BtnExport, BtnInfo, BtnStack, BtnCompare, BtnRedo, BtnUndo, Btn100, BtnFit, BtnHist;
        public IconButton[] TopButtons;
        public Label LblTitle;
        // side panel
        public Panel Side;
        public FlowLayoutPanel SideHead;
        public TableLayoutPanel SideFoot;
        public List<Control> Bodies = new List<Control>();
        public IconButton BtnCancel, BtnApply, BtnToolCompare;
        public IconButton[] HomeTabs;
        public IconButton HeadBack;
        public Label HeadTitle;
        // tone curve
        public const string ToneDefault = "0,0;0.25,0.25;0.5,0.5;0.75,0.75;1,1";
        public Panel CurveDock;
        public CurveEditor ToneEd;
        public IconButton ToneReset;
        public string ToneSig = "";
        public bool ToneEditing;

        // Raised once the window is shown and the photo given on the command line is open (test scripts start here).
        public event EventHandler Started;

        // Creates the application: settings, engine options, window.
        public static App Create(string root)
        {
            Root = root ?? "";
            App a = new App();
            Current = a;
            a.Init();
            return a;
        }

        void Init()
        {
            Settings = AppSettings.Load();
            Doc.PreviewSize = Settings.PreviewSize;
            Gpu.Enabled = Settings.UseGpu;
            Gpu.WarmUp();
            Gpu.WarmShaders();
            Pipeline.WarmUp();
            if (Settings.Appearance == "Light") Theme.UseLight();
            Lama.Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixTune\\ai");
            Diffusion.RuntimePath = Lama.RuntimePath; Diffusion.Dir = Path.Combine(Lama.Dir, "sd15-inpaint");
            Upscaler.RuntimePath = Lama.RuntimePath; Upscaler.Dir = Path.Combine(Lama.Dir, "esrgan");
            Segmenter.RuntimePath = Lama.RuntimePath; Segmenter.Dir = Path.Combine(Lama.Dir, "masks");
            Denoiser.RuntimePath = Lama.RuntimePath; Denoiser.Dir = Path.Combine(Lama.Dir, "denoise");
            SubjectPicker.RuntimePath = Lama.RuntimePath; SubjectPicker.Dir = Path.Combine(Lama.Dir, "pick");
            Depther.RuntimePath = Lama.RuntimePath; Depther.Dir = Path.Combine(Lama.Dir, "depth");
            GpuCheck.Root = Lama.Dir;
            GpuCheck.Load(Settings.GpuModels);
            GpuCheck.LoadSpeed(Settings.GpuSpeed);
            // which version this is, and a quiet look for a newer one once a day (it only asks, never installs)
            Updater.ReadVersion(App.Root);
            Updater.SweepReplaced(App.Root);   // a library an update had to move aside is not in use now
            if (Settings.CheckUpdates) LookForUpdate(false);
            // results found by the check or by a model failing in use are kept in the settings
            GpuCheck.Changed += (s, e) => OnUi(() => { Settings.GpuModels = GpuCheck.Save(); Settings.GpuSpeed = GpuCheck.Speed; Settings.Save(); });
            Tools.Clear();
            RegisterTools();
            BuildWindow();
            // the preview is the size the photo takes on screen at Fit (at most the Preview quality setting)
            Doc.PreviewSizeFor = FitPreviewSide;
            System.Windows.Forms.Timer resizeTimer = new System.Windows.Forms.Timer(); resizeTimer.Interval = 400;
            resizeTimer.Tick += (s, e) => Safe(() => { resizeTimer.Stop(); PreviewFollowsWindow(); });
            Canvas.Resize += (s, e) => { resizeTimer.Stop(); resizeTimer.Start(); };
        }

        // Shows the window until it is closed; path: a photo or a .pixtune project to open first.
        public void Run(string path)
        {
            Form.Shown += (s, e) => Safe(() =>
            {
                ShowHome("tools");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string full = Path.GetFullPath(path);
                    if (full.EndsWith(".pixtune", StringComparison.OrdinalIgnoreCase)) OpenProject(full); else OpenImage(full, null);
                }
                EventHandler h = Started;
                if (h != null) h(this, EventArgs.Empty);
                if (GpuCheck.AtLaunch) CheckGpuWhenIdle();
            });
            Form.ShowDialog();
            Form.Dispose();
        }

        public int SideWidth() { return Side.ClientSize.Width; }

        IconButton NewTopButton(string glyph, string text, string tip, Action click)
        {
            IconButton b = Ui.Button(glyph, text);
            if (!string.IsNullOrEmpty(tip)) Tips.SetToolTip(b, tip);
            if (click != null) b.Click += (s, e) => Later(click);
            return b;
        }

        void BuildWindow()
        {
            Form f = new Form();
            Form = f;
            using (Graphics g0 = f.CreateGraphics()) Scale = g0.DpiX / 96f;
            Theme.Scale = Scale;
            f.Text = "PixTune";
            f.BackColor = Theme.Back; f.ForeColor = Theme.Text;
            f.Size = new Size(S(1440), S(920)); f.MinimumSize = new Size(S(960), S(640));
            f.StartPosition = FormStartPosition.CenterScreen;
            f.KeyPreview = true; f.AllowDrop = true;
            f.Font = new Font("Segoe UI", 9);
            AppIcon = Theme.AppIcon(64); f.Icon = AppIcon;
            Native.DarkTitleBar(f);

            Canvas = new ImageCanvas();
            Canvas.Dock = DockStyle.Fill;
            Canvas.ShowHistogram = Settings.ShowHistogram;
            Renderer = new Renderer(Doc, f);

            // top bar
            TopBar = new Panel(); TopBar.Dock = DockStyle.Top; TopBar.Height = S(56); TopBar.BackColor = Theme.Panel;
            FlowLayoutPanel topLeft = new FlowLayoutPanel();
            topLeft.Dock = DockStyle.Left; topLeft.AutoSize = true; topLeft.WrapContents = false; topLeft.Padding = new Padding(S(8), S(8), 0, 0);
            FlowLayoutPanel topRight = new FlowLayoutPanel();
            topRight.Dock = DockStyle.Right; topRight.AutoSize = true; topRight.WrapContents = false; topRight.FlowDirection = FlowDirection.RightToLeft;
            topRight.Padding = new Padding(0, S(8), S(8), 0);
            TopBar.Controls.Add(topLeft); TopBar.Controls.Add(topRight);

            BtnOpen = NewTopButton(G(0xE8E5), "OPEN", "Open a photo (Ctrl+O)", OpenImageDialog);
            BtnOpen.Primary = true;
            LblTitle = new Label();
            LblTitle.AutoSize = true; LblTitle.ForeColor = Theme.Dim; LblTitle.Font = new Font("Segoe UI", 10.5f);
            LblTitle.Margin = new Padding(S(10), S(10), 0, 0); LblTitle.Text = "PixTune";
            topLeft.Controls.AddRange(new Control[] { BtnOpen, LblTitle });

            BtnMenu = NewTopButton(G(0xE713), "", "Settings", ShowSettingsDialog);
            BtnExport = NewTopButton(G(0xE74E), "EXPORT", "Save and export", () => ShowHome("export"));
            BtnInfo = NewTopButton(G(0xE946), "", "Photo details", ShowInfo);
            BtnStack = NewTopButton(G(0xE81E), "", "Edit stack (view and change edits)", ShowStack);
            BtnCompare = NewTopButton(G(0xE799), "", "Hold to compare with the original (or hold C)", null);
            BtnRedo = NewTopButton(G(0xE7A6), "", "Redo (Ctrl+Shift+Z)", InvokeRedo);
            BtnUndo = NewTopButton(G(0xE7A7), "", "Undo (Ctrl+Z) - the last 10 operations, also inside a tool", InvokeUndo);
            BtnCompare.Pressed += (s, e) => SetCompare(true, false);
            BtnCompare.Released += (s, e) => SetCompare(false, false);
            Btn100 = NewTopButton(G(0xE71E), "100%", "Actual pixels: 1 photo pixel = 1 screen pixel (Ctrl+1)", () => Canvas.ZoomActual());
            BtnFit = NewTopButton(G(0xE740), "Fit", "Fit the photo in the window (Ctrl+0)", () => Canvas.Fit());
            BtnHist = NewTopButton(G(0xE9D2), "", "Live histogram (H)", SwitchHistogram);
            topRight.Controls.AddRange(new Control[] { BtnMenu, BtnExport, BtnInfo, BtnStack, BtnHist, BtnCompare, Btn100, BtnFit, BtnRedo, BtnUndo });
            TopButtons = new IconButton[] { BtnExport, BtnInfo, BtnStack, BtnHist, BtnCompare, Btn100, BtnFit, BtnRedo, BtnUndo };

            // side panel
            Side = new Panel(); Side.Dock = DockStyle.Right; Side.Width = S(352); Side.BackColor = Theme.Panel;
            SideHead = new FlowLayoutPanel(); SideHead.Dock = DockStyle.Top; SideHead.Height = S(58); SideHead.WrapContents = false;
            SideHead.Padding = new Padding(S(8), S(8), S(8), 0);
            SideFoot = new TableLayoutPanel(); SideFoot.Dock = DockStyle.Bottom; SideFoot.Height = S(60); SideFoot.ColumnCount = 3; SideFoot.RowCount = 1;
            SideFoot.Padding = new Padding(S(8));
            SideFoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            SideFoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            SideFoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            BuildToneDock();
            BuildMaskDock();
            // docking goes from the last added control: the head sits on top, then the curve, the mask, then the foot
            Side.Controls.Add(SideFoot); Side.Controls.Add(MaskDock); Side.Controls.Add(CurveDock); Side.Controls.Add(SideHead);
            Control body = NewBodyPanel();
            Body = body; WorkBody = body; Head = SideHead; Foot = SideFoot;
            FillMaskDock();
            Body = body;
            Panel sep = new Panel(); sep.Dock = DockStyle.Right; sep.Width = 1; sep.BackColor = Theme.Line;

            BtnCancel = NewTopButton(G(0xE711), "", "Cancel (Esc)", CancelTool);
            BtnApply = NewTopButton(G(0xE73E), "", "Apply (Enter)", ApplyTool);
            BtnApply.Primary = true;
            BtnToolCompare = NewTopButton(G(0xE799), "", "Hold to see the photo before this tool", null);
            BtnToolCompare.Pressed += (s, e) => SetCompare(true, true);
            BtnToolCompare.Released += (s, e) => SetCompare(false, false);
            foreach (IconButton b in new IconButton[] { BtnCancel, BtnToolCompare, BtnApply }) { b.Dock = DockStyle.Fill; b.Height = S(44); }
            SideFoot.Controls.Add(BtnCancel, 0, 0); SideFoot.Controls.Add(BtnToolCompare, 1, 0); SideFoot.Controls.Add(BtnApply, 2, 0);

            Panel topLine = new Panel(); topLine.Dock = DockStyle.Top; topLine.Height = 1; topLine.BackColor = Theme.Line;
            f.Controls.Add(Canvas); f.Controls.Add(sep); f.Controls.Add(Side); f.Controls.Add(topLine); f.Controls.Add(TopBar);
            Native.DarkControls(f);

            Renderer.Rendered += (s, e) => Safe(() => OnRendered(e));
            Timer genTimer = new Timer(); genTimer.Interval = 500;
            genTimer.Tick += (s, e) => ShowProgress();
            genTimer.Start();
            Canvas.ViewChanged += (s, e) => Safe(() =>
            {
                UpdateZoomButtons();
                // during a live gesture the frames are screen-sized on purpose; the final render follows its end
                if (LiveDrag || PendingKind == "live") return;
                if (TestNeedFull())
                {
                    // zoomed in: the part now on screen is computed once the view stops moving
                    if (!FullShown && !Canvas.PatchCovers(Canvas.VisibleNorm, Canvas.DisplayScale)) RenderWhenViewSettles();
                }
                else if ((FullShown || Canvas.HasPatch) && PendingKind != "preview") RequestRender();
            });
            WireCanvas();
            f.KeyDown += (s, e) => Safe(() => OnKeyDown(e));
            f.KeyUp += (s, e) => { if (e.KeyCode == Keys.C && !e.Control) SetCompare(false, false); };
            f.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            f.DragDrop += (s, e) => Safe(() =>
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                if (files[0].EndsWith(".pixtune", StringComparison.OrdinalIgnoreCase)) OpenProject(files[0]); else OpenImage(files[0], null);
            });
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            // Closing the window first empties everything kept about the photo: saved masks, the engine's caches (masks,
            // AI results, previews) and the converted RAW copies on disk; only then does PixTune quit.
            f.FormClosing += (s, e) => { try { WaitScreenshots(); ClearSessionData(); } catch (Exception) { } };
        }

        public void ClearSessionData()
        {
            MaskLib.Clear(); MaskLibShown = -1;
            if (Doc != null) Doc.ClearCaches();
            Caches.Clear();
            Caches.DeleteFiles();
            Gpu.Trim();
            GC.Collect(); GC.WaitForPendingFinalizers();
        }

        // ---------------- side panel bodies ----------------
        // The side panel holds one scrolling body per home tab (built once, then just shown) plus a work body that
        // is rebuilt for each tool and for the edit stack.
        Control NewBodyPanel()
        {
            SmoothFlow p = new SmoothFlow();
            p.Dock = DockStyle.Fill; p.FlowDirection = FlowDirection.TopDown; p.WrapContents = false; p.AutoScroll = true;
            p.Padding = new Padding(0, S(4), 0, S(12));
            p.HorizontalScroll.Maximum = 0;
            p.AutoScroll = false;
            p.HorizontalScroll.Visible = false;
            p.AutoScroll = true;
            p.Visible = Bodies.Count == 0;
            Side.Controls.Add(p);
            p.BringToFront(); // fill panels must come before the docked head and foot
            Bodies.Add(p);
            return p;
        }

        public void ShowBody(Control p)
        {
            if (p.Visible && Body == p) return;
            Side.SuspendLayout();
            p.Visible = true;
            foreach (Control b in Bodies) if (b != p) b.Visible = false;
            Side.ResumeLayout();
            p.PerformLayout(); // scroll range follows the content laid out at the real width
            Body = p;
        }

        // A hidden body is not laid out by its parent: give it the size it will have once shown.
        void SetBodyBounds(Control p)
        {
            int top = Head.Height + (CurveDock.Visible ? CurveDock.Height : 0) + (MaskDock.Visible ? MaskDock.Height : 0);
            int bottom = Foot.Visible ? Foot.Height : 0;
            p.Bounds = new Rectangle(0, top, Side.ClientSize.Width, Math.Max(100, Side.ClientSize.Height - top - bottom));
        }

        // Empties a body and makes it the target of the Add* helpers (without showing it yet).
        void ResetBody(Control p)
        {
            Body = p;
            p.SuspendLayout();
            foreach (Control c in new List<Control>(Collect(p.Controls))) { p.Controls.Remove(c); c.Dispose(); }
            p.ResumeLayout();
            ((ScrollableControl)p).AutoScrollPosition = new Point(0, 0);
        }
        static IEnumerable<Control> Collect(Control.ControlCollection cs) { foreach (Control c in cs) yield return c; }

        // Tool panels and the stack are rebuilt in the work body.
        public void ClearBody()
        {
            ResetBody(WorkBody);
            ShowBody(WorkBody);
        }

        // ---------------- tone curve ----------------
        // Tone curve over the live RGB histogram, always visible under the panel title (like the curve panel of pro
        // editors). It edits a curves step kept last in the stack: drag points, click the curve to add one,
        // right-click a point to delete it.
        void BuildToneDock()
        {
            CurveDock = new Panel(); CurveDock.Dock = DockStyle.Top; CurveDock.Height = S(188); CurveDock.BackColor = Theme.Panel;
            Label toneLabel = new Label();
            toneLabel.Text = "TONE CURVE"; toneLabel.ForeColor = Theme.Dim; toneLabel.Font = Ui.SectionFont;
            toneLabel.AutoSize = true; toneLabel.Location = new Point(S(14), S(6));
            ToneReset = Ui.Button(G(0xE72C), "Reset");
            ToneReset.Size = new Size(S(84), S(26));
            Tips.SetToolTip(ToneReset, "Straighten the tone curve");
            ToneEd = new CurveEditor();
            ToneEd.AddOnCurve = true; ToneEd.PointsString = ToneDefault;
            Tips.SetToolTip(ToneEd, "Drag a point to change the tones. Click the curve to add a point, right-click a point to remove it.");
            Panel toneLine = new Panel(); toneLine.Dock = DockStyle.Bottom; toneLine.Height = 1; toneLine.BackColor = Theme.Line;
            CurveDock.Controls.AddRange(new Control[] { toneLabel, ToneReset, ToneEd, toneLine });
            // placed from the panel's real width (known only once it is docked)
            CurveDock.Resize += (s, e) =>
            {
                int w = CurveDock.ClientSize.Width;
                ToneReset.Location = new Point(w - ToneReset.Width - S(10), S(1));
                ToneEd.Bounds = new Rectangle(S(10), S(28), w - S(20), CurveDock.ClientSize.Height - S(36));
            };
            ToneEd.CurveChanged += (s, e) => Safe(() =>
            {
                if (!Doc.HasImage) return;
                // one undo step per gesture: the history is saved when a drag starts
                Doc.SetToneCurve(ToneEd.PointsString, !ToneEditing);
                ToneEditing = !e.Final;
                Step tc = Doc.ToneCurve; ToneSig = tc != null ? tc.Str("curve_rgb", "") : "";
                RequestRender();
                if (e.Final) { UpdateTopButtons(); if (Mode == "stack") ShowStack(); }
            });
            ToneReset.Click += (s, e) => Safe(() =>
            {
                if (!Doc.HasImage || Doc.ToneCurve == null) { ToneEd.PointsString = ToneDefault; return; }
                Doc.SetToneCurve(ToneDefault, true); ToneSig = ""; ToneEd.PointsString = ToneDefault;
                RequestRender(); UpdateTopButtons(); if (Mode == "stack") ShowStack();
            });
        }

        // follows the document after undo, redo, opening a photo or editing the stack
        void SyncToneCurve()
        {
            if (ToneEd.Dragging) return;
            Step tc = Doc.ToneCurve;
            string cur = tc != null ? tc.Str("curve_rgb", "") : "";
            if (cur != ToneSig) { ToneSig = cur; ToneEd.PointsString = cur.Length > 0 ? cur : ToneDefault; }
        }

        // From the frame's bitmap rather than the picture: the same values (ImageIO.Histogram samples the same pixels
        // and rounds the same way), and a picture kept on the graphics card does not have to come back for it.
        void UpdateToneHistogram(Bitmap b)
        {
            if (b == null) return;
            int w = b.Width, h = b.Height, n = w * h;
            int[] px = new int[n];
            System.Drawing.Imaging.BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, n); }
            finally { b.UnlockBits(bd); }
            int[][] hst = { new int[256], new int[256], new int[256] };
            int step = Math.Max(1, n / 400000);
            for (int i = 0; i < n; i += step)
            {
                int v = px[i];
                hst[0][(v >> 16) & 255]++; hst[1][(v >> 8) & 255]++; hst[2][v & 255]++;
            }
            ToneEd.ChannelHistograms = hst;
            ToneEd.Invalidate();
        }

        // ---------------- rendering ----------------
        // When zoomed in beyond the preview's detail, the canvas shows a full resolution render instead, so 100%
        // really means one photo pixel per screen pixel.
        bool TestNeedFull()
        {
            if (!Doc.HasImage || Canvas.IsFit) return false;
            return Canvas.DisplayScale > (Doc.Preview.W / (double)Doc.Original.W) * 1.05;
        }

        // live (while painting): the frame being rendered is finished and shown, then the newest state follows
        public void RequestRender(bool live = false)
        {
            if (!Doc.HasImage) return;
            Canvas.Busy = true;
            if (live || LiveDrag)
            {
                // a slider moving, a stroke being painted: only what the screen shows, at screen resolution
                PendingKind = "live";
                LiveView v = new LiveView();
                v.Visible = Canvas.VisibleNorm; v.DisplayScale = Canvas.DisplayScale; v.Full = TestNeedFull(); v.Quality = LiveQuality;
                Renderer.RequestLiveView(v);
                return;
            }
            if (TestNeedFull())
            {
                // zoomed in past the preview: the preview with every edit, and the part on screen at full quality
                // (the whole photo at full resolution is only made for exports)
                PendingKind = "zoomed";
                LiveView z = new LiveView();
                z.Visible = Canvas.VisibleNorm; z.DisplayScale = Canvas.DisplayScale; z.Full = true;
                z.Ahead = 0.2;   // a little around the view: small pans and zooming out show pixels already made
                Renderer.RequestZoomed(z);
                return;
            }
            PendingKind = "preview";
            Renderer.Request();
        }

        // The photo's long side on screen at Fit: a bigger preview would only compute pixels the screen cannot show
        // (zooming in past it shows the part on screen at full resolution instead).
        int FitPreviewSide(int ow, int oh)
        {
            int cw = Canvas.ClientSize.Width - 32, ch = Canvas.ClientSize.Height - 32;
            if (cw < 200 || ch < 200 || ow <= 0 || oh <= 0) return int.MaxValue;   // not laid out yet: the setting decides
            double k = Math.Min(cw / (double)ow, ch / (double)oh);
            return Math.Max(800, (int)Math.Ceiling(Math.Max(ow, oh) * k));
        }

        // after the window is resized: a preview more than 12% away from the new size is made again
        void PreviewFollowsWindow()
        {
            if (!Doc.HasImage || LiveDrag) return;
            int want = Math.Min(Doc.PreviewSize, FitPreviewSide(Doc.Original.W, Doc.Original.H));
            int now = Math.Max(Doc.Preview.W, Doc.Preview.H), full = Math.Max(Doc.Original.W, Doc.Original.H);
            if (Math.Min(want, full) == now || Math.Abs(Math.Min(want, full) - now) < now * 0.12) return;
            if (Doc.RefreshPreview()) { InputCache = null; RequestRender(); }
        }

        // While a slider moves, each frame should take about 70 ms so the photo follows the hand. On a slow computer
        // the frames are computed at a lower resolution (down to a third), on a fast one back up to full; letting go
        // always renders at full quality.
        public double LiveQuality = 1;
        public bool LiveAdaptive = true;   // tests hold the quality still to check sizes
        public static double LiveTargetMs = 70;
        void AdaptLiveQuality(long ms)
        {
            if (ms <= 0 || !LiveAdaptive) return;
            if (ms > LiveTargetMs * 1.3) LiveQuality = Math.Max(0.33, LiveQuality * Math.Sqrt(LiveTargetMs / ms));
            else if (ms < LiveTargetMs * 0.6) LiveQuality = Math.Min(1, LiveQuality * 1.15);
        }

        // panning or zooming: one render once the view has stopped moving for a moment
        System.Windows.Forms.Timer viewTimer;
        public bool ViewRenderPending { get { return viewTimer != null && viewTimer.Enabled; } }
        void RenderWhenViewSettles()
        {
            if (viewTimer == null)
            {
                viewTimer = new System.Windows.Forms.Timer(); viewTimer.Interval = 120;
                viewTimer.Tick += (s, e) => Safe(() =>
                {
                    viewTimer.Stop();
                    if (!LiveDrag && TestNeedFull() && !Canvas.PatchCovers(Canvas.VisibleNorm, Canvas.DisplayScale)) RequestRender();
                });
            }
            viewTimer.Stop(); viewTimer.Start();
        }

        void OnRendered(RenderedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Error)) SetStatus("Render error: " + e.Error);
            else if (!GpuAnnounced && (Gpu.Ready || !Gpu.Enabled))
            {
                GpuAnnounced = true;
                SetStatus("Processing on " + GpuStatus());
            }
            PendingKind = null;
            if (e.Live) AdaptLiveQuality(e.Ms);
            if (e.Zoomed && e.Bitmap != null)
            {
                // the preview under the view, the part on screen at full quality over it
                Canvas.SourceWidth = (int)Math.Round(e.Bitmap.Width * Doc.Original.W / (double)Doc.Preview.W);
                FullShown = false;
                Canvas.SetImage(e.Bitmap);
                if (e.PartBitmap != null) Canvas.SetPatch(e.PartBitmap, e.Patch, e.ToOriginal);
                Canvas.Busy = false;
                Canvas.Invalidate();
                UpdateToneHistogram(e.Bitmap);
                SyncToneCurve();
                if (Mode == "tool" && ToolDef != null && ToolDef.Id == "crop") UpdateCropHud();
                UpdateToolHistory();
                if (PrepareAfterRender != null) PrepareTool();
                return;
            }
            if (e.Live && e.Bitmap != null)
            {
                if (e.Patch.IsEmpty)
                {
                    // the whole photo at screen size (or at preview size when zoomed in on a tool that needs it whole)
                    Canvas.SourceWidth = (int)Math.Round(e.Bitmap.Width / e.ToOriginal);
                    FullShown = false;
                    Canvas.SetImage(e.Bitmap);
                    UpdateToneHistogram(e.Bitmap);
                }
                else Canvas.SetPatch(e.Bitmap, e.Patch, e.ToOriginal);
                Canvas.Busy = false;
                Canvas.Invalidate();
                return;
            }
            if (e.Bitmap != null)
            {
                // width of this picture at full resolution, so the zoom readout counts photo pixels
                Canvas.SourceWidth = e.Full ? e.Bitmap.Width : (int)Math.Round(e.Bitmap.Width * Doc.Original.W / (double)Doc.Preview.W);
                FullShown = e.Full;
                Canvas.SetImage(e.Bitmap);
            }
            Canvas.Busy = false;
            Canvas.Invalidate();
            if (!e.Full) UpdateToneHistogram(e.Bitmap);
            SyncToneCurve();
            if (Mode == "tool" && ToolDef != null && ToolDef.Id == "crop") UpdateCropHud();
            UpdateToolHistory();
            if (PrepareAfterRender != null) PrepareTool();
        }

        // long AI work (generative fill, denoise, masks, head pose) says how far it is while the canvas waits
        void ShowProgress()
        {
            float p = Diffusion.Progress;
            string what = !string.IsNullOrEmpty(Diffusion.Status) ? Diffusion.Status : "Generating the extension";
            // saying where it is running answers the question the wait raises: the processor took over because
            // the graphics card could not be trusted with the tiles, and it is much slower
            if (Denoiser.Progress > 0) { p = Denoiser.Progress; what = Denoiser.OnCpu > 0 ? "Removing noise on the processor" : "Removing noise"; }
            string seg = Segmenter.Working;
            if (LivePortrait.Working) seg = "Redrawing the face";
            if (!string.IsNullOrEmpty(SubjectPicker.Working)) seg = SubjectPicker.Working;
            // the depth of a photo is worked out once and then kept, so this shows on the first use only
            if (!string.IsNullOrEmpty(Depther.Working)) seg = Depther.Working;
            string msg = null;
            if (!string.IsNullOrEmpty(seg) && Canvas.Busy) msg = seg + "...";
            if (Canvas.Busy && p > 0) msg = string.Format("{0}... {1}%", what, (int)(100 * p));
            // The status line drops whatever it is given after seven seconds, so a message about work that is over
            // used to sit there through the next zoom or slider move, and reading "Finding the subject..." again
            // looks like finding it again. It comes down with the work instead.
            if (msg != null) { if (Canvas.Status != msg) SetStatus(msg); workShown = msg; }
            else if (workShown != null) { if (Canvas.Status == workShown) SetStatus(""); workShown = null; }
        }
        string workShown;   // the message this put up, so it can take it down again

        void UpdateZoomButtons()
        {
            bool at100 = !Canvas.IsFit && Math.Abs(Canvas.DisplayScale - 1) < 0.005;
            Btn100.Toggled = at100; BtnFit.Toggled = Canvas.IsFit;
            Btn100.Invalidate(); BtnFit.Invalidate();
        }

        public void SwitchHistogram()
        {
            Settings.ShowHistogram = !Settings.ShowHistogram;
            Canvas.ShowHistogram = Settings.ShowHistogram;
            BtnHist.Toggled = Settings.ShowHistogram; BtnHist.Invalidate();
            Settings.Save();
        }

        public void SetCompare(bool on, bool beforeTool)
        {
            if (!Doc.HasImage) return;
            if (on)
            {
                bool full = FullShown;
                if (beforeTool && Mode == "tool")
                {
                    Img im = full ? Doc.RenderFullCached(Doc.StepsBeforePending(), System.Threading.CancellationToken.None) : ToolInput();
                    Canvas.SetCompare(im.ToBitmap());
                }
                // zoomed in, the original at full resolution (no render needed) keeps the comparison sharp
                else Canvas.SetCompare(full || TestNeedFull() ? Doc.Original.ToBitmap() : Doc.Preview.ToBitmap());
            }
            Canvas.ShowCompare = on;
            Canvas.Invalidate();
        }

        // ---------------- canvas gestures ----------------
        void WireCanvas()
        {
            ImageCanvas c = Canvas;
            // working on the photo with the tool's own gestures puts the mask back out of sight
            c.Pressing += (s, e) => Safe(() => { if (Mode == "tool" && MaskPaint < 0) HideMaskEdit(); });
            c.ValueDrag += (s, e) => Safe(() =>
            {
                if (Mode != "tool" || ParamKeys.Count == 0) return;
                if (e.Final)
                {
                    // the drag is over: the slider's last value, rendered in full
                    bool was = Dragging; Dragging = false;
                    if (was) Sliders[ParamKeys[Active]].SetValue((float)DragVal, true);
                    return;
                }
                ParamSlider sl = Sliders[ParamKeys[Active]];
                if (!sl.Enabled) return;
                if (!Dragging) { Dragging = true; DragVal = sl.Value; }
                DragVal = Math.Max(sl.Min, Math.Min(sl.Max, DragVal + e.Value * (sl.Max - sl.Min) / S(600)));
                sl.SetValue((float)DragVal, false);
            });
            c.ParamCycle += (s, e) => Safe(() =>
            {
                if (Mode != "tool" || ParamKeys.Count == 0) return;
                int i = Math.Max(0, Math.Min(ParamKeys.Count - 1, Active + (int)e.Value));
                SetActive(ParamKeys[i]);
                Dragging = false;
            });
            c.CenterMoved += (s, e) => Safe(() =>
            {
                if (Mode != "tool") return;
                Step.Set("cx", e.P.X); Step.Set("cy", e.P.Y);
                if (e.Final) EndLive(false); else BeginLive();
                RequestRender();
            });
            c.EllipseChanged += (s, e) => Safe(() =>
            {
                if (Mode != "tool" || ToolDef.OnEllipse == null) return;
                if (e.Final) EndLive(false); else BeginLive();
                ToolDef.OnEllipse();
            });
            c.CropChanged += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnCrop != null) ToolDef.OnCrop(); if (e.Final) UpdateToolHistory(); });
            c.StrokeChanged += (s, e) => Safe(() =>
            {
                if (Mode != "tool") return;
                if (MaskPaint >= 0)
                {
                    if (e.Final) { SetMaskChanged(); UpdateMaskView(); SyncMaskDock(); RequestRender(); } else RequestRender(true);
                    return;
                }
                if (ToolDef.RenderOnStrokeEnd && !e.Final) return;
                if (ShowMask && e.Final) UpdateBrushMask();
                RequestRender(!e.Final);
            });
            c.QuadChanged += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnQuad != null) ToolDef.OnQuad(e.Index, e.P); });
            c.Picked += (s, e) => Safe(() =>
            {
                if (Mode == "tool" && MaskPaint == 2) { AddMaskPick(e.P); return; }
                if (Mode == "tool" && ToolDef.OnPicked != null) ToolDef.OnPicked(e.P);
            });
            c.MoveDrag += (s, e) => Safe(() => { if (Mode == "tool" && !e.Final && ToolDef.OnMove != null) ToolDef.OnMove(e.P); });
            c.ImageClick += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnClick != null) ToolDef.OnClick(e.P); });
            c.ImageDoubleClick += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnDoubleClick != null) ToolDef.OnDoubleClick(); });
            c.PointAdded += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnPointAdded != null) ToolDef.OnPointAdded(e.P); });
            c.PointSelected += (s, e) => Safe(() => { if (Mode == "tool" && ToolDef.OnPointSelected != null) ToolDef.OnPointSelected(); });
            c.PointMoved += (s, e) => Safe(() =>
            {
                if (Mode != "tool" || ToolDef.OnPointMoved == null) return;
                if (e.Final) EndLive(false); else BeginLive();
                ToolDef.OnPointMoved(e.Index, e.P, e.Final);
            });
            c.PointResized += (s, e) => Safe(() =>
            {
                if (Mode != "tool" || ToolDef.OnPointResized == null) return;
                if (e.Final) EndLive(false); else BeginLive();
                ToolDef.OnPointResized(e.Index, e.P, e.Value, e.Final);
            });
        }

        // ---------------- keyboard ----------------
        void OnKeyDown(KeyEventArgs e)
        {
            bool inText = Form.ActiveControl is TextBox || Form.ActiveControl is ComboBox;
            if (e.Control)
            {
                switch (e.KeyCode)
                {
                    case Keys.O: OpenImageDialog(); e.Handled = true; break;
                    case Keys.S: if (e.Shift) ExportAs(); else SaveCopy(); e.Handled = true; break;
                    case Keys.E: ExportToFolder(); e.Handled = true; break;
                    case Keys.Z: if (!inText) { if (e.Shift) InvokeRedo(); else InvokeUndo(); e.Handled = true; } break;
                    case Keys.Y: if (!inText) { InvokeRedo(); e.Handled = true; } break;
                    case Keys.D0: Canvas.Fit(); e.Handled = true; break;
                    case Keys.D1: Canvas.ZoomActual(); e.Handled = true; break;
                }
                return;
            }
            if (inText) { if (e.KeyCode == Keys.Escape) Canvas.Focus(); return; }
            switch (e.KeyCode)
            {
                case Keys.Enter: ApplyTool(); e.Handled = true; break;
                case Keys.Escape: if (Mode == "tool") CancelTool(); else if (Mode == "stack") ShowHome(null); e.Handled = true; break;
                case Keys.C: SetCompare(true, false); e.Handled = true; break;
                case Keys.H: if (Doc.HasImage) SwitchHistogram(); e.Handled = true; break;
                case Keys.Up: if (Mode == "tool" && ParamKeys.Count > 0) { SetActive(ParamKeys[Math.Max(0, Active - 1)]); e.Handled = true; } break;
                case Keys.Down: if (Mode == "tool" && ParamKeys.Count > 0) { SetActive(ParamKeys[Math.Min(ParamKeys.Count - 1, Active + 1)]); e.Handled = true; } break;
            }
        }

        // ---------------- header ----------------
        // The header keeps the same controls (home tabs, Back button, title) and only switches which are visible:
        // recreating them forced the whole side panel to lay out again on every change.
        void SetHeadTabs()
        {
            if (HomeTabs == null)
            {
                string[][] tabs = { new[] { "looks", "LOOKS" }, new[] { "tools", "TOOLS" }, new[] { "export", "EXPORT" } };
                int[] glyphs = { 0xE790, 0xE9E9, 0xE74E };
                HomeTabs = new IconButton[3];
                for (int i = 0; i < 3; i++)
                {
                    string name = tabs[i][0];
                    IconButton b = NewTopButton(G(glyphs[i]), tabs[i][1], tabs[i][1], () => ShowHome(name));
                    b.Name = name; b.Width = S(108);
                    HomeTabs[i] = b;
                }
                HeadBack = NewTopButton(G(0xE72B), "Back", "Back to tools", () => ShowHome(null));
                HeadBack.Width = S(90);
                HeadTitle = new Label();
                HeadTitle.AutoSize = true; HeadTitle.Font = new Font("Segoe UI Semibold", 13); HeadTitle.ForeColor = Theme.Text;
                HeadTitle.Margin = new Padding(S(6), S(6), 0, 0);
                HeadTitle.UseMnemonic = false; // show "&" in names such as "Crop & Rotate"
                Head.Controls.AddRange(HomeTabs);
                Head.Controls.Add(HeadBack); Head.Controls.Add(HeadTitle);
            }
            Side.SuspendLayout();
            foreach (IconButton b in HomeTabs) { b.Visible = true; b.Toggled = b.Name == Tab; b.Invalidate(); }
            HeadBack.Visible = false; HeadTitle.Visible = false;
            Side.ResumeLayout();
        }

        public void SetHeadTitle(string text, bool back = false)
        {
            if (HomeTabs == null) SetHeadTabs();
            Side.SuspendLayout();
            foreach (IconButton b in HomeTabs) b.Visible = false;
            HeadBack.Visible = back;
            HeadTitle.Text = text; HeadTitle.Visible = true;
            Side.ResumeLayout();
        }

        public void UpdateTopButtons()
        {
            bool has = Doc.HasImage, notTool = Mode != "tool";
            // inside a tool, undo and redo walk through that tool's own changes
            BtnUndo.Enabled = has && (notTool ? Doc.CanUndo : ToolUndo.Count > 0);
            BtnRedo.Enabled = has && (notTool ? Doc.CanRedo : ToolRedo.Count > 0);
            foreach (IconButton b in new IconButton[] { BtnExport, BtnInfo, BtnCompare, Btn100, BtnFit, BtnHist }) b.Enabled = has;
            BtnStack.Enabled = has && notTool;
            BtnStack.Text = has && Doc.Steps.Count > 0 ? Doc.Steps.Count.ToString() : "";
            BtnStack.Width = BtnStack.Text.Length > 0 ? S(64) : S(44);
            foreach (IconButton b in TopButtons) b.Invalidate();
        }

        // Looks on GitHub for a newer PixTune. Quiet when it finds nothing, and never installs on its own:
        // it offers, and the owner says yes. Asked for from Settings (loud = true) it always says what it found.
        public void LookForUpdate(bool loud)
        {
            if (Updater.Checking || Updater.Installing) return;
            if (!loud)
            {
                // once a day is enough, and a version already turned down is not offered again
                DateTime last;
                if (DateTime.TryParse(Settings.UpdateChecked, out last) && (DateTime.Now - last).TotalHours < 24) return;
            }
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                bool found = Updater.Check();
                OnUi(() =>
                {
                    Settings.UpdateChecked = DateTime.Now.ToString("o"); Settings.Save();
                    if (!found)
                    {
                        if (!loud) return;
                        SetStatus(Updater.LastError.Length > 0
                            ? "Could not look for an update: " + Updater.LastError
                            : "PixTune " + Updater.Current + " is the newest version.");
                        return;
                    }
                    if (!loud && Settings.UpdateSkipped == Updater.Latest) return;
                    OfferUpdate();
                });
            });
        }

        void OfferUpdate()
        {
            string notes = Updater.Notes.Length > 220 ? Updater.Notes.Substring(0, 220) + "..." : Updater.Notes;
            string text = "PixTune " + Updater.Latest + " is available. This copy is " + Updater.Current + "."
                + (notes.Length > 0 ? "\r\n\r\n" + notes : "")
                + "\r\n\r\nInstall it now? PixTune will close and start again, and the version you have is kept"
                + "\r\nso it can be put back.";
            DialogResult r = MessageBox.Show(Form, text, "A newer PixTune", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { Settings.UpdateSkipped = Updater.Latest; Settings.Save(); return; }
            if (Doc != null && Doc.HasImage && Doc.Steps.Count > 0
                && MessageBox.Show(Form, "The edits on this photo are not saved. Install anyway?", "A newer PixTune",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SetStatus("Installing PixTune " + Updater.Latest + "...");
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                string backup = Updater.Install(App.Root);
                OnUi(() =>
                {
                    if (backup == null) { SetStatus("The update could not be installed: " + Updater.LastError); return; }
                    MessageBox.Show(Form, "PixTune " + Updater.Latest + " is in place. It will start again now.\r\n\r\n"
                        + "The version you had is kept in:\r\n" + backup, "A newer PixTune",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Restart();
                });
            });
        }

        // The new copy is told which process this is and waits for it to be gone before it loads anything, so
        // the updated PixTune replaces this one instead of standing beside it. Starting it first and closing
        // afterwards is the only order available - a closed program cannot start anything - so the waiting is
        // done at the other end.
        void Restart()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.Combine(App.Root, "PixTune.bat"),
                    Arguments = "-AfterPid " + System.Diagnostics.Process.GetCurrentProcess().Id,
                    WorkingDirectory = App.Root,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { SetStatus("Could not start again: " + ex.Message); return; }
            ShutForRestart();
        }

        // Settings, which is where the update is usually asked for, is a window shown over the main one with a
        // message loop of its own. While it is up, Close on the main window does nothing at all - it returns,
        // and the window stays - which is how an update left the old PixTune on screen beside the new one. So
        // the windows over it are shut first, and the main one is asked to close once their loops have unwound.
        void ShutForRestart()
        {
            CloseWindowsOver();
            Form.BeginInvoke((MethodInvoker)delegate { try { Form.Close(); } catch (Exception) { } });
            // and if something still holds the window after that, this copy goes anyway: it has been replaced,
            // and leaving two PixTunes running is worse than leaving a temporary file behind
            Timer go = new Timer();
            go.Interval = 4000;
            go.Tick += (s, e) => { go.Stop(); go.Dispose(); Environment.Exit(0); };
            go.Start();
        }

        // the windows standing over the main one, newest first
        void CloseWindowsOver()
        {
            List<Form> others = new List<Form>();
            foreach (Form f in Application.OpenForms) if (f != Form) others.Add(f);
            for (int i = others.Count - 1; i >= 0; i--) try { others[i].Close(); } catch (Exception) { }
        }

        // At launch, once the window has been idle for two seconds: installed AI models not checked yet on this
        // graphics card are tried on it in the background (usually nothing to do after the first launch).
        void CheckGpuWhenIdle()
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 500;
            int idle = 0;
            t.Tick += (s, e) =>
            {
                idle = Idle ? idle + 1 : 0;
                if (idle < 4) return;
                t.Stop(); t.Dispose();
                if (Settings.UseGpu && Gpu.Ready && GpuCheck.Pending) RunGpuCheck(false);
            };
            t.Start();
        }

        // Runs the GPU check in the background (all: check every model again); says how it goes in the status line.
        public bool RunGpuCheck(bool all)
        {
            return GpuCheck.Start(all, null,
                p => OnUi(() => SetStatus("Checking the AI models on the graphics card: " + p + "...")),
                done => OnUi(() => { if (done.Length > 0) SetStatus(done); }));
        }

        public string GpuStatus()
        {
            if (!Gpu.Enabled) return "CPU (GPU acceleration off)";
            if (Gpu.Available) return "GPU: " + Gpu.Adapter;
            return "CPU (no usable GPU" + (string.IsNullOrEmpty(Gpu.LastError) ? "" : " - " + Gpu.LastError) + ")";
        }
    }
}

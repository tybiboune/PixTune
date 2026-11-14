using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace PixTune
{
    public static class Theme
    {
        public static Color Back = Color.FromArgb(18, 18, 20);
        public static Color Panel = Color.FromArgb(30, 31, 34);
        public static Color Panel2 = Color.FromArgb(40, 41, 45);
        public static Color Line = Color.FromArgb(58, 60, 66);
        public static Color Text = Color.FromArgb(232, 234, 237);
        public static Color Dim = Color.FromArgb(150, 154, 160);
        public static Color Accent = Color.FromArgb(76, 175, 120);
        public static Color AccentDim = Color.FromArgb(46, 105, 72);
        public static bool IsLight;

        // Light appearance (chosen in Settings, applied when the window is created).
        public static void UseLight()
        {
            IsLight = true;
            Back = Color.FromArgb(228, 229, 232);
            Panel = Color.FromArgb(248, 248, 250);
            Panel2 = Color.FromArgb(236, 237, 240);
            Line = Color.FromArgb(208, 210, 215);
            Text = Color.FromArgb(28, 30, 34);
            Dim = Color.FromArgb(104, 108, 116);
            Accent = Color.FromArgb(40, 150, 92);
            AccentDim = Color.FromArgb(56, 160, 104);
        }
        // Windows 11 ships "Segoe Fluent Icons", Windows 10 only "Segoe MDL2 Assets" (same code points).
        // A missing font would silently turn every icon into an empty box, so pick one that is installed.
        public static string IconFont = PickIconFont();

        static string PickIconFont()
        {
            string forced = Environment.GetEnvironmentVariable("PIXTUNE_ICONFONT");
            if (!string.IsNullOrEmpty(forced)) return forced;
            try { using (FontFamily f = new FontFamily("Segoe Fluent Icons")) return f.Name; }
            catch (ArgumentException) { return "Segoe MDL2 Assets"; } // not installed (Windows 10)
        }

        public static bool LegacyIcons { get { return IconFont != "Segoe Fluent Icons"; } }

        public static float Scale = 1f; // display DPI / 96

        public static Font Icon(float size)
        {
            return new Font(IconFont, size * Scale, GraphicsUnit.Pixel);
        }

        // App icon: rounded green tile with the tuning-sliders symbol.
        public static System.Drawing.Icon AppIcon(int size)
        {
            using (Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    RectangleF r = new RectangleF(size * 0.04f, size * 0.04f, size * 0.92f, size * 0.92f);
                    using (GraphicsPath p = ImageCanvas.Rounded(r, size * 0.22f))
                    using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.FromArgb(104, 204, 140), Color.FromArgb(30, 120, 110), 45f))
                        g.FillPath(lb, p);
                    using (Font f = new Font(IconFont, size * 0.56f, GraphicsUnit.Pixel))
                    {
                        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                        g.DrawString("", f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
                    }
                }
                return System.Drawing.Icon.FromHandle(b.GetHicon());
            }
        }
    }

    public static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Dark title bar (Windows 10 2004+ / 11) with the caption tinted to the app's panel colour (Windows 11).
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = Theme.IsLight ? 0 : 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = ColorTranslator.ToWin32(Theme.Panel), text = ColorTranslator.ToWin32(Theme.Text), border = ColorTranslator.ToWin32(Theme.Line);
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(f.Handle, 36, ref text, 4);
                DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
            }
            catch { }
        }

        // Dark scrollbars and list/combo chrome for standard controls.
        public static void DarkControls(Control root)
        {
            try
            {
                // only standard controls with system-drawn parts need the dark theme;
                // custom-drawn tiles, buttons and sliders are skipped to keep panel building fast
                if (Theme.IsLight) return; // the standard light chrome already matches
                bool themed = root is ScrollableControl || root is ListView || root is ComboBox || root is TextBoxBase || root is NumericUpDown;
                if (themed && !(root is Form))
                {
                    string theme = root is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer";
                    if (root.IsHandleCreated) SetWindowTheme(root.Handle, theme, null);
                    else root.HandleCreated += (s, e) => SetWindowTheme(((Control)s).Handle, theme, null);
                }
                if (root is Tile || root is IconButton || root is ParamSlider || root is ImageCanvas) return;
                foreach (Control c in root.Controls) DarkControls(c);
                root.ControlAdded += (s, e) => DarkControls(e.Control);
            }
            catch { }
        }
    }

    // Side panel container that paints off-screen as one surface (no flicker while its tiles are drawn).
    public class SmoothFlow : FlowLayoutPanel
    {
        public SmoothFlow()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }
    }

    // Tag carried by tiles and stack actions: script handlers read .Index and .Data.
    public class TileTag { public int Index; public object Data; }

    // A wrapping group of tiles with single-selection and one click event for the whole group.
    public class TileGroup : FlowLayoutPanel
    {
        public object Handler;  // the script block run on click (kept here for the script side)
        public event EventHandler<CanvasEventArgs> TileClicked;
        public event EventHandler<CanvasEventArgs> TileRightClicked;

        public TileGroup() { WrapContents = true; AutoSize = true; }

        public Tile Add(string caption, string glyph, Bitmap thumb, Color swatch, object data, int w, int h, bool selected)
        {
            Tile t = new Tile();
            t.Size = new Size(w, h);
            t.Margin = new Padding(Ui.Px(2));
            t.Caption = caption ?? ""; t.Glyph = glyph ?? ""; t.Thumb = thumb; t.Swatch = swatch;
            t.Selected = selected;
            TileTag tag = new TileTag(); tag.Index = Controls.Count; tag.Data = data;
            t.Tag = tag;
            t.Click += (s, e) =>
            {
                MouseEventArgs me0 = e as MouseEventArgs;
                if (me0 != null && me0.Button == MouseButtons.Right)
                {
                    EventHandler<CanvasEventArgs> rh = TileRightClicked;
                    if (rh != null) rh(this, new CanvasEventArgs(PointF.Empty, 0, ((TileTag)((Tile)s).Tag).Index, true));
                    return;
                }
                foreach (Control c in Controls) { Tile o = c as Tile; if (o != null && o.Selected) { o.Selected = false; o.Invalidate(); } }
                Tile me = (Tile)s; me.Selected = true; me.Invalidate();
                EventHandler<CanvasEventArgs> handler = TileClicked;
                if (handler != null) handler(this, new CanvasEventArgs(PointF.Empty, 0, ((TileTag)me.Tag).Index, true));
            };
            Controls.Add(t);
            return t;
        }

        public void ClearSelection()
        {
            foreach (Control c in Controls) { Tile o = c as Tile; if (o != null && o.Selected) { o.Selected = false; o.Invalidate(); } }
        }
    }

    // A wrapping row of text buttons with one click event; toggle buttons flip themselves.
    public class ButtonRow : FlowLayoutPanel
    {
        public object Specs;    // the script-side button specs, indexed like the buttons
        public event EventHandler<CanvasEventArgs> ButtonClicked;
        public ButtonRow() { WrapContents = true; AutoSize = true; }

        public IconButton Add(string glyph, string text, bool toggle, bool toggled)
        {
            IconButton b = Ui.Button(glyph, text);
            b.Toggled = toggled;
            int idx = Controls.Count;
            b.Click += (s, e) =>
            {
                if (toggle) { b.Toggled = !b.Toggled; b.Invalidate(); }
                EventHandler<CanvasEventArgs> h = ButtonClicked;
                if (h != null) h(this, new CanvasEventArgs(PointF.Empty, 0, idx, true));
            };
            Controls.Add(b);
            return b;
        }
    }

    // One edit-stack entry drawn as a single control: tool icon, title and its action buttons.
    public class StackRow : Control
    {
        public string Glyph = "", Title = "";
        public bool CanBrush = true, Dim;
        public bool Faded;         // a step switched off (hidden): its icon and title greyed, its buttons still work
        public int Index;
        // Index = row, Value = the button's number (0 = the leftmost); Glyphs.Length when the row itself is clicked
        // (only with RowClick)
        public event EventHandler<CanvasEventArgs> Action;
        public bool RowClick;      // the title area is a button too
        public bool Selected;      // drawn with the accent outline (the row in use)
        public int Lit = -1;       // a button shown switched on
        public string[] Glyphs;    // the button icons, left to right (the edit stack's by default)
        public int BrushButton = 1;  // the button that needs CanBrush
        int hot = -1;
        static readonly string[] glyphs = { "", "", "" };

        public StackRow()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel2;
            Glyphs = glyphs;
        }

        RectangleF Btn(int i)
        {
            float s = Ui.Px(Glyphs.Length > 3 ? 36 : 40);
            return new RectangleF(Width - (Glyphs.Length - i) * (s + Ui.Px(4)) - Ui.Px(6), (Height - s) / 2, s, s);
        }
        bool CanUse(int i) { return !Dim && (i != BrushButton || CanBrush || RowClick); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Dim ? Theme.Panel : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font fi = Theme.Icon(18))
            using (SolidBrush b = new SolidBrush(Dim || Faded ? Theme.Dim : Theme.Accent))
            {
                SizeF s = g.MeasureString(Glyph, fi);
                g.DrawString(Glyph, fi, b, (Ui.Px(42) - s.Width) / 2, (Height - s.Height) / 2 + 1);
            }
            using (SolidBrush b = new SolidBrush(Dim || Faded ? Theme.Dim : Theme.Text))
            {
                SizeF s = g.MeasureString(Title, Ui.TextFont);
                StringFormat sf = new StringFormat(); sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags = StringFormatFlags.NoWrap;
                float maxW = Dim ? Width - Ui.Px(50) : Btn(0).X - Ui.Px(46);
                g.DrawString(Title, Ui.TextFont, b, new RectangleF(Ui.Px(42), (Height - s.Height) / 2, maxW, s.Height + 2), sf);
            }
            if (Selected) using (Pen pen = new Pen(Theme.Accent, 2)) g.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
            if (Dim) return;
            using (Font fi = Theme.Icon(17))
                for (int i = 0; i < Glyphs.Length; i++)
                {
                    RectangleF r = Btn(i);
                    if ((i == hot && CanUse(i)) || i == Lit)
                        using (GraphicsPath p = ImageCanvas.Rounded(r, Ui.Px(8))) using (SolidBrush hb = new SolidBrush(i == Lit ? Theme.AccentDim : Theme.Line)) g.FillPath(hb, p);
                    using (SolidBrush b = new SolidBrush(i == Lit ? Color.White : CanUse(i) ? Theme.Text : Color.FromArgb(80, 84, 90)))
                    {
                        SizeF s = g.MeasureString(Glyphs[i], fi);
                        g.DrawString(Glyphs[i], fi, b, r.X + (r.Width - s.Width) / 2, r.Y + (r.Height - s.Height) / 2 + 1);
                    }
                }
        }

        int HitTest(Point p) { for (int i = 0; i < Glyphs.Length; i++) if (Btn(i).Contains(p)) return i; return RowClick ? Glyphs.Length : -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Dim ? -1 : HitTest(e.Location);
            if (h != hot) { hot = h; Cursor = h >= 0 && CanUse(h) ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = Dim ? -1 : HitTest(e.Location);
            if (h >= 0 && CanUse(h)) { EventHandler<CanvasEventArgs> a = Action; if (a != null) a(this, new CanvasEventArgs(PointF.Empty, h, Index, true)); }
            base.OnMouseClick(e);
        }
        // lets tests and keyboard code trigger an action like a click would
        public void RunAction(int action) { if (CanUse(action)) { EventHandler<CanvasEventArgs> a = Action; if (a != null) a(this, new CanvasEventArgs(PointF.Empty, action, Index, true)); } }
    }

    // Prepares tile thumbnails in the background (each job = a list of edits applied to a small base
    // image), then shows them all at once, fading in from the top row to the bottom one.
    public static class Thumbs
    {
        static int jobSeq;

        public static void Render(Control ui, Img baseImg, List<List<Step>> jobs, List<Tile> tiles, bool animate)
        {
            int id = System.Threading.Interlocked.Increment(ref jobSeq);
            foreach (Tile t in tiles) { t.ThumbJob = id; if (t.Thumb == null) { t.Placeholder = true; t.Invalidate(); } }
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Bitmap[] res = new Bitmap[jobs.Count];
                Ops.For(0, jobs.Count, i =>
                {
                    try { res[i] = Pipeline.ApplyAll(baseImg, jobs[i]).ToBitmap(); }
                    catch { res[i] = baseImg.ToBitmap(); }
                });
                try
                {
                    ui.BeginInvoke((MethodInvoker)delegate
                    {
                        int top = int.MaxValue;
                        foreach (Tile t in tiles) if (!t.IsDisposed) top = Math.Min(top, t.PointToScreen(Point.Empty).Y);
                        for (int i = 0; i < tiles.Count && i < res.Length; i++)
                        {
                            Tile t = tiles[i];
                            if (t.IsDisposed || t.ThumbJob != id) { if (res[i] != null) res[i].Dispose(); continue; }
                            if (animate)
                            {
                                // top to bottom: each row starts a little later, columns ripple left to right
                                int row = Math.Max(0, (t.PointToScreen(Point.Empty).Y - top) / Math.Max(1, t.Height));
                                int col = Math.Max(0, t.Left / Math.Max(1, t.Width));
                                t.FadeIn(res[i], row * 70 + col * 25);
                            }
                            else { t.Thumb = res[i]; t.Placeholder = false; t.Invalidate(); }
                        }
                    });
                }
                catch { }
            });
        }
    }

    // Fast builders for the side panel (one call from the script per element).
    public static class Ui
    {
        public static int Px(double v) { return (int)Math.Round(v * Theme.Scale); }
        static Font sectionFont, hintFont, textFont, buttonFont;
        public static Font SectionFont { get { return sectionFont ?? (sectionFont = new Font("Segoe UI Semibold", 8.5f)); } }
        public static Font HintFont { get { return hintFont ?? (hintFont = new Font("Segoe UI", 8.5f)); } }
        public static Font TextFont { get { return textFont ?? (textFont = new Font("Segoe UI", 10f)); } }
        public static Font ButtonFont { get { return buttonFont ?? (buttonFont = new Font("Segoe UI Semibold", 9f)); } }

        public static T Place<T>(Control body, T c, int width, int height) where T : Control
        {
            c.Width = width;
            if (height > 0) c.Height = height;
            c.Margin = new Padding(Px(12), Px(3), Px(4), Px(3));
            body.Controls.Add(c);
            return c;
        }

        public static Label Section(Control body, string text, int width)
        {
            Label l = new Label();
            l.Text = text.ToUpperInvariant(); l.ForeColor = Theme.Dim; l.Font = SectionFont; l.TextAlign = ContentAlignment.BottomLeft;
            return Place(body, l, width, Px(26));
        }

        public static Label Hint(Control body, string text, int width)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = Theme.Dim; l.Font = HintFont;
            l.AutoSize = true; l.MaximumSize = new Size(width, 0);
            l.Margin = new Padding(Px(12), Px(8), Px(4), Px(4));
            body.Controls.Add(l);
            return l;
        }

        public static ParamSlider Slider(Control body, string key, string label, float min, float max, float def, float step, string suffix, float value, int width)
        {
            ParamSlider s = new ParamSlider();
            s.Key = key; s.Label = label; s.Min = min; s.Max = max; s.Default = def; s.Step = step; s.Suffix = suffix ?? "";
            s.Value = value;
            return Place(body, s, width, Px(54));
        }

        public static IconButton Button(string glyph, string text)
        {
            IconButton b = new IconButton();
            b.Glyph = glyph ?? ""; b.Text = text ?? "";
            int w = string.IsNullOrEmpty(text) ? Px(44) : Px(40) + TextRenderer.MeasureText(text, ButtonFont).Width;
            b.Size = new Size(w, Px(40));
            b.Margin = new Padding(Px(2));
            return b;
        }

        public static TileGroup Tiles(Control body, int width)
        {
            TileGroup g = new TileGroup();
            g.MaximumSize = new Size(width + Px(8), 0);
            g.Margin = new Padding(Px(8), Px(2), 0, Px(2));
            body.Controls.Add(g);
            return g;
        }

        public static ButtonRow Buttons(Control body, int width)
        {
            ButtonRow r = new ButtonRow();
            r.MaximumSize = new Size(width, 0);
            r.Margin = new Padding(Px(10), Px(4), Px(4), Px(4));
            body.Controls.Add(r);
            return r;
        }

        public static StackRow StackEntry(Control body, int index, string glyph, string title, bool canBrush, bool dim, int width)
        {
            StackRow r = new StackRow();
            r.Index = index; r.Glyph = glyph ?? ""; r.Title = title ?? ""; r.CanBrush = canBrush; r.Dim = dim;
            return Place(body, r, width, Px(dim ? 40 : 46));
        }

        // an edit-stack step: show/hide (eye), edit, stack brush, delete
        public static StackRow StepEntry(Control body, int index, string glyph, string title, bool canBrush, bool hidden, int width)
        {
            StackRow r = StackEntry(body, index, glyph, title, canBrush, false, width);
            // eye (or crossed eye when hidden), pencil, brush, bin
            r.Glyphs = new string[] { ((char)(hidden ? 0xED1A : 0xE890)).ToString(), ((char)0xE70F).ToString(), ((char)0xE771).ToString(), ((char)0xE74D).ToString() };
            r.BrushButton = 2;
            r.Faded = hidden;
            return r;
        }

        // a saved mask: click the name to use it; buttons show, rename, delete
        public static StackRow MaskEntry(Control body, int index, string title, bool selected, bool shown, int width)
        {
            StackRow r = new StackRow();
            r.Index = index; r.Glyph = ""; r.Title = title ?? ""; r.RowClick = true; r.Selected = selected; r.Lit = shown ? 0 : -1;
            r.Glyphs = new string[] { "", "", "" };
            return Place(body, r, width, Px(40));
        }
    }

    public enum CanvasMode { View, Adjust, Crop, Points, Center, Brush, Quad, Move, Pick }

    public class CanvasEventArgs : EventArgs
    {
        public PointF P; public float Value; public int Index; public bool Final;
        public CanvasEventArgs(PointF p, float v, int i, bool f) { P = p; Value = v; Index = i; Final = f; }
    }

    public class ImageCanvas : Control
    {
        Bitmap img, compare, mask;
        public bool ShowCompare;
        public CanvasMode Mode = CanvasMode.View;
        public string Hud = "";
        public string Status = "";
        // Busy shows a thin animated progress line along the top once a render takes more than a moment.
        bool busy;
        DateTime busySince;
        Timer anim;
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (value == busy) return;
                busy = value;
                if (busy)
                {
                    busySince = DateTime.Now;
                    if (anim == null) { anim = new Timer(); anim.Interval = 30; anim.Tick += (s, e) => Invalidate(new Rectangle(0, 0, Width, 6)); }
                    anim.Start();
                }
                else if (anim != null) anim.Stop();
                Invalidate(new Rectangle(0, 0, Width, 6));
            }
        }

        float zoom = 1; PointF pan; bool fit = true;

        // crop
        public RectangleF CropRect = new RectangleF(0, 0, 1, 1);
        public float CropAspect; // width/height in pixels, 0 = free
        // points
        public List<PointF> Points = new List<PointF>();
        public List<float> PointRadii = new List<float>();
        // optional: the selected point's zone is then an ellipse with resize and rotate handles (Selective)
        public List<float> PointRadiiY = new List<float>(), PointAngles = new List<float>();
        public List<string> PointLabels = new List<string>();
        public int Selected = -1;
        // center / ellipse
        public PointF Center = new PointF(0.5f, 0.5f);
        public bool ShowEllipse; public int EllipseShape; public float RadX = 0.25f, RadY = 0.25f, Angle;
        // faces
        public List<RectangleF> Faces = new List<RectangleF>();
        // brush
        public List<Stroke> BrushTarget;
        public string BrushChannel = "";
        public float BrushValue = 5, BrushSize = 40;
        // quad
        public PointF[] Quad;
        // marks of the clicks of a "pick" mask: true = part added, false = part removed
        public List<PointF> Marks = new List<PointF>();
        public List<bool> MarkAdd = new List<bool>();

        public event EventHandler Pressing; // left button down on the photo, before the gesture is handled
        public event EventHandler<CanvasEventArgs> ValueDrag, ParamCycle, PointAdded, PointMoved, PointSelected, PointResized,
            CenterMoved, EllipseChanged, CropChanged, StrokeChanged, QuadChanged, Picked, MoveDrag, ImageClick, ImageDoubleClick;

        public ImageCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Back;
        }

        public Size ImageSize { get { return img == null ? Size.Empty : img.Size; } }
        public bool HasImage { get { return img != null; } }

        // Live view: the part of the photo on screen recomputed while a slider moves, drawn over the picture until
        // the next whole picture arrives. norm: where it goes, in fractions of the photo.
        Bitmap patch; RectangleF patchRect; double patchScale;
        public void SetPatch(Bitmap b, RectangleF norm) { SetPatch(b, norm, 1); }

        // toOriginal: the patch's pixels per full-resolution pixel
        public void SetPatch(Bitmap b, RectangleF norm, double toOriginal)
        {
            if (patch != null && patch != b) patch.Dispose();
            patch = b; patchRect = norm; patchScale = toOriginal;
            Invalidate();
        }

        // the patch shows all of 'visible' (fractions of the photo) with as much detail as the screen shows
        public bool PatchCovers(RectangleF visible, double displayScale)
        {
            if (patch == null) return false;
            const float e = 0.002f;
            bool inside = visible.X >= patchRect.X - e && visible.Y >= patchRect.Y - e && visible.Right <= patchRect.Right + e && visible.Bottom <= patchRect.Bottom + e;
            return inside && patchScale >= Math.Min(1.0, displayScale) * 0.95;
        }
        void DropPatch() { if (patch != null) { patch.Dispose(); patch = null; } }
        public bool HasPatch { get { return patch != null; } }

        // the part of the photo inside the view, in fractions of the photo
        public RectangleF VisibleNorm
        {
            get
            {
                if (img == null) return new RectangleF(0, 0, 1, 1);
                RectangleF r = ImgRect();
                RectangleF v = RectangleF.Intersect(r, new RectangleF(0, 0, Width, Height));
                if (v.Width <= 0 || v.Height <= 0) return new RectangleF(0, 0, 1, 1);
                return new RectangleF((v.X - r.X) / r.Width, (v.Y - r.Y) / r.Height, v.Width / r.Width, v.Height / r.Height);
            }
        }

        public void SetImage(Bitmap b)
        {
            DropPatch();
            Bitmap old = img;
            bool sizeChanged = old == null || b == null || old.Size != b.Size;
            img = b;
            if (sizeChanged)
            {
                // Same picture at another resolution (preview <-> full size): keep the view where it is.
                bool sameShape = old != null && b != null && Math.Abs((float)old.Width / old.Height - (float)b.Width / b.Height) < 0.01f;
                if (sameShape && !fit) zoom *= (float)old.Width / b.Width;
                else fit = true;
            }
            if (scaledFrom == old) DropScaled();
            if (old != null && old != b) old.Dispose();
            Invalidate();
            if (sizeChanged) RaiseView();
            if (showHist) ComputeHistogram();
        }

        // ---- live histogram overlay ----
        bool showHist;
        int[][] hist;
        public bool ShowHistogram
        {
            get { return showHist; }
            set { showHist = value; if (value) ComputeHistogram(); Invalidate(); }
        }

        void ComputeHistogram()
        {
            if (img == null) { hist = null; return; }
            int[][] hh = { new int[256], new int[256], new int[256], new int[256] };
            BitmapData bd = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[bd.Stride * img.Height];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
            int stride = bd.Stride;
            img.UnlockBits(bd);
            int step = Math.Max(1, (int)Math.Sqrt(img.Width * (double)img.Height / 250000));
            for (int y = 0; y < img.Height; y += step)
                for (int x = 0; x < img.Width; x += step)
                {
                    int o = y * stride + x * 4;
                    int b = buf[o], g = buf[o + 1], r = buf[o + 2];
                    hh[0][r]++; hh[1][g]++; hh[2][b]++; hh[3][(r * 77 + g * 150 + b * 29) >> 8]++;
                }
            hist = hh;
        }

        void DrawHistogram(Graphics g)
        {
            if (hist == null) return;
            float pw = 256, ph = 96, pad = 10;
            RectangleF box = new RectangleF(14, 14, pw + pad * 2, ph + pad * 2);
            using (GraphicsPath p = Rounded(box, 10))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 24, 25, 28))) g.FillPath(bg, p);
            int mx = 1;
            for (int c = 0; c < 4; c++) for (int i = 1; i < 255; i++) mx = Math.Max(mx, hist[c][i]);
            Color[] cols = { Color.FromArgb(120, 235, 70, 70), Color.FromArgb(120, 70, 210, 90), Color.FromArgb(120, 70, 120, 240) };
            for (int c = 0; c < 4; c++)
            {
                PointF[] pts = new PointF[258];
                pts[0] = new PointF(box.X + pad, box.Y + pad + ph);
                for (int i = 0; i < 256; i++)
                {
                    float v = (float)Math.Sqrt(Math.Min(1.0, hist[c][i] / (double)mx));
                    pts[i + 1] = new PointF(box.X + pad + i * pw / 255f, box.Y + pad + ph - v * ph);
                }
                pts[257] = new PointF(box.X + pad + pw, box.Y + pad + ph);
                if (c < 3) using (SolidBrush b = new SolidBrush(cols[c])) g.FillPolygon(b, pts);
                else using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.2f)) g.DrawLines(pen, pts);
            }
        }

        // Width of the original photo, so zoom can be expressed in original pixels.
        public int SourceWidth;
        public event EventHandler ViewChanged;
        void RaiseView() { EventHandler h = ViewChanged; if (h != null) h(this, EventArgs.Empty); }

        // Screen pixels per original photo pixel (1 = 100%).
        public float DisplayScale
        {
            get
            {
                if (img == null) return 1;
                RectangleF r = ImgRect();
                return r.Width / (SourceWidth > 0 ? SourceWidth : img.Width);
            }
        }

        public bool IsFit { get { return fit; } }

        public void SetCompare(Bitmap b) { Bitmap old = compare; compare = b; if (scaledFrom == old) DropScaled(); if (old != null && old != b) old.Dispose(); }

        public void ClearMask() { if (mask != null) { mask.Dispose(); mask = null; } Invalidate(); }

        // Paints a translucent overlay of a stroke channel: warm = positive, cool = negative.
        public void ShowMaskFor(List<Stroke> strokes, string ch, float scale)
        {
            if (img == null) return;
            int w = Math.Max(1, img.Width / 2), h = Math.Max(1, img.Height / 2);
            float[] m = Raster.Channel(strokes, ch, w, h);
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[bd.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = m[y * w + x] / scale;
                    int o = y * bd.Stride + x * 4;
                    float a = Math.Min(1f, Math.Abs(v));
                    if (v >= 0) { buf[o] = 60; buf[o + 1] = 60; buf[o + 2] = 235; }
                    else { buf[o] = 235; buf[o + 1] = 140; buf[o + 2] = 40; }
                    buf[o + 3] = (byte)(a * 150);
                }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            b.UnlockBits(bd);
            if (mask != null) mask.Dispose();
            mask = b;
            Invalidate();
        }

        public void ShowPlaneMask(float[] m, int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            byte[] buf = new byte[bd.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = y * bd.Stride + x * 4;
                    buf[o] = 30; buf[o + 1] = 30; buf[o + 2] = 255; buf[o + 3] = (byte)(Ops.C01(m[y * w + x]) * 140);
                }
            System.Runtime.InteropServices.Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
            b.UnlockBits(bd);
            if (mask != null) mask.Dispose();
            mask = b;
            Invalidate();
        }

        public void Fit() { fit = true; Invalidate(); RaiseView(); }

        // moves the view by dx, dy screen pixels, like a pan with the mouse
        public void Pan(float dx, float dy)
        {
            if (img == null) return;
            fit = false; pan = new PointF(pan.X + dx, pan.Y + dy); Invalidate(); RaiseView();
        }

        // 100%: one photo pixel per screen pixel, zooming around the centre of the view.
        public void ZoomActual() { SetDisplayScale(1f); }

        public void SetDisplayScale(float s)
        {
            if (img == null) return;
            ImgRect();
            float src = SourceWidth > 0 ? SourceWidth : img.Width;
            float nz = s * src / img.Width;
            float k = nz / zoom;
            pan = new PointF(pan.X * k, pan.Y * k);
            zoom = nz; fit = false;
            Invalidate(); RaiseView();
        }

        RectangleF ImgRect()
        {
            if (img == null) return RectangleF.Empty;
            float pad = Mode == CanvasMode.Crop || Mode == CanvasMode.Quad ? 40 : 16;
            if (fit)
            {
                zoom = Math.Min((Width - pad * 2) / img.Width, (Height - pad * 2) / img.Height);
                if (zoom <= 0) zoom = 0.01f;
                pan = PointF.Empty;
            }
            float w = img.Width * zoom, h = img.Height * zoom;
            float x = (Width - w) / 2 + pan.X, y = (Height - h) / 2 + pan.Y;
            // whole-pixel placement keeps 100% views perfectly sharp
            if (!fit) { x = (float)Math.Round(x); y = (float)Math.Round(y); }
            return new RectangleF(x, y, w, h);
        }

        public PointF ToNorm(Point p)
        {
            RectangleF r = ImgRect();
            return new PointF((p.X - r.X) / r.Width, (p.Y - r.Y) / r.Height);
        }

        PointF ToScreen(PointF n)
        {
            RectangleF r = ImgRect();
            return new PointF(r.X + n.X * r.Width, r.Y + n.Y * r.Height);
        }

        float MinDimScreen { get { RectangleF r = ImgRect(); return Math.Min(r.Width, r.Height); } }

        // ---------------- painting ----------------
        // Painting must never throw: Windows Forms would then give up on the control for good and draw a
        // red cross instead of the photo. GDI+ can run out of memory drawing a full resolution picture
        // on modest machines; the photo is then drawn the simplest way and the next repaint tries again.
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintAll(e.Graphics); }
            catch (Exception)
            {
                DropScaled();
                try
                {
                    Graphics g = e.Graphics;
                    g.Clear(BackColor);
                    if (img != null)
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(img, ImgRect());
                    }
                }
                catch (Exception) { }
            }
        }

        // A large picture shown much smaller is drawn from a copy at about screen size: far less memory
        // and work for GDI+ than scaling the full bitmap at every repaint.
        Bitmap scaled; Bitmap scaledFrom; Size scaledSize;
        void DropScaled() { if (scaled != null) { scaled.Dispose(); scaled = null; } scaledFrom = null; }

        Bitmap ForDisplay(Bitmap b, RectangleF r)
        {
            if (b == null || r.Width >= b.Width * 0.98f || r.Width < 8 || r.Height < 8) return b;
            Size want = new Size((int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height));
            if (scaled != null && scaledFrom == b && Math.Abs(scaledSize.Width - want.Width) <= 2) return scaled;
            DropScaled();
            try
            {
                Bitmap s = new Bitmap(want.Width, want.Height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(s))
                {
                    // the high-quality filter only matters for strong reductions; down to half size plain bilinear
                    // looks the same and is several times faster (this runs for every new frame while a slider moves)
                    g.InterpolationMode = want.Width * 2 >= b.Width ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(b, new Rectangle(0, 0, want.Width, want.Height));
                }
                scaled = s; scaledFrom = b; scaledSize = want;
                return s;
            }
            catch (Exception) { DropScaled(); return b; }
        }

        // where a repaint's time goes, so it can be measured instead of guessed at
        public static double PClear, PShadow, PPhoto, PScale, POverlay, PTotal;
        public static int Paints;

        void PaintAll(Graphics g)
        {
            System.Diagnostics.Stopwatch pw = System.Diagnostics.Stopwatch.StartNew();
            double t0 = 0;
            g.Clear(BackColor);
            PClear += pw.Elapsed.TotalMilliseconds - t0; t0 = pw.Elapsed.TotalMilliseconds;
            if (img == null)
            {
                using (Font f = new Font("Segoe UI Light", 22f))
                using (Font f2 = new Font("Segoe UI", 10.5f))
                using (Font fi = Theme.Icon(64))
                using (SolidBrush b = new SolidBrush(Theme.Dim))
                {
                    StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.DrawString("", fi, b, Width / 2f, Height / 2f - 110, sf);
                    g.DrawString("Open a photo to start editing", f, b, Width / 2f, Height / 2f - 20, sf);
                    g.DrawString("Click OPEN, press Ctrl+O, or drop an image here", f2, b, Width / 2f, Height / 2f + 25, sf);
                }
                return;
            }
            RectangleF r = ImgRect();
            g.InterpolationMode = zoom < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (Math.Abs(DisplayScale * (img.Width / (float)(SourceWidth > 0 ? SourceWidth : img.Width)) - 1f) < 0.001f)
                g.InterpolationMode = InterpolationMode.NearestNeighbor; // bitmap shown pixel for pixel
            Bitmap show = ShowCompare && compare != null ? compare : img;
            bool compareSameShape = compare != null && Math.Abs((float)compare.Width / compare.Height - (float)img.Width / img.Height) < 0.01f;
            if (ShowCompare && compare != null && !compareSameShape)
            {
                float z = Math.Min((Width - 32f) / compare.Width, (Height - 32f) / compare.Height);
                float w = compare.Width * z, h = compare.Height * z;
                g.DrawImage(compare, new RectangleF((Width - w) / 2, (Height - h) / 2, w, h));
            }
            else
            {
                // soft shadow so the photo floats above the background (only its margin is painted: the photo
                // covers the rest, and filling the whole area twelve times made every repaint slow)
                if (fit)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(r, CombineMode.Exclude);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                        for (int k = 12; k >= 1; k--)
                            g.FillRectangle(sb, r.X - k + 3, r.Y - k + 6, r.Width + 2 * k - 6, r.Height + 2 * k - 6);
                    g.Restore(st);
                }
                PShadow += pw.Elapsed.TotalMilliseconds - t0; t0 = pw.Elapsed.TotalMilliseconds;
                Bitmap d = ForDisplay(show, r);
                PScale += pw.Elapsed.TotalMilliseconds - t0; t0 = pw.Elapsed.TotalMilliseconds;
                if (d != show)
                {
                    // the copy is already at screen size: copied pixel for pixel, the fastest way GDI+ draws
                    g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(d, new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), d.Width, d.Height));
                }
                else g.DrawImage(d, r);
                if (patch != null && !ShowCompare)
                {
                    RectangleF pr = new RectangleF(r.X + patchRect.X * r.Width, r.Y + patchRect.Y * r.Height, patchRect.Width * r.Width, patchRect.Height * r.Height);
                    // computed at screen size: copied pixel for pixel when it fits, smoothed otherwise
                    bool same = Math.Abs(pr.Width - patch.Width) < 1.5f && Math.Abs(pr.Height - patch.Height) < 1.5f;
                    // a part computed at a zoom level above the screen's is shrunk with the smooth filter
                    g.InterpolationMode = same || pr.Width > patch.Width * 1.5f ? InterpolationMode.NearestNeighbor
                        : pr.Width < patch.Width * 0.8f ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    if (same) g.DrawImage(patch, new Rectangle((int)Math.Round(pr.X), (int)Math.Round(pr.Y), patch.Width, patch.Height));
                    else g.DrawImage(patch, pr);
                }
            }
            PPhoto += pw.Elapsed.TotalMilliseconds - t0; t0 = pw.Elapsed.TotalMilliseconds;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!ShowCompare)
            {
                if (mask != null) { g.InterpolationMode = InterpolationMode.Bilinear; g.DrawImage(mask, r); }
                DrawOverlay(g, r);
            }
            POverlay += pw.Elapsed.TotalMilliseconds - t0;
            PTotal += pw.Elapsed.TotalMilliseconds; Paints++;
            if (ShowCompare) DrawPill(g, "Original", new PointF(Width / 2f, 18));
            else if (!string.IsNullOrEmpty(Hud)) DrawPill(g, Hud, new PointF(Width / 2f, 18));
            if (busy && (DateTime.Now - busySince).TotalMilliseconds > 180)
            {
                // indeterminate progress: a segment sweeping across the top edge
                double t = ((DateTime.Now - busySince).TotalMilliseconds % 1100) / 1100.0;
                float seg = Width * 0.28f, x = (float)(-seg + (Width + seg) * t);
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(x - 1, 0, seg + 2, 3), Color.FromArgb(0, Theme.Accent), Theme.Accent, LinearGradientMode.Horizontal))
                {
                    ColorBlend cb = new ColorBlend(3);
                    cb.Colors = new Color[] { Color.FromArgb(0, Theme.Accent), Theme.Accent, Color.FromArgb(0, Theme.Accent) };
                    cb.Positions = new float[] { 0, 0.5f, 1 };
                    lb.InterpolationColors = cb;
                    g.FillRectangle(lb, x, 0, seg, 3);
                }
            }
            if (!fit)
            {
                string zs = Math.Round(DisplayScale * 100).ToString(CultureInfo.InvariantCulture) + "%";
                using (Font f = new Font("Segoe UI Semibold", 9.5f))
                {
                    SizeF s = g.MeasureString(zs, f);
                    RectangleF zr = new RectangleF(Width - s.Width - 26, Height - s.Height - 16, s.Width + 14, s.Height + 6);
                    using (GraphicsPath p = Rounded(zr, zr.Height / 2))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 30, 31, 34))) g.FillPath(b, p);
                    g.DrawString(zs, f, Brushes.White, zr.X + 7, zr.Y + 3);
                }
            }
            if (showHist && !ShowCompare) DrawHistogram(g);
            if (!string.IsNullOrEmpty(Status))
            {
                // toast: rounded dark pill with an accent marker
                using (Font f = new Font("Segoe UI", 9.5f))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    SizeF s = g.MeasureString(Status, f, Math.Max(100, Width - 140));
                    RectangleF tr = new RectangleF(14, Height - s.Height - 30, s.Width + 34, s.Height + 14);
                    using (GraphicsPath p = Rounded(tr, 10))
                    {
                        using (SolidBrush b = new SolidBrush(Theme.IsLight ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(235, 38, 39, 43))) g.FillPath(b, p);
                        using (Pen pen = new Pen(Theme.IsLight ? Theme.Line : Color.FromArgb(70, 255, 255, 255), 1)) g.DrawPath(pen, p);
                    }
                    bool err = Status.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || Status.StartsWith("Render error", StringComparison.OrdinalIgnoreCase);
                    using (SolidBrush mk = new SolidBrush(err ? Color.FromArgb(232, 92, 92) : Theme.Accent)) g.FillEllipse(mk, tr.X + 11, tr.Y + tr.Height / 2 - 4, 8, 8);
                    using (SolidBrush b = new SolidBrush(Theme.Text))
                        g.DrawString(Status, f, b, new RectangleF(tr.X + 26, tr.Y + 7, s.Width + 2, s.Height + 2));
                }
            }
        }

        void DrawPill(Graphics g, string text, PointF top)
        {
            using (Font f = new Font("Segoe UI Semibold", 11f))
            {
                SizeF s = g.MeasureString(text, f);
                RectangleF rr = new RectangleF(top.X - s.Width / 2 - 14, top.Y, s.Width + 28, s.Height + 8);
                using (GraphicsPath p = Rounded(rr, rr.Height / 2))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(225, Theme.Accent)))
                    g.FillPath(b, p);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, f, Brushes.White, rr.X + 14, rr.Y + 4);
            }
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Knob(Graphics g, PointF p, float rad, bool sel)
        {
            using (SolidBrush b = new SolidBrush(sel ? Theme.Accent : Color.White))
            using (Pen pen = new Pen(Color.FromArgb(160, 0, 0, 0), 1.5f))
            {
                g.FillEllipse(b, p.X - rad, p.Y - rad, rad * 2, rad * 2);
                g.DrawEllipse(pen, p.X - rad, p.Y - rad, rad * 2, rad * 2);
            }
        }

        void DrawOverlay(Graphics g, RectangleF r)
        {
            if (Mode == CanvasMode.Crop)
            {
                RectangleF c = new RectangleF(r.X + CropRect.X * r.Width, r.Y + CropRect.Y * r.Height, CropRect.Width * r.Width, CropRect.Height * r.Height);
                using (Region rg = new Region(r))
                {
                    rg.Exclude(c);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRegion(b, rg);
                }
                using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
                {
                    for (int i = 1; i < 3; i++)
                    {
                        g.DrawLine(p, c.X + c.Width * i / 3, c.Y, c.X + c.Width * i / 3, c.Bottom);
                        g.DrawLine(p, c.X, c.Y + c.Height * i / 3, c.Right, c.Y + c.Height * i / 3);
                    }
                }
                using (Pen p = new Pen(Color.White, 2)) g.DrawRectangle(p, c.X, c.Y, c.Width, c.Height);
                using (Pen p = new Pen(Color.White, 5))
                {
                    float L = Math.Min(24, Math.Min(c.Width, c.Height) / 3);
                    PointF[] cs = { new PointF(c.X, c.Y), new PointF(c.Right, c.Y), new PointF(c.Right, c.Bottom), new PointF(c.X, c.Bottom) };
                    float[] sx = { 1, -1, -1, 1 }, sy = { 1, 1, -1, -1 };
                    for (int i = 0; i < 4; i++)
                    {
                        g.DrawLine(p, cs[i].X, cs[i].Y, cs[i].X + sx[i] * L, cs[i].Y);
                        g.DrawLine(p, cs[i].X, cs[i].Y, cs[i].X, cs[i].Y + sy[i] * L);
                    }
                }
            }
            if (Mode == CanvasMode.Points)
            {
                using (Font f = new Font("Segoe UI Semibold", 9f))
                    for (int i = 0; i < Points.Count; i++)
                    {
                        PointF p = ToScreen(Points[i]);
                        if (i == Selected && i < PointRadii.Count)
                        {
                            float rx = PointRadii[i] * MinDimScreen * ZoneScale, ry = rx, ang = 0;
                            bool ell = HasZone(i);
                            if (ell) { ry = PointRadiiY[i] * MinDimScreen * ZoneScale; ang = PointAngles[i]; }
                            GraphicsState st = g.Save();
                            g.TranslateTransform(p.X, p.Y); g.RotateTransform(ang);
                            using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f)) { pen.DashStyle = DashStyle.Dash; g.DrawEllipse(pen, -rx, -ry, rx * 2, ry * 2); }
                            g.Restore(st);
                            if (ell)
                            {
                                Knob(g, RotA(p, rx, 0, ang), 7, false);
                                Knob(g, RotA(p, 0, -ry, ang), 7, false);
                                Knob(g, RotA(p, rx + 26, 0, ang), 6, true);
                            }
                        }
                        Knob(g, p, 13, i == Selected);
                        string lab = i < PointLabels.Count ? PointLabels[i] : "";
                        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(lab, f, i == Selected ? Brushes.White : Brushes.Black, p, sf);
                    }
            }
            if (Mode == CanvasMode.Center || ShowEllipse)
            {
                PointF c = ToScreen(Center);
                if (ShowEllipse)
                {
                    float rx = RadX * MinDimScreen, ry = RadY * MinDimScreen;
                    GraphicsState st = g.Save();
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform(Angle);
                    using (Pen pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
                    {
                        if (EllipseShape == 1)
                        {
                            g.DrawLine(pen, -4000, -ry, 4000, -ry); g.DrawLine(pen, -4000, ry, 4000, ry);
                        }
                        else g.DrawEllipse(pen, -rx, -ry, rx * 2, ry * 2);
                    }
                    g.Restore(st);
                    if (EllipseShape != 1) Knob(g, Rot(c, rx, 0), 7, false);
                    Knob(g, Rot(c, 0, -ry), 7, false);
                    Knob(g, Rot(c, (EllipseShape == 1 ? 60 : rx) + 26, 0), 6, true);
                }
                Knob(g, c, 11, true);
                using (Pen pen = new Pen(Color.Black, 1.5f)) { g.DrawLine(pen, c.X - 5, c.Y, c.X + 5, c.Y); g.DrawLine(pen, c.X, c.Y - 5, c.X, c.Y + 5); }
            }
            for (int i = 0; i < Marks.Count && i < MarkAdd.Count; i++)
            {
                PointF p = ToScreen(Marks[i]);
                float d = Ui.Px(16);
                using (SolidBrush b = new SolidBrush(MarkAdd[i] ? Theme.Accent : Color.FromArgb(230, 70, 70)))
                    g.FillEllipse(b, p.X - d / 2, p.Y - d / 2, d, d);
                using (Pen pen = new Pen(Color.White, Math.Max(1.5f, Ui.Px(2))))
                {
                    g.DrawEllipse(pen, p.X - d / 2, p.Y - d / 2, d, d);
                    g.DrawLine(pen, p.X - d * 0.25f, p.Y, p.X + d * 0.25f, p.Y);
                    if (MarkAdd[i]) g.DrawLine(pen, p.X, p.Y - d * 0.25f, p.X, p.Y + d * 0.25f);
                }
            }
            if (Faces.Count > 0)
                using (Pen pen = new Pen(Color.FromArgb(180, Theme.Accent), 2f))
                {
                    pen.DashStyle = DashStyle.Dot;
                    foreach (RectangleF f in Faces)
                    {
                        PointF a = ToScreen(new PointF(f.X, f.Y)), b = ToScreen(new PointF(f.Right, f.Bottom));
                        g.DrawEllipse(pen, a.X, a.Y, b.X - a.X, b.Y - a.Y);
                    }
                }
            if (Mode == CanvasMode.Quad && Quad != null)
            {
                PointF[] q = new PointF[4];
                for (int i = 0; i < 4; i++) q[i] = ToScreen(Quad[i]);
                using (Pen pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f)) g.DrawPolygon(pen, q);
                for (int i = 0; i < 4; i++) Knob(g, q[i], 10, dragIdx == i);
            }
            // the stroke being painted, drawn at once (the rendered result follows a moment later)
            if (Mode == CanvasMode.Brush && drag == Drag.Brush && curStroke != null && curStroke.Pts.Count > 0)
            {
                Color col = BrushChannel == "mask" ? (curStroke.V >= 0.5f ? Color.FromArgb(110, 255, 30, 30) : Color.FromArgb(110, 25, 25, 25))
                    : Color.FromArgb(70, 255, 255, 255);
                float wd = Math.Max(2f, curStroke.R * MinDimScreen * 2);
                PointF[] ps = new PointF[curStroke.Pts.Count];
                for (int i = 0; i < ps.Length; i++) ps[i] = ToScreen(curStroke.Pts[i]);
                if (ps.Length == 1) using (SolidBrush b = new SolidBrush(col)) g.FillEllipse(b, ps[0].X - wd / 2, ps[0].Y - wd / 2, wd, wd);
                else
                    using (Pen pen = new Pen(col, wd))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, ps);
                    }
            }
            if (Mode == CanvasMode.Brush && hover.X > -1000)
            {
                using (Pen pen = new Pen(Color.White, 1.5f)) g.DrawEllipse(pen, hover.X - BrushSize, hover.Y - BrushSize, BrushSize * 2, BrushSize * 2);
                using (Pen pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(pen, hover.X - BrushSize - 1.5f, hover.Y - BrushSize - 1.5f, BrushSize * 2 + 3, BrushSize * 2 + 3);
            }
            if (Mode == CanvasMode.Pick && hover.X > -1000)
            {
                using (Pen pen = new Pen(Color.White, 2f))
                {
                    g.DrawEllipse(pen, hover.X - 12, hover.Y - 12, 24, 24);
                    g.DrawLine(pen, hover.X - 20, hover.Y, hover.X - 6, hover.Y); g.DrawLine(pen, hover.X + 6, hover.Y, hover.X + 20, hover.Y);
                }
            }
        }

        PointF Rot(PointF c, float x, float y) { return RotA(c, x, y, Angle); }

        static PointF RotA(PointF c, float x, float y, float deg)
        {
            double a = deg * Math.PI / 180;
            return new PointF(c.X + (float)(x * Math.Cos(a) - y * Math.Sin(a)), c.Y + (float)(x * Math.Sin(a) + y * Math.Cos(a)));
        }

        // a point's radius is the spread of its falloff: the zone outline is drawn where the effect has mostly faded
        const float ZoneScale = 0.8f;
        bool HasZone(int i) { return i >= 0 && i < Points.Count && i < PointRadii.Count && i < PointRadiiY.Count && i < PointAngles.Count; }

        // which handle of the selected point's zone is under pt: 0 width, 1 height, 2 rotation, -1 none
        int ZoneHandleAt(PointF pt)
        {
            int i = Selected;
            if (!HasZone(i)) return -1;
            PointF p = ToScreen(Points[i]);
            float rx = PointRadii[i] * MinDimScreen * ZoneScale, ry = PointRadiiY[i] * MinDimScreen * ZoneScale, ang = PointAngles[i];
            if (Dist(RotA(p, rx + 26, 0, ang), pt) < 14) return 2;
            if (Dist(RotA(p, rx, 0, ang), pt) < 14) return 0;
            if (Dist(RotA(p, 0, -ry, ang), pt) < 14) return 1;
            return -1;
        }

        // ---------------- mouse ----------------
        enum Drag { None, Pan, Adjust, Undecided, CropMove, CropHandle, Point, Center, EllX, EllY, EllRot, Brush, Quad, Move, ZoneX, ZoneY, ZoneRot }
        Drag drag = Drag.None;
        Point down, last;
        PointF hover = new PointF(-9999, -9999);
        int dragIdx = -1, cropHandle = -1;
        RectangleF cropStart;
        float accY;
        bool spaceDown;
        Stroke curStroke;
        int cycleSteps;

        protected override bool IsInputKey(Keys k) { return true; }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { spaceDown = true; Cursor = Cursors.Hand; } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { spaceDown = false; Cursor = Cursors.Default; } base.OnKeyUp(e); }

        void Raise(EventHandler<CanvasEventArgs> h, PointF p, float v, int i, bool fin) { if (h != null) h(this, new CanvasEventArgs(p, v, i, fin)); }
        // P = the zone's radii (width, height), Value = its angle
        void RaiseZone(bool fin) { if (HasZone(dragIdx)) Raise(PointResized, new PointF(PointRadii[dragIdx], PointRadiiY[dragIdx]), PointAngles[dragIdx], dragIdx, fin); }

        float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            down = last = e.Location;
            if (img == null) return;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right || spaceDown) { drag = Drag.Pan; if (fit) { fit = false; } return; }
            if (e.Button != MouseButtons.Left) return;
            { EventHandler h = Pressing; if (h != null) h(this, EventArgs.Empty); }
            PointF pt = e.Location;
            switch (Mode)
            {
                case CanvasMode.Crop:
                    {
                        RectangleF r = ImgRect();
                        RectangleF c = new RectangleF(r.X + CropRect.X * r.Width, r.Y + CropRect.Y * r.Height, CropRect.Width * r.Width, CropRect.Height * r.Height);
                        PointF[] hs = { new PointF(c.X, c.Y), new PointF(c.Right, c.Y), new PointF(c.Right, c.Bottom), new PointF(c.X, c.Bottom),
                                        new PointF(c.X + c.Width / 2, c.Y), new PointF(c.Right, c.Y + c.Height / 2), new PointF(c.X + c.Width / 2, c.Bottom), new PointF(c.X, c.Y + c.Height / 2) };
                        cropHandle = -1;
                        for (int i = 0; i < hs.Length; i++) if (Dist(hs[i], pt) < 18) { cropHandle = i; break; }
                        cropStart = CropRect;
                        drag = cropHandle >= 0 ? Drag.CropHandle : (c.Contains(pt) ? Drag.CropMove : Drag.None);
                        return;
                    }
                case CanvasMode.Points:
                    {
                        int zh = ZoneHandleAt(pt);
                        if (zh >= 0) { dragIdx = Selected; drag = zh == 0 ? Drag.ZoneX : zh == 1 ? Drag.ZoneY : Drag.ZoneRot; return; }
                    }
                    for (int i = Points.Count - 1; i >= 0; i--)
                        if (Dist(ToScreen(Points[i]), pt) < 16) { Selected = i; dragIdx = i; drag = Drag.Point; Raise(PointSelected, Points[i], 0, i, false); Invalidate(); return; }
                    drag = Drag.Undecided; return;
                case CanvasMode.Center:
                    {
                        PointF c = ToScreen(Center);
                        if (Dist(c, pt) < 18) { drag = Drag.Center; return; }
                        if (ShowEllipse)
                        {
                            float rx = RadX * MinDimScreen, ry = RadY * MinDimScreen;
                            if (EllipseShape != 1 && Dist(Rot(c, rx, 0), pt) < 14) { drag = Drag.EllX; return; }
                            if (Dist(Rot(c, 0, -ry), pt) < 14) { drag = Drag.EllY; return; }
                            if (Dist(Rot(c, (EllipseShape == 1 ? 60 : rx) + 26, 0), pt) < 14) { drag = Drag.EllRot; return; }
                        }
                        drag = Drag.Undecided; return;
                    }
                case CanvasMode.Brush:
                    if (BrushTarget == null) return;
                    curStroke = new Stroke();
                    curStroke.Ch = BrushChannel; curStroke.V = BrushValue;
                    curStroke.R = (BrushSize / Math.Max(1, MinDimScreen));
                    curStroke.Pts.Add(ToNorm(e.Location));
                    BrushTarget.Add(curStroke);
                    drag = Drag.Brush;
                    Raise(StrokeChanged, ToNorm(e.Location), 0, BrushTarget.Count - 1, false);
                    return;
                case CanvasMode.Quad:
                    if (Quad != null)
                        for (int i = 0; i < 4; i++) if (Dist(ToScreen(Quad[i]), pt) < 18) { dragIdx = i; drag = Drag.Quad; return; }
                    drag = Drag.Undecided; return;
                case CanvasMode.Move:
                    drag = Drag.Move; return;
                case CanvasMode.Pick:
                    Raise(Picked, ToNorm(e.Location), 0, 0, true); return;
                case CanvasMode.Adjust:
                    drag = Drag.Undecided; return;
                default:
                    drag = Drag.Undecided; return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            hover = e.Location;
            int dx = e.X - last.X, dy = e.Y - last.Y;
            if (drag == Drag.Undecided)
            {
                int tx = e.X - down.X, ty = e.Y - down.Y;
                if (Math.Abs(tx) > 6 || Math.Abs(ty) > 6)
                {
                    if (Mode == CanvasMode.View) drag = Drag.Pan;
                    else { drag = Drag.Adjust; accY = 0; cycleSteps = 0; adjustVertical = Math.Abs(ty) > Math.Abs(tx); }
                }
            }
            RectangleF r = ImgRect();
            switch (drag)
            {
                case Drag.Pan:
                    // the view moved: zoomed in, the part coming into view is rendered once it stops (App)
                    fit = false; pan = new PointF(pan.X + dx, pan.Y + dy); Invalidate(); RaiseView(); break;
                case Drag.Adjust:
                    if (adjustVertical)
                    {
                        accY += dy;
                        while (Math.Abs(accY) >= 34) { int s = accY > 0 ? 1 : -1; accY -= s * 34; cycleSteps += s; Raise(ParamCycle, PointF.Empty, s, 0, false); }
                    }
                    else Raise(ValueDrag, PointF.Empty, dx, 0, false);
                    break;
                case Drag.CropMove:
                    {
                        float nx = (e.X - down.X) / r.Width, ny = (e.Y - down.Y) / r.Height;
                        RectangleF c = cropStart;
                        c.X = Math.Max(0, Math.Min(1 - c.Width, cropStart.X + nx));
                        c.Y = Math.Max(0, Math.Min(1 - c.Height, cropStart.Y + ny));
                        CropRect = c; Invalidate(); Raise(CropChanged, PointF.Empty, 0, 0, false);
                        break;
                    }
                case Drag.CropHandle: DragCrop(e.Location, r); break;
                case Drag.Point:
                    if (dragIdx >= 0 && dragIdx < Points.Count)
                    {
                        PointF n = ToNorm(e.Location);
                        Points[dragIdx] = new PointF(Ops.C01(n.X), Ops.C01(n.Y));
                        Invalidate(); Raise(PointMoved, Points[dragIdx], 0, dragIdx, false);
                    }
                    break;
                case Drag.Center:
                    {
                        PointF n = ToNorm(e.Location);
                        Center = new PointF(Ops.C01(n.X), Ops.C01(n.Y));
                        Invalidate(); Raise(CenterMoved, Center, 0, 0, false);
                        break;
                    }
                case Drag.EllX:
                case Drag.EllY:
                case Drag.EllRot:
                    {
                        PointF c = ToScreen(Center);
                        float vx = e.X - c.X, vy = e.Y - c.Y;
                        double a = -Angle * Math.PI / 180;
                        float lx = (float)(vx * Math.Cos(a) - vy * Math.Sin(a)), ly = (float)(vx * Math.Sin(a) + vy * Math.Cos(a));
                        if (drag == Drag.EllX) RadX = Math.Max(0.02f, Math.Abs(lx) / MinDimScreen);
                        else if (drag == Drag.EllY) RadY = Math.Max(0.01f, Math.Abs(ly) / MinDimScreen);
                        else Angle = (float)(Math.Atan2(vy, vx) * 180 / Math.PI);
                        Invalidate(); Raise(EllipseChanged, Center, 0, 0, false);
                        break;
                    }
                case Drag.ZoneX:
                case Drag.ZoneY:
                case Drag.ZoneRot:
                    if (HasZone(dragIdx))
                    {
                        PointF c = ToScreen(Points[dragIdx]);
                        float vx = e.X - c.X, vy = e.Y - c.Y;
                        double a = -PointAngles[dragIdx] * Math.PI / 180;
                        float lx = (float)(vx * Math.Cos(a) - vy * Math.Sin(a)), ly = (float)(vx * Math.Sin(a) + vy * Math.Cos(a));
                        float k = 1f / (MinDimScreen * ZoneScale);
                        if (drag == Drag.ZoneX) PointRadii[dragIdx] = Math.Max(0.01f, Math.Min(1.5f, Math.Abs(lx) * k));
                        else if (drag == Drag.ZoneY) PointRadiiY[dragIdx] = Math.Max(0.01f, Math.Min(1.5f, Math.Abs(ly) * k));
                        else PointAngles[dragIdx] = (float)(Math.Atan2(vy, vx) * 180 / Math.PI);
                        Invalidate(); RaiseZone(false);
                    }
                    break;
                case Drag.Brush:
                    if (curStroke != null)
                    {
                        PointF n = ToNorm(e.Location);
                        PointF lp = curStroke.Pts[curStroke.Pts.Count - 1];
                        if (Math.Abs(n.X - lp.X) * r.Width + Math.Abs(n.Y - lp.Y) * r.Height > 2)
                        {
                            curStroke.Pts.Add(n);
                            Raise(StrokeChanged, n, 0, BrushTarget.Count - 1, false);
                        }
                    }
                    break;
                case Drag.Quad:
                    if (dragIdx >= 0)
                    {
                        Quad[dragIdx] = ToNorm(e.Location);
                        Invalidate(); Raise(QuadChanged, Quad[dragIdx], 0, dragIdx, false);
                    }
                    break;
                case Drag.Move:
                    Raise(MoveDrag, new PointF(dx / r.Width, dy / r.Height), 0, 0, false);
                    break;
            }
            last = e.Location;
            if (Mode == CanvasMode.Brush || Mode == CanvasMode.Pick) Invalidate();
            base.OnMouseMove(e);
        }

        bool adjustVertical;

        void DragCrop(Point p, RectangleF r)
        {
            PointF n = new PointF(Ops.C01((p.X - r.X) / r.Width), Ops.C01((p.Y - r.Y) / r.Height));
            float x0 = cropStart.Left, y0 = cropStart.Top, x1 = cropStart.Right, y1 = cropStart.Bottom;
            int h = cropHandle;
            if (h == 0 || h == 3 || h == 7) x0 = Math.Min(n.X, x1 - 0.02f);
            if (h == 1 || h == 2 || h == 5) x1 = Math.Max(n.X, x0 + 0.02f);
            if (h == 0 || h == 1 || h == 4) y0 = Math.Min(n.Y, y1 - 0.02f);
            if (h == 2 || h == 3 || h == 6) y1 = Math.Max(n.Y, y0 + 0.02f);
            if (CropAspect > 0 && img != null)
            {
                // keep the ratio in pixel space: adjust the dependent side
                float ar = CropAspect * img.Height / img.Width; // normalized w/h
                float w = x1 - x0, hh = y1 - y0;
                bool horizontalDriver = h == 5 || h == 7 || (h < 4 && w / ar > hh);
                if (horizontalDriver) hh = w / ar; else w = hh * ar;
                if (h == 0) { x0 = x1 - w; y0 = y1 - hh; }
                else if (h == 1) { x1 = x0 + w; y0 = y1 - hh; }
                else if (h == 2) { x1 = x0 + w; y1 = y0 + hh; }
                else if (h == 3) { x0 = x1 - w; y1 = y0 + hh; }
                else if (h == 4 || h == 6) { float cx = (x0 + x1) / 2; x0 = cx - w / 2; x1 = cx + w / 2; if (h == 4) y0 = y1 - hh; else y1 = y0 + hh; }
                else { float cy = (y0 + y1) / 2; y0 = cy - hh / 2; y1 = cy + hh / 2; if (h == 7) x0 = x1 - w; else x1 = x0 + w; }
                if (x0 < 0 || y0 < 0 || x1 > 1 || y1 > 1) return;
            }
            CropRect = RectangleF.FromLTRB(x0, y0, x1, y1);
            Invalidate();
            Raise(CropChanged, PointF.Empty, 0, 0, false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Drag d = drag;
            drag = Drag.None;
            if (d == Drag.Undecided && img != null)
            {
                PointF n = ToNorm(e.Location);
                bool inside = n.X >= 0 && n.Y >= 0 && n.X <= 1 && n.Y <= 1;
                if (Mode == CanvasMode.Points && inside) Raise(PointAdded, n, 0, Points.Count, true);
                else if (inside) Raise(ImageClick, n, 0, 0, true);
            }
            if (d == Drag.Adjust) Raise(ValueDrag, PointF.Empty, 0, 0, true);
            if (d == Drag.Brush) { curStroke = null; Raise(StrokeChanged, PointF.Empty, 0, BrushTarget == null ? 0 : BrushTarget.Count - 1, true); }
            if (d == Drag.Point) Raise(PointMoved, dragIdx >= 0 && dragIdx < Points.Count ? Points[dragIdx] : PointF.Empty, 0, dragIdx, true);
            if (d == Drag.Center) Raise(CenterMoved, Center, 0, 0, true);
            if (d == Drag.EllX || d == Drag.EllY || d == Drag.EllRot) Raise(EllipseChanged, Center, 0, 0, true);
            if (d == Drag.ZoneX || d == Drag.ZoneY || d == Drag.ZoneRot) RaiseZone(true);
            if (d == Drag.CropHandle || d == Drag.CropMove) Raise(CropChanged, PointF.Empty, 0, 0, true);
            if (d == Drag.Quad) Raise(QuadChanged, Quad[dragIdx], 0, dragIdx, true);
            if (d == Drag.Move) Raise(MoveDrag, PointF.Empty, 0, 0, true);
            dragIdx = -1;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle) { Fit(); return; }
            if (img != null && e.Button == MouseButtons.Left) Raise(ImageDoubleClick, ToNorm(e.Location), 0, 0, true);
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = new PointF(-9999, -9999); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (img == null) return;
            if (Mode == CanvasMode.Brush && (ModifierKeys & Keys.Control) == 0 && (ModifierKeys & Keys.Alt) != 0)
            {
                BrushSize = Math.Max(4, Math.Min(400, BrushSize * (e.Delta > 0 ? 1.12f : 0.89f)));
                Invalidate(); return;
            }
            RectangleF r = ImgRect();
            float one = (SourceWidth > 0 ? SourceWidth : img.Width) / (float)img.Width; // zoom value of 100%
            float nz = Math.Max(0.02f, Math.Min(8f * one, zoom * (e.Delta > 0 ? 1.18f : 1 / 1.18f)));
            // stop exactly on 100% when passing through it
            if ((zoom < one && nz > one) || (zoom > one && nz < one)) nz = one;
            float ix = (e.X - r.X) / zoom, iy = (e.Y - r.Y) / zoom;
            fit = false;
            zoom = nz;
            float w = img.Width * zoom, h = img.Height * zoom;
            float nx = e.X - ix * zoom, ny = e.Y - iy * zoom;
            pan = new PointF(nx - (Width - w) / 2, ny - (Height - h) / 2);
            Invalidate();
            RaiseView();
        }

        public float ZoomLevel { get { ImgRect(); return zoom; } }
    }

    // Slider styled like a mobile photo editor: bipolar fill from zero, value on the right.
    public class ParamSlider : Control
    {
        public string Key = "", Label = "";
        public float Min = -100, Max = 100, Default, Step = 1;
        float val;
        public bool Active;
        public string Suffix = "";
        public event EventHandler<CanvasEventArgs> ValueChanged;
        bool dragging;

        public ParamSlider()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 52; BackColor = Theme.Panel; Cursor = Cursors.Hand;
        }

        public float Value
        {
            get { return val; }
            set { float v = Math.Max(Min, Math.Min(Max, value)); if (v != val) { val = v; Invalidate(); } }
        }

        public void SetValue(float v, bool final)
        {
            float old = val;
            Value = (float)(Math.Round(v / Step) * Step);
            if (old != val || final) { EventHandler<CanvasEventArgs> h = ValueChanged; if (h != null) h(this, new CanvasEventArgs(PointF.Empty, val, 0, final)); }
        }

        RectangleF Track { get { return new RectangleF(14, Height - 18, Width - 28, 4); } }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font f = new Font("Segoe UI", 9.5f))
            using (Font fv = new Font("Segoe UI Semibold", 9.5f))
            using (SolidBrush tb = new SolidBrush(!Enabled ? Theme.Dim : (Active ? Theme.Accent : Theme.Text)))
            using (SolidBrush vb = new SolidBrush(val != Default ? Theme.Accent : Theme.Dim))
            {
                g.DrawString(Label, f, tb, 10, 6);
                string vs = (Min < 0 && val > 0 ? "+" : "") + val.ToString(Step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture) + Suffix;
                SizeF s = g.MeasureString(vs, fv);
                g.DrawString(vs, fv, vb, Width - s.Width - 10, 6);
            }
            RectangleF t = Track;
            using (SolidBrush b = new SolidBrush(Theme.Line)) g.FillRectangle(b, t);
            float z = Min < 0 && Max > 0 ? 0 : Min;
            float px = t.X + (val - Min) / (Max - Min) * t.Width, pz = t.X + (z - Min) / (Max - Min) * t.Width;
            using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Math.Min(px, pz), t.Y, Math.Abs(px - pz), t.Height);
            if (Min < 0 && Max > 0) using (SolidBrush b = new SolidBrush(Theme.Dim)) g.FillRectangle(b, pz - 1, t.Y - 3, 2, t.Height + 6);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, px - 8, t.Y + 2 - 8, 16, 16);
            if (Focused) using (Pen p = new Pen(Theme.Accent, 2)) g.DrawEllipse(p, px - 8, t.Y + 2 - 8, 16, 16);
        }

        float FromX(int x) { RectangleF t = Track; return Min + (Max - Min) * Ops.C01((x - t.X) / t.Width); }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { dragging = true; SetValue(FromX(e.X), false); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetValue(FromX(e.X), false); }
        protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; SetValue(val, true); } }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { SetValue(Default, true); }
        protected override void OnMouseWheel(MouseEventArgs e) { SetValue(val + (e.Delta > 0 ? Step : -Step), true); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            float st = e.Shift ? Step * 10 : Step;
            if (e.KeyCode == Keys.Left) SetValue(val - st, true);
            if (e.KeyCode == Keys.Right) SetValue(val + st, true);
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // Tile with an icon glyph or a thumbnail, used for tools, styles and looks.
    public class Tile : Control
    {
        public string Glyph = "";
        public string Caption = "";
        static Font captionFont;
        static Font CaptionFont { get { return captionFont ?? (captionFont = new Font("Segoe UI", 8.5f)); } }
        public Bitmap Thumb;
        public bool Selected;
        public bool Stored;          // a check mark in the corner: this choice is saved in the edit
        public Color Swatch = Color.Empty;
        public bool Placeholder;     // a thumbnail is being prepared: draw an empty frame meanwhile
        public int ThumbJob;         // id of the latest thumbnail job, older results are dropped
        bool hot;

        // ---- fade-in of a newly prepared thumbnail ----
        DateTime fadeStart = DateTime.MinValue;
        const double FadeMs = 320;
        static readonly List<Tile> fading = new List<Tile>();
        static Timer fadeTimer;

        int pendingDelay = -1; // thumbnail ready while the tile is hidden: fade in when it is shown

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && pendingDelay >= 0) { int d = pendingDelay; pendingDelay = -1; FadeIn(Thumb, d); }
        }

        public void FadeIn(Bitmap b, int delayMs)
        {
            Thumb = b; Placeholder = false;
            if (!Visible) { pendingDelay = delayMs; fadeStart = DateTime.MaxValue; return; }
            fadeStart = DateTime.Now.AddMilliseconds(delayMs);
            if (!fading.Contains(this)) fading.Add(this);
            if (fadeTimer == null)
            {
                fadeTimer = new Timer(); fadeTimer.Interval = 15;
                fadeTimer.Tick += (s, e) =>
                {
                    DateTime now = DateTime.Now;
                    for (int i = fading.Count - 1; i >= 0; i--)
                    {
                        Tile t = fading[i];
                        if (t.IsDisposed || (now - t.fadeStart).TotalMilliseconds > FadeMs + 30) { fading.RemoveAt(i); if (!t.IsDisposed) t.Invalidate(); }
                        else if (now >= t.fadeStart) t.Invalidate();
                    }
                    if (fading.Count == 0) fadeTimer.Stop();
                };
            }
            fadeTimer.Start();
            Invalidate();
        }

        float FadeLevel()
        {
            if (fadeStart == DateTime.MinValue) return 1f;
            if (fadeStart == DateTime.MaxValue) return 0f;
            double t =(DateTime.Now - fadeStart).TotalMilliseconds / FadeMs;
            if (t >= 1) return 1f;
            if (t <= 0) return 0f;
            return (float)(1 - Math.Pow(1 - t, 3)); // ease-out
        }

        public Tile()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel; Cursor = Cursors.Hand; Margin = new Padding(4);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath p = ImageCanvas.Rounded(r, 8))
            {
                if (hot || Selected) using (SolidBrush b = new SolidBrush(Selected ? Theme.AccentDim : Theme.Panel2)) g.FillPath(b, p);
                if (Selected) using (Pen pen = new Pen(Theme.Accent, 2)) g.DrawPath(pen, p);
            }
            StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            // the caption strip follows the real text height: fonts grow with Windows display scaling (125 %,
            // 150 %...) and a fixed strip cut the text off on such screens
            float capH = string.IsNullOrEmpty(Caption) ? 0 : CaptionFont.GetHeight(g) + Ui.Px(3);
            bool captionOnly = Thumb == null && !Placeholder && Swatch == Color.Empty && string.IsNullOrEmpty(Glyph);
            if (Thumb != null || Placeholder)
            {
                float avail = Height - capH - 10, aw = Width - 10;
                // placeholder frame with the photo's shape (or a 3:2 frame before the first thumbnail)
                float tw = Thumb != null ? Thumb.Width : 3, th = Thumb != null ? Thumb.Height : 2;
                float k = Math.Min(aw / tw, avail / th);
                float w = tw * k, h = th * k;
                RectangleF dst = new RectangleF((Width - w) / 2, 5 + (avail - h) / 2, w, h);
                float a = Thumb != null ? FadeLevel() : 0f;
                if (a < 1f) using (SolidBrush pb = new SolidBrush(Theme.Panel2)) g.FillRectangle(pb, dst);
                if (Thumb != null && a > 0f)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    if (a >= 1f) g.DrawImage(Thumb, dst);
                    else
                    {
                        // fade in while settling a few pixels down into place
                        ColorMatrix cm = new ColorMatrix(); cm.Matrix33 = a;
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            float dy = (1 - a) * -6;
                            g.DrawImage(Thumb, new Rectangle((int)dst.X, (int)(dst.Y + dy), (int)Math.Round(dst.Width), (int)Math.Round(dst.Height)), 0, 0, Thumb.Width, Thumb.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
            }
            else if (Swatch != Color.Empty)
            {
                float d = Math.Min(Width, Height - capH) - 16;
                using (SolidBrush b = new SolidBrush(Swatch)) g.FillEllipse(b, (Width - d) / 2, 6, d, d);
                using (Pen pen = new Pen(Theme.Line, 1)) g.DrawEllipse(pen, (Width - d) / 2, 6, d, d);
            }
            else if (!string.IsNullOrEmpty(Glyph))
            {
                // Theme.Icon scales by itself, so the room left above the caption is converted back to 96 dpi units
                float room = (Height - capH - Ui.Px(8)) / Theme.Scale;
                using (Font fi = Theme.Icon(Math.Max(10f, Math.Min(30f, room * 0.62f))))
                using (SolidBrush b = new SolidBrush(Selected ? Color.White : Theme.Text))
                {
                    SizeF s = g.MeasureString(Glyph, fi);
                    g.DrawString(Glyph, fi, b, (Width - s.Width) / 2, Math.Max(0f, (Height - capH - s.Height) / 2 + Ui.Px(2)));
                }
            }
            if (capH > 0)
                using (SolidBrush b = new SolidBrush(Selected ? Color.White : Theme.Dim))
                {
                    RectangleF cr = captionOnly ? new RectangleF(2, 0, Width - 4, Height) : new RectangleF(2, Height - capH - Ui.Px(2), Width - 4, capH);
                    if (captionOnly) sf.LineAlignment = StringAlignment.Center;
                    // long names on narrow tiles get a slightly smaller font before any "..."
                    Font cf = CaptionFont;
                    for (float pt = 8f; pt >= 6.5f && g.MeasureString(Caption, cf).Width > cr.Width; pt -= 0.5f)
                    {
                        if (cf != CaptionFont) cf.Dispose();
                        cf = new Font("Segoe UI", pt);
                    }
                    g.DrawString(Caption, cf, b, cr, sf);
                    if (cf != CaptionFont) cf.Dispose();
                }
            if (Stored)
            {
                float d = Ui.Px(15), x = Width - d - Ui.Px(4), y = Ui.Px(4);
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, x, y, d, d);
                using (Pen pen = new Pen(Color.White, Math.Max(1.5f, Ui.Px(2))))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new PointF[] { new PointF(x + d * 0.27f, y + d * 0.52f), new PointF(x + d * 0.44f, y + d * 0.69f), new PointF(x + d * 0.74f, y + d * 0.33f) });
                }
            }
        }
    }

    // Flat button with an icon glyph and optional text.
    public class IconButton : Control
    {
        public string Glyph = "";
        public bool Toggled;
        public bool Primary;
        bool hot, pressed;
        public event EventHandler Pressed, Released;

        public IconButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel; ForeColor = Theme.Text; Cursor = Cursors.Hand; Size = new Size(44, 40);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); EventHandler h = Pressed; if (h != null) h(this, e); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); EventHandler h = Released; if (h != null) h(this, e); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF r = new RectangleF(2, 2, Width - 5, Height - 5);
            Color bg = Primary ? Theme.Accent : (Toggled ? Theme.AccentDim : (pressed ? Theme.Line : (hot ? Theme.Panel2 : BackColor)));
            using (GraphicsPath p = ImageCanvas.Rounded(r, 8)) using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
            Color fg = !Enabled ? Color.FromArgb(80, 84, 90) : (Primary || Toggled ? Color.White : ForeColor);
            using (Font fi = Theme.Icon(18))
            using (Font ft = new Font("Segoe UI Semibold", 9f))
            using (SolidBrush b = new SolidBrush(fg))
            {
                SizeF gs = string.IsNullOrEmpty(Glyph) ? SizeF.Empty : g.MeasureString(Glyph, fi);
                SizeF ts = string.IsNullOrEmpty(Text) ? SizeF.Empty : g.MeasureString(Text, ft);
                float gap = gs.Width > 0 && ts.Width > 0 ? 2 : 0;
                float x = (Width - gs.Width - ts.Width - gap) / 2;
                if (gs.Width > 0) g.DrawString(Glyph, fi, b, x, (Height - gs.Height) / 2 + 1);
                if (ts.Width > 0) g.DrawString(Text, ft, b, x + gs.Width + gap, (Height - ts.Height) / 2);
            }
        }
    }

    // Curve editor with histogram backdrop. Points are normalized (x,y), y up.
    public class CurveEditor : Control
    {
        public List<PointF> Pts = new List<PointF>();
        public Color CurveColor = Color.White;
        public int[] Histogram;
        public bool ShowCurve = true; // false: histogram only (photo details)
        public int[][] ChannelHistograms; // red, green, blue drawn together like in pro editors (instead of Histogram)
        public bool AddOnCurve;           // a click adds a point on the curve itself, which therefore does not jump
        public event EventHandler<CanvasEventArgs> CurveChanged;
        public bool Dragging { get { return drag >= 0; } }
        int drag = -1;

        public CurveEditor()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel2; Cursor = Cursors.Cross;
            Reset();
        }

        public void Reset() { Pts.Clear(); Pts.Add(new PointF(0, 0)); Pts.Add(new PointF(1, 1)); Invalidate(); }

        public string PointsString
        {
            get
            {
                List<string> s = new List<string>();
                foreach (PointF p in Pts) s.Add(p.X.ToString("0.####", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.####", CultureInfo.InvariantCulture));
                return string.Join(";", s.ToArray());
            }
            set
            {
                Pts.Clear();
                foreach (float[] p in Adjust.ParsePoints(value)) Pts.Add(new PointF(p[0], p[1]));
                if (Pts.Count < 2) Reset();
                Invalidate();
            }
        }

        RectangleF Area { get { return new RectangleF(10, 10, Width - 20, Height - 20); } }
        PointF ToScr(PointF p) { RectangleF a = Area; return new PointF(a.X + p.X * a.Width, a.Bottom - p.Y * a.Height); }
        PointF FromScr(Point p) { RectangleF a = Area; return new PointF(Ops.C01((p.X - a.X) / a.Width), Ops.C01((a.Bottom - p.Y) / a.Height)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF a = Area;
            if (ChannelHistograms != null)
            {
                int mx = 1;
                foreach (int[] h in ChannelHistograms) for (int i = 2; i < 254; i++) mx = Math.Max(mx, h[i]);
                Color[] cols = { Color.FromArgb(90, 235, 70, 70), Color.FromArgb(90, 70, 220, 90), Color.FromArgb(90, 80, 130, 255) };
                for (int c = 0; c < 3; c++)
                {
                    PointF[] poly = new PointF[258];
                    poly[0] = new PointF(a.X, a.Bottom);
                    for (int i = 0; i < 256; i++)
                    {
                        float h = (float)Math.Sqrt(Math.Min(1.0, ChannelHistograms[c][i] / (double)mx)) * a.Height;
                        poly[i + 1] = new PointF(a.X + (i + 0.5f) * a.Width / 256f, a.Bottom - h);
                    }
                    poly[257] = new PointF(a.Right, a.Bottom);
                    using (SolidBrush b = new SolidBrush(cols[c])) g.FillPolygon(b, poly);
                }
            }
            else if (Histogram != null)
            {
                int mx = 1; foreach (int v in Histogram) mx = Math.Max(mx, v);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                    for (int i = 0; i < 256; i++)
                    {
                        float h = (float)Math.Sqrt(Histogram[i] / (double)mx) * a.Height;
                        g.FillRectangle(b, a.X + i * a.Width / 256f, a.Bottom - h, a.Width / 256f + 0.6f, h);
                    }
            }
            using (Pen p = new Pen(Theme.Line, 1))
            {
                for (int i = 1; i < 4; i++) { g.DrawLine(p, a.X + a.Width * i / 4, a.Y, a.X + a.Width * i / 4, a.Bottom); g.DrawLine(p, a.X, a.Y + a.Height * i / 4, a.Right, a.Y + a.Height * i / 4); }
                g.DrawRectangle(p, a.X, a.Y, a.Width, a.Height);
                if (ShowCurve) { p.DashStyle = DashStyle.Dot; g.DrawLine(p, a.X, a.Bottom, a.Right, a.Y); }
            }
            if (!ShowCurve) return;
            float[] lut = Adjust.Lut(PointsString, 256);
            PointF[] cp = new PointF[256];
            for (int i = 0; i < 256; i++) cp[i] = ToScr(new PointF(i / 255f, lut[i]));
            using (Pen p = new Pen(CurveColor, 2f)) g.DrawLines(p, cp);
            for (int i = 0; i < Pts.Count; i++)
            {
                PointF s = ToScr(Pts[i]);
                using (SolidBrush b = new SolidBrush(i == drag ? Theme.Accent : Color.White)) g.FillEllipse(b, s.X - 6, s.Y - 6, 12, 12);
                using (Pen p = new Pen(Color.Black, 1)) g.DrawEllipse(p, s.X - 6, s.Y - 6, 12, 12);
            }
        }

        float D(PointF a, Point b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            drag = -1;
            for (int i = 0; i < Pts.Count; i++) if (D(ToScr(Pts[i]), e.Location) < 10) { drag = i; break; }
            if (e.Button == MouseButtons.Right)
            {
                if (drag > 0 && drag < Pts.Count - 1) { Pts.RemoveAt(drag); drag = -1; Fire(true); }
                return;
            }
            if (drag < 0)
            {
                PointF n = FromScr(e.Location);
                int idx = 0; while (idx < Pts.Count && Pts[idx].X < n.X) idx++;
                if (idx == 0 || idx == Pts.Count) return;
                if (AddOnCurve) { float[] lut = Adjust.Lut(PointsString, 1024); n.Y = lut[(int)Math.Round(n.X * 1023)]; }
                Pts.Insert(idx, n); drag = idx; Fire(false);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag < 0 || e.Button != MouseButtons.Left) return;
            PointF n = FromScr(e.Location);
            float lo = drag == 0 ? 0 : Pts[drag - 1].X + 0.01f, hi = drag == Pts.Count - 1 ? 1 : Pts[drag + 1].X - 0.01f;
            if (drag == 0) n.X = Math.Min(n.X, hi);
            else if (drag == Pts.Count - 1) n.X = Math.Max(n.X, lo);
            else n.X = Math.Max(lo, Math.Min(hi, n.X));
            Pts[drag] = n;
            Fire(false);
        }

        protected override void OnMouseUp(MouseEventArgs e) { if (drag >= 0) Fire(true); drag = -1; Invalidate(); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            for (int i = 1; i < Pts.Count - 1; i++) if (D(ToScr(Pts[i]), e.Location) < 10) { Pts.RemoveAt(i); Fire(true); return; }
        }

        void Fire(bool fin) { Invalidate(); EventHandler<CanvasEventArgs> h = CurveChanged; if (h != null) h(this, new CanvasEventArgs(PointF.Empty, 0, 0, fin)); }
    }
}

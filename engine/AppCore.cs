using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PixTune
{
    // A slider of a tool: key, label, range, default, step and unit.
    public class Prm
    {
        public string K = "", L = "", Suf = "";
        public double Min = -100, Max = 100, Def, Step = 1;
    }

    // A button of a button row; Click receives the button (toggle buttons are already flipped).
    public class Btn
    {
        public string Text = "", Glyph = "";
        public bool Toggle, Checked;
        public Action<IconButton> Click;
    }

    // A tile of a tile group; Tag comes back on click as ((TileTag)tile.Tag).Data.
    public class TileSpec
    {
        public string Caption = "", Glyph = "";
        public Bitmap Thumb;
        public Color Swatch = Color.Empty;
        public object Tag;
    }

    // One tool: what the panel shows (styles, sliders, Setup) and how the photo reacts (canvas mode, handlers).
    public class ToolDef
    {
        public string Id = "", Name = "", Glyph = "", Canvas = "Adjust";
        public List<Prm> Params = new List<Prm>();
        public string[] Styles;
        public double StyleW, StyleH;          // style tile size (0: 74 x 82)
        public bool Hidden, Ellipse, HideOutput, RenderOnStrokeEnd;
        public Action Setup, OnActive, OnPointSelected, OnEllipse, OnCrop, OnDoubleClick, BeforeApply;
        public Func<string, double> GetParam;   // for tools whose values are not plain step values
        public Action<string, double> SetParam;
        public Action<string> OnParam;          // after a value changed
        public Action<PointF> OnClick, OnPointAdded, OnPicked, OnMove;
        public Action<int, PointF, bool> OnPointMoved;
        public Action<int, PointF, float, bool> OnPointResized;
        public Action<int, PointF> OnQuad;
    }

    // Settings kept in %APPDATA%\PixTune\settings.json.
    public class AppSettings
    {
        public string Format = "jpg";
        public int Quality = 95, LongSide = 0, PreviewSize = 1600;
        public bool KeepExif = true, UseGpu = true, ShowHistogram, HealAi, MaskFolded;
        public string ExportDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PixTune");
        public string LastDir = "", Appearance = "Dark";
        public string[] Favorites = new string[0];
        public string[] GpuModels = new string[0];   // GPU check results (GpuCheck)
        public string GpuSpeed = "";                 // image tools on the card or the processor (GpuCheck.MeasureSpeed)

        public static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixTune\\settings.json");

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (d == null) return s;
                object o;
                if (d.TryGetValue("Format", out o) && o != null) s.Format = Convert.ToString(o, CultureInfo.InvariantCulture);
                if (d.TryGetValue("Quality", out o) && o != null) s.Quality = Convert.ToInt32(o, CultureInfo.InvariantCulture);
                if (d.TryGetValue("LongSide", out o) && o != null) s.LongSide = Convert.ToInt32(o, CultureInfo.InvariantCulture);
                if (d.TryGetValue("PreviewSize", out o) && o != null) s.PreviewSize = Convert.ToInt32(o, CultureInfo.InvariantCulture);
                if (d.TryGetValue("KeepExif", out o) && o != null) s.KeepExif = Convert.ToBoolean(o);
                if (d.TryGetValue("UseGpu", out o) && o != null) s.UseGpu = Convert.ToBoolean(o);
                if (d.TryGetValue("ShowHistogram", out o) && o != null) s.ShowHistogram = Convert.ToBoolean(o);
                if (d.TryGetValue("HealAi", out o) && o != null) s.HealAi = Convert.ToBoolean(o);
                if (d.TryGetValue("MaskFolded", out o) && o != null) s.MaskFolded = Convert.ToBoolean(o);
                if (d.TryGetValue("ExportDir", out o) && o != null) s.ExportDir = Convert.ToString(o);
                if (d.TryGetValue("LastDir", out o) && o != null) s.LastDir = Convert.ToString(o);
                if (d.TryGetValue("Appearance", out o) && o != null) s.Appearance = Convert.ToString(o);
                object[] fav = (d.TryGetValue("Favorites", out o) ? o : null) as object[];
                if (fav != null) { List<string> l = new List<string>(); foreach (object f in fav) if (f != null) l.Add(Convert.ToString(f)); s.Favorites = l.ToArray(); }
                object[] gm = (d.TryGetValue("GpuModels", out o) ? o : null) as object[];
                if (gm != null) { List<string> l = new List<string>(); foreach (object g in gm) if (g != null) l.Add(Convert.ToString(g)); s.GpuModels = l.ToArray(); }
                if (d.TryGetValue("GpuSpeed", out o) && o != null) s.GpuSpeed = Convert.ToString(o);
            }
            catch (Exception) { }
            return s;
        }

        public void Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["Format"] = Format; d["Quality"] = Quality; d["LongSide"] = LongSide; d["KeepExif"] = KeepExif; d["PreviewSize"] = PreviewSize;
            d["UseGpu"] = UseGpu; d["ExportDir"] = ExportDir; d["LastDir"] = LastDir; d["Appearance"] = Appearance;
            d["ShowHistogram"] = ShowHistogram; d["Favorites"] = Favorites; d["HealAi"] = HealAi; d["MaskFolded"] = MaskFolded;
            d["GpuModels"] = GpuModels; d["GpuSpeed"] = GpuSpeed;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(d));
        }

        // a text that changes whenever a setting shown in the Export tab changes
        public string Signature()
        {
            return string.Join("|", new string[] { Format, Quality.ToString(), LongSide.ToString(), KeepExif.ToString(), PreviewSize.ToString(), UseGpu.ToString(), ExportDir, Appearance });
        }
    }

    // The application: one window, the side panel and the tool being used. Its public members keep the names the
    // former script used, so test scripts drive it the same way.
    public partial class App
    {
        public static App Current;
        public static readonly Dictionary<string, ToolDef> Tools = new Dictionary<string, ToolDef>();
        public static string Root = "";

        // ---------------- state ----------------
        public AppSettings Settings;
        public Document Doc = new Document();
        public string Mode = "home", Tab = "tools";
        public Step Step;
        public ToolDef ToolDef;
        public Dictionary<string, ParamSlider> Sliders = new Dictionary<string, ParamSlider>();
        public List<string> ParamKeys = new List<string>();
        public int Active;
        public Img InputCache;
        public bool Dragging;
        public double DragVal;
        public bool ShowMask, Eraser;
        public double BrushValue = 5, MaskScale = 10;
        public string ClipSteps;
        public float Scale = 1f;
        public bool FullShown, GpuAnnounced;
        public string PendingKind;
        public string ReturnTo;
        public readonly List<string> ErrorLog = new List<string>();

        // ---------------- window parts ----------------
        public Form Form;
        public ImageCanvas Canvas;
        public Renderer Renderer;
        public Control Body, WorkBody, Head, Foot;
        public Icon AppIcon;
        public ToolTip Tips = new ToolTip();

        public static string G(int code)
        {
            // the half-filled circle only exists in the Windows 11 icon font
            if (code == 0xE7A1 && Theme.LegacyIcons) code = 0xE793;
            return ((char)code).ToString();
        }

        public static Prm P(string k, string l, double min = -100, double max = 100, double def = 0, double step = 1, string suf = "")
        {
            Prm p = new Prm(); p.K = k; p.L = l; p.Min = min; p.Max = max; p.Def = def; p.Step = step; p.Suf = suf ?? "";
            return p;
        }

        public static void Register(ToolDef t) { Tools[t.Id] = t; }

        public int S(double v) { return (int)Math.Round(v * Scale); }

        // ---------------- deferred actions ----------------
        // Click actions run after the click finishes, so a handler may rebuild the panel that holds its button.
        readonly Queue<Action> queue = new Queue<Action>();
        public void Later(Action a)
        {
            queue.Enqueue(a);
            if (Form != null && Form.IsHandleCreated) Form.BeginInvoke((MethodInvoker)RunQueue);
        }
        // runs a on the window's thread, from any thread (dropped when the window is gone)
        public void OnUi(Action a)
        {
            try { if (Form != null && Form.IsHandleCreated && !Form.IsDisposed) Form.BeginInvoke((MethodInvoker)delegate { Safe(a); }); }
            catch (InvalidOperationException) { }
        }
        // nothing left to do: no render under way, no mask overlay being computed and no deferred action waiting
        public bool Idle { get { return Canvas != null && !Canvas.Busy && !Renderer.Busy && MaskJobs == 0 && queue.Count == 0 && !ViewRenderPending; } }

        public void RunQueue()
        {
            while (queue.Count > 0)
            {
                Action a = queue.Dequeue();
                try { a(); } catch (Exception ex) { ShowError(ex); }
            }
        }

        // ---------------- status line and errors ----------------
        Timer statusTimer;
        public void SetStatus(string msg)
        {
            if (Canvas == null) return;
            Canvas.Status = msg ?? ""; Canvas.Invalidate();
            if (statusTimer == null)
            {
                statusTimer = new Timer(); statusTimer.Interval = 7000;
                statusTimer.Tick += (s, e) => { statusTimer.Stop(); Canvas.Status = ""; Canvas.Invalidate(); };
            }
            statusTimer.Stop(); if (!string.IsNullOrEmpty(msg)) statusTimer.Start();
        }

        public void ShowError(Exception ex)
        {
            Exception e = ex;
            while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
            SetStatus("Error: " + e.Message);
            ErrorLog.Add(e.Message + "\n" + e.StackTrace);
        }

        // A picture of the window saved as PNG: captured at once, encoded in the background (scripts take many).
        readonly List<System.Threading.Tasks.Task> shots = new List<System.Threading.Tasks.Task>();
        public void SaveScreenshot(string path)
        {
            Bitmap b = new Bitmap(Form.Width, Form.Height);
            Form.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
            lock (shots)
            {
                shots.RemoveAll(t => t.IsCompleted);
                shots.Add(System.Threading.Tasks.Task.Factory.StartNew(() => { using (b) b.Save(path, System.Drawing.Imaging.ImageFormat.Png); }));
            }
        }
        public void WaitScreenshots()
        {
            System.Threading.Tasks.Task[] all;
            lock (shots) all = shots.ToArray();
            try { System.Threading.Tasks.Task.WaitAll(all, 10000); } catch (Exception) { }
        }

        // runs a handler and reports what goes wrong instead of letting it escape to Windows Forms
        public void Safe(Action a) { try { a(); } catch (Exception ex) { ShowError(ex); } }

        // ---------------- panel building (in the current body) ----------------
        public int BodyW() { return SideWidth() - SystemInformation.VerticalScrollBarWidth - S(22); }

        public Label AddSection(string t) { return Ui.Section(Body, t, BodyW()); }
        public Label AddHint(string t) { return Ui.Hint(Body, t, BodyW()); }

        public static Btn B(string text, string glyph, Action<IconButton> click, bool toggle = false, bool isChecked = false)
        {
            Btn b = new Btn(); b.Text = text; b.Glyph = glyph; b.Click = click; b.Toggle = toggle; b.Checked = isChecked;
            return b;
        }

        public List<IconButton> AddButtons(params Btn[] specs)
        {
            ButtonRow row = Ui.Buttons(Body, BodyW());
            row.Specs = specs;
            List<IconButton> l = new List<IconButton>();
            foreach (Btn sp in specs) l.Add(row.Add(sp.Glyph, sp.Text, sp.Toggle, sp.Checked));
            row.ButtonClicked += OnRowButton;
            return l;
        }

        void OnRowButton(object s, CanvasEventArgs e)
        {
            ButtonRow row = (ButtonRow)s;
            // any setting of the tool (not of the mask panel) hides the mask being defined
            if (row.Parent != MaskBody) HideMaskEdit();
            Btn[] specs = row.Specs as Btn[];
            if (specs == null || e.Index >= specs.Length || specs[e.Index].Click == null) return;
            Action<IconButton> click = specs[e.Index].Click;
            IconButton b = (IconButton)row.Controls[e.Index];
            Later(() => click(b));
        }

        public static TileSpec T(string caption, string glyph = "", object tag = null, Bitmap thumb = null)
        {
            TileSpec t = new TileSpec(); t.Caption = caption; t.Glyph = glyph ?? ""; t.Tag = tag; t.Thumb = thumb;
            return t;
        }

        public TileGroup AddTiles(IList<TileSpec> items, int selected, double w, double h, Action<int, Tile> click)
        {
            TileGroup grp = Ui.Tiles(Body, BodyW());
            grp.Handler = click;
            int pw = Ui.Px(w), ph = Ui.Px(h);
            for (int i = 0; i < items.Count; i++)
            {
                TileSpec it = items[i];
                grp.Add(it.Caption, it.Glyph, it.Thumb, it.Swatch, it.Tag, pw, ph, i == selected);
            }
            grp.TileClicked += OnTileClicked;
            return grp;
        }

        void OnTileClicked(object s, CanvasEventArgs e)
        {
            TileGroup grp = (TileGroup)s;
            if (grp.Parent != MaskBody) HideMaskEdit();
            Action<int, Tile> click = grp.Handler as Action<int, Tile>;
            if (click == null) return;
            int i = e.Index;
            Tile t = (Tile)grp.Controls[i];
            Later(() => click(i, t));
        }

        public void SelectTileIn(TileGroup g, int i)
        {
            if (g == null || g.IsDisposed || i < 0 || i >= g.Controls.Count) return;
            g.ClearSelection();
            Tile t = (Tile)g.Controls[i]; t.Selected = true; t.Invalidate();
        }

        // ---------------- tool values ----------------
        public double GetParam(string k)
        {
            ToolDef def = ToolDef;
            if (def.GetParam != null && !k.StartsWith("_")) return def.GetParam(k);
            if (k.StartsWith("_")) { ParamSlider sl; return Sliders.TryGetValue(k, out sl) ? sl.Value : 0; }
            double d = 0;
            foreach (Prm p in def.Params) if (p.K == k) { d = p.Def; break; }
            return Step.Get(k, d);
        }

        public void SetParam(string k, double v)
        {
            ToolDef def = ToolDef;
            if (def.SetParam != null) { def.SetParam(k, v); if (!k.StartsWith("_") && def.Id != "selective" && def.Id != "color") Step.Set(k, v); }
            else if (!k.StartsWith("_")) Step.Set(k, v);
            if (def.OnParam != null) def.OnParam(k);
        }

        // several values at once (a dictionary of key to number), shown on their sliders as the tool kept them
        public void SetParams(IDictionary h)
        {
            HideMaskEdit();
            foreach (DictionaryEntry kv in h)
            {
                string k = Convert.ToString(kv.Key);
                SetParam(k, Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture));
                ParamSlider sl;
                if (Sliders.TryGetValue(k, out sl)) sl.Value = (float)GetParam(k);
            }
            UpdateHud();
            RequestRender();
        }
        public void SetParams(string k, double v) { Hashtable h = new Hashtable(); h[k] = v; SetParams(h); }

        public ParamSlider AddSlider(Prm p, bool noGesture = false)
        {
            float v = (float)(p.K.StartsWith("_") ? p.Def : GetParam(p.K));
            ParamSlider sl = Ui.Slider(Body, p.K, p.L, (float)p.Min, (float)p.Max, (float)p.Def, (float)p.Step, p.Suf ?? "", v, BodyW());
            Sliders[p.K] = sl;
            if (!noGesture) ParamKeys.Add(p.K);
            sl.ValueChanged += (s, e) => Safe(() => OnParamChanged(sl.Key, e.Value, e.Final));
            sl.MouseDown += (s, e) => { if (ParamKeys.Contains(sl.Key)) SetActive(sl.Key); };
            return sl;
        }

        // A gesture in progress (a slider or the photo dragged): frames of only what the screen shows until it ends.
        // The end is the gesture's own last event; a timer also ends it once no mouse button is held, so a final
        // render at full quality always follows.
        public bool LiveDrag;
        public int LiveEndMs = 300;   // how soon a gesture with no mouse button held is taken as over (tests lengthen it)
        Timer liveTimer;
        // While a gesture runs the memory collector avoids its long full passes, so frames come without pauses;
        // the usual mode is restored when the gesture ends. Renders ask for the same thing and the two overlap,
        // so both go through the one counter.
        public void BeginLive()
        {
            if (!LiveDrag) Renders.Begin();
            LiveDrag = true;
            if (liveTimer == null)
            {
                liveTimer = new Timer();
                liveTimer.Tick += (s, e) => { if (Control.MouseButtons == MouseButtons.None) Safe(() => EndLive(true)); };
            }
            liveTimer.Stop(); liveTimer.Interval = Math.Max(50, LiveEndMs); liveTimer.Start();
        }

        // render: whether to ask for the final render here (false when the caller renders anyway)
        public void EndLive(bool render)
        {
            if (liveTimer != null) liveTimer.Stop();
            if (!LiveDrag) return;
            LiveDrag = false;
            Renders.End();
            if (render) RequestRender();
        }

        void OnParamChanged(string k, double v, bool final)
        {
            if (final) EndLive(false); else BeginLive();
            HideMaskEdit();
            SetParam(k, v);
            if (ParamKeys.Contains(k)) SetActive(k);
            UpdateHud();
            if (!k.StartsWith("_")) RequestRender();
        }

        public void SetActive(string k)
        {
            int i = ParamKeys.IndexOf(k);
            if (i < 0) return;
            Active = i;
            foreach (string key in ParamKeys) { Sliders[key].Active = key == k; Sliders[key].Invalidate(); }
            if (ToolDef != null && ToolDef.OnActive != null) ToolDef.OnActive();
            UpdateHud();
        }

        public void UpdateHud()
        {
            if (Mode != "tool" || ParamKeys.Count == 0) return;
            ParamSlider sl = Sliders[ParamKeys[Active]];
            float v = sl.Value;
            string txt = sl.Step < 1 ? v.ToString("0.0", CultureInfo.InvariantCulture) : ((int)v).ToString(CultureInfo.InvariantCulture);
            if (sl.Min < 0 && v > 0) txt = "+" + txt;
            Canvas.Hud = sl.Label + " " + txt + sl.Suffix;
            Canvas.Invalidate();
        }

        public void SetCanvasMode(string m)
        {
            Canvas.Mode = (CanvasMode)Enum.Parse(typeof(CanvasMode), m);
            Canvas.Invalidate();
        }

        // the picture the tool works on (the result of the steps before it), kept while the tool is open
        public Img ToolInput()
        {
            if (InputCache == null) InputCache = Doc.PendingInput();
            return InputCache;
        }

        // ---------------- dialogs ----------------
        // Dialogs get the same dark chrome and control colours as the main window.
        public void DarkDialog(Form f)
        {
            f.BackColor = Theme.Panel; f.ForeColor = Theme.Text;
            f.Icon = AppIcon; f.ShowIcon = true;
            Stack<Control> st = new Stack<Control>();
            st.Push(f);
            while (st.Count > 0)
            {
                Control c = st.Pop();
                foreach (Control k in c.Controls) st.Push(k);
                Button b = c as Button;
                if (b != null)
                {
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = Theme.Panel2; b.ForeColor = Theme.Text; b.FlatAppearance.BorderColor = Theme.Line;
                    if (b.DialogResult == DialogResult.OK) { b.BackColor = Theme.Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderColor = Theme.Accent; }
                }
                else if (c is ComboBox) { ((ComboBox)c).FlatStyle = FlatStyle.Flat; c.BackColor = Theme.Panel2; c.ForeColor = Theme.Text; }
                else if (c is TextBox) { ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; c.BackColor = Theme.Panel2; c.ForeColor = Theme.Text; }
                else if (c is NumericUpDown) { ((NumericUpDown)c).BorderStyle = BorderStyle.FixedSingle; c.BackColor = Theme.Panel2; c.ForeColor = Theme.Text; }
                else if (c is CheckBox) { ((CheckBox)c).FlatStyle = FlatStyle.Flat; c.ForeColor = Theme.Text; }
                else if (c is ListView) { c.BackColor = Theme.Panel; c.ForeColor = Theme.Text; }
                else if (c is Label) { if (c.ForeColor != Theme.Dim) c.ForeColor = Theme.Text; }
            }
            Native.DarkTitleBar(f);
            Native.DarkControls(f);
        }

        // a one-line text question; null when cancelled
        public string ReadText(string title, string prompt, string def)
        {
            using (Form f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(S(380), S(130)); f.MaximizeBox = false; f.MinimizeBox = false;
                Label l = new Label(); l.Text = prompt; l.Location = new Point(S(14), S(14)); l.AutoSize = true;
                TextBox t = new TextBox(); t.Text = def ?? ""; t.Location = new Point(S(14), S(40)); t.Width = S(350);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Location = new Point(S(200), S(84)); ok.Size = new Size(S(80), S(30));
                Button ca = new Button(); ca.Text = "Cancel"; ca.DialogResult = DialogResult.Cancel; ca.Location = new Point(S(286), S(84)); ca.Size = new Size(S(80), S(30));
                f.Controls.AddRange(new Control[] { l, t, ok, ca }); f.AcceptButton = ok; f.CancelButton = ca;
                DarkDialog(f);
                DialogResult r = f.ShowDialog(Form);
                string v = t.Text.Trim();
                return r == DialogResult.OK ? v : null;
            }
        }

        public static string Inv(double v, string fmt) { return v.ToString(fmt, CultureInfo.InvariantCulture); }
    }
}

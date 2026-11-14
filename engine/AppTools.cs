using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PixTune
{
    // Tool catalogue: parameters, styles and tool-specific panels/canvas behaviour.
    // Setup actions run when a tool opens and use the panel helpers of AppCore.
    public partial class App
    {
        // ---------------- tool state ----------------
        public string CurveKey = "curve_rgb";
        public CurveEditor CurveEditor;
        public double CropAspect;
        public bool CropPortrait;
        public TileGroup ExpandTiles, HealTiles, PoseTiles;
        public int ExpandWant;
        public List<Control> ExpandGen;
        public TextBox TextBox;
        public string ColorBand = "red";
        public bool DenoiseAsked;

        static readonly Random toolRandom = new Random();
        static readonly string Degree = "°";

        // Sections of the tools grid.
        public static readonly List<KeyValuePair<string, string[]>> ToolGroups = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("Adjust", new string[] { "tune", "details", "curves", "wb", "color", "dehaze", "rawdev" }),
            new KeyValuePair<string, string[]>("Transform", new string[] { "crop", "perspective", "expand" }), // 'rotate' stays available for older edit stacks
            new KeyValuePair<string, string[]>("Retouch", new string[] { "selective", "brush", "healing", "portrait", "headpose", "face" }),
            new KeyValuePair<string, string[]>("Film", new string[] { "film", "bloom", "halation", "grain", "grainy" }),
            new KeyValuePair<string, string[]>("Styles", new string[] { "hdr", "glamour", "tonal", "drama", "vintage", "retrolux", "grunge", "bw", "noir" }),
            new KeyValuePair<string, string[]>("Creative", new string[] { "lensblur", "vignette", "double", "text", "frames" }),
        };

        static string[] Numbered(int n)
        {
            string[] r = new string[n];
            for (int i = 0; i < n; i++) r[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
            return r;
        }

        // ---------------- AI healing (optional LaMa model) ----------------
        public bool TestHealAi() { return Settings.HealAi && Lama.Installed; }

        // Strokes painted from now on use the AI model (value 2) or content-aware fill (value 1).
        public void SetHealMode(bool ai, bool quiet = false)
        {
            if (ai && !Lama.Installed)
            {
                SelectHealTile(0);
                StartAiDownload("lama", ok => { if (ok) { SelectHealTile(1); SetHealMode(true, true); } else SelectHealTile(0); });
                return;
            }
            if (Settings.HealAi != ai) { Settings.HealAi = ai; Settings.Save(); }
            if (ToolDef != null && ToolDef.Id == "healing") Canvas.BrushValue = ai ? 2 : 1;
            if (ai)
            {
                Lama.WarmUp();
                if (!quiet) SetStatus("AI healing on: each stroke takes a few seconds");
            }
        }

        public void SelectHealTile(int i) { SelectTileIn(HealTiles, i); }

        // ---------------- faces ----------------
        public void GetFacesForStep()
        {
            if (Step.Str("faces", "") != "") return;
            string found = FindFaces(ToolInput());
            Step.SetS("faces", found);
            if (found == "") SetStatus("No face found automatically. Click on a face to place it.");
        }

        public void SyncFacesOverlay()
        {
            Canvas.Faces.Clear();
            foreach (float[] f in PixTune.Face.ParseFaces(Step.Str("faces", "")))
                Canvas.Faces.Add(new RectangleF(f[0], f[1], f[2], f[3]));
            Canvas.Invalidate();
        }

        public void AddFaceAt(PointF p)
        {
            Img im = ToolInput();
            double s = 0.24 * Math.Min(im.W, im.H);
            double w = s / im.W, h = (s * 1.25) / im.H;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string box = string.Format("{0},{1},{2},{3}", (p.X - w / 2).ToString(inv), (p.Y - h / 2).ToString(inv), w.ToString(inv), h.ToString(inv));
            string cur = Step.Str("faces", "");
            Step.SetS("faces", !string.IsNullOrEmpty(cur) ? cur + ";" + box : box);
            SyncFacesOverlay();
            RequestRender();
        }

        public void AddFaceButtons()
        {
            AddButtons(
                B("Detect", G(0xE8D4), b => { Step.SetS("faces", ""); GetFacesForStep(); SyncFacesOverlay(); RequestRender(); }),
                B("Clear faces", G(0xE711), b => { Step.SetS("faces", ""); SyncFacesOverlay(); RequestRender(); }));
            AddHint("Click on the photo to place a face the detector missed.");
        }

        // ---------------- Curves ----------------
        public static readonly List<KeyValuePair<string, Dictionary<string, string>>> CurvePresets = BuildCurvePresets();

        static List<KeyValuePair<string, Dictionary<string, string>>> BuildCurvePresets()
        {
            List<KeyValuePair<string, Dictionary<string, string>>> l = new List<KeyValuePair<string, Dictionary<string, string>>>();
            Action<string, string[]> add = (name, kv) =>
            {
                Dictionary<string, string> d = new Dictionary<string, string>();
                for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
                l.Add(new KeyValuePair<string, Dictionary<string, string>>(name, d));
            };
            add("Default", new string[0]);
            add("Lighten", new string[] { "curve_rgb", "0,0;0.45,0.58;1,1" });
            add("Darken", new string[] { "curve_rgb", "0,0;0.55,0.42;1,1" });
            add("Low contrast", new string[] { "curve_rgb", "0,0.06;0.3,0.33;0.7,0.67;1,0.94" });
            add("High contrast", new string[] { "curve_rgb", "0,0;0.25,0.17;0.75,0.84;1,1" });
            add("Fade", new string[] { "curve_rgb", "0,0.14;0.5,0.52;1,0.93" });
            add("Matte", new string[] { "curve_rgb", "0,0.1;0.2,0.18;0.8,0.85;1,0.95" });
            add("Cross process", new string[] { "curve_r", "0,0;0.25,0.18;0.75,0.84;1,1", "curve_g", "0,0;0.3,0.24;0.7,0.78;1,1", "curve_b", "0,0.14;1,0.84" });
            add("Warm", new string[] { "curve_r", "0,0;0.5,0.56;1,1", "curve_b", "0,0;0.5,0.44;1,1" });
            add("Cool", new string[] { "curve_r", "0,0;0.5,0.45;1,1", "curve_b", "0,0;0.5,0.56;1,1" });
            add("Strong S", new string[] { "curve_lum", "0,0;0.22,0.1;0.5,0.5;0.78,0.9;1,1" });
            add("Brighten shadows", new string[] { "curve_lum", "0,0;0.2,0.3;0.6,0.65;1,1" });
            add("Negative", new string[] { "curve_rgb", "0,1;1,0" });
            return l;
        }

        public void SetCurveChannel(string key)
        {
            CurveKey = key;
            CurveEditor ed = CurveEditor;
            ed.PointsString = Step.Str(key, "0,0;1,1");
            Dictionary<string, string> colors = new Dictionary<string, string> { { "curve_rgb", "White" }, { "curve_r", "Tomato" }, { "curve_g", "LimeGreen" }, { "curve_b", "DodgerBlue" }, { "curve_lum", "Silver" } };
            ed.CurveColor = Color.FromName(colors[key]);
            Dictionary<string, int> chs = new Dictionary<string, int> { { "curve_rgb", -1 }, { "curve_r", 0 }, { "curve_g", 1 }, { "curve_b", 2 }, { "curve_lum", -1 } };
            ed.Histogram = ImageIO.Histogram(ToolInput(), chs[key]);
            ed.Invalidate();
        }

        // ---------------- Crop ----------------
        public void SetCropAspect(double ar)
        {
            ImageCanvas c = Canvas;
            CropAspect = ar;
            c.CropAspect = (float)ar;
            if (ar <= 0) { c.Invalidate(); return; }
            Size sz = c.ImageSize;
            double nar = ar * sz.Height / sz.Width;   // normalized w/h
            RectangleF r = c.CropRect;
            double w = r.Width, h = w / nar;
            if (h > r.Height) { h = r.Height; w = h * nar; }
            if (w > 1) { w = 1; h = w / nar; }
            if (h > 1) { h = 1; w = h * nar; }
            double cx = r.X + r.Width / 2.0, cy = r.Y + r.Height / 2.0;
            double x = Math.Min(Math.Max(0, cx - w / 2), 1 - w), y = Math.Min(Math.Max(0, cy - h / 2), 1 - h);
            c.CropRect = new RectangleF((float)x, (float)y, (float)w, (float)h);
            c.Invalidate();
            UpdateCropHud();
        }

        public void UpdateCropHud()
        {
            // size in photo pixels of the (rotated) picture shown under the frame
            ImageCanvas c = Canvas;
            Size sz = c.ImageSize;
            if (sz.Width <= 0) return;
            double fw = c.SourceWidth > 0 ? c.SourceWidth : sz.Width;
            double fh = sz.Height * fw / (double)sz.Width;
            RectangleF r = c.CropRect;
            c.Hud = string.Format("{0} x {1}", (int)Math.Round(r.Width * fw), (int)Math.Round(r.Height * fh));
            c.Invalidate();
        }

        public void ResetCropFrame()
        {
            Canvas.CropRect = new RectangleF(0, 0, 1, 1);
            CropAspect = 0; Canvas.CropAspect = 0;
            Canvas.Invalidate();
        }

        // ---------------- Perspective ----------------
        public void SyncQuad()
        {
            double[] c = Geo.PerspectiveCorners(Step);
            PointF[] q = new PointF[4];
            for (int i = 0; i < 4; i++) q[i] = new PointF((float)c[i * 2], (float)c[i * 2 + 1]);
            Canvas.Quad = q;
            Canvas.Invalidate();
        }

        // ---------------- Expand ----------------
        public static readonly string[] ExpandSides = { "left", "right", "top", "bottom" };

        public void LimitExpandSide(string k)
        {
            if (Array.IndexOf(ExpandSides, k) < 0) return;
            string other = ExpandSides.FirstOrDefault(s => s != k && Step.F(s, 0) > 0);
            if (other != null && Step.F(k, 0) > 0)
            {
                // another side is already expanded: this one stays at 0
                Step.Set(k, 0);
                if (Sliders.ContainsKey(k)) Sliders[k].Value = 0;
                SetStatus("Expand one side at a time: set " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(other) + " back to 0 first");
            }
            SyncExpandSides();
        }

        // sliders of the other sides are locked while one side is expanded
        public void SyncExpandSides()
        {
            string on = ExpandSides.FirstOrDefault(s => Step.F(s, 0) > 0);
            foreach (string s in ExpandSides)
                if (Sliders.ContainsKey(s)) Sliders[s].Enabled = on == null || s == on;
        }

        // Fill modes 3 (AI fill, LaMa) and 4 (Generative) need optional files: offer them, keep the mode once installed.
        public void SetExpandFill(int i)
        {
            string need = i == 3 && !Lama.Installed ? "lama" : i == 4 && !Diffusion.Installed ? "gen" : null;
            if (need != null)
            {
                SelectTileIn(ExpandTiles, Step.I("fill", 0));
                ExpandWant = i;
                Action<bool> done = ok =>
                {
                    if (ok && ToolDef != null && ToolDef.Id == "expand") { SelectTileIn(ExpandTiles, ExpandWant); SetExpandFill(ExpandWant); }
                };
                if (need == "gen") StartAiDownload("generative", done); else StartAiDownload("lama", done);
                return;
            }
            Step.Set("fill", i);
            if (i == 4)
            {
                Diffusion.WarmUp(); SetStatus("Generating the extension on the graphics card...");
                // installs from before High detail: offer just the upscaler (the mode works without it)
                if (Step.I("hd", 1) == 1 && !Upscaler.Installed) StartAiDownload("generative", null);
            }
            else if (i == 3) Lama.WarmUp();
            SyncExpandGen();
            RequestRender();
        }

        public void SyncExpandGen()
        {
            if (ExpandGen == null) return;
            bool gen = Step.I("fill", 0) == 4;
            foreach (Control c in ExpandGen) if (c != null) c.Visible = gen;
        }

        // generating takes a while, so the prompt is used on Enter or when leaving the box, not on every key
        public void ExpandPrompt(TextBox s)
        {
            if (Step != null && Step.Tool == "expand" && Step.Str("prompt", "") != s.Text.Trim()) { Step.SetS("prompt", s.Text.Trim()); RequestRender(); }
        }

        // ---------------- Selective ----------------
        public List<Adjust.CtlPoint> GetSelPoints() { return Adjust.ParseCtl(Step.Str("points", "")); }
        public void SaveSelPoints(List<Adjust.CtlPoint> list) { Step.SetS("points", Adjust.JoinCtl(list)); }

        public void SyncSelCanvas()
        {
            ImageCanvas c = Canvas;
            List<Adjust.CtlPoint> pts = GetSelPoints();
            c.Points.Clear(); c.PointRadii.Clear(); c.PointRadiiY.Clear(); c.PointAngles.Clear(); c.PointLabels.Clear();
            Dictionary<string, string> labels = new Dictionary<string, string> { { "brightness", "B" }, { "contrast", "C" }, { "saturation", "S" }, { "structure", "St" }, { "size", "R" } };
            string lab = null;
            if (Active >= 0 && Active < ParamKeys.Count) labels.TryGetValue(ParamKeys[Active], out lab);
            foreach (Adjust.CtlPoint p in pts) { c.Points.Add(new PointF(p.X, p.Y)); c.PointRadii.Add(p.R); c.PointRadiiY.Add(p.RY); c.PointAngles.Add(p.Ang); c.PointLabels.Add(lab); }
            if (c.Selected >= pts.Count) c.Selected = pts.Count - 1;
            c.Invalidate();
            if (ShowMask && c.Selected >= 0) { Img im = ToolInput(); c.ShowPlaneMask(Adjust.CtlMask(im, pts[c.Selected]), im.W, im.H); }
            else if (!ShowMask) c.ClearMask();
        }

        // The Size slider shows the larger radius of a zone and scales both, keeping its shape
        public double GetZoneSize(Adjust.CtlPoint p) { return Math.Min(100, Math.Round(Math.Max(p.R, p.RY) * 200.0)); }

        public void SetZoneSize(Adjust.CtlPoint p, double v)
        {
            double k = (v / 200) / Math.Max(0.0001, Math.Max(p.R, p.RY));
            p.R = (float)(p.R * k); p.RY = (float)(p.RY * k);
        }

        double SelValue(Adjust.CtlPoint p, string k)
        {
            switch (k)
            {
                case "brightness": return p.Br;
                case "contrast": return p.Co;
                case "saturation": return p.Sa;
                case "structure": return p.St;
                case "size": return GetZoneSize(p);
            }
            return 0;
        }

        public void SyncSelSliders()
        {
            int sel = Canvas.Selected;
            List<Adjust.CtlPoint> pts = GetSelPoints();
            foreach (string k in Sliders.Keys.ToList())
            {
                double v = 0;
                if (sel >= 0 && sel < pts.Count) v = SelValue(pts[sel], k);
                Sliders[k].Value = (float)v;
                Sliders[k].Enabled = sel >= 0;
            }
        }

        // ---------------- Brush family ----------------
        // channels: caption, brush channel, glyph
        public void AddBrushControls(string[][] channels, double min, double max, double def, string scaleLabel)
        {
            Canvas.BrushTarget = Step.Strokes;
            Canvas.BrushChannel = channels[0][1];
            Canvas.BrushValue = (float)def;
            BrushValue = def;
            MaskScale = Math.Max(Math.Abs(min), Math.Abs(max));
            if (channels.Length > 1)
                AddTiles(channels.Select(ch => T(ch[0], ch[2], ch[1])).ToList(), 0, 72, 64, (i, t) =>
                {
                    Canvas.BrushChannel = (string)((TileTag)t.Tag).Data; UpdateBrushMask();
                });
            AddSlider(P("_bval", scaleLabel, min, max, def, 1), true);
            AddSlider(P("_bsize", "Brush size", 4, 300, Canvas.BrushSize, 1, " px"), true);
            AddButtons(
                B("Eraser", G(0xE75C), b => { Eraser = b.Toggled; Canvas.BrushValue = b.Toggled ? 0 : (float)BrushValue; }, true),
                B("Mask", G(0xE890), b => { ShowMask = b.Toggled; UpdateBrushMask(); }, true),
                B("Undo stroke", G(0xE7A7), b => { List<Stroke> l = Step.Strokes; if (l.Count > 0) { l.RemoveAt(l.Count - 1); UpdateBrushMask(); RequestRender(); } }));
            AddHint("Paint on the photo. Alt + mouse wheel changes the brush size, the mouse wheel zooms.");
        }

        public void UpdateBrushMask()
        {
            if (!ShowMask) { Canvas.ClearMask(); return; }
            Canvas.ShowMaskFor(Canvas.BrushTarget, Canvas.BrushChannel, (float)MaskScale);
        }

        // the brush value and size sliders of the brush-family tools
        public void BrushParam(string k, double v)
        {
            if (k == "_bval") { BrushValue = v; if (!Eraser) Canvas.BrushValue = (float)v; }
            else if (k == "_bsize") { Canvas.BrushSize = (float)v; Canvas.Invalidate(); }
        }

        // ---------------- Head pose ----------------
        // AI mode is an optional download, offered the first time it is chosen
        public void SetHeadPoseMode(bool ai)
        {
            if (ai && !LivePortrait.Installed)
            {
                SelectTileIn(PoseTiles, 0);
                StartAiDownload("headpose", ok => { if (ok && ToolDef != null && ToolDef.Id == "headpose") { SelectTileIn(PoseTiles, 1); SetHeadPoseMode(true); } });
                return;
            }
            Step.Set("ai", ai ? 1 : 0);
            if (ai) { LivePortrait.WarmUp(); SetStatus("AI head pose: each change of turn, tilt or smile takes a few seconds"); }
            RequestRender();
        }

        // ---------------- Lens Blur / Vignette ----------------
        public static Bitmap NewShapeThumb(int shape)
        {
            Bitmap b = new Bitmap(40, 40);
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                {
                    bool inside = Adjust.InShape(shape, (float)((x - 19.5) / 17), (float)((y - 19.5) / 17));
                    b.SetPixel(x, y, inside ? Color.FromArgb(240, 240, 240) : Color.FromArgb(30, 31, 34));
                }
            return b;
        }

        public void SyncEllipse()
        {
            ImageCanvas c = Canvas; Step s = Step;
            c.Center = new PointF(s.F("cx", 0.5), s.F("cy", 0.5));
            c.RadX = s.F("rx", 0.25); c.RadY = s.F("ry", 0.25); c.Angle = s.F("angle"); c.EllipseShape = s.I("shape", 0);
            c.Invalidate();
        }

        // ---------------- Double exposure ----------------
        public void SelectOverlayImage()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = ImageFilter;
                dlg.Title = "Choose the image to blend";
                if (dlg.ShowDialog(Form) == DialogResult.OK) { Step.SetS("path", dlg.FileName); RequestRender(); }
            }
        }

        // ---------------- Text ----------------
        public List<TileSpec> GetTextStyleThumbs()
        {
            int n = PixTune.Style.TextStyles.Length;
            Img bg = new Img(120, 80);
            for (int i = 0; i < bg.D.Length; i++) bg.D[i] = 0.16f;
            List<TileSpec> l = new List<TileSpec>();
            for (int i = 0; i < n; i++)
            {
                Step s = new Step("text");
                s.Set("style", i); s.Set("tsize", 230); s.SetS("text", "Aa");
                l.Add(T("", "", null, Pipeline.Apply(bg, s).ToBitmap()));
            }
            return l;
        }

        // ---------------- Color ----------------
        public void SyncColorSliders()
        {
            foreach (string k in new string[] { "hue", "sat", "lum" }) Sliders[k].Value = (float)GetParam(k);
            UpdateHud();
        }

        // ---------------------------------------------------------------- the catalogue
        public void RegisterTools()
        {
            // ---------------- Tune Image
            Register(new ToolDef
            {
                Id = "tune", Name = "Tune Image", Glyph = G(0xE9E9), Canvas = "Adjust",
                Params = new List<Prm> { P("brightness", "Brightness"), P("ambiance", "Ambiance"), P("contrast", "Contrast"), P("saturation", "Saturation"),
                    P("shadows", "Shadows"), P("highlights", "Highlights"), P("warmth", "Warmth") },
                Setup = () =>
                {
                    // Auto adapts to the photo below it; the sliders then fine-tune on top of the automatic correction.
                    AddButtons(B("Auto", G(0xE9A9), b => { Step.Set("auto", b.Toggled ? 1 : 0); RequestRender(); }, true, Step.I("auto", 0) == 1));
                }
            });

            // ---------------- Details
            Register(new ToolDef
            {
                Id = "details", Name = "Details", Glyph = G(0xE71E), Canvas = "Adjust",
                Params = new List<Prm> { P("structure", "Structure"), P("sharpening", "Sharpening", 0, 100, 0) },
                Setup = () => AddHint("Tip: zoom with the mouse wheel to judge sharpening at 100%.")
            });

            // ---------------- Curves
            Register(new ToolDef
            {
                Id = "curves", Name = "Curves", Glyph = G(0xE9D2), Canvas = "View",
                Setup = () =>
                {
                    CurveEditor ed = new CurveEditor();
                    CurveEditor = ed;
                    Ui.Place(Body, ed, BodyW(), Ui.Px(280));
                    ed.CurveChanged += (s, e) => Safe(() => { Step.SetS(CurveKey, CurveEditor.PointsString); RequestRender(); });
                    string[][] chs = { new[] { "RGB", "curve_rgb" }, new[] { "Red", "curve_r" }, new[] { "Green", "curve_g" }, new[] { "Blue", "curve_b" }, new[] { "Lum", "curve_lum" } };
                    AddTiles(chs.Select(c => T(c[0], "", c[1])).ToList(), 0, 54, 34, (i, t) => SetCurveChannel((string)((TileTag)t.Tag).Data));
                    AddHint("Click to add a point, drag to move, right-click or double-click to remove.");
                    AddSection("Presets");
                    AddTiles(CurvePresets.Select(kv => T(kv.Key, "", kv.Key)).ToList(), -1, 92, 34, (i, t) =>
                    {
                        string name = (string)((TileTag)t.Tag).Data;
                        Dictionary<string, string> preset = CurvePresets.First(kv => kv.Key == name).Value;
                        foreach (string k in new string[] { "curve_rgb", "curve_r", "curve_g", "curve_b", "curve_lum" })
                            Step.SetS(k, preset.ContainsKey(k) ? preset[k] : "0,0;1,1");
                        SetCurveChannel(CurveKey);
                        RequestRender();
                    });
                    SetCurveChannel("curve_rgb");
                }
            });

            // ---------------- White Balance
            Register(new ToolDef
            {
                Id = "wb", Name = "White Balance", Glyph = G(0xE9CA), Canvas = "Adjust",
                Params = new List<Prm> { P("temperature", "Temperature"), P("tint", "Tint") },
                Setup = () =>
                {
                    AddButtons(
                        B("Auto", G(0xE9A9), b =>
                        {
                            double[] v = Adjust.AutoWB(ToolInput());
                            Hashtable h = new Hashtable(); h["temperature"] = v[0]; h["tint"] = v[1]; SetParams(h);
                        }),
                        B("Pick grey", G(0xEF3C), b =>
                        {
                            if (b.Toggled) { SetCanvasMode("Pick"); SetStatus("Click on something that should be neutral grey or white."); } else SetCanvasMode("Adjust");
                        }, true));
                },
                OnPicked = p =>
                {
                    Img im = ToolInput();
                    double r = 0, g = 0, bl = 0; int n = 0;
                    int cx = (int)Math.Round(p.X * (double)im.W), cy = (int)Math.Round(p.Y * (double)im.H);
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            float rr, gg, bb;
                            im.Get(cx + dx, cy + dy, out rr, out gg, out bb);
                            r += rr; g += gg; bl += bb; n++;
                        }
                    double[] v = Adjust.PickWB((float)(r / n), (float)(g / n), (float)(bl / n));
                    Hashtable h = new Hashtable(); h["temperature"] = v[0]; h["tint"] = v[1]; SetParams(h);
                }
            });

            // ---------------- Crop
            // Crop & Rotate (merged as in the current mobile app): the photo is shown rotated but uncropped
            // while the tool is open, the frame is applied on Apply.
            Register(new ToolDef
            {
                Id = "crop", Name = "Crop & Rotate", Glyph = G(0xE7A8), Canvas = "Crop",
                Params = new List<Prm> { P("angle", "Straighten", -45, 45, 0, 0.1, Degree) },
                Setup = () =>
                {
                    Step s = Step;
                    s.Set("nocrop", 1);
                    Canvas.CropRect = RectangleF.FromLTRB(s.F("x0", 0), s.F("y0", 0), s.F("x1", 1), s.F("y1", 1));
                    AddButtons(
                        B("90", G(0xE7AD), b => { Step.Set("rot90", (Step.I("rot90", 0) + 1) % 4); ResetCropFrame(); RequestRender(); }),
                        B("Flip H", G(0xE8AB), b => { Step.Set("flipH", 1 - Step.I("flipH", 0)); RequestRender(); }),
                        B("Flip V", G(0xE8CB), b => { Step.Set("flipV", 1 - Step.I("flipV", 0)); RequestRender(); }),
                        B("Auto straighten", G(0xE9A9), b => SetParams("angle", Geo.AutoStraighten(ToolInput()))));
                    AddSection("Aspect ratio");
                    CropAspect = 0;
                    Canvas.CropAspect = 0;
                    object[][] ratios = { new object[] { "Free", 0.0 }, new object[] { "Original", -1.0 }, new object[] { "1:1", 1.0 }, new object[] { "DIN", 1.41421 }, new object[] { "3:2", 1.5 },
                        new object[] { "4:3", 1.33333 }, new object[] { "5:4", 1.25 }, new object[] { "7:5", 1.4 }, new object[] { "16:9", 1.77778 } };
                    AddTiles(ratios.Select(r => T((string)r[0], "", r[1])).ToList(), 0, 58, 34, (i, t) =>
                    {
                        double ar = Convert.ToDouble(((TileTag)t.Tag).Data, CultureInfo.InvariantCulture);
                        if (ar < 0) { Size sz = Canvas.ImageSize; ar = sz.Width / (double)sz.Height; }
                        else if (ar > 0 && CropPortrait) ar = 1 / ar;
                        SetCropAspect(ar);
                    });
                    AddButtons(
                        B("Rotate frame", G(0xE7AD), b =>
                        {
                            CropPortrait = !CropPortrait;
                            if (CropAspect > 0) SetCropAspect(1 / CropAspect);
                        }),
                        B("Reset", G(0xE72C), b => { Canvas.CropRect = new RectangleF(0, 0, 1, 1); SetCropAspect(CropAspect); UpdateCropHud(); }));
                    AddHint("Drag the corners or edges, drag inside to move the frame. Straightening crops the corners automatically.");
                    UpdateCropHud();
                },
                OnCrop = () => UpdateCropHud(),
                BeforeApply = () =>
                {
                    RectangleF r = Canvas.CropRect;
                    Step.Set("x0", r.Left); Step.Set("y0", r.Top); Step.Set("x1", r.Right); Step.Set("y1", r.Bottom);
                    Step.P.Remove("nocrop");
                }
            });

            // ---------------- Rotate
            Register(new ToolDef
            {
                Id = "rotate", Name = "Rotate", Glyph = G(0xE7AD), Canvas = "Adjust",
                Params = new List<Prm> { P("angle", "Straighten", -45, 45, 0, 0.1, Degree) },
                Setup = () =>
                {
                    AddButtons(
                        B("90", G(0xE7AD), b => { Step.Set("rot90", (Step.I("rot90", 0) + 1) % 4); RequestRender(); }),
                        B("Flip H", G(0xE8AB), b => { Step.Set("flipH", 1 - Step.I("flipH", 0)); RequestRender(); }),
                        B("Flip V", G(0xE8CB), b => { Step.Set("flipV", 1 - Step.I("flipV", 0)); RequestRender(); }));
                    AddButtons(B("Auto straighten", G(0xE9A9), b => SetParams("angle", Geo.AutoStraighten(ToolInput()))));
                    AddHint("The photo is cropped automatically to hide the corners.");
                }
            });

            // ---------------- Perspective
            Register(new ToolDef
            {
                Id = "perspective", Name = "Perspective", Glyph = G(0xE9A6), Canvas = "Quad",
                Params = new List<Prm> { P("tiltv", "Tilt vertical"), P("tilth", "Tilt horizontal"), P("prot", "Rotate", -45, 45, 0, 0.1, Degree), P("pscale", "Scale") },
                Setup = () =>
                {
                    AddSection("Fill");
                    AddTiles(new List<TileSpec> { T("Smart"), T("White"), T("Black") }, Step.I("fill", 0), 80, 34, (i, t) => { Step.Set("fill", i); RequestRender(); });
                    AddButtons(B("Reset corners", G(0xE72C), b =>
                    {
                        for (int i = 0; i < 4; i++) { Step.Set("c" + i + "x", 0); Step.Set("c" + i + "y", 0); }
                        SyncQuad(); RequestRender();
                    }));
                    AddHint("Drag the four corners for free perspective, or drag on the photo to change the selected slider.");
                    SyncQuad();
                },
                OnParam = k => SyncQuad(),
                OnQuad = (i, p) =>
                {
                    Step bs = Step.Clone();
                    for (int k = 0; k < 4; k++) { bs.Set("c" + k + "x", 0); bs.Set("c" + k + "y", 0); }
                    double[] c = Geo.PerspectiveCorners(bs);
                    Step.Set("c" + i + "x", p.X - c[i * 2]); Step.Set("c" + i + "y", p.Y - c[i * 2 + 1]);
                    RequestRender();
                }
            });

            // ---------------- Expand
            Register(new ToolDef
            {
                Id = "expand", Name = "Expand", Glyph = G(0xE740), Canvas = "Adjust",
                Params = new List<Prm> { P("left", "Left", 0, 25, 0, 1, "%"), P("right", "Right", 0, 25, 0, 1, "%"), P("top", "Top", 0, 25, 0, 1, "%"), P("bottom", "Bottom", 0, 25, 0, 1, "%") },
                // one side at a time, at most a quarter of the photo: the other sides unlock when this one is back to 0
                OnParam = k => LimitExpandSide(k),
                Setup = () =>
                {
                    SyncExpandSides();
                    AddHint("Expand one side at a time, by up to a quarter of the photo. Set it back to 0 to choose another side.");
                    AddSection("Fill");
                    ExpandTiles = AddTiles(new List<TileSpec> { T("Smart"), T("White"), T("Black"), T("AI fill"), T("Generative") }, Step.I("fill", 0), 80, 34, (i, t) => SetExpandFill(i));
                    AddHint("Smart continues the texture around the photo. AI fill uses LaMa. Generative invents new scenery with Stable Diffusion on the graphics card (about 30 to 60 s).");
                    // the generative options come last, so hiding them leaves no gap
                    ExpandGen = new List<Control>();
                    TextBox tb = new TextBox();
                    tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text; tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.Font = new Font("Segoe UI", 10);
                    tb.Text = Step.Str("prompt", "");
                    tb.KeyDown += (s, e) => Safe(() => { if (e.KeyCode == Keys.Return) { e.SuppressKeyPress = true; ExpandPrompt(tb); } });
                    tb.Leave += (s, e) => Safe(() => ExpandPrompt(tb));
                    ExpandGen.Add(AddHint("Describe the extension (optional), then press Enter:"));
                    Ui.Place(Body, tb, BodyW(), Ui.Px(30));
                    ExpandGen.Add(tb);
                    List<IconButton> gb = AddButtons(
                        B("Generate again", G(0xE8B1), b => { Step.Set("seed", toolRandom.Next(1, 99999)); RequestRender(); }),
                        // sharper, finer extension at full size (export, 100%): AI upscaling, a light second pass, matched grain
                        B("High detail", G(0xE8A3), b =>
                        {
                            Step.Set("hd", b.Toggled ? 1 : 0);
                            if (b.Toggled && !Upscaler.Installed) StartAiDownload("generative", null);
                            RequestRender();
                        }, true, Step.I("hd", 1) == 1));
                    ExpandGen.Add(gb[0].Parent);
                    ExpandGen.Add(AddHint("High detail sharpens the extension and matches the photo's grain when it is exported or viewed at 100% (about 15 extra seconds on large photos)."));
                    SyncExpandGen();
                }
            });

            // ---------------- Selective
            Register(new ToolDef
            {
                Id = "selective", Name = "Selective", Glyph = G(0xE707), Canvas = "Points",
                Params = new List<Prm> { P("brightness", "Brightness"), P("contrast", "Contrast"), P("saturation", "Saturation"), P("structure", "Structure"), P("size", "Size", 2, 100, 30) },
                Setup = () =>
                {
                    Canvas.Selected = -1;
                    AddButtons(
                        B("Delete point", G(0xE74D), b =>
                        {
                            List<Adjust.CtlPoint> l = GetSelPoints(); int i = Canvas.Selected;
                            if (i >= 0 && i < l.Count) { l.RemoveAt(i); SaveSelPoints(l); Canvas.Selected = l.Count - 1; SyncSelCanvas(); SyncSelSliders(); RequestRender(); }
                        }),
                        B("Copy point", G(0xE8C8), b =>
                        {
                            List<Adjust.CtlPoint> l = GetSelPoints(); int i = Canvas.Selected;
                            if (i >= 0 && i < l.Count)
                            {
                                Adjust.CtlPoint o = l[i];
                                Adjust.CtlPoint n = new Adjust.CtlPoint();
                                n.X = (float)Math.Min(0.97, o.X + 0.05); n.Y = o.Y; n.R = o.R; n.RY = o.RY; n.Ang = o.Ang; n.Br = o.Br; n.Co = o.Co; n.Sa = o.Sa; n.St = o.St;
                                l.Add(n); SaveSelPoints(l); Canvas.Selected = l.Count - 1; SyncSelCanvas(); SyncSelSliders(); RequestRender();
                            }
                        }),
                        B("Mask", G(0xE890), b => { ShowMask = b.Toggled; SyncSelCanvas(); }, true));
                    AddHint("Click the photo to add a control point. Drag a point to move it, the white handles of its zone to stretch and turn it; drag elsewhere to change the selected slider.");
                    SyncSelCanvas(); SyncSelSliders();
                },
                GetParam = k =>
                {
                    List<Adjust.CtlPoint> l = GetSelPoints(); int i = Canvas.Selected;
                    if (i < 0 || i >= l.Count) return 0;
                    return SelValue(l[i], k);
                },
                SetParam = (k, v) =>
                {
                    List<Adjust.CtlPoint> l = GetSelPoints(); int i = Canvas.Selected;
                    if (i < 0 || i >= l.Count) return;
                    switch (k)
                    {
                        case "brightness": l[i].Br = (float)v; break;
                        case "contrast": l[i].Co = (float)v; break;
                        case "saturation": l[i].Sa = (float)v; break;
                        case "structure": l[i].St = (float)v; break;
                        case "size": SetZoneSize(l[i], v); break;
                    }
                    SaveSelPoints(l);
                    if (k == "size" || ShowMask) SyncSelCanvas();
                },
                OnActive = () => SyncSelCanvas(),
                OnPointAdded = p =>
                {
                    List<Adjust.CtlPoint> l = GetSelPoints();
                    Adjust.CtlPoint n = new Adjust.CtlPoint();
                    n.X = p.X; n.Y = p.Y; n.R = 0.15f; n.RY = 0.15f;
                    l.Add(n); SaveSelPoints(l);
                    Canvas.Selected = l.Count - 1;
                    SyncSelCanvas(); SyncSelSliders(); RequestRender();
                },
                OnPointSelected = () => { SyncSelSliders(); SyncSelCanvas(); },
                OnPointMoved = (i, p, final) =>
                {
                    List<Adjust.CtlPoint> l = GetSelPoints();
                    if (i >= 0 && i < l.Count) { l[i].X = p.X; l[i].Y = p.Y; SaveSelPoints(l); if (final) SyncSelCanvas(); RequestRender(); }
                },
                // a zone handle dragged: P = width and height radii, angle in degrees
                OnPointResized = (i, p, ang, final) =>
                {
                    List<Adjust.CtlPoint> l = GetSelPoints();
                    if (i < 0 || i >= l.Count) return;
                    l[i].R = p.X; l[i].RY = p.Y; l[i].Ang = ang; SaveSelPoints(l);
                    ParamSlider sz;
                    if (Sliders.TryGetValue("size", out sz) && sz != null) sz.Value = (float)GetZoneSize(l[i]);
                    if (final) SyncSelCanvas();
                    RequestRender();
                }
            });

            // ---------------- Brush family
            Register(new ToolDef
            {
                Id = "brush", Name = "Brush", Glyph = G(0xE771), Canvas = "Brush",
                Setup = () => AddBrushControls(new string[][] { new[] { "Dodge & Burn", "dodge", G(0xE706) }, new[] { "Exposure", "exposure", G(0xE793) },
                    new[] { "Temperature", "temp", G(0xE9CA) }, new[] { "Saturation", "sat", G(0xE790) } }, -10, 10, 5, "Strength"),
                SetParam = BrushParam
            });

            Register(new ToolDef
            {
                Id = "healing", Name = "Healing", Glyph = G(0xE75C), Canvas = "Brush", RenderOnStrokeEnd = true,
                Setup = () =>
                {
                    Canvas.BrushTarget = Step.Strokes;
                    Canvas.BrushChannel = "heal";
                    Canvas.BrushSize = 22;
                    AddSlider(P("_bsize", "Brush size", 4, 200, 22, 1, " px"), true);
                    AddButtons(B("Undo stroke", G(0xE7A7), b => { List<Stroke> l = Step.Strokes; if (l.Count > 0) { l.RemoveAt(l.Count - 1); RequestRender(); } }));
                    AddSection("Mode");
                    HealTiles = AddTiles(new List<TileSpec> { T("Content-aware", G(0xE8B9)), T("AI (LaMa)", G(0xE945)) }, TestHealAi() ? 1 : 0, 118, 64, (i, t) => SetHealMode(i == 1));
                    Canvas.BrushValue = TestHealAi() ? 2 : 1;
                    if (TestHealAi()) Lama.WarmUp();
                    AddHint("Paint over a spot or an object, shadow and glow included. Content-aware rebuilds it from matching texture around it. AI mode redraws it with the LaMa model: best for large objects, a few seconds per stroke. Each stroke keeps the mode it was painted with.");
                },
                SetParam = BrushParam
            });

            Register(new ToolDef
            {
                Id = "face", Name = "Face Enhance", Glyph = G(0xE76E), Canvas = "Brush",
                Setup = () => AddBrushControls(new string[][] { new[] { "Eye focus", "eyes", G(0xE890) }, new[] { "Spotlight", "spot", G(0xE706) }, new[] { "Smooth skin", "skin", G(0xE76E) } }, 0, 10, 10, "Strength"),
                SetParam = BrushParam
            });

            // ---------------- Styled filters
            Register(new ToolDef
            {
                Id = "hdr", Name = "HDR Scape", Glyph = G(0xE706), Canvas = "Adjust",
                Styles = new string[] { "Nature", "People", "Fine", "Strong" },
                Params = new List<Prm> { P("strength", "Filter strength", 0, 100, 50), P("brightness", "Brightness"), P("saturation", "Saturation") }
            });
            Register(new ToolDef
            {
                Id = "glamour", Name = "Glamour Glow", Glyph = G(0xE734), Canvas = "Adjust",
                Styles = new string[] { "1", "2", "3", "4", "5", "6" },
                Params = new List<Prm> { P("glow", "Glow", 0, 100, 50), P("saturation", "Saturation"), P("warmth", "Warmth") }
            });
            Register(new ToolDef
            {
                Id = "tonal", Name = "Tonal Contrast", Glyph = G(0xE793), Canvas = "Adjust",
                Params = new List<Prm> { P("high", "High tones", 0, 100, 25), P("mid", "Mid tones", 0, 100, 25), P("low", "Low tones", 0, 100, 25),
                    P("protshadows", "Protect shadows", 0, 100, 0), P("prothighlights", "Protect highlights", 0, 100, 0) }
            });
            Register(new ToolDef
            {
                Id = "drama", Name = "Drama", Glyph = G(0xE945), Canvas = "Adjust",
                Styles = new string[] { "Bright 1", "Bright 2", "Bright 3", "Dark 1", "Dark 2", "Dark 3" },
                Params = new List<Prm> { P("strength", "Filter strength", 0, 100, 50), P("saturation", "Saturation") }
            });
            Register(new ToolDef
            {
                Id = "vintage", Name = "Vintage", Glyph = G(0xE823), Canvas = "Center",
                Styles = Numbered(12),
                Params = new List<Prm> { P("brightness", "Brightness"), P("saturation", "Saturation"), P("strength", "Style strength", 0, 100, 50), P("vignette", "Vignette strength", 0, 100, 50), P("blur", "Blur", 0, 100, 0) },
                Setup = () => AddHint("Drag the centre point to place the sharp area used by Blur.")
            });
            Register(new ToolDef
            {
                Id = "grainy", Name = "Grainy Film", Glyph = G(0xE714), Canvas = "Adjust",
                Styles = new string[] { "A01", "A02", "A03", "A04", "B01", "B02", "B03", "B04", "C01", "C02", "C03", "C04", "L01", "L02", "L03", "X01" },
                Params = new List<Prm> { P("grain", "Grain", 0, 100, 25), P("strength", "Style strength", 0, 100, 50) }
            });
            Register(new ToolDef
            {
                Id = "retrolux", Name = "Retrolux", Glyph = G(0xE722), Canvas = "Adjust",
                Styles = Numbered(13),
                Params = new List<Prm> { P("brightness", "Brightness"), P("contrast", "Contrast"), P("saturation", "Saturation"), P("strength", "Style strength", 0, 100, 50),
                    P("scratches", "Scratches", 0, 100, 50), P("leaks", "Light leaks", 0, 100, 50) },
                Setup = () => AddButtons(B("Shuffle", G(0xE8B1), b => { Step.Set("seed", toolRandom.Next(1, 9999)); RequestRender(); }))
            });
            Register(new ToolDef
            {
                Id = "grunge", Name = "Grunge", Glyph = G(0xE790), Canvas = "Center",
                Params = new List<Prm> { P("style", "Style", 0, 1499, 0), P("brightness", "Brightness"), P("contrast", "Contrast"), P("texstrength", "Texture strength", 0, 100, 50), P("saturation", "Saturation") },
                Setup = () =>
                {
                    AddSection("Texture");
                    AddTiles(Numbered(5).Select(c => T(c)).ToList(), Step.I("texture", 0), 52, 34, (i, t) => { Step.Set("texture", i); RequestRender(); });
                    AddButtons(B("Shuffle", G(0xE8B1), b => { SetParams("style", toolRandom.Next(1500)); Step.Set("texture", toolRandom.Next(5)); RequestRender(); }));
                    AddHint("Drag the centre point to choose the focus of the effect.");
                }
            });
            Register(new ToolDef
            {
                Id = "bw", Name = "Black & White", Glyph = G(0xE7A1), Canvas = "Adjust",
                Styles = new string[] { "Neutral", "Contrast", "Bright", "Dark", "Film", "Darken sky" },
                Params = new List<Prm> { P("brightness", "Brightness"), P("contrast", "Contrast"), P("grain", "Grain", 0, 100, 0) },
                Setup = () =>
                {
                    AddSection("Color filter");
                    string[][] sw = { new[] { "Neutral", "Gray" }, new[] { "Red", "IndianRed" }, new[] { "Orange", "Orange" }, new[] { "Yellow", "Gold" }, new[] { "Green", "SeaGreen" }, new[] { "Blue", "RoyalBlue" } };
                    AddTiles(sw.Select(c => new TileSpec { Caption = c[0], Swatch = Color.FromName(c[1]) }).ToList(), Step.I("filter", 0), 50, 66, (i, t) => { Step.Set("filter", i); RequestRender(); });
                }
            });
            Register(new ToolDef
            {
                Id = "noir", Name = "Noir", Glyph = G(0xE708), Canvas = "Adjust",
                Styles = Enumerable.Range(1, 14).Select(i => "S" + i.ToString("00", CultureInfo.InvariantCulture)).ToArray(),
                Params = new List<Prm> { P("brightness", "Brightness"), P("wash", "Wash", 0, 100, 25), P("strength", "Filter strength", 0, 100, 50), P("grain", "Grain", 0, 100, 25) }
            });

            // ---------------- Portrait / Head pose
            Register(new ToolDef
            {
                Id = "portrait", Name = "Portrait", Glyph = G(0xE77B), Canvas = "Adjust",
                Params = new List<Prm> { P("spotlight", "Face spotlight", 0, 100, 25), P("smoothing", "Skin smoothing", 0, 100, 25), P("eyes", "Eye clarity", 0, 100, 25) },
                Setup = () =>
                {
                    GetFacesForStep(); SyncFacesOverlay();
                    AddSection("Presets");
                    // name, spotlight, smoothing, eyes
                    object[][] pre = { new object[] { "Natural", 25, 25, 25 }, new object[] { "Smooth", 15, 70, 15 }, new object[] { "Spotlight", 70, 30, 30 }, new object[] { "Bright eyes", 20, 25, 80 },
                        new object[] { "Soft", 10, 50, 10 }, new object[] { "Strong", 60, 60, 60 }, new object[] { "Subtle", 10, 15, 15 }, new object[] { "None", 0, 0, 0 } };
                    AddTiles(pre.Select(v => T((string)v[0], "", v)).ToList(), -1, 70, 34, (i, t) =>
                    {
                        object[] v = (object[])((TileTag)t.Tag).Data;
                        Hashtable h = new Hashtable(); h["spotlight"] = v[1]; h["smoothing"] = v[2]; h["eyes"] = v[3]; SetParams(h);
                    });
                    AddSection("Skin tone");
                    string[][] tones = { new[] { "None", "Gray" }, new[] { "Pale", "MistyRose" }, new[] { "Light", "PeachPuff" }, new[] { "Medium", "SandyBrown" }, new[] { "Deep", "Sienna" }, new[] { "Rosy", "LightPink" } };
                    AddTiles(tones.Select(c => new TileSpec { Caption = c[0], Swatch = Color.FromName(c[1]) }).ToList(), Step.I("skintone", 0), 50, 66, (i, t) => { Step.Set("skintone", i); RequestRender(); });
                    AddFaceButtons();
                },
                OnClick = p => AddFaceAt(p)
            });
            Register(new ToolDef
            {
                Id = "headpose", Name = "Head Pose", Glyph = G(0xE8D4), Canvas = "Adjust",
                Params = new List<Prm> { P("hrot", "Horizontal rotation"), P("vrot", "Vertical rotation"), P("pupil", "Pupil size", 0, 100, 0), P("smile", "Smile", 0, 100, 0), P("focal", "Focal length", 0, 100, 0) },
                Setup = () =>
                {
                    GetFacesForStep(); SyncFacesOverlay(); AddFaceButtons();
                    AddSection("Mode");
                    PoseTiles = AddTiles(new List<TileSpec> { T("3D mesh", G(0xE81E)), T("AI (LivePortrait)", G(0xE945)) }, Step.I("ai", 0), 118, 64, (i, t) => SetHeadPoseMode(i == 1));
                    if (Step.I("ai", 0) == 1) LivePortrait.WarmUp();
                    AddHint("3D mesh bends the photo itself: instant, best for small changes. AI mode redraws the face with LivePortrait, ears, hair and jaw included: more natural turns and smiles, about 5 to 15 seconds per change.");
                },
                OnClick = p => AddFaceAt(p)
            });

            // ---------------- Lens Blur / Vignette
            Register(new ToolDef
            {
                Id = "lensblur", Name = "Lens Blur", Glyph = G(0xE890), Canvas = "Center", Ellipse = true,
                Params = new List<Prm> { P("strength", "Blur strength", 0, 100, 50), P("transition", "Transition", 0, 100, 30), P("vignette", "Vignette strength", 0, 100, 30) },
                Setup = () =>
                {
                    AddTiles(new List<TileSpec> { T("Elliptical", G(0xEA3A)), T("Linear", G(0xE738)) }, Step.I("shape", 0), 100, 56, (i, t) => { Step.Set("shape", i); SyncEllipse(); RequestRender(); });
                    AddSection("Bokeh");
                    string[] names = { "Circle", "Hexagon", "Octagon", "Triangle", "Star", "Heart", "Ring" };
                    List<TileSpec> items = new List<TileSpec>();
                    for (int i = 0; i < names.Length; i++) items.Add(T(names[i], "", null, NewShapeThumb(i)));
                    AddTiles(items, Step.I("bokeh", 0), 62, 66, (i, t) => { Step.Set("bokeh", i); RequestRender(); });
                    AddHint("Drag the centre to move the focus, the white handles to resize and rotate it.");
                    SyncEllipse();
                },
                OnEllipse = () =>
                {
                    ImageCanvas c = Canvas;
                    Step.Set("rx", c.RadX); Step.Set("ry", c.RadY); Step.Set("angle", c.Angle);
                    RequestRender();
                }
            });
            Register(new ToolDef
            {
                Id = "vignette", Name = "Vignette", Glyph = G(0xE91F), Canvas = "Center",
                Params = new List<Prm> { P("outer", "Outer brightness", -100, 100, -50), P("inner", "Inner brightness"), P("size", "Size", 0, 100, 50) },
                Setup = () => AddHint("Drag the centre point to move the vignette.")
            });

            // ---------------- Double exposure
            Register(new ToolDef
            {
                Id = "double", Name = "Double Exposure", Glyph = G(0xE7C4), Canvas = "Move",
                Params = new List<Prm> { P("opacity", "Opacity", 0, 100, 50), P("dscale", "Scale"), P("drot", "Rotation", -180, 180, 0, 1, Degree) },
                Setup = () =>
                {
                    AddButtons(B("Add image", G(0xE8E5), b => SelectOverlayImage()));
                    AddSection("Blend");
                    string[] modes = { "Default", "Lighten", "Darken", "Add", "Subtract", "Multiply", "Overlay", "Screen" };
                    AddTiles(modes.Select(m => T(m)).ToList(), Step.I("mode", 0), 70, 34, (i, t) => { Step.Set("mode", i); RequestRender(); });
                    AddHint("Drag on the photo to move the added image.");
                    if (Step.Str("path", "") == "") SelectOverlayImage();
                },
                OnMove = d =>
                {
                    Step.Set("ox", Step.F("ox", 0.5) + d.X); Step.Set("oy", Step.F("oy", 0.5) + d.Y);
                    RequestRender();
                }
            });

            // ---------------- Text
            Register(new ToolDef
            {
                Id = "text", Name = "Text", Glyph = G(0xE8D2), Canvas = "Move",
                Params = new List<Prm> { P("tsize", "Size", 5, 100, 40), P("opacity", "Opacity", 0, 100, 100), P("trot", "Rotation", -180, 180, 0, 1, Degree) },
                Setup = () =>
                {
                    if (!Step.S.ContainsKey("text")) Step.SetS("text", "Your text");
                    TextBox tb = new TextBox();
                    tb.Multiline = true; tb.AcceptsReturn = true;
                    tb.BackColor = Theme.Panel2; tb.ForeColor = Theme.Text; tb.BorderStyle = BorderStyle.FixedSingle;
                    tb.Font = new Font("Segoe UI", 11);
                    tb.Text = Step.Str("text", "").Replace("\n", "\r\n");
                    tb.TextChanged += (s, e) => Safe(() => { Step.SetS("text", tb.Text.Replace("\r", "")); RequestRender(); });
                    TextBox = tb;
                    Ui.Place(Body, tb, BodyW(), Ui.Px(64));
                    AddButtons(B("Invert", G(0xE7A1), b => { Step.Set("invert", b.Toggled ? 1 : 0); RequestRender(); }, true, Step.I("invert", 0) == 1));
                    AddSection("Color");
                    AddTiles(PixTune.Style.Palette.Select(c => new TileSpec { Swatch = c }).ToList(), Step.I("color", 0), 40, 40, (i, t) => { Step.Set("color", i); RequestRender(); });
                    AddSection("Style");
                    List<TileSpec> thumbs = GetTextStyleThumbs();
                    AddTiles(thumbs, Step.I("style", 0), 70, 52, (i, t) => { Step.Set("style", i); RequestRender(); });
                    AddHint("Drag on the photo to move the text. Double-click the photo to edit the text.");
                },
                OnMove = d =>
                {
                    Step.Set("tx", Step.F("tx", 0.5) + d.X); Step.Set("ty", Step.F("ty", 0.5) + d.Y);
                    RequestRender();
                },
                OnDoubleClick = () => { if (TextBox != null) { TextBox.Focus(); TextBox.SelectAll(); } }
            });

            // ---------------- Frames
            Register(new ToolDef
            {
                Id = "frames", Name = "Frames", Glyph = G(0xE8B9), Canvas = "Adjust",
                Styles = Numbered(23),
                Params = new List<Prm> { P("fwidth", "Frame width", 0, 100, 50) }
            });

            // ---------------- Film and the film effects (newest app releases)
            string[] filmNames = Film.Stocks.Select(st => Regex.Replace(Regex.Replace(st.Name, "^(Kodak|Fujifilm|Fujicolor|Agfa|Ilford|Lomography) ", "", RegexOptions.IgnoreCase), " Plus", "", RegexOptions.IgnoreCase)).ToArray();
            Register(new ToolDef
            {
                Id = "film", Name = "Film", Glyph = G(0xE8B2), Canvas = "Adjust", StyleW = 92, StyleH = 88,
                Styles = filmNames,
                Params = new List<Prm> { P("strength", "Style strength", 0, 100, 100), P("grain", "Grain", 0, 100, 50), P("halation", "Halation", 0, 100, 50), P("brightness", "Brightness"), P("warmth", "Warmth") },
                Setup = () => AddHint("Film stock emulations: colour, contrast curve, grain and halation of each film.")
            });
            Register(new ToolDef
            {
                Id = "bloom", Name = "Bloom", Glyph = G(0xE781), Canvas = "Adjust",
                Params = new List<Prm> { P("strength", "Strength", 0, 100, 50), P("radius", "Radius", 0, 100, 50), P("threshold", "Threshold", 0, 100, 60) },
                Setup = () => AddHint("Bright lights spill softly into the surrounding areas, like on film.")
            });
            Register(new ToolDef
            {
                Id = "halation", Name = "Halation", Glyph = G(0xE81D), Canvas = "Adjust",
                Params = new List<Prm> { P("strength", "Strength", 0, 100, 50), P("radius", "Radius", 0, 100, 40), P("threshold", "Threshold", 0, 100, 65), P("hue", "Hue") },
                Setup = () => AddHint("Adds the red glow film shows around bright lights.")
            });
            Register(new ToolDef
            {
                Id = "grain", Name = "Grain", Glyph = G(0xEA86), Canvas = "Adjust",
                Params = new List<Prm> { P("amount", "Amount", 0, 100, 40), P("size", "Size", 0, 100, 40) },
                Setup = () => AddButtons(B("Monochrome grain", G(0xE7A1), b => { Step.Set("mono", b.Toggled ? 1 : 0); RequestRender(); }, true, Step.I("mono", 0) == 1))
            });
            Register(new ToolDef
            {
                Id = "dehaze", Name = "Dehaze", Glyph = G(0xE78A), Canvas = "Adjust",
                Params = new List<Prm> { P("amount", "Dehaze") },
                Setup = () => AddHint("Positive values remove haze and smog, negative values add atmosphere.")
            });

            // ---------------- Color
            Register(new ToolDef
            {
                Id = "color", Name = "Color", Glyph = G(0xEB42), Canvas = "Adjust",
                Params = new List<Prm> { P("hue", "Hue"), P("sat", "Saturation"), P("lum", "Luminance") },
                Setup = () =>
                {
                    ColorBand = "red";
                    string[][] sw = { new[] { "Red", "red", "Crimson" }, new[] { "Orange", "orange", "DarkOrange" }, new[] { "Yellow", "yellow", "Gold" }, new[] { "Green", "green", "ForestGreen" },
                        new[] { "Aqua", "aqua", "DarkTurquoise" }, new[] { "Blue", "blue", "RoyalBlue" }, new[] { "Purple", "purple", "MediumPurple" }, new[] { "Magenta", "magenta", "MediumVioletRed" } };
                    AddSection("Color");
                    AddTiles(sw.Select(c => new TileSpec { Caption = c[0], Tag = c[1], Swatch = Color.FromName(c[2]) }).ToList(), 0, 70, 66, (i, t) =>
                    {
                        ColorBand = (string)((TileTag)t.Tag).Data; SyncColorSliders();
                    });
                    AddButtons(B("Reset all colors", G(0xE72C), b =>
                    {
                        foreach (string k in Step.P.Keys.ToList()) if (Regex.IsMatch(k, "^[hsl]_", RegexOptions.IgnoreCase)) Step.P.Remove(k);
                        SyncColorSliders(); RequestRender();
                    }));
                    AddHint("Pick a colour range, then shift its hue, saturation and luminance.");
                },
                GetParam = k => Step.Get(k.Substring(0, 1) + "_" + ColorBand, 0),
                SetParam = (k, v) => { if (!k.StartsWith("_")) Step.Set(k.Substring(0, 1) + "_" + ColorBand, v); }
            });

            // ---------------- Raw develop
            Register(new ToolDef
            {
                Id = "rawdev", Name = "RAW Develop", Glyph = G(0xE8B3), Canvas = "Adjust",
                Params = new List<Prm> { P("exposure", "Exposure"), P("highlights", "Highlights"), P("shadows", "Shadows"), P("contrast", "Contrast"), P("structure", "Structure"),
                    P("saturation", "Saturation"), P("temperature", "Temperature"), P("tint", "Tint"), P("denoise", "AI denoise", 0, 100, 0) },
                // the AI denoiser is an optional download, offered the first time its slider is used
                OnParam = k =>
                {
                    if (k == "denoise" && Step.F("denoise", 0) > 0)
                    {
                        if (Denoiser.Installed) Denoiser.WarmUp();
                        else if (!DenoiseAsked) { DenoiseAsked = true; StartAiDownload("denoise", ok => { if (ok) RequestRender(); }); }
                    }
                },
                Setup = () => AddHint("AI denoise removes sensor noise first (best on high-ISO photos; computed once, then the strength is instant). The mask buttons below limit RAW Develop to the subject, the background or the sky.")
            });

            // ---------------- Pseudo tool: looks
            Register(new ToolDef { Id = "look", Name = "Looks", Glyph = G(0xE790), Canvas = "View", Hidden = true });
        }
    }
}

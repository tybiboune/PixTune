using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace PixTune
{
    // The mask panel, under the tone curve: limits the open tool to part of the photo, in place. A mask starts from
    // Photo, Subject, Pick (the parts clicked), Background or Sky (several at once with Ctrl+click), is refined with
    // Paint / Erase and Invert, and is kept with the step. Saved masks (named, kept for the photo being edited) are
    // switched on or off for any tool at any moment, several at once; they are forgotten when another photo is
    // opened or PixTune closes. The mask shows in translucent red while it is defined (and with the eye button
    // whenever wanted); changing a setting of the tool hides it again and ends painting or picking. Not offered
    // for tools that move pixels around (crop, rotate, perspective, expand) or paint their own areas.
    public partial class App
    {
        public class SavedMask { public string Name, Smart, Pts, Extra; public List<Stroke> Strokes = new List<Stroke>(); public bool Invert; }

        public Panel MaskDock, MaskHead;
        public Label MaskLabel, MaskState, MaskFold;
        public IconButton MaskClear, MaskShow, MaskSave;
        public FlowLayoutPanel MaskBody, MaskLibBox;
        public TileGroup MaskTiles;
        public List<IconButton> MaskButtons;
        public ParamSlider MaskSize;
        public Label MaskPickHint, MaskLibTitle;
        public Control MaskPickRow;
        public readonly List<SavedMask> MaskLib = new List<SavedMask>();
        public int MaskLibShown = -1;
        // -1: the photo belongs to the tool's own gestures; 1 paints the edit in, 0 paints it out, 2 picks parts
        public int MaskPaint = -1;
        public bool MaskView, PickAdd;
        public class CanvasState { public CanvasMode Mode; public List<Stroke> Target; public string Channel; public float Value, Size; }
        public CanvasState ToolCanvas;

        public static readonly string[] MaskKinds = { "all", "subject", "pick", "background", "sky" };

        public bool TestMaskable()
        {
            return Mode == "tool" && ToolDef != null && Step != null && !Pipeline.IsGeometric(ToolDef.Id)
                && ToolDef.Id != "healing" && ToolDef.Id != "look" && ToolDef.Id != "frames";
        }

        IconButton HeadButton(string glyph, string tip, Action click)
        {
            IconButton b = Ui.Button(glyph, "");
            b.Size = new Size(S(30), S(26));
            Tips.SetToolTip(b, tip);
            b.Click += (s, e) => Safe(click);
            return b;
        }

        // The panel's frame; its title bar folds it.
        void BuildMaskDock()
        {
            MaskDock = new Panel(); MaskDock.Dock = DockStyle.Top; MaskDock.Height = S(32); MaskDock.BackColor = Theme.Panel;
            MaskHead = new Panel(); MaskHead.Dock = DockStyle.Top; MaskHead.Height = S(32); MaskHead.Cursor = Cursors.Hand;
            MaskLabel = new Label(); MaskLabel.Text = "MASK"; MaskLabel.ForeColor = Theme.Dim; MaskLabel.Font = Ui.SectionFont;
            MaskLabel.AutoSize = true; MaskLabel.Location = new Point(S(14), S(8));
            MaskState = new Label(); MaskState.ForeColor = Theme.Dim; MaskState.AutoEllipsis = true; MaskState.Bounds = new Rectangle(S(70), S(8), S(150), S(18));
            MaskFold = new Label(); MaskFold.Font = new Font("Segoe MDL2 Assets", 9); MaskFold.ForeColor = Theme.Dim; MaskFold.AutoSize = true;
            MaskClear = HeadButton(G(0xE711), "Remove the mask: the tool applies to the whole photo", ClearMask);
            MaskShow = HeadButton(G(0xE890), "Show or hide the mask in red over the photo", SwitchMaskView);
            MaskSave = HeadButton(G(0xE74E), "Save this mask with a name, to use it again on any tool while this photo is open", () => SaveCurrentMask(null));
            MaskHead.Controls.AddRange(new Control[] { MaskLabel, MaskState, MaskFold, MaskClear, MaskShow, MaskSave });
            MaskHead.Resize += (s, e) =>
            {
                MaskFold.Location = new Point(MaskHead.ClientSize.Width - MaskFold.Width - S(20), S(10));
                MaskClear.Location = new Point(MaskFold.Left - MaskClear.Width - S(8), S(3));
                MaskSave.Location = new Point(MaskClear.Left - MaskSave.Width - S(2), S(3));
                MaskShow.Location = new Point(MaskSave.Left - MaskShow.Width - S(2), S(3));
                MaskState.Width = Math.Max(S(40), MaskShow.Left - MaskState.Left - S(6));
            };
            foreach (Control c in new Control[] { MaskHead, MaskLabel, MaskState, MaskFold }) c.Click += (s, e) => Safe(SwitchMaskFold);
            Tips.SetToolTip(MaskHead, "Show or hide the mask settings");
            MaskBody = new FlowLayoutPanel(); MaskBody.Dock = DockStyle.Fill; MaskBody.FlowDirection = FlowDirection.TopDown; MaskBody.WrapContents = false;
            Panel line = new Panel(); line.Dock = DockStyle.Bottom; line.Height = 1; line.BackColor = Theme.Line;
            MaskDock.Controls.Add(MaskBody); MaskDock.Controls.Add(line); MaskDock.Controls.Add(MaskHead);
        }

        // The panel's content, built once (after the side panel exists, so widths are known).
        void FillMaskDock()
        {
            Control keep = Body;
            Body = MaskBody;
            int w = BodyW();
            MaskTiles = AddTiles(new List<TileSpec> { T("Photo", G(0xE740)), T("Subject", G(0xE77B)), T("Pick", G(0xE707)), T("Background", G(0xE91B)), T("Sky", G(0xE706)) },
                -1, 56, 56, (i, t) =>
                {
                    // Ctrl+click adds the kind to the mask (or takes it out) instead of replacing it
                    bool add = (Control.ModifierKeys & Keys.Control) != 0;
                    if (MaskKinds[i] == "pick") StartMaskPick(add); else SetMaskKind(MaskKinds[i], add);
                });
            MaskButtons = AddButtons(
                B("Paint", G(0xE771), b => SetMaskPaint(b.Toggled ? 1 : -1, false), true),
                B("Erase", G(0xE75C), b => SetMaskPaint(b.Toggled ? 0 : -1, false), true),
                B("Invert", G(0xE7A1), b => { if (TestMaskable()) { Step.MaskInvert = b.Toggled; SetMaskChanged(); ShowMaskEdit(); } }, true));
            MaskSize = Ui.Slider(MaskBody, "_msize", "Brush size", 4, 300, 60, 1, " px", 60, w);
            MaskSize.ValueChanged += (s, e) => { Canvas.BrushSize = e.Value; Canvas.Invalidate(); };
            // shown while picking
            MaskPickHint = Ui.Hint(MaskBody, "Click each part to add it (a person, a coat, a car...). Ctrl+click a part to cut it out.", w);
            List<IconButton> pick = AddButtons(B("Undo click", G(0xE7A7), b => UndoMaskPick()), B("Done", G(0xE73E), b => SetMaskPaint(-1, false)));
            MaskPickRow = pick[0].Parent;
            // saved masks
            MaskLibTitle = Ui.Section(MaskBody, "Saved masks", w);
            MaskLibBox = new FlowLayoutPanel();
            MaskLibBox.FlowDirection = FlowDirection.TopDown; MaskLibBox.WrapContents = false; MaskLibBox.AutoSize = true;
            MaskLibBox.Margin = new Padding(0);
            MaskBody.Controls.Add(MaskLibBox);
            Body = keep;
            SyncMaskDock();
        }

        public void SwitchMaskFold()
        {
            Settings.MaskFolded = !Settings.MaskFolded;
            Settings.Save();
            SyncMaskDock();
        }

        // ---------------- what a mask is made of ----------------
        public static List<string> MaskKindsOf(Step s) { return s.Str("smartmask", "").Split('+').Where(k => k.Length > 0).ToList(); }
        public static void SetMaskKinds(Step s, IEnumerable<string> kinds)
        {
            List<string> k = kinds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (k.Count > 0) s.SetS("smartmask", string.Join("+", k.ToArray())); else s.S.Remove("smartmask");
            if (!k.Contains("pick")) s.S.Remove("pickpts");
        }
        // the saved masks switched on for a step: copies, each a step holding only a mask, named by 'maskname'
        public static List<Step> MaskExtras(Step s)
        {
            string j = s.Str("maskextra", "");
            return j.Length > 0 ? Step.ListFromJson(j) : new List<Step>();
        }
        public static void SetMaskExtras(Step s, List<Step> list)
        {
            if (list.Count > 0) s.SetS("maskextra", Step.ListToJson(list)); else s.S.Remove("maskextra");
        }
        // the step is masked when it holds anything (or while painting starts from an empty mask)
        void UpdateMaskOn(Step s)
        {
            s.MaskOn = MaskKindsOf(s).Count > 0 || s.Mask.Count > 0 || MaskExtras(s).Count > 0 || MaskPaint == 1;
        }

        // "subject + sky + 2 strokes": what a step's own mask is made of (empty when it has none)
        public static string MaskSummary(Step s)
        {
            List<string> names = new List<string>();
            foreach (string k in MaskKindsOf(s))
            {
                if (k == "pick")
                {
                    List<float[]> pts = SubjectPicker.ParsePoints(s.Str("pickpts", ""));
                    int add = pts.Count(p => p[2] > 0), cut = pts.Count - add;
                    string x = add == 1 ? "picked part" : add + " picked parts";
                    if (cut > 0) x += " - " + cut;
                    names.Add(x);
                }
                else
                {
                    string n = k == "all" ? "whole photo" : k == "subject" ? "subject" : k == "background" ? "background" : k == "sky" ? "sky" : "";
                    if (n.Length > 0) names.Add(n);
                }
            }
            string t = string.Join(" + ", names.ToArray());
            int ns = s.Mask.Count;
            string st = ns == 1 ? "1 stroke" : ns + " strokes";
            if (t.Length == 0 && ns > 0) t = "painted, " + st; else if (ns > 0) t += " + " + st;
            return t;
        }
        // everything a step's mask combines, saved masks by name: "Blobs + sky, inverted"
        public static string MaskText(Step s)
        {
            List<string> parts = MaskExtras(s).Select(e => e.Str("maskname", "saved mask")).ToList();
            string own = MaskSummary(s);
            if (own.Length > 0) parts.Add(own);
            string t = parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "empty";
            if (s.MaskInvert) t += ", inverted";
            return t;
        }

        // ---------------- panel state ----------------
        // shown in full only for a tool that can be masked
        public void SyncMaskDock()
        {
            if (MaskTiles == null) return;
            bool ok = TestMaskable();
            bool folded = Settings.MaskFolded;
            // the panel is in front of the user: the subject model starts loading now instead of inside the click
            // that asks for it, which is what made a first tap on Subject take twice as long as the detection
            if (ok && !folded) Segmenter.WarmUp(true);
            Step s = Step;
            MaskFold.Text = G(folded ? 0xE70D : 0xE70E);
            MaskState.Text = !ok ? (Mode != "tool" ? "open a tool to mask it" : "not used by this tool")
                : !s.MaskOn ? "none: the whole photo" : "stored: " + MaskText(s);
            // a stored mask lights up the panel title and puts a check mark on each kind it uses
            MaskState.ForeColor = ok && s.MaskOn ? Theme.Accent : Theme.Dim;
            MaskLabel.ForeColor = MaskState.ForeColor;
            bool open = ok && !folded;
            MaskClear.Visible = ok && s.MaskOn;
            MaskSave.Visible = ok && s.MaskOn;
            MaskShow.Visible = ok && (s.MaskOn || MaskLibShown >= 0);
            MaskShow.Toggled = MaskView || MaskLibShown >= 0; MaskShow.Invalidate();
            List<string> kinds = ok && s.MaskOn ? MaskKindsOf(s) : new List<string>();
            MaskTiles.ClearSelection();
            for (int k = 0; k < MaskKinds.Length; k++)
            {
                Tile t = (Tile)MaskTiles.Controls[k];
                bool on = kinds.Contains(MaskKinds[k]);
                t.Stored = on; t.Selected = on || (k == 2 && MaskPaint == 2);
            }
            MaskTiles.Invalidate(true);
            int p = MaskPaint;
            MaskButtons[0].Toggled = p == 1; MaskButtons[1].Toggled = p == 0;
            MaskButtons[2].Toggled = ok && s.MaskInvert;
            foreach (IconButton b in MaskButtons) b.Invalidate();
            MaskSize.Visible = p == 0 || p == 1;
            MaskPickHint.Visible = p == 2; MaskPickRow.Visible = p == 2;
            SyncMaskLib();
            SyncPickMarks();
            int h = MaskHead.Height + 1;
            if (open) h += MaskBody.GetPreferredSize(new Size(MaskBody.Width, 0)).Height + S(6);
            if (MaskDock.Height != h) MaskDock.Height = h;
            MaskBody.Visible = open;
        }

        // the saved masks, one row each: those switched on for the tool are outlined, the one shown has its eye lit
        void SyncMaskLib()
        {
            FlowLayoutPanel box = MaskLibBox;
            box.SuspendLayout();
            foreach (Control c in box.Controls.Cast<Control>().ToList()) { box.Controls.Remove(c); c.Dispose(); }
            List<string> on = TestMaskable() ? MaskExtras(Step).Select(e => e.Str("maskname", "")).ToList() : new List<string>();
            for (int i = 0; i < MaskLib.Count; i++)
            {
                SavedMask m = MaskLib[i];
                StackRow row = Ui.MaskEntry(box, i, m.Name, on.Contains(m.Name), MaskLibShown == i, BodyW());
                row.Action += (o, e) =>
                {
                    int idx = e.Index, a = (int)e.Value;
                    Later(() =>
                    {
                        switch (a) { case 3: SwitchSavedMask(idx); break; case 0: ShowSavedMask(idx); break; case 1: RenameSavedMask(idx, null); break; case 2: RemoveSavedMask(idx); break; }
                    });
                };
                Tips.SetToolTip(row, "Click to switch this mask on or off for the tool (several can be on at once). Eye: show it in red. Pencil: rename. Bin: delete.");
            }
            box.ResumeLayout();
            MaskLibTitle.Visible = MaskLib.Count > 0;
        }

        // ---------------- saved masks ----------------
        // a copy of a mask's whole definition (resolution-free: kinds, clicked points, strokes, saved masks, inversion)
        static SavedMask MaskDefOf(Step s)
        {
            SavedMask d = new SavedMask();
            d.Smart = s.Str("smartmask", ""); d.Pts = s.Str("pickpts", ""); d.Extra = s.Str("maskextra", ""); d.Invert = s.MaskInvert;
            foreach (Stroke k in s.Mask) d.Strokes.Add(k.Clone());
            return d;
        }
        static void SetMaskDef(Step s, SavedMask d)
        {
            s.MaskOn = true; s.MaskInvert = d.Invert;
            if (!string.IsNullOrEmpty(d.Smart)) s.SetS("smartmask", d.Smart); else s.S.Remove("smartmask");
            if (!string.IsNullOrEmpty(d.Pts)) s.SetS("pickpts", d.Pts); else s.S.Remove("pickpts");
            if (!string.IsNullOrEmpty(d.Extra)) s.SetS("maskextra", d.Extra); else s.S.Remove("maskextra");
            s.Mask.Clear(); foreach (Stroke k in d.Strokes) s.Mask.Add(k.Clone());
        }
        // a saved mask as a step holding only that mask (what 'maskextra' lists)
        static Step NewMaskStep(SavedMask m)
        {
            Step e = new Step("mask");
            SetMaskDef(e, m);
            e.SetS("maskname", m.Name);
            return e;
        }

        // Saves the tool's whole mask under a name. The tool then uses it as that saved mask (switched on in the
        // list), so the next kinds, picks or strokes start a new mask that adds to it. name skips the question.
        public void SaveCurrentMask(string name)
        {
            if (!TestMaskable() || !Step.MaskOn) return;
            if (MaskPaint >= 0) SetMaskPaint(-1, true);
            Step s = Step;
            string def = MaskText(s);
            def = def.Substring(0, 1).ToUpper() + def.Substring(1);
            if (string.IsNullOrEmpty(name)) name = ReadText("Save mask", "Name of this mask:", def);
            if (string.IsNullOrEmpty(name)) return;
            string b0 = name;
            for (int n = 2; MaskLib.Any(x => x.Name == name); n++) name = b0 + " " + n;
            SavedMask m = MaskDefOf(s); m.Name = name;
            MaskLib.Add(m);
            s.Mask.Clear(); SetMaskKinds(s, new string[0]); s.MaskInvert = false;
            SetMaskExtras(s, new List<Step> { NewMaskStep(m) });
            UpdateMaskOn(s);
            SetStatus("Mask saved: " + name + ". Click saved masks to switch them on or off on any tool while this photo is open; several can be on at once.");
            ShowMaskEdit();
        }

        // switches a saved mask on or off for the open tool (several can be on at once)
        public void SwitchSavedMask(int i)
        {
            if (i < 0 || i >= MaskLib.Count) return;
            if (!TestMaskable()) { SetStatus("Open a tool first: saved masks are then switched on for it."); return; }
            if (MaskPaint >= 0) SetMaskPaint(-1, true);
            SavedMask m = MaskLib[i]; Step s = Step;
            List<Step> list = MaskExtras(s);
            string what;
            if (list.RemoveAll(x => x.Str("maskname", "") == m.Name) > 0) what = "off";
            else
            {
                list.Add(NewMaskStep(m)); what = "on";
                if ((m.Smart ?? "").Split('+').Any(k => k == "subject" || k == "background" || k == "sky")) RequestAiMasks();
            }
            SetMaskExtras(s, list);
            UpdateMaskOn(s);
            ShowMaskEdit();
            SetStatus("Mask " + m.Name + " " + what);
        }

        // a saved mask in red over the photo (the eye again hides it)
        public void ShowSavedMask(int i)
        {
            if (i < 0 || i >= MaskLib.Count) return;
            if (MaskLibShown == i) { MaskLibShown = -1; Canvas.ClearMask(); UpdateMaskView(); SyncMaskDock(); return; }
            MaskLibShown = i; MaskView = false;
            Step tmp = NewMaskStep(MaskLib[i]);
            int shown = i;
            if (Mode == "tool") ShowMaskAsync(tmp, () => MaskLibShown == shown);
            else { Img im = Doc.Preview; Canvas.ShowPlaneMask(Pipeline.EffectiveMask(im, tmp), im.W, im.H); }
            SyncMaskDock();
        }

        // renames in the list and in the open tool (steps already applied keep the name they were given)
        public void RenameSavedMask(int i, string name)
        {
            if (i < 0 || i >= MaskLib.Count) return;
            SavedMask m = MaskLib[i];
            if (string.IsNullOrEmpty(name)) name = ReadText("Rename mask", "New name:", m.Name);
            if (string.IsNullOrEmpty(name) || name == m.Name) return;
            if (MaskLib.Any(x => x.Name == name)) { SetStatus("A mask is already named " + name); return; }
            if (TestMaskable())
            {
                List<Step> list = MaskExtras(Step);
                foreach (Step x in list) if (x.Str("maskname", "") == m.Name) x.SetS("maskname", name);
                SetMaskExtras(Step, list);
            }
            m.Name = name;
            SyncMaskDock();
        }

        // deletes from the list and switches it off for the open tool (steps already applied keep their copy)
        public void RemoveSavedMask(int i)
        {
            if (i < 0 || i >= MaskLib.Count) return;
            SavedMask m = MaskLib[i];
            MaskLib.RemoveAt(i);
            if (MaskLibShown == i) { MaskLibShown = -1; Canvas.ClearMask(); UpdateMaskView(); }
            else if (MaskLibShown > i) MaskLibShown--;
            if (TestMaskable())
            {
                Step s = Step; List<Step> list = MaskExtras(s);
                if (list.RemoveAll(x => x.Str("maskname", "") == m.Name) > 0) { SetMaskExtras(s, list); UpdateMaskOn(s); UpdateMaskView(); RequestRender(); }
            }
            SyncMaskDock();
        }

        // saved masks belong to the photo being edited
        public void ClearMaskLib()
        {
            MaskLib.Clear(); MaskLibShown = -1;
            if (MaskTiles != null) SyncMaskDock();
        }

        // the mask was changed by hand: it is no longer the saved one it came from
        public void SetMaskChanged() { if (Step != null) Step.S.Remove("maskname"); MaskLibShown = -1; }

        // ---------------- showing the mask ----------------
        // the mask of the open tool in red over the photo
        public void ShowMaskEdit()
        {
            MaskView = true; MaskLibShown = -1;
            UpdateMaskView();
            SyncMaskDock();
            RequestRender();
        }
        public void UpdateMaskView()
        {
            if (!MaskView || !TestMaskable()) return;
            if (!Step.MaskOn) { Canvas.ClearMask(); return; }
            ShowMaskAsync(Step, () => MaskView && TestMaskable());
        }

        // The red overlay is computed in the background: an AI mask can take seconds the first time, and the window
        // stays usable meanwhile (the renderer, which needs the same mask, shares the result). Only the newest
        // request is shown, and only if still wanted then.
        int maskViewSeq;
        public int MaskJobs;
        void ShowMaskAsync(Step s, Func<bool> stillWanted)
        {
            Img im = ToolInput();
            Img basis = Pipeline.GeometryBase(Doc.Preview, Doc.StepsBeforePending());
            Step copy = s.Clone();
            int id = ++maskViewSeq;
            MaskJobs++;
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                float[] m = null; Exception err = null;
                Img prev = Pipeline.MaskBase;
                Pipeline.MaskBase = basis;
                try { m = Pipeline.EffectiveMask(im, copy); }
                catch (Exception ex) { err = ex; }
                finally { Pipeline.MaskBase = prev; }
                Form.BeginInvoke((MethodInvoker)delegate
                {
                    MaskJobs--;
                    if (err != null) { ShowError(err); return; }
                    if (id == maskViewSeq && stillWanted()) Canvas.ShowPlaneMask(m, im.W, im.H);
                });
            });
        }

        // a step's mask on the open tool's input, found on the same picture as when the stack is rendered
        public float[] MaskOf(Step s)
        {
            Img im = ToolInput();
            Img prev = Pipeline.MaskBase;
            Pipeline.MaskBase = Pipeline.GeometryBase(Doc.Preview, Doc.StepsBeforePending());
            try { return Pipeline.EffectiveMask(im, s); }
            finally { Pipeline.MaskBase = prev; }
        }
        // the eye of the panel title: the tool's mask in red, on demand
        public void SwitchMaskView()
        {
            if (MaskView || MaskLibShown >= 0) { MaskView = false; MaskLibShown = -1; Canvas.ClearMask(); }
            else if (TestMaskable()) { MaskView = true; UpdateMaskView(); }
            SyncMaskDock();
        }

        // a setting of the tool changed: the mask goes back out of sight and painting or picking ends
        public void HideMaskEdit()
        {
            if (MaskPaint < 0 && !MaskView && MaskLibShown < 0) return;
            if (MaskPaint >= 0) SetMaskPaint(-1, true);
            if (MaskView || MaskLibShown >= 0) { MaskView = false; MaskLibShown = -1; Canvas.ClearMask(); if (ShowMask) UpdateBrushMask(); }
            SyncMaskDock();
        }

        // ---------------- building the mask ----------------
        // a one-tap kind replaces the kinds of the mask, or with toggle (Ctrl+click) is added to them or taken out
        public void SetMaskKind(string kind, bool toggle)
        {
            if (!TestMaskable()) return;
            if (MaskPaint == 2) SetMaskPaint(-1, true);
            Step s = Step;
            List<string> kinds = MaskKindsOf(s);
            if (toggle) { if (kinds.Contains(kind)) kinds.Remove(kind); else kinds.Add(kind); }
            else kinds = new List<string> { kind };
            SetMaskKinds(s, kinds);
            s.MaskOn = true; UpdateMaskOn(s);
            SetMaskChanged();
            if (kind != "all") RequestAiMasks();
            ShowMaskEdit();
        }

        // Pick: each click on the photo adds the part under it, Ctrl+click cuts a part out. With add (Ctrl+click on the
        // tile) the parts picked add to the kinds already chosen. The model is an optional download, offered the
        // first time.
        public void StartMaskPick(bool add)
        {
            if (!TestMaskable()) return;
            if (!SubjectPicker.Installed)
            {
                StartAiDownload("pick", ok => { if (ok && TestMaskable()) StartMaskPick(false); else SyncMaskDock(); });
                SyncMaskDock();
                return;
            }
            PickAdd = add;
            SubjectPicker.WarmUp();
            SetMaskPaint(2, false);
            SetStatus("Click each part to mask. Ctrl+click cuts a part out.");
        }

        public void AddMaskPick(PointF p)
        {
            Step s = Step;
            CultureInfo inv = CultureInfo.InvariantCulture;
            bool cut = (Control.ModifierKeys & Keys.Control) != 0;
            string pt = p.X.ToString("0.####", inv) + "," + p.Y.ToString("0.####", inv) + (cut ? ",0" : "");
            List<string> kinds = MaskKindsOf(s);
            bool had = kinds.Contains("pick") && s.Str("pickpts", "").Length > 0;
            // a cut needs something to cut from
            if (cut && !had) { SetStatus("Click a part to add it first; Ctrl+click then cuts parts out of it."); return; }
            if (had) s.SetS("pickpts", s.Str("pickpts", "") + ";" + pt);
            else
            {
                // the first click: the picked parts replace the one-tap kinds, or add to them (Pick started with Ctrl)
                if (PickAdd) kinds.Add("pick"); else kinds = new List<string> { "pick" };
                SetMaskKinds(s, kinds);
                s.SetS("pickpts", pt);
            }
            s.MaskOn = true;
            SetMaskChanged();
            UpdatePickedMask();
        }

        public void UndoMaskPick()
        {
            Step s = Step;
            if (s == null || !MaskKindsOf(s).Contains("pick")) return;
            List<string> l = s.Str("pickpts", "").Split(';').Where(x => x.Length > 0).ToList();
            if (l.Count <= 1)
            {
                SetMaskKinds(s, MaskKindsOf(s).Where(k => k != "pick"));
                UpdateMaskOn(s); SetMaskChanged(); SyncPickMarks(); ShowMaskEdit();
                return;
            }
            l.RemoveAt(l.Count - 1);
            s.SetS("pickpts", string.Join(";", l.ToArray()));
            SetMaskChanged();
            UpdatePickedMask();
        }

        public void UpdatePickedMask()
        {
            SetStatus("Finding the part...");
            SyncPickMarks();
            Form.Update();
            ShowMaskEdit();
            string err = SubjectPicker.LastError;
            int n = SubjectPicker.ParsePoints(Step.Str("pickpts", "")).Count;
            SetStatus(!string.IsNullOrEmpty(err) ? "Subject picker: " + err
                : n + " click" + (n > 1 ? "s" : "") + ". Click more parts to add them, Ctrl+click to cut one out, Done when finished.");
        }

        // the clicks of a pick mask, marked on the photo while picking or showing the mask
        void SyncPickMarks()
        {
            ImageCanvas c = Canvas;
            c.Marks.Clear(); c.MarkAdd.Clear();
            if (TestMaskable() && MaskKindsOf(Step).Contains("pick") && (MaskPaint == 2 || MaskView))
                foreach (float[] q in SubjectPicker.ParsePoints(Step.Str("pickpts", ""))) { c.Marks.Add(new PointF(q[0], q[1])); c.MarkAdd.Add(q[2] > 0); }
            c.Invalidate();
        }

        // 1 paints the edit in, 0 paints it out, 2 picks parts, -1 gives the photo back to the tool's own gestures
        public void SetMaskPaint(int v, bool quiet)
        {
            ImageCanvas c = Canvas;
            if (v >= 0 && !TestMaskable()) v = -1;
            if (v >= 0)
            {
                if (MaskPaint < 0) ToolCanvas = new CanvasState { Mode = c.Mode, Target = c.BrushTarget, Channel = c.BrushChannel, Value = c.BrushValue, Size = c.BrushSize };
                Step s = Step;
                if (v == 2)
                {
                    MaskPaint = 2;
                    c.Mode = CanvasMode.Pick;
                    if (MaskKindsOf(s).Contains("pick")) ShowMaskEdit(); else { SyncMaskDock(); c.Invalidate(); }
                    return;
                }
                // erasing with no mask yet starts from the whole photo; painting starts from nothing
                if (!s.MaskOn) { s.MaskOn = true; if (v == 0) s.SetS("smartmask", "all"); else s.S.Remove("smartmask"); SetMaskChanged(); }
                MaskPaint = v;
                c.BrushTarget = s.Mask; c.BrushChannel = "mask"; c.BrushValue = v; c.BrushSize = MaskSize.Value;
                c.Mode = CanvasMode.Brush;
                ShowMaskEdit();
                return;
            }
            if (MaskPaint >= 0 && ToolCanvas != null)
            {
                CanvasState t = ToolCanvas;
                c.Mode = t.Mode; c.BrushTarget = t.Target; c.BrushChannel = t.Channel; c.BrushValue = t.Value; c.BrushSize = t.Size;
                c.Invalidate();
            }
            MaskPaint = -1; ToolCanvas = null;
            if (!quiet) SyncMaskDock();
        }

        public void ClearMask()
        {
            if (!TestMaskable()) return;
            if (MaskPaint >= 0) SetMaskPaint(-1, true);
            Step s = Step;
            s.Mask.Clear(); s.S.Remove("smartmask"); s.S.Remove("pickpts"); s.S.Remove("maskextra"); s.MaskInvert = false; s.MaskOn = false;
            SetMaskChanged();
            MaskView = false; Canvas.ClearMask();
            SyncMaskDock();
            RequestRender();
        }

        // for scripts: a one-tap mask ('subject', 'background', 'sky', 'all'), picking ('pick') or painting ('')
        public void OpenMaskFor(string kind)
        {
            if (!TestMaskable()) return;
            if (kind == "pick") StartMaskPick(false); else if (!string.IsNullOrEmpty(kind)) SetMaskKind(kind, false); else SetMaskPaint(1, false);
        }
    }
}

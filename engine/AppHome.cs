using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PixTune
{
    // Home tabs (looks, tools, export), the edit stack, opening, saving and exporting, and the two dialogs.
    public partial class App
    {
        class HomePanel { public Control Body; public string Key, ThumbKey; }
        public readonly Dictionary<string, object> Panels = new Dictionary<string, object>();
        public BatchJob Batch;
        int batchErrors;

        // ---------------- home ----------------
        public void ShowHome(string tab)
        {
            if (!string.IsNullOrEmpty(tab)) Tab = tab;
            Mode = "home";
            Foot.Visible = false;
            SetHeadTabs();
            string t = !Doc.HasImage && Tab != "export" ? "empty" : Tab;
            Control body = GetHomePanel(t);
            foreach (Control o in body.Controls) { TileGroup g = o as TileGroup; if (g != null) g.ClearSelection(); }
            ShowBody(body);
            UpdateTopButtons();
        }

        string StepsKey() { return Doc.Path + "|" + Step.ListToJson(Doc.Steps); }

        // Each home tab is built once in its own body and rebuilt only when what it shows has changed.
        public Control GetHomePanel(string tab)
        {
            string key;
            switch (tab)
            {
                case "looks":
                    key = string.Join(",", Directory.Exists(LooksDir())
                        ? new DirectoryInfo(LooksDir()).GetFiles("*.json").Select(f => f.Name + f.LastWriteTimeUtc.Ticks).ToArray() : new string[0]);
                    break;
                case "export": key = Settings.Signature(); break;
                case "tools": key = "favorites:" + string.Join(",", Settings.Favorites); break;
                default: key = "static"; break;
            }
            object o;
            HomePanel panel = Panels.TryGetValue(tab, out o) ? o as HomePanel : null;
            if (panel == null) { panel = new HomePanel(); panel.Body = NewBodyPanel(); Panels[tab] = panel; }
            if (panel.Key != key)
            {
                if (!panel.Body.Visible) SetBodyBounds(panel.Body);
                ResetBody(panel.Body);
                panel.Body.SuspendLayout();
                switch (tab)
                {
                    case "empty": AddHint("Open a photo to see the tools."); break;
                    case "tools": BuildToolsTab(); break;
                    case "looks": ShowLooksTab(); break;
                    default: ShowExportTab(); break;
                }
                panel.Body.ResumeLayout();
                panel.Key = key;
                panel.ThumbKey = StepsKey();
            }
            else if (tab == "looks" && panel.ThumbKey != StepsKey())
            {
                // after an edit only the pictures change: refresh the existing look tiles instead of rebuilding them
                StartLookThumbs(panel.Body, false);
                panel.ThumbKey = StepsKey();
            }
            return panel.Body;
        }

        void BuildToolsTab()
        {
            List<KeyValuePair<string, string[]>> groups = new List<KeyValuePair<string, string[]>>();
            string[] fav = Settings.Favorites.Where(f => Tools.ContainsKey(f)).ToArray();
            if (fav.Length > 0) groups.Add(new KeyValuePair<string, string[]>("Favorites", fav));
            groups.AddRange(ToolGroups);
            foreach (KeyValuePair<string, string[]> grp in groups)
            {
                AddSection(grp.Key);
                List<TileSpec> items = grp.Value.Select(id => T(Tools[id].Name, Tools[id].Glyph, id)).ToList();
                TileGroup tg = AddTiles(items, -1, 100, 80, (i, t) => StartTool((string)((TileTag)t.Tag).Data, null, -1));
                tg.TileRightClicked += OnToolPin;
            }
            AddHint("Right-click a tool to pin it to Favorites (or remove it).");
        }

        // Right-click on a tool tile pins it to (or removes it from) the Favorites section.
        void OnToolPin(object s, CanvasEventArgs e)
        {
            TileGroup g = (TileGroup)s;
            string id = (string)((TileTag)g.Controls[e.Index].Tag).Data;
            List<string> fav = Settings.Favorites.Where(f => !string.IsNullOrEmpty(f)).ToList();
            string msg;
            if (fav.Contains(id)) { fav.Remove(id); msg = Tools[id].Name + " removed from Favorites"; }
            else { fav.Add(id); msg = Tools[id].Name + " pinned to Favorites"; }
            Settings.Favorites = fav.ToArray();
            Settings.Save();
            Later(() => { ShowHome("tools"); SetStatus(msg); });
        }

        // ---------------- looks tab ----------------
        public class LookEntry { public string Name; public List<Step> Steps; public bool User; }

        // All looks: built-in, then the user's own.
        public List<LookEntry> AllLooks()
        {
            List<LookEntry> all = new List<LookEntry>();
            foreach (KeyValuePair<string, List<Step>> kv in BuiltinLooks()) all.Add(new LookEntry { Name = kv.Key, Steps = kv.Value, User = false });
            foreach (KeyValuePair<string, List<Step>> kv in UserLooks()) all.Add(new LookEntry { Name = kv.Key, Steps = kv.Value, User = true });
            return all;
        }

        Img LookThumbBase() { return Doc.RenderPreviewSync(Doc.Steps).FitInside(120); }

        // Look thumbnails are prepared in the background; the first time they fade in from top to bottom.
        void StartLookThumbs(Control body, bool animate)
        {
            List<Tile> tiles = new List<Tile>();
            List<List<Step>> jobs = new List<List<Step>>();
            foreach (Control c in body.Controls)
            {
                TileGroup grp = c as TileGroup;
                if (grp == null) continue;
                foreach (Control t in grp.Controls)
                {
                    LookEntry le = ((TileTag)t.Tag).Data as LookEntry;
                    if (le == null) continue;
                    tiles.Add((Tile)t); jobs.Add(new List<Step>(le.Steps));
                }
            }
            Thumbs.Render(Form, LookThumbBase(), jobs, tiles, animate);
        }

        void ShowLooksTab()
        {
            List<LookEntry> all = AllLooks();
            foreach (bool user in new[] { false, true })
            {
                List<LookEntry> mine = all.Where(l => l.User == user).ToList();
                if (mine.Count == 0) continue;
                if (user) AddSection("My looks");
                AddTiles(mine.Select(l => T(l.Name, "", l)).ToList(), -1, 100, 104, (i, t) =>
                {
                    LookEntry le = (LookEntry)((TileTag)t.Tag).Data;
                    SelectLook(le.Name, le.Steps);
                });
            }
            AddButtons(
                B("Save edits as look", G(0xE710), b => SaveCurrentLook()),
                B("Import look", G(0xE8B5), b => ImportLookFile()),
                B("Looks folder", G(0xE8B7), b => OpenFolder(LooksDir())));
            AddHint("Click a look to preview it, then apply it: it is added to the edit stack as the tools it is made of, each one editable.");
            StartLookThumbs(Body, true);
        }

        // Explorer shows a folder (created when missing).
        public void OpenFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // ---------------- export tab ----------------
        void ShowExportTab()
        {
            List<TileSpec> items = new List<TileSpec> {
                T("Save copy", G(0xE74E), "save"), T("Export", G(0xEDE1), "export"), T("Export as...", G(0xE792), "exportas"),
                T("Copy image", G(0xE8C8), "clip"), T("Save project", G(0xE78C), "proj"), T("Open project", G(0xE8E5), "openproj"),
                T("Batch edit", G(0xE8B9), "batch"), T("Export folder", G(0xE8B7), "folder"), T("Settings", G(0xE713), "settings") };
            AddTiles(items, -1, 100, 86, (i, t) =>
            {
                switch ((string)((TileTag)t.Tag).Data)
                {
                    case "save": SaveCopy(); break;
                    case "export": ExportToFolder(); break;
                    case "exportas": ExportAs(); break;
                    case "clip": CopyToClipboard(); break;
                    case "proj": SaveProject(); break;
                    case "openproj": OpenProject(null); break;
                    case "batch": StartBatchEdit(); break;
                    case "folder": OpenFolder(Settings.ExportDir); break;
                    case "settings": ShowSettingsDialog(); break;
                }
                ((TileGroup)t.Parent).ClearSelection();
            });
            string fmt = Settings.Format.ToUpperInvariant();
            string rs = Settings.LongSide > 0 ? "longest side " + Settings.LongSide + " px" : "full size";
            AddHint(string.Format("Format: {0}{1}, {2}. Save copy writes next to the original; Export writes to {3}.", fmt, fmt == "JPG" ? " " + Settings.Quality + "%" : "", rs, Settings.ExportDir));
            AddHint("Batch edit applies the current edits to other photos you choose and saves them to the export folder.");
        }

        // Batch editing: the current edit stack applied to many photos, in the background.
        public void StartBatchEdit()
        {
            if (!Doc.HasImage || Doc.Steps.Count == 0) { SetStatus("Edit a photo first: its edits are then applied to the photos you choose."); return; }
            if (Batch != null) { SetStatus("A batch is already running."); return; }
            string[] files;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = ImageFilter; dlg.Multiselect = true; dlg.Title = "Choose the photos to edit like this one";
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                files = dlg.FileNames;
            }
            // edits tied to this photo's content (healed spots, detected faces) are left out or reset
            List<Step> steps = new List<Step>();
            foreach (Step s in Doc.Steps)
            {
                if (s.Tool == "healing") continue;
                Step c = s.Clone();
                if (c.S.ContainsKey("faces")) c.S.Remove("faces");
                steps.Add(c);
            }
            string dir = Settings.ExportDir;
            Directory.CreateDirectory(dir);
            Batch = new BatchJob();
            batchErrors = 0;
            Batch.Progress += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Error)) batchErrors++;
                if (e.Done < e.Total) SetStatus(string.Format("Batch edit: {0} of {1} done ({2})", e.Done, e.Total, Path.GetFileName(e.File)));
                else
                {
                    string msg = "Batch edit finished: " + (e.Total - batchErrors) + " photos saved to " + Settings.ExportDir;
                    if (batchErrors > 0) msg += " (" + batchErrors + " could not be processed)";
                    SetStatus(msg);
                    Batch = null;
                }
            };
            SetStatus("Batch edit: 0 of " + files.Length + " done");
            Batch.Start(Form, files, steps, dir, Settings.Format, Settings.Quality, Settings.LongSide, Settings.KeepExif);
        }

        // ---------------- edit stack ----------------
        public string StepTitle(Step s)
        {
            if (s.Tool == "look") return "Look: " + s.Str("name", "");
            if (s.Tool == "curves" && s.Str("tonecurve", "") == "1") return "Tone curve";
            ToolDef d;
            string n = Tools.TryGetValue(s.Tool, out d) ? d.Name : s.Tool;
            string from = s.Str("look", "");
            return from.Length > 0 ? n + "  ·  " + from : n;
        }

        public void ShowStack()
        {
            if (!Doc.HasImage) return;
            Mode = "stack";
            Foot.Visible = false;
            SetHeadTitle("Edit stack", true);
            ClearBody();
            Body.SuspendLayout();
            List<Step> steps = Doc.Steps;
            if (steps.Count == 0) AddHint("No edits yet. Every tool you apply appears here, and stays editable.");
            int w = BodyW();
            for (int i = steps.Count - 1; i >= 0; i--)
            {
                Step s = steps[i];
                ToolDef def;
                string glyph = Tools.TryGetValue(s.Tool, out def) ? def.Glyph : G(0xE790);
                string title = StepTitle(s) + (s.MaskOn ? "   masked: " + MaskText(s) : "") + (s.Hidden ? "   hidden" : "");
                StackRow row = Ui.StepEntry(Body, i, glyph, title, !Pipeline.IsGeometric(s.Tool), s.Hidden, w);
                row.Action += (o, e) =>
                {
                    int idx = e.Index; string a = new[] { "eye", "edit", "brush", "del" }[(int)e.Value];
                    Later(() => InvokeStackAction(idx, a));
                };
                Tips.SetToolTip(row, (s.Hidden ? "Show this step" : "Hide this step (the steps below and above it stay)") +
                    "  ·  Edit  ·  Stack brush (paint where this step applies)  ·  Delete");
            }
            // the unedited photo closes the list, like the bottom of a layer stack
            if (steps.Count > 0) Ui.StackEntry(Body, -1, G(0xE91B), "Original photo", false, true, w);
            AddSection("Edits");
            AddButtons(
                B("Copy edits", G(0xE8C8), b => { ClipSteps = Step.ListToJson(Doc.Steps); SetStatus("Edits copied. Open another photo and paste them."); }),
                B("Paste edits", G(0xE77F), b => PasteEdits()),
                B("Save look", G(0xE710), b => SaveCurrentLook()),
                B("Revert", G(0xE7A7), b => { if (Doc.Steps.Count > 0) { Doc.SetSteps(new List<Step>()); RequestRender(); ShowStack(); } }));
            AddHint("The eye hides a step without deleting it: the photo shows the steps below and above it (exports too). Edit reopens a tool with its settings. The stack brush paints where a step is visible. Later steps are recomputed automatically.");
            Body.ResumeLayout();
            UpdateTopButtons();
        }

        public void PasteEdits()
        {
            if (string.IsNullOrEmpty(ClipSteps)) { SetStatus("Nothing to paste: copy edits from a photo first."); return; }
            List<Step> cur = Step.CloneList(Doc.Steps);
            cur.AddRange(Step.ListFromJson(ClipSteps));
            Doc.SetSteps(cur);
            RequestRender();
            ShowStack();
        }

        public void InvokeStackAction(int i, string a)
        {
            Step s = Doc.Steps[i];
            switch (a)
            {
                case "edit":
                    if (s.Tool == "look")
                    {
                        // looks saved in older projects were one sealed step: split them in place
                        List<Step> all = new List<Step>();
                        for (int k = 0; k < Doc.Steps.Count; k++)
                            if (k == i) all.AddRange(LookSteps(s)); else all.Add(Doc.Steps[k].Clone());
                        Doc.SetSteps(all); RequestRender(); ShowStack();
                    }
                    else { ReturnTo = "stack"; StartTool(s.Tool, s, i); }
                    break;
                case "brush": ReturnTo = "stack"; StartStackBrush(i, ""); break;
                case "del": Doc.RemoveStep(i); RequestRender(); ShowStack(); break;
                // the eye: the photo is shown without this step (the steps below and above it stay); undo brings it back
                case "eye":
                    Doc.SetHidden(i, !s.Hidden);
                    SetStatus(StepTitle(s) + (s.Hidden ? " hidden" : " shown again"));
                    RequestRender(); ShowStack();
                    break;
            }
        }

        // The edit stack's brush: opens the step's own tool with its mask shown and the brush ready (the mask panel
        // does the rest). Steps whose tool cannot be masked open as they are.
        public void StartStackBrush(int i, string smart)
        {
            Step s = Doc.Steps[i];
            StartTool(s.Tool, s, i);
            if (!TestMaskable()) return;
            if (!string.IsNullOrEmpty(smart)) SetMaskKind(smart, false); else SetMaskPaint(1, false);
        }

        // ---------------- open / export ----------------
        public void OpenImageDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = ImageFilter;
                if (!string.IsNullOrEmpty(Settings.LastDir) && Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (dlg.ShowDialog(Form) == DialogResult.OK) OpenImage(dlg.FileName, null);
            }
        }

        public void OpenImage(string p, List<Step> steps)
        {
            if (Mode == "tool") { Doc.Cancel(); Step = null; }
            Form.Cursor = Cursors.WaitCursor;
            try
            {
                Doc.PreviewSize = Settings.PreviewSize;
                Gpu.Trim();
                Doc.Load(p);
                ProjectFile = null;   // OpenProject sets it again once the photo is open
                // saved masks and everything computed for the previous photo go
                ClearMaskLib();
                Caches.Clear();
                if (steps != null) Doc.Steps = steps;
                Settings.LastDir = Path.GetDirectoryName(p);
                Settings.Save();
                FullShown = false;
                Canvas.SourceWidth = Doc.Original.W;
                Canvas.SetImage(Doc.Preview.ToBitmap());
                Canvas.Fit();
                LblTitle.Text = string.Format("{0}   {1} x {2}", Path.GetFileName(p), Doc.Original.W, Doc.Original.H);
                Form.Text = "PixTune - " + Path.GetFileName(p);
                if (Tab == "export") Tab = "tools";
                ShowHome(null);
                RequestRender();
                // RAW files: say when the photo had to be opened another way (camera JPEG, missing runtime)
                if (!string.IsNullOrEmpty(RawDecoder.Notice)) { GpuAnnounced = true; SetStatus(RawDecoder.Notice); }
                else if (RawDecoder.IsRaw(p) && !RawDecoder.Available) { GpuAnnounced = true; SetStatus("RAW decoder unavailable, using the Windows codec: " + RawDecoder.LastError); }
                PrepareLooksWhenIdle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Form, "This file could not be opened.\n\n" + ex.Message, "PixTune", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Form.Cursor = Cursors.Default; }
        }

        // The Looks tab (34 thumbnails) is prepared once nothing else is going on for a moment, so its first opening
        // is instant without slowing down what is done right after opening a photo.
        Timer looksTimer;
        int looksIdle;
        void PrepareLooksWhenIdle()
        {
            if (looksTimer == null)
            {
                looksTimer = new Timer(); looksTimer.Interval = 250;
                looksTimer.Tick += (s, e) => Safe(() =>
                {
                    if (!Doc.HasImage) { looksTimer.Stop(); return; }
                    bool calm = Idle && Mode == "home" && Control.MouseButtons == MouseButtons.None && Batch == null;
                    looksIdle = calm ? looksIdle + 1 : 0;
                    if (looksIdle < 6) return;
                    looksTimer.Stop();
                    Control cur = Body; GetHomePanel("looks"); Body = cur;
                });
            }
            looksIdle = 0;
            looksTimer.Start();
        }

        // The open photo back as it was opened: edits, history and saved masks go; what was computed from the photo
        // (AI masks, previews) stays. Used by scripts to start each test from the same state without reloading.
        public void ResetEdits()
        {
            if (Mode == "tool") CancelTool();
            Doc.ClearEdits();
            ClearMaskLib();
            ReturnTo = null;
            Canvas.Fit();
            ShowHome("tools");
            RequestRender();
        }

        Img FullRender()
        {
            Form.Cursor = Cursors.WaitCursor;
            SetStatus("Rendering at full resolution...");
            Application.DoEvents();
            try
            {
                Img img = Doc.RenderFull();
                int ls = Settings.LongSide;
                if (ls > 0 && Math.Max(img.W, img.H) > ls) img = img.FitInside(ls);
                return img;
            }
            finally { Form.Cursor = Cursors.Default; }
        }

        string Ext() { return Settings.Format == "png" ? ".png" : Settings.Format == "tif" ? ".tif" : ".jpg"; }

        static string FreeName(string dir, string b, string ext)
        {
            string p = Path.Combine(dir, b + ext);
            for (int n = 2; File.Exists(p); n++) p = Path.Combine(dir, b + " (" + n + ")" + ext);
            return p;
        }

        void WriteExport(string target)
        {
            Img img = FullRender();
            string exif = Settings.KeepExif ? Doc.Path : null;
            string e = Path.GetExtension(target).ToLowerInvariant();
            string fmt = e == ".png" ? "png" : (e == ".tif" || e == ".tiff") ? "tif" : e == ".bmp" ? "bmp" : "jpg";
            ImageIO.Save(img, target, fmt, Settings.Quality, exif);
            SetStatus("Saved: " + target);
        }

        public void SaveCopy()
        {
            if (!Doc.HasImage) return;
            WriteExport(FreeName(Path.GetDirectoryName(Doc.Path), Path.GetFileNameWithoutExtension(Doc.Path) + "_edited", Ext()));
        }

        public void ExportToFolder()
        {
            if (!Doc.HasImage) return;
            Directory.CreateDirectory(Settings.ExportDir);
            WriteExport(FreeName(Settings.ExportDir, Path.GetFileNameWithoutExtension(Doc.Path), Ext()));
        }

        public void ExportAs()
        {
            if (!Doc.HasImage) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "JPEG|*.jpg|PNG|*.png|TIFF|*.tif|Bitmap|*.bmp";
                dlg.FilterIndex = Settings.Format == "png" ? 2 : Settings.Format == "tif" ? 3 : 1;
                dlg.FileName = Path.GetFileNameWithoutExtension(Doc.Path) + "_edited";
                dlg.InitialDirectory = Path.GetDirectoryName(Doc.Path);
                if (dlg.ShowDialog(Form) == DialogResult.OK) WriteExport(dlg.FileName);
            }
        }

        public void CopyToClipboard()
        {
            if (!Doc.HasImage) return;
            using (Bitmap b = FullRender().ToBitmap()) Clipboard.SetImage(b);
            SetStatus("Image copied to the clipboard.");
        }

        // ---------------- projects ----------------
        // A project keeps the whole edit stack, every step still editable (settings, strokes, masks, hidden steps),
        // and the saved masks of the mask panel. Two kinds, both ending in .pixtune:
        //  - with the photo: one file holding the project and the original photo as it was (RAW files included), to
        //    carry the work to another computer (a zip: project.json and photo/<name>);
        //  - linked: the project alone (text), pointing at the photo where it is (looked for next to the project when
        //    that path does not exist, e.g. on another computer with both copied in one folder).
        public string ProjectFile;   // the project the photo was opened from or last saved to
        // where photos of projects opened with their photo are unpacked; emptied when PixTune closes
        public static string ProjectPhotos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixTune\\project-photos");

        public void SaveProject()
        {
            if (!Doc.HasImage) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PixTune project with the photo, to open on any computer (*.pixtune)|*.pixtune|PixTune project only, linked to the photo where it is (*.pixtune)|*.pixtune";
                dlg.FilterIndex = 1;
                string from = ProjectFile ?? Doc.Path;
                dlg.FileName = Path.GetFileNameWithoutExtension(from) + ".pixtune";
                dlg.InitialDirectory = Path.GetDirectoryName(from);
                if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                // a photo unpacked from a project only lives until PixTune closes: it is always packed again
                bool pack = dlg.FilterIndex == 1 || IsUnpackedPhoto(Doc.Path);
                Form.Cursor = Cursors.WaitCursor;
                try
                {
                    SaveProjectTo(dlg.FileName, pack);
                    SetStatus((pack ? "Project saved with the photo: " : "Project saved (the photo stays where it is): ") + dlg.FileName);
                }
                catch (Exception ex) { MessageBox.Show(Form, "The project could not be saved.\n\n" + ex.Message, "PixTune", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { Form.Cursor = Cursors.Default; }
            }
        }

        static bool IsUnpackedPhoto(string path)
        {
            return !string.IsNullOrEmpty(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(ProjectPhotos), StringComparison.OrdinalIgnoreCase);
        }

        string ProjectJsonText(string photoName)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["app"] = "PixTune"; d["version"] = 2;
            d["source"] = IsUnpackedPhoto(Doc.Path) ? Path.GetFileName(Doc.Path) : Doc.Path;
            if (photoName != null) d["photo"] = "photo/" + photoName;
            d["steps"] = Step.ListToJson(Doc.Steps);
            List<Step> masks = new List<Step>();
            foreach (SavedMask m in MaskLib) masks.Add(NewMaskStep(m));
            d["masks"] = Step.ListToJson(masks);
            System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js.Serialize(d);
        }

        // Writes the project (pack: with the photo). Written next to the target first, so a failure leaves an
        // existing project as it was.
        public void SaveProjectTo(string file, bool pack)
        {
            string tmp = file + ".saving";
            if (!pack) File.WriteAllText(tmp, ProjectJsonText(null));
            else
            {
                string name = Path.GetFileName(Doc.Path);
                using (FileStream fs = new FileStream(tmp, FileMode.Create))
                using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    System.IO.Compression.ZipArchiveEntry pj = zip.CreateEntry("project.json", System.IO.Compression.CompressionLevel.Optimal);
                    using (StreamWriter w = new StreamWriter(pj.Open(), new System.Text.UTF8Encoding(false))) w.Write(ProjectJsonText(name));
                    // photos are compressed already: stored as they are
                    System.IO.Compression.ZipArchiveEntry ph = zip.CreateEntry("photo/" + name, System.IO.Compression.CompressionLevel.NoCompression);
                    using (Stream o = ph.Open()) using (FileStream i = File.OpenRead(Doc.Path)) i.CopyTo(o);
                }
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
            ProjectFile = file;
        }

        public void OpenProject(string file)
        {
            if (string.IsNullOrEmpty(file))
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "PixTune project (*.pixtune)|*.pixtune";
                    if (dlg.ShowDialog(Form) != DialogResult.OK) return;
                    file = dlg.FileName;
                }
            string json, src = null;
            try
            {
                if (IsZip(file))
                {
                    // with the photo: the photo is unpacked where PixTune keeps its temporary files
                    using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(file))
                    {
                        System.IO.Compression.ZipArchiveEntry pj = zip.GetEntry("project.json");
                        if (pj == null) throw new Exception("this file is not a PixTune project");
                        using (StreamReader r = new StreamReader(pj.Open())) json = r.ReadToEnd();
                        Dictionary<string, object> d0 = ParseJson(json);
                        object po;
                        System.IO.Compression.ZipArchiveEntry ph = d0.TryGetValue("photo", out po) ? zip.GetEntry(Convert.ToString(po)) : null;
                        if (ph == null) throw new Exception("the photo is missing from this project");
                        string dir = Path.Combine(ProjectPhotos, Guid.NewGuid().ToString("N").Substring(0, 8));
                        Directory.CreateDirectory(dir);
                        src = Path.Combine(dir, Path.GetFileName(ph.Name));
                        using (Stream i = ph.Open()) using (FileStream o = File.Create(src)) i.CopyTo(o);
                    }
                }
                else json = File.ReadAllText(file);
            }
            catch (Exception ex) { MessageBox.Show(Form, "This project could not be opened.\n\n" + ex.Message, "PixTune", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Dictionary<string, object> d;
            try { d = ParseJson(json); }
            catch (Exception ex) { MessageBox.Show(Form, "This project could not be opened.\n\n" + ex.Message, "PixTune", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            object o2;
            if (src == null)
            {
                src = d.TryGetValue("source", out o2) ? Convert.ToString(o2) : "";
                if (!File.Exists(src))
                {
                    string alt = Path.Combine(Path.GetDirectoryName(file), Path.GetFileName(src));
                    if (File.Exists(alt)) src = alt; else { SetStatus("The original photo was not found: " + src + ". Copy it next to the project, or save the project with the photo."); return; }
                }
            }
            OpenImage(src, Step.ListFromJson(d.TryGetValue("steps", out o2) ? Convert.ToString(o2) : "[]"));
            if (!Doc.HasImage || !string.Equals(Doc.Path, src, StringComparison.OrdinalIgnoreCase)) return;
            // the saved masks of the mask panel come back too
            if (d.TryGetValue("masks", out o2))
                foreach (Step ms in Step.ListFromJson(Convert.ToString(o2)))
                {
                    SavedMask m = MaskDefOf(ms);
                    m.Name = ms.Str("maskname", "Mask " + (MaskLib.Count + 1));
                    MaskLib.Add(m);
                }
            if (MaskTiles != null) SyncMaskDock();
            ProjectFile = file;
            Settings.LastDir = Path.GetDirectoryName(file); Settings.Save();
            SetStatus("Project opened: " + Path.GetFileName(file) + (IsUnpackedPhoto(src) ? " (photo included)" : ""));
        }

        static bool IsZip(string file)
        {
            using (FileStream f = File.OpenRead(file)) return f.ReadByte() == 'P' && f.ReadByte() == 'K';
        }

        static Dictionary<string, object> ParseJson(string json)
        {
            System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) throw new Exception("this file is not a PixTune project");
            return d;
        }

        // ---------------- dialogs ----------------
        public void ShowInfo()
        {
            if (!Doc.HasImage) return;
            using (Form f = new Form())
            {
                f.Text = "Details"; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedToolWindow;
                f.ClientSize = new Size(S(420), S(470));
                FileInfo fi = new FileInfo(Doc.Path);
                List<string> lines = new List<string> {
                    "File\t" + fi.Name, "Folder\t" + fi.DirectoryName, string.Format("Size\t{0:N1} MB", fi.Length / 1048576.0),
                    "Dimensions\t" + Doc.Original.W + " x " + Doc.Original.H, string.Format("Megapixels\t{0:N1}", Doc.Original.N / 1e6),
                    "Modified\t" + fi.LastWriteTime, "Edits\t" + Doc.Steps.Count, "Processing\t" + GpuStatus() };
                lines.AddRange(ImageIO.ExifSummary(Doc.Path));
                ListView lv = new ListView();
                lv.View = View.Details; lv.HeaderStyle = ColumnHeaderStyle.None; lv.FullRowSelect = true;
                lv.BackColor = Theme.Panel; lv.ForeColor = Theme.Text; lv.BorderStyle = BorderStyle.None;
                lv.Location = new Point(S(12), S(12)); lv.Size = new Size(S(396), S(270));
                lv.Columns.Add("", S(130)); lv.Columns.Add("", S(250));
                foreach (string l in lines)
                {
                    string[] p = l.Split(new[] { '\t' }, 2);
                    ListViewItem it = new ListViewItem(p[0]); it.SubItems.Add(p.Length > 1 ? p[1] : ""); it.ForeColor = Theme.Text; lv.Items.Add(it);
                }
                CurveEditor hist = new CurveEditor();
                hist.Histogram = ImageIO.Histogram(Doc.RenderPreviewSync(Doc.Steps), -1);
                hist.Enabled = false; hist.ShowCurve = false;
                hist.Location = new Point(S(12), S(292)); hist.Size = new Size(S(396), S(166));
                f.Controls.AddRange(new Control[] { lv, hist });
                DarkDialog(f);
                f.ShowDialog(Form);
            }
        }

        public void ShowSettingsDialog()
        {
            using (Form f = new Form())
            {
                f.Text = "Settings"; f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.ClientSize = new Size(S(460), S(450));
                int y = 16;
                Action<string, Control> row = (label, ctrl) =>
                {
                    Label l = new Label(); l.Text = label; l.AutoSize = true; l.Location = new Point(S(16), S(y + 4));
                    ctrl.Location = new Point(S(170), S(y));
                    f.Controls.Add(l); f.Controls.Add(ctrl);
                    y += 40;
                };
                ComboBox fmt = new ComboBox(); fmt.DropDownStyle = ComboBoxStyle.DropDownList; fmt.Width = S(120);
                fmt.Items.AddRange(new object[] { "JPG", "PNG", "TIF" }); fmt.SelectedItem = Settings.Format.ToUpperInvariant();
                row("Format", fmt);
                NumericUpDown q = new NumericUpDown(); q.Minimum = 30; q.Maximum = 100; q.Value = Math.Max(30, Math.Min(100, Settings.Quality)); q.Width = S(80);
                row("JPG quality (%)", q);
                ComboBox sz = new ComboBox(); sz.DropDownStyle = ComboBoxStyle.DropDownList; sz.Width = S(160);
                int[] sizes = { 0, 4096, 3000, 2000, 1600, 1080 };
                sz.Items.AddRange(new object[] { "Don't resize", "4096 px", "3000 px", "2000 px", "1600 px", "1080 px" });
                sz.SelectedIndex = Math.Max(0, Array.IndexOf(sizes, Settings.LongSide));
                row("Image sizing", sz);
                CheckBox ex = new CheckBox(); ex.Text = "Keep camera data (EXIF)"; ex.Checked = Settings.KeepExif; ex.AutoSize = true;
                row("Metadata", ex);
                ComboBox pv = new ComboBox(); pv.DropDownStyle = ComboBoxStyle.DropDownList; pv.Width = S(160);
                int[] pvs = { 1200, 1600, 2400 };
                // at most this size: the preview is never bigger than the photo on screen at Fit
                pv.Items.AddRange(new object[] { "Fast (up to 1200 px)", "Balanced (up to 1600 px)", "Detailed (up to 2400 px)" });
                pv.SelectedIndex = Math.Max(0, Array.IndexOf(pvs, Settings.PreviewSize));
                row("Preview quality", pv);
                CheckBox gpu = new CheckBox(); gpu.Text = "Use the GPU (graphics card)"; gpu.Checked = Settings.UseGpu; gpu.AutoSize = true;
                Tips.SetToolTip(gpu, GpuStatus());
                row("Acceleration", gpu);
                ComboBox ap = new ComboBox(); ap.DropDownStyle = ComboBoxStyle.DropDownList; ap.Width = S(160);
                ap.Items.AddRange(new object[] { "Dark", "Light" }); ap.SelectedItem = Settings.Appearance == "Light" ? "Light" : "Dark";
                row("Appearance", ap);
                TextBox dir = new TextBox(); dir.Text = Settings.ExportDir; dir.Width = S(220);
                row("Export folder", dir);
                Button br = new Button(); br.Text = "..."; br.Size = new Size(S(36), S(24)); br.Location = new Point(S(396), S(y - 40)); br.FlatStyle = FlatStyle.Flat;
                br.Click += (s, e) =>
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog()) { d.SelectedPath = dir.Text; if (d.ShowDialog() == DialogResult.OK) dir.Text = d.SelectedPath; }
                };
                f.Controls.Add(br);
                // AI models on the graphics card: what the GPU check found, and a button to check them all again
                Button gt = new Button(); gt.Text = "Auto test GPU"; gt.Size = new Size(S(120), S(26)); gt.FlatStyle = FlatStyle.Flat;
                Label gs = new Label(); gs.AutoSize = false; gs.Size = new Size(S(160), S(38)); gs.Location = new Point(S(296), S(y));
                row("GPU use", gt);
                f.Controls.Add(gs);
                Action showGpu = () =>
                {
                    // first line: where the image tools run (measured on this PC); second: the AI models
                    string speed = GpuCheck.SpeedSummary().Replace("Image tools: ", "Tools: ");
                    gs.Text = GpuCheck.Running ? "Testing: " + GpuCheck.Working : speed + "\r\nAI: " + GpuCheck.Summary();
                    gt.Enabled = !GpuCheck.Running && Gpu.Enabled;
                    string d = GpuCheck.Details();
                    // every card in the machine, so it is clear which one is in use
                    string cards = "";
                    for (int i = 0; i < Gpu.Adapters.Count; i++)
                        cards += "\r\n" + (i == Gpu.AdapterIndex ? "> " : "   ") + Gpu.Adapters[i];
                    if (cards.Length > 0) cards = "\r\nGraphics cards found:" + cards;
                    if (GpuCheck.Cards.Length > 0) cards += "\r\nTimed:\r\n" + GpuCheck.Cards;
                    Tips.SetToolTip(gs, GpuCheck.SpeedSummary() + "\r\n" + (d.Length > 0 ? d : "No AI model installed") + cards);
                    Tips.SetToolTip(gt, "Times the image tools on every graphics card in this PC and on the processor, keeps whichever is\r\n" +
                        "quickest here, and tries every installed AI model on the chosen card again (a model that cannot run\r\n" +
                        "there uses the processor).");
                };
                showGpu();
                System.Windows.Forms.Timer gtTimer = new System.Windows.Forms.Timer(); gtTimer.Interval = 400;
                gtTimer.Tick += (s, e) => showGpu();
                gtTimer.Start();
                f.FormClosed += (s, e) => { gtTimer.Stop(); gtTimer.Dispose(); };
                gt.Click += (s, e) =>
                {
                    if (RunGpuCheck(true)) showGpu();
                };
                // which version this is, and a button to look on GitHub for a newer one
                Button ut = new Button(); ut.Text = "Check for updates"; ut.Size = new Size(S(120), S(26)); ut.FlatStyle = FlatStyle.Flat;
                Label us = new Label(); us.AutoSize = false; us.Size = new Size(S(160), S(38)); us.Location = new Point(S(296), S(y));
                CheckBox au = new CheckBox(); au.Text = "look once a day"; au.Checked = Settings.CheckUpdates;
                au.AutoSize = true; au.Location = new Point(S(296), S(y + 20)); au.ForeColor = Theme.Dim;
                row("Version", ut);
                f.Controls.Add(us); f.Controls.Add(au);
                Action showUpd = () =>
                {
                    us.Text = Updater.Working.Length > 0 ? Updater.Working
                        : Updater.Available ? Updater.Latest + " is available"
                        : Updater.LastError.Length > 0 ? "This copy is " + Updater.Current
                        : "This copy is " + Updater.Current;
                    ut.Enabled = !Updater.Checking && !Updater.Installing;
                    string nl = Environment.NewLine;
                    Tips.SetToolTip(us, "This copy is PixTune " + Updater.Current
                        + (Updater.Latest.Length > 0 ? nl + "Newest published: " + Updater.Latest : "")
                        + (Updater.LastError.Length > 0 ? nl + Updater.LastError : ""));
                    Tips.SetToolTip(ut, "Looks on GitHub for a newer PixTune and offers to put it in place." + nl
                        + "Nothing is replaced without being asked, and the version you have is kept so it can be put back.");
                };
                showUpd();
                System.Windows.Forms.Timer utTimer = new System.Windows.Forms.Timer(); utTimer.Interval = 400;
                utTimer.Tick += (s, e) => showUpd();
                utTimer.Start();
                f.FormClosed += (s, e) => { utTimer.Stop(); utTimer.Dispose(); };
                ut.Click += (s, e) => LookForUpdate(true);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Location = new Point(S(270), S(402)); ok.Size = new Size(S(84), S(32));
                Button ca = new Button(); ca.Text = "Cancel"; ca.DialogResult = DialogResult.Cancel; ca.Location = new Point(S(362), S(402)); ca.Size = new Size(S(84), S(32));
                f.Controls.AddRange(new Control[] { ok, ca }); f.AcceptButton = ok; f.CancelButton = ca; f.ActiveControl = ok;
                DarkDialog(f);
                if (f.ShowDialog(Form) != DialogResult.OK) return;
                Settings.Format = ((string)fmt.SelectedItem).ToLowerInvariant();
                Settings.Quality = (int)q.Value;
                Settings.LongSide = sizes[sz.SelectedIndex];
                Settings.KeepExif = ex.Checked;
                Settings.ExportDir = dir.Text;
                Settings.UseGpu = gpu.Checked;
                Settings.CheckUpdates = au.Checked;
                Gpu.Enabled = gpu.Checked;
                SetStatus("Processing on " + GpuStatus());
                if ((string)ap.SelectedItem != Settings.Appearance)
                {
                    Settings.Appearance = (string)ap.SelectedItem;
                    SetStatus("Appearance set to " + Settings.Appearance + ": restart PixTune to apply it.");
                }
                int newPv = pvs[pv.SelectedIndex];
                if (newPv != Settings.PreviewSize)
                {
                    Settings.PreviewSize = newPv;
                    if (Doc.HasImage) { Doc.SetPreviewSize(newPv); InputCache = null; RequestRender(); }
                }
                Settings.Save();
                if (Mode == "home") ShowHome(null);
            }
        }
    }
}

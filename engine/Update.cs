using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace PixTune
{
    // Looks on GitHub for a newer PixTune and puts it in place. Only ever talks to the one repository below,
    // only over https, and never replaces anything until the download has been unpacked and checked to be a
    // PixTune folder. What is replaced is kept in a dated backup beside the cache, so a bad update can be undone
    // by copying it back. The program is source that is compiled when it starts, so the new code runs at the
    // next start: nothing is patched underneath a running window.
    public static class Updater
    {
        public const string Repo = "tybiboune/PixTune";
        public static string Current = "0.0.0";          // from the VERSION file beside PixTune.ps1
        public static string Latest = "", Notes = "", Url = "", LastError = "", Working = "";
        public static bool Checking, Installing;
        public static DateTime Checked = DateTime.MinValue;
        public static event EventHandler Changed;

        public static bool Available
        {
            get { return Latest.Length > 0 && Compare(Latest, Current) > 0 && Url.Length > 0; }
        }

        static void Raise() { EventHandler h = Changed; if (h != null) h(null, EventArgs.Empty); }

        public static void ReadVersion(string root)
        {
            try
            {
                string f = Path.Combine(root, "VERSION");
                if (File.Exists(f)) Current = File.ReadAllText(f).Trim();
            }
            catch { }
        }

        // "1.2.3" against "1.10.0": compared piece by piece as numbers, so 10 comes after 2
        public static int Compare(string a, string b)
        {
            string[] x = Clean(a).Split('.'), y = Clean(b).Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int xi = i < x.Length ? Num(x[i]) : 0, yi = i < y.Length ? Num(y[i]) : 0;
                if (xi != yi) return xi < yi ? -1 : 1;
            }
            return 0;
        }
        static string Clean(string s) { s = (s ?? "").Trim(); return s.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? s.Substring(1) : s; }
        static int Num(string s) { int n; int.TryParse(new string(Array.FindAll(s.ToCharArray(), char.IsDigit)), out n); return n; }

        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "PixTune-Updater";                 // GitHub turns away a request without one
            r.Accept = "application/vnd.github+json";
            r.Timeout = 20000;
            using (WebResponse w = r.GetResponse())
            using (StreamReader sr = new StreamReader(w.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // the newest release, if there is one newer than this copy
        public static bool Check()
        {
            if (Checking) return false;
            Checking = true; LastError = ""; Working = "Looking for a newer version"; Raise();
            try
            {
                string s = Get("https://api.github.com/repos/" + Repo + "/releases/latest");
                Latest = Field(s, "tag_name");
                Notes = Field(s, "body");
                Url = ZipFrom(s);
                Checked = DateTime.Now;
                return Available;
            }
            catch (Exception ex) { LastError = Short(ex); Latest = ""; Url = ""; return false; }
            finally { Checking = false; Working = ""; Raise(); }
        }

        // the first asset whose name ends in .zip, or failing that the source zip of the release
        static string ZipFrom(string json)
        {
            int at = 0;
            while (true)
            {
                int k = json.IndexOf("\"browser_download_url\"", at, StringComparison.Ordinal);
                if (k < 0) break;
                string u = Field(json.Substring(k), "browser_download_url");
                if (u.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Trusted(u)) return u;
                at = k + 20;
            }
            string z = Field(json, "zipball_url");
            return Trusted(z) ? z : "";
        }

        // nothing is fetched from anywhere but GitHub's own hosts
        static bool Trusted(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h == "api.github.com" || h.EndsWith(".githubusercontent.com");
        }

        static string Field(string json, string name)
        {
            int k = json.IndexOf("\"" + name + "\"", StringComparison.Ordinal);
            if (k < 0) return "";
            int c = json.IndexOf(':', k); if (c < 0) return "";
            int q = json.IndexOf('"', c + 1); if (q < 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = q + 1; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '\\' && i + 1 < json.Length)
                {
                    char n = json[++i];
                    sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n == 't' ? '\t' : n);
                    continue;
                }
                if (ch == '"') break;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        static string Short(Exception ex)
        {
            WebException we = ex as WebException;
            HttpWebResponse r = we != null ? we.Response as HttpWebResponse : null;
            if (r != null && r.StatusCode == HttpStatusCode.NotFound) return "no release has been published yet";
            return ex.Message;
        }

        public static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixTune\\update"); }
        }

        // Downloads the release and puts it in place. Returns the folder the old files were kept in, or null.
        public static string Install(string appRoot)
        {
            if (!Available || Installing) return null;
            Installing = true; LastError = ""; Raise();
            string unpack = Path.Combine(Folder, "unpacked"), zip = Path.Combine(Folder, "download.zip");
            try
            {
                Working = "Downloading " + Latest; Raise();
                Directory.CreateDirectory(Folder);
                if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
                if (File.Exists(zip)) File.Delete(zip);
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "PixTune-Updater");
                    wc.DownloadFile(Url, zip);
                }
                Working = "Unpacking"; Raise();
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, unpack);
                string from = FindApp(unpack);
                if (from == null) { LastError = "what was downloaded is not a PixTune folder"; return null; }

                Working = "Keeping a copy of this version"; Raise();
                string backup = Path.Combine(Folder, "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                CopyTree(appRoot, backup, true);
                Working = "Putting the new version in place"; Raise();
                CopyTree(from, appRoot, false);
                File.WriteAllText(Path.Combine(appRoot, "VERSION"), Clean(Latest));
                Current = Clean(Latest);
                return backup;
            }
            catch (Exception ex) { LastError = Short(ex); return null; }
            finally { Installing = false; Working = ""; Raise(); try { if (File.Exists(zip)) File.Delete(zip); } catch { } }
        }

        // the folder inside the download that holds PixTune itself (a release zip wraps it in one folder)
        static string FindApp(string root)
        {
            if (IsApp(root)) return root;
            foreach (string d in Directory.GetDirectories(root))
                if (IsApp(d)) return d;
            return null;
        }
        static bool IsApp(string d)
        {
            return File.Exists(Path.Combine(d, "PixTune.ps1")) && Directory.Exists(Path.Combine(d, "engine"))
                && File.Exists(Path.Combine(d, "engine", "Core.cs"));
        }

        // only the parts of PixTune: whatever else sits in the folder is left alone
        static readonly string[] Parts = { "engine", "lib", "tests" };
        static void CopyTree(string from, string to, bool backup)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from, "*.ps1")) Copy(f, to);
            foreach (string f in Directory.GetFiles(from, "*.bat")) Copy(f, to);
            foreach (string n in new[] { "README.md", "VERSION" })
            {
                string f = Path.Combine(from, n);
                if (File.Exists(f)) Copy(f, to);
            }
            foreach (string part in Parts)
            {
                string src = Path.Combine(from, part);
                if (!Directory.Exists(src)) continue;
                string dst = Path.Combine(to, part);
                Directory.CreateDirectory(dst);
                foreach (string f in Directory.GetFiles(src)) Copy(f, dst);
            }
            // the native folder is big and rarely changes: only brought over when the download has one
            string nat = Path.Combine(from, "native");
            if (!backup && Directory.Exists(nat))
            {
                string dst = Path.Combine(to, "native");
                Directory.CreateDirectory(dst);
                foreach (string f in Directory.GetFiles(nat)) Copy(f, dst);
            }
        }
        // The native folder holds the libraries this very process has loaded: libraw once a RAW file has been
        // opened, onnxruntime once any AI model has run. Windows will not let a loaded library be written, and
        // the update stopped there with "the process cannot access the file libraw.dll". They hardly ever change
        // between versions, so a file that is already the same byte for byte is left alone, which is the usual
        // case; one that really has changed is moved aside, which Windows does allow while it is open, and what
        // is left behind is swept up at the next start.
        static void Copy(string file, string toDir)
        {
            string to = Path.Combine(toDir, Path.GetFileName(file));
            if (Same(file, to)) return;
            try { File.Copy(file, to, true); }
            catch (IOException)
            {
                try { File.Move(to, to + OldMark + DateTime.Now.ToString("yyyyMMddHHmmss")); }
                catch (IOException)
                {
                    throw new IOException(Path.GetFileName(to) + " is in use and could not be replaced. Close PixTune"
                        + " and run the update again.");
                }
                File.Copy(file, to, true);
            }
        }

        public const string OldMark = ".replaced-";

        static bool Same(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a), fb = new FileInfo(b);
                if (!fb.Exists || fa.Length != fb.Length) return false;
                using (FileStream sa = File.OpenRead(a))
                using (FileStream sb = File.OpenRead(b))
                {
                    byte[] ba = new byte[65536], bb = new byte[65536];
                    int n;
                    while ((n = sa.Read(ba, 0, ba.Length)) > 0)
                    {
                        int m = 0;
                        while (m < n) { int k = sb.Read(bb, m, n - m); if (k <= 0) return false; m += k; }
                        for (int i = 0; i < n; i++) if (ba[i] != bb[i]) return false;
                    }
                    return true;
                }
            }
            catch (Exception) { return false; }   // cannot tell: copy it and find out
        }

        // A library that was in use when it was replaced is still on disk under its old name. It is not in use
        // now, at the start of the next run, so it goes.
        public static void SweepReplaced(string root)
        {
            try
            {
                string nat = Path.Combine(root, "native");
                if (!Directory.Exists(nat)) return;
                foreach (string f in Directory.GetFiles(nat, "*" + OldMark + "*"))
                    try { File.Delete(f); } catch (Exception) { }
            }
            catch (Exception) { }
        }
    }
}

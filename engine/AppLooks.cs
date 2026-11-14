using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixTune
{
    // Built-in looks (preset combinations of tools) and user looks saved as JSON.
    public partial class App
    {
        public static PixTune.Step NewLookStep(string tool, IDictionary p = null, IDictionary s = null)
        {
            PixTune.Step st = new PixTune.Step(tool);
            if (p != null) foreach (DictionaryEntry kv in p) st.P[Convert.ToString(kv.Key)] = Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
            if (s != null) foreach (DictionaryEntry kv in s) st.S[Convert.ToString(kv.Key)] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return st;
        }

        // key, value, key, value... in the order given
        static OrderedDictionary LookValues(params object[] kv)
        {
            OrderedDictionary d = new OrderedDictionary();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        static PixTune.Step LS(string tool, params object[] kv) { return NewLookStep(tool, LookValues(kv)); }

        public static List<KeyValuePair<string, List<PixTune.Step>>> BuiltinLooks()
        {
            List<KeyValuePair<string, List<PixTune.Step>>> r = new List<KeyValuePair<string, List<PixTune.Step>>>();
            Action<string, PixTune.Step[]> add = (name, steps) => r.Add(new KeyValuePair<string, List<PixTune.Step>>(name, steps.ToList()));

            // Recipes marked (S) follow the tool sequence the mobile app's look adds to the stack; amounts are estimates.
            add("Portrait", new[] { LS("portrait", "spotlight", 25, "smoothing", 30, "eyes", 25), LS("glamour", "glow", 20), LS("vignette", "outer", -25) }); // (S)
            add("Smooth", new[] { LS("glamour", "glow", 30), LS("portrait", "spotlight", 15, "smoothing", 35, "eyes", 15) }); // (S)
            add("Pop", new[] { LS("tune", "contrast", 25, "saturation", 30, "ambiance", 25), LS("details", "structure", 25) });
            add("Accentuate", new[] { LS("tune", "ambiance", 40, "contrast", 15, "saturation", 15, "shadows", 20), LS("details", "structure", 30, "sharpening", 20) });
            add("Faded Glow", new[] { LS("portrait", "spotlight", 15, "smoothing", 25, "eyes", 15), LS("vintage", "style", 1, "strength", 40, "vignette", 20),
                LS("glamour", "glow", 35), LS("tonal", "high", 20, "mid", 20, "low", 20) }); // (S)
            add("Morning", new[] { LS("wb", "temperature", 20), LS("details", "structure", 25),
                NewLookStep("curves", null, LookValues("curve_rgb", "0,0;0.25,0.3;1,1")), LS("tune", "saturation", 35) }); // (S)
            add("Bright", new[] { LS("tune", "brightness", 30, "ambiance", 15, "shadows", 25, "saturation", 10) });
            add("Fine Art", new[] { LS("bw", "style", 1, "filter", 3), LS("tonal", "high", 30, "mid", 30, "low", 30), LS("vignette", "outer", -40) });
            add("Push", new[] { LS("tune", "contrast", 35, "saturation", 25, "ambiance", 30, "warmth", 10), LS("details", "structure", 40), LS("vignette", "outer", -30) });
            add("Structure", new[] { LS("details", "structure", 60, "sharpening", 30), LS("tune", "contrast", 10) });
            add("Silhouette", new[] { LS("tune", "brightness", 24, "ambiance", -23), LS("bw", "style", 0, "contrast", 34),
                NewLookStep("curves", null, LookValues("curve_rgb", "0,0;0.25,0.2;0.75,0.8;1,1")) }); // (S) black & white
            add("Film Warm", new[] { LS("grainy", "style", 1, "grain", 20, "strength", 60), LS("tune", "warmth", 15) });
            add("Film Cool", new[] { LS("grainy", "style", 6, "grain", 20, "strength", 60), LS("wb", "temperature", -15) });
            add("Matte", new[] { NewLookStep("curves", null, LookValues("curve_rgb", "0,0.1;0.2,0.18;0.8,0.85;1,0.95")), LS("tune", "saturation", -15) });
            add("Noir Classic", new[] { LS("noir", "style", 2, "wash", 20, "strength", 60, "grain", 30) });
            // film looks (newest releases); LKP1 (Kodak Portra 400) and LFP1 (Fuji Pro 400H) are (S) recipes,
            // the others use the Film tool, style = index in Film.Stocks
            add("LKP1", new[] { LS("tune", "auto", 1), LS("grain", "amount", 25, "size", 30) });
            add("LFP1", new[] { LS("tune", "auto", 1), LS("grain", "amount", 15, "size", 30), LS("tune", "auto", 1), LS("grain", "amount", 15, "size", 40) });
            add("Portra", new[] { LS("film", "style", 1) });
            add("Gold", new[] { LS("film", "style", 3) });
            add("Ektachrome", new[] { LS("film", "style", 6) });
            add("Kodachrome", new[] { LS("film", "style", 7) });
            add("Superia", new[] { LS("film", "style", 11) });
            add("Pro 400H", new[] { LS("film", "style", 13) });
            add("Velvia", new[] { LS("film", "style", 16) });
            add("Optima", new[] { LS("film", "style", 19) });
            add("Polaroid", new[] { LS("film", "style", 25) });
            add("SX-70", new[] { LS("film", "style", 26) });
            add("Technicolor", new[] { LS("film", "style", 28) });
            add("CineStill", new[] { LS("film", "style", 29), LS("bloom", "strength", 25, "threshold", 70) });
            add("Tri-X", new[] { LS("film", "style", 9) });
            add("Scala", new[] { LS("film", "style", 21) });
            add("HP5", new[] { LS("film", "style", 22) });
            add("Dreamy", new[] { LS("film", "style", 13, "grain", 30), LS("bloom", "strength", 45, "radius", 70, "threshold", 50) });
            add("Clear Air", new[] { LS("dehaze", "amount", 45), LS("tune", "saturation", 10, "contrast", 10) });
            return r;
        }

        public static string LooksDir()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixTune\\looks");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return d;
        }

        public static List<KeyValuePair<string, List<PixTune.Step>>> UserLooks()
        {
            List<KeyValuePair<string, List<PixTune.Step>>> r = new List<KeyValuePair<string, List<PixTune.Step>>>();
            FileInfo[] files;
            try { files = new DirectoryInfo(LooksDir()).GetFiles("*.json"); }
            catch (Exception) { return r; }
            foreach (FileInfo f in files.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    List<PixTune.Step> steps = PixTune.Step.ListFromJson(File.ReadAllText(f.FullName));
                    string name = Path.GetFileNameWithoutExtension(f.Name);
                    r.RemoveAll(kv => kv.Key == name);
                    r.Add(new KeyValuePair<string, List<PixTune.Step>>(name, steps));
                }
                catch (Exception) { }
            }
            return r;
        }

        public static string SaveUserLook(string name, IEnumerable<PixTune.Step> steps)
        {
            string safe = Regex.Replace(name ?? "", "[\\\\/:*?\"<>|]", "_").Trim();
            if (safe == "") return null;
            List<PixTune.Step> list = new List<PixTune.Step>();
            foreach (PixTune.Step s in steps) list.Add(s);
            string path = Path.Combine(LooksDir(), safe + ".json");
            File.WriteAllText(path, PixTune.Step.ListToJson(list));
            return path;
        }

        // A look is stored in the stack as a single 'look' step that bundles its sub-steps.
        public static PixTune.Step NewLookBundle(string name, IEnumerable<PixTune.Step> steps)
        {
            List<PixTune.Step> list = new List<PixTune.Step>();
            foreach (PixTune.Step s in steps) list.Add(s.Clone());
            PixTune.Step b = new PixTune.Step("look");
            b.SetS("name", name);
            b.SetS("steps", PixTune.Step.ListToJson(list));
            return b;
        }
    }
}

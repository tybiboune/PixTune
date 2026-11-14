# Compares CPU and GPU: speed and maximum pixel difference, at preview and full resolution.
# Timing runs in compiled code so PowerShell's pipeline does not distort the numbers.
param([string]$Image = 'C:\Windows\Web\Screen\img103.jpg')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $root 'lib\Engine.ps1')
Import-PixTuneEngine -Root $root

Add-Type -ReferencedAssemblies ([PixTune.Img].Assembly.Location), 'System.Core.dll' -TypeDefinition @'
using System; using System.Diagnostics; using System.Collections.Generic; using PixTune;
public static class GpuBench {
    static Step S(string tool, params object[] kv) { Step s = new Step(tool); for (int i = 0; i < kv.Length; i += 2) s.Set((string)kv[i], Convert.ToDouble(kv[i + 1])); return s; }
    public static float[] Op(string op, Img im) {
        switch (op) {
            case "gauss": { float[] L = Ops.Luma(im); return Ops.Gauss(L, im.W, im.H, 12 * im.Scale); }
            case "guided": { float[] L = Ops.Luma(im); return Ops.Guided(L, L, im.W, im.H, (int)(20 * im.Scale), 0.01f); }
            case "lens": return Pipeline.Apply(im, S("lensblur", "strength", 70, "bokeh", 1)).D;
            case "grain": return Pipeline.Apply(im, S("film", "style", 23)).D;
            case "hdr": return Pipeline.Apply(im, S("hdr", "strength", 60)).D;
            case "portrait": return Pipeline.Apply(im, S("portrait", "smoothing", 60)).D;
            default: {
                List<Step> l = new List<Step>();
                l.Add(S("tune", "shadows", 30, "highlights", -30, "ambiance", 20)); l.Add(S("details", "structure", 50, "sharpening", 30));
                l.Add(S("hdr", "strength", 60)); l.Add(S("drama", "style", 1)); l.Add(S("lensblur", "strength", 60, "bokeh", 1));
                l.Add(S("film", "style", 1)); l.Add(S("portrait", "smoothing", 60));
                return Pipeline.ApplyAll(im, l).D;
            }
        }
    }
    public static double Time(string op, Img im, bool gpu, int n, out float[] res) {
        Gpu.Enabled = gpu;
        res = Op(op, im); // warm-up
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) res = Op(op, im);
        return sw.Elapsed.TotalMilliseconds / n;
    }
    public static float MaxDiff(float[] a, float[] b) { float m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i])); return m; }
}
'@

"GPU available: $([PixTune.Gpu]::Available)  adapter: $([PixTune.Gpu]::Adapter)  $([PixTune.Gpu]::LastError)"
$b = [PixTune.ImageIO]::Load($Image); $full = [PixTune.Img]::FromBitmap($b); $b.Dispose()
$prev = $full.FitInside(1600)
$names = [ordered]@{ gauss = 'Gaussian blur (sigma 12)'; guided = 'Guided filter (r 20)'; hdr = 'HDR Scape'; portrait = 'Portrait smoothing'; lens = 'Lens blur'; grain = 'Film grain (Delta 3200)'; stack = 'Full 7-tool stack' }

foreach ($res in @(@('preview', $prev, 5), @('full', $full, 2))) {
    $im = $res[1]
    "`n== {0} {1}x{2} ({3:N1} MP) ==" -f $res[0], $im.W, $im.H, ($im.N / 1e6)
    '{0,-26} {1,9} {2,9} {3,8} {4,10}' -f 'operation', 'CPU ms', 'GPU ms', 'speedup', 'max diff'
    foreach ($k in $names.Keys) {
        $oc = $null; $og = $null
        $cpu = [GpuBench]::Time($k, $im, $false, $res[2], [ref]$oc)
        $gpu = [GpuBench]::Time($k, $im, $true, $res[2], [ref]$og)
        '{0,-26} {1,9:N1} {2,9:N1} {3,7:N1}x {4,10:N5}' -f $names[$k], $cpu, $gpu, ($cpu / [Math]::Max(0.1, $gpu)), [GpuBench]::MaxDiff($oc, $og)
    }
}
if ([PixTune.Gpu]::LastError) { "GPU error: $([PixTune.Gpu]::LastError)" }

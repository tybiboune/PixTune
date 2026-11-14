# How quickly the window answers while a render is under way. A background thread posts work to the window
# thread over and over and times the round trip: if the window is free, every trip is a few milliseconds; a long
# trip means the window thread was busy and the photo would not have panned or zoomed in that moment.
$script:StOut = $SelfTest
if (-not (Test-Path $script:StOut)) { New-Item -ItemType Directory $script:StOut -Force | Out-Null }
$script:Trace = New-Object System.Collections.Generic.List[string]
$script:StSteps = New-Object System.Collections.Queue
function global:Expect([bool]$ok, [string]$what) { $script:Trace.Add(("{0} {1}" -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $what)); if (-not $ok) { $script:ErrorLog.Add("expected: $what") } }

$probe = @'
using System; using System.Collections.Generic; using System.Diagnostics; using System.Threading;
using System.Windows.Forms;
public static class Latency {
  public static List<double> Trips = new List<double>();
  static volatile bool stop;
  public static void Start(Control ui) {
    Trips = new List<double>(); At = new List<double>(); stop = false;
    var since = Stopwatch.StartNew();
    var t = new Thread(() => {
      while (!stop) {
        double began = since.Elapsed.TotalMilliseconds;
        var sw = Stopwatch.StartNew();
        var done = new ManualResetEventSlim(false);
        try { ui.BeginInvoke((MethodInvoker)delegate { done.Set(); }); } catch { break; }
        if (!done.Wait(20000)) break;
        lock (Trips) { Trips.Add(sw.Elapsed.TotalMilliseconds); At.Add(began); }
        Thread.Sleep(15);
      }
    });
    t.IsBackground = true; t.Start();
  }
  public static void Stop() { stop = true; }
  public static List<double> At = new List<double>();   // when each trip started, from the first
  public static string Report() {
    lock (Trips) {
      if (Trips.Count == 0) return "no round trips";
      var v = new List<double>(Trips); v.Sort();
      string worst = "";
      for (int i = 0; i < Trips.Count; i++) if (Trips[i] > 50) worst += string.Format(" {0:N0}ms@{1:N0}ms", Trips[i], At[i]);
      return string.Format("{0} round trips, middling {1:N0} ms, nine in ten under {2:N0} ms, slowest {3:N0} ms | the slow ones:{4}",
        v.Count, v[v.Count / 2], v[(int)(v.Count * 0.9)], v[v.Count - 1], worst.Length > 0 ? worst : " none over 50 ms");
    }
  }
  public static double Worst() { lock (Trips) { double m = 0; foreach (double d in Trips) if (d > m) m = d; return m; } }
  // the odd slow trip is the memory collector and varies with the machine; what tells a freeze from a smooth
  // window is how the bulk of the trips behave
  public static double NineInTen() { lock (Trips) { if (Trips.Count == 0) return 0; var v = new List<double>(Trips); v.Sort(); return v[(int)(v.Count * 0.9)]; } }
}
'@
Add-Type -TypeDefinition $probe -ReferencedAssemblies System.Windows.Forms, System.Drawing

St 'watch-memory' {
    try { [AppDomain]::MonitoringIsEnabled = $true } catch { }
    if ($env:PIXTUNE_LAT_RECYCLE -eq 'off') {
        [PixTune.Document]::RecyclePictures = $false
        [PixTune.Pool]::Forget()
        $script:Trace.Add('pictures are not recycled in this run')
    }
    else { [PixTune.Document]::RecyclePictures = $true }
}
St 'cores' {
    # PIXTUNE_LAT_CORES pins the program to a few cores, to stand in for a weaker computer
    $n = $env:PIXTUNE_LAT_CORES
    if ($n) {
        $mask = [int]([Math]::Pow(2, [int]$n) - 1)
        [Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity = [IntPtr]$mask
        # pinning does not change what the program thinks it has, so the tools are held down to match
        if ($env:PIXTUNE_LAT_SPARE -eq 'off') {
            [PixTune.Ops]::SpareCores = 0      # the way this used to work: the tools take every core
            $script:Trace.Add("pinned to $n cores, the tools take all of them")
        }
        else {
            [PixTune.Ops]::SpareCores = [Environment]::ProcessorCount - ([int]$n - 1)
            $script:Trace.Add("pinned to $n cores, the tools may use $([int]$n - 1)")
        }
    }
}
St 'load-big' {
    # a photo large enough that a full render takes real time
    $App.Settings.PreviewSize = 2400
    $App.Doc.PreviewSizeFor = $null
    $App.Doc.RefreshPreview()
}
StWait 'loaded'
St 'stack' {
    # a stack of the heavier tools
    foreach ($pair in @(@('tune', 'shadows', 60), @('details', 'structure', 70), @('hdr', 'strength', 70), @('film', 'strength', 80))) {
        $s = New-Object PixTune.Step $pair[0]; $s.Set($pair[1], $pair[2]); $App.Doc.Steps.Add($s)
    }
}
StWait 'stacked'
St 'warm' {
    # one render first, so the scratch pool holds planes of this photo's size, as it would in real use
    $App.Doc.ClearCaches()
    $App.Renderer.RequestFull()
}
StUntil 'warmed' { -not $App.Renderer.Busy } 120000
St 'measure' {
    [Latency]::Start($App.Form)
    # a changed setting, so the stack is worked out again without emptying the pool
    $App.Doc.Steps[0].Set('shadows', 45)
    $App.Doc.ClearCaches()
    $script:G0 = [GC]::CollectionCount(0); $script:G1 = [GC]::CollectionCount(1); $script:G2 = [GC]::CollectionCount(2)
    $script:Mem = [GC]::GetTotalMemory($false)
    $script:IM = [PixTune.Img]::Made; $script:IB = [PixTune.Img]::MadeBytes
    $script:Alloc = try { [AppDomain]::CurrentDomain.MonitoringTotalAllocatedMemorySize } catch { 0 }
    $script:PR = [PixTune.Pool]::Reused; $script:PM = [PixTune.Pool]::Made
    $App.Renderer.RequestFull()
}
StUntil 'rendering-done' { -not $App.Renderer.Busy } 120000
# two more edits in a row, as someone working on a photo would: a pause put off is not a pause avoided
St 'again-1' { $App.Doc.Steps[0].Set('shadows', 50); $App.Doc.ClearCaches(); $App.Renderer.RequestFull() }
StUntil 'again-1-done' { -not $App.Renderer.Busy } 120000
St 'again-2' { $App.Doc.Steps[0].Set('shadows', 55); $App.Doc.ClearCaches(); $App.Renderer.RequestFull() }
StUntil 'again-2-done' { -not $App.Renderer.Busy } 120000
St 'report' {
    [Latency]::Stop()
    [Threading.Thread]::Sleep(200)
    $script:Trace.Add([Latency]::Report())
    $script:Trace.Add(("memory collections during the render: quick {0}, middle {1}, full {2}; heap {3:N0} -> {4:N0} MB; cores {5}" -f `
        ([GC]::CollectionCount(0) - $script:G0), ([GC]::CollectionCount(1) - $script:G1), ([GC]::CollectionCount(2) - $script:G2),
        ($script:Mem / 1MB), ([GC]::GetTotalMemory($false) / 1MB), [Environment]::ProcessorCount))
    $after = try { [AppDomain]::CurrentDomain.MonitoringTotalAllocatedMemorySize } catch { 0 }
    $script:Trace.Add(("memory actually allocated during the render: {0:N0} MB" -f (($after - $script:Alloc) / 1MB)))
    $script:Trace.Add(("pictures asked for during the render: {0}, weighing {1:N0} MB; scratch planes reused {2}, made {3}" -f `
        ([PixTune.Img]::Made - $script:IM), (([PixTune.Img]::MadeBytes - $script:IB) / 1MB),
        ([PixTune.Pool]::Reused - $script:PR), ([PixTune.Pool]::Made - $script:PM)))
    $w = [Latency]::Worst(); $p = [Latency]::NineInTen()
    # A pan or a zoom is drawn on the window thread. Before the tools left a core free, nine in ten trips were
    # under 38 ms on four cores and the worst was 431 ms; now they are under about 10 ms.
    Expect ($p -lt 25) ("the window answers through a render: nine in ten round trips under $([int]$p) ms (slowest $([int]$w) ms)")
}
Start-StRun -Log 'latency.log'

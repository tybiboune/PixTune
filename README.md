# PixTune

**[Download the latest release](https://github.com/tybiboune/PixTune/releases/latest)** &nbsp;·&nbsp;
[All releases](https://github.com/tybiboune/PixTune/releases) &nbsp;·&nbsp; extract it anywhere and
double-click `PixTune.bat`. Once it is running it keeps itself up to date: Settings > Version > **Check for
updates**.

A desktop photo editor for Windows modelled on the mobile app Snapseed: the same tools, filters, looks,
editable stack and gestures, running on a PC. It needs nothing beyond Windows itself. The whole program
(window, tools and image engine) is C# source in `engine\`: on first launch the C# compiler built into
Windows turns it into a library cached in `%LOCALAPPDATA%\PixTune\cache` (again only when the source
changes), and PowerShell loads that library and runs it inside its own process. No program file (.exe) is
created or started.

## Start

- Extract the folder anywhere and double-click `PixTune.bat` (or drop a photo on it).
- Or from PowerShell: `powershell -STA -ExecutionPolicy Bypass -File PixTune.ps1 [photo]`
- Right-click `Install-Shortcut.ps1` > Run with PowerShell to add desktop and Start menu shortcuts.

The first launch on a PC compiles the engine (about 10-15 seconds); later launches take about 5-8 seconds.

## Camera RAW files

RAW files from over 1,000 cameras open directly (Sony ARW, Nikon NEF, Canon CR2/CR3, Fujifilm RAF, Olympus
ORF, Panasonic RW2, Pentax PEF, DNG...), developed at 16-bit precision by LibRaw 0.22.3 (`native\libraw.dll`,
LGPL 2.1 / CDDL licence, the decoder used by darktable and RawTherapee): camera white balance, sRGB, PPG
demosaicing, the camera's orientation. No Windows extension is needed. LibRaw needs the Microsoft Visual C++
runtime (x64), installed on almost every PC; without it PixTune says so and falls back to the Windows RAW codec.

Files LibRaw cannot read yet, such as the Nikon Z6III (and Z8, Z9, Z f) "High Efficiency" and "High
Efficiency★" NEFs or the Sony A7 V, are developed through Adobe's free **DNG Converter** when it is installed:
PixTune converts the file once in the background (about 10-20 seconds), keeps the DNG in
`%LOCALAPPDATA%\PixTune\dng-cache` (the 40 most recent), and LibRaw develops its raw data like any RAW file.
`PixTune.bat` offers to install it when it is missing (`Install-DngConverter.ps1`: through winget, or Adobe's
installer checked against its fingerprint and Adobe's signature; it needs administrator approval, and a "No" is
remembered, run the script again to be asked again). Adobe's installer is not part of the zip: its licence does
not allow redistributing it, and it weighs about 1.7 GB.
Without the converter, PixTune opens the full-size JPEG the camera stores inside the RAW file and says so in
the status line, rather than showing a broken picture. Z6III files shot in "Lossless compressed" need neither.
Tested on Sony A1 II, A6400, A7 V and Nikon D5300, Z50 II, Z6III (lossless, HE, HE★) samples.

## Requirements

- Windows 10 or Windows 11 (64-bit). Nothing to install: PowerShell, the .NET Framework compiler and
  Direct3D 11 are part of Windows. A graphics card is used when present, otherwise the CPU.
- If the zip was downloaded or received by e-mail, right-click it > Properties > tick **Unblock** before
  extracting; otherwise Windows may show "Windows protected your PC" (More info > Run anyway).
- Company PCs that block PowerShell scripts or on-the-fly compilation (AppLocker, Constrained Language
  mode) cannot run it.

## Tools and filters

| Tools | Filters |
|---|---|
| Tune Image (brightness, ambiance, contrast, saturation, shadows, highlights, warmth, Auto) | HDR Scape (Nature, People, Fine, Strong) |
| Details (structure, sharpening) | Glamour Glow (6 styles) |
| Curves (RGB, R, G, B, luminance, presets, histogram) | Tonal Contrast (high/mid/low tones, protect shadows/highlights) |
| White Balance (temperature, tint, Auto, grey picker) | Drama (Bright 1-3, Dark 1-3) |
| Crop & Rotate (straighten, auto straighten, 90 degrees, flips; free, original, 1:1, DIN, 3:2, 4:3, 5:4, 7:5, 16:9) | Vintage (12 styles, vignette, blur with centre) |
| Smart masks for any effect: Subject, Background, Sky (then refine with the brush) | Grainy Film (16 styles) |
| Perspective (tilt, rotate, scale, free corners, smart/white/black fill) | Retrolux (13 styles, scratches, light leaks, shuffle) |
| Expand (one side at a time, up to 25 %; smart, white, black, AI or generative fill) | Grunge (1500 styles, 5 textures, centre, shuffle) |
| Selective (control points: brightness, contrast, saturation, structure; each zone stretched and turned with handles, like Lens Blur) | Black & White (6 styles, colour filters, grain) |
| Brush (dodge & burn, exposure, temperature, saturation, eraser, mask) | Noir (14 styles, wash, grain) |
| Healing: content-aware fill (multi-scale PatchMatch completion) or optional AI mode (LaMa) for spots and objects | Portrait (face spotlight, skin smoothing, eye clarity, skin tones, presets) |
| RAW Develop (with AI denoise) | Head Pose (rotation, pupil size, smile, focal length) |
| | Face Enhance (eye focus, spotlight, smooth skin brushes) |
| | Lens Blur (elliptical/linear, 7 bokeh shapes, transition, vignette) |
| | Vignette, Double Exposure (8 blend modes), Text (24 styles, 15 colours, invert), Frames (23) |
| Color (hue, saturation, luminance per colour range) | Film (31 film stocks: Portra, Gold, E200, Superia, Pro 400H, Optima, Scala, Polaroid 600/SX-70, Technicolor, HP5...) |
| Dehaze (remove or add haze) | Bloom, Halation, Grain |

Plus: 34 built-in looks (including film looks) and your own saved looks, each added to the stack as
the individual tools it is made of so every part stays editable (Portrait, Smooth, Faded Glow, Morning,
Silhouette, LKP1 and LFP1 use the same tool sequence as the mobile app; amounts are estimates), the edit stack (an eye to hide a step without deleting it, so the photo shows the steps below and above it,
in exports too; edit any step later; stack brush to paint where a step applies; delete), undo/redo, copy/paste edits between photos, before/after compare,
photo details with EXIF and histogram, projects (`.pixtune`, see below), export to JPG/PNG/TIFF with quality,
resizing and EXIF options, batch editing (apply the current edits to many photos), favourite tools (right-click
a tool to pin it), a live histogram (H) and a light appearance (Settings).

## Projects

**Save project** (Export tab) keeps the whole edit stack, every step still editable (settings, brush strokes,
masks, hidden steps, looks as their tools), and the saved masks of the mask panel. The save dialog offers two
kinds, both `.pixtune`:

- **with the photo** (the default): one file holding the project and the original photo as it was, RAW files
  included, to carry the work to another computer. Opening it there unpacks the photo into PixTune's temporary
  folder (emptied when PixTune closes; the project file keeps it), and saving again packs it again;
- **project only**: a small file linked to the photo where it is; on another computer, copy the photo next to
  the project.

**Open project** (Export tab), dropping the file on the window, or passing it to `PixTune.bat` reopens it.
AI results (subject or sky masks, AI healing) are computed again on the other computer, which needs the AI
models installed for them; HEIC, WebP and some RAW files also need the same Windows extensions or Adobe DNG
Converter there. Projects saved by earlier versions still open.

## Tone curve

A tone curve over the live red, green and blue histogram is always visible at the top of the right panel,
like the curve panel of pro editors. It starts with three points (shadows, midtones, highlights) besides the
black and white ends: drag a point to change the tones, click anywhere on the curve to add a point, right-click
a point to remove it, Reset to straighten it. It is stored as a "Tone curve" step that always stays last in
the edit stack, so it is part of undo, copied edits, batch editing, projects and export.

## Mask panel

Under the tone curve, a Mask panel (click its title bar to fold it) limits the open tool to part of the photo,
without leaving the tool. It is available for every tool where a mask makes sense, not for tools that move or
rebuild pixels (crop, rotate, perspective, expand, healing, frames).

- **Start from** Photo (all of it), Subject (the main subject the model finds), Pick, Background or Sky. A click
  on a tile replaces the kinds chosen; Ctrl+click adds it to them (Subject + Sky...) or takes it out.
- **Pick** selects exactly the parts you click: each click adds the person or object under it (a face, a coat,
  a car...), Ctrl+click cuts a part out, Undo click removes the last one, Done ends picking. Each click is marked
  on the photo (green + added, red - cut). It uses MobileSAM, a light Segment Anything model (Apache 2.0
  licence, Qualcomm AI Hub export, about 40 MB, offered for download the first time Pick is used): about a
  second for the first click on a photo, then a fraction of a second per click.
- **Refine** with Paint (brings the edit in) or Erase (takes it out), the brush size showing while painting,
  and Invert. Painting is drawn on the photo at once and the preview keeps updating during the stroke.
- **See it**: the mask is shown in translucent red while it is defined, and the eye button of the title bar shows
  or hides it at any time. Changing a setting of the tool hides it again and ends painting or picking.
- **Stored**: a mask belongs to its step. The MASK title turns green and says what it is made of (for example
  "stored: subject + 2 strokes, inverted"), its kind has a check mark, and the edit stack names it on the step.
  The brush button of a step in the edit stack opens that step's tool with the brush ready.
- **Saved masks, several at once**: the save button of the title bar keeps the tool's whole mask under a name;
  the tool then uses it as that saved mask, and new kinds, picks or strokes add to it. Saved masks are listed in
  the panel: click one to switch it on or off for the open tool (any tool, at any moment, several at once:
  they add up, outlined in the list), the eye shows it in red, the pencil renames it, the bin deletes it.
  Paint, Erase and Invert apply to the whole combination. A step keeps its own copy, so deleting a saved mask
  never changes an applied edit. Saved masks belong to the photo being edited: they are forgotten when another
  photo is opened or when PixTune closes.

When the window closes, PixTune first empties everything it kept about the photo (saved masks, the computed
masks, AI results and previews in memory, and the converted RAW copies in `%LOCALAPPDATA%\PixTune\dng-cache`),
then quits. Opening another photo empties the memory caches the same way. Downloaded AI models and the compiled
program in `%LOCALAPPDATA%\PixTune\cache` are kept: they are not photo data.

## Undo and redo

The undo and redo arrows of the top bar, Ctrl+Z and Ctrl+Shift+Z (or Ctrl+Y) go back and forth through the
last 10 operations. Inside a tool they are the tool's own changes (a slider let go, a style, a stroke, a point,
the crop frame, a mask...); once the tool is applied, it counts as one operation of the photo, which keeps its
own last 10 (tools applied, steps deleted, tone curve changes, looks...).

## Smart masks

Subject, Background and Sky masks are made by AI models when installed (offered the first time one of them is
picked, about 230 MB): BiRefNet (MIT licence) finds the main subject with fine edges, the semantic head of
Detectron2 Panoptic FPN (Apache 2.0 licence, Qualcomm AI Hub export) finds the sky, and an edge-aware filter
snaps both to the photo. A mask is computed once per photo and framing (a few seconds the first time) and
reused for the preview, every slider change, every tool and the export: it is found on the photo with only the
crop, rotation and perspective applied, so later tones and looks do not trigger a new search. Each model is
loaded the first time its kind of mask is asked for, the red overlay is computed in the background so the
window never freezes, and when the window and the renderer ask for the same mask at once the model runs only
once. Without the models, simple colour and position rules are used. Masks can always be refined with the
brush.

Both mask models were put through the same stress as the denoiser (a photo darkened, grainy, white, black). They
stay steady where the denoiser ran away, and so do High detail, AI healing and the Head pose models. The one
limit found: the subject model is confidently wrong on very heavy grain -- it picked half the frame instead of
the subject -- but only when the photo is already as small as the model's own 1024 pixels. On any larger photo
the shrink to 1024 averages the grain away before the model sees it, and the mask comes out the same as on the
clean photo (measured at 2000, 4000 and 6400 pixels wide with grain of 25 levels).

## AI denoise

RAW Develop has an AI denoise slider: NAFNet (MIT licence), trained on real camera and phone sensor noise
(SIDD), removes the noise before exposure and shadows lift it. It runs on the graphics card in 256-pixel
tiles, is computed once per photo, then the slider only sets how much is used. The model (about 430 MB,
Qualcomm AI Hub export) is offered the first time the slider is moved.

**Keeping the model in its range.** The model's numbers run away when what it is shown sits outside what it was
trained on. It then returns values many times brighter than white, and clamping those to a picture turns the area
into coloured mush. It happens at both ends:

- a dark, flat tile -- a night sky, a dark foreground, the bottom of a sunset;
- a tile grainier than any it was trained for, which is to say a genuinely noisy photo. Grain of 25 levels used
  to come back as coloured confetti over the whole picture: further from the clean photo (129 levels) than the
  grain itself was (25).

Both are cured by changing the tile's level before the model sees it and putting it back after. A dark tile is
lifted to a comfortable level; a tile whose answer still comes back beyond anything a picture can hold is shown
again at half the level, up to three times, until the model settles. Scaling never clips, so it is undone
exactly, and a tile the model handles first time -- nearly all of them in a normal photo -- costs nothing.

A graphics card short of memory does not refuse the work either: it answers, and the answer is nonsense. Another
program using the card, or one of the bigger models loaded beside this one, is enough. Every tile was then kept
as it arrived and the photo came back untouched with nothing said. So a tile the card gives up on is shown to the
same model on the processor, and once the processor settles where the card did not, the rest of the photo goes
straight to it: about eight times slower a tile, but it answers, and the status line says the noise is being
removed on the processor. Only then, if the processor gives up too, is a tile kept exactly as it arrived, so the
worst case is an area left undenoised rather than ruined.

How much grain is actually removed, measured by adding a known amount to a clean photo and comparing with it:

| Grain added | Removed, before | Removed, now |
|---|---|---|
| 5 levels | 56% | 56% |
| 10 levels | 60% | 60% |
| 20 levels | 5% | 69% |
| 30 levels | none (the picture was destroyed) | 73% |
| 45 levels | none (the picture was destroyed) | 74% |

`tests\DenoiseTest.ps1` checks a deliberately dark picture and a deliberately grainy one, each against what the
model does when left to itself.

The model's own cost is everything this tool spends -- about 90 ms a tile on a discrete card, eight times that on
a processor -- so running fewer tiles is the only way to make it quicker. Tiles are now spread evenly over the
photo instead of marching by a fixed step and pushing the last one back inside, which used to leave it almost on
top of its neighbour, and they share 16 pixels instead of 24, which the pictures tested cannot tell apart from
sharing 48 (0.14 levels on average either way). A 1.6 MP preview went from 40 tiles to 35, about 9% quicker.

## Finding your way around a zoomed photo

Zoom past Fit and the whole photo appears small in the bottom right corner, with the part on screen marked on
it and everything else dimmed. Press or drag inside that map and the view goes there, so a 24 megapixel photo
at 100% can be crossed in one movement instead of by panning blindly. It appears only while part of the photo
is off screen, sits clear of the zoom badge, and can be used while a tool is open.

## Keeping PixTune up to date

PixTune looks at its GitHub releases for a newer version: once a day in the background, and whenever **Check
for updates** is pressed in Settings, which also shows which version this copy is. It never replaces anything
on its own -- it says what it found and asks, and asks again if the open photo has unsaved edits. What it
replaces is kept first in a dated folder under `%LOCALAPPDATA%\PixTune\update`, so a bad update can be undone
by copying that back. Only `engine`, `lib`, `tests`, the scripts and the README are touched; anything else in
the folder is left alone. It will only fetch from `github.com` and its own content hosts, over https, and
nothing is put in place until the download has been unpacked and found to be a PixTune folder. Turn the daily
look off with the tick box beside the button.

## Working like the mobile app

- Drag horizontally on the photo to change the selected slider, drag vertically to pick another slider.
- Mouse wheel zooms, right or middle button drag pans, double middle-click fits the photo.
- Brushes: Alt + mouse wheel changes the brush size.
- Hold the compare button, or hold `C`, to see the original.

| Shortcut | Action |
|---|---|
| Ctrl+O | Open |
| Ctrl+S / Ctrl+Shift+S / Ctrl+E | Save copy / Export as / Export to folder |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| H | Live histogram on / off |
| Enter / Esc | Apply / Cancel the open tool |
| Up / Down | Select the previous / next slider |
| Ctrl+0 / Ctrl+1 | Fit / 100% (also the Fit and 100% buttons at the top) |

At 100% one photo pixel is one screen pixel: when you zoom past the preview's detail, the photo is
rendered at full resolution (on the GPU, cached so slider changes only recompute the open tool).
The current zoom is shown at the bottom right; the mouse wheel stops on 100% on its way.

## GPU acceleration

The image tools run on the graphics card through Direct3D 11 compute shaders (built into Windows, compiled
in the background when PixTune starts, in under a second). The fastest GPU is picked automatically (the
discrete card on hybrid laptops), shown at the bottom left and in Photo details. It can be turned off in
Settings; without a usable GPU everything runs on the CPU with the same pixels.

A tool runs on the card as one job: the picture is sent once (as three colour planes), the tool's blurs,
edge-aware smoothing, grain and its own per-pixel maths run there with every intermediate picture staying on
the card, and only the result comes back (engine\GpuJob.cs, shaders in engine\GpuKernels.cs). This covers
Tune (and Raw Develop's tone), Details, HDR Scape, Glamour Glow, Tonal Contrast, Drama, Vintage, Grainy Film,
Noir, Film (with its halation, vignette and grain), Bloom, Halation, Grunge and Retrolux. Measured on an RTX
A2000 laptop:

| | CPU | GPU |
|---|---|---|
| Preview (1.7 MP), per slider move | 40-190 ms | 13-55 ms |
| Full resolution (24 MP) | 1.8-8.3 s | 0.2-0.37 s |

Pictures also stay on the card from one step to the next: a tool's result is kept there, the next GPU tool
starts from it, the stack's cached steps keep their copy (so a slider on the third step never sends its input
again), the mask blend and the 0..1 clamp at the end of each tool run there, and the frame shown is converted
to 8 bits on the card, so only 4 bytes per pixel come back. CPU code that reads a picture brings it back once
(and the card's copy is dropped, since it may change the pixels); within a quarter of the card's memory (at
most 2 GB), the pictures used least recently go back to the processor first. The simple per-pixel tools
(White Balance, Curves, Vignette, Color, Black & White, Grain) then run on the card too.

A slider move on the second of five steps (Tune, then Film, Vignette and Curves after it, and the frame):

| | CPU | GPU |
|---|---|---|
| Preview (1.7 MP) | 323 ms | 28 ms |
| Full resolution (24 MP) | 8.9 s | 0.54 s |

`tests\GpuToolTest.ps1` checks that every GPU version gives the same pixels as the CPU code, on the whole photo,
on a live-view part and through a masked stack kept on the card; `tests\Bench-Gpu.ps1` measures the blurs on
your machine. A part is compared inside the margin the stack throws away, because that is the part of it that is
kept and shown: outside it a wide glow differs by about a level, the processor building it on a shrunk copy and
the card at full size.

In a blur on the card, each thread of the downward pass walks a run of rows keeping a running sum, so a pixel
costs two reads whatever the blur's width. The sideways pass keeps its plain sum on purpose: there, neighbouring
threads read neighbouring pixels, which the card's cache serves. This matters most on a weak card, where the
width used to decide everything — a blur of a 10 MP plane at width 96:

| | Before | Now |
|---|---|---|
| Discrete card (RTX A2000) | 47 ms | 34 ms |
| Software card (stands in for a weak one) | 1 539 ms | 311 ms |

A strong graphics card runs the image tools many times faster than the processor, but a weak integrated one
(Intel Iris or UHD, small AMD APUs, which also borrow the PC's RAM) can be slower. So once per card, in the
background after start-up, PixTune times the same edits (Tune, Details, Film and the frame shown) on every card
and on the processor, and the image tools use the quickest; Settings > GPU use shows the choice ("Tools: GPU
(3.1x faster)" or "Tools: processor") and **Auto test GPU** measures again. With an integrated card, the memory PixTune keeps on it is
counted against the PC's RAM (at most 1 GB on a 16 GB PC). `PIXTUNE_GPU=software` runs the shaders on Windows'
software GPU, to try a slow card on any machine.

**More than one graphics card.** Every card in the machine is found and listed in Settings > GPU use (hover the
line), with the one in use marked. **Auto test GPU** times the same three edits on *every* card as well as on
the processor, keeps whichever is quickest here, and remembers it by name, so a later start goes straight to
that card instead of to whichever has the most memory. On this laptop the whole test takes about two seconds
and reports, for instance, `NVIDIA RTX A2000: 21 ms` against `Intel Iris Xe: 49 ms`. The choice survives a
driver change or a new card, because the saved line records which cards were present when it was measured; add
or remove one and the test runs again. `PIXTUNE_GPU_ADAPTER=<number>` still forces a particular card, which is
how each was timed below.

Work is deliberately **not** spread across several cards at once, nor across the card and the processor at the
same time, because both were measured and neither pays on a machine like this one (a laptop with Intel Iris Xe
built in and an NVIDIA RTX A2000 beside it):

| On a 22 MP photo | Tune | Details | Drama |
|---|---|---|---|
| RTX A2000 alone | 101 ms | 107 ms | 107 ms |
| Iris Xe alone | 178 ms | 165 ms | 168 ms |
| Both cards at once, what they manage together | **−27%** | +6% | +20% |

The reason is that none of this work is held up by arithmetic. A tool on the card already keeps about one
processor core busy moving pixels to it and back, while the same tool on the processor uses twelve; both are
limited by how fast memory can be read and written, and a built-in card reads the same memory as the processor.
Running two of them at once therefore shares out the same bandwidth rather than adding any. Measured on one
tool on the card beside another on the processor, the pair took 111% and 117% of the time they take one after
the other -- slower, not faster. Two cards each with their own memory, in a desktop, would not share that
bottleneck and the answer could be different; the pieces needed to try it (choosing a card, listing them all)
are in place.

The AI models are checked on the graphics card once: when PixTune starts and the window has been idle for two
seconds, every installed model not checked yet on this card is opened on it through DirectML and run on a test
input, in the background (Stable Diffusion, several GB, only with Auto test GPU or when Generative fill first
runs; a model short of graphics memory is put on the processor rather than tried again at every launch). A model that fails there, or gives invalid numbers, is never tried on the GPU again
and runs on the processor straight away (LaMa healing is such a case with the current runtime). A model that
fails later while in use is marked the same way. The results are kept in the settings, per model file, graphics
card and runtime, so a new model, card or runtime is checked again. Settings > AI models shows the result
(the tooltip lists each model) and **Auto test GPU** checks them all again, for instance after a driver update.
The face mesh and the subject picker are small models kept on the processor on purpose (it is quicker for them).

## Live view

While a slider moves (or the photo is dragged to change a value, a centre or a zone is moved, or a stroke is
painted), only what the screen shows is computed, at the size it is shown; once the slider is let go the photo
is rendered at full quality.

- At Fit (or any zoom the preview covers), the open tool runs on the photo at screen size instead of the preview
  size, from the stack's cached result just before the tool.
- Zoomed in past the preview, tools that give the same pixels on a part of the photo compute only the part on
  screen plus the margin their filters reach into, drawn over the photo: Tune, Details, Curves, White Balance,
  Brush, Glamour Glow, Tonal Contrast, Drama, Vignette, Lens Blur, Vintage, Grainy Film, Black & White, Noir, Film,
  Bloom, Halation, Grain, Color and Face Enhance. Sizes, centres, strokes and grain are those of the whole photo,
  and one-tap masks (subject, background, sky, picked parts) are always found on the whole photo, then the part
  on screen is taken from them. `tests\LiveRegionTest.ps1` checks every one of these tools against a render of
  the whole photo.
- Tools that need the whole photo (HDR, Dehaze, automatic Tune, Portrait and Head Pose, Selective, Retrolux,
  Grunge, Text, Frames, Double Exposure, Healing, RAW Develop) show the whole photo at preview size while the
  slider moves, which is still several times quicker than full resolution.

A slider move at 100% on a 24 MP photo goes from 0.6-2.7 s (the whole photo) to 50-300 ms (the part on screen).

Most of a tool's work does not depend on its sliders: the luminance of its input, the edge-aware smoothing and
wide blurs Tune (shadows, highlights, ambiance), Details, Tonal Contrast, Drama, HDR Scape and Glamour Glow read.
While a slider moves, the tool's input stays the same cached picture, so that work is done once and kept (on the
processor or on the graphics card, within a share of the memory), and each frame only redoes the tool's own
per-pixel step, with exactly the same pixels. Opening one of these tools prepares it in the background as soon as
its first frame is shown, so even the first frame of a drag is quick. Measured on a drag frame at preview size:
1.7-6x quicker on the processor (Details 284 -> 47 ms on 4 cores), 1.3-3.4x on the graphics card (14-21 ms).

The preview is also the size the photo takes on screen at Fit (at most the Preview quality setting, and followed
when the window is resized): a bigger one only computed pixels the screen cannot show, since zooming in past it
shows the part on screen at full resolution.

Zooming between Fit and 100% uses zoom levels a square root of two apart (100%, 71%, 50%, 35%, 25%...): the part
on screen is computed at the nearest level at or above the screen's, from a copy of the photo at that level made
once (and the steps that need the whole photo, done once per level at that size), not from a crop of the
full-resolution photo shrunk at every zoom step. A fifth of the view is also computed on each side, so a small pan
or zoom out needs no new render; and when the view covers half the photo or more, the whole photo at that level is
made and kept, so panning and coming back to that zoom are free. On a 24 MP Nikon Z6 III photo with Tune, Details,
Vignette and Grain, a sequence of ten zoom steps from 26% to 100% and back went from 33 s to 11.6 s on 4 processor
cores (returning to 30%: 4 s -> 0.05 s) and from 9.7 s to 1.4 s on the graphics card. Grain, Grainy Film and Black &
White now need only a few pixels of margin around the part on screen (their grain), instead of the widest blur's.

The edge-aware smoothing behind Tune, Details, Tonal Contrast, Drama and HDR Scape works on a copy shrunk 2 to 8
times when its radius is large (8 pixels or more), then brings its coefficients back to full size. It reads the
fine texture from the full-size picture, so the result stays within a few levels of the exact filter (on a 25 MP
photo: 0.02-0.19 levels on average, at most 9 levels on a handful of pixels at the photo's edge), on the processor
and the graphics card alike. On that photo the smoothing alone went from 756 ms to 93 ms on the processor, and each
of those tools from 0.7-1.3 s to 0.37-0.67 s. Radii under 8 pixels keep the exact filter.

While a slider or the photo is dragged, the buffers that turn a frame into the picture on screen are reused rather
than allocated each time, and the memory collector avoids its long full passes until the gesture ends; on the
graphics card that removes the occasional slow frame (slowest of 40 frames: 31-34 ms -> 15-17 ms).

Raising every pixel to the same power is one of the slowest things the processor path does, and the tools that
lift or lower brightness (Tune, HDR Scape, Raw Develop, Vintage, Retrolux, Grainy Film, Noir, Film and the looks'
grades) do it on every pixel. The curve is now sampled once per render into a table, and each pixel costs a square
root, a lookup and a blend. The table's steps are spaced by the square root of the value, so they crowd where the
curve bends most: evenly spaced, brightening deep shadows would be 2.4 levels out, this way 0.002. A 24 MP Tune
with brightness on the processor went from 574 ms to 307 ms (without brightness it is 277 ms, so the power itself
went from 247 ms to 30 ms).

Dehaze reads its haze colour -- the mean colour of the haziest 0.2% of the photo -- from a copy 1024 pixels across
instead of ranking every pixel, which is within 0.4 levels of the exact colour on the photos tested. Per slider
move it went from 256 ms to 125 ms at preview size, and from 4.3 s to 1.3 s on a 25 MP photo.

Four more of the slowest tools were reworked, measured on a 22 MP photo with both ways run in one session:

| On the processor | Before | Now | Why |
|---|---|---|---|
| Halation | 1 270 ms | 182 ms | glow built on a smaller copy |
| Film | 1 640 ms | 660 ms | the same (its halation) |
| Bloom | 531 ms | 147 ms | the same |
| Noir | 1 149 ms | 982 ms | its film damage read from a noise grid |
| Retrolux | 726 ms | 617 ms | each light leak as a row times a column |

- A glow is a wide blur of the brightest parts, so it holds nothing fine: it is built on a copy shrunk by up to
  four and stretched back, within 0.7 levels of the full-size glow. A narrow glow (a small photo, a preview) is
  still built at full size, and a part of the photo rendered on its own only shrinks when its corner lands on the
  grid, so patches match the whole render.
- Fractal noise (Noir's film damage, Grunge's textures) is smooth by construction -- its finest octave is a
  fraction of the pattern's size -- so it is worked out on a grid a sixth of that fine and blended between points,
  within 0.7 levels of a value per pixel. Grid points sit at fixed places in the photo, so patches match. A small
  pattern falls back to a value per pixel.
- A light leak's falloff, exp(-(dx*dx + dy*dy)/c), is exp(-dx*dx/c) times exp(-dy*dy/c): one depends only on the
  column and the other only on the row, so two to four exponentials per pixel became two tables (0.00001 levels).

Lens Blur and Dehaze were the two slowest left, and both were losing most of their time to things that had nothing
to do with their real work:

- Lens Blur raises every pixel to a power twice (into linear light for the bokeh discs and back again). One of
  those passes ran on a single core, and with a small blur it ran on the whole photo rather than a shrunken copy,
  which is why a *weak* blur was the slowest setting of all. Both passes now run on every core and read their
  power from a table.
- Its bokeh discs were gathered a tap at a time, about 200 reads per pixel. For every shape offered (circle,
  hexagon, octagon, triangle, star, heart) the taps of one row form a handful of runs of neighbouring pixels, so
  with a running sum along each row a run costs two reads: about 17 pairs instead of 200 singles. It is the same
  sum, within 0.02 levels of gathering each tap.
- Dehaze built its dark channel and its local minimum on a single core, over every pixel of the photo; both now
  run on every core.
- Dehaze smooths its transmission with a guided filter whose two pictures differ, which the smaller-grid filter
  used to refuse. It takes it now, except while the card is doing that step, where the exact filter is already
  quick and the picture stays on the card.

Grunge and Retrolux also gained graphics-card versions, the two tools that had none and so were the slowest things
on a card. On that 22 MP photo Grunge went from 397 ms on the processor to 207 ms on the card, and Retrolux from
726 ms to 350 ms (its scratches are still drawn on the processor, so the picture comes back for them).
`tests\GpuToolTest.ps1` checks both against the processor, on the whole photo and on a part.

Retrolux's damage is drawn as the three things an old print really shows. A scratch is where grit in the gate cut
the emulsion away, so it lets light through and prints bright, with a thin dark ridge of displaced emulsion hard
against one side; it weaves as the film moves, its width breathes, it fades in and out along its length, and it
usually cuts one dye layer, which tints it warm or cool. Then the hairs caught in the gate: thin dark curves that
bend as they lie and taper at both ends. Then the dust, which is dirt on the print printing dark and dust on the
negative printing white, with an edge that wobbles, because a speck has no radius. They are spread across the
frame rather than left to chance, which bunches them against one edge, and come in close pairs, because grit sits
at its own places in the gate. What they replace was straight lines of even brightness and perfectly round dots,
which is what made them look drawn rather than photographed.

On a slow computer the frames are computed at a lower resolution while the slider moves (each frame aims at about
70 ms; down to a third of the size), and letting go renders at full quality. Zoomed in past the preview, letting
go no longer renders the whole photo at full resolution: the preview with every edit goes under the view and the
part on screen is computed at full quality over it (from the latest full-resolution result kept), and panning
computes the part that comes into view once the view stops. The whole photo at full resolution is only made for
exports. A patch needs a margin only for the filters that reach around a pixel (Tune with only brightness,
contrast, saturation or warmth needs none). On a 24 MP Nikon Z6 III photo with 4 processor cores and no GPU help:

| | Before | Now |
|---|---|---|
| Slider move at Fit | 0.78 s | 0.12 s |
| Slider move at 100% | 4.3 s | 1.2 s (lower resolution while moving) |
| Letting go at 100% | 12.6-19.9 s | 2.9 s |

## AI healing and expansion (optional)

Expand fills the new border in one of five ways: Smart (content-aware, continues the texture around the
photo, no extra files), White, Black, AI fill (LaMa) and Generative (Stable Diffusion). One side can be
expanded at a time, by up to a quarter of the photo, which is what keeps a generated extension convincing.

Generative invents new scenery that continues the photo. It runs Stable Diffusion 1.5 inpainting on the
graphics card (DirectX 12, about 30 to 60 seconds; 6 GB of video memory or more recommended): the new
strip is first filled content-aware, then redrawn by the model from that start, so it keeps the scene's
colours and layout. The join is hidden by matching the broad tone, letting the model redraw a band of the
photo's edge, and mixing the two along a sinuous seam that follows the places where they agree best, with
broad tones blended widely and fine detail over a short distance. An optional text box describes what the
extension should show, and Generate again gives another version. The exported photo reuses the extension
shown on screen.

The extension is generated at about 1536 pixels wide, so on large photos it is enlarged when exported or
viewed at 100%. High detail (on by default) restores its definition at that size, adding roughly 15 seconds:
the strip is enlarged with Real-ESRGAN for clean edges, then receives the photo's own fine texture (each part
of the new area is matched against the nearby photo at a coarse scale with PatchMatch, and the finest detail
of the best match is copied in), and finally grain is added until its noise level matches the photo's.
A low-strength second diffusion pass was tried for the texture and dropped: much slower, and it invented
structures. The files (about 2.2 GB: the ONNX fp16 version of Stable Diffusion 1.5 inpainting, CreativeML
OpenRAIL-M licence, pinned to one revision of RanaLLC/stable-diffusion-v1-5-inpainting-onnx-fp16; and
Real-ESRGAN x4plus, BSD-3-Clause, ONNX export by Qualcomm AI Hub) are downloaded the first time Generative is
picked and checked against fixed SHA-256 fingerprints.

Healing has two modes. Content-aware (the default) rebuilds the area from matching texture elsewhere in
the photo, with no extra files. AI (LaMa) redraws it with the LaMa inpainting model, which understands
structure better and is the best choice for removing large objects; it takes a few seconds per stroke. The
model is loaded in the background as soon as AI is picked (about 6 seconds on the processor, where the GPU check
puts it: DirectML cannot run its Fourier layers); a stroke made before it is ready waits for it rather than
silently using content-aware.

The first time AI (healing) or AI fill (Expand) is picked, PixTune offers to download two files (about 220 MB, once):
the LaMa model (`lama_fp32.onnx`, Carve/LaMa-ONNX on Hugging Face, Apache 2.0 licence) and the
ONNX Runtime engine (`onnxruntime.dll` from Microsoft's NuGet package, MIT licence). Both are checked
against fixed SHA-256 fingerprints before they are used, and stored in `%LOCALAPPDATA%\PixTune\ai`
(delete that folder to remove them). They are not part of the zip. Each stroke remembers the mode it was
painted with; on a computer without the AI files, AI strokes fall back to content-aware fill.

## Notes

- Results follow the original tools closely but are not pixel-identical: the mobile app's algorithms are
  not public, so every filter is an independent implementation.
- Faces are found with the face detector built into Windows; if one is missed, click on it to place it.
- Head Pose fits a 3D face mesh on each face: Google's MediaPipe Face Mesh (Apache 2.0, ONNX export by
  Qualcomm AI Hub, shipped in `native\` with ONNX Runtime, nothing to download) finds 468 points with depth.
  Turning and tilting rotate those points in 3D around a pivot inside the head, the smile raises and widens the
  mouth corners and lifts the cheeks, the focal length changes the perspective by depth, and the photo is
  re-rendered through the mesh triangle by triangle (nearer triangles in front, so the far side of a turned face
  folds away). Two rings of points around the face let hair, ears and neck follow while the background stays.
  Pupil size enlarges the irises inside the eye openings only. Fitting a face takes about a quarter of a second
  and is kept while the sliders move. Large turns of very large faces can stretch earrings or hair near the ears.
- Head Pose also has an AI mode (Mode > AI (LivePortrait)). Instead of bending the photo, the LivePortrait
  network (Kuaishou, MIT licence; ONNX export from warmshao/FasterLivePortrait pinned to one revision, about
  540 MB, downloaded the first time AI is picked and checked against fixed SHA-256 fingerprints) redraws the
  face from implicit 3D keypoints at the new angle or with a wider smile, ears, hair and jaw included. The new
  face is pasted back through LivePortrait's soft face mask, with its sharpness matched to the photo. Turns and
  smiles look more natural; each change takes about 5 to 15 seconds (the warping network runs on the
  processor). Pupil size and focal length still use the face mesh. 3D mesh stays the default.
- HEIC and WebP files open when the matching Windows image extensions are installed.
- Looks are shared as `.json` files (Looks > Import look) rather than QR codes.
- Settings and looks are stored in `%APPDATA%\PixTune`.

## Staying answerable while a photo is being worked on

Rendering has always run away from the window, on its own thread and cancellable, and the photo on screen is
drawn from the last picture made, so zooming and panning never wait for a render to finish. What did make the
window stutter was the render crowding it out, measured by posting work to the window thread over and over
during a render and timing the round trip -- a long trip is a moment where a pan would not have been drawn.

- **A core is kept free.** Every per-pixel loop in the tools goes through one place, which leaves one core for
  the window while a photo is being edited. An export takes every core, since only a progress bar is waiting.
  On a four-core computer, during a 3.6 second render: nine in ten round trips went from 38-42 ms to **8 ms**,
  the hitches above 50 ms from five or six to **one**, and the worst from about 365 ms to 139 ms. The render
  itself takes about 5% longer.
- **The memory collector keeps out of the way** while anything is rendering (and while a slider is dragged,
  which used to set this on its own), so it avoids its long blocking passes.
- **Less memory is churned.** A full-size render used to allocate about 3.4 GB, and filling the heap's
  large-object list is what forced the collection that froze every thread at once. Three things changed. The
  working planes of the filters (luminance, the blurs, the guided filter, a picture's three channels) are taken
  from a pool and handed back by the tool that asked for them, instead of being allocated and dropped -- at 22
  megapixels a plane is 86 MB and a tool may use five. A picture every pixel of which is about to be written
  (a resize, a crop, a merge of three channels) takes its memory the same way, without the clearing a fresh one
  carries. And each step of the stack hands its picture back for the next step to write into, once nothing else
  holds it. The pool is big enough to hold a full-size photo's planes while a render runs, and gives the memory
  back when nothing is being rendered, so the window's own bitmaps are never left short.

  What may be handed back is decided by asking whether the result was kept to be used again, not by its size:
  Memo registers everything it keeps, so a plane or a picture it holds is known exactly, and anything else was
  the tool's own. That is what makes this safe at preview sizes too, where most results are kept.

Together, on a 22 megapixel photo with four tools:

A picture of that size is 257 MB on its own, which is the floor: a tool cannot hand back less than the result it
was asked for. What each tool allocates to make one:

| Tool | Before | Now |
|---|---|---|
| Tune | 689 MB | **262 MB** |
| HDR Scape | 517 MB | **262 MB** |
| Dehaze | 621 MB | **280 MB** |
| Details | 539 MB | 327 MB |
| Tonal Contrast | 711 MB | 327 MB |
| Drama | 539 MB | 327 MB |
| Glamour Glow | 1 303 MB | 430 MB |
| Film | 993 MB | 469 MB |
| Lens Blur | 1 390 MB | 773 MB |
| Curves, Vignette, Vintage | 258 MB | 258 MB (already the floor) |

Over three edits in a row the whole program went from 6 286 MB allocated to 5 014 MB, and on four cores the
hitches above 50 ms went from thirteen to three, the worst from 332 ms to 151 ms, and nine in ten round trips
from 55 ms to 12 ms. What is left is one pause of about 150 ms per render, where the collector sweeps the heap,
and Lens Blur's two full-size pictures, which are inherent to how it stretches its blur back.
`tests\LatencyTest.ps1` measures all of it and fails if the window stops answering; it takes a core count, so
the four-core case can be checked on any machine. `tests\FeelTest.ps1` goes further and handles the photo the
way a person would -- zoom to 100%, six pans, down to 50%, six more, back to Fit -- while a full-resolution
render of a four-tool stack is running, and times each one to the moment it is on screen.

**The drawing itself.** With nothing being worked on, one of those gestures costs about 8 ms, and while a
render runs about 16 to 19 ms: the photo tracks the hand either way. Of the 8 ms, the painting is 3 ms --
0.4 clearing the canvas, 0.1 for the shadow under the photo, nothing at all for the shrunken copy (it is kept
and reused while the size holds), 2.6 drawing the photo, and nothing measurable for the mask and the overlay.
The remaining 5 ms is Windows copying the finished canvas to the screen. So a repaint is two passes over the
canvas, one of them not ours, and both are limited by how fast memory can be moved rather than by anything
being worked out. Putting the photo's pixels down without blending, and holding the shrunken copy without a
transparency channel, were both tried and measured: neither changed the time, so neither was kept. Going below
this would mean drawing through Direct3D instead of GDI+, which is a different program, not a tuning.

## Tests

- `tests\Test-Engine.ps1` runs every tool headless and writes a contact sheet.
- `PixTune.ps1 <photo> -SelfTest <folder> -TestScript Suite.ps1` runs the window tests one after the other in a
  single session (`<folder>\suite.log` sums them up; each test's log and screenshots are in its own folder).
  A single test: `-TestScript ZoomTest.ps1`. The tests drive the program through `tests\Compat.ps1`.
  Steps are run by `tests\Harness.ps1` (`St` to declare one, `Start-StRun` to play them): each step starts as
  soon as the window is idle (nothing rendering, no mask being computed, nothing queued) instead of after fixed
  pauses, and `<log>.times` lists every step's duration, slowest first. The whole suite takes one to one and a
  half minutes, most of it the first run of each AI model. Generative fill (Stable Diffusion) is left out unless
  asked, test runs do not start the GPU check by themselves, and `GpuCheckTest.ps1` checks only two small models.

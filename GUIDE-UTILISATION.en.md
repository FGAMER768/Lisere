# Liseré user guide

[Français](GUIDE-UTILISATION.md) | [English](GUIDE-UTILISATION.en.md)

This guide covers two levels:

1. **The application guide**: launching Liseré, understanding the window, navigating and completing a project from start to finish.
2. **The feature guide**: every page, button, setting and limit, with usage advice.

Liseré runs **entirely locally**: no image, video or setting is sent over the Internet. No account is required. Only the optional update check, started on your request, contacts GitHub.

---

## 1. What Liseré does

Liseré enhances an **image** or a **video**, then lets you compare the result with the original and export it.

| Workspace | What it allows |
|---|---|
| **Image** | Open a photo, apply a six-setting pipeline, estimate the parameters automatically, compare before/after and export as JPEG. |
| **Video** | Analyse a clip with FFmpeg, prepare a preview, render a one-second test, export the complete clip as MP4 with audio, or process several clips in a queue. |
| **Updates** | Check published GitHub versions, download the package, verify its digest and restart with the new version. |

Both workspaces share the same six main parameters and enhancement presets; Ultron grading is specific to the Image page. Video adds enhancement engines, resolution, frame rate, stabilisation, deinterlacing, RIFE interpolation, codec and the queue.

---

# Part I: Application guide

## 2. Prerequisites and launch

### 2.1 Prerequisites

- **Windows 10 or Windows 11**.
- **Installed build**: no .NET runtime to download. The application is self-contained; the installer only uses .NET Framework 4.8, included with Windows 10 and Windows 11.
- **From source**: **.NET SDK 10.0** or a version compatible with `net10.0-windows` to build and run.
- **Bundled FFmpeg** for video features: `ffmpeg.exe`, `ffprobe.exe`, the DLLs and a manifest are delivered in `ffmpeg/` and verified by SHA-256 at startup. The Image page works without FFmpeg.

To develop from a checkout without the binaries, run `scripts/fetch-ffmpeg.ps1`. Liseré then looks for FFmpeg in this order:

1. the `LISERE_FFMPEG` environment variable;
2. the verified `ffmpeg/manifest.json` package shipped with the application;
3. the application folder;
4. `C:\ffmpeg\bin`;
5. `C:\ffmpeg`;
6. `C:\Program Files\ffmpeg\bin`;
7. the Windows `PATH`.

If a manifest is present but a file is missing or modified, video features are disabled instead of running an unverified binary.

Three ONNX models are provided in `models/` and copied next to the executable: **Super-Resolution CNN x3**, **RIFE 4.9** and **YuNet** (face detection for Image page enhancement). The classic engine depends on no model. Licences and sources are detailed in section **12** and in [models/README.md](models/README.md).

### 2.2 Installing or running from source

#### With the installer

1. Download **Lisere-Setup.exe** from the [latest GitHub release](https://github.com/FGAMER768/Lisere/releases/latest).
2. Run the program. It does not ask for administrator rights and needs no additional .NET runtime: the application and FFmpeg are included.
3. Choose the installation folder or keep the default, `%LOCALAPPDATA%\Programs\Lisere`, then tick the optional Desktop shortcut. A Start menu shortcut is always created.
4. Click **Install**, then **Launch Liseré** when it finishes.

The installer is not code-signed yet: Windows may show a SmartScreen warning. The SHA-256 digest published with the release lets you verify the file.

To uninstall, use **Settings → Apps → Liseré** or `Lisere.exe --uninstall`. The application, its shortcuts and its uninstall registration are removed; images, presets and settings in `%APPDATA%\Lisere` are kept.

#### From source

From the repository root:

```powershell
dotnet restore Lisere.slnx
dotnet run --project src/Lisere
```

To build only:

```powershell
dotnet build Lisere.slnx
```

### 2.3 First launch

1. The window opens on the **Image** page.
2. The Video page reports whether the bundled FFmpeg package is verified; otherwise it shows why it is unavailable.
3. Choose the language with **FR** or **EN** in the title bar. The choice is stored in `%APPDATA%\Lisere\settings.json` and restored on the next launch.

---

## 3. Understanding the window

The window is organised into four areas:

| Area | Role |
|---|---|
| **Title bar** | Application name, **Updates** button, **FR** / **EN** buttons, window system buttons. |
| **Image / Video tabs** | Switch the workspace. The active tab is highlighted. |
| **Central preview** | Shows the image or video frame, with the before/after comparison. |
| **Right column** | Preset, settings, export options and, for video, engine, resolution and queue. |
| **Bottom status bar** | Latest status message: loading, processing, export, cancellation, error. |

### 3.1 Dialogs

- **Please wait**: blocks the page during a load, computation or image export. An **Cancel** button appears when the operation can be interrupted.
- **Render confirmation**: shown before a complete video export. It recalls the source, destination, settings and estimate. Rendering starts only after **Start render**.
- **Message**: error, warning or information. Setting warnings do not block processing; errors explain what failed.

### 3.2 Comparing before and after

The central preview is interactive:

- **Three modes** in the top-right corner: **Split** (divider), **Single** (output only) and **Side by side**. The Before/After labels are no longer shown in the preview footer.
- **Drag the divider** horizontally to reveal more or less of the original in Split mode.
- **Mouse wheel** over the preview to zoom.
- **+ / - buttons** to zoom in 25% steps.
- **`+` / `-` keys** when the preview has focus.
- **`0` key, 100% button or double-click** to return to fit-to-window.
- **Drag with the left button** when the image is zoomed to move it.
- With the divider focused: `Left` / `Right` move by 1%, `Page Up` / `Page Down` by 10%, `Home` / `End` go to the ends.

Zoom is **synchronised**: before and after zoom together. The point under the cursor stays in place as much as possible, which avoids losing the detail being inspected.

---

## 4. Complete workflows

### 4.1 Enhancing an image from start to finish

1. Click **Open** or press `Ctrl+O`.
2. Choose an image. The preview shows the **original**, not the result yet.
3. In the **Enhancement (AI)** menu, choose a **preset**:
   - **Chroma** for a balanced starting point;
   - **Auto** to let Liseré estimate the six parameters;
   - **Manual** to adjust them yourself;
   - **Auto + relative medium** to combine estimation and a moderate adjustment.
4. If you also want to grade the colours, click **Colour grading** and choose **Ultron CC**; **None** applies nothing. The two menus are independent.
5. Adjust the sliders or numeric fields if needed. Warnings appear when you leave the recommended ranges.
6. Click **Enhance image** or press `Ctrl+Enter`.
7. Compare with the divider, zoom and pan.
8. If the result is not right, change the settings and run the enhancement again.
9. Click **Export JPEG** or press `Ctrl+S`, then choose the destination file.

> Opening never triggers enhancement. Computing the result is always a deliberate action.

### 4.2 Exporting a video from start to finish

1. Open the **Video** tab, then click **Open** (`Ctrl+O`).
2. Liseré analyses the file and loads a source frame at the preview position (25% by default).
3. Choose the **engine**, **preset** and **output resolution**, then adjust the settings.
4. Set the **preview position** and click **Refresh preview** (`Ctrl+Enter`). Only the frame is computed; nothing is rendered yet.
5. To test the full pipeline on one second, use **Render 1 second**. The result is written to a temporary file and the middle frame is displayed.
6. To keep this test, use **Export preview (1 s)** and choose a file.
7. When the result suits you, click **Export video** (`Ctrl+S`).
8. Choose the destination, check the confirmation dialog, then start the render.
9. Follow the progress. The **Cancel render** button remains available throughout.
10. At the end, the MP4 file is written to the chosen destination.

### 4.3 Processing several videos

1. Prepare the first video and its settings.
2. Click **Add to queue** and choose the destination for the first export.
3. Open the next clip, configure it, then add it the same way.
4. Click **Start queue**.
5. Exports run **one at a time**. A failure on one clip does not remove the following ones: the job is marked failed and the queue continues.
6. **Stop** cancels the current job; the following jobs remain pending and can be restarted.
7. **Clear queue** removes pending jobs when nothing is running.

Each addition **freezes** the source, settings and destination. Changing sliders after adding does not modify the job already in the queue.

### 4.4 Cancelling processing

Cancellation is deliberately available in several places, depending on the operation:

| Operation | Where to cancel | Effect |
|---|---|---|
| Image load, enhancement or export | **Cancel** button in the **Please wait** window | Interrupts the operation; the status becomes **Operation cancelled.**; no error dialog is shown. |
| Video open or analysis | **Cancel** button at the top of the Video page, next to the processing indicator | Interrupts analysis or frame loading; status **Loading cancelled.** |
| Video preview refresh | Same **Cancel** button at the top of the Video page | Interrupts frame computation; status **Preview cancelled.** |
| Full render, preview render or preview export | **Cancel render** button below the export card | Stops the FFmpeg process and removes partial temporary files. The last produced frame stays visible. |
| Queue | **Stop** button in the **Render queue** card | Cancels the current job; following jobs remain pending. |

Cancellation is **cooperative**: processing stops at the next checkpoint. An FFmpeg render may take a moment to finish, but the process is indeed interrupted.

---

# Part II: Feature guide

## 5. Image page

### 5.1 Opening an image

The **Open** button offers the formats supported by Windows Imaging Component (WIC):

`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif`, `.tiff`, `.gif`, `.ico`, `.wdp`, `.jxr`, `.webp`, `.heic`, `.heif`, `.avif`, as well as **All files**.

Automatic processing on load:

- EXIF orientation applied;
- CMYK, 16-bit greyscale, indexed, progressive or transparent images converted to 8-bit RGB;
- transparency composited on a **white background**;
- animated or multipage images: only the **first image** is used;
- limit of **24 MB** per file and **40 million pixels** at decoding.

If a format is not recognised, install the corresponding Windows codec or convert the image to PNG/JPEG.

### 5.2 The two parameter menus

The right column of the Image page is divided into two menus, opened by the **Enhancement (AI)** and **Colour grading** buttons. They are independent: changing one does not modify the other.

#### Enhancement (AI)

The **Preset** menu offers four modes; the Video page offers the same four modes:

| Preset | Behaviour | When to use it |
|---|---|---|
| **Chroma** | Recommended reference values. | Starting point for any image or video. |
| **Auto (local estimation)** | Analyses the content and estimates the six parameters. For a video, several frames of the clip are sampled. | Unknown source, batch of images or quick setup. |
| **Manual (sliders)** | Applies exactly the entered values; `0` disables the corresponding effect. | You know what you want to adjust. |
| **Auto + relative medium** | Automatic estimation, then a moderate relative adjustment. | Auto result slightly too timid, without starting from scratch. |
| **No enhancement** | No correction: the six sliders and both additional settings are ignored. | Work only on colour grading, or compare grading with the original image. |

#### Colour grading

The **Colour preset** menu offers:

| Preset | Behaviour | When to use it |
|---|---|---|
| **None** | No grading: the image keeps the colours produced by enhancement. | Default. |
| **Ultron CC** | Grading of the After Effects preset `Ultron_CC.ffx`: saturation 129.997%, brightness +5, contrast +7, vibrance +50, plus sharpening. | Reproduce the preset's look without After Effects or plug-ins. |
| **Matrix CC** | Grading of the After Effects preset `Matrix_CC.ffx`: brightness +30, contrast +20 and three sharpening passes (30% / 15 px, 10%, 15% / 30 px). Fixed values, no sliders. | A brighter, more contrasted look with no adjustment. |
| **Infinity CC** | Grading of the After Effects preset `Infinity_CC.ffx`: multiscale sharpening, unsharp mask 16% / radius 9.8, soft halo, exposure -0.36. Fixed values. | A sharp, slightly darkened look with a subtle highlight halo. |
| **Nebulus CC** | Grading of the After Effects preset `Nebulus_CC.ffx`: multiscale sharpening, unsharp mask 10% / radius 20, pronounced halo. Fixed values. | A sharp, bright look with a strong halo around bright areas. |
| **Splendida CC** | Grading of the After Effects preset `Splendida_CC.ffx`: sharpness 50, unsharp mask 15% / radius 25, 25% vignette at 40 degrees, brightness +21 / contrast +10, then its master curve (128 to 118). Fixed values. | A sharp, brighter and more contrasted look with slight vignetting. |
| **Manual (sliders)** | Four sliders: saturation, brightness, contrast and vibrance, starting from neutral. | You want to grade by hand without a preset. |

The two menus combine in this order: enhancement first, colour grading second. Choosing **None** in the first menu applies only grading; choosing **None** in the second applies only enhancement. **Nebulus CC** has a pronounced halo: it suits dark or contrasted scenes, whereas on an already very bright image it strongly brightens the sky. **Infinity CC** is more subtle and slightly darkens the image.

Choosing **Ultron CC** initialises the four colour sliders to the preset values; they remain adjustable. **Manual (sliders)** starts neutral and enables all four sliders. **Matrix CC**, **Infinity CC**, **Nebulus CC** and **Splendida CC** apply their file values and leave the sliders disabled, with a note explaining this. Sharpening, specific to each preset, keeps the file values and is not exposed. Grading applies **after** enhancement: the six AI sliders and colour grading combine, and **None** leaves the image as the enhancement produced it.

Ultron CC is a documented reproduction: native After Effects effects are exact, third-party plug-ins (Magic Bullet Looks, Sapphire, Boris Continuum) are approximated from their saved parameters, and non-decodable proprietary tools in the Looks graph are not reproduced. The full analysis is in `docs/ULTRON-CC-ANALYSE.md`.

The note below the menu explains the active mode. Changing a slider while an automatic preset is active switches Liseré to **Manual**: this is deliberate, your values are not overwritten by a new estimation.

During **Auto** estimation, changing the preset or touching a slider cancels the previous estimation and starts the new configuration.

### 5.3 The six main settings

Sliders and numeric fields are synchronised. The value `0` disables the effect in Manual mode. Exact bounds are shown in the application; most range from `0` to `100`.

| Setting | What it does | Advice |
|---|---|---|
| **Compression repair** | Attenuates blocks and ringing in JPEG images or compressed videos. | Effective on heavily compressed sources; too high a value smooths texture. |
| **Noise reduction** | Removes grain and digital noise. | Increase on a dark or noisy scan; too high makes faces waxy. |
| **Sharpness** | Strengthens edges and micro-contrast. | Stay moderate: too much sharpness creates halos and a crunchy look. |
| **Detail recovery** | Restores fine details and textures. | Very useful on a soft source; an excessive value invents false texture. |
| **Halo reduction** | Attenuates halos around contrasted edges. | Around the recommended range; too high makes edges soft. |
| **Edge smoothing** | Softens stair-stepping and irregular edges. The range allows a negative value to compensate for slight blur. | `0` disables it. Use after strong enlargement or on an interlaced source. |

### 5.4 Additional settings

| Setting | What it does | Advice |
|---|---|---|
| **Original detail** | Reinjects part of the source detail into the result. | Avoids an overly smooth look; too high brings back the original noise. |
| **Enhance faces** | Detects faces with YuNet and strengthens their micro-texture; the rest of the image keeps its own. | Enabled by default. Without the YuNet model, Liseré falls back to the global skin mask without blocking the render. |
| **Add noise** | Adds fine grain to avoid a plastic look. | Very small values are enough. `0` disables it. |

Face enhancement runs during the detail recovery pass. It locates up to sixteen faces, concentrates texture on them and avoids adding it to the background or hair. The setting only runs when detail recovery is active; it does not replace noise reduction or sharpness.

### 5.5 Warnings

Liseré validates the settings and shows a warning banner when:

- detail recovery exceeds the recommended value;
- halo reduction leaves the ideal range;
- sharpness and detail recovery are both high;
- strong compression repair is combined with a lot of detail.

A warning does not prevent rendering. It signals a risk of noise, false texture or soft edges.

### 5.6 Enhancing the image

The **Enhance image** button (`Ctrl+Enter`) runs the pipeline on the loaded image. During computation:

- the page is blocked by the waiting window;
- the **Cancel** button interrupts the operation;
- any error is shown in a dialog and in the error banner.

The result stays in memory until the next load or enhancement.

As soon as a setting, preset or imported preset changes after a render, the result becomes **stale**: it stays displayed for comparison, a banner **Stale preview: run the enhancement again.** appears, and export is disabled. Run **Enhance image** again to get an exportable result.

### 5.7 Exporting as JPEG

The **Export JPEG** button (`Ctrl+S`) is active only after a successful enhancement and while the result is not stale. It opens the file picker, then writes a JPEG at **quality 92**. Export can be cancelled during writing; a newly created partial file is then removed.

---

## 6. Video page

### 6.1 Opening and analysing a video

The **Open** button accepts the formats offered by FFmpeg:

`.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, `.m4v`, `.mts`, `.m2ts`, `.wmv`, `.mpg`, `.mpeg`.

Analysis (`ffprobe`) displays in the header:

- the file name;
- the resolution;
- the duration;
- the frame rate;
- the video codec;
- audio presence and codec.

Opening also loads a **source frame** at the preview position, without computing the after view. Orientation carried only in rotation metadata is not applied during decoding.

### 6.2 The engine

The **Engine** menu offers the installed engines:

| Engine | Description | Availability |
|---|---|---|
| **Lisere Classic** | Deterministic local pipeline, no external model. | Always available. |
| **1x photo cleaning** | Colour network that removes noise and part of the JPEG artifacts without changing the size. The slowest engine: about one minute for a 900×900 photo. | Depends on the model being present and intact. |
| **Super-Resolution CNN x3** | Local ONNX model verified by SHA-256, DirectML inference on a compatible iGPU with CPU fallback. | Depends on the model being present and intact. |
| **Real-ESRGAN general x4** | Colour network: all three planes are rebuilt with the detail, on free-size tiles, then brought back to the chosen output resolution. Slower than a luma model. | Depends on the model being present and intact. |

The note below the menu indicates whether the engine is ready or why it is unavailable. If a neural engine is unavailable, choose **Classic**: the rest of the application keeps working.

The same choice is offered on the **Image** page, with **Lisere Classic** as the default. A neural engine replaces the whole pipeline for the frame, at its original size, and disables **Refine faces**: that texture belongs to the classical pipeline.

The estimate announced before a long render and the progress bar both follow the chosen engine: a neural model costs tens of seconds per megapixel, and the bar moves tile by tile instead of jumping at the end.

That duration sharpens with use: every render times its engine, and the result is remembered per model, fingerprint, provider and ONNX Runtime version in `%LOCALAPPDATA%\Lisere\engine-calibration.json`. The confirmation then shows the engine, the provider actually used (for example "1x photo cleaning · DirectML") and where the figure comes from: "Measured on this PC" or "Catalog seed", the latter refined by the first render here; on another graphics card the measurement from that machine is what counts, not the shipped reference figure.

A personal ONNX model dropped into `models/custom/` (see `models/README.md`) appears in the same list, marked "Local model, unchecked fingerprint".

### 6.3 Importing a video preset

The **Import preset...** button reads a JSON file and applies what Liseré can reproduce:

| Preset field | Use |
|---|---|
| `enhance.compress`, `denoise`, `sharpen`, `detail`, `dehalo`, `deblur` | The six main sliders, in the same order. |
| `enhance.recoverOriginalDetailValue` | Original detail. |
| `enhance.addNoise` | Added noise. |
| `enhance.auto` | Auto mode when no value is supplied; otherwise Manual mode with the file values. |
| `output.outSizeMethod` 6 / 7 / 9 | 1080p / 2160p / explicit height. |
| `output.outFPS` and `outputFps` | 30, 60 or 120 fps. |
| `stabilize.active` | Stabilisation. |
| `deinterlace.active` | Deinterlacing. |
| `grain.active` + `grainStrength` | Cautious approximation of added noise. |

The model named in the preset is shown for information only. **No proprietary weights are loaded**: the render uses the engine selected in Liseré. The preset transposes the settings and output geometry, not the original network. After import, the existing video preview is marked for refresh: run **Refresh preview** before judging the result.

### 6.4 Output resolution

The **Output resolution** menu offers:

- **Source (1x scale)**: keeps the input resolution;
- **2x** and **4x**: multiply the dimensions;
- **1080p** and **2160p**: target a height of 1080 or 2160 pixels; the width follows the source ratio and is rounded to an even number.

A 4K output is noticeably slower and larger. For a test, prefer a modest resolution and the one-second preview.

### 6.5 Position and preview

The **timeline** below the preview shows the duration and selects the position: click or drag the playhead, or use the arrow keys and `Home` / `End` when the timeline has focus. The position stays limited to 95% so the last frame remains decodable.

The image follows the playhead: the source frame is reloaded at every new position and, as soon as a preview exists, the after image is recomputed there. A request still running is cancelled by the next one, so only the last stop counts.

The five buttons above the timeline are the transport: **start**, **previous frame**, **play/pause** (`Space`), **next frame** (`Ctrl` with the arrows) and **end**. Playback advances one frame at a time, at the rate the file decodes, and stops at the end of the clip; the after image follows playback as fast as the engine allows, then refreshes completely when you pause.

- **Refresh preview** (`Ctrl+Enter`) extracts the frame at the chosen position and applies the current settings.
- **Render 1 second** runs the real pipeline on a segment around the position, then displays the middle frame. The file is temporary and silent.
- **Export preview (1 s)** writes that segment to a chosen file, without audio, for inspection or keeping.

After a setting change, the status indicates that the parameters have changed: refresh the preview before judging the result.

### 6.6 The six settings and additional settings

The Video page uses exactly the same six parameters and two additional settings as the Image page. The table in section **5.3** and the advice in section **5.4** apply the same way, frame by frame.

In **Auto** mode, video samples several positions in the clip to estimate the parameters, instead of relying only on the displayed frame.

### 6.7 Processing and output

| Option | What it does | Impact |
|---|---|---|
| **Stabilisation** | Analyses motion in two passes (`vidstab`). | Reduces shake; significantly lengthens rendering. |
| **Deinterlacing** | Applies `bwdif` before enhancement. | For interlaced sources; lengthens rendering. |
| **Output frame rate** | Source, 30, 60 or 120 fps. A different rate recreates frames with motion compensation (`minterpolate`). | Very CPU-intensive. |
| **RIFE x2 interpolation** | Doubles the frame rate with the RIFE 4.9 model (ONNX, DirectML or CPU). | The frame-rate list is disabled: RIFE produces exactly two frames per source frame. Slow in HD. |
| **Codec** | H.264 or H.265 (HEVC). | H.265 compresses better but is less universally readable. |
| **Quality (CRF)** | From 14 to 32; a lower value gives better quality and a larger file. | The default is 20. |

Stabilisation, deinterlacing and interpolation are additional passes: they add to the frame-by-frame enhancement time.

### 6.8 Pre-render estimate

The estimate shows the frame count and an approximate duration, recalculated from:

- the engine;
- the output resolution;
- the frame rate;
- RIFE;
- stabilisation and deinterlacing.

It is **indicative**: per-frame cost is a model, not a measurement of your clip. During rendering, however, the remaining time is computed from actual progress.

### 6.9 Exporting the video

1. Click **Export video** (`Ctrl+S`).
2. Choose the destination MP4 file.
3. Read the confirmation dialog: source, destination, settings, estimate.
4. Click **Start render** to begin, or **Cancel** to return without starting anything.

During rendering:

- a progress bar, the real percentage and a reassuring message, without naming stages or tiles;
- the latest produced frame replaces the preview and stays visible after cancellation;
- the **Cancel render** button stops the process.

The output is an **MP4** with the chosen codec. The original audio track is preserved as **AAC 192 kbit/s** when present. Preview exports are silent.

### 6.10 The queue

The queue lets you prepare several exports and start them at once.

| Button | Action |
|---|---|
| **Add to queue** | Freezes the video, settings and chosen destination. A duplicate destination is refused. |
| **Start queue** | Runs pending, failed or cancelled jobs one by one. |
| **Stop** | Cancels the current job; following jobs remain pending. |
| **Clear queue** | Removes jobs when the queue is inactive. |

The queue is **sequential**: one render at a time. A failed job is marked and the queue continues with the following ones. Each job has frozen settings, so you can prepare several variants without mixing them.

---

## 7. Common features

### 7.1 Language

The **FR** / **EN** buttons in the title bar immediately change all labels, without restarting. The choice is saved in `%APPDATA%\Lisere\settings.json`.

### 7.2 Keyboard shortcuts

| Shortcut | Image page | Video page |
|---|---|---|
| `Ctrl+O` | Open an image | Open a video |
| `Ctrl+Enter` | Enhance the image | Refresh the preview |
| `Ctrl+S` | Export as JPEG | Export the video |

Preview shortcuts, when the preview has focus:

| Key | Action |
|---|---|
| Mouse wheel | Zoom in / out |
| `+` or `-` | Zoom in / out in 25% steps |
| `0` | Fit to window |
| Double-click | Fit to window |
| `Left` / `Right` on the divider | Move the comparison by 1% |
| `Page Up` / `Page Down` | Move by 10% |
| `Home` / `End` | Comparison at 0% / 100% |

### 7.3 Status bar and messages

The status bar shows the latest event: image loaded, processing, export succeeded, operation cancelled, error. Detailed errors also appear in a dialog with a localised, explicit message; raw technical messages and internal paths are not shown.

Messages are translated into both languages. Source texts are in `src/Lisere/Lang/Strings.resx` (French) and `Strings.en.resx` (English).

### 7.4 Privacy and offline operation

- No file upload, no telemetry, no account.
- ONNX models are local and verified by SHA-256.
- FFmpeg is called locally and its bundled package is verified by SHA-256.
- The application does not need the Internet to work; only the voluntary update check contacts GitHub.

### 7.5 Updates

The **Updates** button in the title bar opens a dedicated window:

1. **Check** reads `Update/version.txt` on GitHub and compares the published version with the application version.
2. If a newer version exists, **Install** downloads `Lisere.zip` from the latest release, verifies its SHA-256 digest and checks every archive path.
3. Liseré closes, a script waits for the process to exit, copies the files and restarts the application. Your settings in `%APPDATA%\Lisere` remain untouched.

The official installer places the application in a writable per-user folder, which is what allows updating without UAC. If you copy Liseré to `C:\Program Files` or another protected folder, checking may work but installation will stop with a message explaining that the folder is not writable.

A network error, a different digest or a non-writable installation folder stops the operation with a localised message; no application file is replaced before the full package is validated.

---

## 8. Formats, limits and performance

### 8.1 Image

- Input: WIC formats listed in **5.1**, depending on installed codecs.
- Maximum file size: **24 MB**.
- Decoding limit: **40 million pixels**.
- Orientation metadata in a PNG eXIf chunk is read within a **64 KB** limit; an oversized or truncated chunk is ignored and the image remains usable.
- Output: **JPEG quality 92**.
- An animated or multipage image uses only its first image.
- Transparency is flattened on white.

### 8.2 Video

- Input: formats supported by FFmpeg (list in **6.1**).
- Output: **MP4**, H.264 or H.265, AAC 192 kbit/s audio if present.
- Audio is not kept in one-second previews.
- Rotation carried in metadata is not applied during decoding.
- The queue is sequential, with no parallelism.
- **RIFE is limited to x2** and imposes the doubled frame rate.
- Temporary preview files go to `%TEMP%\Lisere\previews` and are purged after 24 h.
- The temporary render folder is removed at the end; cancellation also removes partial files.

### 8.3 What is not integrated

- **BasicVSR++**: no verifiable ONNX is distributed; adding it would require a validated PyTorch/CUDA export.
- **Face restoration** (GFPGAN, CodeFormer): heavy models, licences and a detection/alignment/compositing pipeline are not integrated.
- **Image batch processing**: the Image page processes one image at a time. The queue is only for video.
- **Cloud, accounts, synchronisation**: out of scope.

---

## 9. Troubleshooting

### "The video engine is unavailable"

FFmpeg or `ffprobe` cannot be found, or the bundled package was modified. Reinstall Liseré or run `scripts/fetch-ffmpeg.ps1`; during development, also check `LISERE_FFMPEG`, `C:\ffmpeg\bin`, `C:\ffmpeg`, `C:\Program Files\ffmpeg\bin` and `PATH`. The Image page remains usable without FFmpeg.

### "Model unavailable" or a greyed-out neural engine

The ONNX file is missing, modified or invalid. Restore the model from `models/` and check its hash. In the meantime, select **Lisere Classic**.

### An image does not open

The WIC codec may not be installed, the file may exceed 24 MB, or the image may exceed 40 million pixels. Convert it to PNG/JPEG or reduce its size.

### Face enhancement does nothing

Check that **Enhance faces** is enabled and that detail recovery is not `0`. The `face_detection_yunet_2023mar.onnx` model must be present next to the executable; if it is missing or invalid, Liseré silently falls back to the global skin mask. Face enhancement is deliberately subtle: it does not replace enlarging a face.

### Video rendering is very slow

Main factors: neural engine, 4K output, RIFE, stabilisation, deinterlacing and recreated frame rate. To iterate, use **Render 1 second**, a lower resolution and the **Classic** engine. The CPU fallback of ONNX engines is much slower than DirectML.

### The video preview does not change

After changing a setting, click **Refresh preview** (`Ctrl+Enter`). Opening only computes the source frame.

### I want to stop a computation

- Image: **Cancel** in the waiting window.
- Video open/preview: **Cancel** at the top of the page.
- Video render: **Cancel render** below the export card.
- Queue: **Stop** in the queue card.

### The exported video has no audio

Check that the source has an audio track. One-second preview exports are deliberately silent; only the full export keeps audio.

### The queue stopped

A failed job is marked **Failed** but does not clear the queue. **Stop** cancels the current job and leaves the following ones pending. Fix the cause, then start the queue again: pending, failed or cancelled jobs are resumed.

### Where are my preferences?

In `%APPDATA%\Lisere\settings.json`. The language choice is stored there.

---

## 10. Best practices

- **Old or compressed image**: start with **Chroma**, slowly raise compression repair and noise reduction, then sharpness. Watch the warnings.
- **Portrait photo**: be careful with noise reduction and sharpness; leave **Enhance faces** enabled and add a very small amount of noise to avoid a plastic look.
- **Scan or document**: favour sharpness and edge smoothing, with moderate noise reduction.
- **HD video**: first make a **one-second preview** with the final resolution and engine. It is the best way to estimate the look and the time before a full export.
- **Several clips**: prepare each job in the queue, then start the queue and let it run. Each job is independent.
- **Testing settings**: keep a copy of the original and export to a new file; Liseré never modifies the source.

---

## 11. Command summary

| Command | Effect |
|---|---|
| `dotnet restore Lisere.slnx` | Restores dependencies. |
| `dotnet build Lisere.slnx` | Builds the solution. |
| `dotnet run --project src/Lisere` | Launches the application. |
| `dotnet test Lisere.slnx` | Runs the tests. |

---

## 12. Credits and licences

- **YuNet** (face detection): Shiqi Yu / OpenCV Zoo, MIT licence. Reference: Wu, Peng and Yu, 2023.
- **RIFE 4.9**: Practical-RIFE, MIT licence (Megvii and hzwer); ONNX export edgetools/rife.
- **Sub-pixel SRCNN x3**: ONNX Model Zoo, Apache-2.0 licence; Shi et al., 2016.
- **ONNX Runtime / DirectML**: Microsoft, MIT licence.
- **WPF-UI**, **CommunityToolkit.Mvvm** and **Microsoft.Extensions.Hosting**: MIT licences.
- **xUnit.net** and **Microsoft.NET.Test.Sdk**: Apache-2.0 and MIT.
- **FFmpeg**: BtbN build `N-129046-g3a78197c4c`, GNU GPL v3; bundled package verified by SHA-256. Provenance and licence in `ffmpeg/README.md` and `ffmpeg/LICENSE.txt`.
- The After Effects presets mentioned belong to their owners; no `.ffx` file is distributed. Topaz and Adobe references are trademarks of their owners; no proprietary weights, code or media are included.
- Test images and their sources are documented in `tests/Lisere.App.Tests/TestData/README.md`. The full list is in the **Credits, sources and licences** section of the [English README](README.en.md#credits-sources-and-licences).

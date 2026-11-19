# Liseré

[Français](README.md) | [English](README.en.md)

Liseré is a Windows desktop application for enhancing **an image or a video**, comparing the result with the original and exporting the retouched version. All processing is local: no external service and no account.

The interface has two workspaces:

- **Image**: deterministic six-parameter pipeline, presets, automatic estimation, a separate colour-grading menu and JPEG export.
- **Video**: FFmpeg analysis, before/after preview, frame-by-frame rendering, interchangeable engines (local pipeline or ONNX models), FHD/4K resolutions, 30/60/120 fps, RIFE x2 neural interpolation, deinterlacing, stabilisation, a queue and MP4 export with audio.

## Features

### Image

- Opens the image formats supported by Windows Imaging Component, with normalisation of irregular cases.
- Before/after comparison with a divider that moves across the full image height and a position slider.
- Opening shows the original: the enhancement is only computed when **Enhance image** is clicked.
- Two parameter menus, **Enhancement (AI)** and **Colour grading**, each with its own preset and sliders.
- Enhancement presets: Chroma, Auto, Manual and Auto + relative medium.
- Colour presets **Ultron CC**, **Matrix CC**, **Infinity CC**, **Nebulus CC** and **Splendida CC**, reproductions of After Effects presets applied after enhancement (`docs/ULTRON-CC-ANALYSE.md` and `docs/PRESETS-ANALYSE.md`).
- Settings for compression repair, noise reduction, sharpness, detail recovery, halo reduction and edge smoothing.
- Additional settings for original detail and added noise.
- **Face enhancement**: YuNet (MIT) locates faces and concentrates micro-texture on their skin without adding grain to the background or hair. If the model is missing, Liseré falls back to the global skin mask without blocking the render.
- **Stale preview**: after a setting change or a preset import, the existing preview stays visible but gets a banner, and export is disabled until the next render.
- Automatic parameter estimation from the image content.
- Warnings when settings leave the recommended ranges.
- JPEG export at quality 92.
- Confirmation with a duration estimate before a long enhancement (above about 4 s estimated, measured at about 2.4 s per megapixel).
- Cancellation of loads, processing and exports from the waiting dialog.

### Video

- File analysis with `ffprobe`: resolution, frame rate, duration, codec, audio presence and codec.
- Before/after preview on a frame extracted at the chosen position; opening does not compute the preview, it must be requested explicitly (`Ctrl+Enter`).
- Applies the same six parameters to each frame, with Auto estimation sampled across the clip.
- Interchangeable engines:
  - **Lisere Classic**: deterministic local pipeline, always available;
  - **Super-Resolution CNN x3**: local ONNX model, SHA-256 verified, DirectML inference on the iGPU with CPU fallback.
- Output resolution: source, 2x, 4x, 1080p or 2160p; the FHD/4K width follows the source ratio and is rounded to an even value.
- Output frame rate: source, 30, 60 or 120 fps (`minterpolate`, only when the rate differs).
- **RIFE x2 neural interpolation**: RIFE 4.9 ONNX model (MIT), two frames and a timestep, executed on DirectML (Intel iGPU) with CPU fallback. The frame rate is doubled; this is deliberately limited to x2.
- Deinterlacing (`bwdif`) before enhancement for interlaced sources.
- Two-pass stabilisation (`vidstab`).
- **Video preset import (JSON)**: reads a preset JSON (six parameters, detail recovery, noise, FHD/4K size, frame rate, stabilisation, deinterlacing) and applies it to Liseré. No proprietary weights are read or used.
- **Multi-clip queue**: each job freezes its source and settings, exports run sequentially, one failure does not stop the next jobs, and Stop cancels the current job.
- MP4 H.264 or H.265 export, adjustable CRF quality, original audio track preserved (AAC 192 kbit/s).
- Step-by-step progress and real cancellation of FFmpeg processes.
- **Pre-render estimate**: frame count and approximate duration recalculated from the engine, resolution, frame rate, RIFE and optional passes. It appears in the settings column and on the export card.
- **Pre-render confirmation**: a dialog shows the source and current output, settings, destination and estimate; nothing starts without confirmation.
- **One-second preview render**: renders the real segment around the chosen position with the current settings, then shows the resulting frame.
- **Preview export**: the same one-second segment can be written to a chosen file, without audio, for inspection or keeping.
- **Live frame**: during a render, the latest produced frame replaces the preview and stays visible after cancellation.
- **Synchronised zoom**: wheel, buttons or `+` / `-` / `0` keys on the comparison; before and after zoom together, the point under the cursor stays in place (the centre stays centred), and dragging moves the enlarged image.

### Common

- French and English interface, switched immediately from the title bar and remembered.
- Asynchronous processing, waiting dialogs and localised error messages, without raw technical details.
- Keyboard shortcuts: `Ctrl+O` open, `Ctrl+Enter` start the preview or enhancement, `Ctrl+S` export.
- Cancellation available for image operations, video analysis and preview, renders and the queue.

## Prerequisites

- Windows 10 or Windows 11.
- [.NET SDK 10.0](https://dotnet.microsoft.com/download) or a later version compatible with `net10.0-windows`.
- **FFmpeg** with `ffmpeg.exe` and `ffprobe.exe` for video features. Liseré does not redistribute it. It looks in this order: the `LISERE_FFMPEG` variable, the application folder, `C:\ffmpeg\bin`, `C:\ffmpeg`, `C:\Program Files\ffmpeg\bin`, then `PATH`.
- No database, no account and no network connection are required at runtime.

Three ONNX models are included in `models/` and copied next to the executable: SRCNN x3 (240 KB, Apache-2.0), RIFE 4.9 (20.5 MB, MIT) and YuNet (227 KB, MIT). See [models/README.md](models/README.md) and [Credits, sources and licences](#credits-sources-and-licences). DirectML is used automatically for ONNX engines when the iGPU supports it; otherwise Liseré falls back to the CPU and reports it in the engine note.

## Getting started

From the repository root:

```powershell
dotnet restore Lisere.slnx
dotnet run --project src/Lisere
```

To build without running:

```powershell
dotnet build Lisere.slnx
```

## Usage

### Image

1. Click **Open** or press `Ctrl+O` to load an image.
2. Choose an enhancement preset or adjust the sliders and numeric fields; the **Colour grading** button opens the second menu, with its preset and four sliders.
3. Click **Enhance image** or press `Ctrl+Enter`: the render never starts on load.
4. Drag the preview divider to compare before and after.
5. Click **Export JPEG** or press `Ctrl+S`.

### Video

1. Open the **Video** tab, then click **Open** (`Ctrl+O`).
2. Liseré analyses the file and shows the source frame at the chosen position without computing the after view.
3. Choose the engine, preset and resolution; adjust the settings, frame rate, deinterlacing and stabilisation.
4. **Refresh preview** (`Ctrl+Enter`) computes the comparison image; nothing is rendered before this action.
5. **Export video** (`Ctrl+S`) asks for the destination, then renders the file with progress and cancellation.
6. For multiple clips, use **Add to queue**, then **Start queue**: each addition freezes the current settings and asks for its destination.
7. **Export preview (1 s)** writes the comparison segment to a chosen file, without audio.
8. To double the frame rate with AI, enable **RIFE x2 interpolation**. The frame-rate list is disabled: RIFE produces exactly two frames per source frame.

Stabilisation, deinterlacing and FFmpeg interpolation are additional passes; RIFE runs inside the render loop. All of them noticeably lengthen the render, especially in HD.

### Presets

| Preset | Behaviour |
|---|---|
| Chroma | Reference preset with the recommended starting values; the detail stage reconstructs the 1, 2.5 and 5 px scales, bounded to the local extrema of the unsharpened image at +/-6 levels, with a gain adapted to the acutance already present. The sharpness and antialias unsharp masks are bounded the same way. The preset finally applies the tone curve measured on the Topaz reference (Base.png to UltronCCReel.png), which lifts the midtones without clipping the highlights. The Video page applies the same curve after the engine; Ultron grading remains specific to the Image page, so only the curve is shared between both workspaces. |
| Auto (local estimation) | Analyses the image or several frames of the clip and estimates the parameters automatically. |
| Manual (sliders) | Applies the entered values directly. |
| Auto + relative medium | Combines automatic estimation and a moderate relative adjustment. |
| No enhancement | No correction: only the colour preset and export apply. |

### Colour grading

The **Colour grading** menu on the Image page is independent of the enhancement presets; grading applies after enhancement.

| Preset | Behaviour |
|---|---|
| None | No grading: the image keeps the colours produced by the enhancement. |
| Ultron CC | Grading of the After Effects preset `Ultron_CC.ffx`: saturation 129.997%, brightness +5, contrast +7, vibrance +50, plus the sharpening. The four sliders start at the preset values and remain adjustable; sharpening is fixed. |
| Matrix CC | Grading of the After Effects preset `Matrix_CC.ffx`: brightness +30, contrast +20 and three sharpening passes (30% / 15 px, 10%, 15% / 30 px). Fixed values, no sliders. |
| Infinity CC | Grading of the After Effects preset `Infinity_CC.ffx`: multiscale sharpening, unsharp mask 16% / radius 9.8, soft halo, then exposure -0.36. Fixed values. |
| Nebulus CC | Grading of the After Effects preset `Nebulus_CC.ffx`: multiscale sharpening, unsharp mask 10% / radius 20 and a pronounced halo. Fixed values. |
| Splendida CC | Grading of the After Effects preset `Splendida_CC.ffx`: sharpness 50, unsharp mask 15% / radius 25, 25% vignette at 40 degrees, brightness +21 / contrast +10, then its master curve read from the file (128 to 118). Fixed values. |

The main parameters are bounded from `0` to `100`, except edge smoothing, which accepts a negative value to compensate for blur. The value `0` disables the corresponding effect in Manual mode. The **No enhancement** preset greys out the sliders: the image passes through the pipeline without correction, which allows applying only colour grading.

### Full preset (AI + colour grading)

The **Full preset** card on the Image page imports and exports a single JSON file containing both configurations: the six enhancement sliders with Original detail and Added noise, then colour grading with its four settings. The saved name comes from the file chosen at export.

```json
{
  "format": "lisere-preset",
  "version": 1,
  "name": "Portrait",
  "enhancement": {
    "mode": "Manual",
    "parameters": {
      "fixCompression": 60,
      "reduceNoise": 40,
      "sharpen": 10,
      "recoverDetails": 85,
      "dehalo": 10,
      "antialiasDeblur": 95
    },
    "recoverOriginalDetail": 20,
    "addNoise": 0
  },
  "color": {
    "preset": "UltronCc",
    "settings": {
      "saturationPercent": 129.997,
      "brightness": 5,
      "contrast": 7,
      "vibrance": 50
    }
  }
}
```

Import is normalised: out-of-range values are clamped to the slider ranges, and a dynamic mode (`Auto`, `AutoRelativeMedium`) becomes **Manual** with the saved values, so a preset always reproduces the same result. `Chroma` is kept when the six values match its preset. Colour presets other than Ultron ignore the four settings. Unknown fields are ignored; an unrecognised format or version is refused with a localised message. If a result already exists, importing marks it as stale: the **Stale preview** banner appears and export stays disabled until the next render.

### Imported video presets

The **Import preset...** button reads a JSON preset exported by a video enhancement application and applies what Liseré can reproduce:

| Preset field | Use in Liseré |
|---|---|
| `enhance.compress`, `denoise`, `sharpen`, `detail`, `dehalo`, `deblur` | The six sliders, in the same order. |
| `enhance.recoverOriginalDetailValue` | Original detail. |
| `enhance.addNoise` | Added noise. |
| `enhance.auto` | Auto mode when no value is supplied, otherwise Manual mode with the preset values. |
| `output.outSizeMethod` 6 / 7 / 9 | 1080p / 2160p / explicit height. |
| `output.outFPS` and `outputFps` | 30, 60 or 120 fps. |
| `stabilize.active` | Stabilisation. |
| `deinterlace.active` | Deinterlacing. |
| `grain.active` + `grainStrength` | Cautious approximation of added noise. |

The named model is shown for information only. **No proprietary weights are loaded**: the render uses the engine selected in Liseré (Classic or ONNX). The preset therefore reproduces the control surface and output geometry, not the original network.

### Shortcuts

| Shortcut | Image | Video |
|---|---|---|
| `Ctrl+O` | Open an image | Open a video |
| `Ctrl+Enter` | Start enhancement | Refresh preview |
| `Ctrl+S` | Export JPEG | Export video |

### Language

The **FR** and **EN** buttons in the title bar change the interface language without restarting. The choice is saved in `%APPDATA%\Lisere\settings.json` and restored on the next launch.

Texts are grouped in `src/Lisere/Lang/Strings.resx` (French) and `src/Lisere/Lang/Strings.en.resx` (English). `ILocalizationService` resolves keys and notifies the views on every language change.

## Formats and limits

### Image

- Offered input formats: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif`, `.tiff`, `.gif`, `.ico`, `.wdp`, `.jxr`, `.webp`, `.heic`, `.heif` and `.avif`. Codecs installed in Windows determine which formats are actually decodable; the file picker also offers "All files".
- Irregular formats: CMYK, 16-bit greyscale, indexed, progressive or transparent images are converted to 8-bit RGB before processing.
- EXIF orientation is applied on load. Transparency is composited on white.
- For animated or multipage images, only the first image is used.
- Maximum file size: 24 MB. Decoding limit: 40 million pixels.
- Orientation metadata in a PNG eXIf chunk is read within a 64 KB limit; an oversized or truncated chunk is ignored and the image remains usable.
- Export format: JPEG, quality 92.

### Video

- Offered inputs: `.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, `.m4v`, `.mts`, `.m2ts`, `.wmv`, `.mpg`, `.mpeg` (depending on FFmpeg codecs).
- MP4 output only, H.264 or H.265, AAC audio.
- Video metadata rotation is not applied during decoding.
- The queue is **sequential**: one render at a time, no parallel execution. A failed job is marked and the queue continues; Stop cancels the current job.
- **RIFE is limited to x2** and does not accept an arbitrary frame rate. On this machine's Intel Iris Xe, one 1080p inference costs about **0.83 s** after warm-up (0.037 s at 192 x 192): a long HD clip remains slow. CPU fallback is much slower.
- **BasicVSR++ is not integrated**: the official repository only publishes PyTorch weights; its `pytorch2onnx.py` script goes through mmcv and DCN alignment depends on CUDA. No verifiable pre-exported ONNX is distributed. Adding it would require a verified PyTorch/CUDA export, not a simple file load.
- **Face restoration is not integrated**: GFPGAN 1.4 weighs 340 MB (Apache-2.0 with third-party components), CodeFormer 377 MB (non-commercial NTU S-Lab licence). Both require a detector, 512 x 512 alignment and compositing; this is not a standalone frame-by-frame engine.
- Preset import loads no proprietary weights and does not reproduce the original models: only the settings, size and frame rate are transposed.
- `outSizeMethod` 6 to 1080p and 7 to 2160p are **inferred** from the installed preset corpus; other methods fall back to the source geometry.
- Preset grain (size, sigma, type) is approximated by a single noise amount.
- Auto mode samples three positions in the clip; it does not replace a dedicated neural estimator.
- **The estimate is indicative**: per-frame cost is a model anchored on the measured 1080p RIFE cost, not a measurement of the clip; the remaining time shown during rendering is computed from actual progress.
- **The one-second preview** writes a temporary MP4 in `%TEMP%\Lisere\previews`, purged after 24 h; preview export goes to the chosen file. Both keep the settings but not the audio.
- Neural engines run on **DirectML** when a DirectX 12 adapter is available (the Intel iGPU on this machine), with a **CPU fallback** whose reason is shown in the engine note. CPU fallback is much slower: neural upscaling of a long HD video remains slow in both cases.
- The temporary render folder is removed at the end; cancellation also removes partial files.

## Architecture

| Project | Role |
|---|---|
| `src/Lisere` | WPF application, navigation, Image and Video pages, view models, dialog, file and localisation services. |
| `src/Lisere.Core` | Image models, filters, automatic estimation, validation, enhancement pipeline, face detection (`FaceBox`), clip processing. |
| `src/Lisere.Ai` | Engine abstraction (`IFrameEnhancer`, `IFrameInterpolator`), classic engine, tiled ONNX super-resolution, RIFE interpolation, YuNet face detection, DirectML/CPU sessions, catalogue and SHA-256 verification. |
| `src/Lisere.Video` | FFmpeg discovery, `ffprobe`, frame extraction, video rendering, deinterlacing, stabilisation, interpolation, video preset import, progress and cancellation. |
| `tests/Lisere.Core.Tests` | Pipeline, filters, parameters, estimation and parity with the reference Python implementation. |
| `tests/Lisere.Ai.Tests` | Catalogue, tile planning, real ONNX inference, engine availability. |
| `tests/Lisere.Video.Tests` | ffprobe parser, FFmpeg command construction, end-to-end render with a generated clip. |
| `tests/Lisere.App.Tests` | Image and Video view models, dialogs, image codec, localisation, face detection and error messages. |

The Image page remains organised around `EnhanceViewModel` and `IEnhancementOrchestrator`. The Video page uses `VideoViewModel`, split into partial files by function (queue, preview, render); it delegates analysis to `IVideoProbe`, thumbnails to `VideoPreviewRenderer`, confirmation-plan construction to `VideoRenderPlanFactory` and rendering to `IVideoRenderService`. Engine selection goes through `IFrameEnhancer`: the interface and the video pipeline are completely unaware of which model is used. The queue is carried by `VideoQueueItem`, which freezes a complete render request; `VideoQueueRunner` then calls `VideoRenderService` sequentially, without stopping the queue when one clip fails.

The `models/catalog.json` catalogue describes each model (file, scale, input/output, licence, hash). A missing or modified model is reported as unavailable, with the reason, and cannot be used.

## Credits, sources and licences

Liseré does not redistribute any proprietary weights, code or media. The third-party resources used are credited below; the full model licences are provided in `models/`.

### Models and engines

- **YuNet**, face detection: model by Shiqi Yu, distributed by [OpenCV Zoo](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet), MIT licence. Reference: Wu, W., Peng, H., Yu, S. (2023), *YuNet: A Tiny Millisecond-level Face Detector*, Machine Intelligence Research, 20(5), 656-665. Licence: `models/yunet/LICENSE`.
- **RIFE 4.9** / Practical-RIFE: code and weights under the MIT licence (Copyright (c) Megvii Inc. and hzwer). ONNX export used: [edgetools/rife](https://huggingface.co/edgetools/rife). Licence: `models/rife/LICENSE`.
- **Sub-pixel SRCNN x3**: model by Shi et al. (2016), distributed by the [ONNX Model Zoo](https://github.com/onnx/models/tree/main/validated/vision/super_resolution/sub_pixel_cnn_2016), Apache-2.0 licence. Licence: `models/onnx/LICENSE`.
- **ONNX Runtime and DirectML**: Microsoft, MIT licence. Used for local inference of the ONNX models.
- **FFmpeg**: [ffmpeg.org](https://ffmpeg.org), under the LGPL or GPL depending on the build. Liseré does not redistribute it; it must be installed separately.

### Libraries and tests

- **WPF-UI** (lepo.co), MIT licence, for the interface controls and theme.
- **CommunityToolkit.Mvvm** (.NET Foundation), MIT licence, for the view models.
- **Microsoft.Extensions.Hosting**, MIT licence.
- **xUnit.net** and **Microsoft.NET.Test.Sdk**, Apache-2.0 and MIT licences, for the tests.

### Analysis references and trademarks

- The **Ultron CC**, **Matrix CC**, **Infinity CC**, **Nebulus CC** and **Splendida CC** presets refer to analysed After Effects presets; Adobe After Effects and those names belong to their respective owners. No `.ffx` file is distributed.
- The Chroma preset tone curve was measured on a reference image rendered with Topaz; Topaz is a trademark of Topaz Labs. No Topaz weights, code or media are distributed.
- The captures and analyses in `docs/` are local working documents and are not part of the distributed package.

### Test images

- `tests/Lisere.App.Tests/TestData/face.jpg`: official portrait of President Barack Obama, photographed by Pete Souza for the Obama-Biden transition (2009), [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), attribution Pete Souza. Source: [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Official_portrait_of_Barack_Obama.jpg), cropped and resized for the tests. See `tests/Lisere.App.Tests/TestData/README.md`.
- `tests/Lisere.App.Tests/TestData/preset-photo.jpg`: Blue Lagoon photo by Jeff Sheldon, distributed by Lorem Picsum (ID 512, Unsplash licence), used for colour regression testing.

### Documentation

- [README.md](README.md): French README.
- [GUIDE-UTILISATION.md](GUIDE-UTILISATION.md): complete French user guide.
- [GUIDE-UTILISATION.en.md](GUIDE-UTILISATION.en.md): complete English user guide.
- [models/README.md](models/README.md): model provenance, licence and verification.
- `tests/Lisere.App.Tests/TestData/README.md`: test image provenance.

## Tests

```powershell
dotnet test Lisere.slnx
```

The solution contains four xUnit test projects: `Lisere.Core.Tests`, `Lisere.Ai.Tests`, `Lisere.Video.Tests` and `Lisere.App.Tests`.

Current state: **340 tests** (120 Core, 15 Ai, 36 Video, 169 App), 0 build warnings. The full suite passes in parallel on the reference machine. The one-second preview render is covered by an FFmpeg integration test that checks its duration and the absence of an audio track; exporting that segment is covered by a view-model test.

Video integration tests generate a clip with FFmpeg and verify rendering, audio preservation, deinterlacing, exact size, cancellation and preview extraction. They are silently skipped when FFmpeg cannot be found, so the rest of the suite remains runnable on a machine without video tooling.

The complete After Effects preset corpus (47 files, `Experimental` folder excluded) is inventoried in `docs/PRESETS-ANALYSE.md`, with raw values in `docs/presets/PRESETS-INVENTAIRE.md` and the JSON files in `docs/presets/scan/`. Only the documented presets are described there; five are implemented (Ultron, Matrix, Infinity, Nebulus, Splendida).

Colour presets are covered by grading tests: for Ultron, luma-weighted saturation, legacy contrast, vibrance, band sharpening, full sequence and settings (`tests/Lisere.Core.Tests/UltronCcLookTests.cs`); for Matrix, extracted values, approximated tone curve, sharpening and ignoring Ultron settings (`MatrixLookTests.cs`); for Infinity and Nebulus, extracted values, exposure in linear light, a halo that spares flat areas and spills from a bright area (`InfinityLookTests.cs`, `NebulusLookTests.cs`); for Splendida, extracted values, decoded master curve, cos4 vignette that spares the centre and darkens the corners, and a curve table applied per channel (`SplendidaLookTests.cs`, `GradingFiltersTests.cs`). A regression test applies the five presets to a versioned test photo and compares the SHA-256 hash of the pixels (`ColorPresetRegressionTests.cs`). Separation of the two menus is covered in `tests/Lisere.App.Tests/EnhanceViewModelLanguageTests.cs`: preset independence, exclusive tabs and a status mentioning grading.

Preset import is tested on synthetic JSON and, when `LISERE_PRESET_SAMPLE_DIR` points to a preset folder, on a local corpus. The queue is tested with a fake renderer: execution order, settings snapshot, isolated failure, cancellation and final states.

RIFE is tested at three levels: real inference on two images, video rendering with a fake interpolator, then a real render with the shipped model (10 to 20 fps, audio preserved).

## Repository layout

```text
Lisere.slnx
src/
  Lisere/
  Lisere.Core/
  Lisere.Ai/
  Lisere.Video/
tests/
  Lisere.App.Tests/
  Lisere.Core.Tests/
  Lisere.Ai.Tests/
  Lisere.Video.Tests/
models/
  catalog.json
  onnx/
  rife/
  yunet/
```

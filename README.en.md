<div align="center">

<img src="assets/lisere-banner.png" width="880" alt="Liseré banner with the app interface">

# 🖼️ Liseré

**Enhance your photos and videos without sending them anywhere.**

An Image space, a Video space, an honest before/after comparison,
engines that run locally, and exports ready to keep.

[![Download Lisere-Setup.exe](https://img.shields.io/badge/Download-Lisere--Setup.exe-6f42c1?style=for-the-badge)](https://github.com/FGAMER768/Lisere/releases/latest/download/Lisere-Setup.exe)
[![Windows 10 and 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/FGAMER768/Lisere/releases/latest)

[Français](README.md) · [English](README.en.md)

</div>

> [!WARNING]
> **Project on hold: the CPU-render cause is identified and fixed, but resuming needs
> validation on a discrete GPU.**
>
> Latest test before the fix: **8K ×2** upscale with **Lisere Classic**, the **Chroma** preset
> and the **Ultron** grade, on an **RTX 4070**. Result: **the GPU is not being
> used**, the load goes to **RAM**, and 4K rendering therefore cannot be
> iterated on under proper conditions.
>
> Two causes were found. **Lisere Classic only had a CPU pipeline**: it now runs the
> same pipeline as a **D3D12 compute backend (ComputeSharp)** on the dedicated GPU,
> with an exact CPU fallback. For ONNX engines, DirectML was
> **pinned to DXGI adapter 0**, which on a hybrid laptop is usually the Intel iGPU:
> inference ran on the integrated GPU and its shared memory, not on the RTX 4070.
> Liseré now picks the adapter with the most **dedicated VRAM** and reports the GPU
> actually used; `LISERE_DML_ADAPTER` can force a DXGI index or a name fragment.
> Tile copies and the render preview no longer retain full-resolution frames in
> memory either.
>
> Classic's D3D12 backend still needs revalidation on the RTX 4070; development
> remains **paused** until these fixes are revalidated on a machine with a discrete
> GPU. Existing releases remain downloadable, but no performance
> improvement is promised for them.

<img src="assets/screenshots/image-en.png" alt="Liseré Image space: before/after comparison with Ultron CC grading">

## 📖 Contents

- [💡 Why Liseré?](#why)
- [📥 Installation](#installation)
- [⚡ Getting started](#getting-started)
- [✨ Features](#features)
- [🎛️ Presets](#presets)
- [🖼️ Before / after](#before-after)
- [⌨️ Shortcuts](#shortcuts)
- [📐 Formats and limits](#formats-and-limits)
- [🛠️ Development](#development)
- [❓ FAQ](#faq)
- [❤️ Credits and licences](#credits)

<a id="why"></a>
## 💡 Why Liseré?

- **Everything stays on your PC.** No account, no file upload, no external service.
  Only the optional update check, started when you ask for it, contacts GitHub.
- **A comparison, not a promise.** The divider, side-by-side mode and zoom show
  exactly what changed, on photos as well as videos.
- **Photos and videos in one app.** Six fine-grained controls, presets, colour
  grading, a timeline, RIFE interpolation and an export queue.
- **Complete presets.** One JSON file holds the sliders, the colour grading, the
  engine used and face refinement.

<a id="installation"></a>
## 📥 Installation

Download [Lisere-Setup.exe](https://github.com/FGAMER768/Lisere/releases/latest/download/Lisere-Setup.exe)
and run it. The installer installs Liseré for the current Windows account, without
administrator rights and without asking for anything else: the package contains the
self-contained app, FFmpeg and the models.

By default it installs to `%LOCALAPPDATA%\Programs\Lisere`. A Start menu shortcut is
offered, the desktop one is optional. To uninstall, use **Settings → Apps → Liseré** or
`Lisere.exe --uninstall`: your images, presets and settings in `%APPDATA%\Lisere` are
kept.

> [!NOTE]
> The binary is not signed with a commercial certificate: Windows may show a
> SmartScreen warning on first launch. The SHA-256 published with the release lets you
> verify the file. The publishing scripts accept an Authenticode certificate
> (`-CertificateThumbprint` or `-CertificatePath`).

<details>
<summary>Silent install or custom folder</summary>

```powershell
.\Lisere-Setup.exe --silent --install-dir="D:\Apps\Lisere" --no-shortcuts
```

`--silent` installs without UI, `--install-dir=...` chooses the folder, `--no-shortcuts`
adds no shortcut and `--desktop-shortcut` adds the desktop one. The exit code is `0` on
success, `1` otherwise. The chosen folder must be empty or contain an existing Liseré
installation, because uninstalling removes the whole install folder.

</details>

<a id="getting-started"></a>
## ⚡ Getting started

### 🖼️ An image

1. **Open** (`Ctrl+O`): the original appears, nothing is computed.
2. Pick a preset or adjust the sliders. **Colour grading** opens the second menu.
3. **Enhance image** (`Ctrl+Enter`).
4. Drag the divider, then **Export as JPEG** (`Ctrl+S`).

### 🎬 A video

1. Open the **Video** tab, then **Open** (`Ctrl+O`).
2. Choose the engine, preset, resolution, frame rate and optional passes.
3. **Refresh preview** (`Ctrl+Enter`) to compare the current frame.
4. **Export video** (`Ctrl+S`), or **Add to queue** to chain several clips.

Playback follows real time: when the engine cannot keep up, frames are skipped instead
of slowing the clip down, and before and after always show the same frame.

<a id="features"></a>
## ✨ Features

### 📷 Image

- Opens the formats supported by Windows Imaging Component, normalising CMYK, 16-bit,
  indexed, progressive or transparent images.
- Three comparison modes: **Split**, **Single** and **Side by side**, with synchronised
  zoom and panning of the enlarged image.
- Six enhancement controls: compression fix, noise reduction, sharpening, detail
  recovery, halo reduction and edge smoothing.
- Original detail, added noise and **face refinement** (YuNet): micro-texture focuses on
  skin without adding grain to the background or hair.
- Presets **Chroma**, **Auto**, **Manual**, **Auto + relative medium** and **None**.
- Five reproduced After Effects grades: **Ultron CC**, **Matrix CC**, **Infinity CC**,
  **Nebulus CC** and **Splendida CC**, plus a manual mode.
- Importable and exportable complete preset (sliders, colour, engine, faces).
- **Anime ×4** neural engine dedicated to drawing, animation and digital art,
  alongside the classical pipeline and the general engine.
- Automatic estimation, out-of-range warnings, duration estimate before a long render,
  cancellation and JPEG export at quality 92.

### 🎥 Video

- FFmpeg (`ffprobe`) analysis: resolution, frame rate, duration, codecs and audio.
- Editing timeline, keyboard transport, live preview and before/after comparison on the
  chosen frame.
- **Real-time playback** that skips frames the engine cannot keep up with.
- Interchangeable engines: **Lisere Classic** (deterministic local pipeline) or
  SHA-256-verified ONNX models, including **AnimeVideo ×4** for animation, run
  on DirectML with CPU fallback.
- Source resolution, 2×, 4×, 1080p or 2160p; source frame rate, 30, 60 or 120 fps.
- **RIFE ×2 neural interpolation**, `bwdif` deinterlacing and two-pass `vidstab`
  stabilisation.
- The same colour grading as the Image page, frozen into each queue job.
- JSON video preset import: six controls, detail recovery, noise, size, frame rate,
  stabilisation and deinterlacing. No proprietary weights are loaded.
- MP4 H.264/H.265 export with AAC audio, real progress, cancellation, a one-second
  rendered preview and export of that segment.
- Sequential queue: each job freezes its settings, and one failure does not stop the
  following clips.

### 🧩 Both spaces

- French and English UI, instant switch, remembered between sessions.
- Asynchronous work, wait dialogs and localised errors without raw technical details.
- GitHub updates: SHA-256 check, safe extraction and restart.

<a id="presets"></a>
## 🎛️ Presets

### ⚙️ Enhancement

| Preset | Behaviour |
|---|---|
| **Chroma** | Reference preset: bounded multi-scale detail, controlled unsharp and a tone curve measured on the Topaz reference. |
| **Auto** | Estimates the controls from the image or several clip frames. |
| **Manual** | Applies the entered values directly. |
| **Auto + relative medium** | Automatic estimate, then a medium relative adjustment. |
| **None** | No correction: only colour grading and export apply. |

When an anime engine is selected, **Chroma** automatically gives way to **Auto**; an
existing Manual or Auto preset is left untouched.

### 🎨 Colour grading

| Preset | Behaviour |
|---|---|
| **Ultron CC** | Saturation 129.997%, brightness +5, contrast +7, vibrance +50 and sharpening; the four sliders stay adjustable. |
| **Matrix CC** | Brightness +30, contrast +20 and three fixed sharpening passes. |
| **Infinity CC** | Multi-scale sharpening, 16% / radius 9.8 unsharp, soft halo and exposure −0.36. |
| **Nebulus CC** | Multi-scale sharpening, 10% / radius 20 unsharp and a pronounced halo. |
| **Splendida CC** | Sharpening, 15% / radius 25 unsharp, 25% vignette at 40°, brightness +21 / contrast +10 and master curve. |
| **Manual** | Adjusts saturation, brightness, contrast and vibrance by hand. |
| **None** | No grading: the image keeps the enhancement's colours. |

### 📦 Complete preset

The Image page's **Complete preset** card stores the sliders, colour grading, engine
and face refinement in a single JSON file:

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
    "addNoise": 0,
    "engine": "realesrgan-x4",
    "refineFaces": true
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

Import is normalised: out-of-range values return to the slider ranges, and a dynamic
mode (`Auto`, `AutoRelativeMedium`) becomes **Manual** with the saved values. **Chroma**
is kept when the six values match. The saved engine is restored when it is installed
and available; otherwise the current engine is kept, and older presets that do not
mention one stay readable. Face refinement is restored, then disabled when the chosen
engine is a super-resolution model. Unknown fields are ignored; an unrecognised format
or version is refused with a localised message. If a result already exists, importing
marks it stale until the next render.

### 🎞️ Imported video presets

The **Import preset...** button reads a JSON file exported by a video enhancement app
and carries over what Liseré can reproduce: the six controls, original detail, noise,
FHD/4K size, frame rate, stabilisation and deinterlacing. Grain is approximated with a
single noise amount. The mentioned model is shown for information only: **no
proprietary weights are loaded**, and rendering uses the engine selected in Liseré.

<a id="before-after"></a>
## 🖼️ Before / after

The **Chroma** preset and its tone curve were tuned and then compared with a reference
render produced with Topaz. The three images share the same crop and resolution
(1608 × 1608):

| Base (starting image) | Topaz (reference) | Liseré (local render) |
|---|---|---|
| <img src="assets/comparisons/01-base.jpg" width="260" alt="Base image"> | <img src="assets/comparisons/02-topaz.jpg" width="260" alt="Topaz render"> | <img src="assets/comparisons/03-lisere.jpg" width="260" alt="Liseré render"> |

**Base** is the starting image. **Topaz** is the reference render made with the Ultron
CC preset. **Liseré** is the local render with Chroma and Ultron CC. The comparison
covers sharpness, skin and hair micro-texture, leather rendering and midtones. The three
table images are JPEGs reduced to 900 px; the working files are not distributed.

### 🎬 Liseré, After Effects and Topaz on 4K shots

Ten 4K frames from the same clip went through three chains: **Liseré** (Liseré Classic
engine, Chroma preset, Ultron CC grading), **After Effects CC** alone (Ultron CC
grading, no enhancement) and **Topaz then After Effects CC** (Chroma enhancement and
Ultron CC). Two shots are shown here; each shot takes two rows, the full frame reduced
to 960 × 960 then a 100% detail (native 210 × 210 crop).

| 4K source | Liseré Classic + Ultron CC | After Effects CC | Topaz + AE CC |
|---|---|---|---|
| <img src="assets/comparisons/video/frame-01-stock.jpg" width="210" alt="Source frame, shot 1"> | <img src="assets/comparisons/video/frame-01-lisere.jpg" width="210" alt="Liseré render, shot 1"> | <img src="assets/comparisons/video/frame-01-aecc.jpg" width="210" alt="After Effects CC render, shot 1"> | <img src="assets/comparisons/video/frame-01-topaz.jpg" width="210" alt="Topaz then After Effects CC render, shot 1"> |
| <img src="assets/comparisons/video/frame-01-detail-stock.jpg" width="210" alt="100% detail, source, shot 1"> | <img src="assets/comparisons/video/frame-01-detail-lisere.jpg" width="210" alt="100% detail, Liseré, shot 1"> | <img src="assets/comparisons/video/frame-01-detail-aecc.jpg" width="210" alt="100% detail, After Effects CC, shot 1"> | <img src="assets/comparisons/video/frame-01-detail-topaz.jpg" width="210" alt="100% detail, Topaz then After Effects CC, shot 1"> |
| <img src="assets/comparisons/video/frame-10-stock.jpg" width="210" alt="Source frame, shot 10"> | <img src="assets/comparisons/video/frame-10-lisere.jpg" width="210" alt="Liseré render, shot 10"> | <img src="assets/comparisons/video/frame-10-aecc.jpg" width="210" alt="After Effects CC render, shot 10"> | <img src="assets/comparisons/video/frame-10-topaz.jpg" width="210" alt="Topaz then After Effects CC render, shot 10"> |
| <img src="assets/comparisons/video/frame-10-detail-stock.jpg" width="210" alt="100% detail, source, shot 10"> | <img src="assets/comparisons/video/frame-10-detail-lisere.jpg" width="210" alt="100% detail, Liseré, shot 10"> | <img src="assets/comparisons/video/frame-10-detail-aecc.jpg" width="210" alt="100% detail, After Effects CC, shot 10"> | <img src="assets/comparisons/video/frame-10-detail-topaz.jpg" width="210" alt="100% detail, Topaz then After Effects CC, shot 10"> |

Ultron CC grading is the same in all three columns; only the enhancement differs.
Liseré holds the micro-texture of the beard, hair strands and wrinkles with softer
edges; Topaz + AE CC pushes sharpness further, at the cost of drier skin and harder
edges. After Effects CC alone only changes the colour grade.

### 🧪 Two demonstrations + an upscale bonus

Two cases produced by the Image page pipeline, with the exact settings, plus an ×4
upscale bonus. Each comparison reads left to right: before, then after; the second
row is a 100% detail.

**1. 1608 px photo : Auto + Ultron CC**

<img src="assets/demos/01-auto-ultron.jpg" width="880" alt="Before/after comparison on a 1608 px photo, with a 100% detail">

Source 1608 × 1608. **Auto** preset, **Ultron CC** grading, no face refinement, no
upscaling. Output 1608 × 1608.

**2. 1916 × 2608 portrait : Auto, no grading**

<img src="assets/demos/02-auto-portrait.jpg" width="880" alt="Before/after comparison on the official portrait of Barack Obama, with a 100% detail">

Source 1916 × 2608. **Auto** preset, **no colour grading**, no face refinement, no
upscaling. Output 1916 × 2608.

**Bonus : 640 × 427 landscape, ×4 upscale**

<img src="assets/demos/03-mount-cook-4x.jpg" width="880" alt="Bicubic ×4 and Real-ESRGAN ×4 comparison on Hooker Lake and Mount Cook">

Source 640 × 427. **Bicubic ×4** / **Real-ESRGAN ×4** comparison, original detail 20,
output 2560 × 1708. This render uses the **×4 engine from video export**; the Image
page keeps the source size.

*Image credits.* Demo 1 and the comparisons above: still from the *Iron Man* film
series (Marvel Studios), used for illustration only; all rights reserved. Demo 2:
official portrait of Barack Obama by **Pete Souza**,
[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/), via
[Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Official_portrait_of_Barack_Obama.jpg).
Bonus: photo by Danny Postma, CC0, via
[AltPhotos](https://altphotos.com/photo/hooker-lake-at-the-bottom-of-mount-cook-new-zealand-1747/).

### 🎞️ Anime 6B or AnimeVideo v3?

The same five-second excerpt from **Namakura Gatana** (1917), 320 × 180 at 12 fps,
upscaled ×4 with both anime engines. The settings are identical: **Manual** preset at
zero, no colour grading, 1280 × 720 output, 60 frames. **AnimeVideo v3** is on the left,
**Anime 6B** on the right.

<img src="assets/demos/04-anime-6b-vs-animevideo.jpg" width="880" alt="AnimeVideo v3 on the left and Anime 6B on the right on Namakura Gatana, with a 100% detail">

Top row: the whole frame; bottom row: a 100% detail. **AnimeVideo v3** keeps more grain
and micro-texture; **Anime 6B** smooths flat areas and lines more aggressively, at the
cost of a softer look.

<img src="assets/demos/05-anime-temporal-noise.jpg" width="880" alt="Temporal noise maps on the roofs and the ground, AnimeVideo v3 on the left and Anime 6B on the right">

The second plate measures the **mean difference between consecutive frames**,
amplified ×200: roofs and sky on top, a flat patch of ground below. The darker the map,
the less that area changes from one frame to the next.

| Measurement (0–255) | Bicubic | AnimeVideo v3 | Anime 6B |
|---|---:|---:|---:|
| Mean global delta | 1.501 | **1.617** | 1.741 |
| Flat-area delta, mean | 0.068 | 0.177 | **0.093** |
| Per-pixel temporal standard deviation, flat area | 0.791 | 0.895 | **0.742** |
| Per-pixel temporal standard deviation, texture p95 | 12.59 | **21.27** | 28.01 |

**AnimeVideo v3 is therefore not universally more stable.** It is more stable globally
and on highly detailed textures, but Anime 6B moves less on flat areas. The 6B render is
also much slower: **72.3 s** versus **9.9 s** for these 60 frames on DirectML. Both models
process each frame independently; neither adds explicit temporal coherence.

The same full export launched from the Liseré window (AnimeVideo v3, 4×, Manual at zero)
produces exactly **1280 × 720, 60 frames, 5.00 s**. Against the harness render:
**PSNR 40.67 dB**, **SSIM 0.991**, the remaining difference coming from encoding quality
(651,296 bytes versus 1,354,678).

*Credit.* Excerpt from **Namakura Gatana** (『なまくら刀』, 1917) by **Jun'ichi Kōuchi**,
a public-domain Japanese film, via
[Internet Archive](https://archive.org/details/1917-the-dull-sword-namakura-gatana).

<a id="shortcuts"></a>
## ⌨️ Shortcuts

| Shortcut | Image | Video |
|---|---|---|
| `Ctrl+O` | Open an image | Open a video |
| `Ctrl+Enter` | Run the enhancement | Refresh the preview |
| `Ctrl+S` | Export as JPEG | Export the video |
| `Space` | - | Play / pause |
| `Ctrl+←` / `Ctrl+→` | - | Previous / next frame |

The **FR** and **EN** buttons in the title bar switch language without a restart; the
choice is stored in `%APPDATA%\Lisere\settings.json`.

<a id="formats-and-limits"></a>
## 📐 Formats and limits

<details>
<summary>🔍 Images, videos and engine behaviour</summary>

### 📷 Image

- Offered inputs: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif`, `.tiff`, `.gif`, `.ico`,
  `.wdp`, `.jxr`, `.webp`, `.heic`, `.heif` and `.avif`, depending on Windows codecs.
- EXIF orientation is applied; transparency is composited on white. Animated or
  multi-page images use only their first frame.
- Maximum file size: 24 MB. Decoding limit: 40 megapixels.
- JPEG export only, quality 92.

### 🎥 Video

- Offered inputs: `.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, `.m4v`, `.mts`, `.m2ts`,
  `.wmv`, `.mpg`, `.mpeg`, depending on FFmpeg codecs.
- MP4 H.264 or H.265 output with AAC audio. Video rotation metadata is not applied when
  decoding.
- RIFE is limited to **×2**. On the reference Intel Iris Xe, a 1080p inference costs
  about 0.83 s after warm-up (0.037 s at 192×192); the CPU fallback is much slower.
- **BasicVSR++ is not included**: the official weights are PyTorch and DCN alignment
  depends on CUDA. No verifiable ONNX export is distributed.
- **Face restoration is not included**: GFPGAN and CodeFormer need a detector, a
  512×512 alignment and pasting, for 340-377 MB weights.
- The one-second preview writes a temporary MP4 to `%TEMP%\Lisere\previews`, purged
  after 24 h; preview export goes to the chosen file. Neither keeps audio.

### 🧠 Engines and presets

- ONNX engines run on **DirectML** when a DirectX 12 adapter is available, with a CPU
  fallback whose reason is shown in the engine note.
- **Lisere Classic** picks the D3D12 adapter with the most dedicated VRAM and reports
  the GPU actually used; `LISERE_CLASSIC_GPU` forces an index or name fragment, and
  `0`/`cpu`/`off` sends it back to the CPU. Enhancement and resizing run on the GPU,
  then grading and the Chroma tone curve are applied on the CPU.
- Preset import loads no proprietary weights and does not reproduce the original
  models: only settings, size and frame rate are carried over.
- `outSizeMethod` 6 → 1080p and 7 → 2160p are deduced from the preset corpus; other
  methods fall back to the source geometry. Grain is approximated by one noise amount.
- Auto mode samples three positions in the clip; it is not a dedicated neural estimator.
- **Duration estimates are indicative**: they use a per-engine learned cost, while the
  remaining time during a render follows real progress.

</details>

<a id="development"></a>
## 🛠️ Development

### 📋 Requirements

- Windows 10 or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build; the installer does not
  need it, the app is self-contained.
- FFmpeg is bundled in releases. On a fresh checkout, run
  `scripts/fetch-ffmpeg.ps1` for the video tests.

```powershell
dotnet restore Lisere.slnx
dotnet run --project src/Lisere
dotnet build Lisere.slnx
dotnet test Lisere.slnx
```

In Visual Studio, open `Lisere.slnx`, set `src/Lisere` as the startup project and press
`F5`. During development, `LISERE_FFMPEG` takes priority over the bundled package.
`src/Lisere.Setup` is intentionally outside the solution: it targets `net48` and must
be built with `scripts/build-setup.ps1`.

<details>
<summary>🏗️ Architecture and repository layout</summary>

| Project | Role |
|---|---|
| `src/Lisere` | WPF app, Image and Video pages, view models, dialog, file, localisation, update and uninstall services. |
| `src/Lisere.Setup` | WPF installer: bundled package extraction, per-user copy, shortcuts and uninstall registration. |
| `src/Lisere.Core` | Image models, filters, estimation, validation, enhancement pipeline, face detection and clip processing. |
| `src/Lisere.Ai` | Engines (`IFrameEnhancer`, `IFrameInterpolator`), Classic CPU/D3D12 pipeline, tiled ONNX super-resolution, RIFE, YuNet, DirectML/CPU and verified catalogue. |
| `src/Lisere.Video` | FFmpeg, `ffprobe`, frame extraction, rendering, deinterlacing, stabilisation, interpolation, presets and progress. |
| `tests/` | Four xUnit projects: Core, Ai, Video and App. |

The Image page is built around `EnhanceViewModel` and `IEnhancementOrchestrator`. The
Video page uses `VideoViewModel`, split by feature (queue, preview, render); it delegates
analysis to `IVideoProbe`, thumbnails to `VideoPreviewRenderer`, the confirmation plan to
`VideoRenderPlanFactory` and rendering to `IVideoRenderService`. Engine choice goes
through `IFrameEnhancer`: the UI and video pipeline never know which model runs.
`models/catalog.json` describes each model (file, scale, licence, hash); a missing or
modified model is reported as unavailable.

```text
Lisere.slnx
assets/            icon, screenshots, comparisons
src/               Lisere, Lisere.Core, Lisere.Ai, Lisere.Video, Lisere.Setup
tests/             Core, Ai, Video, App
models/            catalog.json, onnx/, rife/, yunet/
ffmpeg/            manifest, DLLs, licences
Update/            version.txt and update documentation
scripts/           ffmpeg, publishing, installer, signing
```

</details>

<details>
<summary>🧪 Tests and publishing</summary>

The suite has **499 tests** (125 Core, 37 Ai, 57 Video, 280 App) with 0 build warnings.
Video integration tests generate a clip with FFmpeg and check rendering, audio,
deinterlacing, exact size, cancellation and preview extraction; they are skipped when
FFmpeg is missing. Presets are covered with synthetic JSON and, when
`LISERE_PRESET_SAMPLE_DIR` is set, a local corpus. RIFE is tested with real inference,
simulated rendering and a real render (10 → 20 fps).

To publish a release:

1. Change `<Version>` in `src/Lisere/Lisere.csproj`.
2. Run `powershell -ExecutionPolicy Bypass -File scripts/publish-update.ps1`.
   It produces `Update/Lisere.zip`, its `.sha256` and `Update/version.txt`.
3. Rebuild the installer with `scripts/build-setup.ps1`: it embeds that ZIP and produces
   `artifacts/installer/Lisere-Setup.exe`, the versioned copy and its checksum.
4. Create a GitHub release `v<version>` with the four assets: `Lisere-Setup.exe`,
   `Lisere-Setup.exe.sha256`, `Lisere.zip` and `Lisere.zip.sha256`. It must remain the
   latest release, because the app uses `releases/latest`.
5. Commit and push `Update/version.txt` **after** uploading the assets.
6. Verify from a previous install: **Updates → Check → Install**, then the restart.

Both scripts accept `-CertificateThumbprint` or `-CertificatePath` to sign artefacts.
`Update/*.zip`, `Update/*.sha256` and `artifacts/` are ignored by Git. Package details
and extraction safeguards are in [`Update/README.md`](Update/README.md).

</details>

<a id="faq"></a>
## ❓ FAQ

**Do my files leave my machine?**
No. All processing is local. Only the update check, when you ask for it, contacts GitHub.

**Do I need a GPU?**
Not to launch the app. **Lisere Classic** runs its pipeline as D3D12 compute on the
dedicated GPU when available, with an exact CPU fallback. `LISERE_CLASSIC_GPU` forces
an adapter index or name fragment; `0`, `cpu` or `off` disables the GPU. ONNX engines
use DirectML on the DXGI adapter with the most
**dedicated VRAM** (the iGPU is skipped when a discrete card is present), with a CPU
fallback whose reason is shown in the engine note. `LISERE_DML_ADAPTER` forces a DXGI
index or an adapter name fragment. The CPU fallback is much slower. 4K tests still need
revalidation on a discrete GPU: Classic's D3D12 backend has not been measured on the
reference RTX 4070 yet.

**Why does Windows warn me on launch?**
The binary is not signed with a commercial certificate. Check the SHA-256 published with
the release, or sign your own builds with the provided scripts.

**Can I import presets from another app?**
Yes for JSON video presets: Liseré carries over the settings it understands. No
proprietary weights are read, and rendering uses the engine selected in Liseré.

**Where are my settings?**
In `%APPDATA%\Lisere`. Uninstalling the app keeps them.

**Why is a video render slow?**
Every frame goes through the engine, and RIFE, stabilisation or deinterlacing add extra
passes. The pre-render estimate gives an order of magnitude; the remaining time during a
render follows real progress.

**How do I uninstall?**
**Settings → Apps → Liseré**, or `Lisere.exe --uninstall`. Images, presets and settings
remain in `%APPDATA%\Lisere`.

<a id="credits"></a>
## ❤️ Credits and licences

Liseré is designed and developed by **FGAMER768**. It redistributes no proprietary
weights, code or media.

### 🧠 Models

- **YuNet** (face detection): Shiqi Yu / OpenCV Zoo, MIT licence.
- **RIFE 4.9** (interpolation): Practical-RIFE, MIT licence.
- **Sub-pixel SRCNN ×3**: ONNX Model Zoo, Apache-2.0 licence.
- **1× RealPLKSR denoising**: Philip Hofmann, CC-BY-4.0 licence.
- **Real-ESRGAN general ×4**: xinntao/Real-ESRGAN, ONNX export, BSD-3-Clause licence.
- **Real-ESRGAN Anime 6B ×4**: xinntao/Real-ESRGAN, SkillSafe ONNX export,
  BSD-3-Clause licence.
- **Real-ESRGAN AnimeVideo v3 ×4**: xinntao/Real-ESRGAN, SkillSafe ONNX export,
  BSD-3-Clause licence.
- **ONNX Runtime and DirectML**: Microsoft, MIT licence.
- **FFmpeg**: BtbN build under GNU GPL v3; provenance, hashes and licence in
  `ffmpeg/README.md` and `ffmpeg/LICENSE.txt`.

Full licences and hashes are in [`models/README.md`](models/README.md).

### 📚 Libraries

**WPF-UI**, **Fluent System Icons**, **CommunityToolkit.Mvvm**,
**Microsoft.Extensions.Hosting**, **xUnit.net** and **Microsoft.NET.Test.Sdk**, under MIT
or Apache-2.0 licences depending on the project.

### 🪪 Identity and references

- The icon (`assets/LisereIcon.svg`, `assets/Lisere.ico`, `assets/LisereIcon.png`) was
  created for the project.
- The **Ultron CC**, **Matrix CC**, **Infinity CC**, **Nebulus CC** and **Splendida CC**
  presets reference analysed After Effects presets; Adobe After Effects and those names
  belong to their owners. No `.ffx` file is distributed.
- The **Chroma** tone curve was measured on a Topaz render; Topaz is a trademark of
  Topaz Labs. No Topaz weights, code or media are distributed.

### 🧪 Test images and documentation

- `tests/Lisere.App.Tests/TestData/face.jpg`: official portrait of Barack Obama by Pete
  Souza (CC BY 3.0), cropped for tests; see the folder README.
- `tests/Lisere.App.Tests/TestData/preset-photo.jpg`: photo by Jeff Sheldon (Lorem
  Picsum / Unsplash), for colour-grading regression testing.
- [GUIDE-UTILISATION.en.md](GUIDE-UTILISATION.en.md): complete English user guide.
- [GUIDE-UTILISATION.md](GUIDE-UTILISATION.md): guide complet en français.
- [docs/MODELES-CANDIDATS.md](docs/MODELES-CANDIDATS.md): comparable open models.

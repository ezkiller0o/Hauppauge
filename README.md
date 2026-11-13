# Hauppauge Live Capture Studio

A standalone Windows program for live video from the **Hauppauge USB-Live2 (UPC00610)** analog capture device, with snapshots and **video-only MP4 / H.264 recording (never any audio)**, built for the lowest latency the driver allows.

العربية: [README.ar.md](README.ar.md) · Test plan: [TESTING.md](TESTING.md) (Arabic)

## Status — read this first

- The source code is **complete** (C# / .NET 8 / WinForms / DirectShow), but it was written in an environment with **no .NET SDK, no Windows and no capture hardware**. Therefore:
  - It has **not been compiled, run, or tested on a real USB-Live2.** There is **no prebuilt EXE** in this package; you build it with one command (section 2).
  - Only static checks (bracket balance, every localisation key defined, cross-file member references) and a manual code review were done.
  - After the first build, run the checklist in [TESTING.md](TESTING.md). If something fails, **Settings > Copy diagnostics** plus `Settings\app.log` is all that is needed to fix it quickly.
- **No promise of zero latency.** The program removes software delay (no render clock, minimum driver buffers, newest-frame-only preview, no image processing). End-to-end latency still depends on the driver and USB, and must be measured (method in TESTING.md).
- **Unconfirmed:** that the USB-Live2 driver exposes a DirectShow video-capture filter. Hauppauge's pages do not say. If the device is not listed, see Troubleshooting.

## Requirements

- Windows 10 or 11, 64-bit.
- A USB-Live2 with its driver (section 1).
- Running the EXE needs **no .NET installation** (self-contained). Building needs the .NET 8 SDK or newer, or GitHub Actions.
- `ffmpeg.exe` for recording only. Live view and snapshots work without it.

## 1) Install the device driver

From Hauppauge's official pages (links and versions can change, so check them):

1. Support page: <https://hauppauge.com/pages/support/support_usblive2.html>
2. Download **Hauppauge Capture** (version 43356, about 86 MB at the time of writing):
   <https://s3.amazonaws.com/hauppauge/capture/hauppaugecapture_1_2_43356.exe>
3. **Save** the installer, then run it from your Downloads folder (do not use "Run" directly from the browser). If SmartScreen blocks it: *More info*, then *Run anyway*.
4. The USB-Live2 driver itself moved to Hauppauge's Windows driver page: <https://www.hauppauge.com/pages/support/support.html#win10> (covers the old model 122000 and the new model 122020).
5. **Restart Windows**, then plug in the device (or unplug and replug it). Prefer a direct USB port over a hub.
6. If S-Video shows no picture, Hauppauge's page offers a dedicated S-Video fix driver (`WinTV-soloHDdualHDquadHD_USB-Live2_Model122020_5_2025_0612_43164-Win7-8-5_2020_0406_38125.exe`) followed by a reboot. The page does not explicitly confirm Windows 10/11 support for it, so use it only if needed.
7. **Verify:** the device shows in Device Manager without a warning icon. Then open another DirectShow application (OBS Studio with *Video Capture Device*, or AMCap) and confirm the device is listed there. **If it is not listed there, it will not be listed in this program either.**
8. Close **Hauppauge Capture** and any other program using the device before starting this one; a capture device normally serves one application at a time.

Hauppauge notes that the green LED on the device stays off when S-Video is connected but Composite is selected. Pick the right input in the program's *Video source* list.

## 2) Get the EXE

**A) On a Windows PC (easiest).** Install the **.NET 8 SDK (x64)** from <https://dotnet.microsoft.com/download>, then double-click `build.bat` (or run `powershell -ExecutionPolicy Bypass -File build.ps1`). Output: `dist\Hauppauge Live Capture Studio\Hauppauge Live Capture Studio.exe`, plus `ffmpeg\ffmpeg.exe` if the automatic download worked. Options: `-SkipFfmpeg` (no download), `-Clean` (delete the previous output).

**B) Without installing anything: GitHub Actions.** Push the folder to a GitHub repository (private is best), open **Actions > Build Windows EXE > Run workflow**, then download the artifact ZIP.

**C) Visual Studio 2022.** Open `HauppaugeLiveCaptureStudio.sln` and build. For the self-contained publish:

```
dotnet publish src\HauppaugeLiveCaptureStudio\HauppaugeLiveCaptureStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 3) FFmpeg (recording only)

Recording starts `ffmpeg.exe` as a separate process that receives raw frames on stdin; the live view never depends on it. Search order: path set in Settings, `ffmpeg\ffmpeg.exe` next to the program, `ffmpeg.exe` next to it, `tools\`, `ffmpeg\bin\`, then PATH.

- An H.264 encoder is required: `libx264` (preferred) or `h264_mf` (Windows Media Foundation). The usual **gpl** and **essentials** builds contain libx264.
- `tools\fetch-ffmpeg.ps1` downloads a build (`build.ps1` calls it).
- **Licence:** builds with libx264 are GPL. They are downloaded on your machine and are not part of this project. If you pass the program on together with ffmpeg, follow the GPL terms.

## 4) Usage

1. Connect the video source (yellow RCA = Composite, or S-Video) and start the program. The device appears under *Capture device* and the indicator turns to *Connected*. *Refresh* rescans.
2. Pick the **Video source** (Composite / S-Video), resolution and frame rate. The lists come from the capabilities the driver reports. The real size and rate are shown under the picture.
3. Press **START LIVE**.
4. **SNAPSHOT:** type a name, choose JPG or PNG. Files go to `Pictures`, at the best quality the source offers (JPG quality 100, or lossless PNG). An existing name gets `(1)`, `(2)`, ...; nothing is overwritten. A success message appears; click it to open the file in Explorer. Works while recording.
5. **Recording:** type a name (date and time optional), press **START RECORDING**. A red **REC** badge and a timer show. The MP4 (H.264, **no audio**) goes to `Videos`. **STOP RECORDING** finalises it. The live view never stops when recording starts or stops. One recording at a time; earlier recordings are never replaced.
6. **Full screen:** FULL SCREEN button, **F11**, or double-click the picture. Leave with **Esc** or EXIT FULL SCREEN. In full screen a floating bar (snapshot, record, timer, exit) appears when the mouse moves and hides again.
7. **Picture shape:** Auto (4:3 for SD), square pixels, 16:9, or fill the window. This affects display; for Auto and 16:9 the MP4 is tagged with the same display aspect without changing pixels.
8. **Language:** button in the top bar (EN / Arabic, with right-to-left layout). **Settings:** save folder, ffmpeg path, recording quality, crash-safe MP4, auto-start live, copy diagnostics.

The info line shows size, measured frame rate, frames dropped by the recorder (should stay 0), and *display delay*: the time inside the program between a frame arriving from the driver and being painted. It is **not** the camera-to-screen latency.

## 5) Folders

```
Documents\Hauppauge Capture Studio\     (created automatically; relocatable in Settings)
  Pictures\    snapshots (JPG / PNG)
  Videos\      recordings (MP4)
  Settings\    settings.json and app.log
```

## 6) Troubleshooting

| Symptom | What to do |
|---|---|
| Device not listed | Install the driver (section 1), try another USB port, press Refresh. Check it in OBS or AMCap; if it is missing there too, the problem is the driver, not this program. |
| "No video frames are arriving" | Check cable, source power, Composite/S-Video choice, that the resolution matches the source standard (NTSC 720x480 or PAL 720x576), and that no other program holds the device. |
| START LIVE fails | Read the message; close Hauppauge Capture and other programs using the device. |
| Recording does not start | ffmpeg is missing or has no H.264 encoder (Settings shows the state). Details in `Settings\app.log`. |
| Comb lines in motion | Analog video is interlaced. The program does no image processing, so it does not deinterlace. |
| SmartScreen warning | The EXE is not code-signed. *More info*, then *Run anyway*. |
| Noticeable delay | Use a direct USB port, close heavy programs, and measure it with the method in TESTING.md. Part of the delay is inside the driver and outside this program's control. |

For any problem: **Settings > Copy diagnostics**, then send the text together with `Settings\app.log`.

## 7) Known limits

- Not yet built or tested on the real device (see Status).
- No deinterlacing or other image processing, to keep latency minimal.
- Audio is unsupported by design: no audio path is connected in the graph and ffmpeg runs with `-an`.
- One device at a time; Windows 10/11 x64 only.

## 8) Project layout (developers)

```
src/HauppaugeLiveCaptureStudio/
  Core/        paths, settings, no-overwrite file naming, localisation, log, MTA worker thread
  DirectShow/  hand-written COM interop (no external packages), graph builder, capture engine
  Media/       FrameHub (frame distribution), FfmpegRecorder, SnapshotService
  UI/          main window, full screen, settings, video view
```

Capture path: device, driver, capture filter, SampleGrabber (RGB32, no render clock), **FrameHub**. Each frame is copied once into a pooled buffer and handed to three independent consumers: the **preview** (newest frame only), **snapshots** (latest frame on demand), and the **recorder** (its own queue, thread and lower-priority ffmpeg process). If ffmpeg falls behind, only the recorder's own queue drops frames (the counter is shown); the preview is never affected.

## Sources

- [USB-Live2 product page (Hauppauge)](https://hauppauge.com/site/products/data_usblive2.html)
- [Hauppauge Capture for USB-Live2, support page](https://hauppauge.com/pages/support/support_usblive2.html)
- [Windows drivers (Hauppauge)](https://www.hauppauge.com/pages/support/support.html#win10)

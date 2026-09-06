# Changelog

Versions follow semantic versioning. The package version lives in `Packages/com.denisislamov.gsplat/package.json`
and in `GSplatVersion.Current`; a test keeps them equal. Tags on `main` mark releases.

## Unreleased

- WebGL: worlds load again. The renderer created structured GPU buffers for the compute sorter on every platform,
  and WebGL2 has none, so `SetData` threw inside a fire-and-forget load and the viewer sat on "Preparing" with a
  black screen. The buffers are now created only where compute shaders exist, and an unexpected exception in
  `WorldLoader.LoadAsync` is logged and shown as a failed load instead of vanishing.
- WebGL: the page hook and `Load On Start` no longer start the same load twice (one "Loading was cancelled" error
  at startup).

## 0.2.1

Readability release: the code was reorganized so that each file does one thing and the long methods read as a
list of steps. Rendering output and frame times are the same as 0.2.0 (checked against the golden images, two new
ones of real scenes included, and the frame-time tests).

- `GaussianSplatRenderer.TryPrepare` is three named steps; the per-camera state has its own file.
- `SplatCameraView` carries the camera data both sorters need; the CPU job and the compute kernel read the same
  struct. `ISplatSorter.PrepareOnMainThread` is now `Sort`, and `NeedsCompute` is gone (ask for `DrawArgs`).
- The vertex shader is load, project, footprint, cull, corner, color; the quad math lives in `GSplatCore.hlsl`.
- `SplatGpuData` upload paths, the PLY header parser and the world descriptor writer are split into small methods.
- UI built in code (viewer overlay, scene-switch canvas) goes through `UiFactory`; one safe-area calculation.
- Scene generators share `SceneObjects`; the viewer and InnerTest scenes were regenerated.
- Web page hooks (`LoadFromPage`, `PauseFromPage`, `ResumeFromPage`) moved from `WorldLoader` to a small
  `WebPageBridge` component on the same object; the page template is unchanged.
- Tests: shared test assembly, real-scene golden images, chunk upload readback, safe area, scene generators,
  camera view (90 EditMode, 61 PlayMode).

Known limits, each marked with a TODO in the code:

- SPZ version 4 (zstd) is recognized and refused; there is no managed zstd decoder yet.
- GPU rotation is 8-bit "first three"; colors above 1.0 (HDR trainers) are clamped when packing.
- One SH texture caps the splat count at SH degree 3; a second texture would lift it.
- Building the GPU layout runs Burst jobs on the main thread (100 to 300 ms per 500k); on the web the decoders
  are not time-sliced yet, so a big file freezes the page for that long.
- The CPU copy of the packed data stays in memory after the upload (8 MB per 500k).
- The level crossfade draws both levels for three seconds; a dithered swap would be cheaper on weak phones.
- The vertex clip of culled splats has not been measured on Mali; it may disable some tile-based fast paths.
- The sort order texture is passed to the draw pass as a raw RenderTexture, not an RTHandle; RenderGraph cannot
  reorder the two passes, so it is safe, but an import would be cleaner.

## 0.2.0

- Sorting by distance to the camera (Spark's choice), logarithmic 16-bit depth keys, frustum and sub-pixel
  culling inside the sort's key pass, indirect draw from the compute sorter.
- Positions stored as 16-bit fractions of the chunk bounds (`.gsplat` format version 2).
- SPZ color constant fixed (0.15); scenes now match the Spark web viewer at 37 to 38 dB PSNR.
- LDF axis convention, world descriptors with units-to-meters scale and spawn point, world root scaling.
- Three InnerTest worlds with four quality levels each, one scene per world with a scene-switch canvas and a
  URP cube; device quality profile applied in hand-made scenes.
- Controls: screen-relative look sensitivity, logarithmic pinch, smoothed touch look; safe-area aware UI and
  overlay; overlay shows the package version.
- Android and WebGL builds from the menu; per-camera sort state; edit-mode safe resource cleanup.

## 0.1.0

- SPZ (versions 1 to 3) and PLY import, chunked GPU layout in RGBA8 textures, Morton ordering.
- Splat shader (shader model 3.5), GPU counting sort without wave intrinsics and CPU Burst sort, URP
  RenderGraph integration.
- Progressive world loading with crossfade, quality controller, fly/walk camera with touch and joystick,
  debug overlay, golden-image tests.

# Changelog

Versions follow semantic versioning. The package version lives in `Packages/com.denisislamov.gsplat/package.json`
and in `GSplatVersion.Current`; a test keeps them equal. Tags on `main` mark releases.

## Unreleased

- Internal: readability refactoring with no change in behavior or performance (verified against golden images
  and the frame-time tests from 0.2.0).
- Web page hooks (`LoadFromPage`, `PauseFromPage`, `ResumeFromPage`) moved from `WorldLoader` to a small
  `WebPageBridge` component on the same object; the page template is unchanged.

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

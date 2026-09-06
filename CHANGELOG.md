# Changelog

Versions follow semantic versioning. The package version lives in `Packages/com.denisislamov.gsplat/package.json`
and in `GSplatVersion.Current`; a test keeps them equal. Tags on `main` mark releases.

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

# How the renderer works

This is the long explanation behind the code. It assumes you know Unity and have seen a shader before, but nothing
about Gaussian splatting. Every section names the files where the thing happens.

## What a Gaussian splat scene is

A splat scene is a cloud of a few hundred thousand to a few million tiny, semi-transparent, colored ellipsoids.
Each one ("a splat") has a position, three axis lengths, a rotation, an opacity and a color. There is no mesh and
no lighting: the color already contains the light that was in the photos or the generator the scene came from.
Drawn back to front with alpha blending, the ellipsoids add up to something that looks like a photograph from
any viewpoint. That is the whole trick, and also the source of every difficulty below: the order matters, there
are a lot of them, and each one is transparent.

Optionally a splat carries spherical harmonics (SH) coefficients: a small set of numbers that make its color
change a little with the viewing direction, which is how reflections and glossy highlights are stored. Degree 0
is a plain color; degrees 1 to 3 add 9, 24 or 45 more numbers per splat.

## Files in, GPU data out

Two formats come in. `.ply` is the original research format: floats for everything, big files. `.spz` is
Niantic's compressed format: 24-bit fixed-point positions, one byte per scale and color channel, a packed
quaternion, and gzip over the whole thing, about ten times smaller. Both decode into the same in-memory structure,
`SplatCloud`, which is just one flat array per attribute.

`Runtime/Formats/Spz`, `Runtime/Formats/Ply`, `Runtime/Data/SplatCloud.cs`

Two details cost real debugging time and are worth knowing. SPZ gzips the header together with the body, so the
file on disk starts with the gzip signature, not with the format's magic number. And SPZ stores color as
`round(c * 0.15 * 255 + 127.5)`, with 0.15 being its own constant and not the spherical-harmonics constant
0.2821 that the rest of the math uses. Decoding with the wrong constant compresses contrast around mid-grey by
1.88x and every scene looks washed out.

Different tools disagree about which way is up. The importer has a setting for the axis convention of the
source (`SplatCoordinateSystem`); converting means negating some axes, which also flips the rotation quaternion
and the sign of the SH coefficients that are odd in that axis. `CoordinateConverter.cs` does all three at once.

After decoding, `GsplatBuilder` turns the cloud into what the GPU wants:

1. Drop nearly transparent splats (they cost as much to draw as opaque ones and show nothing). Optionally keep
   only the N most important ones, importance being opacity times surface area.
2. Sort the splats along a Morton curve so that splats that are close in space are close in the array. This
   matters for the next step.
3. Cut the array into chunks of 65 536 splats. Because of the Morton order each chunk covers a compact region,
   so a chunk has a small bounding box. Chunks are the unit of frustum culling and of streaming uploads.
4. Pack each splat into 16 bytes: position as three 16-bit fractions of its chunk's bounding box, three bytes of
   log-scale, three bytes of rotation, four bytes of RGBA. This is `PackedSplat.cs`; the shader-side twin that
   unpacks it is `GSplatPacked.hlsl`, and a test checks that the two agree bit for bit.

`Runtime/Formats/Gsplat/GsplatBuilder.cs`, `Runtime/Data/SplatSpatialSort.cs`, `Runtime/Gpu/PackedSplat.cs`

The result (`GsplatData`) is saved as the `.gsplat` payload inside a `GaussianSplatAsset`, or built at runtime by
`SplatLoader` from a URL.

The same scene has four shapes on its way to the screen, and the names follow the file format rather than each
other, so here they are side by side: `SplatCloud` is the decoded scene as float arrays, one per attribute;
`GsplatData` is the packed, chunked version of it, exactly what the `.gsplat` file holds; `SplatGpuData` is that
data uploaded into textures and buffers; `GaussianSplatAsset` is the Unity asset that stores a `GsplatData` payload
and hands out a fresh copy on load.

## Why everything lives in textures

Unity cannot create integer-format `Texture2D`s, and WebGL2 / OpenGL ES 3.0 vertex shaders cannot read
structured buffers at all. To keep one shader for every platform, all per-splat data is stored in ordinary RGBA8
textures: the 16 bytes of a splat are four consecutive texels, and the shader rebuilds each 32-bit integer from
four bytes. The same goes for the SH coefficients and for the draw order. The only exceptions are the chunk
table (a structured buffer, used only by the compute path) and a tiny RGBA-float texture with the position
range of each chunk.

Uploading happens one chunk (1 MB) per frame through a small staging texture, so a big scene appears
progressively instead of freezing the frame it arrives in.

`Runtime/Gpu/SplatGpuData.cs`

## Every frame: cull, sort, draw

For each camera, `GaussianSplatRenderer.TryPrepare` does the CPU-side work:

- Test each chunk's bounding box against the camera frustum. Only chunks in view take part in the rest.
- Compute the depth range of the visible chunks and decide, from how far the camera moved, whether the order
  from last frame is still good enough (`SplatSortPolicy`).
- Hand a `SplatSortInput` to the sorter.

The sorter's job is to produce the back-to-front order of the visible splats. It is a counting sort on a
16-bit key: each splat's distance to the camera is mapped to one of 65 536 buckets, a histogram counts the
buckets, a prefix sum turns counts into offsets, and a scatter writes each splat index into its slot. The key
is logarithmic in distance, so surfaces two meters away get sub-millimeter buckets while the background gets
centimeters; and it is the distance to the camera rather than the depth along the view axis because that is
what the reference web viewer (Spark) does, and the two choices produce visibly different blends of overlapping
splats.

There are two sorters behind one interface:

- `GpuCountingSorter` runs the four steps as compute shader kernels. The compute shader uses thread groups of
  128 and only `InterlockedAdd` and group-shared memory, deliberately avoiding wave/subgroup intrinsics, which is
  what limits other Unity splat renderers to D3D12, Metal and Vulkan. It also writes the number of surviving
  splats into an indirect-draw argument buffer, so the draw call never needs a CPU round trip.
- `CpuCountingSorter` does the same with Burst jobs, for WebGL2 and GPUs without compute. It runs
  asynchronously: the job started in one frame is collected in the next, so the order is one frame behind the
  camera, which is not visible in practice.

Both write the order into the RGBA8 order texture. Both also cull in the key pass: a splat that is behind the
camera, entirely off screen, or too small to cover a pixel gets no slot at all. This turned out to be the single
most important optimization on tile-based mobile GPUs: hundreds of thousands of sub-pixel transparent quads cost
about 18 ms on an Apple GPU while contributing nothing visible.

`Runtime/Sorting/`, `Runtime/Shaders/Resources/GSplatCountingSort.compute`

The draw is one instanced procedural call: a six-index quad, drawn as many times as there are ordered splats.
Instance `i` reads slot `i` of the order texture to find its splat, loads the four texels of that splat, and
projects the ellipsoid onto the screen.

## The shader math

`GSplatCore.hlsl` and `GSplatSplat.shader` implement the projection from the 2001 EWA splatting paper as used by
3D Gaussian Splatting:

1. Build the 3D covariance of the ellipsoid from its rotation and scale, in world space, so object transforms
   come for free: `Sigma = M * M^T` with `M = ObjectToWorld * R * S`.
2. Transform it into view space and project it with the Jacobian of the perspective projection at the splat's
   position. The result is a 2x2 covariance in pixels squared.
3. Optionally add a low-pass filter to the diagonal (the classic 0.3 px "dilation"; off by default to match
   Spark) and compensate opacity for it when the scene says it was trained that way.
4. Take the eigenvectors and eigenvalues of the 2x2 matrix: those are the ellipse axes. The quad reaches
   `maxStdDev` standard deviations along each axis (2.83 by default, 2.24 is indistinguishable and cheaper).
5. Place the four quad corners in clip space and pass the corner position in standard-deviation units to the
   fragment shader, which evaluates `exp(-0.5 * d^2)`, multiplies by the splat's opacity and writes premultiplied
   color. Blending is `One, OneMinusSrcAlpha`, depth test on, depth write off.

Colors are treated as sRGB, because that is how scenes are trained. In a linear-color Unity project they are
converted to linear before blending, otherwise the display would apply gamma twice. Spark blends the sRGB values
directly; the difference is small and only shows where semi-transparent splats overlap.

## Fitting into URP

`GaussianSplatRendererFeature` adds two RenderGraph passes after the skybox and before URP's transparent
objects: one that records the compute sort, one that draws every renderer into the camera color target with the
camera depth bound for testing. Splats therefore hide correctly behind walls and floors made of ordinary meshes,
while particles and other transparent materials end up on top of them. Several renderers are drawn far to near.
Compatibility Mode (RenderGraph off) is handled by a plain command buffer path.

`Runtime/Rendering/`

## Loading worlds progressively

A world descriptor is a small JSON that lists quality levels (URLs plus splat counts), an optional collider mesh,
where the camera starts, the axis convention and a units-to-meters scale. `WorldLoader` downloads the smallest
level, shows it, downloads the best level the device's profile allows, crossfades over three seconds and drops
the small one. Network errors retry with a backoff. A low-memory warning falls back to the small level, which is
kept in memory for exactly that purpose.

`SplatQualityProfile` picks the caps per device (phones: 500k splats, sqrt(5), no SH, a 1 px minimum splat
size). `SplatQualityController` watches the 95th-percentile frame time and steps quality down a ladder: render
scale, then splat reach, then SH.

`Runtime/World/`

## Numbers

Measured on an Apple Silicon Mac in the editor, 1080x1920, camera inside the scene, default settings:

| Scene | Splats | ms per frame |
|---|---|---|
| Niantic horned lizard | 786k | 6.1 |
| Niantic racoon family | 933k | 4.8 |
| InnerTest study room | 500k | 4.7 (720x1280) |

Against Spark from the same camera pose, the two Niantic scenes reach 37.9 and 37.1 dB PSNR.

## Testing

EditMode tests cover the formats (round trips, corrupted inputs, quantization), the packing, the axis
conversion, the sort key math and the descriptors. PlayMode tests upload real data, run both sorters and check
the order is back to front, compare the HLSL unpack with the C# one, render a single splat and verify its
measured radius against the analytic value, render scenes and compare them against committed reference images
per graphics API, and time the real scenes. `Window > General > Test Runner` runs all of it.

## Known limits

- SPZ version 4 (zstd) is recognized but not decoded.
- Splats are always under URP's transparent objects.
- The GPU layout stores rotation with 8 bits per component; thin splats in a version-3 SPZ lose a little
  precision. The scenes tested so far were version 2, which stores 8 bits itself.
- No level-of-detail: a full-resolution scene is either loaded or not.
- Verified on Metal only so far. Android and WebGL builds run, but their frame times are not measured yet.

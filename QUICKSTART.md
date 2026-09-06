# Quick start

## See something

1. Clone the repo and open it in Unity 6000.3.19f1. The first import takes a couple of minutes.
2. Open `Assets/Scenes/NianticSamples.unity` and press Play.
3. Hold the right mouse button to look around, WASD to fly, Shift to go faster, R to reset.
   F3 toggles the stats overlay.

FPS in the editor is misleading while the Scene View is visible: it renders the splats a second
time from its own camera. Collapse it or switch to Game View.

## Install the package into your own project

The package is a normal UPM package. In Package Manager choose "Install package from git URL" and paste:

```
https://github.com/denisislamov/GaussPlatUnity.git?path=/Packages/com.denisislamov.gsplat#v0.2.1
```

Change the tag at the end to the version you want (or use `#main`). Unity clones the whole repository for this, and
it is a heavy one because of the sample worlds, so the first install takes a minute.

Then add the renderer feature (menu GSplat, Setup, Add Renderer Feature to URP Renderers) and, if you want something
to look at right away, import a sample from the package's Samples tab: "Viewer Scene" is the ready-made viewer,
"Study Room 100k" is a small generated room already imported as an asset.

## Use your own file

Three generated test worlds live in `Assets/Samples/InnerTest/` (four SPZ levels and a collider each), with one scene per
world in `Assets/Scenes/InnerTest/`. `GSplat > Setup > Create InnerTest Scenes` rebuilds those scenes.

1. Drop an `.spz` or `.ply` into `Assets/`. Select it: the import settings are on the file.
   The one that matters is **Source Coordinate System**. If the scene comes out mirrored or upside
   down, try another value and press Apply. RUB is right for Niantic and three.js files, RDF for
   PLY from the original 3DGS code, LDF for the InnerTest worlds in this repo.
2. Create an empty GameObject, add **GSplat > Gaussian Splat Renderer**, assign the asset.

That is all. It renders in Scene and Game view next to your meshes.

If nothing shows up, check that the URP renderer has the "Gaussian Splats" feature.
`GSplat > Setup > Add Renderer Feature to URP Renderers` adds it to every renderer in the project.

## Settings worth knowing

On the renderer component:

- **Min Pixel Radius**: splats smaller than this on screen are skipped. 0.5 is the default.
  This is the knob that saves the most time on phones; going to 1.0 roughly halves the frame cost
  on a dense scene and is hard to notice.
- **Max Std Dev**: how far each splat quad reaches. 2.83 is the classic value, 2.24 looks the same
  and is cheaper.
- **Sh Degree**: view-dependent color. 0 on phones.
- **Sorter Kind**: Auto picks the GPU sorter when compute shaders exist.
- **Dilation** and **Max Pixel Radius** default to what the Spark web viewer uses (0 and 512). With these and
  SH 3 the horned lizard sample matches Spark at 37.7 dB PSNR from the same camera.

## The viewer

`GSplat > Setup > Create Viewer Scene` makes a scene with a `WorldLoader`. Put a URL into
**World Url** on the World object and press Play. It accepts a direct `.spz`/`.ply` URL or a small
JSON that lists several quality levels:

```json
{
  "coordinateSystem": "Rub",
  "levels": [
    { "url": "https://host/world_150k.spz", "splatCount": 150000 },
    { "url": "https://host/world_500k.spz", "splatCount": 500000 }
  ],
  "colliderUrl": "https://host/world_collider.glb",
  "spawn": { "position": [0, 1.6, -3], "rotationEuler": [0, 0, 0] }
}
```

The loader shows the first level as soon as it arrives, then fades in the best level the device
can afford. With a collider the camera walks; without one it flies. `file://` URLs work for local
testing; for a web build the server needs CORS headers.

## Building

`GSplat > Build` has entries for Android, WebGL and iOS. Output goes to `Builds/`. The web build
reads `?world=<url>` from the page address.

## Tests

`Window > General > Test Runner`, both EditMode and PlayMode. The PlayMode set includes image
comparisons against PNGs in `Packages/com.denisislamov.gsplat/Tests/Runtime/GoldenImages/`.
On a GPU that has no reference images yet the first run writes them and reports the tests as
inconclusive; look at the images and commit them.

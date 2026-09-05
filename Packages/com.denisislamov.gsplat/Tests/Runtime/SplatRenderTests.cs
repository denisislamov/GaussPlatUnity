using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>End to end: data -> renderer component -> URP feature -> pixels. Checks placement, color and blending, not exact images (see GoldenImageTests).</summary>
    public sealed class SplatRenderTests
    {
        private const int Size = 256;

        private static GsplatData SingleSplat(float3 position, float3 logScale, float3 displayColor, float alpha)
        {
            var cloud = new SplatCloud(1, 0, false, Allocator.Persistent);
            try
            {
                cloud.Positions[0] = position;
                cloud.LogScales[0] = logScale;
                cloud.Rotations[0] = new float4(0f, 0f, 0f, 1f);
                cloud.Alphas[0] = alpha;
                // Display color = 0.5 + Sh0Scale * c  ->  c = (display - 0.5) / Sh0Scale
                cloud.Colors[0] = (displayColor - 0.5f) / ShMath.Sh0Scale;
                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f };
                return GsplatBuilder.Build(cloud, options);
            }
            finally
            {
                cloud.Dispose();
            }
        }

        public static IEnumerator Render(Camera camera, RenderTexture target, GaussianSplatRenderer renderer, SplatSorterKind sorterKind)
        {
            camera.targetTexture = target;
            // The CPU sorter delivers its first order a frame later; two frames cover both sorters.
            for (int frame = 0; frame < 3; frame++)
            {
                camera.Render();
                yield return null;
            }
        }

        public static Color32[] ReadPixels(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        [UnityTest]
        public IEnumerator SingleSplatCoversTheCenterAndNotTheCorners([Values(SplatSorterKind.Gpu, SplatSorterKind.Cpu)] SplatSorterKind sorterKind)
        {
            if (sorterKind == SplatSorterKind.Gpu && !GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("test camera");
                var splatObject = new GameObject("test splats");
                var target = new RenderTexture(Size, Size, 24, GraphicsFormat.R8G8B8A8_UNorm);
                GsplatData data = SingleSplat(float3.zero, new float3(math.log(0.5f)), new float3(1f, 0f, 0f), 1f);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.transform.position = new Vector3(0f, 0f, -3f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.fieldOfView = 60f;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetSorterKind(sorterKind);
                    renderer.ConvertSrgbToLinear = false;
                    renderer.SetData(data, true);

                    yield return Render(camera, target, renderer, sorterKind);

                    Color32[] pixels = ReadPixels(target);
                    Color32 center = pixels[Size / 2 * Size + Size / 2];
                    Color32 corner = pixels[3 * Size + 3];
                    Assert.That(center.r, Is.GreaterThan(150), "center should be red: " + center);
                    Assert.That(center.g, Is.LessThan(40), "center should not be green: " + center);
                    Assert.That(corner.r, Is.LessThan(10), "corner should stay background: " + corner);
                    Assert.AreEqual(1, renderer.LastDrawnSplatCount);
                }
                finally
                {
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
        }

        [UnityTest]
        public IEnumerator OpaqueGeometryInFrontHidesTheSplat()
        {
            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("test camera");
                var splatObject = new GameObject("test splats");
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var target = new RenderTexture(Size, Size, 24, GraphicsFormat.R8G8B8A8_UNorm);
                GsplatData data = SingleSplat(float3.zero, new float3(math.log(0.5f)), new float3(1f, 0f, 0f), 1f);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.transform.position = new Vector3(0f, 0f, -3f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    // An opaque green quad between the camera and the splat, covering the center of the screen.
                    wall.transform.position = new Vector3(0f, 0f, -1.5f);
                    wall.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                    var wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.green };
                    wall.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.ConvertSrgbToLinear = false;
                    renderer.SetData(data, true);

                    yield return Render(camera, target, renderer, SplatSorterKind.Auto);

                    Color32[] pixels = ReadPixels(target);
                    Color32 center = pixels[Size / 2 * Size + Size / 2];
                    Assert.That(center.g, Is.GreaterThan(150), "the wall must win the depth test at the center: " + center);
                    Assert.That(center.r, Is.LessThan(40), "no red splat through the wall: " + center);

                    // Just outside the wall the splat's soft edge is still visible.
                    Color32 beside = pixels[Size / 2 * Size + (int)(Size * 0.86f)];
                    Assert.That(beside.r, Is.GreaterThan(beside.g), "splat visible beside the wall: " + beside);

                    Object.DestroyImmediate(wallMaterial);
                }
                finally
                {
                    Object.DestroyImmediate(wall);
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
        }
    }
}

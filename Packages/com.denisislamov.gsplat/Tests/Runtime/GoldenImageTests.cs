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
    /// <summary>
    /// Fixed scenes rendered at a fixed camera and compared to committed PNGs (TZ E11-T2). They catch regressions in
    /// projection, sorting order, color handling and URP compositing. Same-machine only: a different GPU makes its own
    /// baseline folder.
    /// </summary>
    public sealed class GoldenImageTests
    {
        private const int Size = 256;

        /// <summary>A deterministic cloud of ellipsoids with varied colors, sizes and rotations around the origin.</summary>
        private static GsplatData ReferenceScene(int shDegree)
        {
            var random = new Unity.Mathematics.Random(2024);
            const int count = 400;
            var cloud = new SplatCloud(count, shDegree, false, Allocator.Persistent);
            try
            {
                for (int splatIndex = 0; splatIndex < count; splatIndex++)
                {
                    cloud.Positions[splatIndex] = random.NextFloat3(-1.5f, 1.5f);
                    cloud.LogScales[splatIndex] = math.log(random.NextFloat3(0.05f, 0.4f));
                    cloud.Rotations[splatIndex] = math.normalize(random.NextFloat4(-1f, 1f));
                    cloud.Alphas[splatIndex] = random.NextFloat(0.3f, 1f);
                    cloud.Colors[splatIndex] = (random.NextFloat3(0f, 1f) - 0.5f) / ShMath.Sh0Scale;
                }

                for (int floatIndex = 0; floatIndex < cloud.Sh.Length; floatIndex++) cloud.Sh[floatIndex] = random.NextFloat(-0.3f, 0.3f);

                var options = new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f, TargetShDegree = shDegree };
                return GsplatBuilder.Build(cloud, options);
            }
            finally
            {
                cloud.Dispose();
            }
        }

        private static IEnumerator RenderAndCompare(string name, int shDegree, SplatSorterKind sorterKind, float maxStdDev, bool withOpaqueCube, System.Action<GaussianSplatRenderer> configure = null)
        {
            if (sorterKind == SplatSorterKind.Gpu && !GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("golden camera");
                var splatObject = new GameObject("golden splats");
                GameObject cube = withOpaqueCube ? GameObject.CreatePrimitive(PrimitiveType.Cube) : null;
                var target = new RenderTexture(Size, Size, 24, GraphicsFormat.R8G8B8A8_UNorm);
                Texture2D capture = null;
                Material cubeMaterial = null;
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.transform.position = new Vector3(0.5f, 0.8f, -4f);
                    camera.transform.LookAt(Vector3.zero);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
                    camera.fieldOfView = 50f;
                    camera.targetTexture = target;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    if (cube != null)
                    {
                        cube.transform.position = new Vector3(0.6f, -0.2f, 0f);
                        cube.transform.localScale = Vector3.one * 0.9f;
                        cubeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.2f, 0.7f, 0.3f) };
                        cube.GetComponent<MeshRenderer>().sharedMaterial = cubeMaterial;
                    }

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetSorterKind(sorterKind);
                    renderer.MaxStdDev = maxStdDev;
                    renderer.ShDegree = shDegree;
                    configure?.Invoke(renderer);
                    renderer.SetData(ReferenceScene(shDegree), true);

                    for (int frame = 0; frame < 3; frame++)
                    {
                        camera.Render();
                        yield return null;
                    }

                    capture = GoldenImage.Capture(target);
                    GoldenImage.Assert(capture, name);
                }
                finally
                {
                    if (capture != null) Object.DestroyImmediate(capture);
                    if (cubeMaterial != null) Object.DestroyImmediate(cubeMaterial);
                    if (cube != null) Object.DestroyImmediate(cube);
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
        }

        [UnityTest]
        public IEnumerator Reference400Splats_GpuSort()
        {
            yield return RenderAndCompare("reference400_gpu", 0, SplatSorterKind.Gpu, GaussianSplatRenderer.DefaultMaxStdDev, false);
        }

        [UnityTest]
        public IEnumerator Reference400Splats_CpuSort()
        {
            yield return RenderAndCompare("reference400_cpu", 0, SplatSorterKind.Cpu, GaussianSplatRenderer.DefaultMaxStdDev, false);
        }

        [UnityTest]
        public IEnumerator Reference400Splats_WithOpaqueCube()
        {
            yield return RenderAndCompare("reference400_cube", 0, SplatSorterKind.Auto, GaussianSplatRenderer.DefaultMaxStdDev, true);
        }

        [UnityTest]
        public IEnumerator Reference400Splats_Sh3()
        {
            yield return RenderAndCompare("reference400_sh3", 3, SplatSorterKind.Auto, GaussianSplatRenderer.DefaultMaxStdDev, false);
        }

        [UnityTest]
        public IEnumerator Reference400Splats_MaxStdDevSqrt5()
        {
            yield return RenderAndCompare("reference400_sqrt5", 0, SplatSorterKind.Auto, 2.236f, false);
        }

        [UnityTest]
        public IEnumerator Reference400Splats_ChunkDebugView()
        {
            yield return RenderAndCompare("reference400_debug_chunks", 0, SplatSorterKind.Auto, GaussianSplatRenderer.DefaultMaxStdDev, false, r => r.DebugMode = SplatDebugMode.ChunkColors);
        }

        [UnityTest]
        public IEnumerator GpuAndCpuSortersRenderTheSameImage()
        {
            // The two sorters must agree to within blending noise: equal keys may land in a different order, which
            // changes a few pixels a little, not the picture.
            if (!GpuCountingSorter.IsSupported) Assert.Ignore("No compute shaders on this device.");
            yield return RenderAndCompare("reference400_gpu", 0, SplatSorterKind.Cpu, GaussianSplatRenderer.DefaultMaxStdDev, false);
        }
    }
}

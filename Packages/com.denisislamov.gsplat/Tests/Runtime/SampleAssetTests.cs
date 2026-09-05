using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// Smoke test on a real capture: the Niantic sample .spz files (MIT) in Assets/Samples/Niantic. Skipped when the
    /// folder is not there (the package can live in other projects) or outside the editor.
    /// </summary>
    public sealed class SampleAssetTests
    {
        private const string SamplePath = "Assets/Samples/Niantic/hornedlizard.spz";

        [UnityTest]
        public IEnumerator HornedLizardImportsAndRendersSomething()
        {
#if UNITY_EDITOR
            if (!File.Exists(SamplePath)) Assert.Ignore("Sample file not present: " + SamplePath);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(SamplePath);
            Assert.IsNotNull(asset, "the .spz importer produced no asset");
            Assert.That(asset.SplatCount, Is.GreaterThan(100000), "the lizard has hundreds of thousands of splats");
            Assert.That(asset.SplatCount, Is.LessThanOrEqualTo(asset.SourceSplatCount));

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("sample camera");
                var splatObject = new GameObject("sample splats");
                var target = new RenderTexture(640, 480, 24, GraphicsFormat.R8G8B8A8_UNorm);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.targetTexture = target;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.SetData(asset.LoadData(), true);
                    Bounds bounds = renderer.WorldBounds;
                    camera.transform.position = bounds.center - Vector3.forward * bounds.size.magnitude;
                    camera.transform.LookAt(bounds.center);

                    // Upload is 2 chunks per frame; wait for all of it plus a frame for the CPU sorter if used.
                    for (int frame = 0; frame < 12; frame++)
                    {
                        camera.Render();
                        yield return null;
                    }

                    Assert.IsTrue(renderer.Gpu.IsFullyUploaded, "upload did not finish in 12 frames");
                    Assert.That(renderer.LastDrawnSplatCount, Is.GreaterThan(1000), "almost nothing was drawn");

                    // Keep a picture for the human: TestResults/ is ignored by git.
                    Texture2D capture = GoldenImage.Capture(target);
                    System.IO.Directory.CreateDirectory("TestResults");
                    File.WriteAllBytes("TestResults/hornedlizard.png", capture.EncodeToPNG());
                    Object.DestroyImmediate(capture);

                    Color32[] pixels = SplatRenderTests.ReadPixels(target);
                    int lit = 0;
                    foreach (Color32 pixel in pixels)
                    {
                        if (pixel.r + pixel.g + pixel.b > 30) lit++;
                    }

                    Assert.That(lit, Is.GreaterThan(pixels.Length / 20), "less than 5% of the image shows the scene");
                }
                finally
                {
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
#else
            Assert.Ignore("Editor only.");
            yield break;
#endif
        }
    }
}

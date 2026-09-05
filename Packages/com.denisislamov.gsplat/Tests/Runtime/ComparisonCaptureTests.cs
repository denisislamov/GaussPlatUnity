using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace GSplat.Tests
{
    /// <summary>
    /// Not a pass/fail test: renders the Niantic lizard from a fixed pose into TestResults/compare so the frames can
    /// be put next to a reference viewer (Spark) at the same pose. Writes both color modes of TZ E3-T5.
    /// The pose is also written in three.js (RUB) coordinates: our import negates Z, so x, y stay and z flips.
    /// </summary>
    public sealed class ComparisonCaptureTests
    {
        private const string SamplePath = "Assets/Samples/Niantic/hornedlizard.spz";
        private const int Width = 540;
        private const int Height = 960;

        [UnityTest]
        public IEnumerator CaptureLizardForSparkComparison()
        {
#if UNITY_EDITOR
            if (!File.Exists(SamplePath)) Assert.Ignore("Sample file not present: " + SamplePath);
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(SamplePath);
            Assert.IsNotNull(asset);
            Directory.CreateDirectory("TestResults/compare");

            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("compare camera");
                var splatObject = new GameObject("compare splats");
                var target = new RenderTexture(Width, Height, 24, GraphicsFormat.R8G8B8A8_UNorm);
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.targetTexture = target;
                    camera.fieldOfView = 60f;
                    camera.nearClipPlane = 0.05f;
                    camera.farClipPlane = 1000f;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.ShDegree = 3;
                    renderer.MaxStdDev = GaussianSplatRenderer.DefaultMaxStdDev;
                    renderer.MinPixelRadius = 0f; // draw everything: the comparison must not hide our own culling
                    renderer.SetData(asset.LoadData(), true);

                    Bounds bounds = renderer.WorldBounds;
                    // Same pose as the sample scene: inside the capture, slightly above the center, looking +Z.
                    Vector3 position = bounds.center + new Vector3(0f, 0.3f, -bounds.size.z * 0.05f);
                    camera.transform.SetPositionAndRotation(position, Quaternion.identity);

                    string pose = string.Format(CultureInfo.InvariantCulture,
                        "{{ \"unity\": {{ \"position\": [{0}, {1}, {2}], \"lookDirection\": [0, 0, 1], \"fovVertical\": 60, \"width\": {3}, \"height\": {4} }},\n  \"threejs_rub\": {{ \"position\": [{0}, {1}, {5}], \"lookDirection\": [0, 0, -1], \"fovVertical\": 60 }} }}\n",
                        position.x, position.y, position.z, Width, Height, -position.z);
                    File.WriteAllText("TestResults/compare/pose.json", pose);

                    while (!renderer.Gpu.IsFullyUploaded)
                    {
                        camera.Render();
                        yield return null;
                    }

                    // (name, sRGB->linear conversion, SH degree, dilation, max pixel radius)
                    var variants = new (string, bool, int, float, float)[]
                    {
                        ("gamma_sh3_dil0_max0", false, 3, 0f, 0f),
                        ("gamma_sh3_dil0_max512", false, 3, 0f, 512f),
                        ("gamma_sh3_dil0_max256", false, 3, 0f, 256f),
                        ("gamma_sh3_dil03_max512", false, 3, 0.3f, 512f),
                        ("linear_sh3_dil0_max512", true, 3, 0f, 512f)
                    };
                    foreach ((string name, bool linear, int shDegree, float dilation, float maxRadius) in variants)
                    {
                        renderer.ConvertSrgbToLinear = linear;
                        renderer.ShDegree = shDegree;
                        renderer.Dilation = dilation;
                        renderer.MaxPixelRadius = maxRadius;
                        for (int frame = 0; frame < 4; frame++)
                        {
                            camera.Render();
                            yield return null;
                        }

                        Texture2D capture = GoldenImage.Capture(target);
                        File.WriteAllBytes($"TestResults/compare/unity_{name}.png", capture.EncodeToPNG());
                        Object.DestroyImmediate(capture);
                    }

                    Debug.Log($"GSplat compare: asset SH degree {asset.ShDegree}");

                    Debug.Log("GSplat compare: frames written to TestResults/compare, pose " + pose);
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

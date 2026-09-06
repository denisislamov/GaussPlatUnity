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
    /// Renders each InnerTest world (Assets/Samples/InnerTest, git-ignored) from inside, four directions, into
    /// TestResults/innertest/&lt;world&gt;_&lt;dir&gt;.png, and logs the frame time. A human looks at the pictures to check the
    /// axis convention (mirrored / upside down) and the look; the test itself only checks that something is drawn.
    /// </summary>
    public sealed class InnerTestSamplesCaptureTests
    {
        private const string Folder = "Assets/Samples/InnerTest";

        [UnityTest]
        public IEnumerator CaptureEveryInnerTestWorld()
        {
#if UNITY_EDITOR
            if (!Directory.Exists(Folder)) Assert.Ignore("No InnerTest samples folder.");
            string[] worlds = Directory.GetDirectories(Folder);
            if (worlds.Length == 0) Assert.Ignore("No InnerTest worlds downloaded.");
            Directory.CreateDirectory("TestResults/innertest");

            using (new UrpTestPipeline())
            {
                foreach (string worldFolder in worlds)
                {
                    string name = Path.GetFileName(worldFolder);
                    string assetPath = $"{worldFolder}/{name}_500k.spz".Replace('\\', '/');
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
                    if (asset == null)
                    {
                        Debug.LogWarning("GSplat innertest: no asset for " + assetPath);
                        continue;
                    }

                    var cameraObject = new GameObject("innertest camera");
                    var splatObject = new GameObject(name);
                    var target = new RenderTexture(720, 1280, 24, GraphicsFormat.R8G8B8A8_UNorm);
                    try
                    {
                        Camera camera = cameraObject.AddComponent<Camera>();
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        camera.backgroundColor = Color.black;
                        camera.fieldOfView = 70f;
                        camera.nearClipPlane = 0.05f;
                        camera.farClipPlane = 1000f;
                        camera.targetTexture = target;
                        cameraObject.AddComponent<UniversalAdditionalCameraData>();

                        var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                        renderer.SetData(asset.LoadData(), true);
                        Bounds bounds = renderer.WorldBounds;
                        // InnerTest generates the world around the origin: that is the intended viewpoint.
                        camera.transform.position = Vector3.zero;

                        while (!renderer.Gpu.IsFullyUploaded)
                        {
                            camera.Render();
                            yield return null;
                        }

                        string[] directions = { "front", "right", "back", "left" };
                        float totalMs = 0f;
                        int frames = 0;
                        for (int direction = 0; direction < 4; direction++)
                        {
                            camera.transform.rotation = Quaternion.Euler(0f, direction * 90f, 0f);
                            for (int frame = 0; frame < 4; frame++)
                            {
                                float start = Time.realtimeSinceStartup;
                                camera.Render();
                                UnityEngine.Rendering.AsyncGPUReadback.Request(target).WaitForCompletion();
                                if (frame > 0) { totalMs += (Time.realtimeSinceStartup - start) * 1000f; frames++; }
                                yield return null;
                            }

                            Texture2D capture = GoldenImage.Capture(target);
                            File.WriteAllBytes($"TestResults/innertest/{name}_{directions[direction]}.png", capture.EncodeToPNG());
                            Object.DestroyImmediate(capture);
                        }

                        Debug.Log($"GSplat innertest: {name}: {asset.SplatCount:N0} splats (source {asset.SourceSplatCount:N0}), SH {asset.ShDegree}, antialiased {asset.Antialiased}, bounds {bounds.size}, drawn {renderer.LastDrawnSplatCount:N0}, {totalMs / frames:F1} ms per frame at 720x1280");
                        Assert.That(renderer.LastDrawnSplatCount, Is.GreaterThan(1000), name + ": nothing drawn");
                    }
                    finally
                    {
                        Object.DestroyImmediate(splatObject);
                        Object.DestroyImmediate(cameraObject);
                        target.Release();
                    }
                }
            }
#else
            Assert.Ignore("Editor only.");
            yield break;
#endif
        }
    }
}

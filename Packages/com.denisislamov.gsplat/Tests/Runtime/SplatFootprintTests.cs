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
    /// The projected size of a splat must match the EWA math exactly: a splat with half-axis s at distance d through a
    /// camera with focal length f pixels has a 1-sigma radius of s * f / d pixels, and its alpha falls to exp(-0.5)
    /// of the center there. Anything else means every splat in a scene is drawn too big or too small.
    /// </summary>
    public sealed class SplatFootprintTests
    {
        private const int Size = 512;

        [UnityTest]
        public IEnumerator OneSigmaRadiusMatchesTheProjection()
        {
            using (new UrpTestPipeline())
            {
                var cameraObject = new GameObject("footprint camera");
                var splatObject = new GameObject("footprint splat");
                var target = new RenderTexture(Size, Size, 24, GraphicsFormat.R8G8B8A8_UNorm);
                GsplatData data = null;
                try
                {
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.transform.position = new Vector3(0f, 0f, -3f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.fieldOfView = 60f;
                    camera.targetTexture = target;
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();

                    const float halfAxis = 0.5f;
                    var cloud = new SplatCloud(1, 0, false, Allocator.Persistent);
                    cloud.Positions[0] = float3.zero;
                    cloud.LogScales[0] = new float3(math.log(halfAxis));
                    cloud.Rotations[0] = new float4(0f, 0f, 0f, 1f);
                    cloud.Alphas[0] = 1f;
                    cloud.Colors[0] = (new float3(1f, 1f, 1f) - 0.5f) / ShMath.Sh0Scale; // white
                    data = GsplatBuilder.Build(cloud, new SplatImportOptions { SourceCoordinateSystem = SplatCoordinateSystem.Ruf, PruneAlphaBelow = 0f });
                    cloud.Dispose();

                    var renderer = splatObject.AddComponent<GaussianSplatRenderer>();
                    renderer.ConvertSrgbToLinear = false;
                    renderer.Dilation = 0f;
                    renderer.MinPixelRadius = 0f;
                    renderer.MaxStdDev = 4f;
                    renderer.SetData(data, true);

                    for (int frame = 0; frame < 3; frame++)
                    {
                        camera.Render();
                        yield return null;
                    }

                    Color32[] pixels = SplatRenderTests.ReadPixels(target);
                    int center = Size / 2;
                    float centerValue = pixels[center * Size + center].r;
                    Assert.That(centerValue, Is.GreaterThan(240), "center should be fully white: " + centerValue);

                    // Walk right from the center until the value drops below exp(-0.5) of the center.
                    float threshold = centerValue * Mathf.Exp(-0.5f);
                    int radius = 0;
                    for (int x = center; x < Size; x++)
                    {
                        if (pixels[center * Size + x].r < threshold) { radius = x - center; break; }
                    }

                    float focal = Size * 0.5f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    float expected = halfAxis * focal / 3f;
                    Debug.Log($"GSplat footprint: measured 1-sigma radius {radius} px, expected {expected:F1} px (focal {focal:F1})");
                    Assert.That(radius, Is.InRange(expected * 0.9f, expected * 1.1f), $"1-sigma radius {radius} px, expected {expected:F1}");
                }
                finally
                {
                    Object.DestroyImmediate(splatObject);
                    Object.DestroyImmediate(cameraObject);
                    target.Release();
                }
            }
        }
    }
}

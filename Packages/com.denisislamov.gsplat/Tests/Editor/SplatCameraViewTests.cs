using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace GSplat.Tests
{
    public sealed class SplatCameraViewTests
    {
        [Test]
        public void ViewIsPlainDataSoItCanSitInsideAJob()
        {
            // The Burst KeyJob carries the view by value; a managed field would make the job fail to compile at runtime.
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<SplatCameraView>());
        }

        [Test]
        public void VisibilityReadsTheViewFields()
        {
            // A splat straight ahead at depth 10 with a 1 m scale projects to 3 sigma x 1 m x 500 px / 10 = 150 px: visible.
            var view = new SplatCameraView
            {
                CullInKeys = true,
                LocalToClip = PerspectiveLookingDownZ(),
                FocalPixelsY = 500f,
                ScreenSize = new float2(1000f, 1000f),
                MaxStdDev = 3f,
                MinPixelRadius = 0.5f
            };
            Assert.IsTrue(SplatVisibility.IsVisible(new float3(0f, 0f, 10f), new float3(1f), view));

            // Behind the camera: never visible.
            Assert.IsFalse(SplatVisibility.IsVisible(new float3(0f, 0f, -10f), new float3(1f), view));

            // Sub-pixel: 3 x 0.001 m x 500 / 10 = 0.15 px < 0.5.
            Assert.IsFalse(SplatVisibility.IsVisible(new float3(0f, 0f, 10f), new float3(0.001f), view));

            // Off screen by center but huge: still visible, its quad reaches into the frame.
            Assert.IsTrue(SplatVisibility.IsVisible(new float3(15f, 0f, 10f), new float3(5f), view));
            Assert.IsFalse(SplatVisibility.IsVisible(new float3(15f, 0f, 10f), new float3(0.01f), view));
        }

        /// <summary>A symmetric perspective projection with focal length 1 (90 degree field of view) looking down +Z.</summary>
        private static float4x4 PerspectiveLookingDownZ()
        {
            return new float4x4(
                new float4(1f, 0f, 0f, 0f),
                new float4(0f, 1f, 0f, 0f),
                new float4(0f, 0f, 1f, 1f),
                new float4(0f, 0f, 0f, 0f));
        }
    }
}

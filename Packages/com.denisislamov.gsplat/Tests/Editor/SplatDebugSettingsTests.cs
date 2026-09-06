using NUnit.Framework;
using UnityEngine;

namespace GSplat.Tests
{
    public sealed class SplatDebugSettingsTests
    {
        [Test]
        public void JsonRoundTripKeepsEveryKnob()
        {
            var settings = new SplatDebugSettings
            {
                SorterKind = SplatSorterKind.Cpu,
                VerticesPerSplat = 3,
                MinPixelRadius = 1.5f,
                MaxStdDev = 2.236f,
                ShDegree = 1,
                RenderScale = 0.7f,
                ChunkBudgetSplatsPerPixel = 0.5f,
                ChunkBudgetFloor = 123,
                CheapGaussian = true,
                ClipLowAlpha = false,
                SortKeyBits = 12,
                TimeSlicedCpuSort = true,
                CpuSortSlotsPerFrame = 65536,
                SortMoveThreshold = 0.1f,
                SortAngleThreshold = 3f,
                QualityControllerEnabled = false,
                PrimitivesFirstLadder = true,
                StepUpWhenFast = true,
                StagedBuild = false,
                ShowOverlay = false
            };

            var copy = JsonUtility.FromJson<SplatDebugSettings>(JsonUtility.ToJson(settings));
            Assert.AreEqual(JsonUtility.ToJson(settings), JsonUtility.ToJson(copy));
            Assert.AreEqual(3, copy.VerticesPerSplat);
            Assert.AreEqual(12, copy.SortKeyBits);
            Assert.IsTrue(copy.PrimitivesFirstLadder);
        }

        [Test]
        public void ProfileSetsOnlyTheRenderKnobs()
        {
            var settings = new SplatDebugSettings();
            settings.InitializeFrom(SplatQualityProfile.Mobile());
            Assert.AreEqual(1f, settings.MinPixelRadius);
            Assert.AreEqual(2.236f, settings.MaxStdDev, 1e-6f);
            Assert.AreEqual(0, settings.ShDegree);
            Assert.AreEqual(4, settings.VerticesPerSplat, "the profile does not touch the experiments");
            Assert.AreEqual(16, settings.SortKeyBits);
        }

        [Test]
        public void KeyWidthHelpersAgree()
        {
            Assert.AreEqual(65536, SplatSortKeys.BucketCountFor(16));
            Assert.AreEqual(4096, SplatSortKeys.BucketCountFor(12));
            Assert.AreEqual(4095u, SplatSortKeys.MaxKeyFor(12));
            Assert.AreEqual(SplatSortKeys.MaxKey, SplatSortKeys.MaxKeyFor(16));
            // The narrow key still puts the nearest splat at 0 and the farthest at the top.
            SplatSortKeys.LogRange(1f, 100f, out float logMin, out float inverse);
            Assert.AreEqual(4095u, SplatSortKeys.DepthToKey(1f, logMin, inverse, 4095u), "farthest key is the largest bucket... nearest depth maps to max");
            Assert.AreEqual(0u, SplatSortKeys.DepthToKey(100f, logMin, inverse, 4095u));
        }
    }
}

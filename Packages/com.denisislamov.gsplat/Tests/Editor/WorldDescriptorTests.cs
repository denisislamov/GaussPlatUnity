using NUnit.Framework;

namespace GSplat.Tests
{
    public sealed class WorldDescriptorTests
    {
        private const string Sample = @"{
          ""name"": ""White Grecian City"",
          ""coordinateSystem"": ""Rub"",
          ""levels"": [
            { ""url"": ""https://cdn/x/full.spz"", ""splatCount"": 1500000, ""bytes"": 30800000, ""shDegree"": 3 },
            { ""url"": ""https://cdn/x/150k.spz"", ""splatCount"": 150000, ""bytes"": 2400000, ""shDegree"": 0 },
            { ""url"": ""https://cdn/x/500k.spz"", ""splatCount"": 500000, ""bytes"": 8040000, ""shDegree"": 0 }
          ],
          ""colliderUrl"": ""https://cdn/x/collider.glb"",
          ""spawn"": { ""position"": [1, 1.6, -2], ""rotationEuler"": [0, 90, 0] }
        }";

        [Test]
        public void ParsesAndSortsLevelsBySize()
        {
            WorldDescriptor descriptor = WorldDescriptor.Parse(Sample);
            Assert.AreEqual("White Grecian City", descriptor.name);
            Assert.AreEqual(3, descriptor.levels.Count);
            Assert.AreEqual(150000, descriptor.levels[0].splatCount);
            Assert.AreEqual(500000, descriptor.levels[1].splatCount);
            Assert.AreEqual(1500000, descriptor.levels[2].splatCount);
            Assert.AreEqual(SplatCoordinateSystem.Rub, descriptor.CoordinateSystem);
            Assert.IsTrue(descriptor.HasCollider);
            Assert.AreEqual(1.6f, descriptor.spawn.Position.y, 1e-5f);
            Assert.AreEqual(90f, descriptor.spawn.Rotation.eulerAngles.y, 1e-3f);
        }

        [Test]
        public void MobileProfileStopsAt500kAndDesktopTakesEverything()
        {
            WorldDescriptor descriptor = WorldDescriptor.Parse(Sample);
            Assert.AreEqual(150000, descriptor.FirstLevel(SplatQualityProfile.Mobile()).splatCount);
            Assert.AreEqual(500000, descriptor.FinalLevel(SplatQualityProfile.Mobile()).splatCount);
            Assert.AreEqual(1500000, descriptor.FinalLevel(SplatQualityProfile.Desktop()).splatCount);
        }

        [Test]
        public void ProfileBelowTheSmallestLevelStillGetsTheSmallest()
        {
            WorldDescriptor descriptor = WorldDescriptor.Parse(Sample);
            var tiny = new SplatQualityProfile { MaxSplatCount = 1000 };
            Assert.AreEqual(150000, descriptor.FinalLevel(tiny).splatCount);
        }

        [Test]
        public void EmptyAndBrokenJsonAreTyped()
        {
            Assert.AreEqual(WorldDescriptorError.EmptyPayload, Assert.Throws<WorldDescriptorException>(() => WorldDescriptor.Parse("")).Code);
            Assert.AreEqual(WorldDescriptorError.MalformedJson, Assert.Throws<WorldDescriptorException>(() => WorldDescriptor.Parse("{ not json")).Code);
            Assert.AreEqual(WorldDescriptorError.NoLevels, Assert.Throws<WorldDescriptorException>(() => WorldDescriptor.Parse("{ \"name\": \"x\" }")).Code);
            Assert.AreEqual(WorldDescriptorError.InvalidLevel, Assert.Throws<WorldDescriptorException>(() => WorldDescriptor.Parse("{ \"levels\": [ { \"url\": \"\" } ] }")).Code);
            Assert.AreEqual(WorldDescriptorError.InvalidCoordinateSystem, Assert.Throws<WorldDescriptorException>(() => WorldDescriptor.Parse("{ \"coordinateSystem\": \"XYZ\", \"levels\": [ { \"url\": \"a\", \"splatCount\": 1 } ] }")).Code);
        }

        [Test]
        public void SingleFileDescriptorHasOneUnboundedLevel()
        {
            WorldDescriptor descriptor = WorldDescriptor.ForSingleFile("https://x/world.spz", SplatCoordinateSystem.Rdf);
            Assert.AreEqual(1, descriptor.levels.Count);
            Assert.AreEqual(SplatCoordinateSystem.Rdf, descriptor.CoordinateSystem);
            Assert.AreSame(descriptor.FirstLevel(SplatQualityProfile.Mobile()), descriptor.FinalLevel(SplatQualityProfile.Mobile()));
        }
    }

    public sealed class SplatMemoryBudgetTests
    {
        [Test]
        public void EstimateGrowsWithCountAndShDegree()
        {
            long small = SplatMemoryBudget.EstimateBytes(150000, 0);
            long big = SplatMemoryBudget.EstimateBytes(500000, 0);
            long bigSh = SplatMemoryBudget.EstimateBytes(500000, 3);
            Assert.That(big, Is.GreaterThan(small));
            Assert.That(bigSh, Is.GreaterThan(big));
            // 500k at degree 0: 40 B resident + 56 B transient per splat = 48 MB; well under any phone budget.
            Assert.That(big, Is.LessThan(60L * 1024 * 1024));
        }

        [Test]
        public void DesktopBudgetAffordsFullRes()
        {
            // In the editor the "device" is a desktop.
            Assert.IsTrue(SplatMemoryBudget.CanAfford(1500000, 3));
        }
    }

    public sealed class VirtualJoystickTests
    {
        [Test]
        public void DeadZoneReadsZeroAndRescalesTheRest()
        {
            Assert.AreEqual(UnityEngine.Vector2.zero, VirtualJoystick.ApplyDeadZone(new UnityEngine.Vector2(0.05f, 0.05f), 0.1f));
            UnityEngine.Vector2 full = VirtualJoystick.ApplyDeadZone(new UnityEngine.Vector2(1f, 0f), 0.1f);
            Assert.AreEqual(1f, full.x, 1e-5f);
            UnityEngine.Vector2 half = VirtualJoystick.ApplyDeadZone(new UnityEngine.Vector2(0.55f, 0f), 0.1f);
            Assert.AreEqual(0.5f, half.x, 1e-5f, "just above the dead zone the output starts at 0, not at 0.1");
        }

        [Test]
        public void SizesAreTheOnesFromTheSpec()
        {
            Assert.AreEqual(120f, VirtualJoystick.ZoneSizeDp);
            Assert.AreEqual(60f, VirtualJoystick.KnobSizeDp);
            Assert.AreEqual(0.1f, VirtualJoystick.DeadZone);
        }
    }

    public sealed class SplatSortPolicyTests
    {
        [Test]
        public void FirstCallAlwaysSorts()
        {
            var policy = new SplatSortPolicy();
            Assert.IsTrue(policy.ShouldResort(new Unity.Mathematics.float3(0f), new Unity.Mathematics.float3(0f, 0f, 1f), 1, 0.0));
        }

        [Test]
        public void StillCameraDoesNotResort()
        {
            var policy = new SplatSortPolicy();
            var position = new Unity.Mathematics.float3(1f, 2f, 3f);
            var forward = new Unity.Mathematics.float3(0f, 0f, 1f);
            policy.MarkSorted(position, forward, 7, 0.0);
            Assert.IsFalse(policy.ShouldResort(position + new Unity.Mathematics.float3(0.001f), forward, 7, 1.0));
        }

        [Test]
        public void MovingTurningOrCullingChangeResort()
        {
            var policy = new SplatSortPolicy();
            var position = new Unity.Mathematics.float3(0f);
            var forward = new Unity.Mathematics.float3(0f, 0f, 1f);
            policy.MarkSorted(position, forward, 7, 0.0);
            Assert.IsTrue(policy.ShouldResort(position + new Unity.Mathematics.float3(0.1f, 0f, 0f), forward, 7, 1.0), "moved");
            Assert.IsTrue(policy.ShouldResort(position, Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(0.05f, 0f, 1f)), 7, 1.0), "turned");
            Assert.IsTrue(policy.ShouldResort(position, forward, 8, 1.0), "visible set changed");
        }

        [Test]
        public void MinIntervalHoldsBackFrequentSorts()
        {
            var policy = new SplatSortPolicy { MinIntervalSeconds = 0.5f };
            var forward = new Unity.Mathematics.float3(0f, 0f, 1f);
            policy.MarkSorted(new Unity.Mathematics.float3(0f), forward, 1, 0.0);
            Assert.IsFalse(policy.ShouldResort(new Unity.Mathematics.float3(5f), forward, 1, 0.2));
            Assert.IsTrue(policy.ShouldResort(new Unity.Mathematics.float3(5f), forward, 1, 0.6));
        }
    }
}

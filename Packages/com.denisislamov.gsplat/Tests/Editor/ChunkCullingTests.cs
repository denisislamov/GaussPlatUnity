using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GSplat.Tests
{
    public sealed class ChunkCullingTests
    {
        [Test]
        public void ChunksBehindTheCameraAreCulled()
        {
            var chunks = new NativeArray<SplatChunkInfo>(2, Allocator.Temp);
            chunks[0] = new SplatChunkInfo(10, new float3(-1f, -1f, 9f), new float3(1f, 1f, 11f));   // in front
            chunks[1] = new SplatChunkInfo(10, new float3(-1f, -1f, -11f), new float3(1f, 1f, -9f)); // behind

            var cameraObject = new GameObject("test camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = Vector3.zero;
                camera.transform.rotation = Quaternion.identity;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;

                var visible = new List<int>();
                ChunkCulling.CollectVisible(camera, Matrix4x4.identity, chunks, visible);
                Assert.AreEqual(1, visible.Count);
                Assert.AreEqual(0, visible[0]);

                // Rotating the object 180 degrees around Y swaps which chunk is in front.
                visible.Clear();
                ChunkCulling.CollectVisible(camera, Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)), chunks, visible);
                Assert.AreEqual(1, visible.Count);
                Assert.AreEqual(1, visible[0]);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                chunks.Dispose();
            }
        }

        [Test]
        public void TransformedBoundsContainAllCorners()
        {
            var chunk = new SplatChunkInfo(1, new float3(-1f), new float3(1f));
            Bounds world = ChunkCulling.TransformBounds(Matrix4x4.TRS(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 45f, 0f), Vector3.one), chunk);
            Assert.AreEqual(10f, world.center.x, 1e-4f);
            Assert.AreEqual(2f * math.SQRT2, world.size.x, 1e-3f, "45 degree rotation widens x to the diagonal");
            Assert.AreEqual(2f, world.size.y, 1e-4f);
        }
    }
}

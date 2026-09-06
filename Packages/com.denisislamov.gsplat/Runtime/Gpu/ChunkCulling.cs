using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Frustum culling at chunk granularity: the chunk bounds (already padded by the splat extents) are
    /// transformed to world space and tested against the camera planes. Cheap enough to run every frame for
    /// hundreds of chunks and it shrinks both the sort and the draw.
    /// </summary>
    public static class ChunkCulling
    {
        // Shared scratch for the frustum planes: culling runs on the main thread only (from the renderer's prepare
        // step), so one static array serves every renderer without allocating per frame.
        private static readonly Plane[] PlaneScratch = new Plane[6];

        /// <summary>Appends the indices of chunks that intersect the camera frustum to <paramref name="visible"/>.</summary>
        public static void CollectVisible(Camera camera, Matrix4x4 localToWorld, NativeArray<SplatChunkInfo> chunks, List<int> visible)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (visible == null) throw new ArgumentNullException(nameof(visible));

            GeometryUtility.CalculateFrustumPlanes(camera, PlaneScratch);
            CollectVisible(PlaneScratch, localToWorld, chunks, visible);
        }

        public static void CollectVisible(Plane[] frustumPlanes, Matrix4x4 localToWorld, NativeArray<SplatChunkInfo> chunks, List<int> visible)
        {
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                Bounds worldBounds = TransformBounds(localToWorld, chunks[chunkIndex]);
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, worldBounds))
                {
                    visible.Add(chunkIndex);
                }
            }
        }

        /// <summary>World AABB of a rotated/scaled local AABB: transform the 8 corners and take min/max.</summary>
        public static Bounds TransformBounds(Matrix4x4 localToWorld, SplatChunkInfo chunk)
        {
            Vector3 min = chunk.BoundsMin;
            Vector3 max = chunk.BoundsMax;
            var result = new Bounds(localToWorld.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) != 0 ? max.x : min.x,
                    (corner & 2) != 0 ? max.y : min.y,
                    (corner & 4) != 0 ? max.z : min.z);
                result.Encapsulate(localToWorld.MultiplyPoint3x4(local));
            }

            return result;
        }
    }
}

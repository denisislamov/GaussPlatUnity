using Unity.Mathematics;

namespace GSplat
{
    /// <summary>
    /// Decides whether the order is stale. Sorting every frame is wasted work while the camera stands still, and
    /// on the CPU path it is also the most expensive thing we do per frame. Order also goes stale when the set of
    /// visible chunks changes (culling) or more chunks finish uploading.
    /// </summary>
    public sealed class SplatSortPolicy
    {
        /// <summary>Camera movement (in the object's local units) that forces a new sort.</summary>
        public float MinMoveDistance = 0.02f;

        /// <summary>Change of view direction that forces a new sort.</summary>
        public float MinRotationDegrees = 0.5f;

        /// <summary>Lower bound between sorts, 0 = as often as the other rules ask.</summary>
        public float MinIntervalSeconds = 0f;

        private bool hasSorted;
        private float3 lastPosition;
        private float3 lastForward;
        private int lastVisibleHash;
        private double lastTime;

        /// <param name="ignoreRotation">True for radial sorting: the order depends on the camera position only.</param>
        public bool ShouldResort(float3 cameraPositionLocal, float3 cameraForwardLocal, int visibleChunksHash, double time, bool ignoreRotation = false)
        {
            if (!hasSorted) return true;
            if (visibleChunksHash != lastVisibleHash) return true;
            if (time - lastTime < MinIntervalSeconds) return false;

            bool moved = math.distance(cameraPositionLocal, lastPosition) > MinMoveDistance;
            if (ignoreRotation) return moved;

            float cosine = math.clamp(math.dot(math.normalizesafe(cameraForwardLocal), math.normalizesafe(lastForward)), -1f, 1f);
            bool turned = math.degrees(math.acos(cosine)) > MinRotationDegrees;
            return moved || turned;
        }

        public void MarkSorted(float3 cameraPositionLocal, float3 cameraForwardLocal, int visibleChunksHash, double time)
        {
            hasSorted = true;
            lastPosition = cameraPositionLocal;
            lastForward = cameraForwardLocal;
            lastVisibleHash = visibleChunksHash;
            lastTime = time;
        }

        public void Reset()
        {
            hasSorted = false;
        }
    }
}

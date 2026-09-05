using UnityEngine;

namespace GSplat
{
    /// <summary>Destroys Unity objects the right way for the current mode: Destroy in play mode, DestroyImmediate in the editor.</summary>
    public static class SplatObjectUtility
    {
        public static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}

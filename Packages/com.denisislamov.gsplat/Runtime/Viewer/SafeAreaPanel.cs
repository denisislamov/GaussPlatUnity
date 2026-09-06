using UnityEngine;

namespace GSplat
{
    /// <summary>
    /// Keeps this RectTransform's anchors on Screen.safeArea, so UI under it stays clear of notches and rounded
    /// corners. The math is static so the OnGUI overlay can use the same safe rect without a canvas.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("GSplat/Safe Area Panel")]
    public sealed class SafeAreaPanel : MonoBehaviour
    {
        private Rect applied;

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != applied) Apply();
        }

        private void Apply()
        {
            applied = Screen.safeArea;
            var rect = (RectTransform)transform;
            AnchorsFor(applied, Screen.width, Screen.height, out Vector2 anchorMin, out Vector2 anchorMax);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>The safe area as anchor fractions of the screen, which works with any canvas scale.</summary>
        public static void AnchorsFor(Rect safeArea, int screenWidth, int screenHeight, out Vector2 anchorMin, out Vector2 anchorMax)
        {
            anchorMin = new Vector2(safeArea.xMin / screenWidth, safeArea.yMin / screenHeight);
            anchorMax = new Vector2(safeArea.xMax / screenWidth, safeArea.yMax / screenHeight);
        }

        /// <summary>The safe area in IMGUI coordinates (origin top-left, y down), for OnGUI code.</summary>
        public static Rect GuiRect(Rect safeArea, int screenHeight)
        {
            return new Rect(safeArea.xMin, screenHeight - safeArea.yMax, safeArea.width, safeArea.height);
        }
    }
}

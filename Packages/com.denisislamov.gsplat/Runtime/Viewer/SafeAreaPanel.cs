using UnityEngine;

namespace GSplat
{
    /// <summary>Keeps this RectTransform's anchors on Screen.safeArea, so UI under it stays clear of notches and rounded corners.</summary>
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
            rect.anchorMin = new Vector2(applied.xMin / Screen.width, applied.yMin / Screen.height);
            rect.anchorMax = new Vector2(applied.xMax / Screen.width, applied.yMax / Screen.height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}

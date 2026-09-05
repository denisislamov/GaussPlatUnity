using UnityEngine;
using UnityEngine.EventSystems;

namespace GSplat
{
    /// <summary>
    /// The on-screen stick of TZ F-10: a 120 px zone with a 60 px knob and a 0.1 dead zone (sizes in dp, so they
    /// stay the same physical size on any screen). Read <see cref="Value"/> each frame; (0,0) when untouched.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public const float ZoneSizeDp = 120f;
        public const float KnobSizeDp = 60f;
        public const float DeadZone = 0.1f;

        [SerializeField] private RectTransform knob;

        private RectTransform zone;
        private Vector2 value;

        /// <summary>Normalized stick position, dead zone applied, length in [0, 1].</summary>
        public Vector2 Value => value;

        public bool IsPressed { get; private set; }

        public void Initialize(RectTransform knobTransform)
        {
            knob = knobTransform;
        }

        private void Awake()
        {
            zone = (RectTransform)transform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsPressed = true;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(zone, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;

            float radius = zone.rect.width * 0.5f;
            Vector2 normalized = local / radius;
            if (normalized.sqrMagnitude > 1f) normalized.Normalize();
            value = ApplyDeadZone(normalized, DeadZone);
            if (knob != null) knob.anchoredPosition = normalized * radius;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
            value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        /// <summary>Below the dead zone the stick reads zero; above it the remaining range is stretched back to [0, 1] so there is no jump.</summary>
        public static Vector2 ApplyDeadZone(Vector2 normalized, float deadZone)
        {
            float length = normalized.magnitude;
            if (length <= deadZone) return Vector2.zero;
            float scaled = (length - deadZone) / (1f - deadZone);
            return normalized / length * Mathf.Min(scaled, 1f);
        }

        /// <summary>Density-independent pixels to screen pixels; 160 dpi is one dp per pixel. Unknown dpi (0, editor) counts as 160.</summary>
        public static float DpToPixels(float dp)
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            return dp * dpi / 160f;
        }
    }
}

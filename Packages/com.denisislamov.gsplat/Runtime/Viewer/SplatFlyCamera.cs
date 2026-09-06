using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GSplat
{
    /// <summary>
    /// The viewer camera of TZ E9: free flight by default (generated worlds without a collider are fly-through), walking
    /// with a CharacterController when a collider exists. Desktop: WASD/QE, right mouse drag to look, wheel for speed,
    /// Shift to boost. Touch: one finger looks, pinch moves forward/back, two fingers pan, the on-screen stick walks.
    /// </summary>
    [AddComponentMenu("GSplat/Fly Camera")]
    public sealed class SplatFlyCamera : MonoBehaviour
    {
        [Header("Speed")]
        [SerializeField, Min(0.01f), Tooltip("Meters per second at full stick / key.")]
        private float moveSpeed = 1.5f;
        [SerializeField, Min(1f)] private float boostMultiplier = 3f;
        [SerializeField, Range(0f, 0.5f), Tooltip("Seconds to reach the target velocity; 0 = instant.")]
        private float smoothing = 0.15f;

        [Header("Look")]
        [SerializeField, Range(30f, 360f), Tooltip("Degrees turned by a mouse drag across the full screen height. Screen-relative so every DPI feels the same.")]
        private float mouseDegreesPerScreenHeight = 110f;
        [SerializeField, Range(30f, 360f), Tooltip("Degrees turned by a one-finger drag across the full screen height.")]
        private float touchDegreesPerScreenHeight = 150f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Seconds of smoothing on touch look; hides finger jitter without feeling laggy.")]
        private float touchLookSmoothing = 0.06f;

        [Header("Touch")]
        [SerializeField, Min(0.01f), Tooltip("Meters moved forward when the pinch distance doubles (log scale: the same gesture feels the same at any zoom).")]
        private float pinchMetersPerDoubling = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("Meters panned by a two-finger drag across the full screen height.")]
        private float panMetersPerScreenHeight = 1.5f;

        [Header("Limits")]
        [SerializeField, Tooltip("Keep the camera inside the world bounds grown by this factor (0 = no limit).")]
        private float boundsLimitFactor = 1.5f;

        [Header("Walk mode")]
        [SerializeField, Tooltip("Set when a collider exists: gravity and collisions through a CharacterController.")]
        private bool walkMode;
        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private float gravity = 9.81f;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float yaw;
        private float pitch;
        private Vector3 velocity;
        private float verticalVelocity;
        private CharacterController controller;
        private Bounds? limitBounds;
        private float previousPinchDistance = -1f;
        private Vector2 previousTwoFingerCenter;
        private Vector2 smoothedTouchLook;

        /// <summary>Set by the on-screen joystick every frame: x strafe, y forward, each in [-1, 1].</summary>
        public Vector2 JoystickInput { get; set; }

        public bool WalkMode
        {
            get => walkMode;
            set
            {
                walkMode = value;
                if (walkMode && controller == null)
                {
                    controller = gameObject.AddComponent<CharacterController>();
                    controller.height = eyeHeight;
                    controller.radius = 0.3f;
                    controller.center = new Vector3(0f, -eyeHeight * 0.5f + controller.radius, 0f);
                }

                if (controller != null) controller.enabled = walkMode;
            }
        }

        private void Start()
        {
            SetSpawn(transform.position, transform.rotation);
            if (walkMode) WalkMode = true;
        }

        /// <summary>Where Reset goes back to.</summary>
        public void SetSpawn(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
            Vector3 euler = rotation.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        /// <summary>Limit flight to these bounds (grown by the factor); pass null to fly anywhere.</summary>
        public void SetLimitBounds(Bounds? bounds)
        {
            limitBounds = bounds;
        }

        public void ResetToSpawn()
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            Vector3 euler = spawnRotation.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            velocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        private void Update()
        {
            Vector2 look = Vector2.zero;
            Vector3 moveLocal = Vector3.zero;
            float speedScale = 1f;

            // look is accumulated in degrees by the input readers.
            ReadMouseAndKeyboard(ref look, ref moveLocal, ref speedScale);
            ReadTouch(ref look, ref moveLocal);
            moveLocal += new Vector3(JoystickInput.x, 0f, JoystickInput.y);

            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (moveLocal.sqrMagnitude > 1f) moveLocal.Normalize();
            Vector3 targetVelocity = transform.TransformDirection(moveLocal) * moveSpeed * speedScale;
            if (walkMode) targetVelocity.y = 0f; // walking: no vertical flight, gravity handles y
            velocity = smoothing > 0f ? Vector3.Lerp(velocity, targetVelocity, 1f - Mathf.Exp(-Time.deltaTime / smoothing)) : targetVelocity;

            Move(velocity * Time.deltaTime);
        }

        private void Move(Vector3 delta)
        {
            if (walkMode && controller != null && controller.enabled)
            {
                verticalVelocity -= gravity * Time.deltaTime;
                if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -0.5f;
                controller.Move(delta + Vector3.up * verticalVelocity * Time.deltaTime);
                if (transform.position.y < -100f) ResetToSpawn(); // fell out of the world
                return;
            }

            Vector3 position = transform.position + delta;
            if (limitBounds.HasValue && boundsLimitFactor > 0f)
            {
                Bounds limit = limitBounds.Value;
                limit.Expand(limit.size * (boundsLimitFactor - 1f));
                position = limit.ClosestPoint(position);
            }

            transform.position = position;
        }

        private void ReadMouseAndKeyboard(ref Vector2 look, ref Vector3 moveLocal, ref float speedScale)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) moveLocal.z += 1f;
                if (keyboard.sKey.isPressed) moveLocal.z -= 1f;
                if (keyboard.dKey.isPressed) moveLocal.x += 1f;
                if (keyboard.aKey.isPressed) moveLocal.x -= 1f;
                if (keyboard.eKey.isPressed) moveLocal.y += 1f;
                if (keyboard.qKey.isPressed) moveLocal.y -= 1f;
                if (keyboard.leftShiftKey.isPressed) speedScale = boostMultiplier;
                if (keyboard.rKey.wasPressedThisFrame) ResetToSpawn();
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) look += mouse.delta.ReadValue() * (mouseDegreesPerScreenHeight / Screen.height);
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0f ? 1.15f : 1f / 1.15f), 0.05f, 100f);
            }
        }

        private void ReadTouch(ref Vector2 look, ref Vector3 moveLocal)
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

            // Touches that started on UI (the joystick, buttons) belong to the UI.
            int activeCount = 0;
            UnityEngine.InputSystem.Controls.TouchControl first = null;
            UnityEngine.InputSystem.Controls.TouchControl second = null;
            foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
            {
                if (!touch.isInProgress) continue;
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue())) continue;
                if (activeCount == 0) first = touch;
                else if (activeCount == 1) second = touch;
                activeCount++;
            }

            if (activeCount == 1)
            {
                Vector2 target = first.delta.ReadValue() * (touchDegreesPerScreenHeight / Screen.height);
                smoothedTouchLook = touchLookSmoothing > 0f ? Vector2.Lerp(smoothedTouchLook, target, 1f - Mathf.Exp(-Time.deltaTime / touchLookSmoothing)) : target;
                look += smoothedTouchLook;
                previousPinchDistance = -1f;
                return;
            }

            smoothedTouchLook = Vector2.zero;

            if (activeCount >= 2)
            {
                Vector2 a = first.position.ReadValue();
                Vector2 b = second.position.ReadValue();
                float distance = Vector2.Distance(a, b);
                Vector2 center = (a + b) * 0.5f;
                if (previousPinchDistance > 0f && distance > 1f)
                {
                    // Pinch: log of the distance ratio, so spreading 100 -> 200 px moves as much as 200 -> 400 px;
                    // clamped so a finger landing far away cannot teleport the camera. Two-finger drag = pan.
                    float doublings = Mathf.Clamp(Mathf.Log(distance / previousPinchDistance, 2f), -0.5f, 0.5f);
                    float forward = doublings * pinchMetersPerDoubling;
                    Vector2 pan = (center - previousTwoFingerCenter) * (panMetersPerScreenHeight / Screen.height);
                    Move(transform.forward * forward - transform.right * pan.x - transform.up * pan.y);
                }

                previousPinchDistance = distance;
                previousTwoFingerCenter = center;
                return;
            }

            previousPinchDistance = -1f;
        }
    }
}

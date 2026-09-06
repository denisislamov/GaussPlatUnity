using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GSplat
{
    /// <summary>
    /// A uGUI canvas with one button that cycles through the scenes of the build ("Next: study room"), so a phone build
    /// can switch between the sample worlds without a cable. Built in code, inside the safe area, bottom center.
    /// Hidden when the build has a single scene.
    /// </summary>
    [AddComponentMenu("GSplat/Scene Menu")]
    public sealed class SplatSceneMenu : MonoBehaviour
    {
        private RectTransform safeArea;
        private Rect appliedSafeArea;

        private void Start()
        {
            if (SceneManager.sceneCountInBuildSettings < 2) return;
            Build();
        }

        private void Update()
        {
            if (safeArea != null && Screen.safeArea != appliedSafeArea) ApplySafeArea();
        }

        private void Build()
        {
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            var canvasObject = new GameObject("Scene Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = VirtualJoystick.DpToPixels(1f);

            var safeObject = new GameObject("Safe Area", typeof(RectTransform));
            safeObject.transform.SetParent(canvas.transform, false);
            safeArea = (RectTransform)safeObject.transform;
            ApplySafeArea();

            int nextIndex = (SceneManager.GetActiveScene().buildIndex + 1) % SceneManager.sceneCountInBuildSettings;
            string nextName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(nextIndex)).Replace('_', ' ');

            var buttonObject = new GameObject("Next Scene", typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(safeArea, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 16f);
            rect.sizeDelta = new Vector2(220f, 44f);
            Image image = buttonObject.GetComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, 0.75f);

            var labelObject = new GameObject("Label", typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = "Next: " + nextName;
            label.raycastTarget = false;

            buttonObject.GetComponent<Button>().onClick.AddListener(() => SceneManager.LoadScene(nextIndex));
        }

        private void ApplySafeArea()
        {
            appliedSafeArea = Screen.safeArea;
            safeArea.anchorMin = new Vector2(appliedSafeArea.xMin / Screen.width, appliedSafeArea.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(appliedSafeArea.xMax / Screen.width, appliedSafeArea.yMax / Screen.height);
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
        }
    }
}

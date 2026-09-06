using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GSplat
{
    /// <summary>
    /// The few uGUI pieces the package builds in code: the runtime viewer overlay (<see cref="SplatViewerUi"/>) and the
    /// editor-made scene canvases share them, so a button or a label looks the same everywhere and is created in one
    /// place. Layout (anchors, sizes, offsets) stays with the caller; this only knows how a piece is put together.
    /// </summary>
    public static class UiFactory
    {
        /// <summary>The built-in font every text uses; the same asset in the editor and in players.</summary>
        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>
        /// A sprite from Unity's "builtin extra" resources (UI/Skin/UISprite.psd, UI/Skin/Knob.psd). In the editor they
        /// come from the AssetDatabase, which is what makes a generated scene keep a real sprite reference.
        /// TODO: in players Resources.GetBuiltinResource does not see the builtin extras (it logs an assert and returns
        /// null), so the runtime-built buttons draw as plain rectangles. Ship a sprite in the package instead.
        /// </summary>
        public static Sprite BuiltinSprite(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
#else
            return Resources.GetBuiltinResource<Sprite>(path);
#endif
        }

        /// <summary>Creates an EventSystem with the Input System module when the scene has none; UI buttons need one.</summary>
        public static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null) return;
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (parent != null) eventSystem.transform.SetParent(parent, false);
        }

        /// <summary>A screen-space overlay canvas with a scaler and a raycaster; the caller sets the scaler's mode.</summary>
        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder)
        {
            var canvasObject = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) canvasObject.transform.SetParent(parent, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            return canvas;
        }

        /// <summary>A full-canvas panel that follows Screen.safeArea (see <see cref="SafeAreaPanel"/>); put everything under it.</summary>
        public static RectTransform CreateSafeArea(Transform canvas)
        {
            var safeObject = new GameObject("Safe Area", typeof(RectTransform), typeof(SafeAreaPanel));
            safeObject.transform.SetParent(canvas, false);
            return (RectTransform)safeObject.transform;
        }

        /// <summary>A white, non-raycast label anchored and pivoted at <paramref name="anchor"/>, offset from it by <paramref name="offset"/>.</summary>
        public static Text CreateText(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            Text text = textObject.GetComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A plain colored rectangle (panel background, slider track); raycasts so it blocks clicks to what is behind it.</summary>
        public static Image CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var panelObject = new GameObject(name, typeof(Image));
            panelObject.transform.SetParent(parent, false);
            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = panelObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>
        /// A vertical scroll view filling <paramref name="parent"/>; rows added to the returned content transform stack
        /// top to bottom (VerticalLayoutGroup + ContentSizeFitter) and the view scrolls when they overflow.
        /// </summary>
        public static RectTransform CreateScrollView(Transform parent, float rowSpacing, RectOffset padding)
        {
            var viewObject = new GameObject("Scroll View", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            viewObject.transform.SetParent(parent, false);
            var viewRect = (RectTransform)viewObject.transform;
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = Vector2.zero;
            viewRect.offsetMax = Vector2.zero;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewObject.transform, false);
            var content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = rowSpacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = viewObject.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        /// <summary>A horizontal uGUI slider (track, fill, handle) with the given range; wholeNumbers for integer knobs.</summary>
        public static Slider CreateSlider(Transform parent, string name, float min, float max, bool wholeNumbers, Vector2 size)
        {
            var sliderObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            var rect = (RectTransform)sliderObject.transform;
            rect.sizeDelta = size;

            Image track = CreatePanel(sliderObject.transform, "Track", new Vector2(0f, 0.35f), new Vector2(1f, 0.65f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.25f));
            track.raycastTarget = false;

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObject.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
            fillAreaRect.offsetMin = new Vector2(6f, 0f);
            fillAreaRect.offsetMax = new Vector2(-6f, 0f);
            Image fill = CreatePanel(fillArea.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(1f, 0.85f, 0.4f, 0.9f));
            fill.raycastTarget = false;

            var handleArea = new GameObject("Handle Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderObject.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);
            Image handle = CreatePanel(handleArea.transform, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.white);
            ((RectTransform)handle.transform).sizeDelta = new Vector2(16f, 0f);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            return slider;
        }

        /// <summary>A sliced-sprite button with a centered black label that fills it. Returns the Button; the label is its only child.</summary>
        public static Button CreateButton(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, float alpha, string labelText, int fontSize)
        {
            var buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            Image image = buttonObject.GetComponent<Image>();
            image.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 1f, 1f, alpha);

            var labelObject = new GameObject("Label", typeof(Text));
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text label = labelObject.GetComponent<Text>();
            label.font = DefaultFont;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = labelText;
            label.raycastTarget = false;

            return buttonObject.GetComponent<Button>();
        }
    }
}

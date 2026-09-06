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

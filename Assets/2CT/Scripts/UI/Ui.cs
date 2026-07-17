using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TwoCT.UI
{
    /// <summary>
    /// Shared uGUI construction primitives used by BOTH the edit-time scene builder and the
    /// runtime fallback, so the hierarchy is identical either way. Building happens in the editor
    /// (baked into the scene, fully editable); components only rebuild at runtime if their
    /// serialized references are missing.
    /// </summary>
    public static class Ui
    {
        private static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        private static Sprite _uiSprite;
        /// <summary>Unity's built-in white UI sprite. Needed for Image.type = Filled to actually clip.</summary>
        public static Sprite UiSprite => _uiSprite != null ? _uiSprite : (_uiSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd"));

        public static Canvas Canvas(string name, Transform parent, int sortingOrder)
        {
            New(name, parent, out var go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Ensure a single EventSystem exists (works at edit time and runtime).</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }

        public static RectTransform New(string name, Transform parent, out GameObject go)
        {
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            var rt = New(name, parent, out _);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var rt = New("Label", parent, out _);
            var t = rt.gameObject.AddComponent<Text>();
            t.text = text; t.font = Font; t.fontSize = size; t.fontStyle = style; t.color = color; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        public static Button Btn(Transform parent, string text, Color bg, Color fg, int fontSize = 24)
        {
            var img = Panel("Button", parent, bg);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            c.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            btn.colors = c;
            var label = Label(img.rectTransform, text, fontSize, FontStyle.Bold, fg, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            return btn;
        }

        public static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // A pooled bar (background + fill). Returns the fill Image so callers can set fillAmount via width.
        public static Image Bar(Transform parent, Color bgColor, Color fillColor, out Image fill)
        {
            var bg = Panel("BarBg", parent, bgColor);
            fill = Panel("Fill", bg.rectTransform, fillColor);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            Stretch(fill.rectTransform);
            return bg;
        }
    }
}

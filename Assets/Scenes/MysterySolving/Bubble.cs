using UnityEngine;
using UnityEngine.UI;
using System;

namespace LLMMysterSolving
{
    struct BubbleUI
    {
        public Sprite sprite;
        public Font font;
        public int fontSize;
        public Color fontColor;
        public Color bubbleColor;
        public float bottomPosition;
        public float leftPosition;
        public float textPadding;
        public float bubbleOffset;
        public float bubbleWidth;
        public float bubbleHeight;
    }

    public class RectTransformResizeHandler : MonoBehaviour
    {
        Action callback;

        public void SetCallBack(Action callback)
        {
            this.callback = callback;
        }

        void OnRectTransformDimensionsChange()
        {
            callback?.Invoke();
        }
    }

    class Bubble
    {
        protected GameObject bubbleObject; // 背景容器
        protected GameObject textObject;   // 文本子物体
        public BubbleUI bubbleUI;

        public Bubble(Transform parent, BubbleUI ui, string name, string message)
        {
            bubbleUI = ui;
            // 1. 创建背景图作为主容器
            bubbleObject = CreateImageObject(parent, name);

            // 2. 配置容器布局组件，用于自动撑开大小
            ConfigureLayout(bubbleObject.GetComponent<RectTransform>());

            // 3. 创建文本子物体
            textObject = CreateTextObject(bubbleObject.transform, "Text", message);

            // 4. 设置最终位置和对齐
            SetBubbleAnchor(bubbleObject.GetComponent<RectTransform>(), bubbleUI);
        }

        void ConfigureLayout(RectTransform rect)
        {
            // 添加布局组来处理 Padding
            VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(
                (int)bubbleUI.textPadding, (int)bubbleUI.textPadding,
                (int)bubbleUI.textPadding, (int)bubbleUI.textPadding
            );
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;

            // 添加 ContentSizeFitter 自动撑开背景
            ContentSizeFitter fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = (bubbleUI.bubbleWidth == -1) ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (bubbleUI.bubbleWidth != -1)
            {
                rect.sizeDelta = new Vector2(bubbleUI.bubbleWidth, rect.sizeDelta.y);
            }
        }

        protected GameObject CreateTextObject(Transform parent, string name, string message)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.text = message;
            if (bubbleUI.font != null) t.font = bubbleUI.font;
            t.fontSize = bubbleUI.fontSize;
            t.color = bubbleUI.fontColor;
            t.alignment = TextAnchor.MiddleLeft;

            // 文本也需要 Fitter 来计算 PreferredSize
            ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = (bubbleUI.bubbleWidth == -1) ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go;
        }

        protected GameObject CreateImageObject(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = bubbleUI.sprite;
            img.color = bubbleUI.bubbleColor;
            return go;
        }

        void SetBubbleAnchor(RectTransform rect, BubbleUI ui)
        {
            rect.pivot = new Vector2(ui.leftPosition, ui.bottomPosition);
            rect.anchorMin = new Vector2(ui.leftPosition, ui.bottomPosition);
            rect.anchorMax = new Vector2(ui.leftPosition, ui.bottomPosition);
            rect.localScale = Vector3.one;

            Vector2 pos = new Vector2(ui.bubbleOffset, ui.bubbleOffset);
            if (ui.leftPosition == 1) pos.x *= -1;
            if (ui.bottomPosition == 1) pos.y *= -1;
            rect.anchoredPosition = pos;
        }

        public void OnResize(Action callback)
        {
            RectTransformResizeHandler handler = bubbleObject.AddComponent<RectTransformResizeHandler>();
            handler.SetCallBack(callback);
        }

        public RectTransform GetRectTransform() => bubbleObject.GetComponent<RectTransform>();
        public RectTransform GetOuterRectTransform() => bubbleObject.GetComponent<RectTransform>();

        public Vector2 GetSize()
        {
            Canvas.ForceUpdateCanvases();
            return bubbleObject.GetComponent<RectTransform>().rect.size;
        }

        public string GetText() => textObject.GetComponent<Text>().text;
        public void SetText(string text) => textObject.GetComponent<Text>().text = text;
        public void Destroy() => UnityEngine.Object.Destroy(bubbleObject);
    }

    class InputBubble : Bubble
    {
        protected InputField inputField;
        protected GameObject placeholderObject;

        public InputBubble(Transform parent, BubbleUI ui, string name, string message, int lineHeight = 4) :
            base(parent, ui, name, emptyLines(message, lineHeight))
        {
            // 移除基类创建的默认布局组件，因为输入框需要特殊处理
            var fitter = bubbleObject.GetComponent<ContentSizeFitter>();
            if (fitter) fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var layout = bubbleObject.GetComponent<VerticalLayoutGroup>();
            if (layout) UnityEngine.Object.DestroyImmediate(layout);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textObject.GetComponent<ContentSizeFitter>().enabled = false;

            // 手动设置文本位置（在背景内部）
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(ui.textPadding, ui.textPadding);
            textRect.offsetMax = new Vector2(-ui.textPadding, -ui.textPadding);

            placeholderObject = CreatePlaceholderObject(bubbleObject.transform, textRect);
            GameObject inputFieldObject = CreateInputFieldObject(bubbleObject.transform, textObject.GetComponent<Text>(), placeholderObject.GetComponent<Text>());
            inputField = inputFieldObject.GetComponent<InputField>();
        }

        static string emptyLines(string message, int lineHeight)
        {
            string s = message;
            for (int i = 0; i < lineHeight - 1; i++) s += "\n";
            return s;
        }

        GameObject CreatePlaceholderObject(Transform parent, RectTransform textRect)
        {
            GameObject go = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.text = "Loading...";
            if (bubbleUI.font != null) t.font = bubbleUI.font;
            t.fontSize = bubbleUI.fontSize;
            t.color = new Color(bubbleUI.fontColor.r, bubbleUI.fontColor.g, bubbleUI.fontColor.b, 0.5f);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = textRect.anchorMin;
            rect.anchorMax = textRect.anchorMax;
            rect.offsetMin = textRect.offsetMin;
            rect.offsetMax = textRect.offsetMax;
            return go;
        }

        GameObject CreateInputFieldObject(Transform parent, Text textComponent, Text placeholderComponent)
        {
            GameObject go = new GameObject("InputField", typeof(RectTransform), typeof(InputField));
            go.transform.SetParent(parent, false);
            InputField field = go.GetComponent<InputField>();
            field.textComponent = textComponent;
            field.placeholder = placeholderComponent;
            field.interactable = true;
            field.lineType = InputField.LineType.MultiLineSubmit;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        public void AddSubmitListener(UnityEngine.Events.UnityAction<string> callback) => inputField.onSubmit.AddListener(callback);
        public void AddValueChangedListener(UnityEngine.Events.UnityAction<string> callback) => inputField.onValueChanged.AddListener(callback);
        public new string GetText() => inputField.text;
        public new void SetText(string text) { inputField.text = text; inputField.MoveTextEnd(true); }
        public void SetPlaceHolderText(string text) => placeholderObject.GetComponent<Text>().text = text;
        public bool inputFocused() => inputField.isFocused;
        public void setInteractable(bool interactable) => inputField.interactable = interactable;
        public void ActivateInputField() => inputField.ActivateInputField();
        public void ReActivateInputField() { inputField.DeactivateInputField(); inputField.Select(); inputField.ActivateInputField(); }
        public void MoveTextEnd() => inputField.MoveTextEnd(true);
        public void FixCaretSorting() { }
    }
}

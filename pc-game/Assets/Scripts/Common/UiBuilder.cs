// 새로 만드는 코드 생성형 화면들(MultiplayerConnectScreen, HowToPlayScreen,
// SettingsScreen)이 공유하는 UI 조립 헬퍼. LoadingScreenController.BuildGeneratedUi()가
// 이미 비슷한 프라이빗 메서드들을 갖고 있지만, 화면이 여러 개로 늘어나면서 매번 복붙하지
// 않도록 공용 유틸로 뺐다 - LoadingScreenController 자체는 검증된 코드라 손대지 않는다.
using UnityEngine;
using UnityEngine.UI;

public static class UiBuilder
{
    public static GameObject CreateOverlayCanvas(string name, Transform parent, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    public static Image AddImage(Transform parent, string name, Sprite sprite, Vector2 anchor,
        Vector2 offset, Vector2 size, bool raycastTarget = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = raycastTarget;
        SetRect(image.rectTransform, anchor, offset, size);
        return image;
    }

    public static Button AddButton(Transform parent, string name, Sprite sprite, Vector2 anchor,
        Vector2 offset, Vector2 size)
    {
        Image image = AddImage(parent, name, sprite, anchor, offset, size, raycastTarget: true);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    public static Text AddText(Transform parent, string name, string value, int fontSize,
        TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = value;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    // 레거시 InputField(TMP 미사용, 프로젝트 전체가 UnityEngine.UI.Text 기반이라 통일).
    public static InputField AddInputField(Transform parent, string name, Vector2 anchor,
        Vector2 offset, Vector2 size, string placeholderText)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, offset, size);
        var background = go.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0f); // 배경은 별도 프레임 이미지가 담당
        var field = go.AddComponent<InputField>();

        Text text = AddText(rt, "Text", "", 34, TextAnchor.MiddleCenter, FontStyle.Normal);
        Stretch(text.rectTransform);

        Text placeholder = AddText(rt, "Placeholder", placeholderText, 34, TextAnchor.MiddleCenter,
            FontStyle.Italic);
        placeholder.color = new Color(1f, 1f, 1f, 0.55f);
        Stretch(placeholder.rectTransform);

        field.textComponent = text;
        field.placeholder = placeholder;
        field.targetGraphic = background;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }

    public static Slider AddSlider(Transform parent, string name, Sprite trackSprite, Sprite handleSprite,
        Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetRect(rt, anchor, offset, size);
        var slider = go.AddComponent<Slider>();

        Image track = AddImage(rt, "Track", trackSprite, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Stretch(track.rectTransform);

        var fillAreaGo = new GameObject("FillArea");
        fillAreaGo.transform.SetParent(rt, false);
        var fillAreaRt = fillAreaGo.AddComponent<RectTransform>();
        StretchWithMargin(fillAreaRt, size.x * 0.04f, size.y * 0.25f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(fillAreaRt, false);
        var fillImage = fillGo.AddComponent<Image>();
        fillImage.color = new Color(0.85f, 0.65f, 0.25f, 1f); // 슬라이더 채움색 - 돌판 톤과 어울리는 호박색
        Stretch(fillImage.rectTransform);

        var handleAreaGo = new GameObject("HandleArea");
        handleAreaGo.transform.SetParent(rt, false);
        var handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
        Stretch(handleAreaRt);

        Image handle = AddImage(handleAreaRt, "Handle", handleSprite, new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(size.y * 1.4f, size.y * 1.4f), raycastTarget: true);

        slider.fillRect = fillImage.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    public static void SetRect(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void StretchWithMargin(RectTransform rt, float horizontal, float vertical)
    {
        Stretch(rt);
        rt.offsetMin = new Vector2(horizontal, vertical);
        rt.offsetMax = new Vector2(-horizontal, -vertical);
    }
}

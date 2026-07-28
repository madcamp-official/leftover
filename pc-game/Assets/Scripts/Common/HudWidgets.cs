// 미니게임별 HUD(StoneThrowHud, CoconutBreakHud 등)가 공통으로 쓰는 uGUI 조립 헬퍼.
// 실제 아트 패널(Image) 위에 숫자/문구(Text)를 얹는 식의 반복되는 코드를 모아둔 것.
using UnityEngine;
using UnityEngine.UI;

public static class HudWidgets
{
    public static RectTransform CreateImage(Transform parent, string name, Sprite sprite,
        Vector2 anchor, Vector2 offset, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(width, HeightFor(sprite, width));

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        return rt;
    }

    public static Text CreateText(RectTransform parent, string name, Vector2 anchor, float width, int fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, fontSize * 1.6f);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;

        // 슬롯 배경이 짙은 갈색이라 흰 글씨만으로는 대비가 약하다 - 검은 외곽선을 준다.
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);
        return text;
    }

    public static float HeightFor(Sprite sprite, float width)
    {
        if (sprite == null || sprite.rect.width <= 0f) return width * 0.32f;
        return width * sprite.rect.height / sprite.rect.width;
    }
}

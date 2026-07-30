// 미니게임별 HUD(StoneThrowHud, CoconutBreakHud 등)가 공통으로 쓰는 uGUI 조립 헬퍼.
// 실제 아트 패널(Image) 위에 숫자/문구(Text)를 얹는 식의 반복되는 코드를 모아둔 것.
using UnityEngine;
using UnityEngine.UI;

public static class HudWidgets
{
    // 미니게임 HUD를 카메라 프레임 크기의 World Space Canvas로 만든다. Overlay는 2048px UI
    // 좌표와 몇 Unity 단위짜리 월드 좌표가 Scene 창에서 따로 놀고, ScreenSpaceCamera는 Scene
    // 창에서 UI 이미지를 숨겨 보여 직접 편집하기 어렵다. WorldSpace + 계산된 scale을 쓰면
    // 배경/캐릭터/UI를 같은 프레임에서 보면서 드래그할 수 있고 Game 출력도 동일하게 유지된다.
    public static void ConfigureForGameCamera(Canvas canvas)
    {
        if (canvas == null) return;
        Camera cam = Camera.main;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100;

        RectTransform rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(cam != null ? 1152f * cam.aspect : 2048f, 1152f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.position = cam != null
            ? new Vector3(cam.transform.position.x, cam.transform.position.y, -1f)
            : new Vector3(0f, 1f, -1f);
        float worldScale = cam != null ? cam.orthographicSize * 2f / 1152f : 6f / 1152f;
        rt.localScale = Vector3.one * worldScale;
        rt.localRotation = Quaternion.identity;

        CameraCanvasFitter fitter = canvas.GetComponent<CameraCanvasFitter>();
        if (fitter == null) fitter = canvas.gameObject.AddComponent<CameraCanvasFitter>();
        fitter.Fit();
    }

    // flipX: true면 그림만 좌우로 뒤집는다(예: P1/P2 판 아트가 서로 미러링되어 그려지지
    // 않고 항상 같은 방향이라, 화면 양쪽에 대칭으로 배치하면 안의 슬롯(숫자 자리 등)이
    // 한쪽은 안쪽에 한쪽은 바깥쪽에 쏠려 보이는 문제가 있을 때 쓴다). 반환하는 RectTransform
    // 자체는 뒤집지 않고, 그림만 별도 자식 오브젝트("Art")에 넣어 뒤집는다 - 이렇게 해야
    // 이 위에 자식으로 얹는 Text(숫자 등)가 같이 뒤집혀 글자가 좌우反전되는 걸 막을 수 있다.
    // 뒤집은 뒤 자식 Text의 anchor.x는 1 - 원래값으로 미러링해서 넣어야 슬롯 위치가 맞는다.
    public static RectTransform CreateImage(Transform parent, string name, Sprite sprite,
        Vector2 anchor, Vector2 offset, float width, bool flipX = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(width, HeightFor(sprite, width));

        var artGo = new GameObject("Art");
        artGo.transform.SetParent(rt, false);
        var artRt = artGo.AddComponent<RectTransform>();
        artRt.anchorMin = Vector2.zero;
        artRt.anchorMax = Vector2.one;
        artRt.offsetMin = Vector2.zero;
        artRt.offsetMax = Vector2.zero;
        artRt.localScale = flipX ? new Vector3(-1f, 1f, 1f) : Vector3.one;

        var image = artGo.AddComponent<Image>();
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
        text.font = UiBuilder.ArcadeFont; // 게임 전체 텍스트 폰트 통일 - Neo둥근모.
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

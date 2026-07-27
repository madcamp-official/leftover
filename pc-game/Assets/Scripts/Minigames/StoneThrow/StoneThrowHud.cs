// 돌던지기 HUD - image/stonethrow/의 v2 네임플레이트/타이머 아트 위에 숫자만 얹는다
// (v2 디자인은 게이지 없이 원형/사각 슬롯에 숫자를 채우는 방식 - hud_received_stones_p1/p2,
// hud_time_remaining 미리보기 그대로). HubController.BuildStartScreen()과 같은 uGUI 조립
// 방식(코드로 Canvas 구성)을 따른다.
// hud_angle/hud_power/hud_wind는 쓰지 않는다 - 건바운드류 포격 조작 전제로 그려진 것이라
// 실제 조작(손 들기 자동발사 + 머리 기울여 회피)과 맞지 않아 v2에서 아예 빠졌다.
using UnityEngine;
using UnityEngine.UI;

public class StoneThrowHud : MonoBehaviour
{
    // stonethrow_v2_screen_preview.png 기준 참조 해상도.
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    // 두 플레이트(hud_received_stones_p1/p2) 모두 같은 레이아웃(좌: 텍스트, 우: 큰 원형 슬롯)이라
    // 슬롯 중심 앵커도 공통으로 쓴다 - 실측(에셋 미리보기) 기준 원 중심 위치.
    private static readonly Vector2 SlotAnchor = new Vector2(0.775f, 0.51f);

    private Text _p1Hits, _p2Hits, _timer, _eventText;
    private float _eventTimer;

    public static StoneThrowHud Build(float matchSeconds)
    {
        var canvasGo = new GameObject("StoneThrowHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<StoneThrowHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), matchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float matchSeconds)
    {
        // 네임플레이트: 좌상단 P1, 우상단 P2.
        const float plateWidth = 620f;
        RectTransform p1Plate = CreateImage(root, "P1Plate", ArtAssets.LoadStoneThrow("hud_received_stones_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = CreateImage(root, "P2Plate", ArtAssets.LoadStoneThrow("hud_received_stones_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Hits = CreateText(p1Plate, "HitCount", SlotAnchor, 260f, 64);
        _p2Hits = CreateText(p2Plate, "HitCount", SlotAnchor, 260f, 64);
        _p1Hits.text = "0";
        _p2Hits.text = "0";

        // 타이머: 상단 중앙.
        RectTransform timerPlate = CreateImage(root, "TimerPlate", ArtAssets.LoadStoneThrow("hud_time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(matchSeconds).ToString();

        // 명중/회피/승리 같은 순간 피드백 - 화면 중앙 위쪽.
        _eventText = CreateText(root, "EventText", new Vector2(0.5f, 0.72f), 900f, 60);
        _eventText.text = "";
    }

    private static RectTransform CreateImage(Transform parent, string name, Sprite sprite,
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

    private static Text CreateText(RectTransform parent, string name, Vector2 anchor, float width, int fontSize)
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

    private static float HeightFor(Sprite sprite, float width)
    {
        if (sprite == null || sprite.rect.width <= 0f) return width * 0.32f;
        return width * sprite.rect.height / sprite.rect.width;
    }

    private void Update()
    {
        if (_eventTimer > 0f)
        {
            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f && _eventText != null) _eventText.text = "";
        }
    }

    public void SetHits(PlayerId player, int hits)
    {
        Text label = player == PlayerId.P1 ? _p1Hits : _p2Hits;
        if (label != null) label.text = hits.ToString();
    }

    public void SetTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(seconds).ToString();
    }

    public void ShowEvent(string text, float seconds = 0.6f)
    {
        if (_eventText == null) return;
        _eventText.text = text;
        _eventTimer = seconds;
    }
}

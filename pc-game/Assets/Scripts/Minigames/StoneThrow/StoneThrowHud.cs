// 돌던지기 HUD - image/games/stone_throw/hud/의 네임플레이트와
// image/common/ui/hud/의 공용 타이머 아트 위에 숫자만 얹는다.
// (v2 디자인은 게이지 없이 원형/사각 슬롯에 숫자를 채우는 방식 - hud_received_stones_p1/p2,
// time_remaining 미리보기 그대로). Canvas와 위젯은 씬에 저장되어 에디터에서 직접 편집한다.
// hud_angle/hud_power/hud_wind는 쓰지 않는다 - 건바운드류 포격 조작 전제로 그려진 것이라
// 실제 조작(손 들기 자동발사 + 머리 기울여 회피)과 맞지 않아 v2에서 아예 빠졌다.
//
// 체력바(P1/P2HealthBar)는 전용 아트가 아직 없어서 단색 Filled Image로 Awake()에서 항상
// 코드로 만든다(씬에 미리 배치된 다른 위젯과 달리 이것만 런타임 생성) - 나중에 전용 프레임
// 아트가 생기면 HudWidgets.CreateImage로 바꿔서 씬에 배치하면 된다.
using UnityEngine;
using UnityEngine.UI;

public class StoneThrowHud : MonoBehaviour
{
    // image/games/stone_throw/previews/ui_layout.png 기준 참조 해상도.
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    // 두 플레이트(hud_received_stones_p1/p2) 모두 같은 레이아웃(좌: 텍스트, 우: 큰 원형 슬롯)이라
    // 슬롯 중심 앵커도 공통으로 쓴다 - 실측(에셋 미리보기) 기준 원 중심 위치.
    private static readonly Vector2 SlotAnchor = new Vector2(0.775f, 0.51f);

    [Header("씬에 배치된 UI")]
    [SerializeField] private Text _p1Hits, _p2Hits, _timer, _eventText;
    private float _eventTimer;
    private Image _p1HealthFill, _p2HealthFill;

    public static StoneThrowHud Build(float matchSeconds)
    {
        var canvasGo = new GameObject("StoneThrowHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
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
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadStoneThrow("hud_received_stones_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadStoneThrow("hud_received_stones_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Hits = HudWidgets.CreateText(p1Plate, "HitCount", SlotAnchor, 260f, 64);
        _p2Hits = HudWidgets.CreateText(p2Plate, "HitCount", SlotAnchor, 260f, 64);
        _p1Hits.text = "0";
        _p2Hits.text = "0";

        // 타이머: 상단 중앙.
        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadStoneThrow("hud_time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(matchSeconds).ToString();

        // 명중/회피/승리 같은 순간 피드백 - 화면 중앙 위쪽.
        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.72f), 900f, 60);
        _eventText.text = "";
    }

    private void Awake()
    {
        var root = (RectTransform)transform;
        // 네임플레이트 폭 620 기준 실측 높이(약 271px) 아래에 여백을 두고 배치.
        _p1HealthFill = BuildHealthBar(root, "P1HealthBar", new Vector2(0f, 1f), new Vector2(30f, -320f), flip: false);
        _p2HealthFill = BuildHealthBar(root, "P2HealthBar", new Vector2(1f, 1f), new Vector2(-30f, -320f), flip: true);
    }

    // 전용 아트가 없어 트랙(반투명 검정) + 채움(플레이어 색) 두 장짜리 단색 게이지로 만든다.
    // flip=true(P2)는 오른쪽 정렬이라 채움이 오른쪽에서 왼쪽으로 줄어들게 fillOrigin을 반대로.
    private static Image BuildHealthBar(RectTransform root, string name, Vector2 anchor, Vector2 offset, bool flip)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(320f, 26f);
        var track = go.AddComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.45f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(go.transform, false);
        var frt = fillGo.AddComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(3f, 3f);
        frt.offsetMax = new Vector2(-3f, -3f);
        var fill = fillGo.AddComponent<Image>();
        fill.color = flip ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.25f, 0.55f, 0.95f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = flip ? 1 : 0;
        fill.fillAmount = 1f;
        return fill;
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

    // ratio: 1 = 풀피, 0 = 사망.
    public void SetHealth(PlayerId player, float ratio)
    {
        Image bar = player == PlayerId.P1 ? _p1HealthFill : _p2HealthFill;
        if (bar != null) bar.fillAmount = Mathf.Clamp01(ratio);
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

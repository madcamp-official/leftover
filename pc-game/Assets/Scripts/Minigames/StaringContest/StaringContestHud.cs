// 눈빛싸움 HUD - image/games/staring_contest/hud/의 "눈 감음 위험" 네임플레이트 + 실제
// 채워지는 게이지(eye_danger_fill, Image.Filled), 공용 타이머, 시작 규칙 배너.
using UnityEngine;
using UnityEngine.UI;

public class StaringContestHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    // 네임플레이트 안에서 게이지가 차지하는 위치/크기 비율(실측 - hud_eye_danger_p1/p2 기준).
    // Play 중에 Inspector에서 드래그해서 조정 가능 - Update()에서 매 프레임 다시 적용한다.
    [Header("게이지 바(채워지는 부분) 위치/크기 (플레이트 이미지 안 0~1 비율)")]
    [SerializeField] private Vector2 gaugeAnchorMin = new Vector2(0.655f, 0.075f);
    [SerializeField] private Vector2 gaugeAnchorMax = new Vector2(1.160f, 0.520f);
    [SerializeField] private Vector2 gaugeScale = new Vector2(1f, 0.5f);

    [Header("게이지 트랙(뒤 배경) 위치/크기 (플레이트 이미지 안 0~1 비율)")]
    [SerializeField] private Vector2 trackAnchorMin = new Vector2(0.655f, 0.075f);
    [SerializeField] private Vector2 trackAnchorMax = new Vector2(1.160f, 0.520f);
    [SerializeField] private Vector2 trackScale = new Vector2(1f, 0.5f);
    [SerializeField] private Color trackColor = new Color32(48, 25, 13, 255);

    [Header("씬에 배치된 UI")]
    [SerializeField] private Image _p1Gauge, _p2Gauge;
    [SerializeField] private Text _timer, _eventText;
    [SerializeField] private RectTransform _ruleBanner;

    private void Start()
    {
        // 판 PNG 안에 그려진 슬롯 모양과 무관하게 실제 게이지 트랙은 양쪽 모두
        // 같은 스프라이트와 같은 Rect를 사용한다.
        EnsureCommonGaugeTrack(_p1Gauge);
        EnsureCommonGaugeTrack(_p2Gauge);
    }

    // Inspector에서 Play 중에 gaugeAnchorMin/Max, trackAnchorMin/Max, trackColor를 조정하면
    // 바로 눈으로 확인할 수 있도록.
    private void Update()
    {
        ApplyGaugeAnchor(_p1Gauge);
        ApplyGaugeAnchor(_p2Gauge);
    }

    private void ApplyGaugeAnchor(Image gauge)
    {
        if (gauge == null) return;
        SetAnchorRect(gauge.rectTransform, gaugeAnchorMin, gaugeAnchorMax, gaugeScale);
        Transform track = gauge.transform.parent != null ? gauge.transform.parent.Find("GaugeTrack") : null;
        if (track == null) return;
        SetAnchorRect((RectTransform)track, trackAnchorMin, trackAnchorMax, trackScale);
        Image trackImage = track.GetComponent<Image>();
        if (trackImage != null) trackImage.color = trackColor;
    }

    private static void SetAnchorRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 scale)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = new Vector3(scale.x, scale.y, 1f);
    }

    public static StaringContestHud Build(float maxMatchSeconds)
    {
        var canvasGo = new GameObject("StaringContestHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<StaringContestHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), maxMatchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float maxMatchSeconds)
    {
        const float plateWidth = 700f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadStaringContest("hud_eye_danger_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadStaringContest("hud_eye_danger_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Gauge = CreateGauge(p1Plate);
        _p2Gauge = CreateGauge(p2Plate);

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadUi("time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 420f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 260f, 40);
        _timer.text = Mathf.CeilToInt(maxMatchSeconds).ToString();

        _ruleBanner = HudWidgets.CreateImage(root, "RuleBanner", ArtAssets.LoadStaringContest("ui_rule_banner"),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), 900f);

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.7f), 900f, 60);
        _eventText.text = "";
    }

    private Image CreateGauge(RectTransform plate)
    {
        var go = new GameObject("Gauge");
        go.transform.SetParent(plate, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchorRect(rt, gaugeAnchorMin, gaugeAnchorMax, gaugeScale);

        var image = go.AddComponent<Image>();
        image.sprite = ArtAssets.LoadStaringContest("hud_eye_danger_fill");
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 0f;
        return image;
    }

    private void EnsureCommonGaugeTrack(Image gauge)
    {
        if (gauge == null) return;

        RectTransform gaugeRect = gauge.rectTransform;
        SetAnchorRect(gaugeRect, gaugeAnchorMin, gaugeAnchorMax, gaugeScale);
        gauge.preserveAspect = false;
        gauge.raycastTarget = false;

        Transform plate = gaugeRect.parent;
        Transform existing = plate.Find("GaugeTrack");
        Image track;
        if (existing != null)
        {
            track = existing.GetComponent<Image>();
        }
        else
        {
            var go = new GameObject("GaugeTrack");
            go.transform.SetParent(plate, false);
            go.AddComponent<RectTransform>();
            track = go.AddComponent<Image>();
        }

        RectTransform trackRect = track.rectTransform;
        SetAnchorRect(trackRect, trackAnchorMin, trackAnchorMax, trackScale);
        track.sprite = ArtAssets.LoadStaringContest("hud_eye_danger_fill");
        track.type = Image.Type.Simple;
        track.preserveAspect = false;
        track.color = trackColor;
        track.raycastTarget = false;

        // 트랙은 채워지는 Gauge 바로 뒤에 둔다.
        track.transform.SetSiblingIndex(gauge.transform.GetSiblingIndex());
        gauge.transform.SetAsLastSibling();
    }

    // ratio: 0(안전, 눈 뜸) ~ 1(위험, 곧 눈 감김 판정).
    public void SetDanger(PlayerId player, float ratio)
    {
        Image gauge = player == PlayerId.P1 ? _p1Gauge : _p2Gauge;
        if (gauge != null) gauge.fillAmount = Mathf.Clamp01(ratio);
    }

    public void SetTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString();
    }

    public void HideRuleBanner() => _ruleBanner?.gameObject.SetActive(false);

    public void ShowEvent(string text)
    {
        if (_eventText != null) _eventText.text = text;
    }
}

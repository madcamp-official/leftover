// 눈빛싸움 HUD - image/games/staring_contest/hud/의 "눈 감음 위험" 네임플레이트 + 실제
// 채워지는 게이지(eye_danger_fill, Image.Filled), 공용 타이머, 시작 규칙 배너.
using UnityEngine;
using UnityEngine.UI;

public class StaringContestHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    // 네임플레이트 안에서 게이지가 차지하는 위치/크기 비율(실측 - hud_eye_danger_p1/p2 기준).
    private static readonly Vector2 GaugeAnchorMin = new Vector2(0.4f, 0.32f);
    private static readonly Vector2 GaugeAnchorMax = new Vector2(0.96f, 0.62f);

    [Header("씬에 배치된 UI")]
    [SerializeField] private Image _p1Gauge, _p2Gauge;
    [SerializeField] private Text _timer, _eventText;
    [SerializeField] private RectTransform _ruleBanner;

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

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadStaringContest("hud_time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 420f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 260f, 40);
        _timer.text = Mathf.CeilToInt(maxMatchSeconds).ToString();

        _ruleBanner = HudWidgets.CreateImage(root, "RuleBanner", ArtAssets.LoadStaringContest("ui_rule_banner"),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), 900f);

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.7f), 900f, 60);
        _eventText.text = "";
    }

    private static Image CreateGauge(RectTransform plate)
    {
        var go = new GameObject("Gauge");
        go.transform.SetParent(plate, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = GaugeAnchorMin;
        rt.anchorMax = GaugeAnchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.sprite = ArtAssets.LoadStaringContest("hud_eye_danger_fill");
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 0f;
        return image;
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

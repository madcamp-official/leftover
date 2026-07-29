// 깃털날기 HUD - P1/P2 네임플레이트 안에 "현재 높이"를 나타내는 세로줄(Line)이 트랙
// 위에서 좌우로 움직인다(왼쪽=height 0, 가운데=중간 높이, 오른쪽=maxHeight). 공용 타이머,
// 이벤트 텍스트도 포함.
using UnityEngine;
using UnityEngine.UI;

public class FeatherFlightHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    [Header("높이 트랙(세로줄이 움직이는 가로 범위) 설정")]
    [SerializeField] private float gaugeTrackWidth = 380f;
    [SerializeField] private float gaugeLineWidth = 8f;
    [SerializeField] private float gaugeLineHeight = 60f;

    [Header("씬에 배치된 UI")]
    [SerializeField] private RectTransform _p1GaugeLine, _p2GaugeLine;
    [SerializeField] private Text _timer, _eventText;
    private float _eventTimer;

    public static FeatherFlightHud Build(float maxMatchSeconds)
    {
        var canvasGo = new GameObject("FeatherFlightHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<FeatherFlightHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), maxMatchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float maxMatchSeconds)
    {
        const float plateWidth = 500f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadFeatherFlight("hud_height_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadFeatherFlight("hud_height_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1GaugeLine = CreateGaugeTrack(p1Plate, new Color(1f, .4f, .35f));
        _p2GaugeLine = CreateGaugeTrack(p2Plate, new Color(.35f, .6f, 1f));

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadUi("time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(maxMatchSeconds).ToString();

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.7f), 1000f, 60);
        _eventText.text = "";
    }

    // 트랙(가로선) + 그 위를 좌우로 움직이는 세로줄을 만들고, 세로줄의 RectTransform을 반환한다.
    private RectTransform CreateGaugeTrack(RectTransform plate, Color lineColor)
    {
        var trackGo = new GameObject("Track");
        trackGo.transform.SetParent(plate, false);
        var trackRt = trackGo.AddComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(.5f, .5f);
        trackRt.anchorMax = new Vector2(.5f, .5f);
        trackRt.sizeDelta = new Vector2(gaugeTrackWidth, 6f);
        trackRt.anchoredPosition = Vector2.zero;
        var trackImage = trackGo.AddComponent<Image>();
        trackImage.color = new Color(1f, 1f, 1f, .35f);
        trackImage.raycastTarget = false;

        var centerTickGo = new GameObject("CenterTick");
        centerTickGo.transform.SetParent(plate, false);
        var centerTickRt = centerTickGo.AddComponent<RectTransform>();
        centerTickRt.anchorMin = new Vector2(.5f, .5f);
        centerTickRt.anchorMax = new Vector2(.5f, .5f);
        centerTickRt.sizeDelta = new Vector2(3f, gaugeLineHeight * .6f);
        centerTickRt.anchoredPosition = Vector2.zero;
        var centerTickImage = centerTickGo.AddComponent<Image>();
        centerTickImage.color = new Color(1f, 1f, 1f, .5f);
        centerTickImage.raycastTarget = false;

        var lineGo = new GameObject("Line");
        lineGo.transform.SetParent(plate, false);
        var lineRt = lineGo.AddComponent<RectTransform>();
        lineRt.anchorMin = new Vector2(.5f, .5f);
        lineRt.anchorMax = new Vector2(.5f, .5f);
        lineRt.sizeDelta = new Vector2(gaugeLineWidth, gaugeLineHeight);
        lineRt.anchoredPosition = Vector2.zero;
        var lineImage = lineGo.AddComponent<Image>();
        lineImage.color = lineColor;
        lineImage.raycastTarget = false;

        return lineRt;
    }

    // ratio: 0(바닥, 패배 직전) ~ 1(maxHeight, 최고 높이). 세로줄이 트랙 왼쪽 끝(0) ~
    // signedRatio: -1(화면 아래쪽 끝) ~ 0(절벽 Y, 트랙 가운데) ~ +1(화면 위쪽 끝).
    public void SetHeight(PlayerId player, float signedRatio)
    {
        RectTransform line = player == PlayerId.P1 ? _p1GaugeLine : _p2GaugeLine;
        if (line == null) return;
        float x = Mathf.Clamp(signedRatio, -1f, 1f) * (gaugeTrackWidth * .5f);
        line.anchoredPosition = new Vector2(x, line.anchoredPosition.y);
    }

    public void SetTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString();
    }

    public void ShowEvent(string text, float seconds = 2f)
    {
        if (_eventText == null) return;
        _eventText.text = text;
        _eventTimer = seconds;
    }

    private void Update()
    {
        if (_eventTimer <= 0f) return;
        _eventTimer -= Time.deltaTime;
        if (_eventTimer <= 0f && _eventText != null) _eventText.text = "";
    }
}

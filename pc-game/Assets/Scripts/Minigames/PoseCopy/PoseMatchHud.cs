// 자세따라하기 HUD - image/games/pose_match/hud/의 "남은 발판" 네임플레이트(4칸 아이콘이
// 이미 그림에 그려져 있음) + 공용 타이머. 남은 개수는 플레이트 아래에 숫자로 보조 표시한다.
using UnityEngine;
using UnityEngine.UI;

public class PoseMatchHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    [Header("씬에 배치된 UI")]
    [SerializeField] private Text _p1Footholds, _p2Footholds, _timer, _eventText;
    private float _eventTimer;

    public static PoseMatchHud Build(float matchSeconds)
    {
        var canvasGo = new GameObject("PoseMatchHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<PoseMatchHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), matchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float matchSeconds)
    {
        const float plateWidth = 560f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadPoseMatch("hud_remaining_footholds_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadPoseMatch("hud_remaining_footholds_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Footholds = HudWidgets.CreateText(p1Plate, "Footholds", new Vector2(0.5f, -0.35f), 300f, 40);
        _p2Footholds = HudWidgets.CreateText(p2Plate, "Footholds", new Vector2(0.5f, -0.35f), 300f, 40);
        _p1Footholds.text = "";
        _p2Footholds.text = "";

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadPoseMatch("hud_time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(matchSeconds).ToString();

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.68f), 1200f, 44);
        _eventText.text = "";
    }

    private void Update()
    {
        if (_eventTimer > 0f)
        {
            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f && _eventText != null) _eventText.text = "";
        }
    }

    public void SetFootholds(PlayerId player, int remaining)
    {
        Text label = player == PlayerId.P1 ? _p1Footholds : _p2Footholds;
        if (label != null) label.text = $"남은 발판 {remaining}";
    }

    public void SetTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(seconds).ToString();
    }

    public void ShowEvent(string text, float seconds = 1.2f)
    {
        if (_eventText == null) return;
        _eventText.text = text;
        _eventTimer = seconds;
    }
}

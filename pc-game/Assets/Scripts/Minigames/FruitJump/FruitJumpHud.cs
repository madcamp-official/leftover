// 과일따기 HUD - image/games/fruit_jump/hud/의 "점수" 네임플레이트 + 공용 타이머.
using UnityEngine;
using UnityEngine.UI;

public class FruitJumpHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;
    // hud_score_p1/p2.png 안 짙은 원(숫자 자리)의 실제 중심을 알파/색상 기준으로 직접 측정한
    // 값(원본 PNG 픽셀 기준 플러드필로 원 영역을 찾아 bbox 중심을 정규화했다) - 두 그림이
    // 픽셀 단위로 완전히 똑같지 않아서 각각 따로 잰 값을 쓴다.
    //
    // P2 그림을 좌우로 뒤집어서 두 판을 화면 중앙 대칭으로 맞춰볼까 했지만, hud_score_p2.png
    // 안에 "플레이어2"·"점수" 글자가 이미 그림으로 그려져 있어서 그림을 뒤집으면 그 글자까지
    // 같이 좌우反전돼 버린다 - 그래서 뒤집지 않고 원본 그대로 쓴다(P1/P2 판 모두 원이
    // 판 오른쪽에 있는 비대칭 배치를 그대로 유지).
    private static readonly Vector2 P1SlotAnchor = new Vector2(0.725f, 0.467f);
    private static readonly Vector2 P2SlotAnchor = new Vector2(0.754f, 0.497f);

    [Header("씬에 배치된 UI")]
    [SerializeField] private Text _p1Score, _p2Score, _timer, _eventText;
    private float _eventTimer;

    public static FruitJumpHud Build(float matchSeconds)
    {
        var canvasGo = new GameObject("FruitJumpHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<FruitJumpHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), matchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float matchSeconds)
    {
        const float plateWidth = 620f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadFruitJump("hud_score_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadFruitJump("hud_score_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Score = HudWidgets.CreateText(p1Plate, "Score", P1SlotAnchor, 260f, 64);
        _p2Score = HudWidgets.CreateText(p2Plate, "Score", P2SlotAnchor, 260f, 64);
        _p1Score.text = "0";
        _p2Score.text = "0";

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadUi("time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(matchSeconds).ToString();

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.72f), 900f, 60);
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

    public void SetScore(PlayerId player, int score)
    {
        Text label = player == PlayerId.P1 ? _p1Score : _p2Score;
        if (label != null) label.text = score.ToString();
    }

    public void SetTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(seconds).ToString();
    }

    public void ShowEvent(string text, float seconds = 0.8f)
    {
        if (_eventText == null) return;
        _eventText.text = text;
        _eventTimer = seconds;
    }
}

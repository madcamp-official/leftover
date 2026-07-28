// 과일따기 HUD - image/games/fruit_jump/hud/의 "점수" 네임플레이트 + 공용 타이머.
using UnityEngine;
using UnityEngine.UI;

public class FruitJumpHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;
    private static readonly Vector2 SlotAnchor = new Vector2(0.775f, 0.51f);

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

        _p1Score = HudWidgets.CreateText(p1Plate, "Score", SlotAnchor, 260f, 64);
        _p2Score = HudWidgets.CreateText(p2Plate, "Score", SlotAnchor, 260f, 64);
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

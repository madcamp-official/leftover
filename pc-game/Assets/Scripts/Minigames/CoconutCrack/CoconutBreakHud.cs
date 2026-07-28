// 코코넛깨기 HUD - image/games/coconut_break/hud/의 "타격 횟수" 네임플레이트와
// image/common/ui/hud/의 공용 타이머 위에 숫자만 얹는다. Canvas는 씬에 저장되어 직접 편집 가능.
using UnityEngine;
using UnityEngine.UI;

public class CoconutBreakHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;
    private static readonly Vector2 SlotAnchor = new Vector2(0.775f, 0.51f);

    [Header("씬에 배치된 UI")]
    [SerializeField] private Text _p1Hits, _p2Hits, _timer, _eventText;
    private float _eventTimer;

    public static CoconutBreakHud Build(float matchSeconds)
    {
        var canvasGo = new GameObject("CoconutBreakHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<CoconutBreakHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), matchSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float matchSeconds)
    {
        const float plateWidth = 620f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadCoconutBreak("hud_hit_count_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadCoconutBreak("hud_hit_count_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Hits = HudWidgets.CreateText(p1Plate, "HitCount", SlotAnchor, 260f, 64);
        _p2Hits = HudWidgets.CreateText(p2Plate, "HitCount", SlotAnchor, 260f, 64);
        _p1Hits.text = "0";
        _p2Hits.text = "0";

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadCoconutBreak("hud_time_remaining"),
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

    public void SetHits(PlayerId player, int hits)
    {
        Text label = player == PlayerId.P1 ? _p1Hits : _p2Hits;
        if (label != null) label.text = hits.ToString();
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

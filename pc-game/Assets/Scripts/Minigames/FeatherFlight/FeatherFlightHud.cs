// 깃털날기 HUD - 캐릭터 높이는 화면에서 캐릭터가 직접 오르내리는 것으로 보이므로 별도
// 게이지는 없다. 공용 타이머, 이벤트 텍스트만 표시한다.
using UnityEngine;
using UnityEngine.UI;

public class FeatherFlightHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    [Header("씬에 배치된 UI")]
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
        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadUi("time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = Mathf.CeilToInt(maxMatchSeconds).ToString();

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.7f), 1000f, 60);
        _eventText.text = "";
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

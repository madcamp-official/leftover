// 돌 or 바나나 HUD - image/games/stone_or_banana/hud/의 "이빨/포만감" 네임플레이트,
// image/games/stone_or_banana/ui/의 던지기/받기 차례 안내판, 공용 타이머.
using UnityEngine;
using UnityEngine.UI;

public class StoneOrBananaHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    [Header("씬에 배치된 UI")]
    [SerializeField] private Text _p1Status, _p2Status, _timer;
    [SerializeField] private RectTransform _throwPrompt, _receivePrompt;

    public static StoneOrBananaHud Build()
    {
        var canvasGo = new GameObject("StoneOrBananaHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<StoneOrBananaHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>());
        return hud;
    }

    private void BuildWidgets(RectTransform root)
    {
        const float plateWidth = 620f;
        RectTransform p1Plate = HudWidgets.CreateImage(root, "P1Plate", ArtAssets.LoadStoneOrBanana("hud_status_p1"),
            new Vector2(0f, 1f), new Vector2(30f, -30f), plateWidth);
        RectTransform p2Plate = HudWidgets.CreateImage(root, "P2Plate", ArtAssets.LoadStoneOrBanana("hud_status_p2"),
            new Vector2(1f, 1f), new Vector2(-30f, -30f), plateWidth);

        _p1Status = HudWidgets.CreateText(p1Plate, "Status", new Vector2(0.5f, 0.5f), 560f, 34);
        _p2Status = HudWidgets.CreateText(p2Plate, "Status", new Vector2(0.5f, 0.5f), 560f, 34);

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadStoneOrBanana("hud_time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 380f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 250f, 40);
        _timer.text = "";

        _throwPrompt = HudWidgets.CreateImage(root, "ThrowPrompt", ArtAssets.LoadStoneOrBanana("ui_throw_turn_prompt"),
            new Vector2(0.5f, 0.5f), Vector2.zero, 640f);
        _receivePrompt = HudWidgets.CreateImage(root, "ReceivePrompt", ArtAssets.LoadStoneOrBanana("ui_receive_turn_prompt"),
            new Vector2(0.5f, 0.5f), Vector2.zero, 640f);
        _throwPrompt.anchoredPosition = new Vector2(0f, 250f);
        _receivePrompt.anchoredPosition = new Vector2(0f, 250f);
        _throwPrompt.gameObject.SetActive(false);
        _receivePrompt.gameObject.SetActive(false);
    }

    public void SetStatus(PlayerId player, float teeth, float maxTeeth, float fullness, float maxFullness)
    {
        Text label = player == PlayerId.P1 ? _p1Status : _p2Status;
        if (label != null) label.text = $"이빨 {teeth:F0}/{maxTeeth:F0}   포만감 {fullness:F0}/{maxFullness:F0}";
    }

    public void ShowThrowPrompt(bool show) => _throwPrompt?.gameObject.SetActive(show);
    public void ShowReceivePrompt(bool show) => _receivePrompt?.gameObject.SetActive(show);

    public void SetTurnTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString();
    }
}

// 돌 or 바나나 HUD - image/games/stone_or_banana/hud/의 "이빨/포만감" 네임플레이트,
// image/games/stone_or_banana/ui/의 던지기/받기 차례 안내판, 공용 타이머.
using UnityEngine;
using UnityEngine.UI;

public class StoneOrBananaHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    private static readonly Vector2[] ToothAnchors =
    {
        new(.43f, .51f), new(.58f, .51f), new(.73f, .51f),
    };
    private static readonly Vector2[] FullnessAnchors =
    {
        new(.38f, .25f), new(.48f, .25f), new(.585f, .25f), new(.69f, .25f), new(.795f, .25f),
    };

    [Header("씬에 배치된 UI")]
    [SerializeField] private Image[] _p1Teeth = new Image[3];
    [SerializeField] private Image[] _p2Teeth = new Image[3];
    [SerializeField] private Image[] _p1Fullness = new Image[5];
    [SerializeField] private Image[] _p2Fullness = new Image[5];
    [SerializeField] private Sprite _toothIntactSprite, _bananaSprite;
    [SerializeField] private Text _timer, _turnRoleText, _eventText;
    [SerializeField] private RectTransform _throwPrompt, _receivePrompt;
    private float _eventTimer;

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

        _toothIntactSprite = ArtAssets.LoadStoneOrBanana("icon_tooth_intact");
        _bananaSprite = ArtAssets.LoadProp("banana");
        BuildStatusSlots(p1Plate, "P1", _p1Teeth, _p1Fullness);
        BuildStatusSlots(p2Plate, "P2", _p2Teeth, _p2Fullness);

        RectTransform timerPlate = HudWidgets.CreateImage(root, "TimerPlate", ArtAssets.LoadUi("time_remaining"),
            new Vector2(0.5f, 1f), new Vector2(0f, -24f), 480f);
        _timer = HudWidgets.CreateText(timerPlate, "TimerText", new Vector2(0.72f, 0.5f), 300f, 50);
        _timer.text = "";

        // 공격/수비는 같은 3초 동안 동시에 선택하므로 두 안내판을 겹치지 않게 나란히 둔다.
        // 초기 x는 임시값 - 실제 위치는 SetTurnRoles가 던지는/받는 플레이어의 실제 화면
        // 위치(p1FrontView/p2FrontView)에 맞춰 매 턴 다시 잡는다.
        _throwPrompt = HudWidgets.CreateImage(root, "ThrowPrompt", ArtAssets.LoadStoneOrBanana("ui_throw_turn_prompt"),
            new Vector2(0.5f, 0.5f), new Vector2(-PromptSideOffset, 260f), 680f);
        _receivePrompt = HudWidgets.CreateImage(root, "ReceivePrompt", ArtAssets.LoadStoneOrBanana("ui_receive_turn_prompt"),
            new Vector2(0.5f, 0.5f), new Vector2(PromptSideOffset, 260f), 680f);
        _throwPrompt.gameObject.SetActive(false);
        _receivePrompt.gameObject.SetActive(false);

        _turnRoleText = HudWidgets.CreateText(root, "TurnRoleText", new Vector2(0.5f, 0.78f), 1000f, 62);
        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.66f), 1100f, 58);
        _turnRoleText.text = "";
        _eventText.text = "";
    }

    private void BuildStatusSlots(RectTransform plate, string prefix, Image[] teeth, Image[] fullness)
    {
        for (int i = 0; i < teeth.Length; i++)
        {
            RectTransform slot = HudWidgets.CreateImage(plate, $"{prefix}Tooth_{i + 1}",
                _toothIntactSprite, ToothAnchors[i], Vector2.zero, 68f);
            teeth[i] = slot.GetComponent<Image>();
        }
        for (int i = 0; i < fullness.Length; i++)
        {
            RectTransform slot = HudWidgets.CreateImage(plate, $"{prefix}Fullness_{i + 1}",
                _bananaSprite, FullnessAnchors[i], Vector2.zero, 62f);
            fullness[i] = slot.GetComponent<Image>();
            fullness[i].gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (_eventTimer <= 0f) return;
        _eventTimer -= Time.deltaTime;
        if (_eventTimer <= 0f && _eventText != null) _eventText.text = "";
    }

    public void SetStatus(PlayerId player, int teeth, int maxTeeth, int fullness, int maxFullness)
    {
        Image[] toothSlots = player == PlayerId.P1 ? _p1Teeth : _p2Teeth;
        Image[] fullnessSlots = player == PlayerId.P1 ? _p1Fullness : _p2Fullness;
        int visibleTeeth = Mathf.Clamp(teeth, 0, Mathf.Min(maxTeeth, toothSlots.Length));
        for (int i = 0; i < toothSlots.Length; i++)
        {
            if (toothSlots[i] == null) continue;
            toothSlots[i].sprite = _toothIntactSprite;
            // 왼쪽부터 남은 이빨만 표시한다. 이빨을 잃으면 오른쪽 칸부터 사라진다.
            toothSlots[i].gameObject.SetActive(i < visibleTeeth);
        }
        for (int i = 0; i < fullnessSlots.Length; i++)
        {
            if (fullnessSlots[i] == null) continue;
            fullnessSlots[i].sprite = _bananaSprite;
            fullnessSlots[i].gameObject.SetActive(i < Mathf.Clamp(fullness, 0, maxFullness));
        }
    }

    public void ShowDecisionPrompts(bool show)
    {
        _throwPrompt?.gameObject.SetActive(show);
        _receivePrompt?.gameObject.SetActive(show);
    }

    // 화면상 P1은 왼쪽, P2는 오른쪽에 선다(실측 확인). 안내판을 "왼쪽=던지기/오른쪽=받기"로
    // 고정해두면 턴마다 실제 캐릭터 위치와 안 맞아 헷갈리므로, 이번 턴 던지는/받는 플레이어가
    // 서 있는 쪽으로 매번 다시 배치한다.
    private const float PromptSideOffset = 600f;

    public void SetTurnRoles(PlayerId thrower, PlayerId receiver)
    {
        if (_turnRoleText != null)
            _turnRoleText.text = $"{Label(thrower)} 던지기  ·  {Label(receiver)} 받기";

        SetPromptSide(_throwPrompt, thrower);
        SetPromptSide(_receivePrompt, receiver);
    }

    private static void SetPromptSide(RectTransform prompt, PlayerId player)
    {
        if (prompt == null) return;
        float x = player == PlayerId.P1 ? -PromptSideOffset : PromptSideOffset;
        Vector2 pos = prompt.anchoredPosition;
        prompt.anchoredPosition = new Vector2(x, pos.y);
    }

    public void ShowEvent(string message, float seconds = .8f)
    {
        if (_eventText == null) return;
        _eventText.text = message;
        _eventTimer = seconds;
    }

    public void SetTurnTimeRemaining(float seconds)
    {
        if (_timer != null) _timer.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString();
    }

    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";
}

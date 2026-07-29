// 소리지르기 HUD - image/games/scream_duel/hud/의 나무판(required_level) 안에
// 턴 소유자 색깔 채움 바(current_level_fill_p1=파랑/
// p2=빨강, Image.Filled)를 겹쳐 그린다. 숫자 점수가 없는 게임이라 "이번에 넘어야 할 음량"과
// "지금 내 음량"을 실시간 게이지로 보여주는 게 핵심(문서 6장).
using UnityEngine;
using UnityEngine.UI;

public class ScreamDuelHud : MonoBehaviour
{
    private const float RefWidth = 2048f;
    private const float RefHeight = 1152f;

    [Header("씬에 배치된 UI")]
    [SerializeField] private Image _levelFill;
    [SerializeField] private RectTransform _requiredMarker;
    [SerializeField] private Text _turnText, _turnTimerText, _eventText, _dbText;
    [SerializeField] private Sprite _p1FillSprite;
    [SerializeField] private Sprite _p2FillSprite;
    private float _eventTimer;
    private Vector2 _trackAnchorMin, _trackAnchorMax; // SetLevels에서 마커 x 위치 계산에 재사용

    private void Awake() => EnsureFillSprites();

    public static ScreamDuelHud Build(float turnSeconds)
    {
        var canvasGo = new GameObject("ScreamDuelHud");
        var canvas = canvasGo.AddComponent<Canvas>();
        HudWidgets.ConfigureForGameCamera(canvas);
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var hud = canvasGo.AddComponent<ScreamDuelHud>();
        hud.BuildWidgets(canvasGo.GetComponent<RectTransform>(), turnSeconds);
        return hud;
    }

    private void BuildWidgets(RectTransform root, float turnSeconds)
    {
        EnsureFillSprites();

        RectTransform plate = HudWidgets.CreateImage(root, "LevelPlate", ArtAssets.LoadScreamDuel("hud_required_level"),
            new Vector2(0.5f, 1f), new Vector2(0f, -30f), 1000f);

        // 나무판 안쪽 실제 트랙 영역(실측 - hud_required_level.png 기준 안쪽 어두운 부분).
        _trackAnchorMin = new Vector2(0.06f, 0.28f);
        _trackAnchorMax = new Vector2(0.94f, 0.72f);
        Vector2 trackAnchorMin = _trackAnchorMin;
        Vector2 trackAnchorMax = _trackAnchorMax;

        var fillGo = new GameObject("CurrentFill");
        fillGo.transform.SetParent(plate, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        SetAnchorRect(fillRt, trackAnchorMin, trackAnchorMax);
        _levelFill = fillGo.AddComponent<Image>();
        _levelFill.sprite = _p1FillSprite;
        _levelFill.type = Image.Type.Filled;
        _levelFill.fillMethod = Image.FillMethod.Horizontal;
        _levelFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _levelFill.fillAmount = 0f;
        _levelFill.preserveAspect = false;
        _levelFill.raycastTarget = false;

        // 이미지에 숫자를 굽지 않고 실시간 음량을 중앙 텍스트로 표시한다.
        // 원본 dBFS는 음수라 직관적이지 않으므로 게임 표시용 0~100 dB로 나타낸다.
        _dbText = HudWidgets.CreateText(plate, "DbText", new Vector2(0.5f, 0.5f), 420f, 48);
        _dbText.text = "0 dB";

        // 이번에 넘어야 할 기준 음량 위치를 트랙 위 얇은 세로선으로 표시.
        var markerGo = new GameObject("RequiredMarker");
        markerGo.transform.SetParent(plate, false);
        _requiredMarker = markerGo.AddComponent<RectTransform>();
        _requiredMarker.anchorMin = _requiredMarker.anchorMax = new Vector2(trackAnchorMin.x, 0.5f);
        _requiredMarker.sizeDelta = new Vector2(8f, 130f);
        var markerImage = markerGo.AddComponent<Image>();
        markerImage.sprite = MakeSolidSprite();
        markerImage.color = new Color(1f, 0.95f, 0.7f, 0.95f);
        markerImage.raycastTarget = false;

        _turnText = HudWidgets.CreateText(root, "TurnText", new Vector2(0.5f, 0.82f), 1000f, 54);
        _turnTimerText = HudWidgets.CreateText(plate, "TurnTimerText", new Vector2(1.06f, 0.5f), 200f, 48);

        _eventText = HudWidgets.CreateText(root, "EventText", new Vector2(0.5f, 0.66f), 1100f, 64);
        _eventText.text = "";

        _turnTimerText.text = Mathf.CeilToInt(turnSeconds).ToString();
    }

    private static void SetAnchorRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // 마커/트랙용 1x1 흰 텍스처 - 별도 원본 이미지 없이 색만 입혀 쓴다.
    private static Sprite MakeSolidSprite()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }

    // 턴이 바뀔 때 채움 바 색(P1=파랑/P2=빨강)과 턴 안내 문구를 갱신한다.
    public void SetTurn(PlayerId owner)
    {
        EnsureFillSprites();
        if (_levelFill != null)
        {
            _levelFill.color = Color.white;
            _levelFill.sprite = owner == PlayerId.P1 ? _p1FillSprite : _p2FillSprite;
        }
        if (_turnText != null) _turnText.text = $"{owner} 턴 - 상대보다 크게 질러라!";
    }

    private void EnsureFillSprites()
    {
        if (_p1FillSprite == null)
            _p1FillSprite = ArtAssets.LoadScreamDuel("hud_current_level_fill_p1");
        if (_p2FillSprite == null)
            _p2FillSprite = ArtAssets.LoadScreamDuel("hud_current_level_fill_p2");
    }

    // requiredLevel/currentLevel/peakLevel 전부 0~1 정규화 음량. peakLevel은 지금 마커로
    // 따로 표시하지 않는다(문서상 "반투명 마커로 같이 표시"는 TODO) - 실시간 채움만 우선 반영.
    public void SetLevels(float requiredLevel, float currentLevel, float peakLevel)
    {
        float normalizedLevel = Mathf.Clamp01(currentLevel);
        if (_levelFill != null) _levelFill.fillAmount = normalizedLevel;
        if (_dbText != null)
        {
            int displayDb = Mathf.RoundToInt(normalizedLevel * 100f);
            _dbText.text = $"{displayDb} dB";
        }
        if (_requiredMarker != null)
        {
            float x = Mathf.Lerp(_trackAnchorMin.x, _trackAnchorMax.x, Mathf.Clamp01(requiredLevel));
            _requiredMarker.anchorMin = new Vector2(x, _requiredMarker.anchorMin.y);
            _requiredMarker.anchorMax = new Vector2(x, _requiredMarker.anchorMax.y);
        }
    }

    public void SetTurnTimeRemaining(float seconds)
    {
        if (_turnTimerText != null) _turnTimerText.text = Mathf.CeilToInt(Mathf.Max(0f, seconds)).ToString();
    }

    private void Update()
    {
        if (_eventTimer > 0f)
        {
            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f && _eventText != null) _eventText.text = "";
        }
    }

    public void ShowEvent(string text, float seconds = 0.8f)
    {
        if (_eventText == null) return;
        _eventText.text = text;
        _eventTimer = seconds;
    }
}

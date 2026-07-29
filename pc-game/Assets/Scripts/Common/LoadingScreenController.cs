using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// SceneFadeTransition이 게임 씬을 비동기로 읽는 동안 표시하는 persistent 로딩/캘리브레이션 UI.
public sealed class LoadingScreenController : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "Loading/LoadingScreenCanvas";

    private readonly string[] _backgroundNames =
    {
        "loading_01_volcanic_springs",
        "loading_02_crystal_cave",
        "loading_03_fossil_canyon",
        "loading_04_waterfall_overlook",
        "loading_05_moonlit_beach",
        "loading_06_snow_valley",
    };

    public float minimumDisplaySeconds = 1.5f;
    public bool IsReady { get; private set; }

    private GameObject _canvasObject;
    private GameObject _root;
    private Image _background;
    private RawImage _preview;
    private Text _previewLabel;
    private Text _message;
    private Text _tip;
    private Text[] _statusTexts;
    private Image[] _statusIcons;
    private RectTransform _ring;
    private Sprite _readyIcon;
    private Sprite _loadingIcon;
    private PlayerId _localPlayer;
    private float _elapsed;
    private readonly Queue<int> _backgroundBag = new Queue<int>();

    private void Awake()
    {
        CreateUi();
        _root.SetActive(false);
    }

    public void Show(string nextScene)
    {
        RebuildUi();
        _localPlayer = ResolveLocalPlayer();
        _elapsed = 0f;
        IsReady = false;

        Sprite[] backgrounds = _backgroundNames.Select(ArtAssets.LoadLoading)
            .Where(x => x != null).ToArray();
        if (backgrounds.Length > 0)
        {
            _background.sprite = backgrounds[NextBackgroundIndex(backgrounds.Length)];
            Debug.Log($"[LoadingScreen] 랜덤 배경 선택: {_background.sprite.name} " +
                      $"({backgrounds.Length}/{_backgroundNames.Length}장 로드됨)");
        }
        else
        {
            Debug.LogError("[LoadingScreen] Resources/Loading에서 로딩 배경 Sprite를 찾지 못했습니다.");
        }

        string[] tips = LoadTips();
        _tip.text = tips.Length > 0 ? tips[UnityEngine.Random.Range(0, tips.Length)] : "몸 전체가 카메라에 보이게 서 주세요.";
        _message.text = $"{PrettySceneName(nextScene)} 준비 중\n몸 전체가 보이도록 차렷 자세를 유지하세요";
        _previewLabel.text = $"{_localPlayer} CAMERA";
        _preview.texture = null;
        _root.SetActive(true);

        PoseInputHub.Instance?.BeginCalibration();
        RefreshStatus(PlayerId.P1, 0);
        RefreshStatus(PlayerId.P2, 1);
    }

    public void Hide()
    {
        IsReady = false;
        _root.SetActive(false);
    }

    private void Update()
    {
        if (!_root.activeSelf) return;
        _elapsed += Time.unscaledDeltaTime;
        _ring.Rotate(0f, 0f, -150f * Time.unscaledDeltaTime);

        CameraPreviewReceiver previewReceiver = CameraPreviewReceiver.Instance;
        Texture texture = previewReceiver != null ? previewReceiver.GetTexture(_localPlayer) : null;
        if (texture != null) _preview.texture = texture;

        RefreshStatus(PlayerId.P1, 0);
        RefreshStatus(PlayerId.P2, 1);

        PoseInputHub hub = PoseInputHub.Instance;
        bool calibrated = hub != null && hub.AreBothCalibrated;
        IsReady = calibrated && _elapsed >= minimumDisplaySeconds;

#if UNITY_EDITOR
        // 한 명만 켜고 씬 흐름을 확인할 때 로딩 화면에 영구히 갇히지 않게 하는 에디터 전용 우회.
        if (Keyboard.current?.enterKey.wasPressedThisFrame == true) IsReady = true;
#endif
        if (IsReady)
            _message.text = "두 플레이어 준비 완료!";
    }

    private void RefreshStatus(PlayerId player, int index)
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState state = hub != null ? hub.Get(player) : null;
        bool poseConnected = state != null && state.IsTracked;
        bool previewConnected = CameraPreviewReceiver.Instance != null &&
                                CameraPreviewReceiver.Instance.IsConnected(player);
        bool ready = state != null && state.IsCalibrated;
        int progress = state != null ? Mathf.RoundToInt(state.CalibrationProgress * 100f) : 0;

        _statusIcons[index].sprite = ready ? _readyIcon : _loadingIcon;
        _statusTexts[index].text = ready
            ? $"{player}  준비 완료"
            : !poseConnected
                ? $"{player}  연결 대기"
                : $"{player}  캘리브레이션 {progress}%";

        if (player == _localPlayer)
            _previewLabel.text = previewConnected
                ? $"{player} CAMERA · 연결됨"
                : $"{player} CAMERA · 프리뷰 대기";
    }

    private void CreateUi()
    {
        GameObject prefab = Resources.Load<GameObject>(CanvasPrefabResourcePath);
        if (prefab != null)
        {
            _canvasObject = Instantiate(prefab, transform, false);
            _canvasObject.name = "LoadingScreenCanvas";
            // 프리팹 편집 중 계층이 달라져 BindUi가 실패하더라도 시작 화면을 덮지 않게
            // LoadingRoot부터 먼저 숨긴다.
            Transform loadingRoot = FindDescendant(_canvasObject.transform, "LoadingRoot");
            if (loadingRoot != null) loadingRoot.gameObject.SetActive(false);
            BindUi();
            return;
        }

        Debug.LogWarning("[LoadingScreen] LoadingScreenCanvas.prefab을 찾지 못해 코드 기본 UI를 사용합니다.");
        BuildGeneratedUi();
    }

    private void BindUi()
    {
        Transform root = FindDescendant(_canvasObject.transform, "LoadingRoot");
        if (root == null)
            throw new InvalidOperationException("LoadingScreenCanvas.prefab에 LoadingRoot가 없습니다.");

        _root = root.gameObject;
        _background = FindDescendant(root, "RandomBackground")?.GetComponent<Image>();
        _preview = FindDescendant(root, "CameraPreview")?.GetComponent<RawImage>();
        _previewLabel = FindDescendant(root, "CameraLabel")?.GetComponent<Text>();
        _message = FindDescendant(root, "MessageText")?.GetComponent<Text>();
        _tip = FindDescendant(root, "TipText")?.GetComponent<Text>();
        _ring = FindDescendant(root, "LoadingRing") as RectTransform;

        Transform p1Status = FindDescendant(root, "P1Status");
        Transform p2Status = FindDescendant(root, "P2Status");

        _statusTexts = new[]
        {
            FindDescendant(p1Status, "StatusText")?.GetComponent<Text>(),
            FindDescendant(p2Status, "StatusText")?.GetComponent<Text>(),
        };
        _statusIcons = new[]
        {
            FindDescendant(p1Status, "Icon")?.GetComponent<Image>(),
            FindDescendant(p2Status, "Icon")?.GetComponent<Image>(),
        };
        _readyIcon = ArtAssets.LoadLoading("status_icon_ready");
        _loadingIcon = ArtAssets.LoadLoading("status_icon_loading");

        if (_background == null || _preview == null || _previewLabel == null ||
            _message == null || _tip == null || _ring == null ||
            _statusTexts.Any(x => x == null) || _statusIcons.Any(x => x == null))
        {
            throw new InvalidOperationException(
                "LoadingScreenCanvas.prefab의 필수 오브젝트 이름이 변경되었습니다. " +
                "LoadingRoot와 그 하위 오브젝트 이름을 유지하세요.");
        }
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        if (parent == null) return null;
        if (parent.name == objectName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDescendant(parent.GetChild(i), objectName);
            if (found != null) return found;
        }
        return null;
    }

    private void BuildGeneratedUi()
    {
        var canvasGo = new GameObject("LoadingScreenCanvas");
        _canvasObject = canvasGo;
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32750;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _root = new GameObject("LoadingRoot");
        _root.transform.SetParent(canvasGo.transform, false);
        RectTransform rootRt = _root.AddComponent<RectTransform>();
        Stretch(rootRt);

        _background = AddImage(rootRt, "RandomBackground", null, Vector2.zero, Vector2.zero);
        Stretch(_background.rectTransform);
        _background.preserveAspect = false;

        Image shade = AddImage(rootRt, "Shade", null, Vector2.zero, Vector2.zero);
        Stretch(shade.rectTransform);
        shade.color = new Color(0.02f, 0.03f, 0.07f, 0.28f);

        RectTransform logo = AddPanel(rootRt, "Logo", ArtAssets.LoadLoading("logo_base"),
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(720f, 230f));
        Text logoText = AddText(logo, "LogoText", "다음 게임 준비", 56);
        Stretch(logoText.rectTransform);

        RectTransform message = AddPanel(rootRt, "MessagePanel", ArtAssets.LoadLoading("message_panel"),
            new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(1050f, 210f));
        _message = AddText(message, "MessageText", "", 40);
        StretchWithMargin(_message.rectTransform, 60f, 30f);

        _ring = AddImage(rootRt, "LoadingRing", ArtAssets.LoadLoading("loading_ring"),
            new Vector2(0.5f, 0.41f), Vector2.zero).rectTransform;
        _ring.sizeDelta = new Vector2(110f, 110f);

        _statusTexts = new Text[2];
        _statusIcons = new Image[2];
        _readyIcon = ArtAssets.LoadLoading("status_icon_ready");
        _loadingIcon = ArtAssets.LoadLoading("status_icon_loading");
        BuildStatus(rootRt, PlayerId.P1, 0, new Vector2(230f, -110f));
        BuildStatus(rootRt, PlayerId.P2, 1, new Vector2(230f, -245f));

        _preview = AddRawImage(rootRt, "CameraPreview", new Vector2(1f, 1f),
            new Vector2(-45f, -25f), new Vector2(540f, 304f));
        _previewLabel = AddText(rootRt, "CameraLabel", "", 24);
        SetRect(_previewLabel.rectTransform, new Vector2(1f, 1f),
            new Vector2(-45f, -279f), new Vector2(540f, 50f));

        // TIP은 장식 팻말 없이 배경 위에 텍스트만 표시한다.
        _tip = AddText(rootRt, "TipText", "", 30);
        SetRect(_tip.rectTransform, new Vector2(0.5f, 0f),
            new Vector2(0f, 20f), new Vector2(1300f, 80f));
    }

    private void RebuildUi()
    {
        GameObject existing = _canvasObject;
        if (existing == null)
        {
            Transform child = transform.Find("LoadingScreenCanvas");
            if (child != null) existing = child.gameObject;
        }
        if (existing != null)
        {
            existing.SetActive(false);
            Destroy(existing);
        }
        CreateUi();
    }

    // Editor 빌더가 최초 프리팹을 만들 때 사용하는 코드 기본 레이아웃.
    public GameObject CreatePrefabTemplate()
    {
        if (_canvasObject == null) CreateUi();
        if (_root != null) _root.SetActive(true);
        return _canvasObject;
    }

    private void BuildStatus(RectTransform root, PlayerId player, int index, Vector2 offset)
    {
        RectTransform panel = AddPanel(root, $"{player}Status", ArtAssets.LoadLoading("status_badge_base"),
            new Vector2(0f, 1f), offset, new Vector2(410f, 130f));
        _statusIcons[index] = AddImage(panel, "Icon", _loadingIcon, new Vector2(0.15f, 0.5f), Vector2.zero);
        _statusIcons[index].rectTransform.sizeDelta = new Vector2(70f, 70f);
        _statusTexts[index] = AddText(panel, "StatusText", $"{player} 연결 대기", 27);
        SetRect(_statusTexts[index].rectTransform, new Vector2(0.62f, 0.5f), Vector2.zero, new Vector2(285f, 80f));
    }

    private static string[] LoadTips()
    {
        TextAsset asset = Resources.Load<TextAsset>("Loading/tips");
        if (asset == null) return Array.Empty<string>();
        var result = new List<string>();
        foreach (string raw in asset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            int dot = line.IndexOf(". ", StringComparison.Ordinal);
            if (dot >= 0 && dot < 4) line = line.Substring(dot + 2).Trim();
            if (!string.IsNullOrEmpty(line)) result.Add(line);
        }
        return result.ToArray();
    }

    // 여섯 게임 동안 같은 배경만 우연히 반복되지 않도록 한 묶음을 섞어 하나씩 꺼낸다.
    // 전부 사용한 뒤에는 다시 섞으므로 장기적으로도 선택 순서는 계속 무작위다.
    private int NextBackgroundIndex(int count)
    {
        if (_backgroundBag.Count == 0)
        {
            var indices = Enumerable.Range(0, count).ToList();
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }
            foreach (int index in indices)
                _backgroundBag.Enqueue(index);
        }
        return _backgroundBag.Dequeue();
    }

    private static PlayerId ResolveLocalPlayer()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--player-id" && args[i + 1].Equals("p2", StringComparison.OrdinalIgnoreCase))
                return PlayerId.P2;
        return PlayerPrefs.GetString("local_player_id", "p1") == "p2" ? PlayerId.P2 : PlayerId.P1;
    }

    private static string PrettySceneName(string scene)
    {
        switch (scene)
        {
            case "StoneThrow": return "돌 던지기";
            case "FruitJump": return "점프해서 과일 따기";
            case "CoconutCrack": return "머리로 코코넛 깨기";
            case "StoneOrBanana": return "돌 or 바나나";
            case "StaringContest": return "눈빛 싸움";
            case "ScreamDuel": return "소리 지르기";
            default: return scene;
        }
    }

    private static RectTransform AddPanel(Transform parent, string name, Sprite sprite,
        Vector2 anchor, Vector2 offset, Vector2 size)
    {
        Image image = AddImage(parent, name, sprite, anchor, offset);
        image.rectTransform.sizeDelta = size;
        image.preserveAspect = true;
        return image.rectTransform;
    }

    private static Image AddImage(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 offset)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        SetRect(image.rectTransform, anchor, offset, new Vector2(100f, 100f));
        return image;
    }

    private static RawImage AddRawImage(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<RawImage>();
        image.color = new Color(0.04f, 0.04f, 0.04f, 1f);
        image.raycastTarget = false;
        image.uvRect = new Rect(0f, 0f, 1f, 1f);
        SetRect(image.rectTransform, anchor, offset, size);
        return image;
    }

    private static Text AddText(Transform parent, string name, string value, int fontSize)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = value;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StretchWithMargin(RectTransform rt, float horizontal, float vertical)
    {
        Stretch(rt);
        rt.offsetMin = new Vector2(horizontal, vertical);
        rt.offsetMax = new Vector2(-horizontal, -vertical);
    }

}

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
    // 카메라가 아직 연결되지 않았을 때 CameraPreview RawImage에 거는 어두운 틴트 - 프레임이
    // 도착하면 Color.white로 되돌려야 한다(Update() 참고). 그러지 않으면 실제 카메라 이미지도
    // 이 틴트만큼 곱해져 사실상 안 보인다.
    private static readonly Color PreviewPlaceholderColor = new Color(0.04f, 0.04f, 0.04f, 1f);

    private readonly string[] _backgroundNames =
    {
        "loading_01_volcanic_springs",
        "loading_02_crystal_cave",
        "loading_03_fossil_canyon",
        "loading_04_waterfall_overlook",
        "loading_05_moonlit_beach",
        "loading_06_snow_valley",
    };

    // 이전 라운드 위치에서 카메라 앞으로 걸어올 시간을 보장하기 위한 최소 대기 - 캘리브레이션이
    // 그보다 먼저 끝나도 최소한 이 시간만큼은 로딩 화면을 붙잡아둔다.
    public float minimumDisplaySeconds = 5f;
    public bool IsReady { get; private set; }
    private string _pendingScene;

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

    // --- 호스트-클라이언트 중계 (docs/멀티플레이_분산_아키텍처_설계.md) ---
    // vision-server는 포즈/프리뷰를 호스트로만 보내므로, 클라이언트의 PoseInputHub는 항상
    // 비어 있다. 그대로 두면 AreBothCalibrated가 영원히 false여서 IsReady가 서지 않고
    // SceneFadeTransition이 로딩 화면에서 무한 대기한다(실제로 클라이언트가 미니게임에
    // 진입하지 못했던 원인). 그래서 호스트가 캘리브레이션 상태를 이벤트로 중계하고, 카메라
    // 프리뷰는 CameraPreviewReceiver가 UDP로 직접 중계하며(ForwardRelayFrame 참고), 클라이언트는
    // 자기 판단 대신 그 값을 그대로 표시/사용한다.
    private const float StatusSendInterval = 0.1f; // 상태는 10Hz면 충분(프리뷰는 도착할 때만 중계)
    private float _lastStatusSentAt;
    private StatusPayload _relayedStatus;
    private bool _subscribed;
    // 호스트가 로컬 판정으로 "준비됨"을 broadcast하는 값 - 아래 IsReady(실제로 게임을
    // 시작하는 데 쓰는 값)와는 다르다. 이 값이 true로 바뀐 프레임에 스로틀 때문에 전송이
    // 밀리면 클라이언트가 영원히 못 받고 멈추므로, 바뀌는 순간에는 스로틀을 무시하고 즉시
    // 보낸다.
    private bool _hostLocallyReady;
    private bool _lastSentReady;

    // --- 양방향 시작 배리어 ---
    // 예전에는 호스트→클라이언트 단방향 중계만 있었다: 호스트는 자기 판정이 끝나는 즉시
    // IsReady=true가 되어 로딩 화면을 닫고 게임을 시작했고, 클라이언트는 그 판정을 네트워크로
    // 받은 뒤에야(최소 한 번의 왕복 지연) 따라왔다 - 호스트가 매 라운드 항상 클라이언트보다
    // 먼저 시작하는 구조적 불공평이 있었다(실측 확인된 원인). 이제 클라이언트는 스스로
    // 준비됐다고 판단한 첫 순간 호스트에게 확인(ack)을 보내고, 호스트는 자기 판정이 끝나도
    // 이 ack을 받기 전까지는 IsReady를 true로 올리지 않는다.
    private bool _clientAcked; // 호스트: 이번 라운드에 클라이언트의 ack을 받았는지
    private bool _ackSentThisRound; // 클라이언트: 이번 라운드에 ack을 이미 보냈는지
    // 실측 디버깅용 - 각 상태 전환을 라운드당 한 번만 로그로 남긴다(Update()의 로그 참고).
    private bool _hostLocallyReadyLogged;
    private bool _finalReadyLogged;

    private void Awake()
    {
        CreateUi();
        _root.SetActive(false);
    }

    private void OnEnable() => EnsureSubscribed();

    private void EnsureSubscribed()
    {
        if (_subscribed) return;
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;
        net.Subscribe("loading_status", OnNetStatus);
        net.Subscribe("loading_preview", OnNetPreview);
        net.Subscribe("loading_client_ack", OnNetClientAck);
        _subscribed = true;
    }

    private void OnNetStatus(NetworkEvent evt) => _relayedStatus = NetworkSession.Read<StatusPayload>(evt);

    private void OnNetPreview(NetworkEvent evt)
    {
        PreviewPayload payload = NetworkSession.Read<PreviewPayload>(evt);
        if (string.IsNullOrEmpty(payload.jpegBase64)) return;
        try
        {
            CameraPreviewReceiver.Instance?.ApplyRelayedFrame(payload.player, Convert.FromBase64String(payload.jpegBase64));
        }
        catch (FormatException e)
        {
            Debug.LogWarning($"[LoadingScreen] 중계 프리뷰 디코딩 실패: {e.Message}");
        }
    }

    private void OnNetClientAck(NetworkEvent evt)
    {
        _clientAcked = true;
        Debug.Log($"[LoadingScreen] 호스트: 클라이언트 ack 수신 (t={Time.unscaledTime:F2})");
    }

    // 호스트: 자기가 받은 상태/프리뷰를 클라이언트로 내보낸다.
    // 프리뷰는 게임 이벤트(하트비트/점수)와 같은 TCP 채널을 쓴다 - 한때 별도 UDP로
    // 직접 보내봤는데, 15~20KB짜리 JPEG가 IP 단편화되면서 와이파이에서 단편 하나만
    // 유실돼도 프레임 전체가 통째로 버려져 클라이언트 화면이 거의 새까맣게 나오는 회귀가
    // 있었다(실측 확인) - TCP는 재전송으로 이 문제가 없으므로 되돌렸다.
    private void BroadcastAsHost(NetworkSession net)
    {
        if (Time.unscaledTime - _lastStatusSentAt >= StatusSendInterval || _hostLocallyReady != _lastSentReady)
        {
            _lastStatusSentAt = Time.unscaledTime;
            _lastSentReady = _hostLocallyReady;
            PoseInputHub hub = PoseInputHub.Instance;
            PlayerPoseState p1 = hub?.Get(PlayerId.P1);
            PlayerPoseState p2 = hub?.Get(PlayerId.P2);
            CameraPreviewReceiver preview = CameraPreviewReceiver.Instance;
            net.Send("loading_status", new StatusPayload
            {
                p1Tracked = p1 != null && p1.IsTracked,
                p2Tracked = p2 != null && p2.IsTracked,
                p1Calibrated = PlayerFullyReady(p1),
                p2Calibrated = PlayerFullyReady(p2),
                p1Progress = p1 != null ? p1.CalibrationProgress : 0f,
                p2Progress = p2 != null ? p2.CalibrationProgress : 0f,
                p1Preview = preview != null && preview.IsConnected(PlayerId.P1),
                p2Preview = preview != null && preview.IsConnected(PlayerId.P2),
                ready = _hostLocallyReady,
            });
        }

        // 프리뷰는 vision-server가 보낸 새 프레임이 있을 때만(기본 5fps) 중계한다.
        if (CameraPreviewReceiver.Instance != null &&
            CameraPreviewReceiver.Instance.TryDequeueRelayFrame(out string player, out byte[] jpeg))
        {
            net.Send("loading_preview", new PreviewPayload
            {
                player = player,
                jpegBase64 = Convert.ToBase64String(jpeg),
            });
        }
    }

    [Serializable]
    private class StatusPayload
    {
        public bool p1Tracked, p2Tracked;
        public bool p1Calibrated, p2Calibrated;
        public float p1Progress, p2Progress;
        public bool p1Preview, p2Preview;
        public bool ready;
    }

    [Serializable]
    private class PreviewPayload
    {
        public string player;
        public string jpegBase64;
    }

    [Serializable]
    private class ClientAckPayload { }

    public void Show(string nextScene)
    {
        RebuildUi();
        _pendingScene = nextScene;
        _localPlayer = ResolveLocalPlayer();
        _elapsed = 0f;
        IsReady = false;
        // 지난 라운드의 ready=true가 남아 있으면 클라이언트가 이번 캘리브레이션을 기다리지 않고
        // 곧바로 통과해버린다 - 라운드마다 중계 상태를 비운다.
        _relayedStatus = null;
        _lastSentReady = false;
        _hostLocallyReady = false;
        _clientAcked = false;
        _ackSentThisRound = false;
        _hostLocallyReadyLogged = false;
        _finalReadyLogged = false;

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
        _preview.color = PreviewPlaceholderColor; // 새 라운드 시작 - 카메라 붙기 전 빈 상자로 되돌림
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
        if (texture != null)
        {
            _preview.texture = texture;
            // RawImage는 텍스처×틴트로 렌더링된다. 카메라가 연결되기 전엔 "빈 상자"처럼
            // 보이도록 아래 Show()에서 어두운 틴트(0.04)를 걸어두는데, 이걸 흰색으로
            // 되돌리지 않으면 실제 카메라 프레임이 들어와도 밝기 4%로 곱해져 사실상 안
            // 보인다(실측 확인된 버그) - 프레임이 처음 도착하는 순간 딱 한 번 되돌린다.
            if (_preview.color != Color.white) _preview.color = Color.white;
        }

        RefreshStatus(PlayerId.P1, 0);
        RefreshStatus(PlayerId.P2, 1);

        EnsureSubscribed(); // Hub에서 역할을 고른 뒤에야 NetworkSession이 생기는 경우가 있다
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            // 클라이언트는 캘리브레이션을 판정하지 않는다 - 호스트가 보낸 ready를 그대로 쓴다.
            // 아직 상태를 못 받았으면 대기(false)로 둔다.
            IsReady = _relayedStatus != null && _relayedStatus.ready && _elapsed >= minimumDisplaySeconds;

            // 내가 준비됐다고 판단한 첫 순간 호스트에게 확인을 보낸다(양방향 시작 배리어) -
            // 호스트는 이 ack을 받아야만 실제로 게임을 시작한다. 라운드당 한 번만 보낸다.
            if (IsReady && !_ackSentThisRound)
            {
                _ackSentThisRound = true;
                Debug.Log($"[LoadingScreen] 클라이언트: 내 Show() 이후 {_elapsed:F2}초 만에 준비 완료 → ack 전송");
                net.Send("loading_client_ack", new ClientAckPayload());
            }
        }
        else
        {
            PoseInputHub hub = PoseInputHub.Instance;
            bool ready = hub != null
                && PlayerFullyReady(hub.Get(PlayerId.P1))
                && PlayerFullyReady(hub.Get(PlayerId.P2));
            _hostLocallyReady = ready && _elapsed >= minimumDisplaySeconds;
            if (_hostLocallyReady && !_hostLocallyReadyLogged)
            {
                _hostLocallyReadyLogged = true;
                Debug.Log($"[LoadingScreen] 호스트: 내 Show() 이후 {_elapsed:F2}초 만에 로컬 판정 완료 " +
                          $"(clientAcked={_clientAcked})");
            }

            // 오프라인/솔로(상대가 없는 경우)는 확인할 클라이언트가 없으므로 곧장 통과.
            // 네트워크 호스트일 때만 클라이언트의 ack을 기다린다.
            bool waitingForClientAck = net != null && net.IsHost && !_clientAcked;
            IsReady = _hostLocallyReady && !waitingForClientAck;
        }

#if UNITY_EDITOR
        // 한 명만 켜고 씬 흐름을 확인할 때 로딩 화면에 영구히 갇히지 않게 하는 에디터 전용 우회.
        if (Keyboard.current?.enterKey.wasPressedThisFrame == true) IsReady = true;
#endif
        // 실측 디버깅용 - 호스트/클라이언트 콘솔의 이 타임스탬프를 나란히 비교하면 동시
        // 시작이 실제로 어디서 어긋나는지(호스트 판정이 느린지, 네트워크 전달이 느린지,
        // ack이 늦는지) 바로 알 수 있다.
        if (IsReady && !_finalReadyLogged)
        {
            _finalReadyLogged = true;
            string role = net != null ? net.Role.ToString() : "Offline";
            Debug.Log($"[LoadingScreen] IsReady=true, 로딩 화면 닫힘 (내 Show() 이후 {_elapsed:F2}초, role={role})");
        }
        if (IsReady)
            _message.text = "두 플레이어 준비 완료!";

        // IsReady를 확정한 뒤에 보내야 클라이언트가 한 프레임 늦지 않는다.
        if (net != null && net.IsHost) BroadcastAsHost(net);
    }

    private void RefreshStatus(PlayerId player, int index)
    {
        NetworkSession net = NetworkSession.Instance;
        bool poseConnected, previewConnected, ready;
        int progress;

        if (net != null && net.IsClient)
        {
            // 클라이언트의 PoseInputHub/프리뷰 수신부는 비어 있으므로 중계받은 값만 쓴다.
            StatusPayload s = _relayedStatus;
            bool isP1 = player == PlayerId.P1;
            poseConnected = s != null && (isP1 ? s.p1Tracked : s.p2Tracked);
            previewConnected = s != null && (isP1 ? s.p1Preview : s.p2Preview);
            ready = s != null && (isP1 ? s.p1Calibrated : s.p2Calibrated);
            progress = Mathf.RoundToInt((s != null ? (isP1 ? s.p1Progress : s.p2Progress) : 0f) * 100f);
        }
        else
        {
            PoseInputHub hub = PoseInputHub.Instance;
            PlayerPoseState state = hub != null ? hub.Get(player) : null;
            poseConnected = state != null && state.IsTracked;
            previewConnected = CameraPreviewReceiver.Instance != null &&
                               CameraPreviewReceiver.Instance.IsConnected(player);
            ready = PlayerFullyReady(state);
            progress = state != null ? Mathf.RoundToInt(state.CalibrationProgress * 100f) : 0;
        }

        // 캘리브레이션(자세 안정)은 끝났는데 이번 게임에 필요한 부위(손/얼굴 등)가 아직 화면
        // 밖이라 최종 준비완료는 못 뜬 상태를 구분해서 보여준다 - "그냥 기다리면 되는지" vs
        // "자세를 고쳐야 하는지"를 사람이 알 수 있게.
        bool calibratedButPartsMissing = !ready && progress >= 100 && poseConnected;

        _statusIcons[index].sprite = ready ? _readyIcon : _loadingIcon;
        _statusTexts[index].text = ready
            ? $"{player}  준비 완료"
            : !poseConnected
                ? $"{player}  연결 대기"
                : calibratedButPartsMissing
                    ? $"{player}  자세를 카메라에 맞춰주세요"
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

    // 캘리브레이션(자세 안정)이 끝났어도, 다음 게임이 실제로 쓰는 부위(손/얼굴)가 카메라
    // 프레임 밖이면 아직 준비된 게 아니다 - 예를 들어 몸통은 잡혔는데 손이 화면 밖으로 나가
    // 있으면 손 들기 판정 자체가 안 되므로 게임을 시작하면 안 된다.
    private bool PlayerFullyReady(PlayerPoseState state)
        => state != null && state.IsCalibrated && RequiredPartsReady(state, _pendingScene);

    private static bool RequiredPartsReady(PlayerPoseState state, string scene)
    {
        switch (scene)
        {
            case "ScreamDuel":
                // 마이크 음량만 쓰고 포즈는 전혀 안 본다 - 부위 확인 자체가 필요 없음.
                return true;
            case "StaringContest":
                return state.IsFaceVisible();
            default: // StoneThrow, FruitJump, CoconutCrack, StoneOrBanana, FeatherFlight
                return state.AreHandsVisible();
        }
    }

    // 호스트=P1, 클라이언트=P2 (게임 전체의 확립된 관례 - MatchController/미니게임 전부
    // 이 매핑을 전제한다). 예전에는 Unity 프로세스 자체의 커맨드라인 인자(--player-id)나
    // PlayerPrefs(local_player_id)를 봤는데, 그 값을 실제로 채워주는 코드가 어디에도 없어서
    // 이 함수가 항상 P1을 반환했다 - 클라이언트(P2) 로딩 화면에도 "P1 CAMERA"가 뜨고 P1의
    // 프리뷰 텍스처가 표시되던 원인이었다(실측 확인됨). 오프라인/솔로 모드는 그대로 P1.
    private static PlayerId ResolveLocalPlayer()
    {
        NetworkSession net = NetworkSession.Instance;
        return net != null && net.IsClient ? PlayerId.P2 : PlayerId.P1;
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
            case "FeatherFlight": return "깃털 날기";
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
        image.color = PreviewPlaceholderColor;
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

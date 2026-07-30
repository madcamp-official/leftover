// 상대방과의 TCP 이벤트 채널이 예기치 않게 끊기면(하트비트 타임아웃, 소켓 끊김, 상대
// 프로세스 종료 등) 전체 화면에 "연결 끊김 - 재연결 대기 중" 오버레이를 띄우고 자동으로
// 재연결을 시도한다. docs/멀티플레이_분산_아키텍처_설계.md 8장에서 계획만 하고 실제
// 코드로는 안 옮겨져 있던 부분 - 이게 없으면 클라이언트는 로딩 화면에서 조용히 멈춘 것처럼
// 보인다(LoadingScreenController가 "클라이언트인지" 판단하는 net.IsClient가 Role==Offline
// 순간 false가 돼서, 카메라 데이터를 받은 적 없는 로컬 PoseInputHub 기준 판정으로 새버려서
// 캘리브레이션이 영원히 100%를 못 채움 - 실측으로 확인된 원인).
//
// NetworkSession.LastRole/LastHostAddress는 Shutdown()으로도 안 지워지므로(사용자가 직접
// 나갈 때만 Disconnect()가 지움), "지금 연결이 끊겨 있는데 LastRole이 Offline이 아니다"가
// 곧 "예기치 않은 끊김"의 신호다.
using UnityEngine;
using UnityEngine.UI;

public sealed class NetworkReconnectOverlay : MonoBehaviour
{
    private const float ReconnectIntervalSeconds = 2f;
    private const float FadeSpeed = 4f; // 초당 알파 변화량(unscaled)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<NetworkReconnectOverlay>() != null) return;
        var go = new GameObject(nameof(NetworkReconnectOverlay));
        go.AddComponent<NetworkReconnectOverlay>();
    }

    private CanvasGroup _group;
    private Text _messageText;
    private bool _overlayActive;
    private float _reconnectTimer;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        BuildUi();
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("NetworkReconnectCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        // SceneFadeTransition(32760)/LoadingScreenController보다 위에 그려야 그 화면들
        // 위로 덮여서 "멈춘 것처럼" 보이는 걸 확실히 가린다.
        canvas.sortingOrder = 32765;
        canvasGo.AddComponent<GraphicRaycaster>();

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        RectTransform root = canvasGo.GetComponent<RectTransform>();

        var shadeGo = new GameObject("Shade");
        shadeGo.transform.SetParent(root, false);
        var shadeRt = shadeGo.AddComponent<RectTransform>();
        shadeRt.anchorMin = Vector2.zero;
        shadeRt.anchorMax = Vector2.one;
        shadeRt.offsetMin = Vector2.zero;
        shadeRt.offsetMax = Vector2.zero;
        var shadeImage = shadeGo.AddComponent<Image>();
        shadeImage.color = new Color(0f, 0f, 0f, 0.88f);
        shadeImage.raycastTarget = false;

        _messageText = HudWidgets.CreateText(root, "Message", new Vector2(0.5f, 0.5f), 1400f, 52);
        _messageText.text = "";
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        bool connected = net != null && net.ConnectionState == NetworkConnectionState.Connected;
        // HadSuccessfulConnection이 있어야만 "재연결" 대상이다 - 최초 접속 시도가 아직 한
        // 번도 성공 못 한 상태(오타 IP 등)는 MultiplayerConnectScreen 자체 UI가 처리하므로
        // 이 전체 화면 오버레이로 덮지 않는다.
        bool unexpectedlyDisconnected = net != null && net.LastRole != NetworkRole.Offline
            && net.HadSuccessfulConnection && !connected;

        if (unexpectedlyDisconnected && !_overlayActive)
        {
            _overlayActive = true;
            _reconnectTimer = 0f; // 뜨자마자 첫 시도
            // 진행 중이던 라운드가 있었다면(Time.deltaTime 기반 미니게임 시뮬레이션 전부)
            // 여기서 한 번에 멈춘다 - 게임마다 따로 손댈 필요 없음. UI는 전부
            // unscaledDeltaTime을 쓰므로 이 오버레이/로딩 화면은 계속 움직인다.
            Time.timeScale = 0f;
        }
        else if (!unexpectedlyDisconnected && _overlayActive)
        {
            _overlayActive = false;
            Time.timeScale = 1f;
        }

        _group.alpha = Mathf.MoveTowards(_group.alpha, _overlayActive ? 1f : 0f, Time.unscaledDeltaTime * FadeSpeed);
        _group.blocksRaycasts = _overlayActive;

        if (!_overlayActive) return;

        _messageText.text = net.LastRole == NetworkRole.Host
            ? "상대방과 연결이 끊겼습니다\n재연결을 기다리는 중..."
            : "호스트와 연결이 끊겼습니다\n재연결 시도 중...";

        _reconnectTimer -= Time.unscaledDeltaTime;
        if (_reconnectTimer <= 0f)
        {
            _reconnectTimer = ReconnectIntervalSeconds;
            net.TryReconnect();
        }
    }
}

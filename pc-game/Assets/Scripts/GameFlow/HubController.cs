// 시작 화면 + 최종 결과 화면을 겸하는 Hub 씬 컨트롤러. 시작 화면(배경/로고/버튼 4개)은
// Assets/Prefabs/StartScreenCanvas.prefab을 Hub 씬에 직접 배치해두고 여기서는 그 참조만
// 받아서 클릭 이벤트만 연결한다 - 위치/크기/스프라이트는 에디터에서 그 프리팹을 열어 직접
// 조정하면 된다(디자인 담당이 손으로 만지는 부분, 코드는 손대지 않아도 됨).
// MatchController가 "시작 전" 상태(CurrentRoundIndex == -1)면 이 시작 화면을, 5판이 다 끝난
// 상태(IsMatchComplete)면 결과를 보여준다(결과 화면은 전용 아트가 없어 텍스트로만).
using UnityEngine;
using UnityEngine.UI;

public class HubController : MonoBehaviour
{
    [Header("씬에 배치된 시작화면 오브젝트 (Assets/Prefabs/StartScreenCanvas.prefab 인스턴스)")]
    [SerializeField] private GameObject startScreenCanvas;
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button howToPlayButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    [Header("씬에 배치된 결과 화면")]
    [SerializeField] private GameObject resultScreenCanvas;
    [SerializeField] private Text resultText;
    [SerializeField] private Button restartButton;

    private string _toastText = "";
    private float _toastTimer;
    private string _joinAddressInput = "127.0.0.1";

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();
        ArtAssets.PreloadLoading();
        ArtAssets.PreloadScreamDuel();

        gameStartButton?.onClick.AddListener(OnGameStartClicked);
        howToPlayButton?.onClick.AddListener(OnHowToPlayClicked);
        settingsButton?.onClick.AddListener(OnSettingsClicked);
        exitButton?.onClick.AddListener(OnExitClicked);
        restartButton?.onClick.AddListener(OnRestartClicked);

        MatchController match = MatchController.Instance;
        bool showStartScreen = match != null && !match.IsMatchComplete && match.CurrentRoundIndex < 0;
        startScreenCanvas?.SetActive(showStartScreen);
        bool showResult = match != null && match.IsMatchComplete;
        resultScreenCanvas?.SetActive(showResult);
        if (showResult && resultText != null)
        {
            PlayerId? winner = match.OverallWinner();
            resultText.text = winner == null
                ? $"무승부!  {match.P1Wins} : {match.P2Wins}"
                : $"{winner} 최종 승리!  {match.P1Wins} : {match.P2Wins}";
        }
    }

    private void Update()
    {
        if (_toastTimer > 0f)
        {
            _toastTimer -= Time.deltaTime;
            if (_toastTimer <= 0f) _toastText = "";
        }

        // 클라이언트는 자기 판단으로 매치를 시작하지 못한다 - 호스트의 match_start
        // 이벤트로만 시작한다(MatchController.StartMatch 참고).
        NetworkSession net = NetworkSession.Instance;
        if (gameStartButton != null && net != null)
            gameStartButton.interactable = net.Role != NetworkRole.Client;
    }

    private void OnGameStartClicked() => MatchController.Instance?.StartMatch();

    private void OnRestartClicked() => MatchController.Instance?.StartMatch();

    private void OnHowToPlayClicked() => ShowToast("게임 방법 - 준비 중입니다!");

    private void OnSettingsClicked() => ShowToast("설정 - 준비 중입니다!");

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowToast(string text)
    {
        _toastText = text;
        _toastTimer = 2f;
    }

    private void OnGUI()
    {
        MatchController match = MatchController.Instance;

        if (!string.IsNullOrEmpty(_toastText))
        {
            var toastStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(0, Screen.height - 100, Screen.width, 50), _toastText, toastStyle);
        }

        bool onStartScreen = match != null && !match.IsMatchComplete && match.CurrentRoundIndex < 0;
        if (onStartScreen) DrawNetworkPanel();
    }

    // 호스트-클라이언트 역할 선택 패널 (docs/멀티플레이_분산_아키텍처_설계.md 5장).
    // 전용 UI 프리팹이 아직 없어 임시로 OnGUI로 그린다 - 위 _toastText와 같은 방식.
    private void DrawNetworkPanel()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;

        var boxStyle = new GUIStyle(GUI.skin.box);
        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };

        GUILayout.BeginArea(new Rect(16, 16, 320, 240), boxStyle);
        GUILayout.Label("네트워크 모드", titleStyle);
        GUILayout.Label($"{DescribeRole(net.Role)} / {DescribeState(net.ConnectionState)}", labelStyle);

        switch (net.Role)
        {
            case NetworkRole.Offline:
                if (GUILayout.Button("호스트로 시작")) { SoloBotController.SetEnabled(false); net.StartHost(); }
                GUILayout.Label("접속할 호스트 IP:", labelStyle);
                _joinAddressInput = GUILayout.TextField(_joinAddressInput);
                if (GUILayout.Button("접속")) { SoloBotController.SetEnabled(false); net.StartClient(_joinAddressInput); }
                GUILayout.Space(6);
                // 혼자하기 - 실제 카메라 없이 P2를 봇이 대신 조작한다(SoloBotController).
                // 상대방 없이도 매치를 끝까지 진행해볼 수 있는 연습용 모드.
                bool soloOn = SoloBotController.IsEnabled;
                string soloLabel = soloOn ? "혼자하기 ON (P2 = 봇)" : "혼자하기 OFF";
                if (GUILayout.Button(soloLabel)) SoloBotController.SetEnabled(!soloOn);
                break;
            case NetworkRole.Host:
                GUILayout.Label($"내 IP: {net.LocalAddressHint} (포트 {GameEventChannel.DefaultPort})", labelStyle);
                GUILayout.Label(net.ConnectionState == NetworkConnectionState.Connected
                    ? "클라이언트 연결됨"
                    : "클라이언트 접속 대기 중...", labelStyle);
                if (GUILayout.Button("연결 끊기")) net.Shutdown();
                break;
            case NetworkRole.Client:
                GUILayout.Label(net.ConnectionState == NetworkConnectionState.Connected
                    ? "호스트에 연결됨 - 호스트가 시작하기를 기다립니다"
                    : "호스트 접속 시도 중...", labelStyle);
                if (GUILayout.Button("연결 끊기")) net.Shutdown();
                break;
        }

        if (!string.IsNullOrEmpty(net.LastError))
            GUILayout.Label(net.LastError, labelStyle);

        GUILayout.EndArea();
    }

    private static string DescribeRole(NetworkRole role)
    {
        switch (role)
        {
            case NetworkRole.Host: return "호스트";
            case NetworkRole.Client: return "클라이언트";
            default: return "오프라인(로컬 단독)";
        }
    }

    private static string DescribeState(NetworkConnectionState state)
    {
        switch (state)
        {
            case NetworkConnectionState.Listening: return "대기 중";
            case NetworkConnectionState.Connecting: return "접속 중";
            case NetworkConnectionState.Connected: return "연결됨";
            default: return "연결 안 됨";
        }
    }
}

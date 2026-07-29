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

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
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

    }
}

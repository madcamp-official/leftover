// 시작 화면 + 최종 결과 화면을 겸하는 Hub 씬 컨트롤러. 시작 화면(배경/로고/버튼들)은
// Assets/Prefabs/StartScreenCanvas.prefab을 Hub 씬에 직접 배치해두고 여기서는 그 참조만
// 받아서 클릭 이벤트만 연결한다 - 위치/크기/스프라이트는 에디터에서 그 프리팹을 열어 직접
// 조정하면 된다(디자인 담당이 손으로 만지는 부분, 코드는 손대지 않아도 됨).
// MatchController가 "시작 전" 상태(CurrentRoundIndex == -1)면 이 시작 화면을, 매치가 다
// 끝난 상태(IsMatchComplete)면 결과를 보여준다.
//
// 멀티플레이 연결/게임 방법/설정 화면은 UI_화면_확장_에셋_계획.md에 따라 전용 프리팹 없이
// 코드로 조립한다(LoadingScreenController.BuildGeneratedUi()와 같은 패턴, UiBuilder 참고) -
// Hub 화면은 다른 미니게임 씬들과 달리 "디자인 담당이 손으로 만지는" StartScreenCanvas
// 하나만 프리팹으로 관리하고, 나머지 화면은 그 위에 얹는 오버레이라 코드 조립이 더 낫다.
using UnityEngine;
using UnityEngine.UI;

public class HubController : MonoBehaviour
{
    private enum HubScreen { Start, Multiplayer, HowTo, Settings, Result }

    [Header("씬에 배치된 시작화면 오브젝트 (Assets/Prefabs/StartScreenCanvas.prefab 인스턴스)")]
    [SerializeField] private GameObject startScreenCanvas;
    [SerializeField] private Button buttonPlay2P;
    [SerializeField] private Button buttonPlay1P;
    [SerializeField] private Button howToPlayButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;

    [Header("씬에 배치된 결과 화면")]
    [SerializeField] private GameObject resultScreenCanvas;
    [SerializeField] private Text resultText;
    [SerializeField] private Button restartButton;

    private MultiplayerConnectScreen _multiplayerScreen;
    private HowToPlayScreen _howToPlayScreen;
    private SettingsScreen _settingsScreen;

    private Image _resultBanner;
    private Text _resultBannerText;
    private Image _resultCrown;
    private Text _resultP1ScoreText;
    private Text _resultP2ScoreText;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();
        ArtAssets.PreloadLoading();
        ArtAssets.PreloadScreamDuel();

        BuildSubScreens();
        BuildResultScreenExtras();

        buttonPlay2P?.onClick.AddListener(OnPlay2PClicked);
        buttonPlay1P?.onClick.AddListener(OnPlay1PClicked);
        howToPlayButton?.onClick.AddListener(() => ShowScreen(HubScreen.HowTo));
        settingsButton?.onClick.AddListener(() => ShowScreen(HubScreen.Settings));
        exitButton?.onClick.AddListener(OnExitClicked);
        restartButton?.onClick.AddListener(OnRestartClicked);

        MatchController match = MatchController.Instance;
        bool matchComplete = match != null && match.IsMatchComplete;
        ShowScreen(matchComplete ? HubScreen.Result : HubScreen.Start);
    }

    private void BuildSubScreens()
    {
        var multiplayerGo = new GameObject("MultiplayerConnectScreen");
        multiplayerGo.transform.SetParent(transform, false);
        _multiplayerScreen = multiplayerGo.AddComponent<MultiplayerConnectScreen>();
        _multiplayerScreen.Init(() => ShowScreen(HubScreen.Start));

        var howToGo = new GameObject("HowToPlayScreen");
        howToGo.transform.SetParent(transform, false);
        _howToPlayScreen = howToGo.AddComponent<HowToPlayScreen>();
        _howToPlayScreen.Init(() => ShowScreen(HubScreen.Start));

        var settingsGo = new GameObject("SettingsScreen");
        settingsGo.transform.SetParent(transform, false);
        _settingsScreen = settingsGo.AddComponent<SettingsScreen>();
        _settingsScreen.Init(() => ShowScreen(HubScreen.Start));
        // 설정 화면을 한 번도 연 적 없어도 저장된 효과음 볼륨은 부팅 시점에 적용해야 한다.
        _settingsScreen.ApplySavedAudioSettings();
    }

    private void ShowScreen(HubScreen screen)
    {
        startScreenCanvas?.SetActive(screen == HubScreen.Start);
        resultScreenCanvas?.SetActive(screen == HubScreen.Result);
        if (screen == HubScreen.Multiplayer) _multiplayerScreen.Show(); else _multiplayerScreen.Hide();
        if (screen == HubScreen.HowTo) _howToPlayScreen.Show(); else _howToPlayScreen.Hide();
        if (screen == HubScreen.Settings) _settingsScreen.Show(); else _settingsScreen.Hide();
        if (screen == HubScreen.Result) RefreshResultScreen(MatchController.Instance);
    }

    // 2인 플레이 - 상대가 필요하므로 연결 화면(호스트/참가)으로 이동한다.
    private void OnPlay2PClicked()
    {
        SoloBotController.SetEnabled(false);
        ShowScreen(HubScreen.Multiplayer);
    }

    // 1인 플레이 - 네트워크 연결 없이 SoloBotController가 P2를 대신하고 바로 매치를 시작한다.
    private void OnPlay1PClicked()
    {
        SoloBotController.SetEnabled(true);
        MatchController.Instance?.StartMatch();
    }

    private void OnRestartClicked() => MatchController.Instance?.StartMatch();

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // 결과 화면에 붙는 승리/무승부 배너, 점수판, 왕관, "메인으로" 버튼을 코드로 조립한다.
    // 기존 resultText/restartButton은 씬에 이미 있는 참조를 그대로 재사용(숨기거나 재스킨).
    private void BuildResultScreenExtras()
    {
        if (resultScreenCanvas == null) return;
        Transform root = resultScreenCanvas.transform;

        if (resultText != null) resultText.gameObject.SetActive(false);

        _resultBanner = UiBuilder.AddImage(root, "ResultBanner", ArtAssets.LoadUi("result_panel_victory"),
            new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1600f, 380f));
        _resultBannerText = UiBuilder.AddText(root, "ResultBannerText", "", 64);
        UiBuilder.SetRect(_resultBannerText.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(-100f, 0f),
            new Vector2(900f, 200f));

        _resultCrown = UiBuilder.AddImage(root, "ResultCrown", ArtAssets.LoadUi("result_crown_icon"),
            new Vector2(0.5f, 0.38f), new Vector2(-270f, 260f), new Vector2(180f, 180f));

        UiBuilder.AddImage(root, "ScoreboardPanel", ArtAssets.LoadUi("result_scoreboard_panel"),
            new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(1100f, 420f));
        _resultP1ScoreText = UiBuilder.AddText(root, "P1ScoreText", "", 80);
        UiBuilder.SetRect(_resultP1ScoreText.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(-270f, 0f),
            new Vector2(400f, 300f));
        _resultP2ScoreText = UiBuilder.AddText(root, "P2ScoreText", "", 80);
        UiBuilder.SetRect(_resultP2ScoreText.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(270f, 0f),
            new Vector2(400f, 300f));

        if (restartButton != null)
        {
            var image = restartButton.GetComponent<Image>();
            if (image != null) image.sprite = ArtAssets.LoadUi("result_button_replay");
            RectTransform rt = restartButton.GetComponent<RectTransform>();
            if (rt != null) UiBuilder.SetRect(rt, new Vector2(0.5f, 0.08f), new Vector2(-260f, 0f),
                new Vector2(480f, 190f));
        }

        Button mainMenu = UiBuilder.AddButton(root, "MainMenuButton", ArtAssets.LoadUi("result_button_main_menu"),
            new Vector2(0.5f, 0.08f), new Vector2(260f, 0f), new Vector2(480f, 190f));
        mainMenu.onClick.AddListener(() => ShowScreen(HubScreen.Start));
    }

    private void RefreshResultScreen(MatchController match)
    {
        if (match == null || _resultBanner == null) return;

        PlayerId? winner = match.OverallWinner();
        bool isDraw = winner == null;

        _resultBanner.sprite = ArtAssets.LoadUi(isDraw ? "result_panel_draw" : "result_panel_victory");
        _resultBannerText.text = isDraw ? "무승부!" : $"{winner} 승리!";

        _resultCrown.gameObject.SetActive(!isDraw);
        if (!isDraw)
        {
            float side = winner == PlayerId.P1 ? -270f : 270f;
            _resultCrown.rectTransform.anchoredPosition = new Vector2(side, 260f);
        }

        _resultP1ScoreText.text = $"P1\n{match.P1Wins}";
        _resultP2ScoreText.text = $"P2\n{match.P2Wins}";
    }
}

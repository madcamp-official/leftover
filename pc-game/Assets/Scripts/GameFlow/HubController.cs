// 시작 화면 + 최종 결과 화면을 겸하는 Hub 씬 컨트롤러. 시작 화면(배경/로고/버튼들)은
// Assets/Prefabs/StartScreenCanvas.prefab을 Hub 씬에 직접 배치해두고 여기서는 그 참조만
// 받아서 클릭 이벤트만 연결한다 - 위치/크기/스프라이트는 에디터에서 그 프리팹을 열어 직접
// 조정하면 된다(디자인 담당이 손으로 만지는 부분, 코드는 손대지 않아도 됨).
// MatchController가 "시작 전" 상태(CurrentRoundIndex == -1)면 이 시작 화면을, 매치가 다
// 끝난 상태(IsMatchComplete)면 결과를 보여준다.
//
// 멀티플레이 연결/게임 방법/설정/최종 결과 화면은 UI_화면_확장_에셋_계획.md에 따라 각자
// Resources/UI/Prefabs/*.prefab을 갖는 독립 컴포넌트로 만들었다(MultiplayerConnectScreen,
// HowToPlayScreen, SettingsScreen, ResultScreenView) - 전용 이미지 UI 프리팹이 있으면 그걸
// 불러와 쓰고 없으면 코드로 기본 레이아웃을 생성하는 패턴(LoadingScreenController와 동일).
// Hub 씬에 예전에 있던 ResultScreenCanvas/ResultText/RestartButton은 더 이상 참조하지
// 않는다(비활성 상태로 씬에 남아있지만 무해함) - ResultScreenView가 완전히 대체했다.
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

    private MultiplayerConnectScreen _multiplayerScreen;
    private HowToPlayScreen _howToPlayScreen;
    private SettingsScreen _settingsScreen;
    private ResultScreenView _resultScreen;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();
        ArtAssets.PreloadLoading();
        ArtAssets.PreloadScreamDuel();

        BuildSubScreens();

        buttonPlay2P?.onClick.AddListener(OnPlay2PClicked);
        buttonPlay1P?.onClick.AddListener(OnPlay1PClicked);
        howToPlayButton?.onClick.AddListener(() => ShowScreen(HubScreen.HowTo));
        settingsButton?.onClick.AddListener(() => ShowScreen(HubScreen.Settings));
        exitButton?.onClick.AddListener(OnExitClicked);

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

        var resultGo = new GameObject("ResultScreenView");
        resultGo.transform.SetParent(transform, false);
        _resultScreen = resultGo.AddComponent<ResultScreenView>();
        _resultScreen.Init(onReplay: OnRestartClicked, onMainMenu: () => ShowScreen(HubScreen.Start));
    }

    private void ShowScreen(HubScreen screen)
    {
        startScreenCanvas?.SetActive(screen == HubScreen.Start);
        if (screen == HubScreen.Multiplayer) _multiplayerScreen.Show(); else _multiplayerScreen.Hide();
        if (screen == HubScreen.HowTo) _howToPlayScreen.Show(); else _howToPlayScreen.Hide();
        if (screen == HubScreen.Settings) _settingsScreen.Show(); else _settingsScreen.Hide();
        if (screen == HubScreen.Result) _resultScreen.Show(MatchController.Instance); else _resultScreen.Hide();
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
}

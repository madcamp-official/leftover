// "2인 플레이" 진입 시 뜨는 호스트/참가 연결 화면. 예전에 HubController.DrawNetworkPanel이
// OnGUI로 임시로 그리던 것을 UI_화면_확장_에셋_계획.md의 정식 이미지 UI로 교체했다.
// HubController가 Start()에서 이 컴포넌트를 만들고 Init()으로 "뒤로가기" 콜백을 넘겨준다.
//
// LoadingScreenController와 같은 패턴: Resources/UI/Prefabs/MultiplayerConnectCanvas
// 프리팹이 있으면 그걸 불러와 쓰고(디자인 담당이 Unity 에디터에서 마우스로 직접 다듬은
// 결과), 없으면 코드로 기본 레이아웃을 생성한다. 프리팹을 새로 만들거나 갱신하려면
// Tools > UGAUGA > Rebuild Hub Screen Prefabs (HubScreenPrefabBuilder.cs)를 실행할 것.
using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerConnectScreen : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "UI/Prefabs/MultiplayerConnectCanvas";

    private GameObject _canvasObject;
    private GameObject _hostPanel;
    private GameObject _joinPanel;
    private Button _tabHostButton;
    private Button _tabJoinButton;
    private Text _hostIpText;
    private Text _hostStatusText;
    private Button _hostStartButton;
    private Button _matchStartButton;
    private InputField _ipInputField;
    private Button _connectButton;
    private Text _joinStatusText;
    private Text _errorText;
    private Button _backButton;
    private bool _showingHostTab = true;
    private Action _onBack;

    public void Init(Action onBack) => _onBack = onBack;

    public void Show()
    {
        if (_canvasObject == null) CreateUi();
        _canvasObject.SetActive(true);
        _showingHostTab = true;
        RefreshTabs();
    }

    public void Hide() => _canvasObject?.SetActive(false);

    // Tools > UGAUGA > Rebuild Hub Screen Prefabs가 호출한다 - 코드 레이아웃을 강제로 새로
    // 만들어서(기존 프리팹은 무시) 반환한다. 그걸 PrefabUtility.SaveAsPrefabAsset으로 저장하면
    // 다음부터는 그 프리팹을 불러와 쓴다.
    public GameObject CreatePrefabTemplate()
    {
        BuildGeneratedUi();
        _canvasObject.SetActive(true);
        return _canvasObject;
    }

    private void Update()
    {
        if (_canvasObject == null || !_canvasObject.activeSelf) return;
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;

        bool connected = net.ConnectionState == NetworkConnectionState.Connected;

        _hostIpText.text = net.Role == NetworkRole.Host
            ? $"내 IP: {net.LocalAddressHint} (포트 {GameEventChannel.DefaultPort})"
            : "호스트로 시작하면 여기에 내 IP가 표시됩니다";
        _hostStatusText.text = net.Role == NetworkRole.Host
            ? (connected ? "클라이언트 연결됨" : "클라이언트 접속 대기 중...")
            : "";
        _hostStartButton.gameObject.SetActive(net.Role != NetworkRole.Host);
        _matchStartButton.gameObject.SetActive(net.Role == NetworkRole.Host && connected);

        _joinStatusText.text = net.Role == NetworkRole.Client
            ? (connected ? "호스트에 연결됨 - 호스트가 시작하기를 기다립니다" : "호스트 접속 시도 중...")
            : "";
        _connectButton.interactable = net.Role != NetworkRole.Client;

        _errorText.text = net.LastError ?? "";
    }

    private void RefreshTabs()
    {
        _hostPanel.SetActive(_showingHostTab);
        _joinPanel.SetActive(!_showingHostTab);
    }

    private void OnBackClicked()
    {
        NetworkSession.Instance?.Shutdown();
        Hide();
        _onBack?.Invoke();
    }

    private void CreateUi()
    {
        GameObject prefab = Resources.Load<GameObject>(CanvasPrefabResourcePath);
        if (prefab != null)
        {
            _canvasObject = Instantiate(prefab, transform, false);
            _canvasObject.name = "MultiplayerConnectCanvas";
            BindUi();
            return;
        }

        Debug.LogWarning($"[MultiplayerConnectScreen] {CanvasPrefabResourcePath} 프리팹을 찾지 못해 " +
            "코드 기본 레이아웃을 사용합니다. Tools > UGAUGA > Rebuild Hub Screen Prefabs로 만들 수 있습니다.");
        BuildGeneratedUi();
    }

    // 프리팹이든 코드 생성이든 동일한 오브젝트 이름 구조를 갖는다 - 이름으로 찾아 참조를
    // 복원하고 클릭 리스너를 (다시) 붙인다. UnityEvent는 프리팹에 저장돼도 C# 람다까지
    // 같이 저장되지는 않으므로 리스너는 항상 코드에서 새로 건다.
    private void BindUi()
    {
        Transform root = _canvasObject.transform;

        _hostPanel = UiBuilder.FindDescendant(root, "HostPanel")?.gameObject;
        _joinPanel = UiBuilder.FindDescendant(root, "JoinPanel")?.gameObject;
        _tabHostButton = UiBuilder.FindDescendant(root, "TabHost")?.GetComponent<Button>();
        _tabJoinButton = UiBuilder.FindDescendant(root, "TabJoin")?.GetComponent<Button>();
        _hostIpText = UiBuilder.FindDescendant(root, "IpText")?.GetComponent<Text>();
        _hostStatusText = UiBuilder.FindDescendant(root, "HostStatusText")?.GetComponent<Text>();
        _hostStartButton = UiBuilder.FindDescendant(root, "HostStartButton")?.GetComponent<Button>();
        _matchStartButton = UiBuilder.FindDescendant(root, "MatchStartButton")?.GetComponent<Button>();
        _ipInputField = UiBuilder.FindDescendant(root, "IpInput")?.GetComponent<InputField>();
        _connectButton = UiBuilder.FindDescendant(root, "ConnectButton")?.GetComponent<Button>();
        _joinStatusText = UiBuilder.FindDescendant(root, "JoinStatusText")?.GetComponent<Text>();
        _errorText = UiBuilder.FindDescendant(root, "ErrorText")?.GetComponent<Text>();
        _backButton = UiBuilder.FindDescendant(root, "BackButton")?.GetComponent<Button>();

        if (_hostPanel == null || _joinPanel == null || _tabHostButton == null || _tabJoinButton == null ||
            _hostIpText == null || _hostStatusText == null || _hostStartButton == null ||
            _matchStartButton == null || _ipInputField == null || _connectButton == null ||
            _joinStatusText == null || _errorText == null || _backButton == null)
        {
            throw new InvalidOperationException(
                $"{CanvasPrefabResourcePath}의 필수 오브젝트 이름이 바뀌었습니다 - 이름을 유지하거나 " +
                "Tools > UGAUGA > Rebuild Hub Screen Prefabs로 프리팹을 다시 만드세요.");
        }

        WireListeners();
    }

    private void WireListeners()
    {
        _tabHostButton.onClick.RemoveAllListeners();
        _tabHostButton.onClick.AddListener(() => { _showingHostTab = true; RefreshTabs(); });
        _tabJoinButton.onClick.RemoveAllListeners();
        _tabJoinButton.onClick.AddListener(() => { _showingHostTab = false; RefreshTabs(); });

        _hostStartButton.onClick.RemoveAllListeners();
        _hostStartButton.onClick.AddListener(() =>
        {
            SoloBotController.SetEnabled(false);
            NetworkSession.Instance?.StartHost();
        });
        _matchStartButton.onClick.RemoveAllListeners();
        _matchStartButton.onClick.AddListener(() => MatchController.Instance?.StartMatch());

        _connectButton.onClick.RemoveAllListeners();
        _connectButton.onClick.AddListener(() =>
        {
            SoloBotController.SetEnabled(false);
            string ip = string.IsNullOrWhiteSpace(_ipInputField.text) ? "127.0.0.1" : _ipInputField.text.Trim();
            NetworkSession.Instance?.StartClient(ip);
        });

        _backButton.onClick.RemoveAllListeners();
        _backButton.onClick.AddListener(OnBackClicked);
    }

    private void BuildGeneratedUi()
    {
        _canvasObject = UiBuilder.CreateOverlayCanvas("MultiplayerConnectCanvas", transform, 32740);
        RectTransform root = _canvasObject.GetComponent<RectTransform>();

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("multiplayer_panel_main"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 1000f));

        Text title = UiBuilder.AddText(root, "Title", "2인 플레이 - 연결 설정", 42);
        UiBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f),
            new Vector2(1200f, 80f));

        _tabHostButton = UiBuilder.AddButton(root, "TabHost", ArtAssets.LoadUi("multiplayer_tab_host"),
            new Vector2(0.5f, 1f), new Vector2(-260f, -220f), new Vector2(480f, 200f));
        _tabJoinButton = UiBuilder.AddButton(root, "TabJoin", ArtAssets.LoadUi("multiplayer_tab_join"),
            new Vector2(0.5f, 1f), new Vector2(260f, -220f), new Vector2(480f, 200f));

        BuildHostPanel(root);
        BuildJoinPanel(root);

        _errorText = UiBuilder.AddText(root, "ErrorText", "", 24);
        UiBuilder.SetRect(_errorText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
            new Vector2(1300f, 60f));
        _errorText.color = new Color(1f, 0.55f, 0.45f, 1f);

        _backButton = UiBuilder.AddButton(root, "BackButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(130f, 130f));

        WireListeners();
    }

    private void BuildHostPanel(RectTransform root)
    {
        _hostPanel = new GameObject("HostPanel");
        _hostPanel.transform.SetParent(root, false);
        RectTransform rt = _hostPanel.AddComponent<RectTransform>();
        UiBuilder.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(1300f, 500f));

        _hostStartButton = UiBuilder.AddButton(rt, "HostStartButton",
            ArtAssets.LoadUi("multiplayer_button_host_start"), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(700f, 250f));

        UiBuilder.AddImage(rt, "IpDisplayFrame", ArtAssets.LoadUi("multiplayer_ip_display_frame"),
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1200f, 200f));
        _hostIpText = UiBuilder.AddText(rt, "IpText", "", 32);
        UiBuilder.SetRect(_hostIpText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
            new Vector2(1100f, 200f));

        _hostStatusText = UiBuilder.AddText(rt, "HostStatusText", "", 30);
        UiBuilder.SetRect(_hostStatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
            new Vector2(1200f, 60f));

        // 클라이언트가 연결되면 이 버튼으로 실제 매치를 시작한다 - 기존 "게임 시작" 버튼
        // 이미지를 그대로 재사용(2인/1인 버튼으로 나뉘기 전 원래 있던 스프라이트).
        _matchStartButton = UiBuilder.AddButton(rt, "MatchStartButton", ArtAssets.LoadUi("button_game_start"),
            new Vector2(0.5f, 0f), new Vector2(0f, -20f), new Vector2(500f, 200f));
    }

    private void BuildJoinPanel(RectTransform root)
    {
        _joinPanel = new GameObject("JoinPanel");
        _joinPanel.transform.SetParent(root, false);
        RectTransform rt = _joinPanel.AddComponent<RectTransform>();
        UiBuilder.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(1300f, 500f));

        Text label = UiBuilder.AddText(rt, "Label", "호스트 IP 입력", 30);
        UiBuilder.SetRect(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f),
            new Vector2(1200f, 60f));

        UiBuilder.AddImage(rt, "IpInputFrame", ArtAssets.LoadUi("multiplayer_ip_input_frame"),
            new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(1200f, 200f));
        _ipInputField = UiBuilder.AddInputField(rt, "IpInput", new Vector2(0.5f, 1f),
            new Vector2(0f, -100f), new Vector2(1050f, 200f), "예: 192.168.0.5");
        _ipInputField.text = "127.0.0.1";

        _connectButton = UiBuilder.AddButton(rt, "ConnectButton", ArtAssets.LoadUi("multiplayer_button_connect"),
            new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(500f, 200f));

        _joinStatusText = UiBuilder.AddText(rt, "JoinStatusText", "", 28);
        UiBuilder.SetRect(_joinStatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -50f),
            new Vector2(1200f, 60f));
    }
}

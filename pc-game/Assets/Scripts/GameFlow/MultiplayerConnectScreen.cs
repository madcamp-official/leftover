// "2인 플레이" 진입 시 뜨는 호스트/참가 연결 화면. 예전에 HubController.DrawNetworkPanel이
// OnGUI로 임시로 그리던 것을 UI_화면_확장_에셋_계획.md의 정식 이미지 UI로 교체했다.
// HubController가 Start()에서 이 컴포넌트를 만들고 Init()으로 "뒤로가기" 콜백을 넘겨준다.
using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerConnectScreen : MonoBehaviour
{
    private GameObject _root;
    private GameObject _hostPanel;
    private GameObject _joinPanel;
    private Text _hostIpText;
    private Text _hostStatusText;
    private Button _hostStartButton;
    private Button _matchStartButton;
    private InputField _ipInputField;
    private Button _connectButton;
    private Text _joinStatusText;
    private Text _errorText;
    private bool _showingHostTab = true;
    private Action _onBack;

    public void Init(Action onBack) => _onBack = onBack;

    public void Show()
    {
        if (_root == null) Build();
        _root.SetActive(true);
        _showingHostTab = true;
        RefreshTabs();
    }

    public void Hide() => _root?.SetActive(false);

    private void Update()
    {
        if (_root == null || !_root.activeSelf) return;
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

    private void Build()
    {
        _root = UiBuilder.CreateOverlayCanvas("MultiplayerConnectCanvas", transform, 32740);
        RectTransform root = _root.GetComponent<RectTransform>();

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("multiplayer_panel_main"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 1000f));

        Text title = UiBuilder.AddText(root, "Title", "2인 플레이 - 연결 설정", 42);
        UiBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f),
            new Vector2(1200f, 80f));

        Button tabHost = UiBuilder.AddButton(root, "TabHost", ArtAssets.LoadUi("multiplayer_tab_host"),
            new Vector2(0.5f, 1f), new Vector2(-260f, -220f), new Vector2(480f, 200f));
        Button tabJoin = UiBuilder.AddButton(root, "TabJoin", ArtAssets.LoadUi("multiplayer_tab_join"),
            new Vector2(0.5f, 1f), new Vector2(260f, -220f), new Vector2(480f, 200f));
        tabHost.onClick.AddListener(() => { _showingHostTab = true; RefreshTabs(); });
        tabJoin.onClick.AddListener(() => { _showingHostTab = false; RefreshTabs(); });

        BuildHostPanel(root);
        BuildJoinPanel(root);

        _errorText = UiBuilder.AddText(root, "ErrorText", "", 24);
        UiBuilder.SetRect(_errorText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
            new Vector2(1300f, 60f));
        _errorText.color = new Color(1f, 0.55f, 0.45f, 1f);

        Button back = UiBuilder.AddButton(root, "BackButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(130f, 130f));
        back.onClick.AddListener(OnBackClicked);
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
        _hostStartButton.onClick.AddListener(() =>
        {
            SoloBotController.SetEnabled(false);
            NetworkSession.Instance?.StartHost();
        });

        UiBuilder.AddImage(rt, "IpDisplayFrame", ArtAssets.LoadUi("multiplayer_ip_display_frame"),
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1200f, 200f));
        _hostIpText = UiBuilder.AddText(rt, "IpText", "", 32);
        UiBuilder.SetRect(_hostIpText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
            new Vector2(1100f, 200f));

        _hostStatusText = UiBuilder.AddText(rt, "StatusText", "", 30);
        UiBuilder.SetRect(_hostStatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
            new Vector2(1200f, 60f));

        // 클라이언트가 연결되면 이 버튼으로 실제 매치를 시작한다 - 기존 "게임 시작" 버튼
        // 이미지를 그대로 재사용(2인/1인 버튼으로 나뉘기 전 원래 있던 스프라이트).
        _matchStartButton = UiBuilder.AddButton(rt, "MatchStartButton", ArtAssets.LoadUi("button_game_start"),
            new Vector2(0.5f, 0f), new Vector2(0f, -20f), new Vector2(500f, 200f));
        _matchStartButton.onClick.AddListener(() => MatchController.Instance?.StartMatch());
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
        _connectButton.onClick.AddListener(() =>
        {
            SoloBotController.SetEnabled(false);
            string ip = string.IsNullOrWhiteSpace(_ipInputField.text) ? "127.0.0.1" : _ipInputField.text.Trim();
            NetworkSession.Instance?.StartClient(ip);
        });

        _joinStatusText = UiBuilder.AddText(rt, "StatusText", "", 28);
        UiBuilder.SetRect(_joinStatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -50f),
            new Vector2(1200f, 60f));
    }
}

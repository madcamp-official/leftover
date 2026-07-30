// 호스트-클라이언트 세션 싱글턴 (설계 문서 5장). Hub 화면에서 역할을 고르면 이 클래스가
// GameEventChannel의 수명주기를 관리하고, 하트비트로 끊김을 감지하며, 호스트-클라이언트
// 시계 오프셋을 추정한다. 씬을 넘나들어야 하므로 DontDestroyOnLoad 싱글턴이다.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class NetworkSession : MonoBehaviour
{
    private static NetworkSession _instance;

    // 다른 싱글턴들과 동일한 이유(도메인 리로드로 static 참조만 끊기는 문제)로 null이면
    // 씬에서 다시 찾는다.
    public static NetworkSession Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<NetworkSession>();
            return _instance;
        }
        private set => _instance = value;
    }

    public NetworkRole Role { get; private set; } = NetworkRole.Offline;
    public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Disconnected;
    public string LocalAddressHint { get; private set; } = "";
    public string RemoteAddress { get; private set; } = "";
    public string LastError { get; private set; } = "";

    // Role/RemoteAddress는 Shutdown()에서 Offline/""로 되돌아가므로, 예기치 않게 끊긴 뒤
    // 재연결을 시도하려면 "직전에 뭐였는지"를 따로 기억해둬야 한다. 사용자가 뒤로가기로
    // 명시적으로 나갈 때만(Disconnect()) 이 값도 같이 지워서 재연결 시도를 멈춘다 -
    // NetworkReconnectOverlay가 이 값으로 "예기치 않은 끊김"인지 판단한다.
    public NetworkRole LastRole { get; private set; } = NetworkRole.Offline;
    public string LastHostAddress { get; private set; } = "";

    // "한 번이라도 실제로 연결된 적 있음" - NetworkReconnectOverlay가 "재연결"과 "최초 접속
    // 시도 실패"를 구분하는 데 쓴다. 최초 접속 실패(오타 IP, 호스트가 아직 안 켰음 등)는
    // MultiplayerConnectScreen 자체 UI(_errorText)가 이미 처리하므로, 그 경우까지 전체
    // 화면 재연결 오버레이로 덮으면 안 된다 - 한 번이라도 붙었다가 끊긴 경우에만 띄운다.
    public bool HadSuccessfulConnection { get; private set; }

    // 클라이언트에서 "호스트 기준 지금"을 계산할 때 쓰는 보정값:
    // 호스트시각 추정 = Time.realtimeSinceStartupAsDouble(클라 로컬) + ClockOffsetToHost
    public double ClockOffsetToHost { get; private set; }

    public bool IsNetworked => Role != NetworkRole.Offline;
    public bool IsHost => Role == NetworkRole.Host;
    public bool IsClient => Role == NetworkRole.Client;

    public event Action OnConnected;
    public event Action OnDisconnected;

    private GameEventChannel _channel;
    private readonly Dictionary<string, Action<NetworkEvent>> _handlers = new Dictionary<string, Action<NetworkEvent>>();
    private float _lastHeartbeatSentAt;
    private float _lastHeartbeatReceivedAt;
    private const float HeartbeatIntervalSeconds = 1f;
    // 실제 노트북 두 대를 같은 와이파이에 놓고 돌리면, 카메라 프리뷰/포즈 UDP 스트림까지
    // 같은 네트워크를 타면서 공유기 부하나 순간 간섭으로 몇 초씩 지연되는 경우가 흔하다.
    // 5초는 그런 정상적인 혼잡까지 "끊김"으로 오판해서 불필요하게 재연결을 유발했다(실측
    // 확인된 불안정성의 한 원인) - 실제 끊김은 대부분 몇 초가 아니라 훨씬 오래 지속되므로
    // 여유를 넉넉히 둬도 진짜 끊김 감지가 느려지는 체감은 거의 없다.
    private const float HeartbeatTimeoutSeconds = 12f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartHost(int port = GameEventChannel.DefaultPort)
    {
        Shutdown();
        Role = NetworkRole.Host;
        LastRole = NetworkRole.Host;
        ConnectionState = NetworkConnectionState.Listening;
        LocalAddressHint = ResolveLocalAddressHint();
        _channel = new GameEventChannel();
        _channel.OnConnected += HandleChannelConnected;
        _channel.OnDisconnected += HandleChannelDisconnected;
        _channel.OnError += HandleChannelError;
        _channel.StartHost(port);
    }

    public void StartClient(string hostAddress, int port = GameEventChannel.DefaultPort)
    {
        Shutdown();
        Role = NetworkRole.Client;
        LastRole = NetworkRole.Client;
        LastHostAddress = hostAddress;
        ConnectionState = NetworkConnectionState.Connecting;
        _channel = new GameEventChannel();
        _channel.OnConnected += HandleChannelConnected;
        _channel.OnDisconnected += HandleChannelDisconnected;
        _channel.OnError += HandleChannelError;
        _channel.StartClient(hostAddress, port);
    }

    // 연결을 끊고 오프라인(로컬 단독) 모드로 되돌린다 - 개발/테스트나 네트워크 없이 한 대로
    // 돌리는 상황을 위해 이 모드는 계속 지원한다(설계 문서 8장). LastRole/LastHostAddress는
    // 일부러 안 지운다 - 하트비트 타임아웃처럼 "예기치 않게" 이 메서드가 불렸을 때도
    // NetworkReconnectOverlay가 재연결을 시도할 수 있어야 하기 때문. 사용자가 직접 나가는
    // 경우는 Disconnect()를 쓸 것.
    public void Shutdown()
    {
        if (_channel != null)
        {
            _channel.OnConnected -= HandleChannelConnected;
            _channel.OnDisconnected -= HandleChannelDisconnected;
            _channel.OnError -= HandleChannelError;
            _channel.Stop();
        }
        _channel = null;
        Role = NetworkRole.Offline;
        ConnectionState = NetworkConnectionState.Disconnected;
        RemoteAddress = "";
    }

    // 사용자가 명시적으로 연결을 끊을 때(뒤로가기 버튼 등) 호출 - Shutdown()과 달리
    // LastRole/LastHostAddress도 같이 지워서 NetworkReconnectOverlay가 재연결을 시도하지
    // 않게 한다.
    public void Disconnect()
    {
        Shutdown();
        LastRole = NetworkRole.Offline;
        LastHostAddress = "";
        HadSuccessfulConnection = false;
    }

    // NetworkReconnectOverlay가 주기적으로 호출.
    //
    // 예전엔 "호스트는 AcceptLoop가 계속 살아서 기다리고 있으니 아무것도 안 해도 된다"고
    // 생각했는데, 틀렸다 - 클라이언트가 정상적으로 접속을 끊는 경우에만 그렇고, 하트비트
    // 타임아웃(케이블이 뽑히는 등 정상 종료 신호 없이 끊기는 경우)은 호스트 자신의
    // Update()에서도 똑같이 걸려서 Shutdown()이 불린다 - 이게 GameEventChannel.Stop()의
    // _listener.Stop()까지 닫아버려서 AcceptLoop 자체가 완전히 끝나버린다. 그러면 클라이언트가
    // 아무리 재접속을 시도해도 받아줄 리스너가 없어서 영원히 실패한다 - 그래서 호스트도
    // 리스너가 죽어 있으면 다시 StartHost()로 열어줘야 한다.
    public bool TryReconnect()
    {
        switch (LastRole)
        {
            case NetworkRole.Host:
                StartHost();
                return true;
            case NetworkRole.Client when !string.IsNullOrEmpty(LastHostAddress):
                StartClient(LastHostAddress);
                return true;
            default:
                return false;
        }
    }

    private void HandleChannelConnected()
    {
        ConnectionState = NetworkConnectionState.Connected;
        RemoteAddress = _channel?.RemoteAddress ?? "";
        HadSuccessfulConnection = true;
        _lastHeartbeatReceivedAt = Time.unscaledTime;
        LastError = "";
        OnConnected?.Invoke();
        if (Role == NetworkRole.Client)
            Send("__ping", new PingPayload { clientSendTime = Time.realtimeSinceStartupAsDouble });
    }

    private void HandleChannelDisconnected()
    {
        ConnectionState = NetworkConnectionState.Disconnected;
        OnDisconnected?.Invoke();
    }

    // 접속 실패는 조용히 넘기면 안 된다 - 사유를 LastError에 남기고 역할을 Offline으로
    // 되돌려서, Hub 화면이 다시 IP 입력 상태로 돌아가되 실패 이유는 화면에 계속 보이게 한다.
    private void HandleChannelError(string message)
    {
        Shutdown();
        LastError = message;
    }

    // type 이벤트가 올 때마다 handler를 부른다. 씬 전환에도 살아남는 싱글턴이므로, 구독자가
    // 씬마다 다시 구독하고 싶다면 반드시 Unsubscribe로 짝을 맞춰야 중복 호출을 피한다.
    public void Subscribe(string type, Action<NetworkEvent> handler)
    {
        _handlers[type] = _handlers.TryGetValue(type, out Action<NetworkEvent> existing) ? existing + handler : handler;
    }

    public void Unsubscribe(string type, Action<NetworkEvent> handler)
    {
        if (_handlers.TryGetValue(type, out Action<NetworkEvent> existing))
            _handlers[type] = existing - handler;
    }

    public void Send<T>(string type, T payload)
    {
        if (_channel == null) return;
        var evt = new NetworkEvent
        {
            type = type,
            senderTime = Time.realtimeSinceStartupAsDouble,
            json = JsonUtility.ToJson(payload),
        };
        _channel.Send(evt);
    }

    public static T Read<T>(NetworkEvent evt) => JsonUtility.FromJson<T>(evt.json);

    private void Update()
    {
        _channel?.Poll(HandleEvent);

        if (ConnectionState != NetworkConnectionState.Connected) return;

        if (Time.unscaledTime - _lastHeartbeatSentAt > HeartbeatIntervalSeconds)
        {
            _lastHeartbeatSentAt = Time.unscaledTime;
            Send("__hb", new HeartbeatPayload());
        }
        if (Time.unscaledTime - _lastHeartbeatReceivedAt > HeartbeatTimeoutSeconds)
        {
            LastError = "상대방과의 연결이 끊겼습니다 (응답 없음)";
            Shutdown();
        }
    }

    private void HandleEvent(NetworkEvent evt)
    {
        _lastHeartbeatReceivedAt = Time.unscaledTime;

        switch (evt.type)
        {
            case "__hb":
                return;
            case "__ping":
            {
                PingPayload ping = Read<PingPayload>(evt);
                Send("__pong", new PongPayload
                {
                    clientSendTime = ping.clientSendTime,
                    hostRecvTime = Time.realtimeSinceStartupAsDouble,
                });
                return;
            }
            case "__pong":
            {
                PongPayload pong = Read<PongPayload>(evt);
                double now = Time.realtimeSinceStartupAsDouble;
                double roundTrip = now - pong.clientSendTime;
                double hostTimeEstimate = pong.hostRecvTime + roundTrip * 0.5;
                ClockOffsetToHost = hostTimeEstimate - now;
                return;
            }
        }

        if (_handlers.TryGetValue(evt.type, out Action<NetworkEvent> handler))
            handler?.Invoke(evt);
    }

    private void OnDestroy() => _channel?.Stop();

    private static string ResolveLocalAddressHint()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return socket.LocalEndPoint is System.Net.IPEndPoint ep ? ep.Address.ToString() : "확인 실패";
        }
        catch
        {
            return "IP 확인 실패 - ipconfig/ifconfig로 직접 확인";
        }
    }

    [Serializable] private class HeartbeatPayload { }
    [Serializable] private class PingPayload { public double clientSendTime; }
    [Serializable] private class PongPayload { public double clientSendTime; public double hostRecvTime; }
}

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
    private const float HeartbeatTimeoutSeconds = 5f;

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
        ConnectionState = NetworkConnectionState.Listening;
        LocalAddressHint = ResolveLocalAddressHint();
        _channel = new GameEventChannel();
        _channel.OnConnected += HandleChannelConnected;
        _channel.OnDisconnected += HandleChannelDisconnected;
        _channel.StartHost(port);
    }

    public void StartClient(string hostAddress, int port = GameEventChannel.DefaultPort)
    {
        Shutdown();
        Role = NetworkRole.Client;
        ConnectionState = NetworkConnectionState.Connecting;
        _channel = new GameEventChannel();
        _channel.OnConnected += HandleChannelConnected;
        _channel.OnDisconnected += HandleChannelDisconnected;
        _channel.StartClient(hostAddress, port);
    }

    // 연결을 끊고 오프라인(로컬 단독) 모드로 되돌린다 - 개발/테스트나 네트워크 없이 한 대로
    // 돌리는 상황을 위해 이 모드는 계속 지원한다(설계 문서 8장).
    public void Shutdown()
    {
        if (_channel != null)
        {
            _channel.OnConnected -= HandleChannelConnected;
            _channel.OnDisconnected -= HandleChannelDisconnected;
            _channel.Stop();
        }
        _channel = null;
        Role = NetworkRole.Offline;
        ConnectionState = NetworkConnectionState.Disconnected;
        RemoteAddress = "";
    }

    private void HandleChannelConnected()
    {
        ConnectionState = NetworkConnectionState.Connected;
        RemoteAddress = _channel?.RemoteAddress ?? "";
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

// vision-server가 UDP 9101로 보내는 저해상도 JPEG 프리뷰를 수신한다.
// 포즈 스트림과 마찬가지로 소켓 수신은 백그라운드 스레드에서, Texture2D 갱신은 Unity 메인
// 스레드에서 처리한다. 패킷 형식: "UGAPREV1|p1|" 또는 "UGAPREV1|p2|" + JPEG bytes.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public sealed class CameraPreviewReceiver : MonoBehaviour
{
    private sealed class PreviewPacket
    {
        public string Player;
        public byte[] Jpeg;
    }

    private static CameraPreviewReceiver _instance;
    public static CameraPreviewReceiver Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<CameraPreviewReceiver>();
            return _instance;
        }
        private set => _instance = value;
    }

    public int listenPort = 9101;
    public float staleTimeout = 1.2f;

    private UdpClient _udp;
    private Thread _thread;
    private volatile bool _running;
    private readonly ConcurrentQueue<PreviewPacket> _incoming = new ConcurrentQueue<PreviewPacket>();
    private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, float> _lastSeen = new Dictionary<string, float>();
    // 호스트-클라이언트 구조에서 vision-server는 프리뷰를 호스트로만 보낸다(설계 문서 2장).
    // 그래서 호스트가 받은 프레임을 클라이언트에게 다시 UDP로 그대로 전달해야 클라이언트
    // 로딩 화면에도 카메라가 보인다 - 그 전달 대상 프레임을 여기 담아둔다.
    // (예전에는 이걸 base64로 인코딩해 게임 이벤트용 TCP 채널로 보냈는데, 하트비트/점수처럼
    // 시간이 중요한 이벤트와 같은 소켓·같은 스트림을 타면서 head-of-line blocking으로
    // 그 이벤트들을 지연시킬 위험이 있었다 - UDP 직접 전달로 완전히 분리했다.)
    private readonly Dictionary<string, byte[]> _relayPending = new Dictionary<string, byte[]>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        try
        {
            _udp = new UdpClient(listenPort);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CameraPreview] UDP {listenPort} 바인딩 실패: {e.Message}");
            enabled = false;
            return;
        }

        _running = true;
        _thread = new Thread(ReceiveLoop) { IsBackground = true };
        _thread.Start();
    }

    private void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, listenPort);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref endpoint);
                PreviewPacket packet = Parse(data);
                if (packet != null) _incoming.Enqueue(packet);
            }
            catch (SocketException) { }
            catch (Exception e) { Debug.LogWarning($"[CameraPreview] recv error: {e.Message}"); }
        }
    }

    private static PreviewPacket Parse(byte[] data)
    {
        const string prefix = "UGAPREV1|";
        if (data == null || data.Length < prefix.Length + 4) return null;
        for (int i = 0; i < prefix.Length; i++)
            if (data[i] != (byte)prefix[i]) return null;

        int idEnd = Array.IndexOf(data, (byte)'|', prefix.Length);
        if (idEnd < 0 || idEnd + 1 >= data.Length) return null;
        string player = Encoding.ASCII.GetString(data, prefix.Length, idEnd - prefix.Length);
        int jpegLength = data.Length - idEnd - 1;
        var jpeg = new byte[jpegLength];
        Buffer.BlockCopy(data, idEnd + 1, jpeg, 0, jpegLength);
        return new PreviewPacket { Player = player, Jpeg = jpeg };
    }

    private void Update()
    {
        // 같은 플레이어의 오래된 프레임은 버리고 이번 Update에서 가장 마지막 JPEG만 디코딩한다.
        var latest = new Dictionary<string, byte[]>();
        while (_incoming.TryDequeue(out PreviewPacket packet))
        {
            latest[packet.Player] = packet.Jpeg;
            _relayPending[packet.Player] = packet.Jpeg;
        }

        foreach (KeyValuePair<string, byte[]> pair in latest)
        {
            if (!_textures.TryGetValue(pair.Key, out Texture2D texture) || texture == null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                texture.name = $"CameraPreview_{pair.Key}";
                _textures[pair.Key] = texture;
            }
            if (ImageConversion.LoadImage(texture, pair.Value, false))
                _lastSeen[pair.Key] = Time.unscaledTime;
        }
    }

    // 호스트가 클라이언트로 중계할 새 프레임을 꺼낸다. 꺼낸 프레임은 대기열에서 지워지므로
    // 같은 프레임이 두 번 전송되지 않는다.
    public bool TryDequeueRelayFrame(out string player, out byte[] jpeg)
    {
        player = null;
        jpeg = null;
        if (_relayPending.Count == 0) return false;

        // 순회 중 Remove를 피하려고 키를 먼저 확정한 뒤 지운다.
        foreach (string key in _relayPending.Keys)
        {
            player = key;
            break;
        }
        jpeg = _relayPending[player];
        _relayPending.Remove(player);
        return true;
    }

    // 호스트가 TryDequeueRelayFrame으로 꺼낸 프레임을 클라이언트의 CameraPreviewReceiver
    // 포트로 직접 재전송한다 - 같은 "UGAPREV1|player|" + JPEG 포맷 그대로라, 클라이언트
    // 쪽은 vision-server에서 직접 받은 프레임과 완전히 같은 경로(ReceiveLoop/Parse)로
    // 처리한다. 별도의 "중계 수신" 코드가 필요 없다.
    public void ForwardRelayFrame(string player, byte[] jpeg, IPEndPoint destination)
    {
        if (_udp == null || destination == null || string.IsNullOrEmpty(player) || jpeg == null) return;
        byte[] header = Encoding.ASCII.GetBytes($"UGAPREV1|{player}|");
        var packet = new byte[header.Length + jpeg.Length];
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        Buffer.BlockCopy(jpeg, 0, packet, header.Length, jpeg.Length);
        try { _udp.Send(packet, packet.Length, destination); }
        catch (Exception e) { Debug.LogWarning($"[CameraPreview] 클라이언트로 중계 전송 실패: {e.Message}"); }
    }

    public Texture GetTexture(PlayerId player)
    {
        string id = player == PlayerId.P1 ? "p1" : "p2";
        if (_textures.TryGetValue(id, out Texture2D texture)) return texture;
        return _textures.TryGetValue("all", out texture) ? texture : null;
    }

    public bool IsConnected(PlayerId player)
    {
        string id = player == PlayerId.P1 ? "p1" : "p2";
        if (!_lastSeen.TryGetValue(id, out float seen) && !_lastSeen.TryGetValue("all", out seen))
            return false;
        return Time.unscaledTime - seen <= staleTimeout;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _running = false;
        _udp?.Close();
        _thread?.Join(200);
        foreach (Texture2D texture in _textures.Values)
            if (texture != null) Destroy(texture);
    }
}

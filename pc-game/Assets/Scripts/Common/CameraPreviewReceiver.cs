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
        // 호스트가 TCP로 중계해준 프레임인지 여부. 중계로 들어온 프레임을 다시 중계 대기열에
        // 넣으면 무한 에코가 되므로 구분해야 한다.
        public bool FromRelay;
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
    // 그래서 호스트가 받은 프레임을 TCP 이벤트 채널로 클라이언트에 중계해야 클라이언트
    // 로딩 화면에도 카메라가 보인다 - 그 중계 대상 프레임을 여기 담아둔다.
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
            // UDP로 직접 받은 프레임만 중계 대상이다(중계로 받은 걸 다시 중계하면 에코 루프).
            if (!packet.FromRelay) _relayPending[packet.Player] = packet.Jpeg;
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

    // 클라이언트가 호스트로부터 중계받은 프레임을 주입한다 - 이후 디코딩/스테일 판정은
    // UDP로 직접 받은 경우와 완전히 같은 경로를 탄다.
    public void ApplyRelayedFrame(string player, byte[] jpeg)
    {
        if (string.IsNullOrEmpty(player) || jpeg == null || jpeg.Length == 0) return;
        _incoming.Enqueue(new PreviewPacket { Player = player, Jpeg = jpeg, FromRelay = true });
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

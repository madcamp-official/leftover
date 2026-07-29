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
            latest[packet.Player] = packet.Jpeg;

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

// PC 쪽(pc-game 프로젝트)에 넣는 스크립트.
// PROTOCOL.md의 "1. 시간 동기화 (NTP 핑퐁)" 구현체.
// Unity 프로젝트 생성 후 Assets/Scripts/Network/ 에 복사해서 사용.
//
// 사용법: 씬에 빈 GameObject를 만들고 이 컴포넌트를 붙인 뒤 phoneIp를 설정.
// 다른 스크립트에서는 TimeSyncClient.Instance.ToPcTime(phoneTimestampMs) 로
// 폰 타임스탬프를 PC 기준 시각으로 변환해서 쓰면 된다.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TimeSyncClient : MonoBehaviour
{
    public static TimeSyncClient Instance { get; private set; }

    [Header("Network")]
    public string phoneIp = "192.168.0.2";
    public int syncPort = 9001;

    [Header("Sync Settings")]
    public int initialSyncSamples = 20;
    public float resyncIntervalSeconds = 30f;
    public int resyncSamples = 5;

    // phone_clock = pc_clock + Offset  =>  pc_clock = phone_clock - Offset
    public double Offset { get; private set; } = 0.0;
    public double LastRttMs { get; private set; } = 0.0;
    public bool HasSynced { get; private set; } = false;

    [Serializable]
    private class PingMsg
    {
        public string type = "ping";
        public int seq;
        public double t1;
    }

    [Serializable]
    private class PongMsg
    {
        public string type;
        public int seq;
        public double t1;
        public double t2;
        public double t3;
    }

    private UdpClient _udp;
    private IPEndPoint _phoneEndpoint;
    private Thread _recvThread;
    private volatile bool _running;
    private int _seq;

    private readonly Dictionary<int, double> _pendingT1 = new Dictionary<int, double>();
    private readonly List<double> _sampleOffsets = new List<double>();
    private readonly object _lock = new object();

    private static double NowMs() =>
        (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _udp = new UdpClient(0); // OS가 임의 포트 할당, 응답은 phoneEndpoint에서 받음
        _phoneEndpoint = new IPEndPoint(IPAddress.Parse(phoneIp), syncPort);

        _running = true;
        _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        _recvThread.Start();

        StartCoroutine(SyncRoutine(initialSyncSamples, immediate: true));
    }

    private System.Collections.IEnumerator SyncRoutine(int sampleCount, bool immediate)
    {
        if (!immediate) yield return new WaitForSeconds(resyncIntervalSeconds);

        lock (_lock) { _sampleOffsets.Clear(); }

        for (int i = 0; i < sampleCount; i++)
        {
            SendPing();
            yield return new WaitForSeconds(0.05f); // 핑 사이 50ms 간격
        }

        yield return new WaitForSeconds(0.5f); // 마지막 pong 도착 대기

        lock (_lock)
        {
            if (_sampleOffsets.Count > 0)
            {
                _sampleOffsets.Sort();
                Offset = _sampleOffsets[_sampleOffsets.Count / 2]; // median
                HasSynced = true;
                Debug.Log($"[TimeSync] offset={Offset:F2}ms samples={_sampleOffsets.Count} rtt(last)={LastRttMs:F2}ms");
            }
        }

        StartCoroutine(SyncRoutine(resyncSamples, immediate: false));
    }

    private void SendPing()
    {
        var msg = new PingMsg { seq = _seq++, t1 = NowMs() };
        lock (_lock) { _pendingT1[msg.seq] = msg.t1; }
        string json = JsonUtility.ToJson(msg);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _udp.Send(bytes, bytes.Length, _phoneEndpoint);
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] data = _udp.Receive(ref remote);
                double t4 = NowMs();
                string json = System.Text.Encoding.UTF8.GetString(data);
                var pong = JsonUtility.FromJson<PongMsg>(json);
                if (pong == null || pong.type != "pong") continue;

                double t1;
                lock (_lock)
                {
                    if (!_pendingT1.TryGetValue(pong.seq, out t1)) continue;
                    _pendingT1.Remove(pong.seq);
                }

                double rtt = (t4 - t1) - (pong.t3 - pong.t2);
                double offset = ((pong.t2 - t1) + (pong.t3 - t4)) / 2.0;

                lock (_lock)
                {
                    _sampleOffsets.Add(offset);
                    LastRttMs = rtt;
                }
            }
            catch (SocketException) { /* shutting down */ }
            catch (Exception e) { Debug.LogWarning($"[TimeSync] recv error: {e.Message}"); }
        }
    }

    // 폰 타임스탬프(ms) -> PC 기준 타임스탬프(ms)
    public double ToPcTime(double phoneTimeMs) => phoneTimeMs - Offset;

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}

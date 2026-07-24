// PC 쪽(pc-game 프로젝트)에 넣는 스크립트.
// PROTOCOL.md의 "2. 센서 스트리밍" 수신부. 백그라운드 스레드에서 UDP로 받고,
// 메인 스레드(Update)에서 큐를 비워 모션 분류기로 넘긴다.
// Unity 프로젝트 생성 후 Assets/Scripts/Network/ 에 복사.
// TimeSyncClient와 같은 씬에 있어야 함 (offset 보정에 사용).

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class SensorReceiver : MonoBehaviour
{
    public int sensorPort = 9000;

    [Serializable]
    public class SensorSample
    {
        public int seq;
        public double t; // 폰 기준 시각 (raw, 아직 보정 전)
        public float ax, ay, az;
        public float gx, gy, gz;

        [NonSerialized] public double pcTime; // 변환 후 PC 기준 시각 (Dequeue 시 채워짐)
    }

    // 모션 분류기(Tier 1 임계값 분류 등)가 여기서 꺼내 쓰면 됨.
    public readonly ConcurrentQueue<SensorSample> Samples = new ConcurrentQueue<SensorSample>();

    private UdpClient _udp;
    private Thread _recvThread;
    private volatile bool _running;
    private int _lastSeq = -1;

    private void Start()
    {
        _udp = new UdpClient(sensorPort);
        _running = true;
        _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        _recvThread.Start();
    }

    private void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, sensorPort);
        while (_running)
        {
            try
            {
                byte[] data = _udp.Receive(ref endpoint);
                string json = System.Text.Encoding.UTF8.GetString(data);
                var sample = JsonUtility.FromJson<SensorSample>(json);
                if (sample == null) continue;

                // 순서 뒤바뀐/오래된 패킷 드롭 (기획서 7 리스크: 레이스 컨디션 대비)
                if (sample.seq <= _lastSeq && _lastSeq - sample.seq < 1000) continue;
                _lastSeq = sample.seq;

                Samples.Enqueue(sample);
            }
            catch (SocketException) { /* shutting down */ }
            catch (Exception e) { Debug.LogWarning($"[SensorReceiver] recv error: {e.Message}"); }
        }
    }

    private void Update()
    {
        var sync = TimeSyncClient.Instance;
        while (Samples.TryPeek(out var sample))
        {
            if (!Samples.TryDequeue(out sample)) break;
            sample.pcTime = sync != null && sync.HasSynced ? sync.ToPcTime(sample.t) : sample.t;
            OnSample(sample);
        }
    }

    // TODO(Day 3): 여기서 모션 분류기로 넘기거나 이벤트로 발행.
    private void OnSample(SensorSample sample)
    {
        // Debug.Log($"[Sensor] t={sample.pcTime:F1} a=({sample.ax},{sample.ay},{sample.az})");
    }

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}

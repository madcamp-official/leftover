// 폰 쪽(phone-sensor 프로젝트)에 넣는 스크립트.
// PROTOCOL.md의 "1. 시간 동기화 (NTP 핑퐁)" — 폰은 PC의 ping에 즉시 pong으로 응답만 한다.
// Unity 프로젝트 생성 후 Assets/Scripts/Network/ 에 복사해서 사용.
//
// 씬에 빈 GameObject를 만들고 이 컴포넌트를 붙이면 끝. 별도 설정 불필요
// (어느 PC가 ping을 보내든 보낸 주소로 그대로 pong 회신).

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TimeSyncServer : MonoBehaviour
{
    public int syncPort = 9001;

    [Serializable]
    private class PingMsg
    {
        public string type;
        public int seq;
        public double t1;
    }

    [Serializable]
    private class PongMsg
    {
        public string type = "pong";
        public int seq;
        public double t1;
        public double t2;
        public double t3;
    }

    private UdpClient _udp;
    private Thread _recvThread;
    private volatile bool _running;

    private static double NowMs() =>
        (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

    private void Start()
    {
        _udp = new UdpClient(syncPort);
        _running = true;
        _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
        _recvThread.Start();
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                IPEndPoint remote = null;
                byte[] data = _udp.Receive(ref remote);
                double t2 = NowMs(); // 수신 즉시 타임스탬프 — 이후 처리 지연이 offset 오차로 들어감

                string json = System.Text.Encoding.UTF8.GetString(data);
                var ping = JsonUtility.FromJson<PingMsg>(json);
                if (ping == null || ping.type != "ping") continue;

                var pong = new PongMsg { seq = ping.seq, t1 = ping.t1, t2 = t2, t3 = NowMs() };
                string outJson = JsonUtility.ToJson(pong);
                byte[] outBytes = System.Text.Encoding.UTF8.GetBytes(outJson);
                _udp.Send(outBytes, outBytes.Length, remote);
            }
            catch (SocketException) { /* shutting down */ }
            catch (Exception e) { Debug.LogWarning($"[TimeSync] server error: {e.Message}"); }
        }
    }

    private void OnDestroy()
    {
        _running = false;
        _udp?.Close();
        _recvThread?.Join(200);
    }
}

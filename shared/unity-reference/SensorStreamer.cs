// 폰 쪽(phone-sensor 프로젝트)에 넣는 스크립트.
// PROTOCOL.md의 "2. 센서 스트리밍" 송신부. 가속도계+자이로를 UDP로 PC에 전송.
// Unity 프로젝트 생성 후 Assets/Scripts/Network/ 에 복사.
//
// 주의: Input.gyro는 기본 꺼져 있어 enableGyro = true로 켜야 함.
// Android는 Player Settings에서 Location/Sensors 관련 권한 체크 필요할 수 있음.

using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class SensorStreamer : MonoBehaviour
{
    [Header("Network")]
    public string pcIp = "192.168.0.1";
    public int sensorPort = 9000;

    [Header("Rate")]
    [Tooltip("초당 전송 샘플 수 (기획서 권장: 60~100Hz)")]
    public int sampleRateHz = 60;

    [Serializable]
    private class SensorSample
    {
        public int seq;
        public double t;
        public float ax, ay, az;
        public float gx, gy, gz;
    }

    private UdpClient _udp;
    private IPEndPoint _pcEndpoint;
    private int _seq;
    private float _sendInterval;
    private float _timer;

    private static double NowMs() =>
        (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

    private void Start()
    {
        Input.gyro.enabled = true;
        _udp = new UdpClient();
        _pcEndpoint = new IPEndPoint(IPAddress.Parse(pcIp), sensorPort);
        _sendInterval = 1f / Mathf.Max(1, sampleRateHz);
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < _sendInterval) return;
        _timer = 0f;

        Vector3 accel = Input.acceleration; // g 단위
        Vector3 gyro = Input.gyro.rotationRateUnbiased; // rad/s — PROTOCOL.md "결정 필요" 항목 참고

        var sample = new SensorSample
        {
            seq = _seq++,
            t = NowMs(),
            ax = accel.x, ay = accel.y, az = accel.z,
            gx = gyro.x, gy = gyro.y, gz = gyro.z,
        };

        string json = JsonUtility.ToJson(sample);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _udp.Send(bytes, bytes.Length, _pcEndpoint);
    }

    private void OnDestroy()
    {
        _udp?.Close();
    }
}

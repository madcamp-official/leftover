// 모든 미니게임이 참조하는 단일 창구. vision-server(Python, MediaPipe)가 UDP 9100으로
// 보내는 연속 포즈 스트림(shared/PROTOCOL.md 참고)을 PoseStreamReceiver가 받아서 여기에
// 채워 넣고, 미니게임은 UDP/JSON을 몰라도 이 클래스의 프로퍼티/메서드만으로 두 플레이어의
// 관절 위치와 표정 상태를 읽는다.
//
// 분업 경계: vision-server + PoseStreamReceiver + 이 파일이 "입력팀" 담당 영역이고,
// Assets/Scripts/Minigames/* 가 "게임팀" 담당 영역이다. 게임팀은 이 파일의 public API만
// 보고 작업하면 되고, vision-server가 아직 안 켜져 있어도(테스트용 가짜 데이터를 직접
// ApplyFrame에 넣어보는 식으로) 미니게임을 독립적으로 개발/테스트할 수 있다.

using System;
using UnityEngine;

public enum PlayerId { P1, P2 }

[Serializable]
public struct JointSample
{
    public Vector2 nose;
    public Vector2 leftShoulder, rightShoulder;
    public Vector2 leftElbow, rightElbow;
    public Vector2 leftWrist, rightWrist;
    public Vector2 leftHip, rightHip;
    public Vector2 leftKnee, rightKnee;
    public Vector2 leftAnkle, rightAnkle;

    public Vector2 ShoulderMid => (leftShoulder + rightShoulder) * 0.5f;
    public Vector2 HipMid => (leftHip + rightHip) * 0.5f;

    // 어깨-엉덩이 중점 거리 - 모든 상대 임계값(손 든 정도, 머리 기울기 등)의 정규화 기준.
    // 사람이 카메라에서 멀거나 가까워도, 키가 다르더라도 "몸통 길이 대비"로 비교하면
    // 일관된 임계값을 쓸 수 있다.
    public float TorsoLength => Vector2.Distance(ShoulderMid, HipMid);
}

public sealed class PlayerPoseState
{
    public bool IsTracked { get; internal set; }
    public JointSample Joints { get; internal set; }
    public float MouthOpenRatio { get; internal set; }
    public float EyeAspectRatio { get; internal set; } = 0.3f;
    internal float LastSeenAt;

    // --- 공용 제스처 판정 (전부 상태 없는 순간값 - 임계값은 게임마다 다를 수 있어 인자로 받음) ---

    public bool IsHandRaised(bool rightHand, float raiseRatio = 0.15f)
    {
        Vector2 wrist = rightHand ? Joints.rightWrist : Joints.leftWrist;
        Vector2 shoulder = rightHand ? Joints.rightShoulder : Joints.leftShoulder;
        float torso = Joints.TorsoLength;
        if (torso <= 0f) return false;
        // MediaPipe 이미지 좌표는 y가 아래로 증가하므로, 손목이 어깨보다 "위"에 있으려면
        // wrist.y가 shoulder.y보다 작아야 한다.
        return (shoulder.y - wrist.y) / torso > raiseRatio;
    }

    // 머리 기울기: 코 x좌표와 엉덩이 중심 x좌표의 차이 (구 버전 검투 게임에서 실측 검증된
    // 방식 그대로 - 어깨 기준보다 지렛대가 길어 작은 움직임도 크게 잡힌다).
    // 양수를 반환하면 오른쪽으로, 음수면 왼쪽으로 기운 것 (거울모드라 화면상 방향 = 실제 방향).
    public float HeadTiltRatio()
    {
        float torso = Joints.TorsoLength;
        if (torso <= 0f) return 0f;
        return (Joints.nose.x - Joints.HipMid.x) / torso;
    }

    public bool IsMouthOpen(float threshold = 0.15f) => MouthOpenRatio > threshold;

    // EAR이 낮을수록 눈을 감은 것 (뜬 눈은 보통 0.25~0.3). 순간값만 보면 자연스러운 깜빡임도
    // "감음"으로 잡히니, 지속시간까지 확인해야 하는 게임은 EyeCloseTimer를 같이 쓸 것.
    public bool IsEyeClosedNow(float threshold = 0.18f) => EyeAspectRatio < threshold;

    // 코코넛 깨기용 - 양손 중점과 머리(코) 사이 거리, 몸통 길이로 정규화.
    public float HandToHeadDistance()
    {
        float torso = Joints.TorsoLength;
        if (torso <= 0f) return float.MaxValue;
        Vector2 handMid = (Joints.leftWrist + Joints.rightWrist) * 0.5f;
        return Vector2.Distance(handMid, Joints.nose) / torso;
    }
}

public sealed class PoseInputHub : MonoBehaviour
{
    public static PoseInputHub Instance { get; private set; }

    // 이 시간 이상 새 프레임이 안 오면 "화면에서 사라짐"으로 취급 (PROTOCOL.md 권장값).
    public float staleTimeout = 0.5f;

    public PlayerPoseState P1 { get; } = new PlayerPoseState();
    public PlayerPoseState P2 { get; } = new PlayerPoseState();

    public PlayerPoseState Get(PlayerId id) => id == PlayerId.P1 ? P1 : P2;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        if (P1.IsTracked && now - P1.LastSeenAt > staleTimeout) P1.IsTracked = false;
        if (P2.IsTracked && now - P2.LastSeenAt > staleTimeout) P2.IsTracked = false;
    }

    // PoseStreamReceiver가 메인 스레드에서 프레임마다 호출한다. 테스트 코드에서 vision-server
    // 없이 미니게임을 검증하고 싶을 때도 이 메서드를 직접 호출해서 가짜 데이터를 넣으면 된다.
    public void ApplyFrame(FramePayload frame)
    {
        if (frame?.players == null) return;
        foreach (PlayerFrameData player in frame.players)
        {
            PlayerPoseState state = player.id == "p1" ? P1 : player.id == "p2" ? P2 : null;
            if (state == null) continue;

            state.IsTracked = true;
            state.LastSeenAt = Time.unscaledTime;
            state.MouthOpenRatio = player.face.mouthOpenRatio;
            state.EyeAspectRatio = player.face.eyeAspectRatio;
            state.Joints = new JointSample
            {
                nose = player.pose.nose.ToVector2(),
                leftShoulder = player.pose.leftShoulder.ToVector2(),
                rightShoulder = player.pose.rightShoulder.ToVector2(),
                leftElbow = player.pose.leftElbow.ToVector2(),
                rightElbow = player.pose.rightElbow.ToVector2(),
                leftWrist = player.pose.leftWrist.ToVector2(),
                rightWrist = player.pose.rightWrist.ToVector2(),
                leftHip = player.pose.leftHip.ToVector2(),
                rightHip = player.pose.rightHip.ToVector2(),
                leftKnee = player.pose.leftKnee.ToVector2(),
                rightKnee = player.pose.rightKnee.ToVector2(),
                leftAnkle = player.pose.leftAnkle.ToVector2(),
                rightAnkle = player.pose.rightAnkle.ToVector2(),
            };
        }
    }

    // MediaPipe 이미지 좌표(x:0~1 왼쪽->오른쪽, y:0~1 위->아래)를 Unity 뷰포트 좌표(0~1,
    // 원점 좌하단)로 변환 - y축이 반대라 뒤집어야 한다. 미니게임에서
    // `camera.ViewportToWorldPoint(new Vector3(v.x, v.y, depth))`로 이어서 쓰면 된다.
    public static Vector2 ImageToViewport(Vector2 imageXY) => new Vector2(imageXY.x, 1f - imageXY.y);
}

// --- UDP로 오는 JSON과 1:1로 대응하는 직렬화 DTO (JsonUtility용, 전부 [Serializable]) ---
// shared/PROTOCOL.md의 스키마와 필드명이 정확히 같아야 한다 - 여기 이름을 바꾸려면
// PROTOCOL.md와 vision-server/main.py도 같이 바꿀 것.

[Serializable]
public struct Vec2Data
{
    public float x;
    public float y;
    public Vector2 ToVector2() => new Vector2(x, y);
}

[Serializable]
public class PoseData
{
    public Vec2Data nose;
    public Vec2Data leftShoulder, rightShoulder;
    public Vec2Data leftElbow, rightElbow;
    public Vec2Data leftWrist, rightWrist;
    public Vec2Data leftHip, rightHip;
    public Vec2Data leftKnee, rightKnee;
    public Vec2Data leftAnkle, rightAnkle;
}

[Serializable]
public class FaceData
{
    public float mouthOpenRatio;
    public float eyeAspectRatio;
}

[Serializable]
public class PlayerFrameData
{
    public string id;
    public PoseData pose;
    public FaceData face;
}

[Serializable]
public class FramePayload
{
    public double t;
    public PlayerFrameData[] players;
}

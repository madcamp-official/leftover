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

    // 소리지르기(ScreamDuel) 전용 - 0(무음)~1(최대 음량)로 정규화된 마이크 순간 음량.
    // pose/face와 별도로 마이크만 끊길 수 있어(예: 마이크만 음소거) IsTracked와 분리된
    // 자체 스테일 타임아웃을 둔다 - PROTOCOL.md "계획 중: 마이크 음량(voice) 필드" 참고.
    public float VoiceLevel { get; internal set; }
    public bool IsVoiceTracked { get; internal set; }
    internal float LastVoiceSeenAt;

    // 게임 사이 로딩 화면에서 수집하는 공통 차렷 자세 기준값.
    public bool IsCalibrated { get; internal set; }
    public float CalibrationProgress { get; internal set; }
    public float BaselineHipY { get; internal set; }
    internal float CalibrationTimer;

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

    // --- 로딩 화면 캘리브레이션용 - 다음 게임이 실제로 쓰는 부위가 카메라 프레임 안에
    // 들어와 있는지 확인. 몸통(엉덩이/어깨)이 잡혀도 손이 프레임 가장자리에 걸려 있으면
    // 손 들기 판정 자체가 불안정하므로, "트래킹됨"과는 별개로 이 값도 확인해야 한다. ---

    public bool AreHandsVisible(float margin = 0.06f) =>
        IsInFrame(Joints.leftWrist, margin) && IsInFrame(Joints.rightWrist, margin);

    public bool IsFaceVisible(float margin = 0.1f) => IsInFrame(Joints.nose, margin);

    private static bool IsInFrame(Vector2 point, float margin) =>
        point.x >= margin && point.x <= 1f - margin && point.y >= margin && point.y <= 1f - margin;
}

public sealed class PoseInputHub : MonoBehaviour
{
    private static PoseInputHub _instance;

    // 플레이 중 스크립트를 고쳐 Unity가 도메인 리로드를 하면(기본 동작인 "Recompile And
    // Continue Playing") 이미 존재하던 오브젝트의 Awake()는 다시 호출되지 않는데 static
    // 필드는 초기화돼버려서 Instance가 null이 된다 - 실제 오브젝트는 씬에 멀쩡히 살아있는데
    // 참조만 끊기는 것. PoseStreamReceiver/EyeCloseTimer가 매 프레임 `Instance == null`로
    // 조용히 return해버려서 겉으로는 아무 에러 없이 포즈 입력만 죽은 것처럼 보였다(실측
    // 확인됨). 그래서 null이면 씬에서 다시 찾아 복구한다.
    public static PoseInputHub Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<PoseInputHub>();
            return _instance;
        }
        private set => _instance = value;
    }

    // 이 시간 이상 새 프레임이 안 오면 "화면에서 사라짐"으로 취급 (PROTOCOL.md 권장값).
    public float staleTimeout = 0.5f;
    public float calibrationSeconds = 1.5f;

    public PlayerPoseState P1 { get; } = new PlayerPoseState();
    public PlayerPoseState P2 { get; } = new PlayerPoseState();
    private bool _calibrationActive;

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
        if (P1.IsVoiceTracked && now - P1.LastVoiceSeenAt > staleTimeout) { P1.IsVoiceTracked = false; P1.VoiceLevel = 0f; }
        if (P2.IsVoiceTracked && now - P2.LastVoiceSeenAt > staleTimeout) { P2.IsVoiceTracked = false; P2.VoiceLevel = 0f; }
        if (_calibrationActive)
        {
            UpdateCalibration(P1);
            UpdateCalibration(P2);
            if (P1.IsCalibrated && P2.IsCalibrated) _calibrationActive = false;
        }
    }

    public void BeginCalibration()
    {
        ResetCalibration(P1);
        ResetCalibration(P2);
        _calibrationActive = true;
    }

    public bool AreBothCalibrated => P1.IsCalibrated && P2.IsCalibrated;

    private static void ResetCalibration(PlayerPoseState state)
    {
        state.IsCalibrated = false;
        state.CalibrationProgress = 0f;
        state.CalibrationTimer = 0f;
        state.BaselineHipY = 0f;
    }

    private void UpdateCalibration(PlayerPoseState state)
    {
        if (state.IsCalibrated) return;
        float torso = state.Joints.TorsoLength;
        if (!state.IsTracked || torso <= 0.04f)
        {
            state.CalibrationTimer = Mathf.Max(0f, state.CalibrationTimer - Time.unscaledDeltaTime * 2f);
            state.CalibrationProgress = calibrationSeconds <= 0f
                ? 0f
                : Mathf.Clamp01(state.CalibrationTimer / calibrationSeconds);
            return;
        }

        float hipY = state.Joints.HipMid.y;
        state.BaselineHipY = state.CalibrationTimer <= 0f
            ? hipY
            : Mathf.Lerp(state.BaselineHipY, hipY, 0.12f);
        state.CalibrationTimer += Time.unscaledDeltaTime;
        state.CalibrationProgress = calibrationSeconds <= 0f
            ? 1f
            : Mathf.Clamp01(state.CalibrationTimer / calibrationSeconds);
        if (state.CalibrationProgress >= 1f) state.IsCalibrated = true;
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
            // voice는 아직 이 값을 보내는 vision-server(소리지르기용)에서만 온다 - 다른
            // 게임들은 이 필드 없이 프레임을 보내므로 null일 수 있다(JsonUtility가 없는
            // 필드는 채우지 않고 기본값 null로 둠).
            if (player.voice != null)
            {
                state.VoiceLevel = player.voice.level;
                state.IsVoiceTracked = true;
                state.LastVoiceSeenAt = Time.unscaledTime;
            }
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

// 소리지르기(ScreamDuel) 전용, 선택 필드 - 이 필드를 보내는 vision-server(--voice)만 채운다.
[Serializable]
public class VoiceData
{
    public float level;
}

[Serializable]
public class PlayerFrameData
{
    public string id;
    public PoseData pose;
    public FaceData face;
    public VoiceData voice;
}

[Serializable]
public class FramePayload
{
    public double t;
    public PlayerFrameData[] players;
}

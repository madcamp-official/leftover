// 테스트 전용 디버그 컴포넌트 - vision-server(카메라) 없이 미니게임을 확인하기 위해
// Inspector 필드를 직접 조작하면(또는 MCP로 원격 조작) 매 프레임 합성 포즈를 만들어
// PoseInputHub에 흘려보낸다. GETTING_STARTED.md에서 언급한 "PoseInputHub.Instance.
// ApplyFrame(...)을 호출하는 테스트용 디버그 스크립트"에 해당한다.
//
// 실제 빌드/플레이에는 필요 없다 - 테스트가 끝나면 씬에서 이 GameObject를 지울 것.
using UnityEngine;

public class PoseSimulator : MonoBehaviour
{
    [Header("P1")]
    public bool p1Tracked = true;
    public bool p1RightHandRaised;
    public bool p1LeftHandRaised;
    [Range(-1f, 1f)] public float p1HeadTilt; // -1=왼쪽, 1=오른쪽
    public bool p1MouthOpen;
    public bool p1EyeClosed;
    public bool p1HandsAtHead; // 코코넛 깨기용 - 양손을 머리 쪽으로 모음
    [Range(0f, 1f)] public float p1JumpRaise; // 엉덩이 중점이 몸통 길이 대비 얼마나 뜨는지

    [Header("P2")]
    public bool p2Tracked = true;
    public bool p2RightHandRaised;
    public bool p2LeftHandRaised;
    [Range(-1f, 1f)] public float p2HeadTilt;
    public bool p2MouthOpen;
    public bool p2EyeClosed;
    public bool p2HandsAtHead;
    [Range(0f, 1f)] public float p2JumpRaise;

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        if (hub == null) return;

        Apply(hub.P1, p1Tracked, p1RightHandRaised, p1LeftHandRaised, p1HeadTilt, p1MouthOpen, p1EyeClosed, p1HandsAtHead, p1JumpRaise);
        Apply(hub.P2, p2Tracked, p2RightHandRaised, p2LeftHandRaised, p2HeadTilt, p2MouthOpen, p2EyeClosed, p2HandsAtHead, p2JumpRaise);
    }

    // torso(몸통 길이) 기준 0.32 정도가 나오도록 잡은 정지 자세 좌표(MediaPipe 이미지 좌표계:
    // x 0~1 왼->오, y 0~1 위->아래) 위에, 각 제스처 토글에 따라 관절을 옮긴다.
    private static void Apply(PlayerPoseState state, bool tracked, bool rightRaised, bool leftRaised,
        float headTilt, bool mouthOpen, bool eyeClosed, bool handsAtHead, float jumpRaise)
    {
        if (!tracked)
        {
            state.IsTracked = false;
            return;
        }

        const float lift = 0.32f; // 대략적인 몸통 길이 - jumpRaise=1이면 이만큼 위로(y 감소)
        float riseY = jumpRaise * lift;

        float noseY = 0.20f - riseY;
        float shoulderY = 0.30f - riseY;
        float hipY = 0.62f - riseY;
        float kneeY = 0.80f - riseY;
        float ankleY = 0.97f - riseY;
        float noseX = 0.5f + Mathf.Clamp(headTilt, -1f, 1f) * 0.10f;

        Vector2 leftWrist = leftRaised ? new Vector2(0.38f, 0.15f - riseY) : new Vector2(0.38f, 0.60f - riseY);
        Vector2 rightWrist = rightRaised ? new Vector2(0.62f, 0.15f - riseY) : new Vector2(0.62f, 0.60f - riseY);
        if (handsAtHead)
        {
            leftWrist = new Vector2(0.47f, noseY + 0.02f);
            rightWrist = new Vector2(0.53f, noseY + 0.02f);
        }

        state.IsTracked = true;
        state.LastSeenAt = Time.unscaledTime;
        state.MouthOpenRatio = mouthOpen ? 0.35f : 0.0f;
        state.EyeAspectRatio = eyeClosed ? 0.05f : 0.30f;
        state.Joints = new JointSample
        {
            nose = new Vector2(noseX, noseY),
            leftShoulder = new Vector2(0.42f, shoulderY),
            rightShoulder = new Vector2(0.58f, shoulderY),
            leftElbow = new Vector2(0.40f, shoulderY + 0.15f),
            rightElbow = new Vector2(0.60f, shoulderY + 0.15f),
            leftWrist = leftWrist,
            rightWrist = rightWrist,
            leftHip = new Vector2(0.44f, hipY),
            rightHip = new Vector2(0.56f, hipY),
            leftKnee = new Vector2(0.44f, kneeY),
            rightKnee = new Vector2(0.56f, kneeY),
            leftAnkle = new Vector2(0.44f, ankleY),
            rightAnkle = new Vector2(0.56f, ankleY),
        };
    }
}

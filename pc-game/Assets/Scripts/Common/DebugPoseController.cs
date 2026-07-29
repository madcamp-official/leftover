// vision-server(카메라) 없이 키보드만으로 PoseInputHub에 가짜 프레임을 채워 넣는 테스트용
// 컨트롤러. 문서(GETTING_STARTED류)에서 이미 권장하던 방식 그대로 -
// "PoseInputHub.Instance.ApplyFrame(...)을 직접 호출해서 가짜 데이터를 넣으면 미니게임
// 로직을 독립적으로 테스트할 수 있다" - 를 코드로 만든 것뿐이다.
//
// 아무 씬(빈 GameObject 하나)에 이 컴포넌트를 붙이고 Play하면, 매 프레임 P1/P2 둘 다
// "트래킹됨" 상태로 채워진다 - 실제 vision-server를 켤 필요가 없다. GameBootstrap이
// 자동으로 추가해주지는 않는다(테스트 전용이라 의도적으로 수동 추가해야 함) - 실제
// 카메라로 테스트할 씬에는 이 오브젝트를 지우거나 비활성화할 것.
//
// 키 배정 (P1 = 방향키/Space/Shift, P2 = WASD/Ctrl/Alt):
//   ←/→ (A/D)     : 왼손/오른손 들기 (돌던지기, 돌바나나)
//   ↑ (W) 누르고 있기 : 점프 높이가 서서히 올라감, 떼면 서서히 내려감 (과일따기)
//   ↓ (S) 누르고 있기 : 양손이 머리로 가까워짐 (코코넛 깨기)
//   Space (LeftCtrl)  : 입 벌림 (과일따기, 돌바나나)
//   LeftShift (LeftAlt): 눈 감음 (눈빛싸움)
// 자세따라하기(포즈 스냅샷 비교)는 키보드로 흉내 내기 어려워서 지원하지 않는다.
using UnityEngine;
using UnityEngine.InputSystem;

public class DebugPoseController : MonoBehaviour
{
    [Header("점프/코코넛처럼 눌렀다 떼는 값이 서서히 변하는 속도")]
    public float jumpApproachSpeed = 3f;
    public float handCloseApproachSpeed = 5f;

    private float _p1Jump, _p2Jump;           // 0(땅)~1(최고 높이)
    private float _p1HandClose, _p2HandClose; // 0(벌림)~1(머리에 붙음)

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        Keyboard keyboard = Keyboard.current;
        // 프로젝트가 Player Settings에서 Active Input Handling을 "Input System Package"로
        // 쓰고 있어서 레거시 UnityEngine.Input.GetKey*는 예외를 던진다 - 새 Input System의
        // Keyboard.current를 써야 한다.
        if (hub == null || keyboard == null) return;

        _p1Jump = Approach(_p1Jump, keyboard.upArrowKey.isPressed ? 1f : 0f, jumpApproachSpeed);
        _p2Jump = Approach(_p2Jump, keyboard.wKey.isPressed ? 1f : 0f, jumpApproachSpeed);
        _p1HandClose = Approach(_p1HandClose, keyboard.downArrowKey.isPressed ? 1f : 0f, handCloseApproachSpeed);
        _p2HandClose = Approach(_p2HandClose, keyboard.sKey.isPressed ? 1f : 0f, handCloseApproachSpeed);

        var frame = new FramePayload
        {
            t = Time.unscaledTimeAsDouble,
            players = new[]
            {
                BuildPlayer("p1", _p1Jump, _p1HandClose,
                    leftHand: keyboard.leftArrowKey.isPressed, rightHand: keyboard.rightArrowKey.isPressed,
                    mouthOpen: keyboard.spaceKey.isPressed, eyeClosed: keyboard.leftShiftKey.isPressed),
                BuildPlayer("p2", _p2Jump, _p2HandClose,
                    leftHand: keyboard.aKey.isPressed, rightHand: keyboard.dKey.isPressed,
                    mouthOpen: keyboard.leftCtrlKey.isPressed, eyeClosed: keyboard.leftAltKey.isPressed),
            },
        };
        hub.ApplyFrame(frame);
    }

    private static float Approach(float current, float target, float speed)
        => Mathf.MoveTowards(current, target, speed * Time.deltaTime);

    // 서 있는 자세를 기준으로, jump만큼 몸 전체를 위로(이미지 y는 아래로 증가하므로 값을
    // 줄이는 방향으로) 옮기고, handClose만큼 양 손목을 코 쪽으로 당긴다. 손을 들었으면
    // (leftHand/rightHand) 그 손목만 어깨보다 위로 올린다 - handClose가 0보다 크면 그 위에
    // 덮어써서 코코넛 시뮬레이션이 우선한다.
    private static PlayerFrameData BuildPlayer(string id, float jump, float handClose,
        bool leftHand, bool rightHand, bool mouthOpen, bool eyeClosed)
    {
        float lift = jump * 0.25f;
        float noseY = 0.22f - lift;
        float shoulderY = 0.35f - lift;
        float hipY = 0.62f - lift;
        float ankleY = 0.97f - lift;

        Vector2 nose = new Vector2(0.34f, noseY);
        Vector2 leftWrist = leftHand ? new Vector2(0.28f, shoulderY - 0.15f) : new Vector2(0.20f, hipY);
        Vector2 rightWrist = rightHand ? new Vector2(0.40f, shoulderY - 0.15f) : new Vector2(0.50f, hipY);
        if (handClose > 0f)
        {
            leftWrist = Vector2.Lerp(leftWrist, nose, handClose);
            rightWrist = Vector2.Lerp(rightWrist, nose, handClose);
        }

        return new PlayerFrameData
        {
            id = id,
            pose = new PoseData
            {
                nose = ToVec2(nose),
                leftShoulder = new Vec2Data { x = 0.28f, y = shoulderY },
                rightShoulder = new Vec2Data { x = 0.40f, y = shoulderY },
                leftElbow = new Vec2Data { x = 0.22f, y = shoulderY + 0.13f },
                rightElbow = new Vec2Data { x = 0.46f, y = shoulderY + 0.13f },
                leftWrist = ToVec2(leftWrist),
                rightWrist = ToVec2(rightWrist),
                leftHip = new Vec2Data { x = 0.30f, y = hipY },
                rightHip = new Vec2Data { x = 0.38f, y = hipY },
                leftKnee = new Vec2Data { x = 0.29f, y = hipY + 0.18f },
                rightKnee = new Vec2Data { x = 0.39f, y = hipY + 0.18f },
                leftAnkle = new Vec2Data { x = 0.28f, y = ankleY },
                rightAnkle = new Vec2Data { x = 0.40f, y = ankleY },
            },
            face = new FaceData
            {
                mouthOpenRatio = mouthOpen ? 0.3f : 0.02f,
                eyeAspectRatio = eyeClosed ? 0.05f : 0.3f,
            },
        };
    }

    private static Vec2Data ToVec2(Vector2 v) => new Vec2Data { x = v.x, y = v.y };
}

// 혼자하기(솔로) 모드 전용 - 실제 카메라 없이 P2 입력을 자동으로 채워 넣는 간단한 봇.
// DebugPoseController와 같은 "PoseInputHub.ApplyFrame에 가짜 프레임을 직접 넣는다" 방식을
// 쓰되, 키보드 대신 제스처별로 독립된 랜덤 on/off 타이머(Urge)로 알아서 움직인다.
//
// PoseInputHub.ApplyFrame은 프레임에 들어있는 id만 갱신하므로(온라인 모드에서 두
// vision-server 스트림이 자연스럽게 합쳐지는 것과 같은 원리), 이 컴포넌트는 "p2"만 담긴
// 프레임을 계속 보내면 되고 실제 카메라가 보내는 P1 데이터는 건드리지 않는다.
//
// Hub에서 "혼자하기"를 누르면 SetEnabled(true)로 생성되고, 매치 내내(씬 전환 중에도)
// 살아남아야 하므로 DontDestroyOnLoad 싱글턴이다.
using UnityEngine;

public sealed class SoloBotController : MonoBehaviour
{
    private static SoloBotController _instance;
    public static bool IsEnabled => _instance != null;

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_instance != null) return;
            var go = new GameObject("SoloBotController");
            _instance = go.AddComponent<SoloBotController>();
        }
        else if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    // 제스처마다 독립적으로 "쉬다가 - 한다 - 다시 쉰다"를 반복하는 랜덤 타이머.
    private sealed class Urge
    {
        private readonly float _minOff, _maxOff, _minOn, _maxOn;
        private float _timer;
        public bool Active { get; private set; }

        public Urge(float minOff, float maxOff, float minOn, float maxOn)
        {
            _minOff = minOff; _maxOff = maxOff; _minOn = minOn; _maxOn = maxOn;
            _timer = Random.Range(minOff, maxOff);
        }

        public void Tick(float dt)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            Active = !Active;
            _timer = Active ? Random.Range(_minOn, _maxOn) : Random.Range(_minOff, _maxOff);
        }
    }

    private Urge _jump;
    private Urge _handClose;   // 코코넛깨기 - 양손을 머리로 모음
    private Urge _leftHand;
    private Urge _rightHand;
    private Urge _mouthOpen;   // 돌바나나 - 바나나 먹기
    private Urge _eyeClosed;   // 눈빛싸움 - 짧고 드물게만 감아야 항상 지지 않음
    private Urge _voice;       // 소리지르기

    // 연속값(점프 높이, 손모음 정도, 음량)은 Urge on/off를 목표값으로 삼아 서서히 접근시킨다 -
    // DebugPoseController의 Approach(MoveTowards)와 같은 방식.
    private float _jumpAmount, _handCloseAmount, _voiceAmount;
    private float _headTiltPhase;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        _jump = new Urge(1.2f, 2.6f, 0.4f, 0.9f);
        _handClose = new Urge(1.0f, 2.2f, 0.3f, 0.7f);
        _leftHand = new Urge(1.5f, 3.5f, 0.4f, 1.0f);
        _rightHand = new Urge(1.5f, 3.5f, 0.4f, 1.0f);
        _mouthOpen = new Urge(2.0f, 4.0f, 0.3f, 0.8f);
        _eyeClosed = new Urge(3.0f, 6.0f, 0.15f, 0.4f);
        _voice = new Urge(0.8f, 2.0f, 0.5f, 1.5f);
        _headTiltPhase = Random.Range(0f, 10f);
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        if (hub == null) return;

        float dt = Time.deltaTime;
        _jump.Tick(dt);
        _handClose.Tick(dt);
        _leftHand.Tick(dt);
        _rightHand.Tick(dt);
        _mouthOpen.Tick(dt);
        _eyeClosed.Tick(dt);
        _voice.Tick(dt);

        _jumpAmount = Mathf.MoveTowards(_jumpAmount, _jump.Active ? 1f : 0f, 3f * dt);
        _handCloseAmount = Mathf.MoveTowards(_handCloseAmount, _handClose.Active ? 1f : 0f, 5f * dt);
        _voiceAmount = Mathf.MoveTowards(_voiceAmount, _voice.Active ? 1f : 0f, 4f * dt);
        _headTiltPhase += dt;

        var frame = new FramePayload
        {
            t = Time.unscaledTimeAsDouble,
            players = new[] { BuildP2Frame() },
        };
        hub.ApplyFrame(frame);
    }

    // DebugPoseController.BuildPlayer와 같은 기준 자세 + 오프셋 조합. 서 있는 자세에서
    // jump만큼 몸 전체를 위로(이미지 y는 아래로 증가하므로 값을 줄이는 방향), handClose만큼
    // 양 손목을 코 쪽으로 당기고, 손을 들었으면 그 손목만 어깨보다 위로 올린다. 눈빛싸움용
    // 머리 기울기는 느린 사인파로 자연스럽게 좌우로 흔든다.
    private PlayerFrameData BuildP2Frame()
    {
        float lift = _jumpAmount * 0.25f;
        float noseY = 0.22f - lift;
        float shoulderY = 0.35f - lift;
        float hipY = 0.62f - lift;
        float ankleY = 0.97f - lift;
        float noseX = 0.34f + Mathf.Sin(_headTiltPhase * 0.35f) * 0.05f;

        Vector2 nose = new Vector2(noseX, noseY);
        Vector2 leftWrist = _leftHand.Active ? new Vector2(0.28f, shoulderY - 0.15f) : new Vector2(0.20f, hipY);
        Vector2 rightWrist = _rightHand.Active ? new Vector2(0.40f, shoulderY - 0.15f) : new Vector2(0.50f, hipY);
        if (_handCloseAmount > 0f)
        {
            leftWrist = Vector2.Lerp(leftWrist, nose, _handCloseAmount);
            rightWrist = Vector2.Lerp(rightWrist, nose, _handCloseAmount);
        }

        return new PlayerFrameData
        {
            id = "p2",
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
                mouthOpenRatio = _mouthOpen.Active ? 0.3f : 0.02f,
                eyeAspectRatio = _eyeClosed.Active ? 0.05f : 0.3f,
            },
            voice = new VoiceData { level = _voiceAmount },
        };
    }

    private static Vec2Data ToVec2(Vector2 v) => new Vec2Data { x = v.x, y = v.y };

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}

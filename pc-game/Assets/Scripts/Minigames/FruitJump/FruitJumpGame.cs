// 점프해서 과일따기 - 우가우가게임_기획_프롬프트.md "3. 점프해서 과일따기" 스펙.
//
// 규칙: 각자 독립된 나무(상대와 자원 경쟁 없음). JumpHeightCalibrator.GetJumpHeight()(엉덩이
// 중점 기반)로 점프 중 도달한 최고 높이(정점, PeakHeight)를 계속 갱신한다. 판정 자체는
// 실시간이 아니라 착지한 순간 한 번만 한다: 이번 점프의 최종 정점 높이가 어느 단
// (tierHeightThresholds)을 넘었는지 보고 그 단 하나만 채점한다.
//
// 원래는 정점에서 입이 벌어져 있어야 채점되는 조건이 있었으나, 카메라에서 멀리 떨어질수록
// 입 벌림(IsMouthOpen) 인식이 불안정해져서 이 조건은 뺐다 - 정점 높이만으로 단이 결정되면
// 곧바로 채점된다.
//
// 채점된 과일은 화면에서 사라졌다가 다음 점프를 시작하는 순간(이륙) 다시 나타난다.
//
// 화면은 image/games/fruit_jump/의 실제 아트(나무가 그려진 배경 + 점수 네임플레이트)로
// 구성한다. 낮은/중간/높은 단은 각각 사과/포도/파인애플 실제 소품으로 표시한다.
//
// 캐릭터 동작: 공중에 떠있는 동안은 실시간 점프 높이를 캐릭터에 반영하지 않는다(화면은
// 가만히 있음) - 점프 높이는 오직 단 판정에만 쓰인다. 착지해서 단이 확정되는 그 순간에만
// PlayScoreBounce가 캐릭터를 그 단 과일의 실제 높이까지 튀어올렸다 내려놓는 연출을
// 재생한다(TickTree 참고).
//
// 캐릭터 표현: 관절 리깅(CavemanSilhouette/Caveman_P1·P2 프리팹) 대신 jump_front_1~4
// 프레임 시퀀스를 통째로 교체한다(FrameAnimatedCharacter). 리깅 프리팹이 더 이상 필요
// 없어 씬에서 제거했으므로, 이 스크립트는 캐릭터를 놓을 빈 Transform 하나(p1Anchor/
// p2Anchor)만 있으면 된다.
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class FruitJumpGame : MonoBehaviour
{
    public float matchSeconds = 30f;
    // 낮은 단/중간 단/높은 단 순서. vision-server main.py로 실측한 값 (몸통 길이 대비 비율).
    public float[] tierHeightThresholds = { 0.2f, 0.4f, 0.75f };
    public int[] tierScores = { 1, 3, 5 };
    public float resultDisplaySeconds = 2f;
    public float fruitWidth = 0.4f;
    public float fruitEatSeconds = 0.2f; // 채점된 과일이 사라지는 데 걸리는 시간(연출용)
    // PlayScoreBounce의 기본 재생 시간 - 디버그 키(A/B/C)처럼 실제 점프 시간을 잴 수 없을 때만
    // 이 값을 그대로 쓴다. 실제 착지 판정에서는 대신 이번 점프가 실제로 공중에 떠 있던
    // 시간(이륙~착지)을 측정해서 그 길이로 재생한다(아래 minScoreBounceSeconds/
    // maxScoreBounceSeconds 사이로 clamp) - 낮게 살짝 뛴 점프는 짧게, 오래 머문 높은 점프는
    // 길게 튀어올랐다 내려오게 해서 실제 점프 리듬과 맞춘다.
    public float scoreBounceSeconds = 0.35f;
    public float minScoreBounceSeconds = 0.2f; // 너무 짧아서 안 보이는 걸 방지
    public float maxScoreBounceSeconds = 1.2f; // 너무 오래 붕 떠 있는 걸 방지
    // 상승/하강 이징 곡선의 거듭제곱 - 클수록 시작/정점 부근(EaseInOut) 또는 정점/착지
    // 부근(EaseOutIn)에서 더 느려진다. 2 = 기본 quad ease, 3 = cubic(더 느리게), 4 = quartic.
    public float bounceEasePower = 3f;
    public float characterFrameWidth = 1.2f; // jump_N 프레임 캐릭터의 표시 폭(월드 유닛) - 기존 리깅과 크기가 맞게 에디터에서 눈으로 보고 조정할 것

    // 테스트용 임시 스위치: true면 P2 캘리브레이션 여부와 무관하게 P1만 캘리브레이션되면
    // 바로 진행한다(P2용 카메라/입력 없이 P1만으로 확인할 때). 실제 2인 플레이 전에는
    // false로 되돌릴 것.
    public bool debugP1OnlyTest = true;

    // 테스트용 임시 키: 캘리브레이션/실제 점프와 무관하게 점수+연출 경로를 바로 태워본다
    // (PlayScoreBounce 애니메이션 자체만 확인하고 싶을 때 - 캘리브레이션이 불안정하면 실제
    // 점프로는 어떤 tier가 확정될지 신뢰하기 어렵다). P1: A=1티어(사과) B=2티어(포도)
    // C=3티어(파인애플). P2: 숫자키 1/2/3 = 1/2/3티어. 실제 플레이 전에는 false로 되돌릴 것.
    public bool debugTierKeys = true;

    // jump_front_1(대기 자세) 원본 PNG는 600x900 캔버스에서 발이 바닥까지 닿지 않고 약 50px의
    // 투명 여백을 남겨둔 채 그려져 있다(양쪽 캐릭터 공통). FrameAnimatedCharacter는 캔버스
    // 하단을 곧 "발이 서 있는 자리"로 가정하므로 보정 없이 그대로 쓰면 그 여백만큼 붕 떠
    // 보인다 - characterFrameWidth 비율로 환산해 아래로 그만큼 당겨서 바닥에 붙인다.
    private const float CharacterBottomPaddingRatio = 50f / 600f;

    // jump_front 원본 캔버스가 600x900이라 세로/가로 비율은 항상 900/600 = 1.5 - 이걸로
    // characterFrameWidth 기준 캐릭터의 대략적인 렌더 높이를 구한다(PlayScoreBounce에서
    // "발이 과일 높이까지" 대신 "몸통 중앙이 과일 높이까지" 오도록 절반만큼 빼는 데 씀).
    private const float CharacterCanvasAspect = 900f / 600f;

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private Transform p1Anchor; // 캐릭터가 서는 자리(바닥) - 빈 GameObject면 충분
    [SerializeField] private Transform p2Anchor;
    [SerializeField] private JumpHeightCalibrator p1Jump;
    [SerializeField] private JumpHeightCalibrator p2Jump;
    [SerializeField] private SpriteRenderer[] p1Fruits;
    [SerializeField] private SpriteRenderer[] p2Fruits;
    [SerializeField] private FruitJumpHud hud;

    private class TreeState
    {
        public PlayerId Player;
        public JumpHeightCalibrator Jump;
        public Transform Anchor;
        public Vector3 BasePosition;
        public SpriteRenderer[] Fruits;
        public Vector3[] FruitBaseScales;
        public bool WasAirborne;
        public float PeakHeight;
        public float AirborneStartTime; // 이번 점프가 이륙한 Time.time - 착지 때 실제 공중 시간을 재는 데 씀
        public bool ScoreBounceActive; // PlayScoreBounce 재생 중 표시(겹쳐 재생되는 것만 방지)
        public FrameAnimatedCharacter Anim;
    }

    private TreeState _p1Tree;
    private TreeState _p2Tree;
    private float _elapsed;
    private int _p1Score;
    private int _p2Score;
    private bool _ended;

    // --- 아래부터 호스트-클라이언트 배선 (docs/멀티플레이_분산_아키텍처_설계.md 4장) ---
    // 판정/타이밍/수치는 전부 위 필드・메서드 그대로다. 호스트(또는 오프라인)는 원래 로직을
    // 한 글자도 안 바꾸고 그대로 실행하면서, 상태가 바뀌는 지점(RespawnFruits/ScoreTier/
    // EndMatch)에서만 이벤트를 추가로 내보낸다. 클라이언트는 그 판정 로직(TickTree,
    // HandleDebugTierKeys, 캘리브레이션 대기)을 아예 실행하지 않고, 받은 이벤트로 같은
    // RespawnFruits/ScoreTier 함수를 그대로 호출해 동일한 연출을 재생한다.
    private bool _startedBroadcast;
    private bool _clientStarted;
    private float _clientElapsed;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();

        _p1Tree = BuildTree(PlayerId.P1, p1Anchor, p1Jump, p1Fruits);
        _p2Tree = BuildTree(PlayerId.P2, p2Anchor, p2Jump, p2Fruits);
        hud?.SetTimeRemaining(matchSeconds);

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            net.Subscribe("fj_started", OnNetStarted);
            net.Subscribe("fj_fruits_respawned", OnNetFruitsRespawned);
            net.Subscribe("fj_score", OnNetScore);
            net.Subscribe("fj_ended", OnNetEnded);
        }
    }

    private TreeState BuildTree(PlayerId player, Transform anchor, JumpHeightCalibrator jump, SpriteRenderer[] fruits)
    {
        var baseScales = new Vector3[fruits.Length];
        for (int i = 0; i < fruits.Length; i++)
            baseScales[i] = fruits[i] != null ? fruits[i].transform.localScale : Vector3.one;

        var state = new TreeState
        {
            Player = player,
            Jump = jump,
            Anchor = anchor,
            BasePosition = anchor.position,
            Fruits = fruits,
            FruitBaseScales = baseScales,
        };
        // jump_front_1~6은 대기->상승->정점->정점->하강->착지(대기와 거의 동일) 순의 "왕복
        // 애니메이션"이라 5/6번은 1/2번과 높이가 겹친다(원본 그림 기준 여백 실측: 50/130/
        // 250/250/160/50px). 이 게임은 정점 하나로 프레임을 고르는 SetProgress(높이 비율)
        // 방식이라 뒤쪽 하강 프레임까지 넣으면 점프 최고점에서 오히려 착지 자세가 나온다 -
        // 그래서 상승 구간만(1~4, 대기->정점) 오름차순으로 사용한다.
        Sprite[] jumpFrames = ArtAssets.LoadCharacterSequence(player, "jump_front");
        if (jumpFrames.Length > 4) System.Array.Resize(ref jumpFrames, 4);
        state.Anim = FrameAnimatedCharacter.Attach(anchor.gameObject, jumpFrames, characterFrameWidth,
            yOffset: -characterFrameWidth * CharacterBottomPaddingRatio);
        return state;
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            UpdateAsClient();
            return;
        }

        HandleDebugTierKeys();

        bool calibrationReady = debugP1OnlyTest
            ? _p1Tree.Jump.IsCalibrated
            : _p1Tree.Jump.IsCalibrated && _p2Tree.Jump.IsCalibrated;
        if (!calibrationReady)
            return; // 캘리브레이션 끝날 때까지 대기 (테스트 모드가 아니면 둘 다 가만히 서 있어야 함)

        if (_ended) return;

        if (!_startedBroadcast && net != null && net.IsHost)
        {
            _startedBroadcast = true;
            net.Send("fj_started", new StartedPayload());
        }

        _elapsed += Time.deltaTime;

        TickTree(_p1Tree, ref _p1Score);
        if (!debugP1OnlyTest) TickTree(_p2Tree, ref _p2Score);
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Score == _p2Score ? null : (_p1Score > _p2Score ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    // 클라이언트는 판정을 전혀 하지 않는다 - 호스트가 fj_started 이벤트를 보내면 타이머
    // 표시만 로컬에서 흘려보내고, 실제 점수/연출은 전부 fj_fruits_respawned/fj_score 이벤트
    // 핸들러(OnNetFruitsRespawned/OnNetScore)가 담당한다.
    private void UpdateAsClient()
    {
        if (_ended || !_clientStarted) return;
        _clientElapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _clientElapsed));
    }

    private void OnNetStarted(NetworkEvent evt)
    {
        _clientStarted = true;
        _clientElapsed = 0f;
    }

    private void OnNetFruitsRespawned(NetworkEvent evt)
    {
        JumpStartPayload payload = NetworkSession.Read<JumpStartPayload>(evt);
        RespawnFruits(payload.player == 0 ? _p1Tree : _p2Tree);
    }

    private void OnNetScore(NetworkEvent evt)
    {
        ScorePayload payload = NetworkSession.Read<ScorePayload>(evt);
        if (payload.player == 0) ScoreTier(_p1Tree, ref _p1Score, payload.tier, payload.bounceSeconds);
        else ScoreTier(_p2Tree, ref _p2Score, payload.tier, payload.bounceSeconds);
    }

    private void OnNetEnded(NetworkEvent evt)
    {
        EndedPayload payload = NetworkSession.Read<EndedPayload>(evt);
        _ended = true;
        PlayerId? winner = DecodeWinner(payload.winner);
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
    }

    // P1 = A/B/C(1/2/3티어), P2 = 숫자키 1/2/3(1/2/3티어).
    private void HandleDebugTierKeys()
    {
        if (!debugTierKeys) return;
        // 프로젝트가 Player Settings에서 Active Input Handling을 "Input System Package"로
        // 쓰고 있어서 레거시 UnityEngine.Input.GetKey*는 예외를 던진다 - 반드시 새 Input
        // System의 Keyboard.current를 써야 한다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 실제 흐름에서는 과일이 "다음 이륙 시점"에 되살아나지만, 디버그 키에는 이륙이 없으므로
        // 반복 테스트가 가능하게 매번 먼저 되살려 둔다(ScoreTier 자체는 실제 판정과 완전히
        // 같은 코드라 여기서 손대지 않는다). 실제 점프가 없어 공중 시간을 잴 수 없으므로
        // 기본값(scoreBounceSeconds)을 쓴다.
        if (_p1Tree != null)
        {
            if (keyboard.aKey.wasPressedThisFrame) { RespawnFruits(_p1Tree); ScoreTier(_p1Tree, ref _p1Score, 0, scoreBounceSeconds); }
            if (keyboard.bKey.wasPressedThisFrame) { RespawnFruits(_p1Tree); ScoreTier(_p1Tree, ref _p1Score, 1, scoreBounceSeconds); }
            if (keyboard.cKey.wasPressedThisFrame) { RespawnFruits(_p1Tree); ScoreTier(_p1Tree, ref _p1Score, 2, scoreBounceSeconds); }
        }
        if (_p2Tree != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) { RespawnFruits(_p2Tree); ScoreTier(_p2Tree, ref _p2Score, 0, scoreBounceSeconds); }
            if (keyboard.digit2Key.wasPressedThisFrame) { RespawnFruits(_p2Tree); ScoreTier(_p2Tree, ref _p2Score, 1, scoreBounceSeconds); }
            if (keyboard.digit3Key.wasPressedThisFrame) { RespawnFruits(_p2Tree); ScoreTier(_p2Tree, ref _p2Score, 2, scoreBounceSeconds); }
        }
    }

    // 착지 판정과 위 디버그 키 둘 다 여기로 들어온다 - 둘이 완전히 같은 경로를 타야 디버그
    // 키로 본 연출이 실제 판정 때와 100% 같다고 믿을 수 있다.
    private void ScoreTier(TreeState tree, ref int score, int tier, float bounceSeconds)
    {
        score += tierScores[tier];
        hud?.SetScore(tree.Player, score);
        hud?.ShowEvent($"{tree.Player} +{tierScores[tier]}!");
        StartCoroutine(EatFruit(tree.Fruits[tier], tree.FruitBaseScales[tier]));
        StartCoroutine(PlayScoreBounce(tree, tier, bounceSeconds));

        // 호스트에서 실제 판정(착지)으로 호출됐을 때만 클라이언트로 알린다. 클라이언트가
        // fj_score 이벤트를 받아 이 함수를 다시 호출할 때는 IsHost가 false라 재전송되지
        // 않는다 - 판정을 두 번 하는 게 아니라 같은 연출 함수를 재사용하는 것뿐이다.
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("fj_score", new ScorePayload
            {
                player = tree.Player == PlayerId.P1 ? 0 : 1,
                tier = tier,
                bounceSeconds = bounceSeconds,
            });
    }

    private void TickTree(TreeState tree, ref int score)
    {
        float height = tree.Jump.GetJumpHeight();

        // 공중에 떠있는 동안은 실시간 높이를 캐릭터에 반영하지 않는다(화면은 가만히 있는
        // 것처럼 보임) - 점프 높이는 판정에만 쓰고, 실제로 캐릭터가 움직이는 연출은 착지해서
        // 단이 확정되는 순간 PlayScoreBounce 한 번만 재생한다(아래 else if 분기).

        bool airborne = height >= tierHeightThresholds[0] * 0.5f;

        if (airborne && !tree.WasAirborne)
        {
            // 이륙(새 점프 시작): 이전 점프에서 먹은 과일을 되돌리고 정점 추적을 초기화.
            RespawnFruits(tree);
            tree.PeakHeight = 0f;
            tree.AirborneStartTime = Time.time;
        }

        if (airborne)
        {
            // 정점 높이를 계속 갱신한다. 하강이 시작되면 더 이상 갱신되지 않으므로, 결국
            // "이번 점프의 진짜 정점 높이"만 남게 된다.
            if (height > tree.PeakHeight)
                tree.PeakHeight = height;
        }
        else if (tree.WasAirborne)
        {
            // 착지(점프 종료): 이번 점프의 최종 정점 높이 하나로 단을 판정하고 곧바로 채점한다.
            int tier = -1;
            for (int i = tierHeightThresholds.Length - 1; i >= 0; i--)
            {
                if (tree.PeakHeight >= tierHeightThresholds[i]) { tier = i; break; }
            }

            if (tier >= 0)
            {
                // 실제 이번 점프가 공중에 떠 있던 시간으로 연출 재생 시간을 정한다.
                float airTime = Time.time - tree.AirborneStartTime;
                float bounceSeconds = Mathf.Clamp(airTime, minScoreBounceSeconds, maxScoreBounceSeconds);
                ScoreTier(tree, ref score, tier, bounceSeconds);
            }
        }

        tree.WasAirborne = airborne;
    }

    // 채점된 과일이 줄어들며 사라진다 - 착지해서 RespawnFruits가 불릴 때까지 비활성 상태.
    private IEnumerator EatFruit(SpriteRenderer fruit, Vector3 baseScale)
    {
        if (fruit == null) yield break;
        Transform t = fruit.transform;
        float elapsed = 0f;
        while (elapsed < fruitEatSeconds)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / fruitEatSeconds;
            t.localScale = Vector3.Lerp(baseScale, Vector3.zero, p);
            yield return null;
        }
        t.localScale = Vector3.zero;
        fruit.enabled = false;
    }

    // 착지해서 점수가 확정된 순간(이벤트) 재생하는 유일한 캐릭터 동작 연출 - 코코넛 깨기의
    // PunchCoconut과 같은 구조: 실시간 점프 높이가 아니라, 그 단 과일이 실제로 걸려 있는
    // 높이(fruit의 world Y)까지 bounceSeconds(보통 이번 점프가 실제로 공중에 떠 있던 시간)
    // 동안 튀어올랐다가 다시 내려온다. 공중에 떠있는 동안에는 이 연출이 없으므로(TickTree에서
    // 실시간 높이를 더 이상 반영하지 않음) 겹쳐 재생될 일은 드물지만, 아주 빠르게 연속 점프해
    // 이전 연출이 안 끝났으면 그냥 무시한다(ScoreBounceActive를 겹침 방지 가드로 사용).
    private IEnumerator PlayScoreBounce(TreeState tree, int tier, float bounceSeconds)
    {
        if (tree.ScoreBounceActive) yield break;
        tree.ScoreBounceActive = true;

        SpriteRenderer fruit = tree.Fruits[tier];
        // 발(anchor) 기준 목표 높이를 과일 Y에 그대로 맞추면 발이 과일 자리까지 가면서 캐릭터
        // 몸통 전체가 과일보다 훨씬 위로 튀어나가 보인다 - 캐릭터 렌더 높이의 절반을 빼서
        // 몸통 중앙이 과일 높이에 오도록 1차 보정한 뒤, 그래도 조금 높아 보여서 20%p 더
        // 빼서(총 70%) 손 언저리가 과일에 닿는 정도로 맞춘다.
        float characterHeight = characterFrameWidth * CharacterCanvasAspect;
        const float HeightSubtractRatio = 0.7f; // 0.5(몸통 중앙) + 0.2(추가 보정)
        float targetY = fruit != null
            ? Mathf.Max(0f, fruit.transform.position.y - tree.BasePosition.y - characterHeight * HeightSubtractRatio)
            : 0f;
        float topThreshold = tierHeightThresholds[tierHeightThresholds.Length - 1];
        float peakProgress = topThreshold > 0f ? tierHeightThresholds[tier] / topThreshold : 0f;

        float half = Mathf.Max(0.01f, bounceSeconds * 0.5f);
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float raw = Mathf.Clamp01(t / half);
            // 올라갈 때: 느리게 시작해서 중간에 빨라졌다가 정점 근처에서 다시 느려진다.
            float eased = EaseInOut(raw, bounceEasePower);
            tree.Anchor.position = tree.BasePosition + Vector3.up * (targetY * eased);
            tree.Anim?.SetProgress(peakProgress * eased);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            float raw = Mathf.Clamp01(t / half);
            // 내려갈 때: 올라갈 때와 반대 - 정점을 떠날 땐 빠르게, 중간에 느려졌다가 착지
            // 직전에 다시 빨라진다.
            float eased = EaseOutIn(raw, bounceEasePower);
            tree.Anchor.position = tree.BasePosition + Vector3.up * (targetY * (1f - eased));
            tree.Anim?.SetProgress(peakProgress * (1f - eased));
            yield return null;
        }

        tree.Anchor.position = tree.BasePosition;
        tree.Anim?.SetProgress(0f);
        tree.ScoreBounceActive = false;
    }

    // 느리게-빠르게-느리게(구간 중앙에서 가장 빠름) - 올라갈 때 씀. power가 클수록 시작/끝
    // (정점 근처)에서 더 느려진다(2=quad, 3=cubic, 4=quartic ease-in-out).
    private static float EaseInOut(float t, float power)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? 0.5f * Mathf.Pow(2f * t, power)
            : 1f - 0.5f * Mathf.Pow(2f * (1f - t), power);
    }

    // EaseInOut의 반대(빠르게-느리게-빠르게, 구간 중앙에서 가장 느림) - 내려갈 때 씀.
    // 앞 절반은 ease-out(빠르게 출발해 감속), 뒤 절반은 ease-in(감속했다가 다시 가속)을
    // 이어붙였다 - 같은 power를 써서 EaseInOut과 정확히 대칭이 되게 한다.
    private static float EaseOutIn(float t, float power)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.5f)
        {
            float x = t * 2f;
            return (1f - Mathf.Pow(1f - x, power)) * 0.5f;
        }
        else
        {
            float x = (t - 0.5f) * 2f;
            return 0.5f + Mathf.Pow(x, power) * 0.5f;
        }
    }

    private void RespawnFruits(TreeState tree)
    {
        for (int i = 0; i < tree.Fruits.Length; i++)
        {
            SpriteRenderer fruit = tree.Fruits[i];
            if (fruit == null) continue;
            fruit.transform.localScale = tree.FruitBaseScales[i];
            fruit.enabled = true;
        }

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("fj_fruits_respawned", new JumpStartPayload { player = tree.Player == PlayerId.P1 ? 0 : 1 });
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("fj_ended", new EndedPayload { winner = EncodeWinner(winner) });
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    private static int EncodeWinner(PlayerId? winner) => winner == PlayerId.P1 ? 0 : winner == PlayerId.P2 ? 1 : -1;
    private static PlayerId? DecodeWinner(int value) => value == 0 ? PlayerId.P1 : value == 1 ? PlayerId.P2 : (PlayerId?)null;

    private void OnGUI()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient) return; // 클라이언트는 캘리브레이션을 하지 않는다

        bool calibrationReady = debugP1OnlyTest
            ? _p1Tree.Jump.IsCalibrated
            : _p1Tree.Jump.IsCalibrated && _p2Tree.Jump.IsCalibrated;
        if (!calibrationReady)
            GUI.Label(new Rect(20, 20, 400, 30), "캘리브레이션 중 - 가만히 서 있으세요...");
    }

    [System.Serializable] private class StartedPayload { }
    [System.Serializable] private class JumpStartPayload { public int player; }

    [System.Serializable]
    private class ScorePayload
    {
        public int player;
        public int tier;
        public float bounceSeconds;
    }

    [System.Serializable] private class EndedPayload { public int winner; }
}

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
// 구성한다. 낮은/중간/높은 단은 각각 사과/포도/파인애플 실제 소품으로 표시하고, 점프
// 높이만큼 캐릭터 자체도 화면에서 위로 튀어올라 보이게 한다.
//
// 캐릭터 표현: 관절 리깅(CavemanSilhouette/Caveman_P1·P2 프리팹) 대신 jump_front_1~4
// 프레임 시퀀스를 점프 높이 비율에 맞춰 통째로 교체한다(FrameAnimatedCharacter). 리깅
// 프리팹이 더 이상 필요 없어 씬에서 제거했으므로, 이 스크립트는 캐릭터를 놓을 빈 Transform
// 하나(p1Anchor/p2Anchor)만 있으면 된다.
using System.Collections;
using UnityEngine;

public class FruitJumpGame : MonoBehaviour
{
    public float matchSeconds = 30f;
    // 낮은 단/중간 단/높은 단 순서. 실측 후 조정 (몸통 길이 대비 비율).
    public float[] tierHeightThresholds = { 0.15f, 0.35f, 0.55f };
    public int[] tierScores = { 1, 3, 5 };
    public float resultDisplaySeconds = 2f;
    public float fruitWidth = 0.4f;
    public float jumpBounceHeight = 1.5f; // 점프 높이 1.0(=몸통 길이만큼 뜸) 기준 캐릭터가 튀어오르는 월드 유닛
    public float fruitEatSeconds = 0.2f; // 채점된 과일이 사라지는 데 걸리는 시간(연출용)
    public float characterFrameWidth = 1.2f; // jump_N 프레임 캐릭터의 표시 폭(월드 유닛) - 기존 리깅과 크기가 맞게 에디터에서 눈으로 보고 조정할 것

    // 테스트용 임시 스위치: true면 P2 캘리브레이션 여부와 무관하게 P1만 캘리브레이션되면
    // 바로 진행한다(P2용 카메라/입력 없이 P1만으로 확인할 때). 실제 2인 플레이 전에는
    // false로 되돌릴 것.
    public bool debugP1OnlyTest = true;

    // jump_front_1(대기 자세) 원본 PNG는 600x900 캔버스에서 발이 바닥까지 닿지 않고 약 50px의
    // 투명 여백을 남겨둔 채 그려져 있다(양쪽 캐릭터 공통). FrameAnimatedCharacter는 캔버스
    // 하단을 곧 "발이 서 있는 자리"로 가정하므로 보정 없이 그대로 쓰면 그 여백만큼 붕 떠
    // 보인다 - characterFrameWidth 비율로 환산해 아래로 그만큼 당겨서 바닥에 붙인다.
    private const float CharacterBottomPaddingRatio = 50f / 600f;

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
        public FrameAnimatedCharacter Anim;
    }

    private TreeState _p1Tree;
    private TreeState _p2Tree;
    private float _elapsed;
    private int _p1Score;
    private int _p2Score;
    private bool _ended;
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _p1Tree = BuildTree(PlayerId.P1, p1Anchor, p1Jump, p1Fruits);
        _p2Tree = BuildTree(PlayerId.P2, p2Anchor, p2Jump, p2Fruits);
        hud?.SetTimeRemaining(matchSeconds);
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
        bool calibrationReady = debugP1OnlyTest
            ? _p1Tree.Jump.IsCalibrated
            : _p1Tree.Jump.IsCalibrated && _p2Tree.Jump.IsCalibrated;
        if (!calibrationReady)
            return; // 캘리브레이션 끝날 때까지 대기 (테스트 모드가 아니면 둘 다 가만히 서 있어야 함)

        if (_ended) return;

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

    private void TickTree(TreeState tree, ref int score)
    {
        float height = tree.Jump.GetJumpHeight();

        // 점프 높이만큼 캐릭터를 실제로 위로 띄워서 화면에서 보이게 한다.
        tree.Anchor.position = tree.BasePosition + Vector3.up * (height * jumpBounceHeight);

        // 가장 높은 단 임계값을 1.0 기준으로 삼아, 점프 높이에 비례해 jump_front_1(대기)~jump_front_4
        // (최고점) 프레임을 고른다.
        float topThreshold = tierHeightThresholds[tierHeightThresholds.Length - 1];
        if (topThreshold > 0f) tree.Anim?.SetProgress(height / topThreshold);

        bool airborne = height >= tierHeightThresholds[0] * 0.5f;

        if (airborne && !tree.WasAirborne)
        {
            // 이륙(새 점프 시작): 이전 점프에서 먹은 과일을 되돌리고 정점 추적을 초기화.
            RespawnFruits(tree);
            tree.PeakHeight = 0f;
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
                score += tierScores[tier];
                hud?.SetScore(tree.Player, score);
                hud?.ShowEvent($"{tree.Player} +{tierScores[tier]}!");
                StartCoroutine(EatFruit(tree.Fruits[tier], tree.FruitBaseScales[tier]));
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

    private void RespawnFruits(TreeState tree)
    {
        for (int i = 0; i < tree.Fruits.Length; i++)
        {
            SpriteRenderer fruit = tree.Fruits[i];
            if (fruit == null) continue;
            fruit.transform.localScale = tree.FruitBaseScales[i];
            fruit.enabled = true;
        }
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    private void OnGUI()
    {
        bool calibrationReady = debugP1OnlyTest
            ? _p1Tree.Jump.IsCalibrated
            : _p1Tree.Jump.IsCalibrated && _p2Tree.Jump.IsCalibrated;
        if (!calibrationReady)
            GUI.Label(new Rect(20, 20, 400, 30), "캘리브레이션 중 - 가만히 서 있으세요...");
    }
}

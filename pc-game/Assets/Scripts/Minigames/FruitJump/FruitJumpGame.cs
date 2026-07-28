// 점프해서 과일따기 - 우가우가게임_기획_프롬프트.md "3. 점프해서 과일따기" 스펙.
//
// 규칙: 각자 독립된 나무(상대와 자원 경쟁 없음). JumpHeightCalibrator.GetJumpHeight()로 3단계
// 높이 판정 -> tierScores 점수. 해당 높이에 처음 도달한 순간 IsMouthOpen()이 true여야 획득
// 인정. 한 번의 점프(이륙~착지)당 과일은 정확히 하나만 채점된다 - 점프 중 이미 하나를
// 먹었으면 그 점프가 끝나고 착지(기준선 근처로 복귀)할 때까지 추가 채점을 막는다. 채점된
// 과일은 화면에서 사라졌다가 착지 시 다시 나타난다(재수확 가능).
//
// 화면은 image/games/fruit_jump/의 실제 아트(나무가 그려진 배경 + 점수 네임플레이트)로
// 구성한다. 낮은/중간/높은 단은 각각 사과/포도/파인애플 실제 소품으로 표시하고, 점프
// 높이만큼 캐릭터 자체도 화면에서 위로 튀어올라 보이게 한다.
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

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private CavemanSilhouette p1Silhouette;
    [SerializeField] private CavemanSilhouette p2Silhouette;
    [SerializeField] private JumpHeightCalibrator p1Jump;
    [SerializeField] private JumpHeightCalibrator p2Jump;
    [SerializeField] private SpriteRenderer[] p1Fruits;
    [SerializeField] private SpriteRenderer[] p2Fruits;
    [SerializeField] private FruitJumpHud hud;

    private class TreeState
    {
        public JumpHeightCalibrator Jump;
        public CavemanSilhouette Silhouette;
        public Vector3 BasePosition;
        public SpriteRenderer[] Fruits;
        public Vector3[] FruitBaseScales;
        public bool ScoredThisJump;
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

        _p1Tree = BuildTree(p1Silhouette, p1Jump, p1Fruits);
        _p2Tree = BuildTree(p2Silhouette, p2Jump, p2Fruits);
        hud?.SetTimeRemaining(matchSeconds);
    }

    private TreeState BuildTree(CavemanSilhouette silhouette, JumpHeightCalibrator jump, SpriteRenderer[] fruits)
    {
        var baseScales = new Vector3[fruits.Length];
        for (int i = 0; i < fruits.Length; i++)
            baseScales[i] = fruits[i] != null ? fruits[i].transform.localScale : Vector3.one;

        var state = new TreeState
        {
            Jump = jump,
            Silhouette = silhouette,
            BasePosition = silhouette.transform.position,
            Fruits = fruits,
            FruitBaseScales = baseScales,
        };
        return state;
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        _p1Tree.Silhouette.ApplyPose(p1);
        _p2Tree.Silhouette.ApplyPose(p2);

        if (!_p1Tree.Jump.IsCalibrated || !_p2Tree.Jump.IsCalibrated)
            return; // 캘리브레이션 끝날 때까지 대기 (둘 다 가만히 서 있어야 함)

        if (_ended) return;

        _elapsed += Time.deltaTime;

        TickTree(_p1Tree, p1, ref _p1Score);
        TickTree(_p2Tree, p2, ref _p2Score);
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Score == _p2Score ? null : (_p1Score > _p2Score ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void TickTree(TreeState tree, PlayerPoseState state, ref int score)
    {
        float height = tree.Jump.GetJumpHeight();

        // 점프 높이만큼 캐릭터를 실제로 위로 띄워서 화면에서 보이게 한다.
        tree.Silhouette.transform.position = tree.BasePosition + Vector3.up * (height * jumpBounceHeight);

        // 기준선 근처로 완전히 내려오면 "착지"로 보고, 이번 점프에서 먹은 과일을 되돌리고
        // 다음 점프을 새로 채점할 수 있게 리셋한다.
        bool landed = height < tierHeightThresholds[0] * 0.5f;
        if (landed)
        {
            if (tree.ScoredThisJump) RespawnFruits(tree);
            tree.ScoredThisJump = false;
            return; // 땅에 있는 동안은 채점할 것이 없다.
        }

        // 한 점프당 과일은 하나만 - 이미 이번 점프에서 먹었으면 착지 전까지 더 채점하지 않는다.
        if (tree.ScoredThisJump) return;

        // 그 프레임에 도달한 가장 높은 단 하나만 후보로 삼는다(중간 단을 거쳐왔더라도
        // 이 프레임에서 판정하는 건 최고 단 하나뿐).
        int currentTier = -1;
        for (int i = tierHeightThresholds.Length - 1; i >= 0; i--)
        {
            if (height >= tierHeightThresholds[i]) { currentTier = i; break; }
        }
        if (currentTier < 0) return; // 낮은 단 임계값에도 못 미치는 높이 - 채점 대상 아님.

        if (state == null || !state.IsTracked || !state.IsMouthOpen()) return; // 입을 안 벌렸으면 이번 프레임은 실패, 다음 프레임에 같은 점프로 재시도 가능.

        score += tierScores[currentTier];
        tree.ScoredThisJump = true;
        hud?.SetScore(tree.Silhouette.player, score);
        hud?.ShowEvent($"{tree.Silhouette.player} +{tierScores[currentTier]}!");
        StartCoroutine(EatFruit(tree.Fruits[currentTier], tree.FruitBaseScales[currentTier]));
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
        if (!_p1Tree.Jump.IsCalibrated || !_p2Tree.Jump.IsCalibrated)
            GUI.Label(new Rect(20, 20, 400, 30), "캘리브레이션 중 - 가만히 서 있으세요...");
    }
}

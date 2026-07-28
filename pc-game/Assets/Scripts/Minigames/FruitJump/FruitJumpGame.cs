// 점프해서 과일따기 - 우가우가게임_기획_프롬프트.md "3. 점프해서 과일따기" 스펙.
//
// 규칙: 각자 독립된 나무(상대와 자원 경쟁 없음). JumpHeightCalibrator.GetJumpHeight()로 3단계
// 높이 판정 -> tierScores 점수. 해당 높이에 처음 도달한 순간 IsMouthOpen()이 true여야 획득
// 인정. 같은 점프에서 같은 단은 한 번만 채점되고(착지해서 기준선 근처로 내려가야 다음 점프로
// 리셋), 단마다 별도 쿨다운(tierCooldowns)이 있어 너무 빠르게 반복 채점되는 것을 막는다.
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
    public float[] tierCooldowns = { 0.3f, 0.6f, 1.0f };
    public float resultDisplaySeconds = 2f;
    public float fruitWidth = 0.4f;
    public float jumpBounceHeight = 1.5f; // 점프 높이 1.0(=몸통 길이만큼 뜸) 기준 캐릭터가 튀어오르는 월드 유닛

    private static readonly string[] TierProps = { "fruit_apple", "fruit_grapes", "fruit_pineapple" };

    private class TreeState
    {
        public JumpHeightCalibrator Jump;
        public CavemanSilhouette Silhouette;
        public Vector3 BasePosition;
        public SpriteRenderer[] Fruits;
        public float[] CooldownTimers;
        public int LastScoredTier = -1;
    }

    private TreeState _p1Tree;
    private TreeState _p2Tree;
    private float _elapsed;
    private int _p1Score;
    private int _p2Score;
    private bool _ended;
    private FruitJumpHud _hud;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        Camera cam = Camera.main;
        if (cam == null)
        {
            var camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        cam.orthographic = true;
        cam.orthographicSize = 4f;
        cam.transform.position = new Vector3(0, 1f, -10f);

        ArtAssets.CreateBackground(cam, ArtAssets.LoadFruitJump("background"));

        _p1Tree = BuildTree(PlayerId.P1, new Vector3(-2.5f, 0f, 0f));
        _p2Tree = BuildTree(PlayerId.P2, new Vector3(2.5f, 0f, 0f));

        _hud = FruitJumpHud.Build(matchSeconds);
    }

    private TreeState BuildTree(PlayerId id, Vector3 basePosition)
    {
        var jumpObj = new GameObject($"{id} JumpCalibrator");
        var jump = jumpObj.AddComponent<JumpHeightCalibrator>();
        jump.player = id;

        var go = new GameObject($"Caveman_{id}");
        go.transform.position = basePosition;
        var silhouette = go.AddComponent<CavemanSilhouette>();
        silhouette.player = id;

        var state = new TreeState
        {
            Jump = jump,
            Silhouette = silhouette,
            BasePosition = basePosition,
            Fruits = new SpriteRenderer[tierHeightThresholds.Length],
            CooldownTimers = new float[tierHeightThresholds.Length],
        };

        for (int i = 0; i < tierHeightThresholds.Length; i++)
        {
            var fruit = new GameObject($"Fruit_Tier{i}");
            fruit.transform.SetParent(go.transform, false);
            float visualHeight = 1f + tierHeightThresholds[i] * 3.5f;
            fruit.transform.localPosition = new Vector3(0f, visualHeight, 0.5f);
            var sr = fruit.AddComponent<SpriteRenderer>();
            sr.sprite = ArtAssets.LoadProp(TierProps[i]);
            sr.sortingOrder = 2;
            ArtAssets.FitWidth(sr, fruitWidth);
            state.Fruits[i] = sr;
        }

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
        _hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Score == _p2Score ? null : (_p1Score > _p2Score ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void TickTree(TreeState tree, PlayerPoseState state, ref int score)
    {
        for (int i = 0; i < tree.CooldownTimers.Length; i++)
            if (tree.CooldownTimers[i] > 0f) tree.CooldownTimers[i] -= Time.deltaTime;

        float height = tree.Jump.GetJumpHeight();

        // 점프 높이만큼 캐릭터를 실제로 위로 띄워서 화면에서 보이게 한다.
        tree.Silhouette.transform.position = tree.BasePosition + Vector3.up * (height * jumpBounceHeight);

        // 기준선 근처로 완전히 내려오면 다음 점프를 새로 채점할 수 있게 리셋.
        if (height < tierHeightThresholds[0] * 0.5f)
            tree.LastScoredTier = -1;

        int currentTier = -1;
        for (int i = tierHeightThresholds.Length - 1; i >= 0; i--)
        {
            if (height >= tierHeightThresholds[i]) { currentTier = i; break; }
        }

        if (currentTier >= 0 && currentTier != tree.LastScoredTier && tree.CooldownTimers[currentTier] <= 0f
            && state != null && state.IsTracked && state.IsMouthOpen())
        {
            score += tierScores[currentTier];
            tree.LastScoredTier = currentTier;
            tree.CooldownTimers[currentTier] = tierCooldowns[currentTier];
            _hud?.SetScore(tree.Silhouette.player, score);
            _hud?.ShowEvent($"{tree.Silhouette.player} +{tierScores[currentTier]}!");
            StartCoroutine(PulseFruit(tree.Fruits[currentTier]));
        }
    }

    private IEnumerator PulseFruit(SpriteRenderer fruit)
    {
        if (fruit == null) yield break;
        Transform t = fruit.transform;
        Vector3 baseScale = t.localScale;
        float duration = 0.25f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / duration;
            t.localScale = Vector3.Lerp(baseScale * 1.6f, baseScale, p);
            yield return null;
        }
        t.localScale = baseScale;
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        _hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
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

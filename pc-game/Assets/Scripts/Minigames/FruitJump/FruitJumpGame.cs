// 점프해서 과일따기 - 우가우가게임_기획_프롬프트.md "3. 점프해서 과일따기" 스펙.
//
// 규칙: 각자 독립된 나무(상대와 자원 경쟁 없음). JumpHeightCalibrator.GetJumpHeight()로 3단계
// 높이 판정 -> tierScores 점수. 해당 높이에 처음 도달한 순간 IsMouthOpen()이 true여야 획득
// 인정. 같은 점프에서 같은 단은 한 번만 채점되고(착지해서 기준선 근처로 내려가야 다음 점프로
// 리셋), 단마다 별도 쿨다운(tierCooldowns)이 있어 너무 빠르게 반복 채점되는 것을 막는다.
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

    private static readonly Color[] TierColors =
    {
        new Color(0.9f, 0.8f, 0.2f),  // 낮은 단 - 노랑
        new Color(0.95f, 0.5f, 0.15f), // 중간 단 - 주황
        new Color(0.85f, 0.15f, 0.2f), // 높은 단 - 빨강
    };

    private class TreeState
    {
        public JumpHeightCalibrator Jump;
        public SpriteRenderer[] Fruits;
        public float[] CooldownTimers;
        public int LastScoredTier = -1;
    }

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
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

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2.5f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2.5f, 0f, 0f));

        var p1JumpObj = new GameObject("P1 JumpCalibrator");
        var p1Jump = p1JumpObj.AddComponent<JumpHeightCalibrator>();
        p1Jump.player = PlayerId.P1;

        var p2JumpObj = new GameObject("P2 JumpCalibrator");
        var p2Jump = p2JumpObj.AddComponent<JumpHeightCalibrator>();
        p2Jump.player = PlayerId.P2;

        _p1Tree = BuildTree(p1Jump, _p1Silhouette.transform);
        _p2Tree = BuildTree(p2Jump, _p2Silhouette.transform);
    }

    private TreeState BuildTree(JumpHeightCalibrator jump, Transform playerTransform)
    {
        var state = new TreeState
        {
            Jump = jump,
            Fruits = new SpriteRenderer[tierHeightThresholds.Length],
            CooldownTimers = new float[tierHeightThresholds.Length],
        };

        // 나무 기둥 - 심플한 세로 캡슐.
        var trunk = new GameObject("TreeTrunk");
        trunk.transform.SetParent(playerTransform, false);
        trunk.transform.localPosition = new Vector3(0f, 1.4f, 1f);
        var trunkRenderer = trunk.AddComponent<SpriteRenderer>();
        trunkRenderer.sprite = RuntimeSpriteFactory.CreateCapsule(24, 160, new Color(0.4f, 0.28f, 0.15f));
        trunkRenderer.sortingOrder = -1;

        for (int i = 0; i < tierHeightThresholds.Length; i++)
        {
            var fruit = new GameObject($"Fruit_Tier{i}");
            fruit.transform.SetParent(playerTransform, false);
            // 점프 높이 비율을 화면상 대략적인 높이감으로 스케일링(순수 시각용, 판정과 무관).
            float visualHeight = 1f + tierHeightThresholds[i] * 3.5f;
            fruit.transform.localPosition = new Vector3(0f, visualHeight, 0.5f);
            var sr = fruit.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSpriteFactory.CreateCircle(30, TierColors[i]);
            sr.sortingOrder = 2;
            state.Fruits[i] = sr;
        }

        return state;
    }

    private CavemanSilhouette Spawn(PlayerId id, Vector3 pos)
    {
        var go = new GameObject($"Caveman_{id}");
        go.transform.position = pos;
        var s = go.AddComponent<CavemanSilhouette>();
        s.player = id;
        return s;
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        _p1Silhouette.ApplyPose(p1);
        _p2Silhouette.ApplyPose(p2);

        if (!_p1Tree.Jump.IsCalibrated || !_p2Tree.Jump.IsCalibrated)
            return; // 캘리브레이션 끝날 때까지 대기 (둘 다 가만히 서 있어야 함)

        if (_ended) return;

        _elapsed += Time.deltaTime;

        TickTree(_p1Tree, p1, ref _p1Score);
        TickTree(_p2Tree, p2, ref _p2Score);

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
            StartCoroutine(PulseFruit(tree.Fruits[currentTier]));
        }
    }

    private IEnumerator PulseFruit(SpriteRenderer fruit)
    {
        if (fruit == null) yield break;
        Transform t = fruit.transform;
        float duration = 0.25f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / duration;
            t.localScale = Vector3.Lerp(Vector3.one * 1.6f, Vector3.one, p);
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
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
        {
            GUI.Label(new Rect(20, 20, 400, 30), "캘리브레이션 중 - 가만히 서 있으세요...");
            return;
        }
        GUI.Label(new Rect(20, 20, 400, 30), $"P1: {_p1Score}점   P2: {_p2Score}점");
        GUI.Label(new Rect(20, 50, 400, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");
        if (_ended)
        {
            PlayerId? winner = _p1Score == _p2Score ? null : (_p1Score > _p2Score ? PlayerId.P1 : PlayerId.P2);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 90, Screen.width, 40), winner == null ? "무승부!" : $"{winner} 승리!", style);
        }
    }
}

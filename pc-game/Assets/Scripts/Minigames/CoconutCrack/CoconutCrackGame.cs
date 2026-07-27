// 머리로 코코넛 깨기 (반복 속도 경쟁) - 우가우가게임_기획_프롬프트.md "4. 머리로 코코넛
// 깨기" 스펙.
//
// 규칙 요약 (구현 필요):
//   - PoseInputHub의 Get(player).HandToHeadDistance()가 hitDistance 이하로 좁혀졌다가
//     releaseDistance 이상으로 다시 벌어지는 한 사이클을 "1회 타격"으로 카운트.
//   - matchSeconds(기본 15초) 동안 더 많이 타격한 사람이 승리 (내구도/단계 없음, 순수
//     반복 속도 경쟁).
//
// 아래는 부트스트랩 + 사이클 카운터 상태 머신 + 타격 시각 피드백(코코넛 펀치 스케일/색 플래시)
// 까지 채워둔 상태. 임계값은 실측 후 조정할 것.
using System.Collections;
using UnityEngine;

public class CoconutCrackGame : MonoBehaviour
{
    public float matchSeconds = 15f;
    public float hitDistance = 0.25f;     // 몸통 길이 대비, 이 이하면 "타격"
    public float releaseDistance = 0.45f; // 이 이상으로 벌어져야 다음 타격을 셀 준비 완료
    public float resultDisplaySeconds = 2f;

    private static readonly Color CoconutBrown = new Color(0.5f, 0.35f, 0.2f);

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private SpriteRenderer _p1Coconut;
    private SpriteRenderer _p2Coconut;
    private float _elapsed;
    private int _p1Hits;
    private int _p2Hits;
    private bool _p1ReadyToHit = true;
    private bool _p2ReadyToHit = true;
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
        cam.orthographicSize = 3f;
        cam.transform.position = new Vector3(0, 1f, -10f);

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2f, 0f, 0f));
        _p1Coconut = SpawnCoconut(_p1Silhouette.transform);
        _p2Coconut = SpawnCoconut(_p2Silhouette.transform);
    }

    private SpriteRenderer SpawnCoconut(Transform parent)
    {
        var go = new GameObject("Coconut");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSpriteFactory.CreateCircle(50, CoconutBrown);
        sr.sortingOrder = 3;
        return sr;
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

        if (_ended) return;

        CountHits(p1, ref _p1ReadyToHit, ref _p1Hits, _p1Coconut);
        CountHits(p2, ref _p2ReadyToHit, ref _p2Hits, _p2Coconut);

        _elapsed += Time.deltaTime;
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void CountHits(PlayerPoseState state, ref bool readyToHit, ref int hitCount, SpriteRenderer coconut)
    {
        if (state == null || !state.IsTracked) return;
        float distance = state.HandToHeadDistance();

        if (readyToHit && distance <= hitDistance)
        {
            hitCount++;
            readyToHit = false;
            StartCoroutine(PunchCoconut(coconut));
        }
        else if (!readyToHit && distance >= releaseDistance)
        {
            readyToHit = true;
        }
    }

    // 타격 순간 코코넛이 살짝 커졌다가(펀치 스케일) 흰색으로 번쩍이며 원래 크기/색으로 돌아온다.
    private IEnumerator PunchCoconut(SpriteRenderer coconut)
    {
        if (coconut == null) yield break;
        Transform t = coconut.transform;
        Vector3 baseScale = Vector3.one;
        coconut.color = Color.white;

        float duration = 0.18f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / duration;
            t.localScale = Vector3.Lerp(baseScale * 1.4f, baseScale, p);
            coconut.color = Color.Lerp(Color.white, CoconutBrown, p);
            yield return null;
        }
        t.localScale = baseScale;
        coconut.color = CoconutBrown;
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
        GUI.Label(new Rect(20, 20, 400, 30), $"P1: {_p1Hits}회   P2: {_p2Hits}회");
        GUI.Label(new Rect(20, 50, 400, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");
        if (_ended)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 90, Screen.width, 40), winner == null ? "무승부!" : $"{winner} 승리!", style);
        }
    }
}

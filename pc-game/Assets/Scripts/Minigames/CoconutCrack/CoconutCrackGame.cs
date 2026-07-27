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
    public float coconutWidth = 0.5f; // 인게임에서 보일 코코넛 가로 폭(월드 유닛)

    private Sprite _coconutSprite;
    private Sprite _coconutBreakLeftSprite;
    private Sprite _coconutBreakRightSprite;

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

        _coconutSprite = ArtAssets.LoadProp("coconut");
        _coconutBreakLeftSprite = ArtAssets.LoadProp("coconut_break_left");
        _coconutBreakRightSprite = ArtAssets.LoadProp("coconut_break_right");

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
        sr.sprite = _coconutSprite;
        sr.sortingOrder = 3;
        ArtAssets.FitWidth(sr, coconutWidth);
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

    // 타격 순간 멀쩡한 코코넛을 잠깐 숨기고, 반으로 쪼개진 코코넛 두 조각이 좌우로 튀어나가며
    // 사라지는 연출을 보여준 뒤 다시 멀쩡한 코코넛으로 복구한다(반복 타격 경쟁이라 매번
    // "깨졌다가 새 코코넛이 나타나는" 것처럼 보이게).
    private IEnumerator PunchCoconut(SpriteRenderer coconut)
    {
        if (coconut == null) yield break;

        SpriteRenderer left = SpawnHalf(coconut.transform, _coconutBreakLeftSprite);
        SpriteRenderer right = SpawnHalf(coconut.transform, _coconutBreakRightSprite);
        coconut.enabled = false;

        const float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / duration;
            left.transform.localPosition = Vector3.Lerp(Vector3.zero, new Vector3(-0.3f, -0.15f, 0f), p);
            left.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, -30f, p));
            right.transform.localPosition = Vector3.Lerp(Vector3.zero, new Vector3(0.3f, -0.15f, 0f), p);
            right.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, 30f, p));
            SetAlpha(left, 1f - p);
            SetAlpha(right, 1f - p);
            yield return null;
        }

        Destroy(left.gameObject);
        Destroy(right.gameObject);
        coconut.enabled = true;
    }

    private SpriteRenderer SpawnHalf(Transform parent, Sprite sprite)
    {
        var go = new GameObject("CoconutHalf");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 4;
        ArtAssets.FitWidth(sr, coconutWidth);
        return sr;
    }

    private static void SetAlpha(SpriteRenderer sr, float alpha)
    {
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
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

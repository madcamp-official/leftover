// 머리로 코코넛 깨기 (반복 속도 경쟁) - 저능아게임_기획_프롬프트.md "4. 머리로 코코넛
// 깨기" 스펙.
//
// 규칙 요약 (구현 필요):
//   - PoseInputHub의 Get(player).HandToHeadDistance()가 hitDistance 이하로 좁혀졌다가
//     releaseDistance 이상으로 다시 벌어지는 한 사이클을 "1회 타격"으로 카운트.
//   - matchSeconds(기본 15초) 동안 더 많이 타격한 사람이 승리 (내구도/단계 없음, 순수
//     반복 속도 경쟁).
//
// 아래는 부트스트랩 + 사이클 카운터 상태 머신 뼈대까지 채워둔 상태. 임계값 실측 조정과
// 시각적 피드백(코코넛 금 가는 연출 등)은 TODO 부분에 채워 넣을 것.
using UnityEngine;

public class CoconutCrackGame : MonoBehaviour
{
    public float matchSeconds = 15f;
    public float hitDistance = 0.25f;     // 몸통 길이 대비, 이 이하면 "타격"
    public float releaseDistance = 0.45f; // 이 이상으로 벌어져야 다음 타격을 셀 준비 완료

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _elapsed;
    private int _p1Hits;
    private int _p2Hits;
    private bool _p1ReadyToHit = true;
    private bool _p2ReadyToHit = true;

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

        CountHits(p1, ref _p1ReadyToHit, ref _p1Hits);
        CountHits(p2, ref _p2ReadyToHit, ref _p2Hits);

        _elapsed += Time.deltaTime;
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            MatchController.Instance?.ReportRoundResult(winner);
            MatchController.Instance?.LoadNextRound();
        }
    }

    private void CountHits(PlayerPoseState state, ref bool readyToHit, ref int hitCount)
    {
        if (state == null || !state.IsTracked) return;
        float distance = state.HandToHeadDistance();

        if (readyToHit && distance <= hitDistance)
        {
            hitCount++;
            readyToHit = false;
            // TODO: 타격 시각/사운드 피드백
        }
        else if (!readyToHit && distance >= releaseDistance)
        {
            readyToHit = true;
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 400, 30), $"P1: {_p1Hits}회   P2: {_p2Hits}회");
        GUI.Label(new Rect(20, 50, 400, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");
    }
}

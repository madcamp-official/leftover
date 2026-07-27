// 자세 따라하기 (인간 테트리스) - 저능아게임_기획_프롬프트.md "2. 자세 따라하기" 스펙.
//
// 규칙 요약 (구현 필요):
//   - "모션 정하기 턴"(poseHoldSeconds 기본 2초 유지) / "플레이 턴"이 번갈아 진행, 총
//     roundCount(기본 3) 왕복.
//   - 모션 정하기 턴에 캡처한 포즈(어깨/팔꿈치/손목/무릎 - PoseInputHub.JointSample)가
//     상대 쪽 벽의 "구멍" 모양이 된다.
//   - 플레이 턴: 벽이 도달하는 순간 자신의 관절 좌표가 그 구멍과 poseMatchTolerance(기본
//     0.15, 관절별 상대오차) 이내면 통과, 아니면 벽에 닿아 knockbackRatio(기본 트랙의 10%)
//     만큼 트랙에서 밀려남.
//   - roundCount 왕복 후 덜 밀려난(트랙 원점에 더 가까운) 사람이 승리.
//
// 아래는 부트스트랩 + 캐릭터 스폰까지만 채워둔 상태. 포즈 캡처/실루엣 구멍 시각화/매칭
// 판정/트랙 밀림 시각화는 TODO 부분에 채워 넣을 것.
using UnityEngine;

public class PoseCopyGame : MonoBehaviour
{
    public float poseHoldSeconds = 2f;
    public float poseMatchTolerance = 0.15f;
    public int roundCount = 3;
    public float knockbackRatio = 0.1f;

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _p1TrackPosition; // 0 = 원점, 커질수록 많이 밀려남
    private float _p2TrackPosition;

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

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-3f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(3f, 0f, 0f));

        // TODO: 턴 상태 머신(모션 정하기 P1 -> 플레이 P2 -> 모션 정하기 P2 -> 플레이 P1 ->
        // ... roundCount번 왕복) 구성. 포즈 캡처는 poseHoldSeconds 동안
        // PoseInputHub.Get(player).Joints를 매 프레임 누적/평균해서 마지막 스냅샷으로 저장.
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
        _p1Silhouette.ApplyPose(hub?.Get(PlayerId.P1));
        _p2Silhouette.ApplyPose(hub?.Get(PlayerId.P2));

        // TODO: 턴 진행, 벽 접근 애니메이션, 매칭 판정, _p1TrackPosition/_p2TrackPosition
        // 갱신. roundCount 왕복이 끝나면:
        // PlayerId? winner = _p1TrackPosition == _p2TrackPosition ? null
        //     : (_p1TrackPosition < _p2TrackPosition ? PlayerId.P1 : PlayerId.P2);
        // MatchController.Instance?.ReportRoundResult(winner);
        // MatchController.Instance?.LoadNextRound();
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 400, 30), $"P1 밀림: {_p1TrackPosition:F2}   P2 밀림: {_p2TrackPosition:F2}");
    }
}

// 점프해서 과일따기 - 우가우가게임_기획_프롬프트.md "3. 점프해서 과일따기" 스펙.
//
// 규칙 요약 (구현 필요):
//   - 각자 독립된 나무(상대와 자원 경쟁 없음).
//   - JumpHeightCalibrator.GetJumpHeight()로 3단계 높이 판정 -> tierScores 점수.
//   - 해당 높이 도달 순간(또는 착지 직후 짧은 윈도우) PoseInputHub의
//     Get(player).IsMouthOpen()이 true여야 과일 획득 인정.
//   - matchSeconds(기본 30초) 동안 더 많은 점수를 낸 사람이 승리.
//
// 아래는 부트스트랩 + 캘리브레이터 부착까지만 채워둔 상태. 나무/과일 배치, 높이 단계 판정,
// 쿨다운, 점수 집계는 TODO 부분에 채워 넣을 것.
using UnityEngine;

public class FruitJumpGame : MonoBehaviour
{
    public float matchSeconds = 30f;
    // 낮은 단/중간 단/높은 단 순서. 실측 후 조정 (몸통 길이 대비 비율).
    public float[] tierHeightThresholds = { 0.15f, 0.35f, 0.55f };
    public int[] tierScores = { 1, 3, 5 };

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private JumpHeightCalibrator _p1Jump;
    private JumpHeightCalibrator _p2Jump;
    private float _elapsed;
    private int _p1Score;
    private int _p2Score;

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
        _p1Jump = p1JumpObj.AddComponent<JumpHeightCalibrator>();
        _p1Jump.player = PlayerId.P1;

        var p2JumpObj = new GameObject("P2 JumpCalibrator");
        _p2Jump = p2JumpObj.AddComponent<JumpHeightCalibrator>();
        _p2Jump.player = PlayerId.P2;

        // TODO: 각자 나무/3단 과일 배치(별도 GameObject나 스프라이트로).
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

        if (!_p1Jump.IsCalibrated || !_p2Jump.IsCalibrated)
            return; // 캘리브레이션 끝날 때까지 대기 (둘 다 가만히 서 있어야 함)

        _elapsed += Time.deltaTime;

        // TODO: _p1Jump.GetJumpHeight()/_p2Jump.GetJumpHeight()를 tierHeightThresholds와
        // 비교해서 몇 단인지 판정 -> 그 순간(또는 짧은 윈도우 내) 입 벌림 확인 -> 점수 획득 +
        // 같은 단 재판정 방지용 쿨다운.

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Score == _p2Score ? null : (_p1Score > _p2Score ? PlayerId.P1 : PlayerId.P2);
            MatchController.Instance?.ReportRoundResult(winner);
            MatchController.Instance?.LoadNextRound();
        }
    }

    private void OnGUI()
    {
        if (!_p1Jump.IsCalibrated || !_p2Jump.IsCalibrated)
        {
            GUI.Label(new Rect(20, 20, 400, 30), "캘리브레이션 중 - 가만히 서 있으세요...");
            return;
        }
        GUI.Label(new Rect(20, 20, 400, 30), $"P1: {_p1Score}점   P2: {_p2Score}점");
        GUI.Label(new Rect(20, 50, 400, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");
    }
}

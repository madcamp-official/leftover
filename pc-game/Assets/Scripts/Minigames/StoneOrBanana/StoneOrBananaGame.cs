// 돌 or 바나나 - 저능아게임_기획_프롬프트.md "5. 돌 or 바나나" 스펙.
//
// 규칙 요약 (구현 필요):
//   - 매 턴 던지는/받는 역할이 번갈아 바뀜 (1턴 P1 던짐/P2 받음, 2턴 P2 던짐/P1 받음, ...).
//   - 던지는 사람: 오른손 들면 돌, 왼손 들면 바나나 (PoseInputHub.IsHandRaised) - 던지는
//     대상은 화면에 물리적으로 보임(숨김 없음).
//   - 받는 사람: 입 벌리면(IsMouthOpen) "먹기", 다물면 "피하기".
//   - 바나나 던졌는데 안 먹음(피함) -> 바나나가 부메랑처럼 돌아와 던진 사람이 먹어야 함
//     (다시 같은 판정, 이번엔 던진 사람이 입 벌려야 포만감 획득).
//   - 바나나 먹음 -> 받는 사람 fullnessGauge 증가.
//   - 돌 던졌는데 먹어버림 -> 순수 페널티, toothGauge 감소, 포만감 증가 없음.
//   - 돌 피함 -> 페널티 없음.
//   - toothGauge가 0이 되면 그 즉시 패배 (fullnessGauge를 다 채우기 전이라도).
//   - 정상적으로는 fullnessGauge를 먼저 채운 사람이 승리.
//
// 아래는 부트스트랩 + 턴 상태 머신 뼈대 + 게이지 필드까지 채워둔 상태. 던지기 판정 시점
// 검출(예: 손을 들고 일정 시간 유지하면 "던짐"으로 확정), 부메랑 연출, 게이지 UI는 TODO
// 부분에 채워 넣을 것.
using UnityEngine;

public class StoneOrBananaGame : MonoBehaviour
{
    private enum ThrowKind { Stone, Banana }
    private enum TurnPhase { WaitingThrow, WaitingCatch, BoomerangBackToThrower }

    public float maxFullness = 5f;
    public float maxTeeth = 3f;
    public float turnTimeoutSeconds = 8f;

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _p1Fullness, _p2Fullness;
    private float _p1Teeth = 3f, _p2Teeth = 3f;
    private PlayerId _currentThrower = PlayerId.P1;
    private TurnPhase _phase = TurnPhase.WaitingThrow;
    private float _turnTimer;

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
        _p1Teeth = _p2Teeth = maxTeeth;
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

        // TODO: _phase 상태머신 구현.
        //   WaitingThrow: 현재 _currentThrower의 IsHandRaised(right)/IsHandRaised(left)를
        //     보고 돌/바나나 확정 -> WaitingCatch로 전환, 받는 사람 지정.
        //   WaitingCatch: 받는 사람의 IsMouthOpen()을 turnTimeoutSeconds 안에 확인 ->
        //     결과에 따라 게이지 변경 (규칙 요약 참고), 바나나+안먹음이면
        //     BoomerangBackToThrower로, 아니면 _currentThrower를 상대로 바꾸고 WaitingThrow로.
        //   BoomerangBackToThrower: 이번엔 원래 던진 사람이 받는 사람이 되어 같은 판정 반복.
        //
        // 매 판정마다 teeth <= 0 이면 즉시 승부 확정:
        //   if (_p1Teeth <= 0f) { MatchController.Instance?.ReportRoundResult(PlayerId.P2); MatchController.Instance?.LoadNextRound(); }
        //   (P2도 동일하게)
        // fullness가 maxFullness에 먼저 도달한 쪽이 승리하는 것도 같은 방식으로 처리.
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 400, 30), $"P1 포만감 {_p1Fullness:F0}/{maxFullness}  이빨 {_p1Teeth:F0}/{maxTeeth}");
        GUI.Label(new Rect(20, 50, 400, 30), $"P2 포만감 {_p2Fullness:F0}/{maxFullness}  이빨 {_p2Teeth:F0}/{maxTeeth}");
        GUI.Label(new Rect(20, 80, 400, 30), $"현재 던지는 사람: {_currentThrower}");
    }
}

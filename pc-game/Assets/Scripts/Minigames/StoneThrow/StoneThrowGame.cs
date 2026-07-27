// 돌던지기 - 저능아게임_기획_프롬프트.md "1. 돌던지기" 스펙.
//
// 규칙 요약 (구현 필요):
//   - 어느 손이든 들려 있으면, 약 fireIntervalSeconds(기본 1초)마다 자동으로 돌 1개가
//     발사된다. 발사 순간 "어느 손이 들려 있었는지"로 조준 방향(상대 왼쪽/오른쪽 중 하나)이
//     정해진다 - PoseInputHub의 IsHandRaised(rightHand:true/false)로 매 프레임 확인.
//   - 상대는 머리를 좌/우로 기울여서(HeadTiltRatio()) 회피 - 발사 순간의 조준 방향과
//     상대의 머리 기울기 방향이 같으면 회피 성공, 다르면 명중.
//   - matchSeconds(기본 30초) 동안 더 많이 맞힌 사람이 승리.
//
// 아래는 씬 부트스트랩 + 캐릭터 스폰까지만 채워둔 상태 - 발사 타이머/조준/피격 판정은
// TODO 표시된 Update() 안에 채워 넣을 것. StaringContestGame.cs가 MatchController 연동
// 예시니 그 파일의 EndRound()/ProceedAfterDelay() 패턴을 그대로 참고하면 된다.
using UnityEngine;

public class StoneThrowGame : MonoBehaviour
{
    public float fireIntervalSeconds = 1f;
    public float matchSeconds = 30f;

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _elapsed;
    private float _p1FireTimer;
    private float _p2FireTimer;
    private int _p1Hits;
    private int _p2Hits;

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

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2.5f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2.5f, 0f, 0f));
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

        _elapsed += Time.deltaTime;

        // TODO: fireIntervalSeconds마다 각 플레이어의 "현재 들려 있는 손"으로 발사 이벤트를
        // 만들고, 상대의 그 순간 HeadTiltRatio() 부호와 비교해서 명중/회피 판정 -> _p1Hits/
        // _p2Hits 누적. 발사 시각적 표시(예: 작은 돌 스프라이트가 화면을 가로지르는 연출)도
        // 여기서 트리거.

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            MatchController.Instance?.ReportRoundResult(winner);
            MatchController.Instance?.LoadNextRound();
        }
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 300, 30), $"P1 hits: {_p1Hits}   P2 hits: {_p2Hits}");
        GUI.Label(new Rect(20, 50, 300, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");
    }
}

// 자세 따라하기 (인간 테트리스) - 우가우가게임_기획_프롬프트.md "2. 자세 따라하기" 스펙.
//
// 규칙: "모션 정하기 턴"(poseHoldSeconds 동안 포즈 유지 -> 관절 오프셋 평균으로 캡처) /
// "플레이 턴"(wallApproachSeconds 후 상대의 현재 포즈와 비교)이 번갈아 진행, roundCount만큼
// 왕복(각자 roundCount번씩 포즈 지정). 벽이 도달하는 순간 자신의 관절 좌표(어깨/팔꿈치/손목/
// 무릎, 엉덩이 중점 기준 몸통 길이로 정규화한 오프셋)가 캡처된 포즈와 관절별 오차
// poseMatchTolerance(기본 15%) 이내면 통과, 아니면 트랙에서 knockbackRatio만큼 밀려난다.
// roundCount 왕복 후 덜 밀려난 사람이 승리. 전체 진행은 순서가 있는 대기/판정의 연속이라
// 단일 코루틴(RunMatch)으로 턴을 몰고, Update()는 매 프레임 실루엣 포즈 반영만 담당한다.
using System.Collections;
using UnityEngine;

public class PoseCopyGame : MonoBehaviour
{
    public float poseHoldSeconds = 2f;
    public float poseMatchTolerance = 0.15f;
    public int roundCount = 3;
    public float knockbackRatio = 0.1f;
    public float wallApproachSeconds = 3f;
    public float trackLength = 4f;
    public float resultDisplaySeconds = 2f;

    private const float BaseP1X = -3f;
    private const float BaseP2X = 3f;

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _p1TrackPosition; // 0 = 원점, 커질수록 많이 밀려남
    private float _p2TrackPosition;
    private string _statusText = "";
    private float _wallCountdown;
    private bool _showCountdown;
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

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(BaseP1X, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(BaseP2X, 0f, 0f));

        BuildTrackVisual(BaseP1X, -1f);
        BuildTrackVisual(BaseP2X, 1f);

        StartCoroutine(RunMatch());
    }

    private void BuildTrackVisual(float baseX, float direction)
    {
        var go = new GameObject("Track");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSpriteFactory.CreateCapsule((int)(trackLength * 100), 20, new Color(0.5f, 0.45f, 0.4f));
        sr.transform.rotation = Quaternion.Euler(0, 0, 90);
        sr.transform.position = new Vector3(baseX + direction * trackLength * 0.5f, -1.3f, 0f);
        sr.sortingOrder = -2;
    }

    private CavemanSilhouette Spawn(PlayerId id, Vector3 pos)
    {
        var go = new GameObject($"Caveman_{id}");
        go.transform.position = pos;
        var s = go.AddComponent<CavemanSilhouette>();
        s.player = id;
        return s;
    }

    private PlayerPoseState State(PlayerId id) => PoseInputHub.Instance?.Get(id);

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        _p1Silhouette.ApplyPose(hub?.Get(PlayerId.P1));
        _p2Silhouette.ApplyPose(hub?.Get(PlayerId.P2));
    }

    private IEnumerator RunMatch()
    {
        for (int round = 0; round < roundCount; round++)
        {
            yield return RunOneDirection(PlayerId.P1, PlayerId.P2, round);
            if (_ended) yield break;
            yield return RunOneDirection(PlayerId.P2, PlayerId.P1, round);
            if (_ended) yield break;
        }

        PlayerId? winner = Mathf.Approximately(_p1TrackPosition, _p2TrackPosition) ? null
            : (_p1TrackPosition < _p2TrackPosition ? PlayerId.P1 : PlayerId.P2);
        EndMatch(winner);
    }

    private IEnumerator RunOneDirection(PlayerId poser, PlayerId copier, int roundIndex)
    {
        _statusText = $"{poser}가 포즈를 정하는 중... ({roundIndex + 1}/{roundCount})";
        Vector2[] captured = null;
        yield return CapturePose(poser, snap => captured = snap);

        _statusText = $"{copier}, 저 포즈를 따라하세요!";
        _showCountdown = true;
        float t = 0f;
        while (t < wallApproachSeconds)
        {
            t += Time.deltaTime;
            _wallCountdown = Mathf.Max(0f, wallApproachSeconds - t);
            yield return null;
        }
        _showCountdown = false;

        bool matched = false;
        PlayerPoseState copierState = State(copier);
        if (captured != null && copierState != null && copierState.IsTracked && copierState.Joints.TorsoLength > 0f)
        {
            Vector2[] current = NormalizedOffsets(copierState.Joints);
            float maxError = 0f;
            for (int i = 0; i < current.Length; i++)
                maxError = Mathf.Max(maxError, Vector2.Distance(captured[i], current[i]));
            matched = maxError <= poseMatchTolerance;
        }

        if (matched)
        {
            _statusText = $"{copier} 통과!";
        }
        else
        {
            _statusText = $"{copier} 벽에 부딪힘 - 밀려남!";
            Knockback(copier);
        }
        yield return new WaitForSeconds(0.8f);
    }

    // 캡처 대상 8관절(어깨/팔꿈치/손목/무릎, 좌우) - 엉덩이 중점 기준, 몸통 길이로 정규화한
    // 오프셋. poseHoldSeconds 동안 매 프레임 누적해서 평균낸 값을 최종 스냅샷으로 쓴다.
    private IEnumerator CapturePose(PlayerId who, System.Action<Vector2[]> onCaptured)
    {
        var sum = new Vector2[8];
        int frames = 0;
        float t = 0f;
        while (t < poseHoldSeconds)
        {
            t += Time.deltaTime;
            PlayerPoseState s = State(who);
            if (s != null && s.IsTracked && s.Joints.TorsoLength > 0f)
            {
                Vector2[] offsets = NormalizedOffsets(s.Joints);
                for (int i = 0; i < offsets.Length; i++) sum[i] += offsets[i];
                frames++;
            }
            yield return null;
        }

        var avg = new Vector2[8];
        for (int i = 0; i < avg.Length; i++) avg[i] = frames > 0 ? sum[i] / frames : Vector2.zero;
        onCaptured(avg);
    }

    private static Vector2[] NormalizedOffsets(JointSample j)
    {
        float torso = j.TorsoLength;
        Vector2 hip = j.HipMid;
        return new[]
        {
            (j.leftShoulder - hip) / torso, (j.rightShoulder - hip) / torso,
            (j.leftElbow - hip) / torso, (j.rightElbow - hip) / torso,
            (j.leftWrist - hip) / torso, (j.rightWrist - hip) / torso,
            (j.leftKnee - hip) / torso, (j.rightKnee - hip) / torso,
        };
    }

    private void Knockback(PlayerId who)
    {
        float amount = trackLength * knockbackRatio;
        if (who == PlayerId.P1)
        {
            _p1TrackPosition += amount;
            _p1Silhouette.transform.position = new Vector3(BaseP1X - _p1TrackPosition, 0f, 0f);
        }
        else
        {
            _p2TrackPosition += amount;
            _p2Silhouette.transform.position = new Vector3(BaseP2X + _p2TrackPosition, 0f, 0f);
        }
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        _statusText = winner == null ? "무승부!" : $"{winner} 승리!";
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
        GUI.Label(new Rect(20, 20, 400, 30), $"P1 밀림: {_p1TrackPosition:F2}   P2 밀림: {_p2TrackPosition:F2}");

        var style = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.UpperCenter };
        GUI.Label(new Rect(0, 60, Screen.width, 40), _statusText, style);

        if (_showCountdown)
            GUI.Label(new Rect(0, 100, Screen.width, 30), $"{_wallCountdown:F1}초 후 판정",
                new GUIStyle(style) { fontSize = 18 });
    }
}

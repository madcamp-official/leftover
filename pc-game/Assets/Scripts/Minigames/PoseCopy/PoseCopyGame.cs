// 자세 따라하기 (인간 테트리스) - 우가우가게임_기획_프롬프트.md "2. 자세 따라하기" 스펙.
//
// 규칙: "모션 정하기 턴"(poseHoldSeconds 동안 포즈 유지 -> 관절 오프셋 평균으로 캡처) /
// "플레이 턴"(wallApproachSeconds 후 상대의 현재 포즈와 비교)이 번갈아 진행, roundCount만큼
// 왕복(각자 roundCount번씩 포즈 지정). 벽이 도달하는 순간 자신의 관절 좌표(어깨/팔꿈치/손목/
// 무릎, 엉덩이 중점 기준 몸통 길이로 정규화한 오프셋)가 캡처된 포즈와 관절별 오차
// poseMatchTolerance(기본 15%) 이내면 통과, 아니면 트랙에서 knockbackRatio만큼 밀려난다.
// roundCount 왕복 후 덜 밀려난 사람이 승리. 전체 진행은 순서가 있는 대기/판정의 연속이라
// 단일 코루틴(RunMatch)으로 턴을 몰고, Update()는 매 프레임 실루엣 포즈 반영만 담당한다.
//
// 화면은 image/games/pose_match/의 실제 아트(강가 배경 + "남은 발판" 네임플레이트)로
// 구성한다. 실제 판정은 캡처된 포즈와의 관절 오차 비교 그대로지만, 시각적으로는
// obstacles/의 돌 벽 그림이 판정 직전 플레이어 쪽으로 다가왔다 사라지는 연출을 붙였다
// (6종을 라운드마다 돌아가며 사용 - 실제 캡처된 포즈 모양과 정확히 일치하진 않지만 "벽이
// 다가온다"는 느낌을 주는 장식용).
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
    public int maxFootholds = 4;

    private static readonly string[] PoseWalls =
    {
        "pose_wall_01_arms_up_v", "pose_wall_02_t_pose", "pose_wall_03_one_arm_up",
        "pose_wall_04_hands_on_waist", "pose_wall_05_leaning_side", "pose_wall_06_wide_squat",
    };

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private CavemanSilhouette p1Silhouette;
    [SerializeField] private CavemanSilhouette p2Silhouette;
    [SerializeField] private PoseMatchHud hud;
    private Vector3 _p1StartPosition;
    private Vector3 _p2StartPosition;
    private float _p1TrackPosition; // 0 = 원점, 커질수록 많이 밀려남
    private float _p2TrackPosition;
    private int _p1KnockbackCount;
    private int _p2KnockbackCount;
    private bool _ended;
    private int _wallCycle;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _p1StartPosition = p1Silhouette.transform.position;
        _p2StartPosition = p2Silhouette.transform.position;
        hud?.SetTimeRemaining(roundCount * 2 * (poseHoldSeconds + wallApproachSeconds + 0.8f));
        hud?.SetFootholds(PlayerId.P1, maxFootholds);
        hud?.SetFootholds(PlayerId.P2, maxFootholds);

        StartCoroutine(RunMatch());
    }

    private PlayerPoseState State(PlayerId id) => PoseInputHub.Instance?.Get(id);

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        p1Silhouette?.ApplyPose(hub?.Get(PlayerId.P1));
        p2Silhouette?.ApplyPose(hub?.Get(PlayerId.P2));
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
        hud?.ShowEvent($"{poser}가 포즈를 정하는 중... ({roundIndex + 1}/{roundCount})", poseHoldSeconds + 0.5f);
        Vector2[] captured = null;
        yield return CapturePose(poser, snap => captured = snap);

        hud?.ShowEvent($"{copier}, 저 포즈를 따라하세요!", wallApproachSeconds + 0.5f);
        GameObject wall = SpawnApproachingWall(copier);
        float t = 0f;
        while (t < wallApproachSeconds)
        {
            t += Time.deltaTime;
            if (wall != null)
                wall.transform.localPosition = Vector3.Lerp(WallStartLocalPos(copier), Vector3.zero, t / wallApproachSeconds);
            yield return null;
        }
        if (wall != null) Destroy(wall);

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
            hud?.ShowEvent($"{copier} 통과!");
        }
        else
        {
            hud?.ShowEvent($"{copier} 벽에 부딪힘 - 밀려남!");
            Knockback(copier);
        }
        yield return new WaitForSeconds(0.8f);
    }

    // 판정용 실제 캡처 모양과는 별개로, 그때그때 다가오는 돌벽 그림(장식용)을 카메라
    // 프레임 밖에서 copier 쪽으로 슬라이드시킨다.
    private GameObject SpawnApproachingWall(PlayerId copier)
    {
        Sprite sprite = ArtAssets.LoadPoseMatch(PoseWalls[_wallCycle % PoseWalls.Length]);
        _wallCycle++;
        if (sprite == null) return null;

        CavemanSilhouette target = copier == PlayerId.P1 ? p1Silhouette : p2Silhouette;
        var go = new GameObject("ApproachingWall");
        go.transform.SetParent(target.transform, false);
        go.transform.localPosition = WallStartLocalPos(copier);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -1;
        ArtAssets.FitWidth(sr, 1.4f);
        return go;
    }

    private static Vector3 WallStartLocalPos(PlayerId copier) =>
        new Vector3(copier == PlayerId.P1 ? -3.5f : 3.5f, 1f, 0f);

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
            _p1KnockbackCount++;
            p1Silhouette.transform.position = _p1StartPosition + Vector3.left * _p1TrackPosition;
            hud?.SetFootholds(PlayerId.P1, Mathf.Max(0, maxFootholds - _p1KnockbackCount));
        }
        else
        {
            _p2TrackPosition += amount;
            _p2KnockbackCount++;
            p2Silhouette.transform.position = _p2StartPosition + Vector3.right * _p2TrackPosition;
            hud?.SetFootholds(PlayerId.P2, Mathf.Max(0, maxFootholds - _p2KnockbackCount));
        }
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

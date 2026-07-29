// 돌던지기 - docs/minigames/01_돌던지기.md 스펙 (기관총식 연속 발사, 체력 10 즉사룰).
//
// 규칙: 어느 손이든 들려 있는 동안 fireIntervalSeconds(기본 1초)마다 자동으로 돌이 발사된다.
// 발사 순간 오른손이 들려 있으면 상대의 오른쪽을, 왼손이 들려 있으면 상대의 왼쪽을 조준한다.
// 양손이 동시에 들려 있으면 "더 먼저 들기 시작한 손" 쪽을 조준한다(오른손 우선이 아니라
// 실제로 먼저 든 손) - _p1RightRaisedAt류 필드로 각 손이 마지막으로 "들리기 시작한" 시각을
// 기록해두고 비교한다. 상대는 그 순간 머리를 좌/우로 기울여 회피 - 조준 방향과 상대의
// 기울기 방향이 "같으면" 회피, "다르면"(또는 상대가 기울이지 않고 있으면) 명중.
//
// 체력: 각자 maxHealth(기본 10)에서 시작, 맞을 때마다 1씩 깎인다. 0이 되는 즉시 그 라운드는
// 끝나고 상대가 승리(제한시간이 안 끝났어도). 제한시간이 다 되면 그 시점에 체력이 더 많이
// 남은(=더 적게 맞은) 사람이 승리.
//
// 화면은 image/games/stone_throw/의 실제 아트로 구성한다 - 배경/플레이어별 "맞은 돌"
// 네임플레이트와 image/common/ui/hud/의 남은 시간판을 사용한다(HUD 조립은 StoneThrowHud 참고).
// 이전 v1 아트에 있던 각도/파워/바람 HUD는 건바운드류 포격 조작을 전제로 그려진 것이라 실제
// 조작(손 들기 자동발사 + 머리 기울이기 회피)과 맞지 않아 쓰지 않았고, v2에서는 아예 빠졌다.
using System.Collections;
using UnityEngine;

public class StoneThrowGame : MonoBehaviour
{
    public float fireIntervalSeconds = 1f;
    public float matchSeconds = 30f;
    public float dodgeTiltThreshold = 0.12f;
    public float stoneTravelSeconds = 0.25f;
    public float resultDisplaySeconds = 2f;
    public float stoneWidth = 0.3f; // 인게임에서 보일 돌 가로 폭(월드 유닛)
    public int maxHealth = 10;      // 이 횟수만큼 맞으면 즉시 패배

    private enum Side { Left, Right }

    private Sprite _stoneSprite;
    [Header("씬에 배치된 오브젝트")]
    [Tooltip("오른쪽 절반 중앙의 P1 앞모습(피격 표정/돌 목표점)")]
    [SerializeField] private CavemanSilhouette p1FrontSilhouette;
    [Tooltip("왼쪽 절반 하단 전경의 P1 뒷모습(돌 발사점)")]
    [SerializeField] private CavemanSilhouette p1BackSilhouette;
    [Tooltip("왼쪽 절반 중앙의 P2 앞모습(피격 표정/돌 목표점)")]
    [SerializeField] private CavemanSilhouette p2FrontSilhouette;
    [Tooltip("오른쪽 절반 하단 전경의 P2 뒷모습(돌 발사점)")]
    [SerializeField] private CavemanSilhouette p2BackSilhouette;
    [SerializeField] private StoneThrowHud hud;
    private float _elapsed;
    private float _p1FireTimer;
    private float _p2FireTimer;

    // 손을 "들기 시작한 시각"(Time.time). 안 들려 있으면 -1. 양손이 동시에 들려 있을 때
    // 어느 손을 우선할지(더 먼저 든 쪽) 비교하는 데 쓴다.
    private float _p1RightRaisedAt = -1f, _p1LeftRaisedAt = -1f;
    private float _p2RightRaisedAt = -1f, _p2LeftRaisedAt = -1f;

    private int _p1Hits;
    private int _p2Hits;
    private bool _ended;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _stoneSprite = ArtAssets.LoadProp("stone");

        hud?.SetTimeRemaining(matchSeconds);
        hud?.SetHealth(PlayerId.P1, 1f);
        hud?.SetHealth(PlayerId.P2, 1f);
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        ApplyPoseToBothViews(p1FrontSilhouette, p1BackSilhouette, p1);
        ApplyPoseToBothViews(p2FrontSilhouette, p2BackSilhouette, p2);

        if (_ended) return;

        _elapsed += Time.deltaTime;

        TickFiring(PlayerId.P1, p1, p2, ref _p1FireTimer, ref _p1RightRaisedAt, ref _p1LeftRaisedAt);
        if (!_ended)
            TickFiring(PlayerId.P2, p2, p1, ref _p2FireTimer, ref _p2RightRaisedAt, ref _p2LeftRaisedAt);

        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));

        if (_elapsed >= matchSeconds)
        {
            // 체력이 더 많이 남은(=더 적게 맞은) 쪽이 승리. 맞은 횟수가 같으면 무승부.
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits < _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void TickFiring(PlayerId thrower, PlayerPoseState throwerState, PlayerPoseState targetState,
        ref float timer, ref float rightRaisedAt, ref float leftRaisedAt)
    {
        if (throwerState == null || !throwerState.IsTracked)
        {
            timer = 0f;
            rightRaisedAt = leftRaisedAt = -1f;
            return;
        }

        bool rightRaised = throwerState.IsHandRaised(rightHand: true);
        bool leftRaised = throwerState.IsHandRaised(rightHand: false);

        // 손이 "새로 들리기 시작한" 프레임에만 시각을 기록한다 - 이미 들려 있는 동안 매
        // 프레임 갱신하면 "먼저 든 손" 비교가 항상 무승부가 되어버린다.
        if (rightRaised && rightRaisedAt < 0f) rightRaisedAt = Time.time;
        else if (!rightRaised) rightRaisedAt = -1f;
        if (leftRaised && leftRaisedAt < 0f) leftRaisedAt = Time.time;
        else if (!leftRaised) leftRaisedAt = -1f;

        if (!rightRaised && !leftRaised)
        {
            timer = 0f; // 손을 내리면 발사 대기를 취소 (다시 들었을 때 처음부터 한 박자 기다림)
            return;
        }

        timer += Time.deltaTime;
        if (timer < fireIntervalSeconds) return;
        timer = 0f;

        // 양손이 동시에 들려 있으면 더 먼저 들기 시작한 손을 우선한다(둘 다 이번 프레임에
        // 동시에 들리기 시작했다면 시각이 같으므로 오른손으로 정함 - 극히 드문 동시 입력).
        Side aimSide;
        if (rightRaised && leftRaised)
            aimSide = leftRaisedAt < rightRaisedAt ? Side.Left : Side.Right;
        else
            aimSide = rightRaised ? Side.Right : Side.Left;

        bool hit = true;
        if (targetState != null && targetState.IsTracked)
        {
            float tilt = targetState.HeadTiltRatio();
            Side? targetTiltSide = tilt > dodgeTiltThreshold ? Side.Right
                : (tilt < -dodgeTiltThreshold ? Side.Left : (Side?)null);
            hit = targetTiltSide != aimSide;
        }

        PlayerId target = thrower == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
        if (hit)
        {
            int targetHitCount;
            if (thrower == PlayerId.P1) { _p1Hits++; targetHitCount = _p1Hits; }
            else { _p2Hits++; targetHitCount = _p2Hits; }

            // HUD의 "맞은 돌"은 각자 플레이트에 자신이 맞은 횟수를 보여준다(누가 맞혔는지가
            // 아니라 누가 맞았는지) - target의 받은 횟수는 곧 thrower가 지금까지 맞힌 횟수와
            // 같으므로 targetHitCount를 그대로 target 쪽 플레이트에 표시한다.
            hud?.SetHits(target, targetHitCount);
            hud?.SetHealth(target, 1f - (float)targetHitCount / maxHealth);
            hud?.ShowEvent($"{Label(thrower)} 명중!");
            StartCoroutine(ReactToHit(target, targetHitCount));

            // 체력이 0이 되면 제한시간과 무관하게 그 즉시 승부가 끝난다. 날아가는 돌 연출은
            // 아래 공통 코드에서 그대로 처리되므로 여기서는 승패만 확정한다.
            if (targetHitCount >= maxHealth)
                EndMatch(thrower);
        }
        else
        {
            hud?.ShowEvent($"{Label(target)} 회피!");
        }

        CavemanSilhouette throwerBack = BackSilhouette(thrower);
        CavemanSilhouette targetFront = FrontSilhouette(target);
        if (throwerBack != null && targetFront != null)
        {
            Vector3 from = throwerBack.transform.position + Vector3.up * 1.65f;
            Vector3 to = targetFront.transform.position + Vector3.up * 1.35f;
            StartCoroutine(FlyStone(from, to, hit));
        }
    }

    private static void ApplyPoseToBothViews(CavemanSilhouette front, CavemanSilhouette back, PlayerPoseState state)
    {
        front?.ApplyPose(state);
        back?.ApplyPose(state);
    }

    private CavemanSilhouette FrontSilhouette(PlayerId id)
        => id == PlayerId.P1 ? p1FrontSilhouette : p2FrontSilhouette;

    private CavemanSilhouette BackSilhouette(PlayerId id)
        => id == PlayerId.P1 ? p1BackSilhouette : p2BackSilhouette;

    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";

    // 맞은 쪽 표정을 잠깐 바꿔준다 - 많이 맞을수록 이빨이 더 나간 얼굴로.
    private IEnumerator ReactToHit(PlayerId target, int timesHit)
    {
        CavemanSilhouette sil = FrontSilhouette(target);
        if (sil == null) yield break;
        string face = timesHit >= 6 ? "face_stone_hit_two_teeth_broken"
            : timesHit >= 3 ? "face_stone_hit_one_tooth_broken"
            : "face_grimacing";
        sil.SetFace(face);
        yield return new WaitForSeconds(0.8f);
        sil.ResetFace();
    }

    private IEnumerator FlyStone(Vector3 from, Vector3 to, bool hit)
    {
        var go = new GameObject("Stone");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _stoneSprite;
        sr.sortingOrder = 5;
        go.transform.position = from;
        ArtAssets.FitWidth(sr, stoneWidth);

        // 명중이면 상대 위치에서 멈추고, 회피면 그대로 조금 더 지나쳐서 빗나간 느낌을 준다.
        Vector3 end = hit ? to : to + (to - from).normalized * 0.6f;
        float t = 0f;
        while (t < stoneTravelSeconds)
        {
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(from, end, t / stoneTravelSeconds);
            go.transform.Rotate(0f, 0f, 540f * Time.deltaTime);
            yield return null;
        }
        Destroy(go);
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.SetTimeRemaining(0f);
        hud?.ShowEvent(winner == null ? "무승부!" : $"{Label(winner.Value)} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

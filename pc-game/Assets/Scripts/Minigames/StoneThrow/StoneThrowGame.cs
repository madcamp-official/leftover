// 돌던지기 - docs/minigames/01_돌던지기.md 기준 구현.
// 고정 주기 발사 이벤트에서 한 손만 든 경우에만 6컷 애니메이션을 재생한다.
using System.Collections;
using UnityEngine;

public class StoneThrowGame : MonoBehaviour
{
    [Header("게임 규칙")]
    [Min(0.1f)] public float fireIntervalSeconds = 1f;
    [Min(1f)] public float matchSeconds = 30f;
    [Min(0f)] public float headSideThreshold = 0.12f;
    [Min(0f)] public float headSideHoldSeconds = 0.1f;
    [Min(0.05f)] public float stoneTravelSeconds = 0.25f;
    [Min(0.05f)] public float throwAnimationSeconds = 0.6f;
    [Min(0f)] public float hitFaceDisplaySeconds = 0.7f;
    [Min(0f)] public float resultDisplaySeconds = 2f;

    [Header("돌 원근 크기 (월드 가로 폭)")]
    [Min(0.01f)] public float nearStoneWidth = 0.52f;
    [Min(0.01f)] public float farStoneWidth = 0.16f;

    [Header("씬에 배치된 캐릭터 뷰")]
    [SerializeField] private StoneThrowCharacterView p1FrontView;
    [SerializeField] private StoneThrowCharacterView p1BackView;
    [SerializeField] private StoneThrowCharacterView p2FrontView;
    [SerializeField] private StoneThrowCharacterView p2BackView;

    [Header("씬에 배치된 투사체/HUD")]
    [SerializeField] private SpriteRenderer stoneTemplate;
    [SerializeField] private Transform projectileContainer;
    [SerializeField] private StoneThrowHud hud;

    private float _elapsed;
    private float _p1FireTimer;
    private float _p2FireTimer;
    private int _p1Hits;
    private int _p2Hits;
    private bool _ended;

    private StoneThrowSide _p1Side = StoneThrowSide.Left;
    private StoneThrowSide _p2Side = StoneThrowSide.Right;
    private StoneThrowSide? _p1Candidate;
    private StoneThrowSide? _p2Candidate;
    private float _p1CandidateSeconds;
    private float _p2CandidateSeconds;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        if (stoneTemplate != null) stoneTemplate.gameObject.SetActive(false);
        ApplySide(PlayerId.P1, _p1Side);
        ApplySide(PlayerId.P2, _p2Side);
        hud?.SetHits(PlayerId.P1, 0);
        hud?.SetHits(PlayerId.P2, 0);
        hud?.SetTimeRemaining(matchSeconds);
    }

    private void Update()
    {
        if (_ended) return;

        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        UpdateSide(PlayerId.P1, p1, ref _p1Side, ref _p1Candidate, ref _p1CandidateSeconds);
        UpdateSide(PlayerId.P2, p2, ref _p2Side, ref _p2Candidate, ref _p2CandidateSeconds);

        _elapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
            return;
        }

        // 손 상태와 무관하게 양쪽 이벤트 시계는 항상 흐른다.
        _p1FireTimer += Time.deltaTime;
        _p2FireTimer += Time.deltaTime;
        while (_p1FireTimer >= fireIntervalSeconds)
        {
            _p1FireTimer -= fireIntervalSeconds;
            TriggerFireEvent(PlayerId.P1, p1, _p2Side);
        }
        while (_p2FireTimer >= fireIntervalSeconds)
        {
            _p2FireTimer -= fireIntervalSeconds;
            TriggerFireEvent(PlayerId.P2, p2, _p1Side);
        }
    }

    private void UpdateSide(PlayerId player, PlayerPoseState state, ref StoneThrowSide current,
        ref StoneThrowSide? candidate, ref float candidateSeconds)
    {
        if (state == null || !state.IsTracked) return;
        float tilt = state.HeadTiltRatio();
        StoneThrowSide? observed = tilt > headSideThreshold ? StoneThrowSide.Right
            : tilt < -headSideThreshold ? StoneThrowSide.Left : null;
        if (!observed.HasValue)
        {
            candidate = null;
            candidateSeconds = 0f;
            return; // 중립은 Center가 아니라 마지막 좌/우를 유지한다.
        }
        if (observed.Value == current)
        {
            candidate = null;
            candidateSeconds = 0f;
            return;
        }
        if (candidate != observed)
        {
            candidate = observed;
            candidateSeconds = 0f;
        }
        candidateSeconds += Time.deltaTime;
        if (candidateSeconds < headSideHoldSeconds) return;
        current = observed.Value;
        candidate = null;
        candidateSeconds = 0f;
        ApplySide(player, current);
    }

    private void ApplySide(PlayerId player, StoneThrowSide side)
    {
        FrontView(player)?.SetSide(side);
        BackView(player)?.SetSide(side);
    }

    private void TriggerFireEvent(PlayerId thrower, PlayerPoseState state, StoneThrowSide targetSide)
    {
        if (state == null || !state.IsTracked)
        {
            hud?.ShowEvent($"{Label(thrower)} 추적 대기");
            return;
        }
        bool right = state.IsHandRaised(rightHand: true);
        bool left = state.IsHandRaised(rightHand: false);
        if (right == left)
        {
            hud?.ShowEvent(right ? $"{Label(thrower)} 양손은 무효!" : $"{Label(thrower)} 한 손을 드세요!");
            return;
        }

        StoneThrowHand hand = right ? StoneThrowHand.Right : StoneThrowHand.Left;
        StoneThrowSide aimSide = right ? StoneThrowSide.Right : StoneThrowSide.Left;
        bool hit = aimSide == targetSide;
        StartCoroutine(PlayThrow(thrower, hand, aimSide, hit));
    }

    private IEnumerator PlayThrow(PlayerId thrower, StoneThrowHand hand, StoneThrowSide aimSide, bool hit)
    {
        StoneThrowCharacterView front = FrontView(thrower);
        StoneThrowCharacterView back = BackView(thrower);
        int frameCount = Mathf.Max(front?.FrameCount(hand) ?? 0, back?.FrameCount(hand) ?? 0);
        if (frameCount == 0) yield break;

        float frameSeconds = throwAnimationSeconds / frameCount;
        for (int frame = 0; frame < frameCount; frame++)
        {
            front?.ShowThrowFrame(hand, frame);
            back?.ShowThrowFrame(hand, frame);
            if (frame == 3) ReleaseStones(thrower, hand, aimSide, hit);
            yield return new WaitForSeconds(frameSeconds);
        }
        front?.ShowIdle();
        back?.ShowIdle();
    }

    private void ReleaseStones(PlayerId thrower, StoneThrowHand hand, StoneThrowSide aimSide, bool hit)
    {
        PlayerId target = thrower == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
        StoneThrowCharacterView throwerBack = BackView(thrower);
        StoneThrowCharacterView throwerFront = FrontView(thrower);
        StoneThrowCharacterView targetFront = FrontView(target);
        StoneThrowCharacterView targetBack = BackView(target);

        // 각 플레이어 화면에서 같은 사건을 동시에 본다.
        if (throwerBack != null && targetFront != null)
            StartCoroutine(FlyStone(throwerBack.ReleasePosition(hand), targetFront.TargetPosition(aimSide),
                nearStoneWidth, farStoneWidth));
        if (throwerFront != null && targetBack != null)
            StartCoroutine(FlyStone(throwerFront.ReleasePosition(hand), targetBack.TargetPosition(aimSide),
                farStoneWidth, nearStoneWidth));

        StartCoroutine(ResolveThrowOutcome(thrower, target, hit));
    }

    private IEnumerator ResolveThrowOutcome(PlayerId thrower, PlayerId target, bool hit)
    {
        yield return new WaitForSeconds(stoneTravelSeconds);
        if (!hit)
        {
            hud?.ShowEvent($"{Label(target)} 회피!");
            yield break;
        }
        if (thrower == PlayerId.P1) _p1Hits++; else _p2Hits++;
        int receivedHits = thrower == PlayerId.P1 ? _p1Hits : _p2Hits;
        hud?.SetHits(target, receivedHits);
        hud?.ShowEvent($"{Label(thrower)} 명중!");
        FrontView(target)?.ShowHitFace(hitFaceDisplaySeconds);
    }

    private IEnumerator FlyStone(Vector3 from, Vector3 to, float startWidth, float endWidth)
    {
        if (stoneTemplate == null) yield break;
        SpriteRenderer stone = Instantiate(stoneTemplate, projectileContainer);
        stone.name = "FlyingStone";
        stone.gameObject.SetActive(true);
        stone.transform.position = from;
        FitStoneWidth(stone, startWidth);
        float elapsed = 0f;
        while (elapsed < stoneTravelSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stoneTravelSeconds);
            stone.transform.position = Vector3.Lerp(from, to, t);
            FitStoneWidth(stone, Mathf.Lerp(startWidth, endWidth, t));
            stone.transform.Rotate(0f, 0f, 540f * Time.deltaTime);
            yield return null;
        }
        Destroy(stone.gameObject);
    }

    private static void FitStoneWidth(SpriteRenderer stone, float width)
    {
        if (stone == null || stone.sprite == null) return;
        float nativeWidth = stone.sprite.bounds.size.x;
        if (nativeWidth > 0f) stone.transform.localScale = Vector3.one * (width / nativeWidth);
    }

    private StoneThrowCharacterView FrontView(PlayerId id) => id == PlayerId.P1 ? p1FrontView : p2FrontView;
    private StoneThrowCharacterView BackView(PlayerId id) => id == PlayerId.P1 ? p1BackView : p2BackView;
    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        StopAllCoroutines();
        if (projectileContainer != null)
        {
            foreach (Transform child in projectileContainer)
                if (stoneTemplate == null || child.gameObject != stoneTemplate.gameObject)
                    Destroy(child.gameObject);
        }
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

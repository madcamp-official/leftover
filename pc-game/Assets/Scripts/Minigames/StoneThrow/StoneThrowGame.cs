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
    [Min(0.05f)] public float stoneTravelSeconds = 1f;
    [Min(0.05f)] public float throwAnimationSeconds = 0.6f;
    [Min(0f)] public float hitReactionDisplaySeconds = 0.7f;
    [Min(0f)] public float resultDisplaySeconds = 2f;

    [Header("돌 원근 크기 (월드 가로 폭)")]
    [Min(0.01f)] public float nearStoneWidth = 2.6f;
    [Min(0.01f)] public float farStoneWidth = 0.25f;

    [Header("돌 회전")]
    [Min(0f)] public float stoneSpinDegreesPerSecond = 1440f;

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

    // --- 호스트-클라이언트 배선 (docs/멀티플레이_분산_아키텍처_설계.md 4장, 다른 미니게임과
    // 동일 패턴) - 판정 임계값/타이밍/이징은 전부 그대로다. 상대 상태 비교가 들어가는
    // 명중 판정(ResolveThrowOutcome)도 호스트 전담이고, 클라이언트는 그 결과만 받아 재생한다.
    private bool _clientStarted;
    private float _clientElapsed;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();
        if (stoneTemplate != null) stoneTemplate.gameObject.SetActive(false);
        ApplySide(PlayerId.P1, _p1Side);
        ApplySide(PlayerId.P2, _p2Side);
        hud?.SetHits(PlayerId.P1, 0);
        hud?.SetHits(PlayerId.P2, 0);
        hud?.SetTimeRemaining(matchSeconds);

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            net.Subscribe("st_started", OnNetStarted);
            net.Subscribe("st_side", OnNetSide);
            net.Subscribe("st_throw", OnNetThrow);
            net.Subscribe("st_result", OnNetResult);
            net.Subscribe("st_ended", OnNetEnded);
        }
        else if (net != null && net.IsHost)
        {
            net.Send("st_started", new StartedPayload());
        }
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            UpdateAsClient();
            return;
        }

        if (_ended) return;

        // 카메라 준비 여부는 로딩 단계에서만 확인한다. 경기가 시작된 뒤에는 추적이
        // 끊겨도 타이머와 기존 투사체/명중 판정은 계속 진행한다.
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
            TriggerFireEvent(PlayerId.P1, p1);
        }
        while (_p2FireTimer >= fireIntervalSeconds)
        {
            _p2FireTimer -= fireIntervalSeconds;
            TriggerFireEvent(PlayerId.P2, p2);
        }
    }

    // 클라이언트는 판정을 하지 않는다 - 손 방향/발사/명중 결과는 전부 이벤트 핸들러가
    // 담당하고, 여기서는 타이머 표시만 로컬에서 흘려보낸다.
    private void UpdateAsClient()
    {
        if (_ended || !_clientStarted) return;
        _clientElapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _clientElapsed));
    }

    private void OnNetStarted(NetworkEvent evt)
    {
        _clientStarted = true;
        _clientElapsed = 0f;
    }

    private void OnNetSide(NetworkEvent evt)
    {
        SidePayload payload = NetworkSession.Read<SidePayload>(evt);
        PlayerId player = DecodePlayer(payload.player);
        StoneThrowSide side = payload.side == 1 ? StoneThrowSide.Right : StoneThrowSide.Left;
        if (player == PlayerId.P1) _p1Side = side; else _p2Side = side;
        ApplySide(player, side);
    }

    private void OnNetThrow(NetworkEvent evt)
    {
        ThrowPayload payload = NetworkSession.Read<ThrowPayload>(evt);
        ExecuteThrow(DecodePlayer(payload.thrower), payload.hand == 1 ? StoneThrowHand.Right : StoneThrowHand.Left);
    }

    private void OnNetResult(NetworkEvent evt)
    {
        ResultPayload payload = NetworkSession.Read<ResultPayload>(evt);
        ApplyThrowOutcome(DecodePlayer(payload.thrower), DecodePlayer(payload.target), payload.hit);
    }

    private void OnNetEnded(NetworkEvent evt)
    {
        EndedPayload payload = NetworkSession.Read<EndedPayload>(evt);
        _ended = true;
        PlayerId? winner = DecodeWinner(payload.winner);
        hud?.ShowEvent(winner == null ? "무승부!" : $"{Label(winner.Value)} 승리!", resultDisplaySeconds);
    }

    private static int EncodePlayer(PlayerId player) => player == PlayerId.P1 ? 0 : 1;
    private static PlayerId DecodePlayer(int value) => value == 0 ? PlayerId.P1 : PlayerId.P2;
    private static int EncodeWinner(PlayerId? winner) => winner == PlayerId.P1 ? 0 : winner == PlayerId.P2 ? 1 : -1;
    private static PlayerId? DecodeWinner(int value) => value == 0 ? PlayerId.P1 : value == 1 ? PlayerId.P2 : (PlayerId?)null;

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

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("st_side", new SidePayload { player = EncodePlayer(player), side = side == StoneThrowSide.Right ? 1 : 0 });
    }

    private void TriggerFireEvent(PlayerId thrower, PlayerPoseState state)
    {
        // 발사 이벤트 순간 현재 카메라에서 한 손만 인식됐을 때만 던진다.
        // 미추적/양손/무손 상태는 단순히 이번 이벤트를 건너뛰며 경기는 멈추지 않는다.
        if (!TryGetRecognizedThrowHand(state, out StoneThrowHand hand)) return;
        ExecuteThrow(thrower, hand);
    }

    // 실제 판정(TriggerFireEvent, 호스트 전용)과 클라이언트가 st_throw 이벤트를 받아 재생할
    // 때(OnNetThrow) 양쪽이 공유하는 진입점 - 던질 손이 이미 정해진 뒤의 연출/좌표 계산은
    // 씬 앵커 기준 순수 기하 계산이라(포즈 입력과 무관) 호스트/클라이언트가 각자 로컬에서
    // 똑같이 계산해도 결과가 같다. 그래서 좌표를 네트워크로 보내지 않고 (thrower, hand)만
    // 보낸다.
    private void ExecuteThrow(PlayerId thrower, StoneThrowHand hand)
    {
        // 던지는 사람 화면의 오른쪽은 마주 보는 상대의 왼쪽이다.
        // 반대로 던지는 사람 화면의 왼쪽은 상대의 오른쪽이다.
        StoneThrowSide aimedTargetSide = hand == StoneThrowHand.Right
            ? StoneThrowSide.Left
            : StoneThrowSide.Right;
        PlayerId target = thrower == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
        StoneThrowCharacterView throwerFront = FrontView(thrower);
        StoneThrowCharacterView throwerBack = BackView(thrower);

        // 발사 이벤트 순간 선택된 손 위치와 그 손이 조준한 상대 기준 좌/우 목표 좌표를
        // 잠근다. 상대가 어디에 서 있는지는 목표를 정하지 않고 명중 여부에만 사용한다.
        Vector3 lockedFrontRelease = throwerFront != null
            ? throwerFront.ReleasePosition(hand) : Vector3.zero;
        Vector3 lockedBackRelease = throwerBack != null
            ? throwerBack.ReleasePosition(hand) : Vector3.zero;
        Vector3 lockedFrontTarget = FrontView(target) != null
            ? FrontView(target).TargetPosition(aimedTargetSide) : Vector3.zero;
        Vector3 lockedBackTarget = BackView(target) != null
            ? BackView(target).TargetPosition(aimedTargetSide) : Vector3.zero;
        StartCoroutine(PlayThrow(thrower, hand, aimedTargetSide,
            lockedFrontRelease, lockedBackRelease, lockedFrontTarget, lockedBackTarget));

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("st_throw", new ThrowPayload { thrower = EncodePlayer(thrower), hand = hand == StoneThrowHand.Right ? 1 : 0 });
    }

    private static bool TryGetRecognizedThrowHand(PlayerPoseState state, out StoneThrowHand hand)
    {
        hand = default;
        if (state == null || !state.IsTracked) return false;

        bool right = state.IsHandRaised(rightHand: true);
        bool left = state.IsHandRaised(rightHand: false);
        if (right == left) return false;

        hand = right ? StoneThrowHand.Right : StoneThrowHand.Left;
        return true;
    }

    private IEnumerator PlayThrow(PlayerId thrower, StoneThrowHand hand, StoneThrowSide lockedTargetSide,
        Vector3 lockedFrontRelease, Vector3 lockedBackRelease,
        Vector3 lockedFrontTarget, Vector3 lockedBackTarget)
    {
        StoneThrowCharacterView front = FrontView(thrower);
        StoneThrowCharacterView back = BackView(thrower);
        int frameCount = Mathf.Max(front?.FrameCount(hand) ?? 0, back?.FrameCount(hand) ?? 0);
        if (frameCount == 0) yield break;

        GameSfx.Play("throw");
        float frameSeconds = throwAnimationSeconds / frameCount;
        for (int frame = 0; frame < frameCount; frame++)
        {
            front?.ShowThrowFrame(hand, frame);
            back?.ShowThrowFrame(hand, frame);
            if (frame == 3)
                ReleaseStones(thrower, lockedTargetSide, lockedFrontRelease, lockedBackRelease,
                    lockedFrontTarget, lockedBackTarget);
            yield return new WaitForSeconds(frameSeconds);
        }
        front?.ShowIdle();
        back?.ShowIdle();
    }

    private void ReleaseStones(PlayerId thrower, StoneThrowSide lockedTargetSide,
        Vector3 lockedFrontRelease, Vector3 lockedBackRelease,
        Vector3 lockedFrontTarget, Vector3 lockedBackTarget)
    {
        PlayerId target = thrower == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
        StoneThrowCharacterView throwerBack = BackView(thrower);
        StoneThrowCharacterView throwerFront = FrontView(thrower);
        StoneThrowCharacterView targetFront = FrontView(target);
        StoneThrowCharacterView targetBack = BackView(target);

        // 각 플레이어 화면에서 같은 사건을 동시에 본다. 피격 판정은 실제 돌 하나가
        // 끝점에 도착한 프레임에 연결해서 이동 시간과 표정 타이밍이 어긋나지 않게 한다.
        bool outcomeBoundToStone = false;
        if (throwerBack != null && targetFront != null)
        {
            StartCoroutine(FlyStone(lockedBackRelease, lockedFrontTarget,
                nearStoneWidth, farStoneWidth,
                () => ResolveThrowOutcome(thrower, target, lockedTargetSide)));
            outcomeBoundToStone = true;
        }
        if (throwerFront != null && targetBack != null)
        {
            StartCoroutine(FlyStone(lockedFrontRelease, lockedBackTarget,
                farStoneWidth, nearStoneWidth,
                outcomeBoundToStone ? null : () => ResolveThrowOutcome(thrower, target, lockedTargetSide)));
            outcomeBoundToStone = true;
        }

        if (!outcomeBoundToStone)
            StartCoroutine(ResolveThrowOutcomeAfterDelay(thrower, target, lockedTargetSide));
    }

    private IEnumerator ResolveThrowOutcomeAfterDelay(PlayerId thrower, PlayerId target,
        StoneThrowSide lockedTargetSide)
    {
        yield return new WaitForSeconds(stoneTravelSeconds);
        ResolveThrowOutcome(thrower, target, lockedTargetSide);
    }

    private void ResolveThrowOutcome(PlayerId thrower, PlayerId target, StoneThrowSide lockedTargetSide)
    {
        // 판정은 호스트 전담이다 - 클라이언트도 이 함수를 호출하긴 하지만(자기 화면의 돌이
        // 도착하는 시점), CurrentSide(target)는 st_side 이벤트로 받은 값이라 호스트가 판정한
        // 그 순간과 미세하게 다를 수 있다. 그래서 클라이언트는 여기서 다시 판정하지 않고
        // st_result 이벤트로 받은 결과(OnNetResult → ApplyThrowOutcome)만 그대로 재생한다.
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient) return;

        // 도착 순간에도 처음 조준한 위치에 남아 있어야 명중한다.
        bool hit = CurrentSide(target) == lockedTargetSide;
        ApplyThrowOutcome(thrower, target, hit);

        if (net != null && net.IsHost)
            net.Send("st_result", new ResultPayload
            {
                thrower = EncodePlayer(thrower),
                target = EncodePlayer(target),
                hit = hit,
            });
    }

    private void ApplyThrowOutcome(PlayerId thrower, PlayerId target, bool hit)
    {
        if (!hit)
        {
            hud?.ShowEvent($"{Label(target)} 회피!");
            return;
        }
        if (thrower == PlayerId.P1) _p1Hits++; else _p2Hits++;
        GameSfx.Play("hit_pan");
        GameSfx.Play("hit_cry");
        int receivedHits = thrower == PlayerId.P1 ? _p1Hits : _p2Hits;
        hud?.SetHits(target, receivedHits);
        hud?.ShowEvent($"{Label(thrower)} 명중!");
        FrontView(target)?.ShowHitFullBody(hitReactionDisplaySeconds);
    }

    private IEnumerator FlyStone(Vector3 from, Vector3 to, float startWidth, float endWidth,
        System.Action onArrived = null)
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
            stone.transform.Rotate(0f, 0f, stoneSpinDegreesPerSecond * Time.deltaTime);
            yield return null;
        }
        stone.transform.position = to;
        FitStoneWidth(stone, endWidth);
        onArrived?.Invoke();
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
    private StoneThrowSide CurrentSide(PlayerId id) => id == PlayerId.P1 ? _p1Side : _p2Side;
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

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("st_ended", new EndedPayload { winner = EncodeWinner(winner) });
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    [System.Serializable] private class StartedPayload { }
    [System.Serializable] private class SidePayload { public int player; public int side; }
    [System.Serializable] private class ThrowPayload { public int thrower; public int hand; }
    [System.Serializable] private class ResultPayload { public int thrower; public int target; public bool hit; }
    [System.Serializable] private class EndedPayload { public int winner; }
}

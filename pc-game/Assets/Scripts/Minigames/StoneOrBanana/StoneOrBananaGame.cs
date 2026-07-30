// 돌 or 바나나 - docs/minigames/05_돌or바나나.md 기준 구현.
// 3초 동안 던지는 손과 받는 입 상태를 서로 바꾸며 심리전을 하고, 종료 순간의 상태를 잠근다.
using System.Collections;
using UnityEngine;

public class StoneOrBananaGame : MonoBehaviour
{
    private enum ThrowKind { Stone, Banana }
    private enum TurnPhase { Decision, Resolving, Ended }

    [Header("게임 규칙")]
    [Min(1f)] public float maxFullness = 5f;
    [Min(1f)] public float maxTeeth = 3f;
    [Min(.1f)] public float turnDecisionSeconds = 3f;
    [Min(.05f)] public float throwAnimationSeconds = .6f;
    [Min(.05f)] public float throwTravelSeconds = .35f;
    [Min(0f)] public float reactionDisplaySeconds = .8f;
    [Min(0f)] public float resultDisplaySeconds = 2f;

    [Header("투사체 원근 크기 (월드 가로 폭)")]
    [Min(.01f)] public float nearStoneWidth = 1.8f;
    [Min(.01f)] public float farStoneWidth = .24f;
    [Min(.01f)] public float nearBananaWidth = 2.1f;
    [Min(.01f)] public float farBananaWidth = .3f;

    [Header("투사체 회전")]
    [Min(0f)] public float projectileSpinDegreesPerSecond = 1440f;

    [Header("씬에 배치된 캐릭터 뷰")]
    [SerializeField] private StoneOrBananaCharacterView p1FrontView;
    [SerializeField] private StoneOrBananaCharacterView p1BackView;
    [SerializeField] private StoneOrBananaCharacterView p2FrontView;
    [SerializeField] private StoneOrBananaCharacterView p2BackView;

    [Header("씬에 배치된 투사체/HUD")]
    [SerializeField] private SpriteRenderer stoneTemplate;
    [SerializeField] private SpriteRenderer bananaTemplate;
    [SerializeField] private Transform projectileContainer;
    [SerializeField] private StoneOrBananaHud hud;

    private int _p1Fullness, _p2Fullness;
    private int _p1Teeth, _p2Teeth;
    private PlayerId _currentThrower = PlayerId.P1;
    private TurnPhase _phase;
    private float _turnElapsed;

    // --- 호스트-클라이언트 배선 (docs/멀티플레이_분산_아키텍처_설계.md 4장) - 판정 임계값/
    // 타이밍/점수는 전부 그대로다. 이 게임은 "결정 구간(3초) 동안 상대가 지금 뭘 들고
    // 있는지/입을 벌렸는지가 실시간으로 보여야 심리전이 성립"하므로, 그 구간만 원시 pose
    // 스트림과 같은 성격(최신 값이 진실)으로 매 프레임 릴레이한다(sb_decision). 턴이 잠기는
    // 순간(sb_locked)부터는 이후 전개(PlayLockedTurn - 포만감/이빨/승패 전부 포함)가 완전히
    // 결정론적이라 호스트와 클라이언트가 같은 함수를 그대로 재생하면 자동으로 똑같은 결과가
    // 나온다 - 그래서 승패/턴 순서를 위한 별도 이벤트가 필요 없다.
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();
        GameBootstrap.EnsureLetterbox();
        if (stoneTemplate != null) stoneTemplate.gameObject.SetActive(false);
        if (bananaTemplate != null) bananaTemplate.gameObject.SetActive(false);
        _p1Teeth = _p2Teeth = Mathf.RoundToInt(maxTeeth);
        RefreshStatusHud();

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            net.Subscribe("sb_decision", OnNetDecision);
            net.Subscribe("sb_locked", OnNetLocked);
        }

        BeginTurn();
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            // 클라이언트는 판정을 하지 않는다 - 손/입 표시는 sb_decision 이벤트가, 턴 잠금
            // 이후 전개는 sb_locked 이벤트(OnNetLocked)가 담당한다. 여기서는 결정 구간
            // 타이머 표시만 로컬에서 흘려보낸다.
            if (_phase == TurnPhase.Decision)
            {
                _turnElapsed += Time.deltaTime;
                hud?.SetTurnTimeRemaining(Mathf.Max(0f, turnDecisionSeconds - _turnElapsed));
            }
            return;
        }

        if (_phase != TurnPhase.Decision) return;

        PlayerId receiver = Other(_currentThrower);
        StoneThrowHand heldHand = ReadThrowHand(State(_currentThrower));
        bool mouthOpen = ReadMouthOpen(State(receiver));
        FrontView(_currentThrower)?.ShowHeldHand(heldHand);
        BackView(_currentThrower)?.ShowHeldHand(heldHand);
        FrontView(receiver)?.ShowReceiver(mouthOpen);
        BackView(receiver)?.ShowReceiver(mouthOpen);

        if (net != null && net.IsHost)
            net.Send("sb_decision", new DecisionPayload
            {
                hand = heldHand == StoneThrowHand.Right ? 1 : 0,
                mouthOpen = mouthOpen,
            });

        _turnElapsed += Time.deltaTime;
        hud?.SetTurnTimeRemaining(Mathf.Max(0f, turnDecisionSeconds - _turnElapsed));
        if (_turnElapsed < turnDecisionSeconds) return;

        // 종료 프레임의 손과 입을 여기서 한 번만 읽고 이후 입력 변화는 무시한다.
        StoneThrowHand lockedHand = ReadThrowHand(State(_currentThrower));
        bool lockedMouthOpen = ReadMouthOpen(State(receiver));
        _phase = TurnPhase.Resolving;
        hud?.ShowDecisionPrompts(false);
        hud?.SetTurnTimeRemaining(0f);

        if (net != null && net.IsHost)
            net.Send("sb_locked", new LockedPayload
            {
                thrower = EncodePlayer(_currentThrower),
                hand = lockedHand == StoneThrowHand.Right ? 1 : 0,
                mouthOpen = lockedMouthOpen,
            });

        StartCoroutine(PlayLockedTurn(_currentThrower, receiver, lockedHand, lockedMouthOpen));
    }

    private void OnNetDecision(NetworkEvent evt)
    {
        DecisionPayload payload = NetworkSession.Read<DecisionPayload>(evt);
        PlayerId thrower = _currentThrower;
        PlayerId receiver = Other(thrower);
        StoneThrowHand hand = payload.hand == 1 ? StoneThrowHand.Right : StoneThrowHand.Left;
        FrontView(thrower)?.ShowHeldHand(hand);
        BackView(thrower)?.ShowHeldHand(hand);
        FrontView(receiver)?.ShowReceiver(payload.mouthOpen);
        BackView(receiver)?.ShowReceiver(payload.mouthOpen);
    }

    private void OnNetLocked(NetworkEvent evt)
    {
        LockedPayload payload = NetworkSession.Read<LockedPayload>(evt);
        PlayerId thrower = DecodePlayer(payload.thrower);
        PlayerId receiver = Other(thrower);
        StoneThrowHand hand = payload.hand == 1 ? StoneThrowHand.Right : StoneThrowHand.Left;
        _phase = TurnPhase.Resolving;
        hud?.ShowDecisionPrompts(false);
        hud?.SetTurnTimeRemaining(0f);
        StartCoroutine(PlayLockedTurn(thrower, receiver, hand, payload.mouthOpen));
    }

    private static int EncodePlayer(PlayerId player) => player == PlayerId.P1 ? 0 : 1;
    private static PlayerId DecodePlayer(int value) => value == 0 ? PlayerId.P1 : PlayerId.P2;

    private void BeginTurn()
    {
        _turnElapsed = 0f;
        _phase = TurnPhase.Decision;
        hud?.SetTurnTimeRemaining(turnDecisionSeconds);
        hud?.SetTurnRoles(_currentThrower, Other(_currentThrower));
        hud?.ShowDecisionPrompts(true);
    }

    private IEnumerator PlayLockedTurn(PlayerId thrower, PlayerId receiver, StoneThrowHand hand,
        bool receiverMouthOpen)
    {
        ThrowKind kind = hand == StoneThrowHand.Left ? ThrowKind.Banana : ThrowKind.Stone;
        StoneOrBananaCharacterView throwerFront = FrontView(thrower);
        StoneOrBananaCharacterView throwerBack = BackView(thrower);
        StoneOrBananaCharacterView receiverFront = FrontView(receiver);
        StoneOrBananaCharacterView receiverBack = BackView(receiver);

        Vector3 backStart = throwerBack != null ? throwerBack.ReleasePosition(hand) : Vector3.zero;
        Vector3 frontStart = throwerFront != null ? throwerFront.ReleasePosition(hand) : Vector3.zero;
        Vector3 frontEnd = receiverFront != null ? receiverFront.ReceivePosition() : Vector3.zero;
        Vector3 backEnd = receiverBack != null ? receiverBack.ReceivePosition() : Vector3.zero;
        Vector3 frontReturnEnd = throwerFront != null ? throwerFront.ReceivePosition() : Vector3.zero;
        Vector3 backReturnEnd = throwerBack != null ? throwerBack.ReceivePosition() : Vector3.zero;

        GameSfx.Play("throw");
        bool projectileArrived = false;
        int frameCount = Mathf.Max(throwerFront?.FrameCount(hand) ?? 0, throwerBack?.FrameCount(hand) ?? 0);
        if (frameCount <= 0) frameCount = 1;
        float frameSeconds = throwAnimationSeconds / frameCount;
        for (int frame = 0; frame < frameCount; frame++)
        {
            throwerFront?.ShowThrowFrame(hand, frame);
            throwerBack?.ShowThrowFrame(hand, frame);
            if (frame == Mathf.Min(3, frameCount - 1))
            {
                StartCoroutine(FlyProjectile(kind, frontStart, backEnd,
                    FarWidth(kind), NearWidth(kind)));
                StartCoroutine(FlyProjectile(kind, backStart, frontEnd,
                    NearWidth(kind), FarWidth(kind), () => projectileArrived = true));
            }
            yield return new WaitForSeconds(frameSeconds);
        }
        while (!projectileArrived) yield return null;

        if (kind == ThrowKind.Banana && !receiverMouthOpen)
        {
            hud?.ShowEvent("바나나가 부메랑으로 돌아갑니다!");
            throwerFront?.ShowReceiver(true);
            throwerBack?.ShowReceiver(true);
            bool boomerangArrived = false;
            StartCoroutine(FlyProjectile(kind, backEnd, frontReturnEnd,
                NearWidth(kind), FarWidth(kind)));
            StartCoroutine(FlyProjectile(kind, frontEnd, backReturnEnd,
                FarWidth(kind), NearWidth(kind), () => boomerangArrived = true));
            while (!boomerangArrived) yield return null;

            AddFullness(thrower);
            throwerFront?.ShowBananaChewing();
            GameSfx.Play("gulp");
            hud?.ShowEvent($"{Label(thrower)} 포만감 +1");
            yield return new WaitForSeconds(reactionDisplaySeconds);
        }
        else if (kind == ThrowKind.Banana)
        {
            AddFullness(receiver);
            receiverFront?.ShowBananaChewing();
            GameSfx.Play("gulp");
            hud?.ShowEvent($"{Label(receiver)} 바나나 먹기! 포만감 +1");
            yield return new WaitForSeconds(reactionDisplaySeconds);
        }
        else if (receiverMouthOpen)
        {
            int lostTeeth = RemoveTooth(receiver);
            receiverFront?.ShowStoneHit(lostTeeth);
            GameSfx.Play("window_break");
            hud?.ShowEvent($"{Label(receiver)} 돌을 먹었습니다! 이빨 -1");
            yield return new WaitForSeconds(reactionDisplaySeconds);
        }
        else
        {
            hud?.ShowEvent($"{Label(receiver)} 돌 피하기!");
            yield return new WaitForSeconds(.35f);
        }

        if (TryGetWinner(out PlayerId winner))
        {
            EndMatch(winner);
            yield break;
        }

        _currentThrower = Other(_currentThrower);
        BeginTurn();
    }

    private IEnumerator FlyProjectile(ThrowKind kind, Vector3 from, Vector3 to,
        float startWidth, float endWidth, System.Action onArrived = null)
    {
        SpriteRenderer template = kind == ThrowKind.Stone ? stoneTemplate : bananaTemplate;
        if (template == null)
        {
            onArrived?.Invoke();
            yield break;
        }

        SpriteRenderer projectile = Instantiate(template, projectileContainer);
        projectile.name = kind == ThrowKind.Stone ? "FlyingStone" : "FlyingBanana";
        projectile.gameObject.SetActive(true);
        projectile.transform.position = from;
        FitWidth(projectile, startWidth);
        float elapsed = 0f;
        while (elapsed < throwTravelSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / throwTravelSeconds);
            projectile.transform.position = Vector3.Lerp(from, to, t);
            FitWidth(projectile, Mathf.Lerp(startWidth, endWidth, t));
            projectile.transform.Rotate(0f, 0f, projectileSpinDegreesPerSecond * Time.deltaTime);
            yield return null;
        }
        projectile.transform.position = to;
        FitWidth(projectile, endWidth);
        onArrived?.Invoke();
        Destroy(projectile.gameObject);
    }

    private static void FitWidth(SpriteRenderer renderer, float width)
    {
        if (renderer == null || renderer.sprite == null) return;
        float nativeWidth = renderer.sprite.bounds.size.x;
        if (nativeWidth > 0f) renderer.transform.localScale = Vector3.one * (width / nativeWidth);
    }

    // 왼손만 단독으로 들었을 때만 바나나. 양손/오른손/무입력/미추적은 모두 돌이다.
    private static StoneThrowHand ReadThrowHand(PlayerPoseState state)
    {
        if (state == null || !state.IsTracked) return StoneThrowHand.Right;
        bool left = state.IsHandRaised(rightHand: false);
        bool right = state.IsHandRaised(rightHand: true);
        return left && !right ? StoneThrowHand.Left : StoneThrowHand.Right;
    }

    private static bool ReadMouthOpen(PlayerPoseState state)
        => state != null && state.IsTracked && state.IsMouthOpen();

    private void AddFullness(PlayerId player)
    {
        int maximum = Mathf.RoundToInt(maxFullness);
        if (player == PlayerId.P1) _p1Fullness = Mathf.Min(maximum, _p1Fullness + 1);
        else _p2Fullness = Mathf.Min(maximum, _p2Fullness + 1);
        RefreshStatusHud();
    }

    private int RemoveTooth(PlayerId player)
    {
        int maximum = Mathf.RoundToInt(maxTeeth);
        if (player == PlayerId.P1) _p1Teeth = Mathf.Max(0, _p1Teeth - 1);
        else _p2Teeth = Mathf.Max(0, _p2Teeth - 1);
        RefreshStatusHud();
        int remaining = player == PlayerId.P1 ? _p1Teeth : _p2Teeth;
        return maximum - remaining;
    }

    private bool TryGetWinner(out PlayerId winner)
    {
        if (_p1Teeth <= 0) { winner = PlayerId.P2; return true; }
        if (_p2Teeth <= 0) { winner = PlayerId.P1; return true; }
        if (_p1Fullness >= Mathf.RoundToInt(maxFullness)) { winner = PlayerId.P1; return true; }
        if (_p2Fullness >= Mathf.RoundToInt(maxFullness)) { winner = PlayerId.P2; return true; }
        winner = default;
        return false;
    }

    private void RefreshStatusHud()
    {
        hud?.SetStatus(PlayerId.P1, _p1Teeth, Mathf.RoundToInt(maxTeeth),
            _p1Fullness, Mathf.RoundToInt(maxFullness));
        hud?.SetStatus(PlayerId.P2, _p2Teeth, Mathf.RoundToInt(maxTeeth),
            _p2Fullness, Mathf.RoundToInt(maxFullness));
    }

    private float NearWidth(ThrowKind kind) => kind == ThrowKind.Stone ? nearStoneWidth : nearBananaWidth;
    private float FarWidth(ThrowKind kind) => kind == ThrowKind.Stone ? farStoneWidth : farBananaWidth;
    private PlayerPoseState State(PlayerId id) => PoseInputHub.Instance?.Get(id);
    private StoneOrBananaCharacterView FrontView(PlayerId id) => id == PlayerId.P1 ? p1FrontView : p2FrontView;
    private StoneOrBananaCharacterView BackView(PlayerId id) => id == PlayerId.P1 ? p1BackView : p2BackView;
    private static PlayerId Other(PlayerId id) => id == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";

    private void EndMatch(PlayerId winner)
    {
        _phase = TurnPhase.Ended;
        hud?.ShowDecisionPrompts(false);
        hud?.ShowEvent($"{Label(winner)} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    [System.Serializable] private class DecisionPayload { public int hand; public bool mouthOpen; }
    [System.Serializable] private class LockedPayload { public int thrower; public int hand; public bool mouthOpen; }
}

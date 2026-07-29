// 머리로 코코넛 깨기 (반복 속도 경쟁) - 우가우가게임_기획_프롬프트.md "4. 머리로 코코넛
// 깨기" 스펙.
//
// 규칙 요약 (구현됨):
//   - PoseInputHub의 Get(player).HandToHeadDistance()가 hitDistance 이하로 좁혀졌다가
//     releaseDistance 이상으로 다시 벌어지는 한 사이클을 "1회 타격"으로 카운트.
//   - matchSeconds(기본 15초) 동안 더 많이 타격한 사람이 승리 (내구도/단계 없음, 순수
//     반복 속도 경쟁).
//
// 캐릭터 표현: 관절 리깅(CavemanSilhouette)은 이제 안 쓴다 - coconut_1~N 프레임 시퀀스를
// CoconutCharacterFrames(Assets/Scripts/Common/)가 씬에 미리 배치된 SpriteRenderer 하나의
// 스프라이트만 갈아끼우는 방식으로 재생한다. FrameAnimatedCharacter(런타임에 새 GameObject를
// 만드는 방식)와 달리 이 게임은 Play를 누르지 않아도 씬을 열면 캐릭터가 바로 보인다.
// p1View/p2View는 씬에 이미 있는 그 컴포넌트를 드래그해서 연결하기만 하면 된다. 코코넛이
// 각 컷(프레임)에서 손에 들려 있어야 할 자리도 좌표 계산이 아니라 CoconutCharacterFrames의
// coconutAnchors(씬에 직접 배치하는 Transform들)로 정한다. 손-머리 거리 판정 자체는
// PlayerPoseState.HandToHeadDistance()로 원시 트래킹 값을 직접 쓰므로 캐릭터 표현 방식과
// 무관하다.
using System.Collections;
using UnityEngine;

public class CoconutCrackGame : MonoBehaviour
{
    public float matchSeconds = 15f;
    public float hitDistance = 0.25f;     // 몸통 길이 대비, 이 이하면 "타격"
    public float releaseDistance = 0.45f; // 이 이상으로 벌어져야 다음 타격을 셀 준비 완료
    public float releaseHoldSeconds = 0.15f; // 손을 충분히 내린 상태를 이 시간만큼 유지해야 재장전
    public float releaseLoweredRatio = 0.15f; // 손이 어깨보다 몸통 길이의 이 비율만큼 아래여야 내려온 것으로 판정
    public float animationSmoothing = 12f; // MediaPipe 좌표 떨림이 프레임 왕복으로 보이지 않게 하는 속도
    public float resultDisplaySeconds = 2f;
    public float coconutWidth = 0.5f; // 인게임에서 보일 코코넛 가로 폭(월드 유닛)
    public float breakAnimSeconds = 0.25f; // 정점에서 코코넛이 쪼개져 날아가는 연출 시간
    public float breakArcHeight = 2.2f; // 깨진 조각이 포물선 정점에서 추가로 올라가는 높이(월드 유닛)
    public float breakSpinDegrees = 1080f; // 날아가는 동안 각 조각이 회전하는 총 각도
    public int breakSourceFrame = 5; // 깨진 조각이 출발할 coconut_N 프레임 번호(1부터 시작)
    public Vector2 p1CoconutWorldOffset = Vector2.zero; // 앵커 위치에 적용하는 최종 월드 좌표 보정
    public Vector2 p2CoconutWorldOffset = Vector2.zero;
    // 쪼개진 반쪽이 날아가는 거리(월드 유닛) - 왼쪽은 (-x,-y), 오른쪽은 (x,-y) 방향으로.
    // 예전엔 (0.3, 0.15)처럼 너무 작아서 "그 자리에서 쪼그라들며 사라지는" 것처럼 보였다.
    public Vector2 breakFlyDistance = new Vector2(3f, 1.5f);

    private Sprite _coconutBreakLeftSprite;
    private Sprite _coconutBreakRightSprite;

    private sealed class CoconutEffect
    {
        public PlayerId Player;
        public bool IsBusy;
        public bool AwaitingRelease;
        public SpriteRenderer Left;
        public SpriteRenderer Right;
    }

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private CoconutCharacterFrames p1View;
    [SerializeField] private CoconutCharacterFrames p2View;
    [SerializeField] private SpriteRenderer p1Coconut;
    [SerializeField] private SpriteRenderer p2Coconut;
    [SerializeField] private CoconutBreakHud hud;
    private CoconutEffect _p1Effect;
    private CoconutEffect _p2Effect;
    private float _elapsed;
    private int _p1Hits;
    private int _p2Hits;
    private bool _p1ReadyToHit = true;
    private bool _p2ReadyToHit = true;
    private float _p1ReleaseHeld;
    private float _p2ReleaseHeld;
    private float _p1AnimationProgress;
    private float _p2AnimationProgress;
    private bool _ended;

    // --- 호스트-클라이언트 배선 (docs/멀티플레이_분산_아키텍처_설계.md 4장) ---
    // 판정 임계값/타이밍/연출 수치는 전부 그대로다. 손-머리 거리로 캐릭터 프레임이 실시간
    // 연속으로 움직이므로(DriveGestureAnimation) 그 진행률은 원시 pose 스트림과 같은 성격
    // (최신 값이 진실, 유실 허용)으로 릴레이하고, 판정 자체는 이산 이벤트(cc_hit/cc_release)로
    // 보낸다.
    //
    // 경과 시간도 이 주기 메시지에 실어 보낸다 - 예전에는 "시작했다"는 일회성 이벤트로
    // 클라이언트 타이머를 켰지만, 호스트가 먼저 씬을 로드하고 클라이언트에 load_round를
    // 보내는 구조라 클라이언트 씬은 항상 나중에 로드된다. 그래서 일회성 시작 이벤트는
    // 구독 전에 지나가버려 클라이언트 타이머가 영원히 멈춰 있었다(실측 확인된 버그).
    private const float StateSendInterval = 0.05f; // 20Hz - 매 프레임(60Hz×2건) 보내면 메인 스레드 TCP 쓰기가 과도하다
    private float _lastStateSentAt;
    private bool _clientHasState;
    private float _clientElapsed;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();

        if (p1View == null || p2View == null || p1Coconut == null || p2Coconut == null)
        {
            Debug.LogError($"{nameof(CoconutCrackGame)}: P1/P2 View와 Coconut 참조를 모두 연결해야 합니다.", this);
            enabled = false;
            return;
        }

        _coconutBreakLeftSprite = ArtAssets.LoadProp("coconut_break_left");
        _coconutBreakRightSprite = ArtAssets.LoadProp("coconut_break_right");
        _p1Effect = CreateEffect(PlayerId.P1);
        _p2Effect = CreateEffect(PlayerId.P2);

        hud?.SetTimeRemaining(matchSeconds);

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            net.Subscribe("cc_state", OnNetState);
            net.Subscribe("cc_hit", OnNetHit);
            net.Subscribe("cc_release", OnNetRelease);
            net.Subscribe("cc_ended", OnNetEnded);
        }
    }

    // 씬이 바뀌어도 NetworkSession은 살아있으므로, 구독을 해제하지 않으면 파괴된 이 오브젝트를
    // 가리키는 핸들러가 계속 쌓여 다음 라운드에 중복 호출/예외가 난다.
    private void OnDestroy()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;
        net.Unsubscribe("cc_state", OnNetState);
        net.Unsubscribe("cc_hit", OnNetHit);
        net.Unsubscribe("cc_release", OnNetRelease);
        net.Unsubscribe("cc_ended", OnNetEnded);
    }

    private void Update()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            UpdateAsClient();
            return;
        }

        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);

        // 고정 시간 재생 대신 실제 손-머리 거리로 프레임 진행률을 결정한다. 손을 올리면
        // 정방향, 내리면 역방향으로 재생되므로 사람의 실제 한 사이클과 애니메이션이 일치한다.
        DriveGestureAnimation(p1, p1View, ref _p1AnimationProgress);
        DriveGestureAnimation(p2, p2View, ref _p2AnimationProgress);

        if (net != null && net.IsHost && Time.unscaledTime - _lastStateSentAt >= StateSendInterval)
        {
            _lastStateSentAt = Time.unscaledTime;
            net.Send("cc_state", new StatePayload
            {
                p1Progress = _p1AnimationProgress,
                p2Progress = _p2AnimationProgress,
                elapsed = _elapsed,
            });
        }

        // 코코넛이 고정 위치가 아니라 지금 재생 중인 컷(coconut_N)에 맞춰 씬에 배치해 둔
        // coconutAnchors 자리를 따라다니게 한다(CoconutCharacterFrames.CoconutAnchorWorld).
        if (p1Coconut != null && p1View != null)
            p1Coconut.transform.position = p1View.CoconutAnchorWorld(p1View.CurrentIndex) + (Vector3)p1CoconutWorldOffset;
        if (p2Coconut != null && p2View != null)
            p2Coconut.transform.position = p2View.CoconutAnchorWorld(p2View.CurrentIndex) + (Vector3)p2CoconutWorldOffset;

        if (_ended) return;

        CountHits(PlayerId.P1, p1, ref _p1ReadyToHit, ref _p1ReleaseHeld, ref _p1Hits, p1Coconut, p1View, _p1Effect);
        CountHits(PlayerId.P2, p2, ref _p2ReadyToHit, ref _p2ReleaseHeld, ref _p2Hits, p2Coconut, p2View, _p2Effect);

        _elapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    // 클라이언트는 판정을 하지 않는다 - 코코넛 위치는 순수 렌더링 상태(view.CurrentIndex)만
    // 있으면 계산되므로 호스트와 동일 코드로 매 프레임 갱신하고, 나머지(점수/타격/타이머
    // 시작)는 전부 이벤트 핸들러가 담당한다.
    private void UpdateAsClient()
    {
        if (p1Coconut != null && p1View != null)
            p1Coconut.transform.position = p1View.CoconutAnchorWorld(p1View.CurrentIndex) + (Vector3)p1CoconutWorldOffset;
        if (p2Coconut != null && p2View != null)
            p2Coconut.transform.position = p2View.CoconutAnchorWorld(p2View.CurrentIndex) + (Vector3)p2CoconutWorldOffset;

        if (_ended || !_clientHasState) return;
        // 호스트가 보낸 경과 시간을 기준으로 하되, 메시지 사이(20Hz)는 로컬에서 이어 세서
        // 타이머 표시가 끊겨 보이지 않게 한다.
        _clientElapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _clientElapsed));
    }

    private void OnNetState(NetworkEvent evt)
    {
        StatePayload payload = NetworkSession.Read<StatePayload>(evt);
        _clientHasState = true;
        _clientElapsed = payload.elapsed; // 호스트 값이 권위 - 로컬 누적 오차를 매번 보정한다
        ApplyGesture(p1View, payload.p1Progress);
        ApplyGesture(p2View, payload.p2Progress);
    }

    private static void ApplyGesture(CoconutCharacterFrames view, float progress)
    {
        if (view == null || view.FrameCount <= 0) return;
        view.ShowFrame(Mathf.RoundToInt(Mathf.Clamp01(progress) * (view.FrameCount - 1)));
    }

    private void OnNetHit(NetworkEvent evt)
    {
        PlayerEventPayload payload = NetworkSession.Read<PlayerEventPayload>(evt);
        if (payload.player == 0) ApplyHit(PlayerId.P1, ref _p1Hits, p1Coconut, p1View, _p1Effect);
        else ApplyHit(PlayerId.P2, ref _p2Hits, p2Coconut, p2View, _p2Effect);
    }

    private void OnNetRelease(NetworkEvent evt)
    {
        PlayerEventPayload payload = NetworkSession.Read<PlayerEventPayload>(evt);
        RespawnCoconut(payload.player == 0 ? p1Coconut : p2Coconut, payload.player == 0 ? _p1Effect : _p2Effect);
    }

    private void OnNetEnded(NetworkEvent evt)
    {
        EndedPayload payload = NetworkSession.Read<EndedPayload>(evt);
        _ended = true;
        PlayerId? winner = DecodeWinner(payload.winner);
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
    }

    private void DriveGestureAnimation(PlayerPoseState state, CoconutCharacterFrames view, ref float smoothedProgress)
    {
        if (view == null || view.FrameCount <= 0) return;
        if (state == null || !state.IsTracked)
        {
            smoothedProgress = 0f;
            view.ShowFrame(0);
            return;
        }

        float distance = state.HandToHeadDistance();
        float targetProgress = Mathf.Clamp01(
            (releaseDistance - distance) / (releaseDistance - hitDistance)
        );
        float blend = 1f - Mathf.Exp(-animationSmoothing * Time.deltaTime);
        smoothedProgress = Mathf.Lerp(smoothedProgress, targetProgress, blend);
        int index = Mathf.RoundToInt(smoothedProgress * (view.FrameCount - 1));
        view.ShowFrame(index);
    }

    private void CountHits(PlayerId id, PlayerPoseState state, ref bool readyToHit, ref float releaseHeld,
        ref int hitCount, SpriteRenderer coconut, CoconutCharacterFrames view, CoconutEffect effect)
    {
        if (state == null || !state.IsTracked)
        {
            releaseHeld = 0f;
            return;
        }

        float distance = state.HandToHeadDistance();

        if (readyToHit && distance <= hitDistance)
        {
            readyToHit = false;
            releaseHeld = 0f;
            ApplyHit(id, ref hitCount, coconut, view, effect);
        }
        else if (!readyToHit)
        {
            bool returnedToRest = distance >= releaseDistance && AreHandsLowered(state);
            releaseHeld = returnedToRest ? releaseHeld + Time.deltaTime : 0f;

            if (releaseHeld >= releaseHoldSeconds)
            {
                readyToHit = true;
                releaseHeld = 0f;
                RespawnCoconut(coconut, effect);
            }
        }
    }

    // 타격이 확정되는 지점(실제 판정 - CountHits, 또는 클라이언트가 cc_hit 이벤트를 받아
    // 재생할 때 - OnNetHit) 양쪽이 공유하는 유일한 진입점. 여기서 갈리지 않게 해서 두 경로가
    // 항상 똑같은 점수/HUD/연출을 만들도록 한다.
    private void ApplyHit(PlayerId id, ref int hitCount, SpriteRenderer coconut, CoconutCharacterFrames view, CoconutEffect effect)
    {
        hitCount++;
        hud?.SetHits(id, hitCount);
        TryBreakCoconut(coconut, view, effect);
    }

    private bool AreHandsLowered(PlayerPoseState state)
    {
        float torso = state.Joints.TorsoLength;
        if (torso <= 0f) return false;

        float handMidY = (state.Joints.leftWrist.y + state.Joints.rightWrist.y) * 0.5f;
        float shoulderMidY = state.Joints.ShoulderMid.y;
        return (handMidY - shoulderMidY) / torso >= releaseLoweredRatio;
    }

    // 손이 머리에 도달한 정점에서 즉시 코코넛을 깨고, 다음 코코넛은 손이 다시
    // releaseDistance까지 내려왔을 때 준비한다. 따라서 생성 주기도 고정 시간이 아니라
    // 플레이어가 실제로 손을 올렸다 내리는 속도를 따른다.
    private void TryBreakCoconut(SpriteRenderer coconut, CoconutCharacterFrames view, CoconutEffect effect)
    {
        if (coconut == null || effect == null) return;
        float sourceWorldWidth = GetSpriteWorldWidth(coconut);
        Vector3 breakStartPosition = view != null
            ? view.CoconutAnchorWorld(breakSourceFrame - 1)
            : coconut.transform.position;
        coconut.enabled = false;
        effect.AwaitingRelease = true;

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("cc_hit", new PlayerEventPayload { player = effect.Player == PlayerId.P1 ? 0 : 1 });

        if (effect.IsBusy) return;

        effect.IsBusy = true;
        GameSfx.Play("coconut_break");
        StartCoroutine(PunchCoconut(coconut, effect, sourceWorldWidth, breakStartPosition));
    }

    private static void RespawnCoconut(SpriteRenderer coconut, CoconutEffect effect)
    {
        if (coconut == null || effect == null || !effect.AwaitingRelease) return;
        effect.AwaitingRelease = false;
        coconut.enabled = true;

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("cc_release", new PlayerEventPayload { player = effect.Player == PlayerId.P1 ? 0 : 1 });
    }

    // 정점에서 시작된 깨진 조각 연출만 짧은 고정 시간을 쓴다. 캐릭터의 올림/내림 속도와
    // 다음 코코넛 준비 시점은 모두 위의 실제 손 거리로 결정된다.
    private IEnumerator PunchCoconut(SpriteRenderer coconut, CoconutEffect effect, float sourceWorldWidth,
        Vector3 startPos)
    {
        if (coconut == null)
        {
            effect.IsBusy = false;
            yield break;
        }

        PrepareHalf(effect.Left, startPos, sourceWorldWidth);
        PrepareHalf(effect.Right, startPos, sourceWorldWidth);

        float duration = breakAnimSeconds;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float arcY = 4f * breakArcHeight * p * (1f - p);
            float fallY = -breakFlyDistance.y * p;

            effect.Left.transform.position = startPos +
                new Vector3(-breakFlyDistance.x * p, fallY + arcY, 0f);
            effect.Left.transform.rotation = Quaternion.Euler(0f, 0f, breakSpinDegrees * p);
            effect.Right.transform.position = startPos +
                new Vector3(breakFlyDistance.x * p, fallY + arcY, 0f);
            effect.Right.transform.rotation = Quaternion.Euler(0f, 0f, -breakSpinDegrees * p);

            // 궤적 대부분은 선명하게 보여주고 마지막 35%에서만 사라지게 한다.
            float alpha = 1f - Mathf.InverseLerp(0.65f, 1f, p);
            SetAlpha(effect.Left, alpha);
            SetAlpha(effect.Right, alpha);
            yield return null;
        }

        SetEffectVisible(effect, false);
        effect.IsBusy = false;
    }

    private CoconutEffect CreateEffect(PlayerId player)
    {
        string playerName = player == PlayerId.P1 ? "P1" : "P2";
        return new CoconutEffect
        {
            Player = player,
            Left = CreateHalfRenderer($"{playerName}_CoconutHalf_Left", _coconutBreakLeftSprite),
            Right = CreateHalfRenderer($"{playerName}_CoconutHalf_Right", _coconutBreakRightSprite),
        };
    }

    private SpriteRenderer CreateHalfRenderer(string objectName, Sprite sprite)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 4;
        ArtAssets.FitWidth(sr, coconutWidth);
        sr.enabled = false;
        return sr;
    }

    private static void PrepareHalf(SpriteRenderer half, Vector3 worldPosition, float targetWorldWidth)
    {
        half.transform.position = worldPosition;
        half.transform.rotation = Quaternion.identity;
        FitWorldWidth(half, targetWorldWidth);
        SetAlpha(half, 1f);
        half.enabled = half.sprite != null;
    }

    private static float GetSpriteWorldWidth(SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null) return 0f;
        return renderer.sprite.bounds.size.x * Mathf.Abs(renderer.transform.lossyScale.x);
    }

    private static void FitWorldWidth(SpriteRenderer renderer, float targetWorldWidth)
    {
        if (renderer == null || renderer.sprite == null || targetWorldWidth <= 0f) return;

        float nativeWidth = renderer.sprite.bounds.size.x;
        float parentScaleX = renderer.transform.parent != null
            ? Mathf.Abs(renderer.transform.parent.lossyScale.x)
            : 1f;
        if (nativeWidth <= 0f || parentScaleX <= 0.0001f) return;

        float scale = targetWorldWidth / (nativeWidth * parentScaleX);
        renderer.transform.localScale = Vector3.one * scale;
    }

    private static void SetEffectVisible(CoconutEffect effect, bool visible)
    {
        if (effect?.Left != null) effect.Left.enabled = visible && effect.Left.sprite != null;
        if (effect?.Right != null) effect.Right.enabled = visible && effect.Right.sprite != null;
    }

    private static void SetAlpha(SpriteRenderer sr, float alpha)
    {
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("cc_ended", new EndedPayload { winner = EncodeWinner(winner) });
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    private static int EncodeWinner(PlayerId? winner) => winner == PlayerId.P1 ? 0 : winner == PlayerId.P2 ? 1 : -1;
    private static PlayerId? DecodeWinner(int value) => value == 0 ? PlayerId.P1 : value == 1 ? PlayerId.P2 : (PlayerId?)null;

    [System.Serializable] private class PlayerEventPayload { public int player; }
    [System.Serializable] private class EndedPayload { public int winner; }

    [System.Serializable]
    private class StatePayload
    {
        public float p1Progress, p2Progress;
        public float elapsed;
    }

    private void OnValidate()
    {
        matchSeconds = Mathf.Max(0.1f, matchSeconds);
        resultDisplaySeconds = Mathf.Max(0f, resultDisplaySeconds);
        breakAnimSeconds = Mathf.Max(0.01f, breakAnimSeconds);
        coconutWidth = Mathf.Max(0.01f, coconutWidth);
        hitDistance = Mathf.Max(0f, hitDistance);
        releaseDistance = Mathf.Max(hitDistance + 0.01f, releaseDistance);
        releaseHoldSeconds = Mathf.Max(0f, releaseHoldSeconds);
        releaseLoweredRatio = Mathf.Max(0f, releaseLoweredRatio);
        animationSmoothing = Mathf.Max(0.01f, animationSmoothing);
        breakArcHeight = Mathf.Max(0f, breakArcHeight);
        breakSpinDegrees = Mathf.Max(0f, breakSpinDegrees);
        breakSourceFrame = Mathf.Max(1, breakSourceFrame);
        breakFlyDistance.x = Mathf.Max(0f, breakFlyDistance.x);
        breakFlyDistance.y = Mathf.Max(0f, breakFlyDistance.y);
    }
}

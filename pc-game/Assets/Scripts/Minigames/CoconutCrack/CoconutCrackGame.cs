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

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        if (p1View == null || p2View == null || p1Coconut == null || p2Coconut == null)
        {
            Debug.LogError($"{nameof(CoconutCrackGame)}: P1/P2 View와 Coconut 참조를 모두 연결해야 합니다.", this);
            enabled = false;
            return;
        }

        _coconutBreakLeftSprite = ArtAssets.LoadProp("coconut_break_left");
        _coconutBreakRightSprite = ArtAssets.LoadProp("coconut_break_right");
        _p1Effect = CreateEffect("P1");
        _p2Effect = CreateEffect("P2");

        hud?.SetTimeRemaining(matchSeconds);
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);

        // 고정 시간 재생 대신 실제 손-머리 거리로 프레임 진행률을 결정한다. 손을 올리면
        // 정방향, 내리면 역방향으로 재생되므로 사람의 실제 한 사이클과 애니메이션이 일치한다.
        DriveGestureAnimation(p1, p1View, ref _p1AnimationProgress);
        DriveGestureAnimation(p2, p2View, ref _p2AnimationProgress);

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
            hitCount++;
            readyToHit = false;
            releaseHeld = 0f;
            hud?.SetHits(id, hitCount);
            TryBreakCoconut(coconut, view, effect);
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
        if (effect.IsBusy) return;

        effect.IsBusy = true;
        StartCoroutine(PunchCoconut(coconut, effect, sourceWorldWidth, breakStartPosition));
    }

    private static void RespawnCoconut(SpriteRenderer coconut, CoconutEffect effect)
    {
        if (coconut == null || effect == null || !effect.AwaitingRelease) return;
        effect.AwaitingRelease = false;
        coconut.enabled = true;
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

    private CoconutEffect CreateEffect(string playerName)
    {
        return new CoconutEffect
        {
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
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
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

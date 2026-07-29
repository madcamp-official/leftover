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
// FrameAnimatedCharacter로 통째로 재생한다. p1Anchor/p2Anchor는 "캐릭터가 서는 자리"만
// 나타내는 빈 Transform이면 충분하다(기존 리깅 오브젝트를 그대로 써도 되고, 새 빈
// 오브젝트를 만들어 써도 된다 - 둘 다 위치만 있으면 됨). 손-머리 거리 판정 자체는
// PlayerPoseState.HandToHeadDistance()로 원시 트래킹 값을 직접 쓰므로 리깅과 무관하다.
using System.Collections;
using UnityEngine;

public class CoconutCrackGame : MonoBehaviour
{
    public float matchSeconds = 15f;
    public float hitDistance = 0.25f;     // 몸통 길이 대비, 이 이하면 "타격"
    public float releaseDistance = 0.45f; // 이 이상으로 벌어져야 다음 타격을 셀 준비 완료
    public float resultDisplaySeconds = 2f;
    public float coconutWidth = 0.5f; // 인게임에서 보일 코코넛 가로 폭(월드 유닛)
    public float hitAnimSeconds = 0.4f; // 캐릭터가 코코넛을 머리로 가져가는 스윙(프레임 애니메이션) 재생 시간 - 이 동안은 코코넛이 손 위치를 따라간다
    public float breakAnimSeconds = 0.25f; // 스윙이 끝난 뒤 코코넛이 쪼개져 날아가는 연출 시간
    public float coconutRespawnSeconds = 0.08f; // 파괴 시작 후 다음 멀쩡한 코코넛이 나타날 때까지의 시간
    public float p1FrameWidth = 1.2f; // P1 coconut_N 프레임 캐릭터의 표시 폭(월드 유닛) - 바닥에 발이 붙은 채로 커지고 작아짐
    public float p2FrameWidth = 1.6f; // P2도 필요하면 따로 조정(같은 방식으로 바닥 기준 유지)
    public float p1FrameYOffset = 0.5f; // P1 캐릭터를 바닥 기준 위치에서 위로 추가로 띄우는 양(월드 유닛)
    public float p2FrameYOffset = 0f;   // P2도 필요하면 같은 식으로 조정
    public int frameSortingOrder = -2;  // 책상/받침대 소품(정렬 순서 -1)보다 뒤에 그려지도록 더 낮은 값
    // 쪼개진 반쪽이 날아가는 거리(월드 유닛) - 왼쪽은 (-x,-y), 오른쪽은 (x,-y) 방향으로.
    // 예전엔 (0.3, 0.15)처럼 너무 작아서 "그 자리에서 쪼그라들며 사라지는" 것처럼 보였다.
    public Vector2 breakFlyDistance = new Vector2(3f, 1.5f);
    // coconut_1(대기)부터 coconut_N(스윙 마지막 프레임)까지, 각 그림에서 실제로 손이 있는
    // 자리를 프레임 순서대로 지정한 배열이다(인덱스 0 = coconut_1). 스프라이트 중앙 기준
    // 오프셋(오른쪽/위가 +, 스프라이트 로컬 단위)이고, 리깅 관절 위치가 아니라 "이 그림"에서
    // 실제 손이 있는 자리를 직접 지정하는 값이라 에디터에서 실제 그림을 보면서 맞춰야 한다.
    // 최종 월드 위치에는 프레임 표시 크기와 부모 Transform이 자동 반영된다. 보간 없이
    // 프레임이 바뀌는 순간 정확히 그 프레임 값으로 스냅한다. 배열이 실제 프레임 수보다
    // 짧으면 마지막 값을 계속 쓴다 - 프레임 수(지금 6장)에 맞게 Inspector에서 배열 크기를
    // 늘려서 하나씩 채우면 된다.
    // 기본값은 일부러 앞쪽 프레임을 전부 같은 값(들고 있는 자리)으로 두고 마지막 프레임에서만
    // 값을 바꿔서, 서서히 올라가지 않고 마지막 순간에 한 번에 순간이동하도록 해뒀다 -
    // 중간 값을 채우면 다시 여러 단계로 나눠 움직이게 할 수도 있다.
    public Vector2[] p1HandAnchors = {
        new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f),
        new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.7f),
    };
    public Vector2[] p2HandAnchors = {
        new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f),
        new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.7f),
    };

    private Sprite _coconutBreakLeftSprite;
    private Sprite _coconutBreakRightSprite;

    private sealed class CoconutEffect
    {
        public bool IsBusy;
        public SpriteRenderer Left;
        public SpriteRenderer Right;
    }

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private Transform p1Anchor; // 캐릭터가 서는 자리 - 기존 Caveman_P1을 그대로 꽂아도 됨
    [SerializeField] private Transform p2Anchor;
    [SerializeField] private SpriteRenderer p1Coconut;
    [SerializeField] private SpriteRenderer p2Coconut;
    [SerializeField] private CoconutBreakHud hud;
    private FrameAnimatedCharacter _p1Anim;
    private FrameAnimatedCharacter _p2Anim;
    private CoconutEffect _p1Effect;
    private CoconutEffect _p2Effect;
    private float _elapsed;
    private int _p1Hits;
    private int _p2Hits;
    private bool _p1ReadyToHit = true;
    private bool _p2ReadyToHit = true;
    private bool _ended;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        if (p1Anchor == null || p2Anchor == null || p1Coconut == null || p2Coconut == null)
        {
            Debug.LogError($"{nameof(CoconutCrackGame)}: P1/P2 Anchor와 Coconut 참조를 모두 연결해야 합니다.", this);
            enabled = false;
            return;
        }

        _coconutBreakLeftSprite = ArtAssets.LoadProp("coconut_break_left");
        _coconutBreakRightSprite = ArtAssets.LoadProp("coconut_break_right");
        _p1Effect = CreateEffect("P1");
        _p2Effect = CreateEffect("P2");

        _p1Anim = FrameAnimatedCharacter.Attach(p1Anchor.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P1, "coconut"), p1FrameWidth, p1FrameYOffset, p1HandAnchors, frameSortingOrder,
            useWorldSpaceLayout: true,
            keepVisible: new[] { p1Coconut });
        _p2Anim = FrameAnimatedCharacter.Attach(p2Anchor.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P2, "coconut"), p2FrameWidth, p2FrameYOffset, p2HandAnchors, frameSortingOrder,
            useWorldSpaceLayout: true,
            keepVisible: new[] { p2Coconut });

        hud?.SetTimeRemaining(matchSeconds);
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);

        // 코코넛이 고정 위치가 아니라 지금 재생 중인 그림(coconut_N)에서 손이 있는 자리를
        // 따라다니게 한다(FrameAnimatedCharacter에 지정해 둔 handAnchor).
        if (p1Coconut != null && _p1Anim != null) p1Coconut.transform.position = _p1Anim.HandAnchorWorld;
        if (p2Coconut != null && _p2Anim != null) p2Coconut.transform.position = _p2Anim.HandAnchorWorld;

        if (_ended) return;

        CountHits(PlayerId.P1, p1, ref _p1ReadyToHit, ref _p1Hits, p1Coconut, _p1Anim, _p1Effect);
        CountHits(PlayerId.P2, p2, ref _p2ReadyToHit, ref _p2Hits, p2Coconut, _p2Anim, _p2Effect);

        _elapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void CountHits(PlayerId id, PlayerPoseState state, ref bool readyToHit, ref int hitCount,
        SpriteRenderer coconut, FrameAnimatedCharacter anim, CoconutEffect effect)
    {
        if (state == null || !state.IsTracked) return;
        float distance = state.HandToHeadDistance();

        if (readyToHit && distance <= hitDistance)
        {
            hitCount++;
            readyToHit = false;
            hud?.SetHits(id, hitCount);
            TryPlayHitVisual(coconut, anim, effect);
        }
        else if (!readyToHit && distance >= releaseDistance)
        {
            readyToHit = true;
        }
    }

    // 점수 판정은 입력 속도 그대로 유지하되, 같은 플레이어의 시각 연출은 하나만 재생한다.
    // 이전 연출이 코코넛 표시 상태를 복구하기 전에 다음 코루틴이 겹쳐 실행되는 것을 막는다.
    private void TryPlayHitVisual(SpriteRenderer coconut, FrameAnimatedCharacter anim, CoconutEffect effect)
    {
        if (effect == null || effect.IsBusy) return;
        effect.IsBusy = true;
        anim?.PlayOnce(hitAnimSeconds);
        StartCoroutine(PunchCoconut(coconut, effect));
    }

    // 타격이 인정된 직후에는 코코넛을 숨기지 않는다 - hitAnimSeconds(스윙) 동안은 코코넛이
    // 계속 보이면서 Update()에서 매 프레임 갱신되는 손 위치(HandAnchorWorld)를 따라 머리로
    // 올라가는 것처럼 보인다. 스윙이 끝난 그 순간에야 멀쩡한 코코넛을 숨기고, 반으로 쪼개진
    // 코코넛 두 조각이 breakFlyDistance만큼 좌우로 크게 튀어나가며 사라지는 연출을
    // breakAnimSeconds 동안 보여준 뒤 다시 멀쩡한 코코넛으로 복구한다(반복 타격 경쟁이라
    // 매번 "깨졌다가 새 코코넛이 나타나는" 것처럼 보이게).
    private IEnumerator PunchCoconut(SpriteRenderer coconut, CoconutEffect effect)
    {
        yield return new WaitForSeconds(hitAnimSeconds);

        if (coconut == null)
        {
            effect.IsBusy = false;
            yield break;
        }

        Vector3 startPos = coconut.transform.position;
        PrepareHalf(effect.Left, startPos);
        PrepareHalf(effect.Right, startPos);
        coconut.enabled = false;

        float duration = breakAnimSeconds;
        float respawnAt = Mathf.Min(coconutRespawnSeconds, duration);
        Vector3 leftOffset = new Vector3(-breakFlyDistance.x, -breakFlyDistance.y, 0f);
        Vector3 rightOffset = new Vector3(breakFlyDistance.x, -breakFlyDistance.y, 0f);
        float elapsed = 0f;
        bool respawned = false;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            effect.Left.transform.position = startPos + leftOffset * p;
            effect.Left.transform.rotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, 1440f, p));
            effect.Right.transform.position = startPos + rightOffset * p;
            effect.Right.transform.rotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, -1440f, p));
            SetAlpha(effect.Left, 1f - p);
            SetAlpha(effect.Right, 1f - p);

            // 깨진 조각 연출이 끝날 때까지 기다리지 않고 다음 코코넛을 먼저 준비한다.
            if (!respawned && elapsed >= respawnAt)
            {
                coconut.enabled = true;
                respawned = true;
            }

            yield return null;
        }

        SetEffectVisible(effect, false);
        if (!respawned) coconut.enabled = true;
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

    private static void PrepareHalf(SpriteRenderer half, Vector3 worldPosition)
    {
        half.transform.position = worldPosition;
        half.transform.rotation = Quaternion.identity;
        SetAlpha(half, 1f);
        half.enabled = half.sprite != null;
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
        hitAnimSeconds = Mathf.Max(0.01f, hitAnimSeconds);
        breakAnimSeconds = Mathf.Max(0.01f, breakAnimSeconds);
        coconutRespawnSeconds = Mathf.Clamp(coconutRespawnSeconds, 0f, breakAnimSeconds);
        coconutWidth = Mathf.Max(0.01f, coconutWidth);
        p1FrameWidth = Mathf.Max(0.01f, p1FrameWidth);
        p2FrameWidth = Mathf.Max(0.01f, p2FrameWidth);
        hitDistance = Mathf.Max(0f, hitDistance);
        releaseDistance = Mathf.Max(hitDistance + 0.01f, releaseDistance);
        breakFlyDistance.x = Mathf.Max(0f, breakFlyDistance.x);
        breakFlyDistance.y = Mathf.Max(0f, breakFlyDistance.y);
    }
}

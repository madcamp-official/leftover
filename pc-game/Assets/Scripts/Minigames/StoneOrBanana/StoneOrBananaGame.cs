// 돌 or 바나나 - 우가우가게임_기획_프롬프트.md "5. 돌 or 바나나" 스펙.
//
// 규칙: 매 턴 던지는/받는 역할이 번갈아 바뀜. 던지는 사람은 오른손 들면 돌, 왼손 들면 바나나
// (돌던지기와 동일한 손 문법). 받는 사람은 입을 벌리면 먹기, 다물면 피하기.
//   - 바나나 먹음 -> 받는 사람 포만감 +1.
//   - 바나나 피함 -> 부메랑처럼 돌아와 던진 사람이 먹어야 함(다시 같은 판정, 이번엔 던진
//     사람이 입 벌려야 포만감 획득, 못 벌리면 그냥 놓침 - 페널티 없음).
//   - 돌 먹어버림(실수) -> 순수 페널티, 이빨 -1, 포만감 증가 없음.
//   - 돌 피함 -> 페널티 없음, 아무 일도 없음.
//   - 이빨이 0이 되면 그 즉시 패배. 정상적으로는 포만감을 먼저 채운 사람이 승리.
using System.Collections;
using UnityEngine;

public class StoneOrBananaGame : MonoBehaviour
{
    private enum ThrowKind { Stone, Banana }
    private enum TurnPhase { WaitingThrow, WaitingCatch, BoomerangBackToThrower, Resolving }

    public float maxFullness = 5f;
    public float maxTeeth = 3f;
    public float turnTimeoutSeconds = 8f;
    public float throwTravelSeconds = 0.5f;
    public float resultDisplaySeconds = 2f;
    public float stoneWidth = 0.3f;  // 인게임에서 보일 돌 가로 폭(월드 유닛)
    public float bananaWidth = 0.45f; // 인게임에서 보일 바나나 가로 폭(월드 유닛)

    private Sprite _stoneSprite;
    private Sprite _bananaSprite;
    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private CavemanSilhouette p1Silhouette;
    [SerializeField] private CavemanSilhouette p2Silhouette;
    [SerializeField] private StoneOrBananaHud hud;
    private float _p1Fullness, _p2Fullness;
    private float _p1Teeth = 3f, _p2Teeth = 3f;
    private PlayerId _currentThrower = PlayerId.P1;
    private TurnPhase _phase = TurnPhase.WaitingThrow;
    private float _turnTimer;
    private ThrowKind _thrownKind;
    private bool _ended;
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _stoneSprite = ArtAssets.LoadProp("stone");
        _bananaSprite = ArtAssets.LoadProp("banana");

        _p1Teeth = _p2Teeth = maxTeeth;
        RefreshStatusHud();
    }

    private void RefreshStatusHud()
    {
        hud?.SetStatus(PlayerId.P1, _p1Teeth, maxTeeth, _p1Fullness, maxFullness);
        hud?.SetStatus(PlayerId.P2, _p2Teeth, maxTeeth, _p2Fullness, maxFullness);
    }

    private PlayerPoseState State(PlayerId id) => PoseInputHub.Instance?.Get(id);
    private CavemanSilhouette Silhouette(PlayerId id) => id == PlayerId.P1 ? p1Silhouette : p2Silhouette;
    private PlayerId Other(PlayerId id) => id == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        p1Silhouette?.ApplyPose(hub?.Get(PlayerId.P1));
        p2Silhouette?.ApplyPose(hub?.Get(PlayerId.P2));

        if (_ended) return;

        switch (_phase)
        {
            case TurnPhase.WaitingThrow:
                TickWaitingThrow();
                break;
            case TurnPhase.WaitingCatch:
                hud?.SetTurnTimeRemaining(turnTimeoutSeconds - _turnTimer);
                TickWaitingCatch(catcher: Other(_currentThrower), onEaten: HandleFirstCatchResolved);
                break;
            case TurnPhase.BoomerangBackToThrower:
                hud?.SetTurnTimeRemaining(turnTimeoutSeconds - _turnTimer);
                TickWaitingCatch(catcher: _currentThrower, onEaten: HandleBoomerangResolved);
                break;
        }
    }

    private void TickWaitingThrow()
    {
        hud?.ShowThrowPrompt(true);

        PlayerPoseState thrower = State(_currentThrower);
        if (thrower == null || !thrower.IsTracked) return;

        bool rightRaised = thrower.IsHandRaised(rightHand: true);
        bool leftRaised = thrower.IsHandRaised(rightHand: false);
        if (!rightRaised && !leftRaised) return;

        _thrownKind = rightRaised ? ThrowKind.Stone : ThrowKind.Banana;
        hud?.ShowThrowPrompt(false);
        _turnTimer = 0f;
        _phase = TurnPhase.WaitingCatch;
        StartCoroutine(FlyItem(Silhouette(_currentThrower).transform.position,
            Silhouette(Other(_currentThrower)).transform.position, _thrownKind));
    }

    // catcher가 입을 벌리면 즉시 onEaten(true), turnTimeoutSeconds 안에 못 벌리면 onEaten(false).
    private void TickWaitingCatch(PlayerId catcher, System.Action<bool> onEaten)
    {
        hud?.ShowReceivePrompt(true);
        _turnTimer += Time.deltaTime;
        PlayerPoseState catcherState = State(catcher);
        bool ate = catcherState != null && catcherState.IsTracked && catcherState.IsMouthOpen();

        if (ate)
        {
            hud?.ShowReceivePrompt(false);
            onEaten(true);
        }
        else if (_turnTimer >= turnTimeoutSeconds)
        {
            hud?.ShowReceivePrompt(false);
            onEaten(false);
        }
    }

    private void HandleFirstCatchResolved(bool ate)
    {
        PlayerId catcher = Other(_currentThrower);

        if (_thrownKind == ThrowKind.Banana)
        {
            if (ate)
            {
                AddFullness(catcher, 1f);
                EndTurnAndAdvance();
            }
            else
            {
                _turnTimer = 0f;
                _phase = TurnPhase.BoomerangBackToThrower;
                StartCoroutine(FlyItem(Silhouette(catcher).transform.position,
                    Silhouette(_currentThrower).transform.position, ThrowKind.Banana));
            }
        }
        else // Stone
        {
            if (ate) AddTeeth(catcher, -1f);
            EndTurnAndAdvance();
        }
    }

    private void HandleBoomerangResolved(bool ate)
    {
        if (ate) AddFullness(_currentThrower, 1f);
        EndTurnAndAdvance();
    }

    private void AddFullness(PlayerId player, float amount)
    {
        if (player == PlayerId.P1) _p1Fullness = Mathf.Min(maxFullness, _p1Fullness + amount);
        else _p2Fullness = Mathf.Min(maxFullness, _p2Fullness + amount);
        RefreshStatusHud();
        StartCoroutine(ReactToEat(player, "face_banana_chewing_puffed_cheeks"));
    }

    private void AddTeeth(PlayerId player, float amount)
    {
        if (player == PlayerId.P1) _p1Teeth = Mathf.Max(0f, _p1Teeth + amount);
        else _p2Teeth = Mathf.Max(0f, _p2Teeth + amount);
        RefreshStatusHud();

        // maxTeeth(3)와 깨진 이 표정 2단계(1개/2개)가 정확히 맞아떨어진다 - 지금까지 몇 개
        // 잃었는지로 표정을 고른다(이빨을 늘리는 호출은 없어서 "잃은 개수" 계산만으로 충분).
        float teeth = player == PlayerId.P1 ? _p1Teeth : _p2Teeth;
        int lost = Mathf.RoundToInt(maxTeeth - teeth);
        string face = lost >= 2 ? "face_stone_hit_two_teeth_broken" : "face_stone_hit_one_tooth_broken";
        StartCoroutine(ReactToEat(player, face));
    }

    // 돌을 잘못 먹었을 때/바나나를 먹었을 때 잠깐 표정을 바꿨다가 되돌린다 - 관련 표정
    // 이미지는 이미 임포트돼 있었지만(9/13번 문서 TODO) 여태 연결이 안 되어 있었다.
    private IEnumerator ReactToEat(PlayerId player, string facePart)
    {
        CavemanSilhouette sil = Silhouette(player);
        if (sil == null) yield break;
        sil.SetFace(facePart);
        yield return new WaitForSeconds(0.8f);
        sil.ResetFace();
    }

    private void EndTurnAndAdvance()
    {
        if (_p1Teeth <= 0f) { EndMatch(PlayerId.P2); return; }
        if (_p2Teeth <= 0f) { EndMatch(PlayerId.P1); return; }
        if (_p1Fullness >= maxFullness) { EndMatch(PlayerId.P1); return; }
        if (_p2Fullness >= maxFullness) { EndMatch(PlayerId.P2); return; }

        _currentThrower = Other(_currentThrower);
        _turnTimer = 0f;
        _phase = TurnPhase.WaitingThrow;
    }

    private IEnumerator FlyItem(Vector3 from, Vector3 to, ThrowKind kind)
    {
        var go = new GameObject($"Thrown_{kind}");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = kind == ThrowKind.Stone ? _stoneSprite : _bananaSprite;
        sr.sortingOrder = 5;
        Vector3 start = from + Vector3.up * 0.6f;
        Vector3 end = to + Vector3.up * 0.6f;
        go.transform.position = start;
        ArtAssets.FitWidth(sr, kind == ThrowKind.Stone ? stoneWidth : bananaWidth);

        float t = 0f;
        while (t < throwTravelSeconds)
        {
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(start, end, t / throwTravelSeconds);
            yield return null;
        }
        Destroy(go);
    }

    private void EndMatch(PlayerId winner)
    {
        _ended = true;
        hud?.ShowThrowPrompt(false);
        hud?.ShowReceivePrompt(false);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

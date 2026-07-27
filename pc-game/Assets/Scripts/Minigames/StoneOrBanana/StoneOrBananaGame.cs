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
    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _p1Fullness, _p2Fullness;
    private float _p1Teeth = 3f, _p2Teeth = 3f;
    private PlayerId _currentThrower = PlayerId.P1;
    private TurnPhase _phase = TurnPhase.WaitingThrow;
    private float _turnTimer;
    private ThrowKind _thrownKind;
    private string _statusText = "";
    private bool _ended;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _stoneSprite = ArtAssets.LoadProp("stone");
        _bananaSprite = ArtAssets.LoadProp("banana");

        Camera cam = Camera.main;
        if (cam == null)
        {
            var camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        cam.orthographic = true;
        cam.orthographicSize = 3f;
        cam.transform.position = new Vector3(0, 1f, -10f);

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2f, 0f, 0f));
        _p1Teeth = _p2Teeth = maxTeeth;
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
    private CavemanSilhouette Silhouette(PlayerId id) => id == PlayerId.P1 ? _p1Silhouette : _p2Silhouette;
    private PlayerId Other(PlayerId id) => id == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        _p1Silhouette.ApplyPose(hub?.Get(PlayerId.P1));
        _p2Silhouette.ApplyPose(hub?.Get(PlayerId.P2));

        if (_ended) return;

        switch (_phase)
        {
            case TurnPhase.WaitingThrow:
                TickWaitingThrow();
                break;
            case TurnPhase.WaitingCatch:
                TickWaitingCatch(catcher: Other(_currentThrower), onEaten: HandleFirstCatchResolved);
                break;
            case TurnPhase.BoomerangBackToThrower:
                TickWaitingCatch(catcher: _currentThrower, onEaten: HandleBoomerangResolved);
                break;
        }
    }

    private void TickWaitingThrow()
    {
        PlayerPoseState thrower = State(_currentThrower);
        if (thrower == null || !thrower.IsTracked) return;

        bool rightRaised = thrower.IsHandRaised(rightHand: true);
        bool leftRaised = thrower.IsHandRaised(rightHand: false);
        if (!rightRaised && !leftRaised) return;

        _thrownKind = rightRaised ? ThrowKind.Stone : ThrowKind.Banana;
        _statusText = $"{_currentThrower}가 {(_thrownKind == ThrowKind.Stone ? "돌" : "바나나")}을 던졌다!";
        _turnTimer = 0f;
        _phase = TurnPhase.WaitingCatch;
        StartCoroutine(FlyItem(Silhouette(_currentThrower).transform.position,
            Silhouette(Other(_currentThrower)).transform.position, _thrownKind));
    }

    // catcher가 입을 벌리면 즉시 onEaten(true), turnTimeoutSeconds 안에 못 벌리면 onEaten(false).
    private void TickWaitingCatch(PlayerId catcher, System.Action<bool> onEaten)
    {
        _turnTimer += Time.deltaTime;
        PlayerPoseState catcherState = State(catcher);
        bool ate = catcherState != null && catcherState.IsTracked && catcherState.IsMouthOpen();

        if (ate)
        {
            onEaten(true);
        }
        else if (_turnTimer >= turnTimeoutSeconds)
        {
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
                _statusText = $"{catcher}가 바나나를 먹었다! 포만감 +1";
                EndTurnAndAdvance();
            }
            else
            {
                _statusText = "바나나를 피했다 - 부메랑처럼 돌아온다!";
                _turnTimer = 0f;
                _phase = TurnPhase.BoomerangBackToThrower;
                StartCoroutine(FlyItem(Silhouette(catcher).transform.position,
                    Silhouette(_currentThrower).transform.position, ThrowKind.Banana));
            }
        }
        else // Stone
        {
            if (ate)
            {
                AddTeeth(catcher, -1f);
                _statusText = $"{catcher}가 돌을 먹어버렸다! 이빨 -1 (페널티)";
            }
            else
            {
                _statusText = $"{catcher}가 돌을 피했다.";
            }
            EndTurnAndAdvance();
        }
    }

    private void HandleBoomerangResolved(bool ate)
    {
        if (ate)
        {
            AddFullness(_currentThrower, 1f);
            _statusText = $"{_currentThrower}가 돌아온 바나나를 먹었다! 포만감 +1";
        }
        else
        {
            _statusText = "돌아온 바나나를 놓쳤다.";
        }
        EndTurnAndAdvance();
    }

    private void AddFullness(PlayerId player, float amount)
    {
        if (player == PlayerId.P1) _p1Fullness = Mathf.Min(maxFullness, _p1Fullness + amount);
        else _p2Fullness = Mathf.Min(maxFullness, _p2Fullness + amount);
    }

    private void AddTeeth(PlayerId player, float amount)
    {
        if (player == PlayerId.P1) _p1Teeth = Mathf.Max(0f, _p1Teeth + amount);
        else _p2Teeth = Mathf.Max(0f, _p2Teeth + amount);
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
        _statusText = $"{winner} 승리!";
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
        GUI.Label(new Rect(20, 20, 400, 30), $"P1 포만감 {_p1Fullness:F0}/{maxFullness}  이빨 {_p1Teeth:F0}/{maxTeeth}");
        GUI.Label(new Rect(20, 50, 400, 30), $"P2 포만감 {_p2Fullness:F0}/{maxFullness}  이빨 {_p2Teeth:F0}/{maxTeeth}");
        GUI.Label(new Rect(20, 80, 400, 30), $"현재 던지는 사람: {_currentThrower}   (오른손=돌, 왼손=바나나)");

        if (!string.IsNullOrEmpty(_statusText))
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 120, Screen.width, 40), _statusText, style);
        }
    }
}

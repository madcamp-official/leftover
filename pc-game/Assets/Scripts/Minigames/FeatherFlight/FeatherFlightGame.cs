// 깃털날기 (버티기 생존전) - docs/minigames/08_깃털날기.md 스펙.
//
// 규칙 요약 (구현됨):
//   - 매 프레임 height -= fallSpeed * deltaTime로 계속 하강한다.
//   - 양손이 "안 든 상태 -> 든 상태"로 바뀌는 그 프레임(rising edge)에만 height += flapBoost.
//     양손을 든 채로 버텨도 추가 상승은 없다 - 다시 내렸다 들어야 다음 날갯짓이 인정된다.
//   - height는 절벽(Cliff) 오브젝트의 Y를 0으로 두는 상대값이다. 캐릭터가 오를 수 있는 가장
//     높은 자리(height의 상한, ceiling)는 카메라 화면의 위쪽 끝이고, 떨어질 수 있는 가장
//     낮은 자리(height의 하한, floor)는 화면의 아래쪽 끝이다 - Start()에서 카메라와 절벽 Y로
//     자동 계산한다. height가 floor에 닿는 즉시(제한시간 전이라도) 그 플레이어가 패배.
//   - matchSeconds 경과 시 더 높은 쪽(height)이 승리, 같으면 무승부.
//
// 인트로: 캐릭터가 절벽 위 시작 자리(=씬에 배치된 원래 위치)에서 X만 RestPoint의 X로
// 서서히 이동하면서, Y는 원래 자리에서 introHopHeight만큼 살짝 떴다가 다시 원래 Y로
// 돌아오는 "제자리 홉"을 한다(jump 컷 재생). 홉이 끝나 원래 Y로 돌아오는 그 순간부터 실제
// 게임플레이(하강/날갯짓)가 시작된다(RestPoint의 Y는 쓰지 않는다, X 이동 목표로만 쓰인다).
//
// 화면 표현: 절벽(Cliff)은 씬에 배치한 자리에서 전혀 움직이지 않는다. 대신 캐릭터가
// "절벽 Y + height"를 따라 위아래로 움직인다. P1/P2는 각자 독립적으로 움직이고, 절벽 Y가
// 다르면 각자의 ceiling/floor도 따로 계산된다. 캐릭터 표현 자체(프레임 전환)는
// FeatherFlightCharacterView가 맡는다(런타임 인스턴스화 없음). 높이는 캐릭터가 화면에서
// 실제로 오르내리는 것으로 이미 드러나므로 별도 HUD 게이지는 쓰지 않는다.
using System.Collections;
using UnityEngine;

public class FeatherFlightGame : MonoBehaviour
{
    public float matchSeconds = 20f;      // 라운드 제한시간
    public float fallSpeed = 1.2f;        // 매 프레임 지속적으로 줄어드는 하강 속도(유닛/초)
    public float flapBoost = .35f;        // 날갯짓 1회(양손 rising edge)당 즉시 더해지는 높이
    public float raiseRatio = .15f;       // IsHandRaised에 전달하는 임계값(공용 기본값)
    public float resultDisplaySeconds = 2f;
    public float jumpIntroSeconds = .5f;  // 인트로 홉(제자리 도약) 재생 시간
    public float introHopHeight = .5f;    // 인트로 중 원래 Y에서 살짝 떴다가 다시 돌아오는 높이(유닛)
    public float wingAnimationSmoothing = 10f; // 날갯짓 프레임 전환이 뚝뚝 끊기지 않게 하는 속도
    [Min(.05f)] public float flapSfxSeconds = .45f; // 12초 원본 중 날갯짓 한 번에 해당하는 구간
    [Min(.05f)] public float flapSfxCooldown = .55f; // 추적 흔들림에 의한 짧은 간격의 중복 재생 방지

    [Header("씬에 배치된 오브젝트")]
    [Tooltip("이 플레이어의 절벽 - 움직이지 않는다. 이 오브젝트의 Y가 height=0 기준선이다.")]
    [SerializeField] private Transform p1Cliff;
    [SerializeField] private Transform p2Cliff;
    [Tooltip("캐릭터가 인트로 중 X만 이동해서 도착하는 화면 쪽 자리(Y는 쓰이지 않는다 - 인트로는 원래 Y로 돌아온다).")]
    [SerializeField] private Transform p1RestPoint;
    [SerializeField] private Transform p2RestPoint;
    [SerializeField] private FeatherFlightCharacterView p1View;
    [SerializeField] private FeatherFlightCharacterView p2View;
    [SerializeField] private FeatherFlightHud hud;

    private float _p1Height, _p2Height;
    private float _p1FloorY, _p2FloorY;       // 절벽 Y = height 0 기준선(월드 좌표)
    private float _p1Ceiling, _p2Ceiling;     // 화면 위쪽 끝까지의 height(항상 양수)
    private float _p1Floor, _p2Floor;         // 화면 아래쪽 끝까지의 height(항상 음수)
    private float _p1RestHeight, _p2RestHeight; // 캐릭터의 원래 스폰 Y를 height로 환산한 값 - 인트로 홉이 끝나 돌아오는 자리(실제 게임플레이 시작 height)
    private bool _p1PrevRaised, _p2PrevRaised;
    private float _p1WingProgress, _p2WingProgress;
    private float _p1LastFlapSfxAt = float.NegativeInfinity;
    private float _p2LastFlapSfxAt = float.NegativeInfinity;
    private float _elapsed;
    private bool _ended;
    private bool _introDone;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        if (p1Cliff == null || p2Cliff == null || p1RestPoint == null || p2RestPoint == null
            || p1View == null || p2View == null)
        {
            Debug.LogError($"{nameof(FeatherFlightGame)}: Cliff/RestPoint/View 참조를 모두 연결해야 합니다.", this);
            enabled = false;
            return;
        }

        // 절벽은 절대 움직이지 않으므로, 절벽 오브젝트 자체의 Y를 "height=0" 기준선으로
        // 캐싱해둔다. 그 기준선에서 카메라 화면 위/아래 끝까지의 거리가 각각 ceiling(최고
        // 높이)/floor(최저 높이)가 된다.
        _p1FloorY = p1Cliff.position.y;
        _p2FloorY = p2Cliff.position.y;

        // 캐릭터가 씬에 배치된 원래 Y(=인트로 홉이 끝나고 돌아오는 자리)를 실제 게임플레이
        // 시작 height로 쓴다. Start() 시점에는 아직 SetBase가 한 번도 호출되지 않았으므로
        // transform.position이 곧 씬에서 배치한 원래 자리다.
        _p1RestHeight = p1View.transform.position.y - _p1FloorY;
        _p2RestHeight = p2View.transform.position.y - _p2FloorY;

        Camera cam = Camera.main;
        if (cam != null)
        {
            float screenTop = cam.transform.position.y + cam.orthographicSize;
            float screenBottom = cam.transform.position.y - cam.orthographicSize;
            _p1Ceiling = screenTop - _p1FloorY;
            _p2Ceiling = screenTop - _p2FloorY;
            _p1Floor = screenBottom - _p1FloorY;
            _p2Floor = screenBottom - _p2FloorY;
        }

        _p1Height = 0f;
        _p2Height = 0f;
        hud?.SetTimeRemaining(matchSeconds);

        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        // 절벽 위 시작 자리(=캐릭터가 씬에 배치된 그 자리)에서 X만 RestPoint의 X로 이동하고,
        // Y는 원래 Y에서 introHopHeight만큼 살짝 떴다가(t=0.5에서 정점) 다시 원래 Y로
        // 돌아오는 제자리 홉이다. 실제 게임플레이는 Y가 원래 자리로 돌아온 뒤(t=1)에 시작한다.
        Vector3 p1Start = p1View.transform.position;
        Vector3 p2Start = p2View.transform.position;
        float p1EndX = p1RestPoint.position.x;
        float p2EndX = p2RestPoint.position.x;
        int p1Frames = Mathf.Max(p1View.JumpFrameCount, 1);
        int p2Frames = Mathf.Max(p2View.JumpFrameCount, 1);

        float t = 0f;
        while (t < 1f)
        {
            t = jumpIntroSeconds > 0f ? Mathf.Clamp01(t + Time.deltaTime / jumpIntroSeconds) : 1f;
            float hop = introHopHeight * Mathf.Sin(t * Mathf.PI);

            p1View.SetBase(new Vector3(Mathf.Lerp(p1Start.x, p1EndX, t), p1Start.y + hop, p1Start.z));
            p2View.SetBase(new Vector3(Mathf.Lerp(p2Start.x, p2EndX, t), p2Start.y + hop, p2Start.z));
            p1View.ShowJumpFrame(Mathf.FloorToInt(t * (p1Frames - 1)));
            p2View.ShowJumpFrame(Mathf.FloorToInt(t * (p2Frames - 1)));

            yield return null;
        }
        p1View.SetBase(new Vector3(p1EndX, p1Start.y, p1Start.z));
        p2View.SetBase(new Vector3(p2EndX, p2Start.y, p2Start.z));

        _p1Height = Mathf.Clamp(_p1RestHeight, _p1Floor, _p1Ceiling);
        _p2Height = Mathf.Clamp(_p2RestHeight, _p2Floor, _p2Ceiling);

        _introDone = true;
    }

    private void Update()
    {
        if (!_introDone || _ended) return;

        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);

        bool p1Landed = TickPlayer(p1, ref _p1Height, ref _p1PrevRaised, ref _p1WingProgress,
            ref _p1LastFlapSfxAt, p1View, p1RestPoint.position.x, _p1FloorY, _p1Ceiling, _p1Floor);
        bool p2Landed = TickPlayer(p2, ref _p2Height, ref _p2PrevRaised, ref _p2WingProgress,
            ref _p2LastFlapSfxAt, p2View, p2RestPoint.position.x, _p2FloorY, _p2Ceiling, _p2Floor);

        if (p1Landed || p2Landed)
        {
            PlayerId? winner = p1Landed && p2Landed ? (PlayerId?)null : (p1Landed ? PlayerId.P2 : PlayerId.P1);
            EndMatch(winner);
            return;
        }

        _elapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = Mathf.Approximately(_p1Height, _p2Height)
                ? (PlayerId?)null
                : (_p1Height > _p2Height ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    // 반환값: 이 플레이어가 이번 프레임에 화면 아래쪽 끝(height<=floor)에 닿았는지.
    private bool TickPlayer(PlayerPoseState state, ref float height, ref bool prevRaised,
        ref float wingProgress, ref float lastFlapSfxAt, FeatherFlightCharacterView view, float fixedX, float floorY,
        float ceiling, float floor)
    {
        bool raisedNow = state != null && state.IsTracked
            && state.IsHandRaised(rightHand: true, raiseRatio)
            && state.IsHandRaised(rightHand: false, raiseRatio);

        height -= fallSpeed * Time.deltaTime;

        // 날갯짓 1회 = 양손이 "안 든 상태"에서 "든 상태"로 바뀌는 그 프레임에만 카운트.
        if (raisedNow && !prevRaised)
        {
            height = Mathf.Min(ceiling, height + flapBoost);
            if (Time.unscaledTime - lastFlapSfxAt >= flapSfxCooldown)
            {
                lastFlapSfxAt = Time.unscaledTime;
                GameSfx.Play("eagle_wings", maxDuration: flapSfxSeconds);
            }
        }

        prevRaised = raisedNow;
        height = Mathf.Min(ceiling, height);

        float targetWing = raisedNow ? 1f : 0f;
        float blend = 1f - Mathf.Exp(-wingAnimationSmoothing * Time.deltaTime);
        wingProgress = Mathf.Lerp(wingProgress, targetWing, blend);
        if (view != null)
        {
            view.SetBase(new Vector3(fixedX, floorY + height, 0f));
            if (view.FlapFrameCount > 0)
                view.ShowFlapFrame(Mathf.RoundToInt(wingProgress * (view.FlapFrameCount - 1)));
        }

        return height <= floor;
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{Label(winner.Value)} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    private void OnValidate()
    {
        matchSeconds = Mathf.Max(.1f, matchSeconds);
        fallSpeed = Mathf.Max(0f, fallSpeed);
        flapBoost = Mathf.Max(0f, flapBoost);
        raiseRatio = Mathf.Max(0f, raiseRatio);
        resultDisplaySeconds = Mathf.Max(0f, resultDisplaySeconds);
        jumpIntroSeconds = Mathf.Max(0f, jumpIntroSeconds);
        introHopHeight = Mathf.Max(0f, introHopHeight);
        wingAnimationSmoothing = Mathf.Max(.01f, wingAnimationSmoothing);
        flapSfxSeconds = Mathf.Max(.05f, flapSfxSeconds);
        flapSfxCooldown = Mathf.Max(.05f, flapSfxCooldown);
    }
}

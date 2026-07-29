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
// 인트로: 캐릭터가 절벽 위에 서 있는 자리(절벽 바닥 = 캐릭터 발)에서 뛰어올라, 화면 중앙
// 쪽 X(RestPoint의 X)와 절벽 Y보다 introDropHeight만큼 높은 자리로 도약한다(jump 컷 재생).
// 도약이 끝나는 순간 실제 게임플레이 height가 그 introDropHeight에서 시작한다.
//
// 화면 표현: 절벽(Cliff)은 씬에 배치한 자리에서 전혀 움직이지 않는다. 대신 캐릭터가
// "절벽 Y + height"를 따라 위아래로 움직인다. P1/P2는 각자 독립적으로 움직이고, 절벽 Y가
// 다르면 각자의 ceiling/floor도 따로 계산된다. 캐릭터 표현 자체(프레임 전환)는
// FeatherFlightCharacterView가 맡는다(런타임 인스턴스화 없음).
using System.Collections;
using UnityEngine;

public class FeatherFlightGame : MonoBehaviour
{
    public float matchSeconds = 20f;      // 라운드 제한시간
    public float fallSpeed = 1.2f;        // 매 프레임 지속적으로 줄어드는 하강 속도(유닛/초)
    public float flapBoost = .35f;        // 날갯짓 1회(양손 rising edge)당 즉시 더해지는 높이
    public float raiseRatio = .15f;       // IsHandRaised에 전달하는 임계값(공용 기본값)
    public float resultDisplaySeconds = 2f;
    public float jumpIntroSeconds = .5f;  // 절벽 위 시작 자리에서 도약 완료까지 걸리는 시간
    public float cliffDropSeconds = .3f;  // (미사용 - 절벽이 안 움직이므로 더는 의미 없음, 기존 값 보존용)
    public float introDropHeight = 1f;    // 도약이 끝났을 때 캐릭터의 시작 height(절벽 Y 위로 뜬 양)
    public float wingAnimationSmoothing = 10f; // 날갯짓 프레임 전환이 뚝뚝 끊기지 않게 하는 속도

    [Header("씬에 배치된 오브젝트")]
    [Tooltip("이 플레이어의 절벽 - 움직이지 않는다. 이 오브젝트의 Y가 height=0 기준선(게이지 중심)이다.")]
    [SerializeField] private Transform p1Cliff;
    [SerializeField] private Transform p2Cliff;
    [Tooltip("캐릭터가 도약한 뒤 머무는 화면 쪽 X 위치(Y는 height 계산에 안 쓰인다).")]
    [SerializeField] private Transform p1RestPoint;
    [SerializeField] private Transform p2RestPoint;
    [SerializeField] private FeatherFlightCharacterView p1View;
    [SerializeField] private FeatherFlightCharacterView p2View;
    [SerializeField] private FeatherFlightHud hud;

    private float _p1Height, _p2Height;
    private float _p1FloorY, _p2FloorY;       // 절벽 Y = height 0 기준선(월드 좌표)
    private float _p1Ceiling, _p2Ceiling;     // 화면 위쪽 끝까지의 height(항상 양수)
    private float _p1Floor, _p2Floor;         // 화면 아래쪽 끝까지의 height(항상 음수)
    private bool _p1PrevRaised, _p2PrevRaised;
    private float _p1WingProgress, _p2WingProgress;
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

        // 절벽은 절대 움직이지 않으므로, 절벽 오브젝트 자체의 Y를 "height=0" 기준선(게이지
        // 중심)으로 캐싱해둔다. 그 기준선에서 카메라 화면 위/아래 끝까지의 거리가 각각
        // ceiling(최고 높이)/floor(최저 높이)가 된다.
        _p1FloorY = p1Cliff.position.y;
        _p2FloorY = p2Cliff.position.y;

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
        hud?.SetHeight(PlayerId.P1, 0f);
        hud?.SetHeight(PlayerId.P2, 0f);
        hud?.SetTimeRemaining(matchSeconds);

        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        // 절벽 위 시작 자리(=캐릭터가 씬에 배치된 그 자리) -> RestPoint의 X, 절벽 Y보다
        // introDropHeight만큼 높은 Y로 도약.
        Vector3 p1Start = p1View.transform.position;
        Vector3 p2Start = p2View.transform.position;
        Vector3 p1End = new Vector3(p1RestPoint.position.x, _p1FloorY + introDropHeight, p1Start.z);
        Vector3 p2End = new Vector3(p2RestPoint.position.x, _p2FloorY + introDropHeight, p2Start.z);
        int p1Frames = Mathf.Max(p1View.JumpFrameCount, 1);
        int p2Frames = Mathf.Max(p2View.JumpFrameCount, 1);

        float t = 0f;
        while (t < 1f)
        {
            t = jumpIntroSeconds > 0f ? Mathf.Clamp01(t + Time.deltaTime / jumpIntroSeconds) : 1f;

            p1View.SetBase(Vector3.Lerp(p1Start, p1End, t));
            p2View.SetBase(Vector3.Lerp(p2Start, p2End, t));
            p1View.ShowJumpFrame(Mathf.FloorToInt(t * (p1Frames - 1)));
            p2View.ShowJumpFrame(Mathf.FloorToInt(t * (p2Frames - 1)));

            yield return null;
        }
        p1View.SetBase(p1End);
        p2View.SetBase(p2End);

        _p1Height = Mathf.Clamp(introDropHeight, _p1Floor, _p1Ceiling);
        _p2Height = Mathf.Clamp(introDropHeight, _p2Floor, _p2Ceiling);
        hud?.SetHeight(PlayerId.P1, SignedRatio(_p1Height, _p1Ceiling, _p1Floor));
        hud?.SetHeight(PlayerId.P2, SignedRatio(_p2Height, _p2Ceiling, _p2Floor));

        _introDone = true;
    }

    private void Update()
    {
        if (!_introDone || _ended) return;

        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);

        bool p1Landed = TickPlayer(PlayerId.P1, p1, ref _p1Height, ref _p1PrevRaised, ref _p1WingProgress,
            p1View, p1RestPoint.position.x, _p1FloorY, _p1Ceiling, _p1Floor);
        bool p2Landed = TickPlayer(PlayerId.P2, p2, ref _p2Height, ref _p2PrevRaised, ref _p2WingProgress,
            p2View, p2RestPoint.position.x, _p2FloorY, _p2Ceiling, _p2Floor);

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
    private bool TickPlayer(PlayerId id, PlayerPoseState state, ref float height, ref bool prevRaised,
        ref float wingProgress, FeatherFlightCharacterView view, float fixedX, float floorY,
        float ceiling, float floor)
    {
        bool raisedNow = state != null && state.IsTracked
            && state.IsHandRaised(rightHand: true, raiseRatio)
            && state.IsHandRaised(rightHand: false, raiseRatio);

        height -= fallSpeed * Time.deltaTime;

        // 날갯짓 1회 = 양손이 "안 든 상태"에서 "든 상태"로 바뀌는 그 프레임에만 카운트.
        if (raisedNow && !prevRaised)
            height = Mathf.Min(ceiling, height + flapBoost);

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

        hud?.SetHeight(id, SignedRatio(height, ceiling, floor));

        return height <= floor;
    }

    // 절벽(height 0)을 가운데(0)로 두고, 화면 위쪽 끝(ceiling)에서 +1, 아래쪽 끝(floor)에서
    // -1이 되도록 정규화한다. 게이지가 이 값을 그대로 좌우 위치로 쓴다.
    private static float SignedRatio(float height, float ceiling, float floor)
    {
        if (height >= 0f)
            return ceiling > 0f ? Mathf.Clamp01(height / ceiling) : 0f;
        return floor < 0f ? -Mathf.Clamp01(height / floor) : 0f;
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
        cliffDropSeconds = Mathf.Max(0f, cliffDropSeconds);
        introDropHeight = Mathf.Max(0f, introDropHeight);
        wingAnimationSmoothing = Mathf.Max(.01f, wingAnimationSmoothing);
    }
}

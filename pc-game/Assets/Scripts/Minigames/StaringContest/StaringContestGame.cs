// 눈빛 싸움 - 우가우가게임_기획_프롬프트.md "6. 눈빛 싸움" 스펙.
//
// 규칙: 조작 없음, 카메라를 계속 응시. EAR(EyeCloseTimer)이 "연속으로" earThreshold 밑에
// 있는 시간이 loseAfterClosedSeconds를 넘기면 그 사람이 즉시 패배. 연속 시간만 보면 눈을
// 감았다 뜨기를 반복해서 계속 리셋시키는 식으로 무한정 버틸 수 있으므로, 라운드 전체에서
// 누적으로 감고 있던 시간(TotalClosedDuration)이 loseAfterTotalClosedSeconds를 넘겨도 패배
// 처리한다 - 깜빡임 반복으로는 결국 못 버틴다.
//
// 캐릭터 표현: 기존 리깅/p1Head·p2Head SpriteRenderer 참조는 안 쓴다(코코넛깨기와 같은
// 이유 - 오브젝트마다 부모 스케일이 제각각이라 화면 크기가 안 맞았다). p1Anchor/p2Anchor는
// "얼굴이 놓일 자리"만 나타내는 빈 Transform이면 충분하고, FrameAnimatedCharacter가
// useWorldSpaceLayout으로 부모 스케일과 무관하게 p1Width/p2Width(월드 유닛)로 정확한
// 크기를 낸다. 표정은 eye_fight_1~N 통짜 프레임을 위험 비율에 맞춰 SetProgress로 바꾼다 -
// P1은 6장, P2는 4장처럼 장수가 달라도 ArtAssets.LoadCharacterSequence가 있는 만큼만
// 불러오므로 그대로 동작한다.
using System.Collections;
using UnityEngine;

public class StaringContestGame : MonoBehaviour
{
    [Header("판정 파라미터 (조정 가능)")]
    public float earThreshold = 0.18f;
    public float loseAfterClosedSeconds = 0.4f;
    public float loseAfterTotalClosedSeconds = 2.5f;
    public float maxMatchSeconds = 60f;
    public float resultDisplaySeconds = 2.5f;
    public float ruleBannerSeconds = 2.5f;

    [Header("얼굴 표시 크기/위치 (월드 유닛)")]
    public float p1Width = 3.25f;
    public float p2Width = 4.29f;
    public float p1YOffset = 0f;
    public float p2YOffset = 0f;
    public int faceSortingOrder = 1;

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private Transform p1Anchor; // 얼굴이 놓일 자리 - 빈 오브젝트면 충분
    [SerializeField] private Transform p2Anchor;
    [SerializeField] private EyeCloseTimer p1Timer;
    [SerializeField] private EyeCloseTimer p2Timer;
    [SerializeField] private StaringContestHud hud;
    private FrameAnimatedCharacter _p1Anim;
    private FrameAnimatedCharacter _p2Anim;
    private float _elapsed;
    private bool _ended;

    // --- 호스트-클라이언트 배선 (docs/멀티플레이_분산_아키텍처_설계.md 4장) - 판정 임계값/
    // 타이밍은 전부 그대로다. 위험도(p1Ratio/p2Ratio)는 표정이 실시간으로 바뀌어야 하므로
    // 원시 pose 스트림과 같은 성격(최신 값이 진실)으로 매 프레임 릴레이한다. "먼저 감은 쪽이
    // 패배" 판정은 반드시 호스트가 자기 타이머로만 내려야 하므로(설계 문서 6장 - 도착 순서가
    // 아니라 시각 비교가 필요한 경우) 클라이언트는 EndRound 판정 자체를 실행하지 않고
    // sc_ended 이벤트로 받은 결과만 표시한다.
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
        GameBootstrap.EnsureNetwork();

        if (p1Anchor == null || p2Anchor == null)
        {
            Debug.LogError($"{nameof(StaringContestGame)}: P1/P2 Anchor를 모두 연결해야 합니다.", this);
            enabled = false;
            return;
        }

        _p1Anim = FrameAnimatedCharacter.Attach(p1Anchor.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P1, "eye_fight"), p1Width, p1YOffset,
            sortingOrder: faceSortingOrder, useWorldSpaceLayout: true);
        _p2Anim = FrameAnimatedCharacter.Attach(p2Anchor.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P2, "eye_fight"), p2Width, p2YOffset,
            sortingOrder: faceSortingOrder, useWorldSpaceLayout: true);

        if (p1Timer != null) p1Timer.earThreshold = earThreshold;
        if (p2Timer != null) p2Timer.earThreshold = earThreshold;
        hud?.SetTimeRemaining(maxMatchSeconds);
        StartCoroutine(HideRuleBannerAfterDelay());

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            net.Subscribe("sc_ratio", OnNetRatio);
            net.Subscribe("sc_ended", OnNetEnded);
        }
    }

    private IEnumerator HideRuleBannerAfterDelay()
    {
        yield return new WaitForSeconds(ruleBannerSeconds);
        hud?.HideRuleBanner();
    }

    private void Update()
    {
        if (_ended) return;

        // Inspector에서 Play 중에 p1Width/p2Width/p1YOffset/p2YOffset을 조정하면 바로
        // 눈으로 확인할 수 있도록 매 프레임 반영한다.
        if (_p1Anim != null) { _p1Anim.Width = p1Width; _p1Anim.YOffset = p1YOffset; }
        if (_p2Anim != null) { _p2Anim.Width = p2Width; _p2Anim.YOffset = p2YOffset; }

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient)
        {
            // 클라이언트는 판정을 하지 않는다 - 위험도/결과는 전부 이벤트 핸들러가 담당하고
            // 여기서는 타이머 표시만 로컬에서 흘려보낸다.
            _elapsed += Time.deltaTime;
            hud?.SetTimeRemaining(maxMatchSeconds - _elapsed);
            return;
        }

        _elapsed += Time.deltaTime;

        float p1ContinuousRatio = (p1Timer?.ClosedDuration ?? 0f) / loseAfterClosedSeconds;
        float p2ContinuousRatio = (p2Timer?.ClosedDuration ?? 0f) / loseAfterClosedSeconds;
        float p1TotalRatio = (p1Timer?.TotalClosedDuration ?? 0f) / loseAfterTotalClosedSeconds;
        float p2TotalRatio = (p2Timer?.TotalClosedDuration ?? 0f) / loseAfterTotalClosedSeconds;
        float p1Ratio = Mathf.Max(p1ContinuousRatio, p1TotalRatio);
        float p2Ratio = Mathf.Max(p2ContinuousRatio, p2TotalRatio);
        hud?.SetDanger(PlayerId.P1, p1Ratio);
        hud?.SetDanger(PlayerId.P2, p2Ratio);
        hud?.SetTimeRemaining(maxMatchSeconds - _elapsed);
        _p1Anim?.SetProgress(p1Ratio);
        _p2Anim?.SetProgress(p2Ratio);

        if (net != null && net.IsHost)
            net.Send("sc_ratio", new RatioPayload { p1 = p1Ratio, p2 = p2Ratio });

        bool p1Closed = p1Timer != null &&
            (p1Timer.IsClosedContinuously(loseAfterClosedSeconds) || p1Timer.TotalClosedDuration >= loseAfterTotalClosedSeconds);
        bool p2Closed = p2Timer != null &&
            (p2Timer.IsClosedContinuously(loseAfterClosedSeconds) || p2Timer.TotalClosedDuration >= loseAfterTotalClosedSeconds);

        if (p1Closed || p2Closed)
        {
            // 둘 다 같은 프레임에 감겼으면 무승부 처리, 아니면 먼저 감은 쪽이 패배.
            PlayerId? winner = (p1Closed && p2Closed) ? null : (p1Closed ? PlayerId.P2 : PlayerId.P1);
            EndRound(winner);
        }
        else if (_elapsed >= maxMatchSeconds)
        {
            EndRound(null);
        }
    }

    private void OnNetRatio(NetworkEvent evt)
    {
        RatioPayload payload = NetworkSession.Read<RatioPayload>(evt);
        hud?.SetDanger(PlayerId.P1, payload.p1);
        hud?.SetDanger(PlayerId.P2, payload.p2);
        _p1Anim?.SetProgress(payload.p1);
        _p2Anim?.SetProgress(payload.p2);
    }

    private void OnNetEnded(NetworkEvent evt)
    {
        EndedPayload payload = NetworkSession.Read<EndedPayload>(evt);
        _ended = true;
        PlayerId? winner = DecodeWinner(payload.winner);
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!");
    }

    private static int EncodeWinner(PlayerId? winner) => winner == PlayerId.P1 ? 0 : winner == PlayerId.P2 ? 1 : -1;
    private static PlayerId? DecodeWinner(int value) => value == 0 ? PlayerId.P1 : value == 1 ? PlayerId.P2 : (PlayerId?)null;

    private void EndRound(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!");
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());

        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsHost)
            net.Send("sc_ended", new EndedPayload { winner = EncodeWinner(winner) });
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }

    [System.Serializable] private class RatioPayload { public float p1; public float p2; }
    [System.Serializable] private class EndedPayload { public int winner; }
}

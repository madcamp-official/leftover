// 눈빛 싸움 - 우가우가게임_기획_프롬프트.md "6. 눈빛 싸움" 스펙.
//
// 규칙: 조작 없음, 카메라를 계속 응시. EAR(EyeCloseTimer)이 "연속으로" earThreshold 밑에
// 있는 시간이 loseAfterClosedSeconds를 넘기면 그 사람이 즉시 패배. 연속 시간만 보면 눈을
// 감았다 뜨기를 반복해서 계속 리셋시키는 식으로 무한정 버틸 수 있으므로, 라운드 전체에서
// 누적으로 감고 있던 시간(TotalClosedDuration)이 loseAfterTotalClosedSeconds를 넘겨도 패배
// 처리한다 - 깜빡임 반복으로는 결국 못 버틴다.
//
// 화면은 image/games/staring_contest/의 실제 아트로 구성한다 - 다른 5개 게임과 달리 이
// 게임은 전신 캐릭터 대신 얼굴 클로즈업 두 개 + 그 사이의 시선 충돌 이펙트로 연출한다
// (해당 게임 미리보기 아트가 이 구도로 그려져 있음). "눈 감음 위험" 게이지는 두 위험 비율
// (연속/누적) 중 더 높은 쪽으로 실시간 채워진다.
//
// 표정: P1은 character1 전용 eye_fight_1~6 프레임(만화컷 통짜 이미지, 캐릭터 파츠 조립이
// 아니라 단일 스프라이트 통째로 교체)으로 위험 비율에 맞춰 바뀐다. character2는 대응하는
// eye_fight 프레임 원본이 아직 없어서(요청 시점 기준) P2는 기존 방식대로 파츠 기반
// face_eyes_closed_1~4of4 표정 교체를 그대로 쓴다.
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

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private SpriteRenderer p1Head;
    [SerializeField] private SpriteRenderer p2Head;
    [SerializeField] private EyeCloseTimer p1Timer;
    [SerializeField] private EyeCloseTimer p2Timer;
    [SerializeField] private StaringContestHud hud;
    private Sprite _p1DefaultHead, _p2DefaultHead;
    private float _elapsed;
    private bool _ended;
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _p1DefaultHead = ArtAssets.LoadCharacter(PlayerId.P1, "eye_fight_1");
        _p2DefaultHead = p2Head != null ? p2Head.sprite : ArtAssets.LoadCharacter(PlayerId.P2, "head");
        if (p1Head != null && _p1DefaultHead != null) p1Head.sprite = _p1DefaultHead;
        if (p1Timer != null) p1Timer.earThreshold = earThreshold;
        if (p2Timer != null) p2Timer.earThreshold = earThreshold;
        hud?.SetTimeRemaining(maxMatchSeconds);
        StartCoroutine(HideRuleBannerAfterDelay());
    }

    private IEnumerator HideRuleBannerAfterDelay()
    {
        yield return new WaitForSeconds(ruleBannerSeconds);
        hud?.HideRuleBanner();
    }

    private void Update()
    {
        if (_ended) return;

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
        UpdateFace(p1Head, _p1DefaultHead, PlayerId.P1, p1Ratio);
        UpdateFace(p2Head, _p2DefaultHead, PlayerId.P2, p2Ratio);

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

    // P1은 character1 전용 eye_fight_1~6 만화컷 프레임을 위험 비율에 그대로 매핑해서 쓴다
    // (파츠 조립 없이 완전한 한 장짜리 스프라이트 교체). P2는 character2용 프레임 원본이
    // 없어서 기존 파츠 기반 face_eyes_closed_1~4of4 표정 교체를 그대로 쓴다.
    private static void UpdateFace(SpriteRenderer head, Sprite defaultHead, PlayerId player, float ratio)
    {
        if (player == PlayerId.P1)
        {
            int eyeFightFrame = Mathf.Clamp(Mathf.CeilToInt(ratio * 6f), 1, 6);
            Sprite frameSprite = ArtAssets.LoadCharacter(player, $"eye_fight_{eyeFightFrame}");
            if (frameSprite != null) head.sprite = frameSprite;
            return;
        }

        if (ratio <= 0.01f)
        {
            head.sprite = defaultHead;
            return;
        }
        int frame = Mathf.Clamp(Mathf.CeilToInt(ratio * 4f), 1, 4);
        Sprite closing = ArtAssets.LoadCharacter(player, $"face_eyes_closed_{frame}of4");
        if (closing != null) head.sprite = closing;
    }

    private void EndRound(PlayerId? winner)
    {
        _ended = true;
        hud?.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!");
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

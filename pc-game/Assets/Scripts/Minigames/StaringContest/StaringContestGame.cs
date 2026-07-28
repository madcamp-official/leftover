// 눈빛 싸움 - 우가우가게임_기획_프롬프트.md "6. 눈빛 싸움" 스펙.
//
// 규칙: 조작 없음, 카메라를 계속 응시. EAR(EyeCloseTimer)이 "연속으로" earThreshold 밑에
// 있는 시간이 loseAfterClosedSeconds를 넘기면 그 사람이 즉시 패배.
//
// 화면은 image/games/staring_contest/의 실제 아트로 구성한다 - 다른 5개 게임과 달리 이
// 게임은 전신 캐릭터 대신 얼굴 클로즈업 두 개 + 그 사이의 시선 충돌 이펙트로 연출한다
// (해당 게임 미리보기 아트가 이 구도로 그려져 있음). "눈 감음 위험" 게이지는 EyeCloseTimer의
// ClosedDuration을 loseAfterClosedSeconds로 나눈 비율로 실시간 채워지고, 그 비율에 맞춰
// 얼굴 표정도 eyes_closed_1~4of4로 점점 감기는 것처럼 바뀐다.
using System.Collections;
using UnityEngine;

public class StaringContestGame : MonoBehaviour
{
    [Header("판정 파라미터 (조정 가능)")]
    public float earThreshold = 0.18f;
    public float loseAfterClosedSeconds = 0.4f;
    public float maxMatchSeconds = 60f;
    public float resultDisplaySeconds = 2.5f;
    public float ruleBannerSeconds = 2.5f;
    public float headWidth = 2.2f;

    private SpriteRenderer _p1Head, _p2Head;
    private Sprite _p1DefaultHead, _p2DefaultHead;
    private EyeCloseTimer _p1Timer;
    private EyeCloseTimer _p2Timer;
    private float _elapsed;
    private bool _ended;
    private StaringContestHud _hud;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

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

        ArtAssets.CreateBackground(cam, ArtAssets.LoadStaringContest("background"));

        _p1DefaultHead = ArtAssets.LoadCharacter(PlayerId.P1, "head");
        _p2DefaultHead = ArtAssets.LoadCharacter(PlayerId.P2, "head");
        _p1Head = SpawnHead(_p1DefaultHead, new Vector3(-1.6f, 0.2f, 0f));
        _p2Head = SpawnHead(_p2DefaultHead, new Vector3(1.6f, 0.2f, 0f));

        SpawnClashEffect();

        var p1TimerObj = new GameObject("P1 EyeTimer");
        _p1Timer = p1TimerObj.AddComponent<EyeCloseTimer>();
        _p1Timer.player = PlayerId.P1;
        _p1Timer.earThreshold = earThreshold;

        var p2TimerObj = new GameObject("P2 EyeTimer");
        _p2Timer = p2TimerObj.AddComponent<EyeCloseTimer>();
        _p2Timer.player = PlayerId.P2;
        _p2Timer.earThreshold = earThreshold;

        _hud = StaringContestHud.Build(maxMatchSeconds);
        StartCoroutine(HideRuleBannerAfterDelay());
    }

    private SpriteRenderer SpawnHead(Sprite sprite, Vector3 pos)
    {
        var go = new GameObject($"Head_{pos.x}");
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 1;
        ArtAssets.FitWidth(sr, headWidth);
        return sr;
    }

    private void SpawnClashEffect()
    {
        Sprite sprite = ArtAssets.LoadStaringContest("effect_stare_clash");
        if (sprite == null) return;
        var go = new GameObject("StareClash");
        go.transform.position = new Vector3(0f, 0.2f, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 2;
        ArtAssets.FitWidth(sr, 2f);
    }

    private IEnumerator HideRuleBannerAfterDelay()
    {
        yield return new WaitForSeconds(ruleBannerSeconds);
        _hud.HideRuleBanner();
    }

    private void Update()
    {
        if (_ended) return;

        _elapsed += Time.deltaTime;

        float p1Ratio = _p1Timer.ClosedDuration / loseAfterClosedSeconds;
        float p2Ratio = _p2Timer.ClosedDuration / loseAfterClosedSeconds;
        _hud.SetDanger(PlayerId.P1, p1Ratio);
        _hud.SetDanger(PlayerId.P2, p2Ratio);
        _hud.SetTimeRemaining(maxMatchSeconds - _elapsed);
        UpdateFace(_p1Head, _p1DefaultHead, PlayerId.P1, p1Ratio);
        UpdateFace(_p2Head, _p2DefaultHead, PlayerId.P2, p2Ratio);

        bool p1Closed = _p1Timer.IsClosedContinuously(loseAfterClosedSeconds);
        bool p2Closed = _p2Timer.IsClosedContinuously(loseAfterClosedSeconds);

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

    private static void UpdateFace(SpriteRenderer head, Sprite defaultHead, PlayerId player, float ratio)
    {
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
        _hud.ShowEvent(winner == null ? "무승부!" : $"{winner} 승리!");
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

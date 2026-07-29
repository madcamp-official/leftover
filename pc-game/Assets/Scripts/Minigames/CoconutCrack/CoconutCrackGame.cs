// 머리로 코코넛 깨기 (반복 속도 경쟁) - 우가우가게임_기획_프롬프트.md "4. 머리로 코코넛
// 깨기" 스펙.
//
// 규칙 요약 (구현됨):
//   - PoseInputHub의 Get(player).HandToHeadDistance()가 hitDistance 이하로 좁혀졌다가
//     releaseDistance 이상으로 다시 벌어지는 한 사이클을 "1회 타격"으로 카운트.
//   - matchSeconds(기본 15초) 동안 더 많이 타격한 사람이 승리 (내구도/단계 없음, 순수
//     반복 속도 경쟁).
//
// 아래는 부트스트랩 + 사이클 카운터 상태 머신 + 타격 시각 피드백(코코넛 펀치 스케일/색 플래시)
// 까지 채워둔 상태. 임계값은 실측 후 조정할 것.
//
// 캐릭터 표현: 관절 리깅 대신 coconut_1~N 프레임 시퀀스를 쓴다(FrameAnimatedCharacter).
// coconut_1이 대기 자세, 타격이 인정되는 순간 나머지 프레임을 hitAnimSeconds 동안 순서대로
// 재생한 뒤 다시 대기 자세로 돌아온다(코코넛이 쪼개지는 연출/어지러운 표정과 같은 길이로 맞춤).
using System.Collections;
using UnityEngine;

public class CoconutCrackGame : MonoBehaviour
{
    public float matchSeconds = 15f;
    public float hitDistance = 0.25f;     // 몸통 길이 대비, 이 이하면 "타격"
    public float releaseDistance = 0.45f; // 이 이상으로 벌어져야 다음 타격을 셀 준비 완료
    public float resultDisplaySeconds = 2f;
    public float coconutWidth = 0.5f; // 인게임에서 보일 코코넛 가로 폭(월드 유닛)
    public float hitAnimSeconds = 0.28f; // 코코넛 쪼개짐/어지러운 표정/캐릭터 타격 프레임이 재생되는 시간

    private Sprite _coconutSprite;
    private Sprite _coconutBreakLeftSprite;
    private Sprite _coconutBreakRightSprite;

    [Header("씬에 배치된 오브젝트")]
    [SerializeField] private CavemanSilhouette p1Silhouette;
    [SerializeField] private CavemanSilhouette p2Silhouette;
    [SerializeField] private SpriteRenderer p1Coconut;
    [SerializeField] private SpriteRenderer p2Coconut;
    [SerializeField] private CoconutBreakHud hud;
    private FrameAnimatedCharacter _p1Anim;
    private FrameAnimatedCharacter _p2Anim;
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

        _coconutSprite = ArtAssets.LoadProp("coconut");
        _coconutBreakLeftSprite = ArtAssets.LoadProp("coconut_break_left");
        _coconutBreakRightSprite = ArtAssets.LoadProp("coconut_break_right");
        _p1Anim = FrameAnimatedCharacter.Attach(p1Silhouette.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P1, "coconut"));
        _p2Anim = FrameAnimatedCharacter.Attach(p2Silhouette.gameObject,
            ArtAssets.LoadCharacterSequence(PlayerId.P2, "coconut"));

        hud?.SetTimeRemaining(matchSeconds);
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        p1Silhouette?.ApplyPose(p1);
        p2Silhouette?.ApplyPose(p2);

        if (_ended) return;

        CountHits(PlayerId.P1, p1, ref _p1ReadyToHit, ref _p1Hits, p1Coconut, p1Silhouette, _p1Anim);
        CountHits(PlayerId.P2, p2, ref _p2ReadyToHit, ref _p2Hits, p2Coconut, p2Silhouette, _p2Anim);

        _elapsed += Time.deltaTime;
        hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));
        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void CountHits(PlayerId id, PlayerPoseState state, ref bool readyToHit, ref int hitCount, SpriteRenderer coconut, CavemanSilhouette silhouette, FrameAnimatedCharacter anim)
    {
        if (state == null || !state.IsTracked) return;
        float distance = state.HandToHeadDistance();

        if (readyToHit && distance <= hitDistance)
        {
            hitCount++;
            readyToHit = false;
            hud?.SetHits(id, hitCount);
            StartCoroutine(PunchCoconut(coconut));
            if (anim != null)
                anim.PlayOnce(hitAnimSeconds); // 프레임 애니메이션이 얼굴까지 포함하므로 어지러운 표정 교체는 생략(둘 다 하면 리깅 머리가 숨겨진 상태라 표정 교체가 안 보임)
            else
                StartCoroutine(ReactToHit(silhouette)); // 프레임이 없는 폴백: 기존 리깅 표정 교체 유지
        }
        else if (!readyToHit && distance >= releaseDistance)
        {
            readyToHit = true;
        }
    }

    // 타격 순간 잠깐 어지러운 표정으로 바꿨다가 되돌린다 - coconut_bump_dizzy 표정 이미지는
    // 이미 임포트돼 있었지만(9/13번 문서 TODO) 여태 연결이 안 되어 있었다.
    private IEnumerator ReactToHit(CavemanSilhouette silhouette)
    {
        if (silhouette == null) yield break;
        silhouette.SetFace("face_coconut_bump_dizzy");
        yield return new WaitForSeconds(hitAnimSeconds);
        silhouette.ResetFace();
    }

    // 타격 순간 멀쩡한 코코넛을 잠깐 숨기고, 반으로 쪼개진 코코넛 두 조각이 좌우로 튀어나가며
    // 사라지는 연출을 보여준 뒤 다시 멀쩡한 코코넛으로 복구한다(반복 타격 경쟁이라 매번
    // "깨졌다가 새 코코넛이 나타나는" 것처럼 보이게).
    private IEnumerator PunchCoconut(SpriteRenderer coconut)
    {
        if (coconut == null) yield break;

        SpriteRenderer left = SpawnHalf(coconut.transform, _coconutBreakLeftSprite);
        SpriteRenderer right = SpawnHalf(coconut.transform, _coconutBreakRightSprite);
        coconut.enabled = false;

        float duration = hitAnimSeconds;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = elapsed / duration;
            left.transform.localPosition = Vector3.Lerp(Vector3.zero, new Vector3(-0.3f, -0.15f, 0f), p);
            left.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, -30f, p));
            right.transform.localPosition = Vector3.Lerp(Vector3.zero, new Vector3(0.3f, -0.15f, 0f), p);
            right.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0f, 30f, p));
            SetAlpha(left, 1f - p);
            SetAlpha(right, 1f - p);
            yield return null;
        }

        Destroy(left.gameObject);
        Destroy(right.gameObject);
        coconut.enabled = true;
    }

    private SpriteRenderer SpawnHalf(Transform parent, Sprite sprite)
    {
        var go = new GameObject("CoconutHalf");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 4;
        ArtAssets.FitWidth(sr, coconutWidth);
        return sr;
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
}

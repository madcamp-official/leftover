// 돌던지기 - 우가우가게임_기획_프롬프트.md "1. 돌던지기" 스펙 (기관총식 연속 발사).
//
// 규칙: 어느 손이든 들려 있는 동안 fireIntervalSeconds(기본 1초)마다 자동으로 돌이 발사된다.
// 발사 순간 오른손이 들려 있으면 상대의 오른쪽을, 왼손이 들려 있으면 상대의 왼쪽을 조준한다
// (둘 다 들려 있으면 오른손 우선). 상대는 그 순간 머리를 좌/우로 기울여 회피 - 조준 방향과
// 상대의 기울기 방향이 "같으면" 회피, "다르면"(또는 상대가 기울이지 않고 있으면) 명중.
//
// 화면은 image/games/stone_throw/의 실제 아트로 구성한다 - 배경/플레이어별 "맞은 돌"
// 네임플레이트와 image/common/ui/hud/의 남은 시간판을 사용한다(HUD 조립은 StoneThrowHud 참고).
// 이전 v1 아트에 있던
// 각도/파워/바람 HUD는 건바운드류 포격 조작을 전제로 그려진 것이라 실제 조작(손 들기 자동발사
// + 머리 기울이기 회피)과 맞지 않아 쓰지 않았고, v2에서는 아예 빠졌다.
using System.Collections;
using UnityEngine;

public class StoneThrowGame : MonoBehaviour
{
    public float fireIntervalSeconds = 1f;
    public float matchSeconds = 30f;
    public float dodgeTiltThreshold = 0.12f;
    public float stoneTravelSeconds = 0.25f;
    public float resultDisplaySeconds = 2f;
    public float stoneWidth = 0.3f; // 인게임에서 보일 돌 가로 폭(월드 유닛)

    private enum Side { Left, Right }

    private Sprite _stoneSprite;
    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _elapsed;
    private float _p1FireTimer;
    private float _p2FireTimer;
    private int _p1Hits;
    private int _p2Hits;
    private bool _ended;

    private StoneThrowHud _hud;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        _stoneSprite = ArtAssets.LoadProp("stone");

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
        cam.backgroundColor = new Color(0.79f, 0.69f, 0.53f);
        cam.clearFlags = CameraClearFlags.SolidColor;

        ArtAssets.CreateBackground(cam, ArtAssets.LoadStoneThrow("background"));

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2.5f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2.5f, 0f, 0f));

        _hud = StoneThrowHud.Build(matchSeconds);
    }

    private CavemanSilhouette Spawn(PlayerId id, Vector3 pos)
    {
        var go = new GameObject($"Caveman_{id}");
        go.transform.position = pos;
        var s = go.AddComponent<CavemanSilhouette>();
        s.player = id;
        return s;
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        PlayerPoseState p1 = hub?.Get(PlayerId.P1);
        PlayerPoseState p2 = hub?.Get(PlayerId.P2);
        _p1Silhouette.ApplyPose(p1);
        _p2Silhouette.ApplyPose(p2);

        if (_ended) return;

        _elapsed += Time.deltaTime;

        TickFiring(PlayerId.P1, p1, p2, ref _p1FireTimer);
        TickFiring(PlayerId.P2, p2, p1, ref _p2FireTimer);

        _hud?.SetTimeRemaining(Mathf.Max(0f, matchSeconds - _elapsed));

        if (_elapsed >= matchSeconds)
        {
            PlayerId? winner = _p1Hits == _p2Hits ? null : (_p1Hits > _p2Hits ? PlayerId.P1 : PlayerId.P2);
            EndMatch(winner);
        }
    }

    private void TickFiring(PlayerId thrower, PlayerPoseState throwerState, PlayerPoseState targetState, ref float timer)
    {
        if (throwerState == null || !throwerState.IsTracked)
        {
            timer = 0f;
            return;
        }

        bool rightRaised = throwerState.IsHandRaised(rightHand: true);
        bool leftRaised = throwerState.IsHandRaised(rightHand: false);
        if (!rightRaised && !leftRaised)
        {
            timer = 0f; // 손을 내리면 발사 대기를 취소 (다시 들었을 때 처음부터 한 박자 기다림)
            return;
        }

        timer += Time.deltaTime;
        if (timer < fireIntervalSeconds) return;
        timer = 0f;

        Side aimSide = rightRaised ? Side.Right : Side.Left;
        bool hit = true;
        if (targetState != null && targetState.IsTracked)
        {
            float tilt = targetState.HeadTiltRatio();
            Side? targetTiltSide = tilt > dodgeTiltThreshold ? Side.Right
                : (tilt < -dodgeTiltThreshold ? Side.Left : (Side?)null);
            hit = targetTiltSide != aimSide;
        }

        PlayerId target = thrower == PlayerId.P1 ? PlayerId.P2 : PlayerId.P1;
        if (hit)
        {
            int targetHitCount;
            if (thrower == PlayerId.P1) { _p1Hits++; targetHitCount = _p1Hits; }
            else { _p2Hits++; targetHitCount = _p2Hits; }

            // HUD의 "맞은 돌"은 각자 플레이트에 자신이 맞은 횟수를 보여준다(누가 맞혔는지가
            // 아니라 누가 맞았는지) - target의 받은 횟수는 곧 thrower가 지금까지 맞힌 횟수와
            // 같으므로 targetHitCount를 그대로 target 쪽 플레이트에 표시한다.
            _hud?.SetHits(target, targetHitCount);
            _hud?.ShowEvent($"{Label(thrower)} 명중!");
            StartCoroutine(ReactToHit(target, targetHitCount));
        }
        else
        {
            _hud?.ShowEvent($"{Label(target)} 회피!");
        }

        CavemanSilhouette throwerSil = thrower == PlayerId.P1 ? _p1Silhouette : _p2Silhouette;
        CavemanSilhouette targetSil = thrower == PlayerId.P1 ? _p2Silhouette : _p1Silhouette;
        StartCoroutine(FlyStone(throwerSil.transform.position + Vector3.up * 0.6f,
            targetSil.transform.position + Vector3.up * 0.6f, hit));
    }

    private static string Label(PlayerId id) => id == PlayerId.P1 ? "플레이어 1" : "플레이어 2";

    // 맞은 쪽 표정을 잠깐 바꿔준다 - 많이 맞을수록 이빨이 더 나간 얼굴로.
    private IEnumerator ReactToHit(PlayerId target, int timesHit)
    {
        CavemanSilhouette sil = target == PlayerId.P1 ? _p1Silhouette : _p2Silhouette;
        string face = timesHit >= 6 ? "face_stone_hit_two_teeth_broken"
            : timesHit >= 3 ? "face_stone_hit_one_tooth_broken"
            : "face_grimacing";
        sil.SetFace(face);
        yield return new WaitForSeconds(0.8f);
        sil.ResetFace();
    }

    private IEnumerator FlyStone(Vector3 from, Vector3 to, bool hit)
    {
        var go = new GameObject("Stone");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _stoneSprite;
        sr.sortingOrder = 5;
        go.transform.position = from;
        ArtAssets.FitWidth(sr, stoneWidth);

        // 명중이면 상대 위치에서 멈추고, 회피면 그대로 조금 더 지나쳐서 빗나간 느낌을 준다.
        Vector3 end = hit ? to : to + (to - from).normalized * 0.6f;
        float t = 0f;
        while (t < stoneTravelSeconds)
        {
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(from, end, t / stoneTravelSeconds);
            go.transform.Rotate(0f, 0f, 540f * Time.deltaTime);
            yield return null;
        }
        Destroy(go);
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        _hud?.SetTimeRemaining(0f);
        _hud?.ShowEvent(winner == null ? "무승부!" : $"{Label(winner.Value)} 승리!", resultDisplaySeconds);
        MatchController.Instance?.ReportRoundResult(winner);
        StartCoroutine(ProceedAfterDelay());
    }

    private IEnumerator ProceedAfterDelay()
    {
        yield return new WaitForSeconds(resultDisplaySeconds);
        MatchController.Instance?.LoadNextRound();
    }
}

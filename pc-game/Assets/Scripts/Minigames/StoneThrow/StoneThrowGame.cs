// 돌던지기 - 우가우가게임_기획_프롬프트.md "1. 돌던지기" 스펙 (기관총식 연속 발사).
//
// 규칙: 어느 손이든 들려 있는 동안 fireIntervalSeconds(기본 1초)마다 자동으로 돌이 발사된다.
// 발사 순간 오른손이 들려 있으면 상대의 오른쪽을, 왼손이 들려 있으면 상대의 왼쪽을 조준한다
// (둘 다 들려 있으면 오른손 우선). 상대는 그 순간 머리를 좌/우로 기울여 회피 - 조준 방향과
// 상대의 기울기 방향이 "같으면" 회피, "다르면"(또는 상대가 기울이지 않고 있으면) 명중.
using System.Collections;
using UnityEngine;

public class StoneThrowGame : MonoBehaviour
{
    public float fireIntervalSeconds = 1f;
    public float matchSeconds = 30f;
    public float dodgeTiltThreshold = 0.12f;
    public float stoneTravelSeconds = 0.25f;
    public float resultDisplaySeconds = 2f;

    private enum Side { Left, Right }

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private float _elapsed;
    private float _p1FireTimer;
    private float _p2FireTimer;
    private int _p1Hits;
    private int _p2Hits;
    private bool _ended;
    private string _eventText = "";
    private float _eventTextTimer;

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

        _p1Silhouette = Spawn(PlayerId.P1, new Vector3(-2.5f, 0f, 0f));
        _p2Silhouette = Spawn(PlayerId.P2, new Vector3(2.5f, 0f, 0f));
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

        if (_eventTextTimer > 0f)
        {
            _eventTextTimer -= Time.deltaTime;
            if (_eventTextTimer <= 0f) _eventText = "";
        }

        if (_ended) return;

        _elapsed += Time.deltaTime;

        TickFiring(PlayerId.P1, p1, p2, ref _p1FireTimer);
        TickFiring(PlayerId.P2, p2, p1, ref _p2FireTimer);

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
            if (thrower == PlayerId.P1) _p1Hits++; else _p2Hits++;
            _eventText = $"{thrower} 명중!";
        }
        else
        {
            _eventText = $"{target} 회피!";
        }
        _eventTextTimer = 0.6f;

        CavemanSilhouette throwerSil = thrower == PlayerId.P1 ? _p1Silhouette : _p2Silhouette;
        CavemanSilhouette targetSil = thrower == PlayerId.P1 ? _p2Silhouette : _p1Silhouette;
        StartCoroutine(FlyStone(throwerSil.transform.position + Vector3.up * 0.6f,
            targetSil.transform.position + Vector3.up * 0.6f, hit));
    }

    private IEnumerator FlyStone(Vector3 from, Vector3 to, bool hit)
    {
        var go = new GameObject("Stone");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RuntimeSpriteFactory.CreateCircle(18, new Color(0.45f, 0.35f, 0.25f));
        sr.sortingOrder = 5;
        go.transform.position = from;

        // 명중이면 상대 위치에서 멈추고, 회피면 그대로 조금 더 지나쳐서 빗나간 느낌을 준다.
        Vector3 end = hit ? to : to + (to - from).normalized * 0.6f;
        float t = 0f;
        while (t < stoneTravelSeconds)
        {
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(from, end, t / stoneTravelSeconds);
            yield return null;
        }
        Destroy(go);
    }

    private void EndMatch(PlayerId? winner)
    {
        _ended = true;
        _eventText = winner == null ? "무승부!" : $"{winner} 승리!";
        _eventTextTimer = resultDisplaySeconds;
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
        GUI.Label(new Rect(20, 20, 300, 30), $"P1 hits: {_p1Hits}   P2 hits: {_p2Hits}");
        GUI.Label(new Rect(20, 50, 300, 30), $"남은 시간: {Mathf.Max(0f, matchSeconds - _elapsed):F0}초");

        if (!string.IsNullOrEmpty(_eventText))
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.UpperCenter,
                normal = { textColor = Color.black },
            };
            GUI.Label(new Rect(0, 90, Screen.width, 40), _eventText, style);
        }
    }
}

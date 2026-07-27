// 눈빛 싸움 - 조작 없음, 그냥 카메라를 응시. 다른 5개 미니게임을 담당할 협업자를 위한
// "완전히 동작하는 예시"로 가장 먼저 구현했다: PoseInputHub를 어떻게 읽고, CavemanSilhouette를
// 어떻게 붙이고, MatchController에 결과를 어떻게 보고하는지 전부 이 파일 하나에 들어있다.
// 저능아게임_기획_프롬프트.md "6. 눈빛 싸움" 스펙 그대로.
using System.Collections;
using UnityEngine;

public class StaringContestGame : MonoBehaviour
{
    [Header("판정 파라미터 (조정 가능)")]
    public float earThreshold = 0.18f;
    public float loseAfterClosedSeconds = 0.4f;
    public float maxMatchSeconds = 60f;
    public float resultDisplaySeconds = 2.5f;

    private CavemanSilhouette _p1Silhouette;
    private CavemanSilhouette _p2Silhouette;
    private EyeCloseTimer _p1Timer;
    private EyeCloseTimer _p2Timer;
    private float _elapsed;
    private bool _ended;
    private string _bannerText = "";

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
        cam.backgroundColor = new Color(0.85f, 0.9f, 0.95f);
        cam.clearFlags = CameraClearFlags.SolidColor;

        _p1Silhouette = SpawnPlayer(PlayerId.P1, new Vector3(-1.4f, 0f, 0f));
        _p2Silhouette = SpawnPlayer(PlayerId.P2, new Vector3(1.4f, 0f, 0f));

        var p1TimerObj = new GameObject("P1 EyeTimer");
        _p1Timer = p1TimerObj.AddComponent<EyeCloseTimer>();
        _p1Timer.player = PlayerId.P1;
        _p1Timer.earThreshold = earThreshold;

        var p2TimerObj = new GameObject("P2 EyeTimer");
        _p2Timer = p2TimerObj.AddComponent<EyeCloseTimer>();
        _p2Timer.player = PlayerId.P2;
        _p2Timer.earThreshold = earThreshold;

        _bannerText = "눈빛 싸움 - 먼저 눈을 감으면 진다!";
    }

    private CavemanSilhouette SpawnPlayer(PlayerId id, Vector3 position)
    {
        var go = new GameObject($"Caveman_{id}");
        go.transform.position = position;
        var silhouette = go.AddComponent<CavemanSilhouette>();
        silhouette.player = id;
        return silhouette;
    }

    private void Update()
    {
        PoseInputHub hub = PoseInputHub.Instance;
        _p1Silhouette.ApplyPose(hub?.Get(PlayerId.P1));
        _p2Silhouette.ApplyPose(hub?.Get(PlayerId.P2));

        if (_ended) return;

        _elapsed += Time.deltaTime;
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

    private void EndRound(PlayerId? winner)
    {
        _ended = true;
        _bannerText = winner == null ? "무승부!" : (winner == PlayerId.P1 ? "P1 승리!" : "P2 승리!");
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
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.UpperCenter,
            normal = { textColor = Color.black },
        };
        GUI.Label(new Rect(0, 20, Screen.width, 60), _bannerText, style);

        var timerStyle = new GUIStyle(style) { fontSize = 18 };
        float remaining = Mathf.Max(0f, maxMatchSeconds - _elapsed);
        GUI.Label(new Rect(0, 80, Screen.width, 30), $"제한시간 {remaining:F0}초", timerStyle);
    }
}

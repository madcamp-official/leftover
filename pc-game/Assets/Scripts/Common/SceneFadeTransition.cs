// 모든 씬 전환에 공통으로 쓰는 검은색 페이드 오버레이.
// RuntimeInitializeOnLoadMethod로 최초 씬보다 먼저 생성되고 DontDestroyOnLoad로 유지된다.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SceneFadeTransition : MonoBehaviour
{
    private static SceneFadeTransition _instance;

    // 플레이 중 스크립트 리로드로 Awake()가 다시 안 불려도 static 참조가 끊기지 않도록
    // null이면 씬에서 다시 찾는다 (PoseInputHub와 동일한 이유 - 실측으로 확인된 문제).
    public static SceneFadeTransition Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<SceneFadeTransition>();
            return _instance;
        }
        private set => _instance = value;
    }

    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.45f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.45f;
    [SerializeField, Min(0f)] private float blackHoldSeconds = 0.08f;
    [SerializeField, Min(0.1f)] private float countdownStepSeconds = 0.9f;

    private CanvasGroup _group;
    private LoadingScreenController _loadingScreen;
    private bool _isTransitioning;

    // --- 라운드 시작 동시 카운트다운 (2인 플레이 전용) ---
    // 로딩 화면이 닫힌 뒤에도 씬 로딩 자체(에셋 로드 속도, 기기 성능 차이)에서 호스트와
    // 클라이언트 사이에 실제 시간차가 또 생길 수 있다 - 로딩 화면 단계의 배리어만으로는
    // "게임 화면에 진입하는 순간"까지 완벽히 맞추기 어렵다는 게 실측으로 확인됐다. 그래서
    // 매 판정을 정밀하게 동기화하려 하지 않고, 대신 씬이 각자 다 뜬 뒤 서로 "나 준비됐다"를
    // 주고받고 나서 화면 가운데 큰 카운트다운을 함께 보여주는 방식으로 바꿨다 - 카운트다운
    // 자체가 몇백ms 어긋나도 5초 안에서는 티가 안 나고, 카운트다운이 도는 동안
    // Time.timeScale=0으로 얼려서(NetworkReconnectOverlay와 같은 방식 - 미니게임 코드를
    // 하나도 안 건드리고 Time.deltaTime 기반 로직 전부를 한 번에 멈출 수 있다) 실제 게임
    // 진행(라운드 타이머, 판정)은 카운트다운이 끝나야 시작된다.
    private const string RoundReadyEvent = "round_ready";
    private bool _roundReadySubscribed;
    // 상대가 확인한 가장 최신 라운드 번호 - bool 하나로 "확인됨/안 됨"만 추적했을 때는,
    // 내가 이번 라운드 대기를 "시작하는 시점"에 그 값을 false로 초기화해야 했는데, 상대가
    // 나보다 먼저 이 지점에 도달해서 이미 자기 신호를 보내놓은 경우 그 신호가 내 초기화로
    // 지워져버리는 레이스 컨디션이 있었다(실측으로 확인된, "P2 카운트다운이 계속 늦게
    // 시작한다"의 원인) - 이번 라운드 번호(MatchController.CurrentRoundIndex)와 비교하는
    // 방식으로 바꿔서, 상대 신호가 내가 기다리기 시작하기 전에 도착해도 절대 사라지지 않는다.
    private int _otherReadyRound = -1;
    private GameObject _countdownRoot;
    private Text _countdownText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() => EnsureInstance();

    private static SceneFadeTransition EnsureInstance()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("SceneFadeTransition");
        return go.AddComponent<SceneFadeTransition>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlay();
        EnsureLoadingScreen();
        EnsureCountdownSubscribed();
    }

    private IEnumerator Start()
    {
        // 에디터에서 어느 씬을 직접 Play해도 검은 화면에서 자연스럽게 시작한다.
        _group.alpha = 1f;
        _group.blocksRaycasts = true;
        yield return HoldBlackFrame();
        yield return FadeTo(0f, fadeInSeconds);
        _group.blocksRaycasts = false;
    }

    public static bool TryLoadScene(string sceneName)
    {
        SceneFadeTransition transition = EnsureInstance();
        if (transition._isTransitioning) return false;
        transition.StartCoroutine(transition.LoadRoutine(sceneName));
        return true;
    }

    private IEnumerator LoadRoutine(string sceneName)
    {
        EnsureRuntimeObjects();
        _isTransitioning = true;
        _group.blocksRaycasts = true;
        yield return FadeTo(1f, fadeOutSeconds);

        bool showLoadingScreen = sceneName != MatchController.HubSceneName;
        AsyncOperation load;

        if (showLoadingScreen)
        {
            GameBgm.PlayLoadingScreen();
            _loadingScreen.Show(sceneName);
            yield return FadeTo(0f, fadeInSeconds);

            load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
            {
                Debug.LogError($"[SceneTransition] 씬을 읽을 수 없습니다: {sceneName}");
                _loadingScreen.Hide();
                GameBgm.ResumeActiveScene();
                _group.blocksRaycasts = false;
                _isTransitioning = false;
                yield break;
            }

            load.allowSceneActivation = false;
            while (load.progress < 0.9f || !_loadingScreen.IsReady)
                yield return null;

            yield return FadeTo(1f, fadeOutSeconds);
            _loadingScreen.Hide();
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
        }
        else
        {
            load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
            {
                Debug.LogError($"[SceneTransition] 씬을 읽을 수 없습니다: {sceneName}");
                _group.blocksRaycasts = false;
                _isTransitioning = false;
                yield break;
            }
            while (!load.isDone) yield return null;
        }

        // 새 씬이 로드된 뒤에도 검정을 다시 확정하고 실제 한 프레임을 렌더한다. 이렇게 해야
        // 새 화면이 잠깐 노출된 다음 어두워지는 현상 없이 반드시 "검정 → 새 화면 fade in"이 된다.
        _group.alpha = 1f;
        yield return HoldBlackFrame();

        yield return FadeTo(0f, fadeInSeconds);
        _group.blocksRaycasts = false;

        // Hub는 상대를 기다릴 필요가 없다(결과 화면/시작 화면) - 실제 미니게임 씬에서만
        // 동시 시작 카운트다운을 돌린다.
        if (showLoadingScreen)
            yield return RunSynchronizedCountdown();

        _isTransitioning = false;
    }

    // 씬이 각자 다 뜬 뒤 서로 "나 준비됐다"를 주고받고, 둘 다 확인되면 화면 가운데 큰
    // 카운트다운을 함께 보여준다. 오프라인/솔로는 기다릴 상대가 없으므로 곧장 통과.
    private IEnumerator RunSynchronizedCountdown()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null || !net.IsNetworked) yield break;

        EnsureCountdownSubscribed();
        EnsureCountdownUi();

        int myRound = MatchController.Instance != null ? MatchController.Instance.CurrentRoundIndex : 0;
        Debug.Log($"[SyncStart:{net.Role}] round={myRound} 대기 시작, timeScale=0으로 얼림");

        // "상대방을 기다리는 중" 문구가 떠 있는 동안에도 이미 로드된 씬의 미니게임 로직
        // (타이머/판정)이 계속 돌고 있던 버그가 있었다(실측 확인) - 예전엔 이 줄이 대기 루프
        // "뒤"에 있어서, 대기 시간이 긴 쪽만 그만큼 게임이 먼저 진행돼버렸다. 대기를 시작하는
        // 바로 이 순간부터 얼려야 대기 시간 자체가 어느 쪽에도 유·불리하게 작용하지 않는다.
        Time.timeScale = 0f;
        _countdownRoot.SetActive(true);
        _countdownText.text = "상대방을 기다리는 중...";
        net.Send(RoundReadyEvent, new RoundReadyPayload { round = myRound });

        // 네트워크 문제로 상대 신호가 영영 안 오면(연결이 끊겼다면 NetworkReconnectOverlay가
        // 따로 처리한다) 여기서 무한정 멈추지 않고 최선을 다해 진행한다.
        const float waitTimeoutSeconds = 10f;
        float waited = 0f;
        while (_otherReadyRound < myRound && waited < waitTimeoutSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        bool timedOut = _otherReadyRound < myRound;
        Debug.Log($"[SyncStart:{net.Role}] round={myRound} 대기 끝 (waited={waited:F2}s, " +
            $"{(timedOut ? "타임아웃" : "상대 확인됨")}) - 카운트다운 시작");

        string[] steps = { "3", "2", "1", "시작!" };
        foreach (string label in steps)
        {
            _countdownText.text = label;
            float t = 0f;
            while (t < countdownStepSeconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        Time.timeScale = 1f;
        _countdownRoot.SetActive(false);
        Debug.Log($"[SyncStart:{net.Role}] round={myRound} 카운트다운 끝, timeScale=1로 복원, 게임 시작");
    }

    private void EnsureCountdownSubscribed()
    {
        if (_roundReadySubscribed) return;
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;
        net.Subscribe(RoundReadyEvent, OnNetRoundReady);
        _roundReadySubscribed = true;
    }

    // 내가 아직 이번 라운드의 RunSynchronizedCountdown을 시작하기도 전에 상대 신호가 먼저
    // 도착할 수 있다(상대 쪽 씬 로딩이 더 빨랐을 때) - 그래도 절대 유실되지 않도록, "가장 최신
    // 라운드 번호"만 갱신할 뿐 여기서는 아무것도 초기화하지 않는다.
    private void OnNetRoundReady(NetworkEvent evt)
    {
        RoundReadyPayload payload = NetworkSession.Read<RoundReadyPayload>(evt);
        if (payload.round > _otherReadyRound)
        {
            _otherReadyRound = payload.round;
            NetworkSession net = NetworkSession.Instance;
            Debug.Log($"[SyncStart:{(net != null ? net.Role.ToString() : "?")}] 상대의 round_ready 수신: round={payload.round}");
        }
    }

    private void EnsureCountdownUi()
    {
        if (_countdownRoot != null) return;

        var go = new GameObject("RoundStartCountdown");
        go.transform.SetParent(transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var shadeGo = new GameObject("Shade");
        shadeGo.transform.SetParent(rt, false);
        var shadeRt = shadeGo.AddComponent<RectTransform>();
        shadeRt.anchorMin = Vector2.zero;
        shadeRt.anchorMax = Vector2.one;
        shadeRt.offsetMin = Vector2.zero;
        shadeRt.offsetMax = Vector2.zero;
        var shadeImage = shadeGo.AddComponent<Image>();
        shadeImage.color = new Color(0f, 0f, 0f, 0.45f);
        shadeImage.raycastTarget = false;

        _countdownText = HudWidgets.CreateText(rt, "CountdownText", new Vector2(0.5f, 0.5f), 800f, 220);

        _countdownRoot = go;
        _countdownRoot.SetActive(false);
    }

    [System.Serializable] private class RoundReadyPayload { public int round; }

    private IEnumerator HoldBlackFrame()
    {
        _group.alpha = 1f;
        Canvas.ForceUpdateCanvases();
        yield return new WaitForEndOfFrame();
        if (blackHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(blackHoldSeconds);
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        float start = _group.alpha;
        if (duration <= 0f) { _group.alpha = target; yield break; }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _group.alpha = Mathf.Lerp(start, target, t * t * (3f - 2f * t));
            yield return null;
        }
        _group.alpha = target;
    }

    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        gameObject.AddComponent<GraphicRaycaster>();

        var imageGo = new GameObject("BlackOverlay");
        imageGo.transform.SetParent(transform, false);
        _group = imageGo.AddComponent<CanvasGroup>();
        var rt = imageGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var image = imageGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;
    }

    // Play 중 스크립트 리로드 뒤에는 Awake가 다시 호출되지 않을 수 있다. 그 상태에서도
    // 기존 BlackOverlay를 다시 찾고 로딩 UI가 없으면 즉시 재생성한다.
    private void EnsureRuntimeObjects()
    {
        if (_group == null)
        {
            Transform black = transform.Find("BlackOverlay");
            if (black == null)
            {
                BuildOverlay();
            }
            else
            {
                _group = black.GetComponent<CanvasGroup>();
                if (_group == null) _group = black.gameObject.AddComponent<CanvasGroup>();
            }
        }
        EnsureLoadingScreen();
        EnsureCountdownSubscribed();
    }

    private void EnsureLoadingScreen()
    {
        if (_loadingScreen != null && _loadingScreen.gameObject != gameObject) return;

        LoadingScreenController existing = FindAnyObjectByType<LoadingScreenController>();
        if (existing != null && existing.gameObject != gameObject)
        {
            _loadingScreen = existing;
            return;
        }

        var loadingGo = new GameObject("LoadingScreenController");
        DontDestroyOnLoad(loadingGo);
        _loadingScreen = loadingGo.AddComponent<LoadingScreenController>();
    }
}

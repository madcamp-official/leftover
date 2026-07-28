// 모든 씬 전환에 공통으로 쓰는 검은색 페이드 오버레이.
// RuntimeInitializeOnLoadMethod로 최초 씬보다 먼저 생성되고 DontDestroyOnLoad로 유지된다.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SceneFadeTransition : MonoBehaviour
{
    public static SceneFadeTransition Instance { get; private set; }

    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.45f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.45f;
    [SerializeField, Min(0f)] private float blackHoldSeconds = 0.08f;

    private CanvasGroup _group;
    private bool _isTransitioning;

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
        _isTransitioning = true;
        _group.blocksRaycasts = true;
        yield return FadeTo(1f, fadeOutSeconds);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (!load.isDone) yield return null;

        // 새 씬이 로드된 뒤에도 검정을 다시 확정하고 실제 한 프레임을 렌더한다. 이렇게 해야
        // 새 화면이 잠깐 노출된 다음 어두워지는 현상 없이 반드시 "검정 → 새 화면 fade in"이 된다.
        _group.alpha = 1f;
        yield return HoldBlackFrame();

        yield return FadeTo(0f, fadeInSeconds);
        _group.blocksRaycasts = false;
        _isTransitioning = false;
    }

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
        _group = gameObject.AddComponent<CanvasGroup>();

        var imageGo = new GameObject("BlackOverlay");
        imageGo.transform.SetParent(transform, false);
        var rt = imageGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var image = imageGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;
    }
}

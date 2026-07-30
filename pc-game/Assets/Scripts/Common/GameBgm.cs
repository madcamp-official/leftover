using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// docs/Cartoon_BGM_Asset_Guide.pdf와 트랙 매핑 TXT에 정의된 게임별 BGM을 관리한다.
// 씬마다 AudioSource를 배치하지 않고 하나의 DontDestroyOnLoad 재생기를 유지해 로딩 화면을
// 지나가는 동안 음악이 끊기거나 같은 곡이 중복 재생되지 않게 한다.
public sealed class GameBgm : MonoBehaviour
{
    private const string ResourceFolder = "Audio/BGM/";
    private const string MusicEnabledKey = "settings.music.enabled";
    private const string MusicVolumeKey = "settings.music.volume";
    private const float DefaultVolume = 0.35f;
    private const float FadeSeconds = 0.3f;
    private const string LoadingTrack = "loading";
    private const float LoadingTrackStartSeconds = 81f;

    private static readonly IReadOnlyDictionary<string, string> SceneTracks =
        new Dictionary<string, string>
        {
            // Track 1. 투척의 달인
            { "StoneThrow", "throw_master" },
            { "StoneOrBanana", "throw_master" },

            // Track 2. 코코넛 크러셔
            { "CoconutCrack", "coconut_crusher" },

            // Track 3. 아슬아슬 체공 시간
            { "FruitJump", "airtime" },
            { "FeatherFlight", "airtime" },

            // Track 4. 째깍째깍 눈치싸움
            { "StaringContest", "standoff" },

            // Track 5. 고성방가 챔피언
            { "ScreamDuel", "scream_champion" },
        };

    private static GameBgm _instance;
    private AudioSource _source;
    private Coroutine _transition;
    private string _currentTrack;

    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicEnabledKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(MusicEnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (_instance != null) _instance.ApplyEnabledState();
        }
    }

    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume));
        set
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            if (_instance != null && _instance._transition == null)
                _instance._source.volume = MusicEnabled ? MusicVolume : 0f;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        GameBgm existing = FindAnyObjectByType<GameBgm>();
        if (existing != null)
        {
            _instance = existing;
            return;
        }

        var go = new GameObject(nameof(GameBgm));
        _instance = go.AddComponent<GameBgm>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = true;
        _source.spatialBlend = 0f;
        _source.ignoreListenerPause = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start() => PlayForScene(SceneManager.GetActiveScene().name);

    private void Update()
    {
        // 로딩곡은 1:21 이후 구간만 사용한다. AudioSource.loop를 켜면 곡이 끝난 뒤 0초로
        // 돌아가므로, 로딩곡만 직접 1:21로 되돌려 같은 구간을 반복한다.
        if (_currentTrack == LoadingTrack && _transition == null &&
            _source.clip != null && !_source.isPlaying)
        {
            PlayCurrentClipFrom(LoadingTrackStartSeconds);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PlayForScene(scene.name);

    public static void PlayLoadingScreen()
    {
        if (_instance == null) Bootstrap();
        _instance.PlayTrack(LoadingTrack);
    }

    public static void ResumeActiveScene()
    {
        if (_instance == null) return;
        _instance.PlayForScene(SceneManager.GetActiveScene().name);
    }

    private void PlayForScene(string sceneName)
    {
        PlayTrack(ResolveTrack(sceneName));
    }

    private void PlayTrack(string track)
    {
        if (_currentTrack == track && _source.clip != null && _source.isPlaying)
        {
            ApplyEnabledState();
            return;
        }

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(TransitionTo(track));
    }

    private static string ResolveTrack(string sceneName)
    {
        if (sceneName == MatchController.HubSceneName)
        {
            // 마지막 라운드 뒤 Hub는 시작 화면이 아니라 최종 결과 화면이므로 엔딩곡을 쓰고,
            // 그 전(처음 시작 화면)에는 별도의 시작 화면 곡을 쓴다.
            bool matchComplete = MatchController.Instance != null && MatchController.Instance.IsMatchComplete;
            return matchComplete ? "ending" : "start";
        }

        return SceneTracks.TryGetValue(sceneName, out string track) ? track : null;
    }

    private IEnumerator TransitionTo(string track)
    {
        float startVolume = _source.volume;
        float elapsed = 0f;
        while (_source.isPlaying && elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            _source.volume = Mathf.Lerp(startVolume, 0f, elapsed / FadeSeconds);
            yield return null;
        }

        _source.Stop();
        _source.clip = null;
        _currentTrack = track;
        if (string.IsNullOrEmpty(track))
        {
            _source.volume = 0f;
            _transition = null;
            yield break;
        }

        AudioClip clip = Resources.Load<AudioClip>(ResourceFolder + track);
        if (clip == null)
        {
            Debug.LogWarning($"BGM을 찾을 수 없습니다: Resources/{ResourceFolder}{track}");
            _transition = null;
            yield break;
        }

        _source.clip = clip;
        _source.loop = track != LoadingTrack;
        _source.volume = 0f;
        PlayCurrentClipFrom(track == LoadingTrack ? LoadingTrackStartSeconds : 0f);

        float targetVolume = MusicEnabled ? MusicVolume : 0f;
        elapsed = 0f;
        while (elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            _source.volume = Mathf.Lerp(0f, targetVolume, elapsed / FadeSeconds);
            yield return null;
        }

        _source.volume = targetVolume;
        _transition = null;
    }

    private void PlayCurrentClipFrom(float startSeconds)
    {
        if (_source.clip == null) return;

        float safeStart = Mathf.Clamp(startSeconds, 0f, Mathf.Max(0f, _source.clip.length - 0.05f));
        _source.time = safeStart;
        _source.Play();
    }

    private void ApplyEnabledState()
    {
        if (_source == null) return;
        _source.mute = !MusicEnabled;
        if (MusicEnabled && _transition == null) _source.volume = MusicVolume;
    }
}

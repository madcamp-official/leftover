// 선택된 미니게임을 순서대로 진행하는 매치 관리자. 씬을 넘나들어야 하므로
// DontDestroyOnLoad 싱글턴으로 두고,
// 각 미니게임 씬은 끝날 때 ReportRoundResult()만 호출하면 된다 - 다음 라운드 로드/최종
// 결과 집계는 전부 여기서 처리하므로 미니게임 쪽은 "누가 이겼는지"만 알면 된다.
using System.Collections.Generic;
using UnityEngine;

public sealed class MatchController : MonoBehaviour
{
    private static MatchController _instance;

    // 플레이 중 스크립트 리로드로 Awake()가 다시 안 불려도 static 참조가 끊기지 않도록
    // null이면 씬에서 다시 찾는다 (PoseInputHub와 동일한 이유 - 실측으로 확인된 문제).
    public static MatchController Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<MatchController>();
            return _instance;
        }
        private set => _instance = value;
    }

    // 우가우가게임_기획_프롬프트.md 표 순서 그대로. 씬 이름은 Build Settings에 등록된
    // 미니게임 씬 이름과 정확히 같아야 한다.
    public static readonly IReadOnlyList<string> RoundScenes = new List<string>
    {
        "StoneThrow",
        "FruitJump",
        "CoconutCrack",
        "StoneOrBanana",
        "StaringContest",
        "ScreamDuel",
        "FeatherFlight",
    };

    public const string HubSceneName = "Hub";

    public int CurrentRoundIndex { get; private set; } = -1;
    public int P1Wins { get; private set; }
    public int P2Wins { get; private set; }
    public int ResultsVersion { get; private set; }
    public IReadOnlyList<string> ActiveRoundScenes => _activeRoundScenes;

    // RoundScenes.Count로 초기화해서, 라운드를 추가/제거할 때마다 이 배열 크기를 손으로
    // 맞춰야 하는 실수를 원천 차단한다(과거에 하드코딩 6으로 두었다가 게임을 추가하면서
    // IndexOutOfRange가 날 뻔한 적이 있음 - docs/멀티플레이_분산_아키텍처_설계.md 9장 참고).
    private readonly PlayerId?[] _roundWinners = new PlayerId?[RoundScenes.Count];
    private readonly bool[] _roundReported = new bool[RoundScenes.Count];
    private readonly List<string> _activeRoundScenes = new List<string>(RoundScenes);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 호스트-클라이언트 모드(docs/멀티플레이_분산_아키텍처_설계.md)에서는 판정/진행 결정이
    // 전부 호스트 전담이다. 클라이언트 쪽 MatchController는 스스로 결정하지 않고, 호스트가
    // 보낸 이벤트를 받아 같은 상태를 반영만 한다(HubController 등 UI는 양쪽 다 동일 코드로
    // 이 상태를 읽으면 되게 하기 위함). 오프라인(NetworkSession.Instance == null 또는
    // Role == Offline)일 때는 기존과 동일하게 로컬에서 전부 결정한다.
    private void Start()
    {
        GameBootstrap.EnsureNetwork();
        NetworkSession net = NetworkSession.Instance;
        if (net == null) return;
        net.Subscribe("match_start", OnNetMatchStart);
        net.Subscribe("load_round", OnNetLoadRound);
        net.Subscribe("round_result", OnNetRoundResult);
    }

    public void StartMatch()
        => StartMatchInternal(null);

    public bool StartMatch(IReadOnlyList<string> selectedScenes)
        => StartMatchInternal(selectedScenes);

    private bool StartMatchInternal(IReadOnlyList<string> selectedScenes)
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient) return false; // 클라이언트는 호스트 이벤트로만 시작한다

        if (selectedScenes != null && !TryApplyRoundSelection(selectedScenes))
        {
            Debug.LogWarning("[Match] 최소 한 개 이상의 미니게임을 선택해야 합니다.");
            return false;
        }

        ResetLocalMatchState();
        if (net != null && net.IsHost)
            net.Send("match_start", new MatchStartPayload { scenes = _activeRoundScenes.ToArray() });
        LoadNextRound();
        return true;
    }

    private void ResetLocalMatchState()
    {
        CurrentRoundIndex = -1;
        P1Wins = 0;
        P2Wins = 0;
        for (int i = 0; i < _roundWinners.Length; i++)
        {
            _roundWinners[i] = null;
            _roundReported[i] = false;
        }
        ResultsVersion++;
    }

    private void OnNetMatchStart(NetworkEvent evt)
    {
        MatchStartPayload payload = NetworkSession.Read<MatchStartPayload>(evt);
        if (payload?.scenes == null || !TryApplyRoundSelection(payload.scenes))
        {
            Debug.LogWarning("[Match] 호스트의 게임 선택 목록이 비어 있어 7개 전체로 복구합니다.");
            ApplyAllRounds();
        }
        ResetLocalMatchState();
    }

    public void LoadNextRound()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient) return; // 클라이언트는 호스트의 load_round 이벤트로만 전환한다

        int nextRoundIndex = CurrentRoundIndex + 1;
        string nextScene = nextRoundIndex >= _activeRoundScenes.Count
            ? HubSceneName
            : _activeRoundScenes[nextRoundIndex];

        if (net != null && net.IsHost)
            net.Send("load_round", new LoadRoundPayload
            {
                roundIndex = nextRoundIndex,
                sceneName = nextScene,
                scenes = _activeRoundScenes.ToArray(),
            });

        // 더블클릭이나 중복 코루틴으로 전환 요청이 겹치면 인덱스도 두 번 증가하지 않게 한다.
        if (SceneFadeTransition.TryLoadScene(nextScene))
            CurrentRoundIndex = nextRoundIndex;
    }

    private void OnNetLoadRound(NetworkEvent evt)
    {
        LoadRoundPayload payload = NetworkSession.Read<LoadRoundPayload>(evt);
        if (payload == null || string.IsNullOrEmpty(payload.sceneName))
        {
            Debug.LogWarning("[Match] 잘못된 load_round 이벤트를 무시합니다.");
            return;
        }
        // 재접속 직후 match_start를 못 받은 클라이언트도 다음 라운드 메시지만으로 호스트의
        // 선택 목록을 복원할 수 있게 매 load_round에 같은 목록을 싣는다.
        if (payload.scenes != null && payload.scenes.Length > 0)
            TryApplyRoundSelection(payload.scenes);
        if (SceneFadeTransition.TryLoadScene(payload.sceneName))
            CurrentRoundIndex = payload.roundIndex;
    }

    // null = 무승부(승자 없음). 각 미니게임은 규칙에 따라 승자가 정해지는 순간 이걸 한 번만
    // 호출하고, 이후 알아서 다음 라운드로 넘어가거나(자동 진행) Hub 씬의 버튼을 기다리면 된다.
    // 호스트-클라이언트 모드에서는 판정이 호스트 전담이므로(설계 문서 1장), 클라이언트 쪽
    // 미니게임이 이걸 호출해도 무시된다 - 클라이언트는 round_result 이벤트로만 결과를 안다.
    public void ReportRoundResult(PlayerId? winner)
    {
        NetworkSession net = NetworkSession.Instance;
        if (net != null && net.IsClient) return;

        ApplyRoundResult(CurrentRoundIndex, winner);
        if (net != null && net.IsHost)
            net.Send("round_result", new RoundResultPayload
            {
                roundIndex = CurrentRoundIndex,
                winner = EncodeWinner(winner),
            });
    }

    private void OnNetRoundResult(NetworkEvent evt)
    {
        RoundResultPayload payload = NetworkSession.Read<RoundResultPayload>(evt);
        ApplyRoundResult(payload.roundIndex, DecodeWinner(payload.winner));
    }

    private void ApplyRoundResult(int roundIndex, PlayerId? winner)
    {
        if (roundIndex < 0 || roundIndex >= _activeRoundScenes.Count) return;
        if (_roundReported[roundIndex]) return;

        _roundReported[roundIndex] = true;
        _roundWinners[roundIndex] = winner;
        if (winner == PlayerId.P1) P1Wins++;
        else if (winner == PlayerId.P2) P2Wins++;
        ResultsVersion++;
    }

    private static int EncodeWinner(PlayerId? winner) => winner == PlayerId.P1 ? 0 : winner == PlayerId.P2 ? 1 : -1;
    private static PlayerId? DecodeWinner(int value) => value == 0 ? PlayerId.P1 : value == 1 ? PlayerId.P2 : (PlayerId?)null;

    public bool HasRoundResult(int roundIndex)
        => roundIndex >= 0 && roundIndex < _activeRoundScenes.Count && _roundReported[roundIndex];

    public PlayerId? RoundWinner(int roundIndex)
        => roundIndex >= 0 && roundIndex < _activeRoundScenes.Count ? _roundWinners[roundIndex] : null;

    public bool IsMatchComplete => CurrentRoundIndex >= _activeRoundScenes.Count;

    public PlayerId? OverallWinner()
    {
        if (P1Wins == P2Wins) return null;
        return P1Wins > P2Wins ? PlayerId.P1 : PlayerId.P2;
    }

    private bool TryApplyRoundSelection(IEnumerable<string> selectedScenes)
    {
        var requested = new HashSet<string>(selectedScenes);
        var ordered = new List<string>();
        foreach (string scene in RoundScenes)
        {
            if (requested.Contains(scene)) ordered.Add(scene);
        }

        if (ordered.Count == 0) return false;
        _activeRoundScenes.Clear();
        _activeRoundScenes.AddRange(ordered);
        return true;
    }

    private void ApplyAllRounds()
    {
        _activeRoundScenes.Clear();
        foreach (string scene in RoundScenes) _activeRoundScenes.Add(scene);
    }

    [System.Serializable]
    private class MatchStartPayload
    {
        public string[] scenes;
    }

    [System.Serializable]
    private class LoadRoundPayload
    {
        public int roundIndex;
        public string sceneName;
        public string[] scenes;
    }

    [System.Serializable]
    private class RoundResultPayload
    {
        public int roundIndex;
        public int winner; // -1 = 무승부, 0 = P1, 1 = P2
    }
}

// 고정 5판 승부 진행 관리자. 씬을 넘나들어야 하므로 DontDestroyOnLoad 싱글턴으로 두고,
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
    };

    public const string HubSceneName = "Hub";

    public int CurrentRoundIndex { get; private set; } = -1;
    public int P1Wins { get; private set; }
    public int P2Wins { get; private set; }
    public int ResultsVersion { get; private set; }

    private readonly PlayerId?[] _roundWinners = new PlayerId?[6];
    private readonly bool[] _roundReported = new bool[6];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartMatch()
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
        LoadNextRound();
    }

    public void LoadNextRound()
    {
        int nextRoundIndex = CurrentRoundIndex + 1;
        string nextScene = nextRoundIndex >= RoundScenes.Count
            ? HubSceneName
            : RoundScenes[nextRoundIndex];

        // 더블클릭이나 중복 코루틴으로 전환 요청이 겹치면 인덱스도 두 번 증가하지 않게 한다.
        if (SceneFadeTransition.TryLoadScene(nextScene))
            CurrentRoundIndex = nextRoundIndex;
    }

    // null = 무승부(승자 없음). 각 미니게임은 규칙에 따라 승자가 정해지는 순간 이걸 한 번만
    // 호출하고, 이후 알아서 다음 라운드로 넘어가거나(자동 진행) Hub 씬의 버튼을 기다리면 된다.
    public void ReportRoundResult(PlayerId? winner)
    {
        if (CurrentRoundIndex < 0 || CurrentRoundIndex >= RoundScenes.Count) return;
        if (_roundReported[CurrentRoundIndex]) return;

        _roundReported[CurrentRoundIndex] = true;
        _roundWinners[CurrentRoundIndex] = winner;
        if (winner == PlayerId.P1) P1Wins++;
        else if (winner == PlayerId.P2) P2Wins++;
        ResultsVersion++;
    }

    public bool HasRoundResult(int roundIndex)
        => roundIndex >= 0 && roundIndex < _roundReported.Length && _roundReported[roundIndex];

    public PlayerId? RoundWinner(int roundIndex)
        => roundIndex >= 0 && roundIndex < _roundWinners.Length ? _roundWinners[roundIndex] : null;

    public bool IsMatchComplete => CurrentRoundIndex >= RoundScenes.Count;

    public PlayerId? OverallWinner()
    {
        if (P1Wins == P2Wins) return null;
        return P1Wins > P2Wins ? PlayerId.P1 : PlayerId.P2;
    }
}

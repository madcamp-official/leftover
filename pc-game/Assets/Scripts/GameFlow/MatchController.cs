// 고정 6판 승부 진행 관리자. 씬을 넘나들어야 하므로 DontDestroyOnLoad 싱글턴으로 두고,
// 각 미니게임 씬은 끝날 때 ReportRoundResult()만 호출하면 된다 - 다음 라운드 로드/최종
// 결과 집계는 전부 여기서 처리하므로 미니게임 쪽은 "누가 이겼는지"만 알면 된다.
using System.Collections.Generic;
using UnityEngine;

public sealed class MatchController : MonoBehaviour
{
    public static MatchController Instance { get; private set; }

    // 우가우가게임_기획_프롬프트.md 표 순서 그대로. 씬 이름은 Build Settings에 등록된
    // 미니게임 씬 이름과 정확히 같아야 한다.
    public static readonly IReadOnlyList<string> RoundScenes = new List<string>
    {
        "StoneThrow",
        "PoseCopy",
        "FruitJump",
        "CoconutCrack",
        "StoneOrBanana",
        "StaringContest",
    };

    public const string HubSceneName = "Hub";

    public int CurrentRoundIndex { get; private set; } = -1;
    public int P1Wins { get; private set; }
    public int P2Wins { get; private set; }

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
        if (winner == PlayerId.P1) P1Wins++;
        else if (winner == PlayerId.P2) P2Wins++;
    }

    public bool IsMatchComplete => CurrentRoundIndex >= RoundScenes.Count;

    public PlayerId? OverallWinner()
    {
        if (P1Wins == P2Wins) return null;
        return P1Wins > P2Wins ? PlayerId.P1 : PlayerId.P2;
    }
}

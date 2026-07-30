using System.Collections.Generic;

// Hub와 GameSelection 씬 사이에서 호스트가 고른 미니게임 목록을 보관한다.
// 선택 화면에 새로 들어갈 때마다 BeginNewSelection()이 7개 전체를 다시 켠다.
public static class GameSelectionState
{
    public const string SceneName = "GameSelection";

    private static readonly List<string> Selected = new List<string>(MatchController.RoundScenes);
    private static bool _returnToMultiplayer;

    public static IReadOnlyList<string> SelectedScenes => Selected;
    public static bool HasSelection => Selected.Count > 0;

    public static void BeginNewSelection()
    {
        Selected.Clear();
        foreach (string scene in MatchController.RoundScenes)
            Selected.Add(scene);
        _returnToMultiplayer = false;
    }

    public static bool ApplySelection(IEnumerable<string> scenes)
    {
        var requested = new HashSet<string>(scenes);
        var ordered = new List<string>();
        foreach (string scene in MatchController.RoundScenes)
        {
            if (requested.Contains(scene))
                ordered.Add(scene);
        }

        if (ordered.Count == 0)
            return false;

        Selected.Clear();
        Selected.AddRange(ordered);
        return true;
    }

    public static void ReturnToMultiplayer()
    {
        _returnToMultiplayer = true;
    }

    public static bool ConsumeReturnToMultiplayer()
    {
        bool shouldReturn = _returnToMultiplayer;
        _returnToMultiplayer = false;
        return shouldReturn;
    }
}

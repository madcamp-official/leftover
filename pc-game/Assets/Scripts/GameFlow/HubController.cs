// 시작 화면 + 최종 결과 화면을 겸하는 Hub 씬 컨트롤러. MatchController가 "시작 전"
// 상태(CurrentRoundIndex == -1)면 시작 버튼을, 6판이 다 끝난 상태(IsMatchComplete)면 최종
// 결과를 보여준다. 게임 시작점(Build Settings의 첫 씬)은 항상 이 Hub 씬이어야 한다.
using UnityEngine;

public class HubController : MonoBehaviour
{
    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();
    }

    private void OnGUI()
    {
        MatchController match = MatchController.Instance;
        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.UpperCenter };
        var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 28 };

        GUI.Label(new Rect(0, 60, Screen.width, 60), "저능아게임", titleStyle);

        if (match == null) return;

        if (match.IsMatchComplete)
        {
            PlayerId? winner = match.OverallWinner();
            string result = winner == null
                ? $"무승부! ({match.P1Wins} : {match.P2Wins})"
                : $"{winner} 최종 승리! ({match.P1Wins} : {match.P2Wins})";
            GUI.Label(new Rect(0, 160, Screen.width, 50),
                result, new GUIStyle(titleStyle) { fontSize = 30 });

            if (GUI.Button(new Rect(Screen.width / 2f - 100, 260, 200, 60), "다시 시작", buttonStyle))
                match.StartMatch();
        }
        else if (match.CurrentRoundIndex < 0)
        {
            if (GUI.Button(new Rect(Screen.width / 2f - 100, 200, 200, 60), "시작", buttonStyle))
                match.StartMatch();
        }
    }
}

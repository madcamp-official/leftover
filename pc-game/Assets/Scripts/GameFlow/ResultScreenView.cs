// 최종 결과("엔딩") 화면 - 승리/무승부 배너, 점수판, 다시하기/메인으로 버튼.
// 다른 코드 생성형 화면들과 같은 패턴: Resources/UI/Prefabs/ResultCanvas 프리팹이 있으면
// 그걸 불러와 쓰고(에디터에서 마우스로 다듬은 결과), 없으면 코드로 기본 레이아웃을
// 생성한다. 프리팹을 새로 만들거나 갱신하려면 Tools > UGAUGA > Rebuild Hub Screen
// Prefabs(HubScreenPrefabBuilder.cs)를 실행할 것.
//
// 예전에는 Hub 씬에 미리 배치된 ResultScreenCanvas/ResultText/RestartButton을 그대로 쓰면서
// 코드로 배너/점수판만 덧붙였는데, 그 화면은 Edit 모드에서 손댈 수 없어서 이 컴포넌트로
// 완전히 옮겼다 - 씬의 옛 오브젝트들은 더 이상 참조하지 않는다(비활성 상태로 남아있지만
// 무해하다).
using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class ResultScreenView : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "UI/Prefabs/ResultCanvas";

    private GameObject _canvasObject;
    private Image _banner;
    private Text _bannerText;
    private Text _p1ScoreText;
    private Text _p2ScoreText;
    private Button _replayButton;
    private Button _mainMenuButton;
    private Action _onReplay;
    private Action _onMainMenu;

    public void Init(Action onReplay, Action onMainMenu)
    {
        _onReplay = onReplay;
        _onMainMenu = onMainMenu;
    }

    public void Show(MatchController match)
    {
        if (_canvasObject == null) CreateUi();
        _canvasObject.SetActive(true);
        Refresh(match);
    }

    public void Hide() => _canvasObject?.SetActive(false);

    public GameObject CreatePrefabTemplate()
    {
        BuildGeneratedUi();
        _canvasObject.SetActive(true);
        return _canvasObject;
    }

    private void Refresh(MatchController match)
    {
        if (match == null) return;

        PlayerId? winner = match.OverallWinner();
        bool isDraw = winner == null;

        _banner.sprite = ArtAssets.LoadUi(isDraw ? "result_panel_draw" : "result_panel_victory");
        // 승리/무승부 배너 이미지에 이미 해당 글자가 그려져 있으므로, 텍스트는 승리 쪽엔
        // 승자 이름만, 무승부 쪽엔 느낌표 없이 "무승부"만 덧붙인다.
        _bannerText.text = isDraw ? "무승부" : $"{winner}";

        _p1ScoreText.text = $"P1\n{match.P1Wins}";
        _p2ScoreText.text = $"P2\n{match.P2Wins}";
    }

    private void CreateUi()
    {
        GameObject prefab = Resources.Load<GameObject>(CanvasPrefabResourcePath);
        if (prefab != null)
        {
            _canvasObject = Instantiate(prefab, transform, false);
            _canvasObject.name = "ResultCanvas";
            BindUi();
            return;
        }

        Debug.LogWarning($"[ResultScreenView] {CanvasPrefabResourcePath} 프리팹을 찾지 못해 " +
            "코드 기본 레이아웃을 사용합니다. Tools > UGAUGA > Rebuild Hub Screen Prefabs로 만들 수 있습니다.");
        BuildGeneratedUi();
    }

    private void BindUi()
    {
        Transform root = _canvasObject.transform;
        _banner = UiBuilder.FindDescendant(root, "Banner")?.GetComponent<Image>();
        _bannerText = UiBuilder.FindDescendant(root, "BannerText")?.GetComponent<Text>();
        _p1ScoreText = UiBuilder.FindDescendant(root, "P1ScoreText")?.GetComponent<Text>();
        _p2ScoreText = UiBuilder.FindDescendant(root, "P2ScoreText")?.GetComponent<Text>();
        _replayButton = UiBuilder.FindDescendant(root, "ReplayButton")?.GetComponent<Button>();
        _mainMenuButton = UiBuilder.FindDescendant(root, "MainMenuButton")?.GetComponent<Button>();

        if (_banner == null || _bannerText == null || _p1ScoreText == null ||
            _p2ScoreText == null || _replayButton == null || _mainMenuButton == null)
        {
            throw new InvalidOperationException(
                $"{CanvasPrefabResourcePath}의 필수 오브젝트 이름이 바뀌었습니다 - 이름을 유지하거나 " +
                "Tools > UGAUGA > Rebuild Hub Screen Prefabs로 프리팹을 다시 만드세요.");
        }

        WireListeners();
    }

    private void WireListeners()
    {
        _replayButton.onClick.RemoveAllListeners();
        _replayButton.onClick.AddListener(() => _onReplay?.Invoke());
        _mainMenuButton.onClick.RemoveAllListeners();
        _mainMenuButton.onClick.AddListener(() => _onMainMenu?.Invoke());
    }

    private void BuildGeneratedUi()
    {
        _canvasObject = UiBuilder.CreateOverlayCanvas("ResultCanvas", transform, 32740);
        RectTransform root = _canvasObject.GetComponent<RectTransform>();

        // Hub 시작 화면과 같은 배경 - 화면 전환 시 배경이 바뀌지 않고 이어지는 느낌을 준다.
        UiBuilder.AddImage(root, "Background", ArtAssets.LoadUi("start_background"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2048f, 1152f));

        _banner = UiBuilder.AddImage(root, "Banner", ArtAssets.LoadUi("result_panel_victory"),
            new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1600f, 380f));
        _bannerText = UiBuilder.AddText(root, "BannerText", "", 64);
        UiBuilder.SetRect(_bannerText.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(-100f, 0f),
            new Vector2(900f, 200f));

        UiBuilder.AddImage(root, "ScoreboardPanel", ArtAssets.LoadUi("result_scoreboard_panel"),
            new Vector2(0.5f, 0.38f), Vector2.zero, new Vector2(1100f, 420f));
        _p1ScoreText = UiBuilder.AddText(root, "P1ScoreText", "", 80);
        UiBuilder.SetRect(_p1ScoreText.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(-270f, 0f),
            new Vector2(400f, 300f));
        _p2ScoreText = UiBuilder.AddText(root, "P2ScoreText", "", 80);
        UiBuilder.SetRect(_p2ScoreText.rectTransform, new Vector2(0.5f, 0.38f), new Vector2(270f, 0f),
            new Vector2(400f, 300f));

        _replayButton = UiBuilder.AddButton(root, "ReplayButton", ArtAssets.LoadUi("result_button_replay"),
            new Vector2(0.5f, 0.08f), new Vector2(-260f, 0f), new Vector2(480f, 190f));
        _mainMenuButton = UiBuilder.AddButton(root, "MainMenuButton", ArtAssets.LoadUi("result_button_main_menu"),
            new Vector2(0.5f, 0.08f), new Vector2(260f, 0f), new Vector2(480f, 190f));

        WireListeners();
    }
}

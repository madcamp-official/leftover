// "게임 방법" 화면 - UI_화면_확장_에셋_계획.md에서 확정한 대로 아이콘/삽화 없이
// 텍스트 영역만 페이지별로 넘기는 구조. 제목/본문/페이지 번호는 전부 Unity Text로 채우고
// 이미지에는 굽지 않는다. 미니게임 규칙 요약은 docs/minigames/*.md "한 줄 요약"을 옮겼다.
//
// LoadingScreenController와 같은 패턴: Resources/UI/Prefabs/HowToPlayCanvas 프리팹이
// 있으면 그걸 불러와 쓰고(에디터에서 마우스로 다듬은 결과), 없으면 코드로 기본 레이아웃을
// 생성한다. 프리팹을 새로 만들거나 갱신하려면 Tools > UGAUGA > Rebuild Hub Screen
// Prefabs(HubScreenPrefabBuilder.cs)를 실행할 것.
using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class HowToPlayScreen : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "UI/Prefabs/HowToPlayCanvas";

    private static readonly (string Title, string Body)[] Pages =
    {
        ("1. 돌 던지기",
            "일정 간격으로 던질 타이밍이 찾아옵니다.\n한쪽 손만 들어 던질 방향을 정하고,\n" +
            "고개를 좌우로 기울여 날아오는 상대 돌을 피하세요.\n제한시간 동안 더 많이 명중시킨 쪽이 승리합니다."),
        ("2. 점프해서 과일 따기",
            "제자리에서 높이 점프하세요.\n높이 뛸수록 더 높은(더 비싼) 과일을 딸 수 있습니다.\n" +
            "상대와 직접 부딪히지 않는 경주이니, 자기 점프 실력에만 집중하면 됩니다.\n" +
            "제한시간이 끝났을 때 점수가 더 높은 쪽이 승리합니다."),
        ("3. 머리로 코코넛 깨기",
            "머리 위 코코넛을 향해 양손을 계속 모았다 벌렸다 반복하세요.\n" +
            "한 번 모을 때마다 코코넛이 깨지며 1회로 기록됩니다.\n" +
            "제한시간 동안 더 많이 깬 쪽이 승리하는 순수 반복 속도 경쟁입니다."),
        ("4. 돌 or 바나나",
            "던지는 사람은 왼손 또는 오른손을 들어 돌인지 바나나인지 속이고,\n" +
            "받는 사람은 입을 벌렸다 닫으며 먹을지 말지 판단하는 3초 턴제 심리전입니다.\n" +
            "이빨 3개가 모두 깨지면 패배, 포만감 5칸을 먼저 채우면 승리합니다."),
        ("5. 눈빛 싸움",
            "따로 조작할 게 없습니다 - 그냥 카메라를 계속 응시하세요.\n" +
            "먼저, 그리고 더 오래 눈을 감는 쪽이 집니다."),
        ("6. 소리지르기",
            "포즈나 손동작은 전혀 보지 않습니다 - 오직 마이크 음량만 봅니다.\n" +
            "서로 번갈아 소리를 지르며, 매번 직전 상대가 낸 소리보다 더 크게 질러야 합니다.\n" +
            "넘기지 못하면 그 자리에서 집니다."),
        ("7. 깃털 날기",
            "절벽에서 뛰어내린 뒤 양손을 동시에 들었다 내리면 날갯짓으로 위로 뜹니다.\n" +
            "가만히 있으면 계속 아래로 떨어지니 꾸준히 날갯짓해서 버텨야 합니다.\n" +
            "제한시간이 끝났을 때 더 높이 떠 있는 쪽이 승리합니다."),
    };

    private GameObject _canvasObject;
    private Text _titleText;
    private Text _bodyText;
    private Text _pageIndicatorText;
    private Button _prevButton;
    private Button _nextButton;
    private Button _closeButton;
    private int _pageIndex;
    private Action _onClose;

    public void Init(Action onClose) => _onClose = onClose;

    public void Show()
    {
        if (_canvasObject == null) CreateUi();
        _pageIndex = 0;
        RefreshPage();
        _canvasObject.SetActive(true);
    }

    public void Hide() => _canvasObject?.SetActive(false);

    public GameObject CreatePrefabTemplate()
    {
        BuildGeneratedUi();
        _canvasObject.SetActive(true);
        return _canvasObject;
    }

    private void RefreshPage()
    {
        (string title, string body) = Pages[_pageIndex];
        _titleText.text = title;
        _bodyText.text = body;
        _pageIndicatorText.text = $"{_pageIndex + 1} / {Pages.Length}";
    }

    private void GoPrev()
    {
        _pageIndex = (_pageIndex - 1 + Pages.Length) % Pages.Length;
        RefreshPage();
    }

    private void GoNext()
    {
        _pageIndex = (_pageIndex + 1) % Pages.Length;
        RefreshPage();
    }

    private void CreateUi()
    {
        GameObject prefab = Resources.Load<GameObject>(CanvasPrefabResourcePath);
        if (prefab != null)
        {
            _canvasObject = Instantiate(prefab, transform, false);
            _canvasObject.name = "HowToPlayCanvas";
            BindUi();
            return;
        }

        Debug.LogWarning($"[HowToPlayScreen] {CanvasPrefabResourcePath} 프리팹을 찾지 못해 " +
            "코드 기본 레이아웃을 사용합니다. Tools > UGAUGA > Rebuild Hub Screen Prefabs로 만들 수 있습니다.");
        BuildGeneratedUi();
    }

    private void BindUi()
    {
        Transform root = _canvasObject.transform;
        _titleText = UiBuilder.FindDescendant(root, "PageTitle")?.GetComponent<Text>();
        _bodyText = UiBuilder.FindDescendant(root, "PageBody")?.GetComponent<Text>();
        _pageIndicatorText = UiBuilder.FindDescendant(root, "PageIndicator")?.GetComponent<Text>();
        _prevButton = UiBuilder.FindDescendant(root, "PrevButton")?.GetComponent<Button>();
        _nextButton = UiBuilder.FindDescendant(root, "NextButton")?.GetComponent<Button>();
        _closeButton = UiBuilder.FindDescendant(root, "CloseButton")?.GetComponent<Button>();

        if (_titleText == null || _bodyText == null || _pageIndicatorText == null ||
            _prevButton == null || _nextButton == null || _closeButton == null)
        {
            throw new InvalidOperationException(
                $"{CanvasPrefabResourcePath}의 필수 오브젝트 이름이 바뀌었습니다 - 이름을 유지하거나 " +
                "Tools > UGAUGA > Rebuild Hub Screen Prefabs로 프리팹을 다시 만드세요.");
        }

        WireListeners();
    }

    private void WireListeners()
    {
        _prevButton.onClick.RemoveAllListeners();
        _prevButton.onClick.AddListener(GoPrev);
        _nextButton.onClick.RemoveAllListeners();
        _nextButton.onClick.AddListener(GoNext);
        _closeButton.onClick.RemoveAllListeners();
        _closeButton.onClick.AddListener(() => { Hide(); _onClose?.Invoke(); });
    }

    private void BuildGeneratedUi()
    {
        _canvasObject = UiBuilder.CreateOverlayCanvas("HowToPlayCanvas", transform, 32740);
        RectTransform root = _canvasObject.GetComponent<RectTransform>();

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("howto_panel_instruction"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 1050f));

        // 흰 글씨 + 진한 갈색 테두리 - 게임 시작 버튼 등 기존 baked-in 텍스트와 같은 조합
        // (UiBuilder.AddText의 기본값이 이미 이 조합이라 별도로 색을 지정하지 않는다).
        _titleText = UiBuilder.AddText(root, "PageTitle", "", 48, TextAnchor.MiddleCenter);
        UiBuilder.SetRect(_titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f),
            new Vector2(1300f, 100f));

        _bodyText = UiBuilder.AddText(root, "PageBody", "", 34, TextAnchor.MiddleCenter, FontStyle.Normal);
        UiBuilder.SetRect(_bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f),
            new Vector2(1300f, 500f));

        _pageIndicatorText = UiBuilder.AddText(root, "PageIndicator", "", 28);
        UiBuilder.SetRect(_pageIndicatorText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
            new Vector2(300f, 60f));

        _prevButton = UiBuilder.AddButton(root, "PrevButton", ArtAssets.LoadUi("howto_button_page_prev"),
            new Vector2(0f, 0.5f), new Vector2(100f, 0f), new Vector2(130f, 130f));

        _nextButton = UiBuilder.AddButton(root, "NextButton", ArtAssets.LoadUi("howto_button_page_next"),
            new Vector2(1f, 0.5f), new Vector2(-100f, 0f), new Vector2(130f, 130f));

        _closeButton = UiBuilder.AddButton(root, "CloseButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(130f, 130f));

        WireListeners();
    }
}

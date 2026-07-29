using UnityEngine;
using UnityEngine.UI;

// 6개 미니게임에서 공통으로 보이는 매치 진행 결과판. 배경/아이콘/얼굴/결과 이미지는 모두
// 씬에 저장되며, 런타임에는 MatchController의 라운드별 승자 데이터만 반영한다.
public sealed class MatchScoreboardHud : MonoBehaviour
{
    private static readonly Vector2[] ColumnAnchors =
    {
        new(0.305f, 0f), new(0.416f, 0f), new(0.527f, 0f),
        new(0.638f, 0f), new(0.749f, 0f), new(0.860f, 0f),
    };

    private static readonly string[] IconNames =
    {
        "stone_throw", "pose_match", "fruit_jump",
        "coconut_break", "stone_or_banana", "staring_contest",
    };

    [SerializeField] private Image[] _p1Results = new Image[6];
    [SerializeField] private Image[] _p2Results = new Image[6];
    [SerializeField] private Sprite _winSprite;
    [SerializeField] private Sprite _lossSprite;
    [SerializeField] private Sprite _drawSprite;
    private int _displayedVersion = -1;

    // 원래 하단 중앙(920x424)에 크게 떠 있어 플레이 화면을 가린다는 피드백을 받아, 절반
    // 크기로 줄이고 하단 우측 구석으로 옮겼다 - 다른 미니게임 HUD는 상단 좌/우(네임플레이트)와
    // 상단 중앙(타이머)만 쓰므로 하단 구석은 비어 있다.
    private const float Scale = 0.5f;

    public static MatchScoreboardHud Build(Transform parent)
    {
        var go = new GameObject("MatchScoreboard");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-30f, 24f);
        rt.sizeDelta = new Vector2(920f, 424f) * Scale;

        MatchScoreboardHud hud = go.AddComponent<MatchScoreboardHud>();
        hud.BuildWidgets(rt);
        return hud;
    }

    private void BuildWidgets(RectTransform root)
    {
        _winSprite = ArtAssets.LoadMatchResult("result_win");
        _lossSprite = ArtAssets.LoadMatchResult("result_loss");
        _drawSprite = ArtAssets.LoadMatchResult("result_draw");

        Image board = CreateBox(root, "Board", ArtAssets.LoadMatchResult("match_result_board"),
            new Vector2(0.5f, 0.5f), new Vector2(920f, 424f) * Scale);
        board.raycastTarget = false;

        for (int i = 0; i < IconNames.Length; i++)
        {
            Vector2 anchor = new Vector2(ColumnAnchors[i].x, 0.704f);
            CreateBox(root, $"GameIcon_{i + 1}_{IconNames[i]}", ArtAssets.LoadGameIcon(IconNames[i]), anchor, new Vector2(82f, 82f) * Scale);
        }

        CreateBox(root, "Player1Face", ArtAssets.LoadCharacter(PlayerId.P1, "head"), new Vector2(0.151f, 0.493f), new Vector2(116f, 108f) * Scale);
        CreateBox(root, "Player2Face", ArtAssets.LoadCharacter(PlayerId.P2, "head"), new Vector2(0.151f, 0.269f), new Vector2(116f, 108f) * Scale);

        for (int i = 0; i < 6; i++)
        {
            _p1Results[i] = CreateBox(root, $"P1Result_{i + 1}", null,
                new Vector2(ColumnAnchors[i].x, 0.493f), new Vector2(76f, 76f) * Scale);
            _p2Results[i] = CreateBox(root, $"P2Result_{i + 1}", null,
                new Vector2(ColumnAnchors[i].x, 0.269f), new Vector2(76f, 76f) * Scale);
            _p1Results[i].gameObject.SetActive(false);
            _p2Results[i].gameObject.SetActive(false);
        }
    }

    private static Image CreateBox(RectTransform parent, string name, Sprite sprite, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private void Start() => Refresh(true);

    private void Update()
    {
        MatchController match = MatchController.Instance;
        if (match != null && match.ResultsVersion != _displayedVersion) Refresh(false);
    }

    private void Refresh(bool force)
    {
        MatchController match = MatchController.Instance;
        if (match == null)
        {
            if (force) SetAllEmpty();
            return;
        }

        _displayedVersion = match.ResultsVersion;
        for (int i = 0; i < 6; i++)
        {
            if (!match.HasRoundResult(i))
            {
                SetResult(_p1Results[i], null);
                SetResult(_p2Results[i], null);
                continue;
            }

            PlayerId? winner = match.RoundWinner(i);
            if (winner == null)
            {
                SetResult(_p1Results[i], _drawSprite);
                SetResult(_p2Results[i], _drawSprite);
            }
            else
            {
                SetResult(_p1Results[i], winner == PlayerId.P1 ? _winSprite : _lossSprite);
                SetResult(_p2Results[i], winner == PlayerId.P2 ? _winSprite : _lossSprite);
            }
        }
    }

    private void SetAllEmpty()
    {
        for (int i = 0; i < 6; i++)
        {
            SetResult(_p1Results[i], null);
            SetResult(_p2Results[i], null);
        }
    }

    private static void SetResult(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.gameObject.SetActive(sprite != null);
    }
}

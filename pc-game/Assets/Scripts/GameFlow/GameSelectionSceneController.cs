using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class GameSelectionSceneController : MonoBehaviour
{
    private static readonly (string Scene, string Label, string IconPath)[] Options =
    {
        ("StoneThrow", "돌 던지기", "UI/GameIcons/stone_throw"),
        ("FruitJump", "과일 점프", "UI/GameIcons/fruit_jump"),
        ("CoconutCrack", "코코넛 깨기", "UI/GameIcons/coconut_break"),
        ("StoneOrBanana", "돌 또는 바나나", "UI/GameIcons/stone_or_banana"),
        ("StaringContest", "눈 감기 대결", "UI/GameIcons/staring_contest"),
        ("ScreamDuel", "소리 지르기", "UI/GameIcons/scream_duel"),
        ("FeatherFlight", "깃털 날리기", "UI/GameIcons/feather_flight"),
    };

    private static readonly Color CheckedGreen = new Color(0.18f, 0.92f, 0.28f, 1f);
    private static readonly Color UncheckedBrown = new Color(0.18f, 0.11f, 0.06f, 0.92f);

    private Toggle[] _toggles;
    private Image[] _boxes;
    private Text[] _marks;
    private Text _warning;
    private Button _confirmButton;
    private Button _backButton;

    private void Start()
    {
        NetworkSession net = NetworkSession.Instance;
        if (net == null || !net.IsHost)
        {
            ReturnToHub();
            return;
        }

        EnsureCamera();
        EnsureEventSystem();
        GameBootstrap.EnsureLetterbox();

        Transform authoredCanvas = UiBuilder.FindDescendant(transform, "GameSelectionCanvas");
        if (authoredCanvas != null)
            BindAuthoredUi(authoredCanvas);
        else
            BuildUi();
    }

    private static void EnsureCamera()
    {
        if (Camera.main != null)
            return;

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        InputSystemUIInputModule inputModule = eventSystem.AddComponent<InputSystemUIInputModule>();
        inputModule.AssignDefaultActions();
    }

    private void BuildUi()
    {
        GameObject canvasObject = UiBuilder.CreateOverlayCanvas("GameSelectionCanvas", transform, 32740);
        RectTransform root = canvasObject.GetComponent<RectTransform>();

        Image background = UiBuilder.AddImage(root, "Background", ArtAssets.LoadUi("start_background"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2048f, 1152f));
        UiBuilder.Stretch(background.rectTransform);
        background.preserveAspect = false;

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("multiplayer_panel_main"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1660f, 980f));

        Text title = UiBuilder.AddText(root, "Title", "진행할 게임 선택", 52);
        UiBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -105f),
            new Vector2(1000f, 90f));

        Text guide = UiBuilder.AddText(root, "Guide",
            "호스트가 선택한 게임만 아래 순서대로 진행됩니다", 27);
        UiBuilder.SetRect(guide.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -175f),
            new Vector2(1200f, 60f));

        _toggles = new Toggle[Options.Length];
        _boxes = new Image[Options.Length];
        _marks = new Text[Options.Length];

        for (int i = 0; i < Options.Length; i++)
        {
            int row = i < 4 ? 0 : 1;
            int column = row == 0 ? i : i - 4;
            int count = row == 0 ? 4 : 3;
            float x = (column - (count - 1) * 0.5f) * 350f;
            float y = row == 0 ? 190f : -175f;
            CreateOption(root, i, new Vector2(x, y));
        }

        _warning = UiBuilder.AddText(root, "SelectionWarning",
            "최소 1개의 게임을 선택해 주세요", 25);
        UiBuilder.SetRect(_warning.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 95f),
            new Vector2(800f, 50f));
        _warning.color = new Color(1f, 0.55f, 0.4f, 1f);
        _warning.gameObject.SetActive(false);

        _confirmButton = UiBuilder.AddButton(root, "ConfirmButton", ArtAssets.LoadUi("settings_dropdown_frame"),
            new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(430f, 140f));
        Text confirmLabel = UiBuilder.AddText(_confirmButton.transform, "Label", "선택 완료", 34);
        UiBuilder.Stretch(confirmLabel.rectTransform);
        _confirmButton.onClick.AddListener(ConfirmSelection);

        _backButton = UiBuilder.AddButton(root, "BackButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(55f, -55f), new Vector2(125f, 125f));
        _backButton.onClick.AddListener(ReturnToHub);
    }

    // Editor에서 GameSelection 씬에 UI를 실제 오브젝트로 한 번 생성할 때 사용한다.
    // 이미 생성된 Canvas는 건드리지 않으므로 이후 Inspector에서 한 배치 수정이 유지된다.
    public GameObject CreateSceneTemplate()
    {
        Transform existing = UiBuilder.FindDescendant(transform, "GameSelectionCanvas");
        if (existing != null)
            return existing.gameObject;

        BuildUi();
        return UiBuilder.FindDescendant(transform, "GameSelectionCanvas")?.gameObject;
    }

    private void BindAuthoredUi(Transform canvas)
    {
        _toggles = new Toggle[Options.Length];
        _boxes = new Image[Options.Length];
        _marks = new Text[Options.Length];

        for (int i = 0; i < Options.Length; i++)
        {
            Transform option = UiBuilder.FindDescendant(canvas, $"GameOption{i}");
            if (option == null)
                throw new MissingReferenceException($"GameSelectionCanvas에 GameOption{i}가 없습니다.");

            Toggle toggle = option.GetComponent<Toggle>();
            Image box = UiBuilder.FindDescendant(option, "CheckBox")?.GetComponent<Image>();
            Text mark = UiBuilder.FindDescendant(option, "CheckMark")?.GetComponent<Text>();
            if (toggle == null || box == null || mark == null)
                throw new MissingReferenceException($"GameOption{i}의 Toggle/CheckBox/CheckMark 구성이 없습니다.");

            int capturedIndex = i;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(true);
            toggle.onValueChanged.AddListener(value => OnToggleChanged(capturedIndex, value));

            _toggles[i] = toggle;
            _boxes[i] = box;
            _marks[i] = mark;
            UpdateCheckVisual(i, true);
        }

        _warning = UiBuilder.FindDescendant(canvas, "SelectionWarning")?.GetComponent<Text>();
        _confirmButton = UiBuilder.FindDescendant(canvas, "ConfirmButton")?.GetComponent<Button>();
        _backButton = UiBuilder.FindDescendant(canvas, "BackButton")?.GetComponent<Button>();
        if (_warning == null || _confirmButton == null || _backButton == null)
            throw new MissingReferenceException("GameSelectionCanvas의 버튼 또는 경고 텍스트 구성이 없습니다.");

        _warning.gameObject.SetActive(false);
        _confirmButton.onClick.RemoveAllListeners();
        _confirmButton.onClick.AddListener(ConfirmSelection);
        _backButton.onClick.RemoveAllListeners();
        _backButton.onClick.AddListener(ReturnToHub);
    }

    private void CreateOption(RectTransform parent, int index, Vector2 position)
    {
        var cardObject = new GameObject($"GameOption{index}");
        cardObject.transform.SetParent(parent, false);
        RectTransform card = cardObject.AddComponent<RectTransform>();
        UiBuilder.SetRect(card, new Vector2(0.5f, 0.5f), position, new Vector2(300f, 315f));

        Image hitArea = cardObject.AddComponent<Image>();
        hitArea.color = new Color(0.16f, 0.09f, 0.04f, 0.48f);
        hitArea.raycastTarget = true;
        Outline cardOutline = cardObject.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.95f, 0.72f, 0.25f, 0.85f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        Image icon = UiBuilder.AddImage(card, "Icon", ArtAssets.LoadSprite(Options[index].IconPath),
            new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(190f, 190f));
        icon.raycastTarget = false;

        Text label = UiBuilder.AddText(card, "Label", Options[index].Label, 24);
        UiBuilder.SetRect(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 78f),
            new Vector2(280f, 52f));

        Image box = UiBuilder.AddImage(card, "CheckBox", null,
            new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(64f, 64f));
        box.raycastTarget = false;
        Outline boxOutline = box.gameObject.AddComponent<Outline>();
        boxOutline.effectColor = Color.white;
        boxOutline.effectDistance = new Vector2(3f, -3f);

        Text mark = UiBuilder.AddText(box.transform, "CheckMark", "✓", 48);
        UiBuilder.Stretch(mark.rectTransform);
        mark.color = Color.white;

        Toggle toggle = cardObject.AddComponent<Toggle>();
        toggle.targetGraphic = hitArea;
        toggle.graphic = null;
        toggle.SetIsOnWithoutNotify(true);
        toggle.onValueChanged.AddListener(value => OnToggleChanged(index, value));

        _toggles[index] = toggle;
        _boxes[index] = box;
        _marks[index] = mark;
        UpdateCheckVisual(index, true);
    }

    private void OnToggleChanged(int index, bool value)
    {
        UpdateCheckVisual(index, value);
        if (_warning != null)
            _warning.gameObject.SetActive(!HasSelection());
    }

    private void UpdateCheckVisual(int index, bool selected)
    {
        if (_boxes[index] != null)
            _boxes[index].color = selected ? CheckedGreen : UncheckedBrown;
        if (_marks[index] != null)
            _marks[index].gameObject.SetActive(selected);
    }

    private bool HasSelection()
    {
        foreach (Toggle toggle in _toggles)
        {
            if (toggle != null && toggle.isOn)
                return true;
        }
        return false;
    }

    private void ConfirmSelection()
    {
        var selected = new List<string>();
        for (int i = 0; i < _toggles.Length; i++)
        {
            if (_toggles[i].isOn)
                selected.Add(Options[i].Scene);
        }

        if (!GameSelectionState.ApplySelection(selected))
        {
            _warning.gameObject.SetActive(true);
            return;
        }

        ReturnToHub();
    }

    private static void ReturnToHub()
    {
        GameSelectionState.ReturnToMultiplayer();
        SceneManager.LoadScene(MatchController.HubSceneName);
    }
}

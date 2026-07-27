// 시작 화면 + 최종 결과 화면을 겸하는 Hub 씬 컨트롤러. 시작 화면은 image/screens/start/의 실제 아트
// (배경+로고+버튼 4개, previews/ui_layout.png 구성)로 uGUI Canvas를 조립해서 보여준다.
// MatchController가 "시작 전" 상태(CurrentRoundIndex == -1)면 이 시작 화면을, 6판이 다 끝난
// 상태(IsMatchComplete)면 결과를 보여준다(결과 화면은 전용 아트가 없어 텍스트로만).
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class HubController : MonoBehaviour
{
    private string _toastText = "";
    private float _toastTimer;

    private void Start()
    {
        GameBootstrap.EnsureInputSystems();
        GameBootstrap.EnsureMatchController();

        MatchController match = MatchController.Instance;
        if (match != null && !match.IsMatchComplete && match.CurrentRoundIndex < 0)
            BuildStartScreen();
    }

    private void Update()
    {
        if (_toastTimer > 0f)
        {
            _toastTimer -= Time.deltaTime;
            if (_toastTimer <= 0f) _toastText = "";
        }
    }

    // ---------------- 시작 화면 (uGUI, 실제 아트) ----------------

    private void BuildStartScreen()
    {
        EnsureEventSystem();

        var canvasGo = new GameObject("StartScreenCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2048f, 1152f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        CreateFullscreenImage(canvasGo.transform, "Background", "start_background");
        CreateTopCenterImage(canvasGo.transform, "Logo", "logo_ugauga_game", topOffset: 40f, width: 1000f);

        // start_screen_preview.png 구성: 왼쪽 위=게임 시작, 오른쪽 위=게임 방법,
        // 왼쪽 아래=설정, 오른쪽 아래=종료. 좌표는 그 미리보기 이미지 비율 그대로.
        CreateButton(canvasGo.transform, "ButtonGameStart", "button_game_start",
            new Vector2(0.19f, 0.488f), 550f, OnGameStartClicked);
        CreateButton(canvasGo.transform, "ButtonHowToPlay", "button_how_to_play",
            new Vector2(0.55f, 0.488f), 550f, OnHowToPlayClicked);
        CreateButton(canvasGo.transform, "ButtonSettings", "button_settings",
            new Vector2(0.19f, 0.253f), 550f, OnSettingsClicked);
        CreateButton(canvasGo.transform, "ButtonExit", "button_exit",
            new Vector2(0.55f, 0.253f), 550f, OnExitClicked);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        // 프로젝트가 Input System(새 방식) 전용(activeInputHandler=1)이라, 레거시
        // Input 클래스에 의존하는 StandaloneInputModule 대신 이걸 써야 UI 클릭이 동작한다.
        go.AddComponent<InputSystemUIInputModule>();
    }

    private static void CreateFullscreenImage(Transform parent, string name, string resourceName)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>();
        image.sprite = ArtAssets.LoadUi(resourceName);
    }

    private static void CreateTopCenterImage(Transform parent, string name, string resourceName, float topOffset, float width)
    {
        Sprite sprite = ArtAssets.LoadUi(resourceName);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -topOffset);
        rt.sizeDelta = new Vector2(width, SizeFromAspect(sprite, width));

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
    }

    // anchorPoint: 캔버스 기준 정규화 좌표(0~1, 왼쪽아래 원점) - 버튼 중심이 이 점에 온다.
    private void CreateButton(Transform parent, string name, string resourceName,
        Vector2 anchorPoint, float width, UnityEngine.Events.UnityAction onClick)
    {
        Sprite sprite = ArtAssets.LoadUi(resourceName);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchorPoint;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, SizeFromAspect(sprite, width));

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
    }

    private static float SizeFromAspect(Sprite sprite, float width)
    {
        if (sprite == null || sprite.rect.width <= 0f) return width * 0.4f;
        return width * sprite.rect.height / sprite.rect.width;
    }

    private void OnGameStartClicked() => MatchController.Instance?.StartMatch();

    private void OnHowToPlayClicked() => ShowToast("게임 방법 - 준비 중입니다!");

    private void OnSettingsClicked() => ShowToast("설정 - 준비 중입니다!");

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowToast(string text)
    {
        _toastText = text;
        _toastTimer = 2f;
    }

    // ---------------- 결과 화면 (아직 전용 아트 없음 - 텍스트) ----------------

    private void OnGUI()
    {
        MatchController match = MatchController.Instance;

        if (!string.IsNullOrEmpty(_toastText))
        {
            var toastStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(0, Screen.height - 100, Screen.width, 50), _toastText, toastStyle);
        }

        if (match == null || !match.IsMatchComplete) return;

        var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.UpperCenter };
        var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 28 };

        GUI.Label(new Rect(0, 60, Screen.width, 60), "우가우가게임", titleStyle);

        PlayerId? winner = match.OverallWinner();
        string result = winner == null
            ? $"무승부! ({match.P1Wins} : {match.P2Wins})"
            : $"{winner} 최종 승리! ({match.P1Wins} : {match.P2Wins})";
        GUI.Label(new Rect(0, 160, Screen.width, 50), result, new GUIStyle(titleStyle) { fontSize = 30 });

        if (GUI.Button(new Rect(Screen.width / 2f - 100, 260, 200, 60), "다시 시작", buttonStyle))
            match.StartMatch();
    }
}

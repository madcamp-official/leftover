// 설정 화면 - UI_화면_확장_에셋_계획.md에서 확정한 대로 음악/효과음 on-off + 볼륨,
// 카메라/마이크 장치 선택을 담는다.
//
// 카메라/마이크 선택은 지금은 실제 캡처(vision-server, 별도 Python 프로세스)에 연결되어
// 있지 않다 - 배포_아키텍처_설계.md 1장(vision-server 번들링)이 끝나야 이 선택값을 실제로
// 어디에 전달할지 정해진다. 그 전까지는 PlayerPrefs에 값만 저장해두고, UI 자체(장치 목록
// 나열/선택)는 Unity의 WebCamTexture.devices / Microphone.devices로 실제 장치를 보여준다 -
// 나중에 vision-server 실행 인자로 이 값을 넘기기만 하면 된다.
//
// 효과음 볼륨은 GameSfx.Volume에, 음악 on/off・볼륨은 GameBgm.MusicEnabled/MusicVolume에
// 반영되어 즉시 동작한다. 두 시스템 모두 값을 직접 PlayerPrefs에 저장/로드하므로 이 화면은
// 자체 PlayerPrefs 키를 따로 두지 않고 그 정적 프로퍼티를 그대로 읽고 쓰기만 한다 -
// 값의 소유자는 항상 GameSfx/GameBgm 쪽이다.
//
// LoadingScreenController와 같은 패턴: Resources/UI/Prefabs/SettingsCanvas 프리팹이
// 있으면 그걸 불러와 쓰고(에디터에서 마우스로 다듬은 결과), 없으면 코드로 기본 레이아웃을
// 생성한다. 프리팹을 새로 만들거나 갱신하려면 Tools > UGAUGA > Rebuild Hub Screen
// Prefabs(HubScreenPrefabBuilder.cs)를 실행할 것.
using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class SettingsScreen : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "UI/Prefabs/SettingsCanvas";

    private const string SfxOnKey = "settings_sfx_on";
    private const string SfxVolumeKey = "settings_sfx_volume";
    private const string CameraDeviceKey = "settings_camera_device";
    private const string MicDeviceKey = "settings_mic_device";

    private GameObject _canvasObject;
    private Button _closeButton;
    private Text _cameraDeviceText;
    private Text _micDeviceText;

    private bool _sfxOn;
    private string[] _cameraDevices = Array.Empty<string>();
    private string[] _micDevices = Array.Empty<string>();
    private int _cameraIndex;
    private int _micIndex;

    private Action _onClose;

    public void Init(Action onClose) => _onClose = onClose;

    public void Show()
    {
        if (_canvasObject == null) CreateUi();
        _canvasObject.SetActive(true);
    }

    public void Hide() => _canvasObject?.SetActive(false);

    public GameObject CreatePrefabTemplate()
    {
        BuildGeneratedUi();
        _canvasObject.SetActive(true);
        return _canvasObject;
    }

    // Hub가 게임 시작 전(부팅 시점)에도 한 번 호출해서 저장된 효과음 볼륨을 실제로
    // 적용해둔다 - 설정 화면을 열어본 적 없어도 이전에 저장한 값이 반영되어야 하므로.
    // 음악 쪽은 GameBgm이 스스로 PlayerPrefs에서 읽어 부팅 시점에 적용하므로 여기서
    // 따로 손댈 필요가 없다.
    public void ApplySavedAudioSettings()
    {
        _sfxOn = PlayerPrefs.GetInt(SfxOnKey, 1) == 1;
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        GameSfx.Volume = _sfxOn ? sfxVolume : 0f;
    }

    private void CreateUi()
    {
        GameObject prefab = Resources.Load<GameObject>(CanvasPrefabResourcePath);
        if (prefab != null)
        {
            _canvasObject = Instantiate(prefab, transform, false);
            _canvasObject.name = "SettingsCanvas";
            BindUi();
            return;
        }

        Debug.LogWarning($"[SettingsScreen] {CanvasPrefabResourcePath} 프리팹을 찾지 못해 " +
            "코드 기본 레이아웃을 사용합니다. Tools > UGAUGA > Rebuild Hub Screen Prefabs로 만들 수 있습니다.");
        BuildGeneratedUi();
    }

    private void BindUi()
    {
        Transform root = _canvasObject.transform;
        ApplySavedAudioSettings();
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);

        Transform musicRow = UiBuilder.FindDescendant(root, "Row_음악");
        Transform sfxRow = UiBuilder.FindDescendant(root, "Row_효과음");
        Transform cameraRow = UiBuilder.FindDescendant(root, "Row_카메라");
        Transform micRow = UiBuilder.FindDescendant(root, "Row_마이크");
        _closeButton = UiBuilder.FindDescendant(root, "CloseButton")?.GetComponent<Button>();

        if (musicRow == null || sfxRow == null || cameraRow == null || micRow == null || _closeButton == null)
        {
            throw new InvalidOperationException(
                $"{CanvasPrefabResourcePath}의 필수 오브젝트 이름이 바뀌었습니다 - 이름을 유지하거나 " +
                "Tools > UGAUGA > Rebuild Hub Screen Prefabs로 프리팹을 다시 만드세요.");
        }

        WireAudioRow(musicRow, GameBgm.MusicEnabled, GameBgm.MusicVolume,
            onToggle: on => GameBgm.MusicEnabled = on,
            onVolume: v => GameBgm.MusicVolume = v);

        float sfxVolumeNow = sfxVolume;
        WireAudioRow(sfxRow, _sfxOn, sfxVolume,
            onToggle: on =>
            {
                _sfxOn = on;
                PlayerPrefs.SetInt(SfxOnKey, on ? 1 : 0);
                GameSfx.Volume = on ? sfxVolumeNow : 0f;
            },
            onVolume: v =>
            {
                sfxVolumeNow = v;
                PlayerPrefs.SetFloat(SfxVolumeKey, v);
                if (_sfxOn) GameSfx.Volume = v;
            });

        WireDeviceRow(cameraRow, isCamera: true);
        WireDeviceRow(micRow, isCamera: false);

        _closeButton.onClick.RemoveAllListeners();
        _closeButton.onClick.AddListener(() => { Hide(); _onClose?.Invoke(); });
    }

    private void BuildGeneratedUi()
    {
        _canvasObject = UiBuilder.CreateOverlayCanvas("SettingsCanvas", transform, 32740);
        RectTransform root = _canvasObject.GetComponent<RectTransform>();

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("howto_panel_instruction"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 1050f));

        // 흰 글씨 + 진한 갈색 테두리(UiBuilder.AddText 기본값) - 게임 시작 버튼 등 기존
        // baked-in 텍스트와 같은 조합.
        Text title = UiBuilder.AddText(root, "Title", "설정", 48);
        UiBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f),
            new Vector2(1200f, 90f));

        ApplySavedAudioSettings();
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);

        Transform musicRow = CreateAudioRowStructure(root, "음악", -260f);
        WireAudioRow(musicRow, GameBgm.MusicEnabled, GameBgm.MusicVolume,
            onToggle: on => GameBgm.MusicEnabled = on,
            onVolume: v => GameBgm.MusicVolume = v);

        Transform sfxRow = CreateAudioRowStructure(root, "효과음", -100f);
        float sfxVolumeNow = sfxVolume;
        WireAudioRow(sfxRow, _sfxOn, sfxVolume,
            onToggle: on =>
            {
                _sfxOn = on;
                PlayerPrefs.SetInt(SfxOnKey, on ? 1 : 0);
                GameSfx.Volume = on ? sfxVolumeNow : 0f;
            },
            onVolume: v =>
            {
                sfxVolumeNow = v;
                PlayerPrefs.SetFloat(SfxVolumeKey, v);
                if (_sfxOn) GameSfx.Volume = v;
            });

        Transform cameraRow = CreateDeviceRowStructure(root, "카메라", 90f);
        WireDeviceRow(cameraRow, isCamera: true);
        Transform micRow = CreateDeviceRowStructure(root, "마이크", 250f);
        WireDeviceRow(micRow, isCamera: false);

        _closeButton = UiBuilder.AddButton(root, "CloseButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(130f, 130f));
        _closeButton.onClick.RemoveAllListeners();
        _closeButton.onClick.AddListener(() => { Hide(); _onClose?.Invoke(); });
    }

    // --- 행(row) 구조 생성 - 코드 생성 경로에서만 쓴다. 프리팹 경로는 이미 있는 구조를
    // 이름으로 찾아 WireAudioRow/WireDeviceRow에 그대로 넘긴다. ---

    private Transform CreateAudioRowStructure(RectTransform root, string label, float y)
    {
        var row = new GameObject($"Row_{label}");
        row.transform.SetParent(root, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        UiBuilder.SetRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1300f, 140f));

        Text labelText = UiBuilder.AddText(rowRt, "Label", label, 32, TextAnchor.MiddleLeft);
        UiBuilder.SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f),
            new Vector2(220f, 80f));

        UiBuilder.AddButton(rowRt, "Toggle", ArtAssets.LoadUi("settings_toggle_off"),
            new Vector2(0f, 0.5f), new Vector2(260f, 0f), new Vector2(140f, 78f));

        UiBuilder.AddSlider(rowRt, "Slider", ArtAssets.LoadUi("settings_slider_track"),
            ArtAssets.LoadUi("settings_slider_handle"), new Vector2(0f, 0.5f), new Vector2(480f, 0f),
            new Vector2(700f, 40f));

        return rowRt;
    }

    private Transform CreateDeviceRowStructure(RectTransform root, string label, float y)
    {
        var row = new GameObject($"Row_{label}");
        row.transform.SetParent(root, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        UiBuilder.SetRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1300f, 140f));

        Text labelText = UiBuilder.AddText(rowRt, "Label", label, 32, TextAnchor.MiddleLeft);
        UiBuilder.SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f),
            new Vector2(220f, 80f));

        Button frameButton = UiBuilder.AddButton(rowRt, "DeviceFrame", ArtAssets.LoadUi("settings_dropdown_frame"),
            new Vector2(0f, 0.5f), new Vector2(260f, 0f), new Vector2(920f, 130f));
        Text deviceText = UiBuilder.AddText(frameButton.transform, "DeviceText", "", 28,
            TextAnchor.MiddleCenter, FontStyle.Normal);
        UiBuilder.StretchWithMargin(deviceText.rectTransform, 100f, 20f);
        UiBuilder.AddImage(frameButton.transform, "Arrow", ArtAssets.LoadUi("settings_dropdown_arrow"),
            new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(50f, 50f));

        return rowRt;
    }

    // --- 참조/리스너 연결 - 프리팹에서 불러왔든 방금 코드로 만들었든 이 두 메서드가
    // 유일한 진입점이라, 구조가 바뀌어도(에디터에서 자식 순서를 바꾸는 정도는) 이름만
    // 유지되면 그대로 동작한다. ---

    private void WireAudioRow(Transform row, bool initialOn, float initialVolume,
        Action<bool> onToggle, Action<float> onVolume)
    {
        Button toggleButton = UiBuilder.FindDescendant(row, "Toggle")?.GetComponent<Button>();
        Slider slider = UiBuilder.FindDescendant(row, "Slider")?.GetComponent<Slider>();
        if (toggleButton == null || slider == null)
            throw new InvalidOperationException($"{row.name}에 Toggle/Slider가 없습니다.");

        Image toggleImage = toggleButton.GetComponent<Image>();
        Sprite onSprite = ArtAssets.LoadUi("settings_toggle_on");
        Sprite offSprite = ArtAssets.LoadUi("settings_toggle_off");
        bool on = initialOn;
        toggleImage.sprite = on ? onSprite : offSprite;

        slider.onValueChanged.RemoveAllListeners();
        slider.value = initialVolume;
        slider.onValueChanged.AddListener(v => onVolume(v));

        toggleButton.onClick.RemoveAllListeners();
        toggleButton.onClick.AddListener(() =>
        {
            on = !on;
            toggleImage.sprite = on ? onSprite : offSprite;
            onToggle(on);
        });
    }

    private void WireDeviceRow(Transform row, bool isCamera)
    {
        Button frameButton = UiBuilder.FindDescendant(row, "DeviceFrame")?.GetComponent<Button>();
        Text deviceText = UiBuilder.FindDescendant(row, "DeviceText")?.GetComponent<Text>();
        if (frameButton == null || deviceText == null)
            throw new InvalidOperationException($"{row.name}에 DeviceFrame/DeviceText가 없습니다.");

        string[] devices = isCamera
            ? Array.ConvertAll(WebCamTexture.devices, d => d.name)
            : Microphone.devices;
        if (devices == null || devices.Length == 0) devices = new[] { "장치 없음" };
        if (isCamera) _cameraDevices = devices; else _micDevices = devices;

        string savedName = PlayerPrefs.GetString(isCamera ? CameraDeviceKey : MicDeviceKey, "");
        int startIndex = Array.IndexOf(devices, savedName);
        if (startIndex < 0) startIndex = 0;
        if (isCamera) _cameraIndex = startIndex; else _micIndex = startIndex;

        deviceText.text = devices[startIndex];
        if (isCamera) _cameraDeviceText = deviceText; else _micDeviceText = deviceText;

        // 정식 펼침형 드롭다운 대신, 눌러서 다음 장치로 순환하는 방식으로 단순화했다 -
        // Unity Dropdown 템플릿을 코드로 조립하는 복잡도 대비 기능은 동일(장치 선택)하다.
        frameButton.onClick.RemoveAllListeners();
        frameButton.onClick.AddListener(() =>
        {
            if (isCamera)
            {
                _cameraIndex = (_cameraIndex + 1) % _cameraDevices.Length;
                _cameraDeviceText.text = _cameraDevices[_cameraIndex];
                PlayerPrefs.SetString(CameraDeviceKey, _cameraDevices[_cameraIndex]);
            }
            else
            {
                _micIndex = (_micIndex + 1) % _micDevices.Length;
                _micDeviceText.text = _micDevices[_micIndex];
                PlayerPrefs.SetString(MicDeviceKey, _micDevices[_micIndex]);
            }
        });
    }
}

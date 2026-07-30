// 설정 화면 - UI_화면_확장_에셋_계획.md에서 확정한 대로 음악/효과음 on-off + 볼륨,
// 카메라/마이크 장치 선택을 담는다.
//
// 카메라/마이크 선택은 지금은 실제 캡처(vision-server, 별도 Python 프로세스)에 연결되어
// 있지 않다 - 배포_아키텍처_설계.md 1장(vision-server 번들링)이 끝나야 이 선택값을 실제로
// 어디에 전달할지 정해진다. 그 전까지는 PlayerPrefs에 값만 저장해두고, UI 자체(장치 목록
// 나열/선택)는 Unity의 WebCamTexture.devices / Microphone.devices로 실제 장치를 보여준다 -
// 나중에 vision-server 실행 인자로 이 값을 넘기기만 하면 된다.
//
// 효과음 볼륨은 실제로 GameSfx.Volume에 반영되어 즉시 동작한다. 음악(배경음악) 쪽은 이
// 프로젝트에 BGM 재생 시스템 자체가 아직 없어서(작업_분담_체크리스트.md "사운드" 항목),
// 토글/슬라이더는 있지만 PlayerPrefs에 저장만 하고 아무 것도 제어하지 않는다 - BGM
// 시스템이 생기면 그때 연결한다.
using UnityEngine;
using UnityEngine.UI;

public sealed class SettingsScreen : MonoBehaviour
{
    private const string MusicOnKey = "settings_music_on";
    private const string SfxOnKey = "settings_sfx_on";
    private const string MusicVolumeKey = "settings_music_volume";
    private const string SfxVolumeKey = "settings_sfx_volume";
    private const string CameraDeviceKey = "settings_camera_device";
    private const string MicDeviceKey = "settings_mic_device";

    private GameObject _root;
    private Text _cameraDeviceText;
    private Text _micDeviceText;

    private bool _musicOn;
    private bool _sfxOn;
    private string[] _cameraDevices = System.Array.Empty<string>();
    private string[] _micDevices = System.Array.Empty<string>();
    private int _cameraIndex;
    private int _micIndex;

    private System.Action _onClose;

    public void Init(System.Action onClose) => _onClose = onClose;

    public void Show()
    {
        if (_root == null) Build();
        _root.SetActive(true);
    }

    public void Hide() => _root?.SetActive(false);

    // Hub가 게임 시작 전(부팅 시점)에도 한 번 호출해서 저장된 볼륨을 실제로 적용해둔다 -
    // 설정 화면을 열어본 적 없어도 이전에 저장한 값이 반영되어야 하므로.
    public void ApplySavedAudioSettings()
    {
        _musicOn = PlayerPrefs.GetInt(MusicOnKey, 1) == 1;
        _sfxOn = PlayerPrefs.GetInt(SfxOnKey, 1) == 1;
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
        GameSfx.Volume = _sfxOn ? sfxVolume : 0f;
    }

    private void Build()
    {
        _root = UiBuilder.CreateOverlayCanvas("SettingsCanvas", transform, 32740);
        RectTransform root = _root.GetComponent<RectTransform>();

        UiBuilder.AddImage(root, "Panel", ArtAssets.LoadUi("howto_panel_instruction"),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 1050f));

        Text title = UiBuilder.AddText(root, "Title", "설정", 48);
        UiBuilder.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f),
            new Vector2(1200f, 90f));
        title.color = new Color(0.32f, 0.18f, 0.08f, 1f);

        ApplySavedAudioSettings();
        float musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f);
        float sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);

        BuildAudioRow(root, "음악", -260f, _musicOn, musicVolume,
            onToggle: on =>
            {
                _musicOn = on;
                PlayerPrefs.SetInt(MusicOnKey, on ? 1 : 0);
                // BGM 시스템이 아직 없어서 여기서 더 할 일이 없다 - 값만 저장.
            },
            onVolume: v => PlayerPrefs.SetFloat(MusicVolumeKey, v));

        float sfxVolumeNow = sfxVolume;
        BuildAudioRow(root, "효과음", -100f, _sfxOn, sfxVolume,
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

        BuildDeviceRow(root, "카메라", 90f, isCamera: true);
        BuildDeviceRow(root, "마이크", 250f, isCamera: false);

        Button close = UiBuilder.AddButton(root, "CloseButton", ArtAssets.LoadUi("multiplayer_button_back"),
            new Vector2(0f, 1f), new Vector2(60f, -60f), new Vector2(130f, 130f));
        close.onClick.AddListener(() => { Hide(); _onClose?.Invoke(); });
    }

    private void BuildAudioRow(RectTransform root, string label, float y, bool initialOn, float initialVolume,
        System.Action<bool> onToggle, System.Action<float> onVolume)
    {
        var row = new GameObject($"Row_{label}");
        row.transform.SetParent(root, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        UiBuilder.SetRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1300f, 140f));

        Text labelText = UiBuilder.AddText(rowRt, "Label", label, 32, TextAnchor.MiddleLeft);
        UiBuilder.SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f),
            new Vector2(220f, 80f));
        labelText.color = new Color(0.32f, 0.18f, 0.08f, 1f);

        bool on = initialOn;
        Sprite onSprite = ArtAssets.LoadUi("settings_toggle_on");
        Sprite offSprite = ArtAssets.LoadUi("settings_toggle_off");
        Button toggleButton = UiBuilder.AddButton(rowRt, "Toggle", on ? onSprite : offSprite,
            new Vector2(0f, 0.5f), new Vector2(260f, 0f), new Vector2(140f, 78f));
        Image toggleImage = toggleButton.GetComponent<Image>();

        Slider slider = UiBuilder.AddSlider(rowRt, "Slider", ArtAssets.LoadUi("settings_slider_track"),
            ArtAssets.LoadUi("settings_slider_handle"), new Vector2(0f, 0.5f), new Vector2(480f, 0f),
            new Vector2(700f, 40f));
        slider.value = initialVolume;
        slider.onValueChanged.AddListener(v => onVolume(v));

        toggleButton.onClick.AddListener(() =>
        {
            on = !on;
            toggleImage.sprite = on ? onSprite : offSprite;
            onToggle(on);
        });
    }

    private void BuildDeviceRow(RectTransform root, string label, float y, bool isCamera)
    {
        var row = new GameObject($"Row_{label}");
        row.transform.SetParent(root, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        UiBuilder.SetRect(rowRt, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1300f, 140f));

        Text labelText = UiBuilder.AddText(rowRt, "Label", label, 32, TextAnchor.MiddleLeft);
        UiBuilder.SetRect(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f),
            new Vector2(220f, 80f));
        labelText.color = new Color(0.32f, 0.18f, 0.08f, 1f);

        string[] devices = isCamera
            ? System.Array.ConvertAll(WebCamTexture.devices, d => d.name)
            : Microphone.devices;
        if (devices == null || devices.Length == 0) devices = new[] { "장치 없음" };
        if (isCamera) _cameraDevices = devices; else _micDevices = devices;

        string savedName = PlayerPrefs.GetString(isCamera ? CameraDeviceKey : MicDeviceKey, "");
        int startIndex = System.Array.IndexOf(devices, savedName);
        if (startIndex < 0) startIndex = 0;
        if (isCamera) _cameraIndex = startIndex; else _micIndex = startIndex;

        Button frameButton = UiBuilder.AddButton(rowRt, "DeviceFrame", ArtAssets.LoadUi("settings_dropdown_frame"),
            new Vector2(0f, 0.5f), new Vector2(260f, 0f), new Vector2(920f, 130f));
        Text deviceText = UiBuilder.AddText(frameButton.transform, "DeviceText", devices[startIndex], 28,
            TextAnchor.MiddleCenter, FontStyle.Normal);
        UiBuilder.StretchWithMargin(deviceText.rectTransform, 100f, 20f);
        UiBuilder.AddImage(frameButton.transform, "Arrow", ArtAssets.LoadUi("settings_dropdown_arrow"),
            new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(50f, 50f));

        if (isCamera) _cameraDeviceText = deviceText; else _micDeviceText = deviceText;

        // 정식 펼침형 드롭다운 대신, 눌러서 다음 장치로 순환하는 방식으로 단순화했다 -
        // Unity Dropdown 템플릿을 코드로 조립하는 복잡도 대비 기능은 동일(장치 선택)하다.
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

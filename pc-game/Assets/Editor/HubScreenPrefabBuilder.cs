// UI_화면_확장_에셋_계획.md의 코드 생성형 Hub 화면들(멀티플레이 연결/게임 방법/설정)을
// 한 번 조립해서 Assets/Resources/UI/Prefabs/*.prefab으로 저장한다 - LoadingAssetImporter의
// BuildLoadingScreenPrefab과 같은 패턴. 이후에는 각 화면 컴포넌트가 이 프리팹을 먼저
// 찾아서 쓰므로, 여기서 저장된 프리팹을 Unity 에디터에서 열어 마우스로 직접 위치/크기/
// 스프라이트를 다듬을 수 있다. 다시 실행하면 코드 레이아웃으로 덮어써지니, 손으로 다듬은
// 뒤에는 이 메뉴를 다시 누르지 말 것(코코넛깨기 EditableLayout 재빌드와 같은 주의사항).
using UnityEditor;
using UnityEngine;

public static class HubScreenPrefabBuilder
{
    private const string MultiplayerPrefabPath = "Assets/Resources/UI/Prefabs/MultiplayerConnectCanvas.prefab";
    private const string HowToPlayPrefabPath = "Assets/Resources/UI/Prefabs/HowToPlayCanvas.prefab";
    private const string SettingsPrefabPath = "Assets/Resources/UI/Prefabs/SettingsCanvas.prefab";
    private const string ResultPrefabPath = "Assets/Resources/UI/Prefabs/ResultCanvas.prefab";

    [MenuItem("Tools/UGAUGA/Rebuild Hub Screen Prefabs")]
    public static void RebuildAll()
    {
        BuildOne<MultiplayerConnectScreen>(MultiplayerPrefabPath,
            (screen) => screen.CreatePrefabTemplate());
        BuildOne<HowToPlayScreen>(HowToPlayPrefabPath,
            (screen) => screen.CreatePrefabTemplate());
        BuildOne<SettingsScreen>(SettingsPrefabPath,
            (screen) => screen.CreatePrefabTemplate());
        BuildOne<ResultScreenView>(ResultPrefabPath,
            (screen) => screen.CreatePrefabTemplate());
        AssetDatabase.SaveAssets();
        Debug.Log("[HubScreenPrefabBuilder] 멀티플레이/게임방법/설정/결과 프리팹 생성 완료.");
    }

    private static void BuildOne<T>(string path, System.Func<T, GameObject> createTemplate) where T : Component
    {
        var host = new GameObject($"__{typeof(T).Name}PrefabBuilder");
        try
        {
            T screen = host.AddComponent<T>();
            GameObject canvas = createTemplate(screen);
            canvas.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(canvas, path);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }
}

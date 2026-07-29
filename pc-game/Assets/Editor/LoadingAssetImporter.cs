using UnityEditor;
using UnityEngine;

// Resources/Loading 아래 PNG는 코드에서 Sprite로 읽으므로, 새로 복사된 파일도
// 수동 Inspector 작업 없이 UI용 Single Sprite 설정으로 통일한다.
public static class LoadingAssetImporter
{
    private const string CanvasPrefabPath =
        "Assets/Resources/Loading/LoadingScreenCanvas.prefab";

    [InitializeOnLoadMethod]
    private static void ConfigureAfterEditorLoad()
    {
        EditorApplication.delayCall += ConfigureAll;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPrefabPath) == null)
            EditorApplication.delayCall += BuildLoadingScreenPrefab;
    }

    [MenuItem("Tools/UGAUGA/Configure Loading Assets")]
    private static void ConfigureAll()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:Texture2D",
            new[] { "Assets/Resources/Loading" });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;

            bool changed =
                importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                importer.mipmapEnabled ||
                importer.maxTextureSize < 4096;

            if (!changed) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
    }

    [MenuItem("Tools/UGAUGA/Rebuild Loading Screen Prefab")]
    public static void BuildLoadingScreenPrefab()
    {
        var host = new GameObject("__LoadingScreenPrefabBuilder");
        try
        {
            var controller = host.AddComponent<LoadingScreenController>();
            GameObject canvas = controller.CreatePrefabTemplate();
            canvas.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(canvas, CanvasPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LoadingScreen] 편집 가능한 프리팹 생성 완료: {CanvasPrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }
}

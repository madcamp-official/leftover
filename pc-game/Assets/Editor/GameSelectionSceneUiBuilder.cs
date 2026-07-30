using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// GameSelection UI를 런타임 생성물에서 편집 가능한 씬 오브젝트로 한 번 변환한다.
// Canvas가 이미 있으면 자동 실행은 아무것도 변경하지 않으므로 수동 배치가 보존된다.
[InitializeOnLoad]
public static class GameSelectionSceneUiBuilder
{
    private const string ScenePath = "Assets/Scenes/GameSelection.unity";

    static GameSelectionSceneUiBuilder()
    {
        EditorApplication.delayCall += EnsureSceneUi;
    }

    [MenuItem("Tools/UGAUGA/Game Selection/Rebuild Scene UI %#g")]
    public static void RebuildSceneUi()
    {
        Build(forceRebuild: true);
    }

    private static void EnsureSceneUi()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        Build(forceRebuild: false);
    }

    private static void Build(bool forceRebuild)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameSelectionSceneController controller = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            controller = root.GetComponent<GameSelectionSceneController>();
            if (controller != null)
                break;
        }

        if (controller == null)
        {
            Debug.LogError("[GameSelectionSceneUiBuilder] GameSelectionSceneController를 찾지 못했습니다.");
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
            return;
        }

        Transform canvas = UiBuilder.FindDescendant(controller.transform, "GameSelectionCanvas");
        if (canvas != null && forceRebuild)
        {
            Object.DestroyImmediate(canvas.gameObject);
            canvas = null;
        }

        bool changed = false;
        if (canvas == null)
        {
            controller.CreateSceneTemplate();
            changed = true;
        }

        if (!HasComponentInRoots<Camera>(scene))
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            changed = true;
        }

        if (!HasComponentInRoots<EventSystem>(scene))
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GameSelectionSceneUiBuilder] GameSelection 씬 UI 생성 완료.");
        }

        if (openedHere)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static bool HasComponentInRoots<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<T>(true) != null)
                return true;
        }
        return false;
    }
}

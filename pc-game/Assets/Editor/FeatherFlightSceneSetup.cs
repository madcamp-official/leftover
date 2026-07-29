#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 깃털날기 프로토타입 씬을 처음부터 만드는 편집 도구다. docs/minigames/08_깃털날기.md 참고.
// CoconutCrack 리팩터 이후 원칙과 동일: 캐릭터/절벽 모두 런타임 인스턴스화 없이 씬에 미리
// 배치되고, 값들은 SerializeField로 노출되어 Inspector에서 조절 가능하다.
public static class FeatherFlightSceneSetup
{
    private const string ScenePath = "Assets/Scenes/FeatherFlight.unity";
    private const float CameraSize = 5f;

    [MenuItem("Tools/Uga Uga/FeatherFlight/Rebuild Scene")]
    public static void Rebuild()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RebuildScene(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] FeatherFlight 씬을 다시 만들었습니다.");
    }

    private static void RebuildScene(Scene scene)
    {
        GameObject oldLayout = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout");
        if (oldLayout != null) UnityEngine.Object.DestroyImmediate(oldLayout);
        GameObject oldController = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "GameController");
        if (oldController != null) UnityEngine.Object.DestroyImmediate(oldController);
        Transform root = new GameObject("EditableLayout").transform;

        SetupCamera();
        AddBackground(root, ArtAssets.LoadFeatherFlight("background"));

        Transform p1Rig = CreateRig(root, "P1Rig", anchorX: 0f);
        Transform p1Cliff = AddSprite(p1Rig, "Cliff", ArtAssets.LoadFeatherFlight("cliff_p1"), Vector3.zero, 5f, -10);
        // RestPoint = 인트로 도약이 끝난 뒤 캐릭터가 고정되는 화면 중앙 쪽 자리(화면 세로 중앙).
        Transform p1RestPoint = CreateEmpty(p1Rig, "RestPoint", new Vector3(3f, 1f, 0f));

        Transform p2Rig = CreateRig(root, "P2Rig", anchorX: 1f);
        Transform p2Cliff = AddSprite(p2Rig, "Cliff", ArtAssets.LoadFeatherFlight("cliff_p2"), Vector3.zero, 5f, -10);
        Transform p2RestPoint = CreateEmpty(p2Rig, "RestPoint", new Vector3(-3f, 1f, 0f));

        // 캐릭터는 절벽 위에 서 있는 자리(절벽 바닥=캐릭터 발, 즉 Rig와 같은 Y)에서 시작해서
        // RestPoint까지 도약한다 - 이 초기 위치가 곧 인트로의 시작점이다.
        GameObject p1CharGo = CreateCharacter(root, "P1Character", PlayerId.P1,
            "feather_flight_jump_right", "feather_flight_flap_right", new Vector3(p1Rig.position.x + 1f, p1Rig.position.y, 0f));
        GameObject p2CharGo = CreateCharacter(root, "P2Character", PlayerId.P2,
            "feather_flight_jump_left", "feather_flight_flap_left", new Vector3(p2Rig.position.x - 1f, p2Rig.position.y, 0f));

        FeatherFlightHud hud = FeatherFlightHud.Build(20f);
        hud.transform.SetParent(root, false);

        GameObject controllerGo = new GameObject("GameController");
        FeatherFlightGame game = controllerGo.AddComponent<FeatherFlightGame>();
        SetRefs(game,
            ("p1Cliff", p1Cliff), ("p2Cliff", p2Cliff),
            ("p1RestPoint", p1RestPoint), ("p2RestPoint", p2RestPoint),
            ("p1View", p1CharGo.GetComponent<FeatherFlightCharacterView>()),
            ("p2View", p2CharGo.GetComponent<FeatherFlightCharacterView>()),
            ("hud", hud));
        SetFloats(game,
            ("matchSeconds", 20f), ("fallSpeed", 1.2f),
            ("flapBoost", .35f), ("raiseRatio", .15f), ("resultDisplaySeconds", 2f),
            ("jumpIntroSeconds", .5f), ("cliffDropSeconds", .3f), ("introDropHeight", 1f),
            ("wingAnimationSmoothing", 10f));
    }

    // 절벽/캐릭터/RestPoint 등 손으로 조정해둔 배치는 전혀 건드리지 않고, HUD(게이지/타이머)만
    // 코드 최신 버전으로 다시 만든다. GameController의 hud 참조도 새 인스턴스로 다시 연결한다.
    [MenuItem("Tools/Uga Uga/FeatherFlight/Rebuild HUD Only")]
    public static void RebuildHudOnly()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform layout = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout")?.transform;
        if (layout == null) throw new InvalidOperationException("FeatherFlight 씬에서 EditableLayout을 찾지 못했습니다.");

        FeatherFlightGame game = UnityEngine.Object.FindAnyObjectByType<FeatherFlightGame>();
        float matchSeconds = 20f;
        if (game != null)
            matchSeconds = new SerializedObject(game).FindProperty("matchSeconds").floatValue;

        FeatherFlightHud oldHud = layout.GetComponentInChildren<FeatherFlightHud>(true);
        if (oldHud != null) UnityEngine.Object.DestroyImmediate(oldHud.gameObject);

        FeatherFlightHud hud = FeatherFlightHud.Build(matchSeconds);
        hud.transform.SetParent(layout, false);
        if (game != null) SetRefs(game, ("hud", hud));

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] FeatherFlight HUD만 다시 만들었습니다(다른 배치는 그대로).");
    }

    private static void SetupCamera()
    {
        Camera cam = Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CameraSize;
        cam.transform.position = new Vector3(0, 1, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
    }

    private static Transform CreateRig(Transform parent, string name, float anchorX)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ScreenEdgeAnchor anchor = go.AddComponent<ScreenEdgeAnchor>();
        anchor.anchor = new Vector2(anchorX, .5f);
        anchor.anchoredPosition = new Vector2(0f, -1f);
        anchor.Apply();
        return go.transform;
    }

    private static Transform CreateEmpty(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    private static Transform AddSprite(Transform parent, string name, Sprite sprite, Vector3 localPosition, float width, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        ArtAssets.FitWidth(sr, width);
        return go.transform;
    }

    private static void AddBackground(Transform root, Sprite sprite)
    {
        GameObject go = new GameObject("Background");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(0, 1, 0);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -100;
        float h = CameraSize * 2f;
        float w = h * 16f / 9f;
        Vector2 native = sprite.bounds.size;
        go.transform.localScale = Vector3.one * Mathf.Max(w / native.x, h / native.y);
        go.AddComponent<CameraBackgroundFitter>();
    }

    private static GameObject CreateCharacter(Transform root, string name, PlayerId player,
        string jumpPrefix, string flapPrefix, Vector3 introStartPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = introStartPosition;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 10;

        FeatherFlightCharacterView view = go.AddComponent<FeatherFlightCharacterView>();
        SetRefs(view, ("frameRenderer", sr));
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("player").enumValueIndex = (int)player;
        serialized.FindProperty("jumpPrefix").stringValue = jumpPrefix;
        serialized.FindProperty("flapPrefix").stringValue = flapPrefix;
        // 컷별 X 오프셋을 바로 Inspector에서 조절할 수 있게, 프레임 수만큼 0으로 미리 채워둔다.
        SetFloatArray(serialized, "jumpFrameOffsetX", 4);
        SetFloatArray(serialized, "flapFrameOffsetX", 3);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);

        // 씬을 열자마자(Play 없이도) 첫 점프 프레임이 보이도록, 프레임을 즉시 로드해서 폭도
        // 맞춰둔다 - EnsureFrames는 private이라 여기서는 로더를 직접 한 번 더 호출한다.
        Sprite[] jumpFrames = ArtAssets.LoadCharacterSequence(player, jumpPrefix);
        if (jumpFrames.Length > 0)
        {
            sr.sprite = jumpFrames[0];
            ArtAssets.FitWidth(sr, 2f);
        }
        return go;
    }

    private static void SetRefs(UnityEngine.Object target, params (string name, object value)[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        foreach ((string name, object value) in values)
        {
            SerializedProperty property = serialized.FindProperty(name)
                ?? throw new InvalidOperationException($"{target.GetType().Name}.{name} 필드를 찾지 못했습니다.");
            property.objectReferenceValue = value as UnityEngine.Object;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetFloats(UnityEngine.Object target, params (string name, float value)[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        foreach ((string name, float value) in values)
            serialized.FindProperty(name).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void SetFloatArray(SerializedObject serialized, string name, int size)
    {
        SerializedProperty property = serialized.FindProperty(name)
            ?? throw new InvalidOperationException($"{name} 필드를 찾지 못했습니다.");
        property.arraySize = size;
        for (int i = 0; i < size; i++)
            property.GetArrayElementAtIndex(i).floatValue = 0f;
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// StoneThrow 전용 마이그레이션 도구. 실행 결과는 씬에 저장되며 런타임 생성에 의존하지 않는다.
public static class StoneThrowSceneSetup
{
    private const string ScenePath = "Assets/Scenes/StoneThrow.unity";
    private const string CharacterPath = "Assets/Resources/Characters/";

    [MenuItem("Tools/Uga Uga/StoneThrow/Rebuild Editable Animated Characters")]
    public static void Rebuild()
    {
        AssetDatabase.Refresh();
        ConfigureImportedArt();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RebuildScene(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] StoneThrow 애니메이션 캐릭터/앵커/투사체를 씬 오브젝트로 저장했습니다.");
    }

    [MenuItem("Tools/Uga Uga/StoneThrow/Fix Front View Left Right Anchors")]
    public static void FixFrontViewLeftRightAnchors()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "StoneThrow")
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform layout = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout")?.transform;
        if (layout == null) throw new InvalidOperationException("StoneThrow 씬에서 EditableLayout을 찾지 못했습니다.");

        StoneThrowCharacterView[] frontViews = layout.GetComponentsInChildren<StoneThrowCharacterView>(true)
            .Where(x => !x.IsBackView).ToArray();
        foreach (StoneThrowCharacterView view in frontViews)
        {
            SerializedObject serialized = new SerializedObject(view);
            Transform dodgeLeft = serialized.FindProperty("leftDodgeAnchor").objectReferenceValue as Transform;
            Transform dodgeRight = serialized.FindProperty("rightDodgeAnchor").objectReferenceValue as Transform;
            Transform targetLeft = serialized.FindProperty("leftTargetAnchor").objectReferenceValue as Transform;
            Transform targetRight = serialized.FindProperty("rightTargetAnchor").objectReferenceValue as Transform;
            SwapLocalPositions(dodgeLeft, dodgeRight);
            SwapLocalPositions(targetLeft, targetRight);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Uga Uga] 정면 뷰 {frontViews.Length}개의 논리적 좌/우 Dodge·Target 앵커를 교환했습니다.");
    }

    public static void RebuildScene(Scene scene)
    {
        ConfigureImportedArt();
        Transform layout = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout")?.transform;
        if (layout == null) throw new InvalidOperationException("StoneThrow 씬에서 EditableLayout을 찾지 못했습니다.");

        foreach (StoneThrowCharacterView old in layout.GetComponentsInChildren<StoneThrowCharacterView>(true))
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        foreach (CavemanSilhouette old in layout.GetComponentsInChildren<CavemanSilhouette>(true))
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        DestroyChild(layout, "StoneThrowCharacters");
        DestroyChild(layout, "ProjectileContainer");
        DestroyChild(layout, "TEMP_StoneThrowTestInput");
        foreach (Transform health in layout.GetComponentsInChildren<Transform>(true)
                     .Where(x => x.name == "P1HealthBar" || x.name == "P2HealthBar").ToArray())
            UnityEngine.Object.DestroyImmediate(health.gameObject);

        SpriteRenderer background = layout.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(x => x.name == "Background");
        if (background != null)
        {
            background.sprite = ArtAssets.LoadStoneThrow("background");
            background.GetComponent<CameraBackgroundFitter>()?.Fit();
            EditorUtility.SetDirty(background);
        }

        Transform characters = Child(layout, "StoneThrowCharacters", Vector3.zero);
        StoneThrowCharacterView p2Front = CreateView(characters,
            "LeftHalf_P2_Front_Target", PlayerId.P2, false, new Vector3(-4.45f, -1.15f, 0f), 3.0f, 0);
        StoneThrowCharacterView p1Back = CreateView(characters,
            "LeftHalf_P1_Back_Thrower", PlayerId.P1, true, new Vector3(-1.15f, -2.15f, 0f), 5.0f, 20);
        StoneThrowCharacterView p1Front = CreateView(characters,
            "RightHalf_P1_Front_Target", PlayerId.P1, false, new Vector3(4.45f, -1.15f, 0f), 3.0f, 0);
        StoneThrowCharacterView p2Back = CreateView(characters,
            "RightHalf_P2_Back_Thrower", PlayerId.P2, true, new Vector3(7.7f, -2.15f, 0f), 5.0f, 20);

        Transform projectileContainer = Child(layout, "ProjectileContainer", Vector3.zero);
        GameObject templateGo = new GameObject("StoneTemplate (Runtime Clone)");
        templateGo.transform.SetParent(projectileContainer, false);
        SpriteRenderer stoneTemplate = templateGo.AddComponent<SpriteRenderer>();
        stoneTemplate.sprite = ArtAssets.LoadProp("stone");
        // Back 캐릭터(20) 바로 아래, Front 캐릭터(0) 위: Back > 돌 > Front.
        stoneTemplate.sortingOrder = 19;
        templateGo.SetActive(false);

        StoneThrowGame game = UnityEngine.Object.FindAnyObjectByType<StoneThrowGame>();
        if (game == null) throw new InvalidOperationException("StoneThrowGame을 찾지 못했습니다.");
        StoneThrowHud hud = layout.GetComponentInChildren<StoneThrowHud>(true);
        SetRefs(game,
            ("p1FrontView", p1Front), ("p1BackView", p1Back),
            ("p2FrontView", p2Front), ("p2BackView", p2Back),
            ("stoneTemplate", stoneTemplate), ("projectileContainer", projectileContainer), ("hud", hud));
        SetFloats(game,
            ("fireIntervalSeconds", 1f), ("matchSeconds", 30f), ("headSideThreshold", .12f),
            ("headSideHoldSeconds", .1f), ("stoneTravelSeconds", 1f),
            ("throwAnimationSeconds", .6f), ("hitReactionDisplaySeconds", .7f),
            ("resultDisplaySeconds", 2f), ("nearStoneWidth", 2.6f), ("farStoneWidth", .25f),
            ("stoneSpinDegreesPerSecond", 1440f));
    }

    private static StoneThrowCharacterView CreateView(Transform parent, string name, PlayerId player,
        bool backView, Vector3 position, float characterWidth, int sortingOrder)
    {
        Transform slot = Child(parent, name, position);
        // 논리적인 캐릭터 좌/우는 후면에서는 화면 방향과 같고 정면에서는 반대로 보인다.
        float leftX = backView ? -.38f : .38f;
        float rightX = -leftX;
        Transform dodgeLeft = Child(slot, "DodgeLeftAnchor", new Vector3(leftX, 0f, 0f));
        Transform dodgeRight = Child(slot, "DodgeRightAnchor", new Vector3(rightX, 0f, 0f));
        Transform targetLeft = Child(slot, "TargetLeftAnchor", new Vector3(leftX, backView ? .95f : .35f, 0f));
        Transform targetRight = Child(slot, "TargetRightAnchor", new Vector3(rightX, backView ? .95f : .35f, 0f));
        Transform visualRoot = Child(slot, "VisualRoot", player == PlayerId.P1 ? dodgeLeft.localPosition : dodgeRight.localPosition);

        Sprite[] leftFrames = LoadFrames(player, backView ? "back_left" : "front_left");
        Sprite[] rightFrames = LoadFrames(player, backView ? "back_right" : "front_right");
        GameObject characterGo = new GameObject("CharacterSprite");
        characterGo.transform.SetParent(visualRoot, false);
        SpriteRenderer character = characterGo.AddComponent<SpriteRenderer>();
        character.sprite = rightFrames[0];
        character.sortingOrder = sortingOrder;
        FitWidth(character, characterWidth);

        float handX = backView ? characterWidth * .27f : characterWidth * .27f;
        float handY = characterWidth * (backView ? .23f : .28f);
        // 정면에서는 캐릭터의 왼손이 화면 오른쪽, 후면에서는 화면 왼쪽에 보인다.
        Transform leftRelease = Child(visualRoot, "LeftHandRelease", new Vector3(backView ? -handX : handX, handY, 0f));
        Transform rightRelease = Child(visualRoot, "RightHandRelease", new Vector3(backView ? handX : -handX, handY, 0f));

        Sprite hitFullBody = !backView
            ? LoadSprite(CharacterPath + $"p{(player == PlayerId.P1 ? 1 : 2)}_stone_throw_hit_fullbody.png")
            : null;

        StoneThrowCharacterView view = slot.gameObject.AddComponent<StoneThrowCharacterView>();
        SetRefs(view,
            ("visualRoot", visualRoot), ("characterRenderer", character), ("hitFullBodySprite", hitFullBody),
            ("leftDodgeAnchor", dodgeLeft), ("rightDodgeAnchor", dodgeRight),
            ("leftHandReleaseAnchor", leftRelease), ("rightHandReleaseAnchor", rightRelease),
            ("leftTargetAnchor", targetLeft), ("rightTargetAnchor", targetRight),
            ("leftHandFrames", leftFrames), ("rightHandFrames", rightFrames));
        SetEnumAndBool(view, player, backView);
        return view;
    }

    private static Sprite[] LoadFrames(PlayerId player, string view)
    {
        int number = player == PlayerId.P1 ? 1 : 2;
        Sprite[] frames = Enumerable.Range(1, 6)
            .Select(i => LoadSprite(CharacterPath + $"p{number}_stone_throw_{view}_{i}.png")).ToArray();
        if (frames.Any(x => x == null)) throw new InvalidOperationException($"P{number} {view} 6컷을 불러오지 못했습니다.");
        return frames;
    }

    private static Sprite LoadSprite(string path)
    {
        ConfigureTexture(path);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void ConfigureImportedArt()
    {
        for (int player = 1; player <= 2; player++)
        {
            foreach (string view in new[] { "front_right", "front_left", "back_right", "back_left" })
                for (int frame = 1; frame <= 6; frame++)
                    ConfigureTexture(CharacterPath + $"p{player}_stone_throw_{view}_{frame}.png");
            ConfigureTexture(CharacterPath + $"p{player}_stone_throw_hit_fullbody.png");
        }
        ConfigureTexture("Assets/Resources/StoneThrow/background.png");
    }

    private static void ConfigureTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        bool dirty = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        if (dirty) importer.SaveAndReimport();
    }

    private static void FitWidth(SpriteRenderer renderer, float width)
    {
        float nativeWidth = renderer.sprite.bounds.size.x;
        renderer.transform.localScale = Vector3.one * (width / nativeWidth);
    }

    private static Transform Child(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    private static void DestroyChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }

    private static void SwapLocalPositions(Transform first, Transform second)
    {
        if (first == null || second == null)
            throw new InvalidOperationException("교환할 StoneThrow 앵커 참조가 비어 있습니다.");
        Undo.RecordObjects(new UnityEngine.Object[] { first, second }, "Fix StoneThrow Front View Anchors");
        Vector3 firstPosition = first.localPosition;
        first.localPosition = second.localPosition;
        second.localPosition = firstPosition;
        EditorUtility.SetDirty(first);
        EditorUtility.SetDirty(second);
    }

    private static void SetRefs(UnityEngine.Object target, params (string name, object value)[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        foreach ((string name, object value) in values)
        {
            SerializedProperty property = serialized.FindProperty(name)
                ?? throw new InvalidOperationException($"{target.GetType().Name}.{name} 필드를 찾지 못했습니다.");
            if (value is Array array)
            {
                property.arraySize = array.Length;
                for (int i = 0; i < array.Length; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = array.GetValue(i) as UnityEngine.Object;
            }
            else property.objectReferenceValue = value as UnityEngine.Object;
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
    }

    private static void SetEnumAndBool(StoneThrowCharacterView view, PlayerId player, bool backView)
    {
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("player").enumValueIndex = (int)player;
        serialized.FindProperty("backView").boolValue = backView;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif

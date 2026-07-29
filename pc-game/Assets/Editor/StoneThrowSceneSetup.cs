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
        stoneTemplate.sortingOrder = 40;
        templateGo.SetActive(false);

        // 캘리브레이션/카메라 연결 전 게임플레이 검수용. 이 오브젝트를 끄면 즉시 실제
        // PoseInputHub 입력만 사용한다. 기본값은 P1 오른손, P2 왼손 투척을 반복한다.
        Transform testInputRoot = Child(layout, "TEMP_StoneThrowTestInput", Vector3.zero);
        PoseSimulator testInput = testInputRoot.gameObject.AddComponent<PoseSimulator>();
        testInput.p1Tracked = true;
        testInput.p1RightHandRaised = true;
        testInput.p1LeftHandRaised = false;
        testInput.p1HeadTilt = -.5f;
        testInput.p2Tracked = true;
        testInput.p2RightHandRaised = false;
        testInput.p2LeftHandRaised = true;
        testInput.p2HeadTilt = .5f;

        StoneThrowGame game = UnityEngine.Object.FindAnyObjectByType<StoneThrowGame>();
        if (game == null) throw new InvalidOperationException("StoneThrowGame을 찾지 못했습니다.");
        StoneThrowHud hud = layout.GetComponentInChildren<StoneThrowHud>(true);
        SetRefs(game,
            ("p1FrontView", p1Front), ("p1BackView", p1Back),
            ("p2FrontView", p2Front), ("p2BackView", p2Back),
            ("stoneTemplate", stoneTemplate), ("projectileContainer", projectileContainer), ("hud", hud));
        SetFloats(game,
            ("fireIntervalSeconds", 1f), ("matchSeconds", 30f), ("headSideThreshold", .12f),
            ("headSideHoldSeconds", .1f), ("stoneTravelSeconds", .25f),
            ("throwAnimationSeconds", .6f), ("hitFaceDisplaySeconds", .7f),
            ("resultDisplaySeconds", 2f), ("nearStoneWidth", .52f), ("farStoneWidth", .16f));
    }

    private static StoneThrowCharacterView CreateView(Transform parent, string name, PlayerId player,
        bool backView, Vector3 position, float characterWidth, int sortingOrder)
    {
        Transform slot = Child(parent, name, position);
        Transform dodgeLeft = Child(slot, "DodgeLeftAnchor", new Vector3(-.38f, 0f, 0f));
        Transform dodgeRight = Child(slot, "DodgeRightAnchor", new Vector3(.38f, 0f, 0f));
        Transform targetLeft = Child(slot, "TargetLeftAnchor", new Vector3(-.38f, backView ? .95f : .35f, 0f));
        Transform targetRight = Child(slot, "TargetRightAnchor", new Vector3(.38f, backView ? .95f : .35f, 0f));
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

        SpriteRenderer face = null;
        if (!backView)
        {
            GameObject faceGo = new GameObject("HitFaceOverlay");
            faceGo.transform.SetParent(visualRoot, false);
            faceGo.transform.localPosition = new Vector3(0f, player == PlayerId.P1 ? .62f : .7f, -.01f);
            face = faceGo.AddComponent<SpriteRenderer>();
            face.sprite = LoadSprite(CharacterPath + $"p{(player == PlayerId.P1 ? 1 : 2)}_face_grimacing.png");
            face.sortingOrder = sortingOrder + 2;
            FitWidth(face, player == PlayerId.P1 ? 1.22f : .72f);
            face.enabled = false;
        }

        StoneThrowCharacterView view = slot.gameObject.AddComponent<StoneThrowCharacterView>();
        SetRefs(view,
            ("visualRoot", visualRoot), ("characterRenderer", character), ("hitFaceRenderer", face),
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
            ConfigureTexture(CharacterPath + $"p{player}_face_grimacing.png");
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

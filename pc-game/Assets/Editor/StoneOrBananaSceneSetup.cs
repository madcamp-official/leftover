#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// StoneOrBanana의 통짜 캐릭터, 수풀, 손/입 앵커를 씬에 저장하는 편집 도구다.
public static class StoneOrBananaSceneSetup
{
    private const string ScenePath = "Assets/Scenes/StoneOrBanana.unity";
    private const string CharacterPath = "Assets/Resources/Characters/";

    [MenuItem("Tools/Uga Uga/StoneOrBanana/Rebuild Editable Turn Characters")]
    public static void Rebuild()
    {
        AssetDatabase.Refresh();
        ConfigureImportedArt();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RebuildScene(scene, rebuildHud: true);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] StoneOrBanana 캐릭터/수풀/손·입 앵커를 씬 오브젝트로 저장했습니다.");
    }

    public static void RebuildScene(Scene scene, bool rebuildHud)
    {
        ConfigureImportedArt();
        Transform layout = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout")?.transform;
        if (layout == null) throw new InvalidOperationException("StoneOrBanana 씬에서 EditableLayout을 찾지 못했습니다.");

        foreach (StoneOrBananaCharacterView old in layout.GetComponentsInChildren<StoneOrBananaCharacterView>(true))
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        foreach (CavemanSilhouette old in layout.GetComponentsInChildren<CavemanSilhouette>(true))
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        DestroyChild(layout, "StoneOrBananaCharacters");
        DestroyChild(layout, "ProjectileContainer");

        SpriteRenderer background = layout.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(x => x.name == "Background");
        if (background != null)
        {
            background.sprite = ArtAssets.LoadStoneOrBanana("background");
            background.GetComponent<CameraBackgroundFitter>()?.Fit();
            EditorUtility.SetDirty(background);
        }

        Transform characters = Child(layout, "StoneOrBananaCharacters", Vector3.zero);
        StoneOrBananaCharacterView p2Front = CreateView(characters, "LeftHalf_P2_Front",
            PlayerId.P2, false, new Vector3(-4.45f, -1.15f, 0f), 3f);
        StoneOrBananaCharacterView p1Back = CreateView(characters, "LeftHalf_P1_Back",
            PlayerId.P1, true, new Vector3(-1.15f, -2.15f, 0f), 5f);
        StoneOrBananaCharacterView p1Front = CreateView(characters, "RightHalf_P1_Front",
            PlayerId.P1, false, new Vector3(4.45f, -1.15f, 0f), 3f);
        StoneOrBananaCharacterView p2Back = CreateView(characters, "RightHalf_P2_Back",
            PlayerId.P2, true, new Vector3(7.7f, -2.15f, 0f), 5f);

        Transform projectileContainer = Child(layout, "ProjectileContainer", Vector3.zero);
        SpriteRenderer stoneTemplate = CreateTemplate(projectileContainer, "StoneTemplate (Runtime Clone)",
            ArtAssets.LoadProp("stone"));
        SpriteRenderer bananaTemplate = CreateTemplate(projectileContainer, "BananaTemplate (Runtime Clone)",
            ArtAssets.LoadProp("banana"));

        StoneOrBananaHud hud = layout.GetComponentInChildren<StoneOrBananaHud>(true);
        if (rebuildHud || hud == null)
        {
            if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
            hud = StoneOrBananaHud.Build();
            hud.transform.SetParent(layout, false);
        }

        StoneOrBananaGame game = UnityEngine.Object.FindAnyObjectByType<StoneOrBananaGame>();
        if (game == null) throw new InvalidOperationException("StoneOrBananaGame을 찾지 못했습니다.");
        SetRefs(game,
            ("p1FrontView", p1Front), ("p1BackView", p1Back),
            ("p2FrontView", p2Front), ("p2BackView", p2Back),
            ("stoneTemplate", stoneTemplate), ("bananaTemplate", bananaTemplate),
            ("projectileContainer", projectileContainer), ("hud", hud));
        SetFloats(game,
            ("maxFullness", 5f), ("maxTeeth", 3f), ("turnDecisionSeconds", 3f),
            ("throwAnimationSeconds", .6f), ("throwTravelSeconds", .35f),
            ("reactionDisplaySeconds", .8f), ("resultDisplaySeconds", 2f),
            ("nearStoneWidth", 1.8f), ("farStoneWidth", .24f),
            ("nearBananaWidth", 2.1f), ("farBananaWidth", .3f),
            ("projectileSpinDegreesPerSecond", 1440f));
    }

    private static StoneOrBananaCharacterView CreateView(Transform parent, string name, PlayerId player,
        bool backView, Vector3 position, float characterWidth)
    {
        Transform slot = Child(parent, name, position);
        Transform visualRoot = Child(slot, "VisualRoot", Vector3.zero);
        Sprite[] leftFrames = LoadFrames(player, backView ? "back_left" : "front_left", banana: true);
        Sprite[] rightFrames = LoadFrames(player, backView ? "back_right" : "front_right", banana: false);

        GameObject characterGo = new GameObject("CharacterSprite");
        characterGo.transform.SetParent(visualRoot, false);
        SpriteRenderer character = characterGo.AddComponent<SpriteRenderer>();
        character.sprite = rightFrames[0];
        // 요구 레이어: Back Character(40) > Back Bush(30) > Front Bush(20) > Front Character(10).
        character.sortingOrder = backView ? 40 : 10;
        FitWidth(character, characterWidth);

        GameObject bushGo = new GameObject("CoverBush");
        bushGo.transform.SetParent(slot, false);
        bushGo.transform.localPosition = new Vector3(0f, backView ? -1.15f : -.7f, 0f);
        SpriteRenderer bush = bushGo.AddComponent<SpriteRenderer>();
        bush.sprite = ArtAssets.LoadStoneOrBanana(player == PlayerId.P1
            ? "prop_cover_bush_p1" : "prop_cover_bush_p2");
        bush.sortingOrder = backView ? 30 : 20;
        FitWidth(bush, characterWidth * (backView ? 1.18f : 1.25f));

        float handX = characterWidth * .27f;
        float handY = characterWidth * (backView ? .23f : .28f);
        Transform leftRelease = Child(visualRoot, "LeftHandRelease",
            new Vector3(backView ? -handX : handX, handY, 0f));
        Transform rightRelease = Child(visualRoot, "RightHandRelease",
            new Vector3(backView ? handX : -handX, handY, 0f));
        Transform receive = Child(visualRoot, "ReceiveAnchor",
            new Vector3(0f, characterWidth * (backView ? .27f : .3f), 0f));

        int number = player == PlayerId.P1 ? 1 : 2;
        StoneOrBananaCharacterView view = slot.gameObject.AddComponent<StoneOrBananaCharacterView>();
        SetRefs(view,
            ("characterRenderer", character), ("bushRenderer", bush),
            ("leftHandReleaseAnchor", leftRelease), ("rightHandReleaseAnchor", rightRelease),
            ("receiveAnchor", receive), ("leftHandFrames", leftFrames), ("rightHandFrames", rightFrames),
            ("mouthClosedSprite", LoadSprite(CharacterPath + $"p{number}_stone_or_banana_mouth_closed.png")),
            ("mouthOpenSprite", LoadSprite(CharacterPath + $"p{number}_stone_or_banana_mouth_open.png")),
            ("bananaChewingSprite", LoadSprite(CharacterPath + $"p{number}_stone_or_banana_banana_chewing.png")),
            ("stoneHitOneToothSprite", LoadSprite(CharacterPath + $"p{number}_stone_or_banana_stone_hit_one_tooth_broken.png")),
            ("stoneHitTwoTeethSprite", LoadSprite(CharacterPath + $"p{number}_stone_or_banana_stone_hit_two_teeth_broken.png")));
        SerializedObject serialized = new SerializedObject(view);
        serialized.FindProperty("player").enumValueIndex = (int)player;
        serialized.FindProperty("backView").boolValue = backView;
        serialized.FindProperty("displayWidth").floatValue = characterWidth;
        serialized.FindProperty("heldPoseFrameIndex").intValue = 2;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);
        return view;
    }

    private static SpriteRenderer CreateTemplate(Transform parent, string name, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        // StoneThrow와 같은 원칙: Back 캐릭터 바로 아래, 나머지 캐릭터/수풀보다 위.
        renderer.sortingOrder = 35;
        go.SetActive(false);
        return renderer;
    }

    private static Sprite[] LoadFrames(PlayerId player, string view, bool banana)
    {
        int number = player == PlayerId.P1 ? 1 : 2;
        string action = banana ? "stone_or_banana" : "stone_throw";
        Sprite[] frames = Enumerable.Range(1, 6)
            .Select(i => LoadSprite(CharacterPath + $"p{number}_{action}_{view}_{i}.png")).ToArray();
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
            foreach (string view in new[] { "front_right", "back_right" })
                for (int frame = 1; frame <= 6; frame++)
                    ConfigureTexture(CharacterPath + $"p{player}_stone_throw_{view}_{frame}.png");
            foreach (string view in new[] { "front_left", "back_left" })
                for (int frame = 1; frame <= 6; frame++)
                    ConfigureTexture(CharacterPath + $"p{player}_stone_or_banana_{view}_{frame}.png");
            foreach (string state in new[] { "mouth_closed", "mouth_open", "banana_chewing",
                         "stone_hit_one_tooth_broken", "stone_hit_two_teeth_broken" })
                ConfigureTexture(CharacterPath + $"p{player}_stone_or_banana_{state}.png");
        }
        ConfigureTexture("Assets/Resources/StoneOrBanana/background.png");
    }

    private static void ConfigureTexture(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        bool dirty = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
                     Math.Abs(importer.spritePixelsPerUnit - 100f) > .01f;
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
        if (renderer?.sprite == null) return;
        float nativeWidth = renderer.sprite.bounds.size.x;
        if (nativeWidth > 0f) renderer.transform.localScale = Vector3.one * (width / nativeWidth);
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
        EditorUtility.SetDirty(target);
    }
}
#endif

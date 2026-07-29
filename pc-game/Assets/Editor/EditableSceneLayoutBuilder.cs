#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 최초 레이아웃 생성/전체 초기화용 도구다. 한 번 생성한 뒤에는 Hierarchy와 Prefab Mode에서
// 직접 편집한다. 다시 실행하면 EditableLayout 아래의 수동 수정이 덮어써지므로 메뉴에 경고한다.
public static class EditableSceneLayoutBuilder
{
    private const string SceneDir = "Assets/Scenes/";
    private const string PrefabDir = "Assets/Prefabs/";
    private const float StandardCameraSize = 5f;

    [MenuItem("Tools/Uga Uga/Rebuild All Editable Scene Layouts...")]
    private static void RebuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("레이아웃 전체 재생성",
            "6개 씬의 EditableLayout과 Hub UI를 기본값으로 다시 만듭니다. 수동 배치 수정은 덮어써집니다.",
            "재생성", "취소")) return;
        RebuildAll();
    }

    [MenuItem("Tools/Uga Uga/Fix All Canvas Scene View")]
    public static void FixMiniGameCanvasSceneView()
    {
        string[] names = { "Hub", "StoneThrow", "PoseCopy", "FruitJump", "CoconutCrack", "StoneOrBanana", "StaringContest" };
        foreach (string name in names)
        {
            Scene scene = Open(name);
            if (name == "Hub")
            {
                string prefabPath = PrefabDir + "StartScreenCanvas.prefab";
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                HudWidgets.ConfigureForGameCamera(prefabRoot.GetComponent<Canvas>());
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
            foreach (Canvas canvas in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Canvas>(true)))
            {
                HudWidgets.ConfigureForGameCamera(canvas);
                EditorUtility.SetDirty(canvas);
            }
            Save(scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] Hub + 미니게임 Canvas를 편집용 World Space로 변환 완료");
    }

    [MenuItem("Tools/Uga Uga/Normalize All Cameras to Size 5")]
    public static void NormalizeAllCameras()
    {
        string[] names = { "Hub", "StoneThrow", "PoseCopy", "FruitJump", "CoconutCrack", "StoneOrBanana", "StaringContest" };
        foreach (string name in names)
        {
            Scene scene = Open(name);
            Camera cam = Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = StandardCameraSize;
            EditorUtility.SetDirty(cam);

            foreach (SpriteRenderer background in scene.GetRootGameObjects()
                         .SelectMany(x => x.GetComponentsInChildren<SpriteRenderer>(true))
                         .Where(x => x.name == "Background" && x.sprite != null))
            {
                float height = StandardCameraSize * 2f;
                float width = height * 16f / 9f;
                Vector2 native = background.sprite.bounds.size;
                background.transform.localScale = Vector3.one * Mathf.Max(width / native.x, height / native.y);
                CameraBackgroundFitter fitter = background.GetComponent<CameraBackgroundFitter>();
                if (fitter == null) fitter = background.gameObject.AddComponent<CameraBackgroundFitter>();
                fitter.Fit();
                EditorUtility.SetDirty(background.transform);
            }

            foreach (Canvas canvas in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Canvas>(true)))
            {
                HudWidgets.ConfigureForGameCamera(canvas);
                EditorUtility.SetDirty(canvas);
            }
            Save(scene);
        }

        Open("Hub");
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] Hub + 미니게임 6개 씬 Camera Size를 5로 통일 완료");
    }

    [MenuItem("Tools/Uga Uga/Apply Common Timer and Match Scoreboard")]
    public static void ApplyCommonMatchHud()
    {
        string[] names = { "StoneThrow", "PoseCopy", "FruitJump", "CoconutCrack", "StoneOrBanana", "StaringContest" };
        foreach (string name in names)
        {
            Scene scene = Open(name);
            Canvas canvas = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Canvas>(true)).FirstOrDefault();
            if (canvas == null) throw new InvalidOperationException($"{name} 씬에서 HUD Canvas를 찾지 못했습니다.");

            Image timerPlate = canvas.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.name == "TimerPlate");
            if (timerPlate == null) throw new InvalidOperationException($"{name} 씬에서 TimerPlate를 찾지 못했습니다.");
            timerPlate.sprite = ArtAssets.LoadUi("time_remaining");
            timerPlate.preserveAspect = true;
            EditorUtility.SetDirty(timerPlate);

            MatchScoreboardHud old = canvas.GetComponentInChildren<MatchScoreboardHud>(true);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            MatchScoreboardHud.Build(canvas.transform);
            Save(scene);
        }

        Open("Hub");
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] 미니게임 6개 씬 공용 타이머 + 매치 결과판 적용 완료");
    }

    [MenuItem("Tools/Uga Uga/Hub/Apply Camera Cover Background")]
    public static void ApplyHubCameraCoverBackground()
    {
        string path = PrefabDir + "StartScreenCanvas.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Image background = prefabRoot.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(x => x.name == "Background");
            if (background == null)
                throw new InvalidOperationException("StartScreenCanvas.prefab에서 Background Image를 찾지 못했습니다.");

            CameraBackgroundImageFitter fitter = background.GetComponent<CameraBackgroundImageFitter>();
            if (fitter == null) fitter = background.gameObject.AddComponent<CameraBackgroundImageFitter>();
            background.preserveAspect = true;
            fitter.Fit();
            EditorUtility.SetDirty(background);
            EditorUtility.SetDirty(fitter);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Uga Uga] Hub 배경을 카메라 cover 방식으로 적용 완료");
    }

    [MenuItem("Tools/Uga Uga/StoneThrow/Apply Split Over-the-Shoulder Characters")]
    public static void ApplyStoneThrowSplitCharacters()
    {
        StoneThrowSceneSetup.Rebuild();
    }

    // -executeMethod EditableSceneLayoutBuilder.RebuildAll 로 CI/초기 마이그레이션에서도 호출 가능.
    public static void RebuildAll()
    {
        AssetDatabase.Refresh();
        BuildCavemanPrefab(PlayerId.P1);
        BuildCavemanPrefab(PlayerId.P2);
        BuildCavemanPrefab(PlayerId.P1, backView: true);
        BuildCavemanPrefab(PlayerId.P2, backView: true);
        BuildHub();
        BuildStoneThrow();
        BuildPoseCopy();
        BuildFruitJump();
        BuildCoconutCrack();
        BuildStoneOrBanana();
        BuildStaringContest();
        AssetDatabase.SaveAssets();
        Debug.Log("[Uga Uga] 편집 가능한 Hub + 미니게임 6개 씬 레이아웃 생성 완료");
    }

    private static void BuildCavemanPrefab(PlayerId player)
        => BuildCavemanPrefab(player, backView: false);

    private static void BuildCavemanPrefab(PlayerId player, bool backView)
    {
        string suffix = backView ? "_Back" : "";
        GameObject root = BuildCavemanObject($"Caveman_{player}{suffix}", player, backView);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + $"Caveman_{player}{suffix}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static GameObject BuildCavemanObject(string name, PlayerId player, bool backView)
    {
        var root = new GameObject(name);
        var silhouette = root.AddComponent<CavemanSilhouette>();
        silhouette.player = player;

        Func<string, Sprite> load = part => backView
            ? ArtAssets.LoadCharacterBack(player, part)
            : ArtAssets.LoadCharacter(player, part);
        Sprite headSprite = load("head");
        Sprite torsoSprite = load("torso");
        Sprite lua = load("left_upper_arm");
        Sprite lla = load("left_lower_arm_hand");
        Sprite rua = load("right_upper_arm");
        Sprite rla = load("right_lower_arm_hand");
        Sprite lul = load("left_upper_leg");
        Sprite lll = load("left_lower_leg_foot");
        Sprite rul = load("right_upper_leg");
        Sprite rll = load("right_lower_leg_foot");

        if (new[] { headSprite, torsoSprite, lua, lla, rua, rla, lul, lll, rul, rll }.Any(x => x == null))
            throw new InvalidOperationException($"{player} {(backView ? "back" : "front")} 캐릭터 파츠를 모두 불러오지 못했습니다.");

        const float torsoHeight = 0.75f;
        float scale = torsoHeight / Mathf.Max(0.01f, torsoSprite.bounds.size.y);
        float torsoWidth = torsoSprite.bounds.size.x * scale;
        float headHeight = headSprite.bounds.size.y * scale;
        float upperArmLength = lua.bounds.size.y * scale;
        float lowerArmLength = lla.bounds.size.y * scale;
        float upperLegLength = lul.bounds.size.y * scale;
        float lowerLegLength = lll.bounds.size.y * scale;
        float torsoBottom = lowerLegLength + upperLegLength - upperLegLength * 0.25f;
        float torsoTop = torsoBottom + torsoHeight;

        CreatePart(root.transform, "LeftUpperLeg", lul, new Vector3(-torsoWidth * .18f, lowerLegLength + upperLegLength * .5f), scale, -1);
        CreatePart(root.transform, "LeftLowerLegFoot", lll, new Vector3(-torsoWidth * .18f, lowerLegLength * .5f), scale, -1);
        CreatePart(root.transform, "RightUpperLeg", rul, new Vector3(torsoWidth * .18f, lowerLegLength + upperLegLength * .5f), scale, -1);
        CreatePart(root.transform, "RightLowerLegFoot", rll, new Vector3(torsoWidth * .18f, lowerLegLength * .5f), scale, -1);
        CreatePart(root.transform, "Torso", torsoSprite, new Vector3(0, torsoBottom + torsoHeight * .5f), scale, 0);
        SpriteRenderer head = CreatePart(root.transform, "Head", headSprite, new Vector3(0, torsoTop + headHeight * .42f), scale, 2);

        Transform leftShoulder = CreatePivot(root.transform, "LeftShoulderPivot", new Vector3(-torsoWidth * .36f, torsoBottom + torsoHeight * .83f));
        Transform rightShoulder = CreatePivot(root.transform, "RightShoulderPivot", new Vector3(torsoWidth * .36f, torsoBottom + torsoHeight * .83f));
        Transform leftUpper = CreatePart(leftShoulder, "LeftUpperArm", lua, new Vector3(0, -upperArmLength * .5f), scale, 1).transform;
        Transform rightUpper = CreatePart(rightShoulder, "RightUpperArm", rua, new Vector3(0, -upperArmLength * .5f), scale, 1).transform;
        Transform leftElbow = CreatePivot(root.transform, "LeftElbowPivot", leftShoulder.localPosition + Vector3.down * upperArmLength);
        Transform rightElbow = CreatePivot(root.transform, "RightElbowPivot", rightShoulder.localPosition + Vector3.down * upperArmLength);
        Transform leftLower = CreatePart(leftElbow, "LeftLowerArmHand", lla, new Vector3(0, -lowerArmLength * .5f), scale, 1).transform;
        Transform rightLower = CreatePart(rightElbow, "RightLowerArmHand", rla, new Vector3(0, -lowerArmLength * .5f), scale, 1).transform;

        SetRefs(silhouette, ("head", head), ("leftShoulderPivot", leftShoulder), ("rightShoulderPivot", rightShoulder),
            ("leftElbowPivot", leftElbow), ("rightElbowPivot", rightElbow), ("leftUpperArm", leftUpper),
            ("rightUpperArm", rightUpper), ("leftLowerArm", leftLower), ("rightLowerArm", rightLower),
            ("mirrorTrackedPoseHorizontally", backView));
        return root;
    }

    private static SpriteRenderer CreatePart(Transform parent, string name, Sprite sprite, Vector3 localPosition, float scale, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    private static Transform CreatePivot(Transform parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    private static Scene Open(string name) => EditorSceneManager.OpenScene(SceneDir + name + ".unity", OpenSceneMode.Single);

    private static Transform ResetLayout(Scene scene)
    {
        GameObject old = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "EditableLayout");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        return new GameObject("EditableLayout").transform;
    }

    private static void Save(Scene scene) => EditorSceneManager.SaveScene(scene);

    private static void SetupCamera(float size)
    {
        Camera cam = Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.transform.position = new Vector3(0, 1, -10);
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Vector3 position, float width, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        ArtAssets.FitWidth(sr, width);
        return sr;
    }

    private static void AddBackground(Transform root, Sprite sprite, float cameraSize)
    {
        SpriteRenderer sr = AddSprite(root, "Background", sprite, new Vector3(0, 1, 0), 1, -100);
        float h = cameraSize * 2f;
        float w = h * 16f / 9f;
        Vector2 native = sprite.bounds.size;
        sr.transform.localScale = Vector3.one * Mathf.Max(w / native.x, h / native.y);
        sr.gameObject.AddComponent<CameraBackgroundFitter>();
    }

    private static CavemanSilhouette AddCaveman(Transform root, PlayerId id, Vector3 pos)
        => AddCaveman(root, id, backView: false, $"Caveman_{id}", pos, 1f, 0);

    private static CavemanSilhouette AddCaveman(Transform root, PlayerId id, bool backView, string name,
        Vector3 pos, float scale, int sortingOffset)
    {
        string suffix = backView ? "_Back" : "";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + $"Caveman_{id}{suffix}.prefab");
        if (prefab == null) throw new InvalidOperationException($"Caveman_{id}{suffix}.prefab을 찾지 못했습니다.");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(root, false);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * scale;
        foreach (SpriteRenderer renderer in go.GetComponentsInChildren<SpriteRenderer>(true))
            renderer.sortingOrder += sortingOffset;
        return go.GetComponent<CavemanSilhouette>();
    }

    private static void CreateStoneThrowCharacters(Transform root, out CavemanSilhouette p1Front,
        out CavemanSilhouette p1Back, out CavemanSilhouette p2Front, out CavemanSilhouette p2Back)
    {
        // 문서 5장: 왼쪽=P2 앞모습 와이드 + P1 뒷모습 바스트,
        // 오른쪽=P1 앞모습 와이드 + P2 뒷모습 바스트.
        p2Front = AddCaveman(root, PlayerId.P2, false, "Left View - P2 Front (Target)",
            new Vector3(-4.45f, -3.15f, 0f), 1.35f, 0);
        p1Back = AddCaveman(root, PlayerId.P1, true, "Left View - P1 Back (Thrower)",
            new Vector3(-0.85f, -5.0f, 0f), 2.1f, 20);
        p1Front = AddCaveman(root, PlayerId.P1, false, "Right View - P1 Front (Target)",
            new Vector3(4.45f, -3.15f, 0f), 1.35f, 0);
        p2Back = AddCaveman(root, PlayerId.P2, true, "Right View - P2 Back (Thrower)",
            new Vector3(8.0f, -5.0f, 0f), 2.1f, 20);
    }

    private static T Controller<T>() where T : Component => UnityEngine.Object.FindAnyObjectByType<T>();

    private static void BuildStoneThrow()
    {
        Scene s = Open("StoneThrow"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadStoneThrow("background"), StandardCameraSize);
        StoneThrowHud hud = StoneThrowHud.Build(30); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        StoneThrowSceneSetup.RebuildScene(s);
        Save(s);
    }

    private static void BuildPoseCopy()
    {
        Scene s = Open("PoseCopy"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadPoseMatch("background"), StandardCameraSize);
        var p1 = AddCaveman(root, PlayerId.P1, new Vector3(-3, 0, 0));
        var p2 = AddCaveman(root, PlayerId.P2, new Vector3(3, 0, 0));
        PoseMatchHud hud = PoseMatchHud.Build(40.8f); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        SetRefs(Controller<PoseCopyGame>(), ("p1Silhouette", p1), ("p2Silhouette", p2), ("hud", hud)); Save(s);
    }

    private static void BuildFruitJump()
    {
        Scene s = Open("FruitJump"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadFruitJump("background"), StandardCameraSize);
        var p1 = AddCaveman(root, PlayerId.P1, new Vector3(-2.5f, 0, 0));
        var p2 = AddCaveman(root, PlayerId.P2, new Vector3(2.5f, 0, 0));
        var j1 = new GameObject("P1 JumpCalibrator").AddComponent<JumpHeightCalibrator>(); j1.transform.SetParent(root); j1.player = PlayerId.P1;
        var j2 = new GameObject("P2 JumpCalibrator").AddComponent<JumpHeightCalibrator>(); j2.transform.SetParent(root); j2.player = PlayerId.P2;
        SpriteRenderer[] f1 = AddFruits(root, "P1 Fruits", -2.5f);
        SpriteRenderer[] f2 = AddFruits(root, "P2 Fruits", 2.5f);
        FruitJumpHud hud = FruitJumpHud.Build(30); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        SetRefs(Controller<FruitJumpGame>(), ("p1Silhouette", p1), ("p2Silhouette", p2), ("p1Jump", j1), ("p2Jump", j2),
            ("p1Fruits", f1), ("p2Fruits", f2), ("hud", hud)); Save(s);
    }

    private static SpriteRenderer[] AddFruits(Transform root, string name, float x)
    {
        var group = new GameObject(name); group.transform.SetParent(root, false);
        string[] props = { "fruit_apple", "fruit_grapes", "fruit_pineapple" };
        float[] heights = { 1.525f, 2.225f, 2.925f };
        return props.Select((p, i) => AddSprite(group.transform, $"Tier {i + 1} - {p}", ArtAssets.LoadProp(p), new Vector3(x, heights[i], .5f), .4f, 2)).ToArray();
    }

    private static void BuildCoconutCrack()
    {
        Scene s = Open("CoconutCrack"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadCoconutBreak("background"), StandardCameraSize);
        var p1 = AddCaveman(root, PlayerId.P1, new Vector3(-2, 0, 0));
        var p2 = AddCaveman(root, PlayerId.P2, new Vector3(2, 0, 0));
        AddSprite(root, "P1 StoneTable", ArtAssets.LoadCoconutBreak("prop_stone_table_p1"), new Vector3(-2, -.6f, 1), 1.6f, -1);
        AddSprite(root, "P2 StoneTable", ArtAssets.LoadCoconutBreak("prop_stone_table_p2"), new Vector3(2, -.6f, 1), 1.6f, -1);
        var c1 = AddSprite(p1.transform, "Coconut", ArtAssets.LoadProp("coconut"), p1.transform.position + Vector3.up * 1.6f, .5f, 3);
        var c2 = AddSprite(p2.transform, "Coconut", ArtAssets.LoadProp("coconut"), p2.transform.position + Vector3.up * 1.6f, .5f, 3);
        CoconutBreakHud hud = CoconutBreakHud.Build(15); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        SetRefs(Controller<CoconutCrackGame>(), ("p1Silhouette", p1), ("p2Silhouette", p2), ("p1Coconut", c1), ("p2Coconut", c2), ("hud", hud)); Save(s);
    }

    private static void BuildStoneOrBanana()
    {
        Scene s = Open("StoneOrBanana"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadStoneOrBanana("background"), StandardCameraSize);
        var p1 = AddCaveman(root, PlayerId.P1, new Vector3(-2, 0, 0));
        var p2 = AddCaveman(root, PlayerId.P2, new Vector3(2, 0, 0));
        AddSprite(p1.transform, "CoverBush", ArtAssets.LoadStoneOrBanana("prop_cover_bush_p1"), p1.transform.position + Vector3.up * .35f, 1.3f, 3);
        AddSprite(p2.transform, "CoverBush", ArtAssets.LoadStoneOrBanana("prop_cover_bush_p2"), p2.transform.position + Vector3.up * .35f, 1.3f, 3);
        StoneOrBananaHud hud = StoneOrBananaHud.Build(); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        SetRefs(Controller<StoneOrBananaGame>(), ("p1Silhouette", p1), ("p2Silhouette", p2), ("hud", hud)); Save(s);
    }

    private static void BuildStaringContest()
    {
        Scene s = Open("StaringContest"); Transform root = ResetLayout(s); SetupCamera(StandardCameraSize);
        AddBackground(root, ArtAssets.LoadStaringContest("background"), StandardCameraSize);
        Transform p1Anchor = CreatePivot(root, "P1 Anchor", new Vector3(-3.6f, -3f, 0));
        Transform p2Anchor = CreatePivot(root, "P2 Anchor", new Vector3(3.6f, -3f, 0));
        AddSprite(root, "StareClash", ArtAssets.LoadStaringContest("effect_stare_clash"), new Vector3(0, .2f, 0), 2, 2);
        var t1 = new GameObject("P1 EyeTimer").AddComponent<EyeCloseTimer>(); t1.transform.SetParent(root); t1.player = PlayerId.P1;
        var t2 = new GameObject("P2 EyeTimer").AddComponent<EyeCloseTimer>(); t2.transform.SetParent(root); t2.player = PlayerId.P2;
        StaringContestHud hud = StaringContestHud.Build(60); hud.transform.SetParent(root, false);
        MatchScoreboardHud.Build(hud.transform);
        SetRefs(Controller<StaringContestGame>(), ("p1Anchor", p1Anchor), ("p2Anchor", p2Anchor), ("p1Timer", t1), ("p2Timer", t2), ("hud", hud)); Save(s);
    }

    private static void BuildHub()
    {
        Scene s = Open("Hub");
        foreach (GameObject go in s.GetRootGameObjects().Where(x => x.name == "StartScreenCanvas" || x.name == "ResultScreenCanvas" || x.name == "EventSystem").ToArray())
            UnityEngine.Object.DestroyImmediate(go);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "StartScreenCanvas.prefab");
        var canvas = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        HudWidgets.ConfigureForGameCamera(canvas.GetComponent<Canvas>());
        Image hubBackground = canvas.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.name == "Background");
        if (hubBackground != null)
        {
            CameraBackgroundImageFitter fitter = hubBackground.GetComponent<CameraBackgroundImageFitter>();
            if (fitter == null) fitter = hubBackground.gameObject.AddComponent<CameraBackgroundImageFitter>();
            fitter.Fit();
        }
        var eventGo = new GameObject("EventSystem"); eventGo.AddComponent<EventSystem>(); eventGo.AddComponent<InputSystemUIInputModule>();
        Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
        GameObject resultCanvas = BuildResultCanvas(out Text resultText, out Button restartButton);
        HudWidgets.ConfigureForGameCamera(resultCanvas.GetComponent<Canvas>());
        SetRefs(Controller<HubController>(), ("startScreenCanvas", canvas),
            ("gameStartButton", buttons.First(x => x.name == "ButtonGameStart")),
            ("howToPlayButton", buttons.First(x => x.name == "ButtonHowToPlay")),
            ("settingsButton", buttons.First(x => x.name == "ButtonSettings")),
            ("exitButton", buttons.First(x => x.name == "ButtonExit")),
            ("resultScreenCanvas", resultCanvas), ("resultText", resultText), ("restartButton", restartButton));
        Save(s);
    }

    private static GameObject BuildResultCanvas(out Text resultText, out Button restartButton)
    {
        var root = new GameObject("ResultScreenCanvas");
        var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2048, 1152); scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("ResultPanel"); panel.transform.SetParent(root.transform, false);
        var panelRt = panel.AddComponent<RectTransform>(); panelRt.anchorMin = new Vector2(.2f, .18f); panelRt.anchorMax = new Vector2(.8f, .82f);
        panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
        var panelImage = panel.AddComponent<Image>(); panelImage.color = new Color(.16f, .08f, .03f, .94f);

        Text title = HudWidgets.CreateText(panelRt, "Title", new Vector2(.5f, .78f), 1000, 72);
        title.text = "우가우가게임";
        resultText = HudWidgets.CreateText(panelRt, "ResultText", new Vector2(.5f, .53f), 1100, 54);
        resultText.text = "P1 최종 승리!  0 : 0";

        var buttonGo = new GameObject("RestartButton"); buttonGo.transform.SetParent(panelRt, false);
        var buttonRt = buttonGo.AddComponent<RectTransform>(); buttonRt.anchorMin = buttonRt.anchorMax = new Vector2(.5f, .22f);
        buttonRt.sizeDelta = new Vector2(420, 110);
        var image = buttonGo.AddComponent<Image>(); image.color = new Color(.72f, .38f, .12f, 1);
        restartButton = buttonGo.AddComponent<Button>(); restartButton.targetGraphic = image;
        Text label = HudWidgets.CreateText(buttonRt, "Label", new Vector2(.5f, .5f), 380, 42); label.text = "다시 시작";
        root.SetActive(false);
        return root;
    }

    private static void SetRefs(UnityEngine.Object target, params (string name, object value)[] values)
    {
        var so = new SerializedObject(target);
        foreach ((string name, object value) in values)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new InvalidOperationException($"{target.GetType().Name}.{name} 직렬화 필드를 찾을 수 없습니다.");
            if (value is UnityEngine.Object obj) p.objectReferenceValue = obj;
            else if (value is bool boolean) p.boolValue = boolean;
            else if (value is Array array)
            {
                p.arraySize = array.Length;
                for (int i = 0; i < array.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = array.GetValue(i) as UnityEngine.Object;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
#endif

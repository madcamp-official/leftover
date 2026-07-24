using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class BossDuelAssetLibraryBuilder
{
    private const string ResourceFolder = "Assets/Resources";
    private const string DuelFolder = ResourceFolder + "/BossDuel";
    private const string LibraryPath = DuelFolder + "/BossDuelAssetLibrary.asset";
    private const string ControllerPath = DuelFolder + "/BossDuelCombat.controller";
    private const string UpperBodyMaskPath = DuelFolder + "/BossDuelUpperBody.mask";
    private const string FighterPath =
        "Assets/EEJANAI_Team/FreeSwordAnimations/Prefabs/EEJANAIbotSword1.prefab";
    private const string AnimationFolder =
        "Assets/EEJANAI_Team/FreeSwordAnimations/Animations/";
    private const string DungeonFolder =
        "Assets/ThirdParty/KenneyModularDungeon/Models/FBX format/";
    private const string ParticleFolder =
        "Assets/ThirdParty/KenneyParticlePack/PNG/";
    private const string RpgAudioFolder =
        "Assets/ThirdParty/KenneyCombatAudio/RPG/";
    private const string ImpactAudioFolder =
        "Assets/ThirdParty/KenneyCombatAudio/Impact/";
    private const string KevinPrefabFolder =
        "Assets/Kevin Iglesias/Skeleton Animations/Prefabs/";

    private static readonly Dictionary<string, string> StateClips = new()
    {
        { "Idle", "battle stance.anim" },
        // Measured with BossDuelSwordClipAudit: slash2 has the strongest
        // lateral hand travel, while slash9 has the strongest vertical travel.
        { "HorizontalSlash", "slash2.anim" },
        { "VerticalSlash", "slash9.anim" },
        { "Kick", "slash8.anim" },
        { "Guard", "deffensive stance.anim" },
        { "Parry", "slash4.anim" },
        { "DodgeCrouch", "deffensive stance.anim" },
        { "DodgeLeft", "slash7.anim" },
        { "Hit", "damaged (tired) stance.anim" },
        { "Stagger", "damaged (tired) stance.anim" },
        { "Dead", "damaged (tired) stance.anim" }
    };

    static BossDuelAssetLibraryBuilder()
    {
        EditorApplication.delayCall += Build;
    }

    [MenuItem("Tools/Boss Duel/Rebuild Asset Library")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureFolder(ResourceFolder, "Resources");
        EnsureFolder(DuelFolder, "BossDuel");

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        while (controller.layers.Length > 1)
            controller.RemoveLayer(1);

        AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in baseMachine.states)
            baseMachine.RemoveState(child.state);

        AnimationClip idleClip =
            AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + StateClips["Idle"]);
        AnimatorState idleState = baseMachine.AddState("Idle");
        idleState.motion = idleClip;
        idleState.writeDefaultValues = true;
        baseMachine.defaultState = idleState;

        AvatarMask upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (upperBodyMask == null)
        {
            upperBodyMask = new AvatarMask();
            AssetDatabase.CreateAsset(upperBodyMask, UpperBodyMaskPath);
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            upperBodyMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        upperBodyMask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);

        var actionMachine = new AnimatorStateMachine { name = "Grounded Upper Body" };
        AssetDatabase.AddObjectToAsset(actionMachine, controller);
        AnimatorState actionReady = actionMachine.AddState("ActionReady");
        actionReady.motion = idleClip;
        actionMachine.defaultState = actionReady;

        foreach (KeyValuePair<string, string> entry in StateClips)
        {
            if (entry.Key == "Idle")
                continue;

            AnimationClip clip =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + entry.Value);
            AnimatorState state = actionMachine.AddState(entry.Key);
            state.motion = clip;
            state.writeDefaultValues = true;
        }

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Grounded Actions",
            avatarMask = upperBodyMask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
            iKPass = true,
            stateMachine = actionMachine
        });

        BossDuelAssetLibrary library =
            AssetDatabase.LoadAssetAtPath<BossDuelAssetLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<BossDuelAssetLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        library.fighterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FighterPath);
        library.combatController = controller;
        library.dungeonRoom =
            AssetDatabase.LoadAssetAtPath<GameObject>(DungeonFolder + "room-large.fbx");
        library.dungeonGate =
            AssetDatabase.LoadAssetAtPath<GameObject>(DungeonFolder + "gate-door.fbx");
        library.dungeonCorridor =
            AssetDatabase.LoadAssetAtPath<GameObject>(DungeonFolder + "corridor-wide.fbx");
        library.slashSprite = LoadParticleSprite("slash_02.png");
        library.impactSprite = LoadParticleSprite("star_03.png");
        library.guardSprite = LoadParticleSprite("circle_03.png");
        library.parrySprite = LoadParticleSprite("spark_04.png");
        library.kevinSwordPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(KevinPrefabFolder + "SkeletonSword.prefab");
        library.kevinShieldPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(KevinPrefabFolder + "SkeletonShield.prefab");
        library.swordSlice =
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "knifeSlice.ogg");
        library.swordSliceHeavy =
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "knifeSlice2.ogg");
        library.swordDraw =
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "drawKnife2.ogg");
        library.swordHit =
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "chop.ogg");
        library.shieldBlock =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactMetal_heavy_000.ogg");
        library.shieldBlockHeavy =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactMetal_heavy_001.ogg");
        library.parryBell =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactBell_heavy_001.ogg");
        library.guardBreak =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPlate_heavy_003.ogg");
        library.bodyImpactHeavy =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPunch_heavy_001.ogg");
        library.bodyImpactMedium =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPunch_medium_003.ogg");
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(upperBodyMask);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    private static Sprite LoadParticleSprite(string fileName)
    {
        string path = ParticleFolder + fileName;
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null &&
            (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 512f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string path, string folderName)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = path[..path.LastIndexOf('/')];
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

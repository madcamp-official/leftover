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
    // Space-warrior body. Rigged but unanimated, so it retargets the same
    // EEJANAI/Human Melee Humanoid clips used for the previous knight model.
    private const string FighterPath =
        "Assets/MyAssets/CyberSoldier/CyberSoldier.fbx";
    private const string AnimationFolder =
        "Assets/EEJANAI_Team/FreeSwordAnimations/Animations/";
    private const string HumanMeleeFolder =
        "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/";
    private const string HumanMeleeHorizontalPath =
        HumanMeleeFolder + "1H/HumanM@Attack1H01_R.fbx";
    private const string HumanMeleeParryPath =
        HumanMeleeFolder + "Shield/HumanM@AttackShield01.fbx";
    private const string HumanMeleeDamagePath =
        HumanMeleeFolder + "HumanM@CombatDamage01.fbx";
    // Mixamo's "Sword And Shield" pack (Adobe, free w/ account) — a single coherent
    // motion-captured source replacing the EEJANAI/Kevin Iglesias mashup for every
    // combat state. Downloaded "Without Skin"/"With Skin" FBX (either works, only the
    // animation sub-asset is used) via mixamo.com, Humanoid retargeting is forced below.
    private const string MixamoFolder = "Assets/Mixamo/Animations/";
    private static readonly string[] MixamoAnimationFiles =
    {
        "Sword And Shield Idle (Idle).fbx",
        "Sword And Shield Slash - Cross Slash (Horizontal).fbx",
        "Sword And Shield Slash - Downward Slash (Vertical).fbx",
        "Sword And Shield Kick - Sparta Kick (Kick).fbx",
        "Sword And Shield Block Idle (Guard).fbx",
        "Sword And Shield Block - Idle To Block (Parry).fbx",
        "Sword And Shield Crouch Block Idle (DodgeCrouch).fbx",
        "Sword And Shield Strafe - Left Walk (DodgeLeft).fbx",
        "Sword And Shield Impact - Unblocked (Hit-Stagger).fbx",
        "Sword And Shield Death - Falling Back (Dead).fbx"
    };
    private const string SciFiModularFolder =
        "Assets/Sci Fi Modular Pack/Prefabs/";
    private const string ParticleFolder =
        "Assets/ThirdParty/KenneyParticlePack/PNG/";
    private const string SlashVfxFolder =
        "Assets/slash5-HungNguyen/prefab/slash/";
    private const string SkyboxPath =
        "Assets/SpaceSkies Free/Skybox_3/Purple_2K_Resolution.mat";
    private const string LaserAudioFolder =
        "Assets/Laser Weapons Sound Pack/Free/";
    private const string SciFiWeaponAudioFolder =
        "Assets/TII_SoundLibrary_3Steps/SCI-FI/Weapons/";
    private const string SciFiShieldAudioFolder =
        "Assets/TII_SoundLibrary_3Steps/SCI-FI/Shield/";
    private const string SciFiWhooshAudioFolder =
        "Assets/TII_SoundLibrary_3Steps/SCI-FI/Whooshs/";

    private static readonly Dictionary<string, string> StateClipPaths = new()
    {
        { "Idle", MixamoFolder + "Sword And Shield Idle (Idle).fbx" },
        { "HorizontalSlash", MixamoFolder + "Sword And Shield Slash - Cross Slash (Horizontal).fbx" },
        { "VerticalSlash", MixamoFolder + "Sword And Shield Slash - Downward Slash (Vertical).fbx" },
        { "Kick", MixamoFolder + "Sword And Shield Kick - Sparta Kick (Kick).fbx" },
        { "Guard", MixamoFolder + "Sword And Shield Block Idle (Guard).fbx" },
        { "Parry", MixamoFolder + "Sword And Shield Block - Idle To Block (Parry).fbx" },
        { "DodgeCrouch", MixamoFolder + "Sword And Shield Crouch Block Idle (DodgeCrouch).fbx" },
        { "DodgeLeft", MixamoFolder + "Sword And Shield Strafe - Left Walk (DodgeLeft).fbx" },
        { "Hit", MixamoFolder + "Sword And Shield Impact - Unblocked (Hit-Stagger).fbx" },
        { "Stagger", MixamoFolder + "Sword And Shield Impact - Unblocked (Hit-Stagger).fbx" },
        { "Dead", MixamoFolder + "Sword And Shield Death - Falling Back (Dead).fbx" }
    };

    // If a teammate hasn't installed the Mixamo pack yet, fall back to whatever this
    // state used before the Mixamo pass (EEJANAI / Kevin Iglesias Human Melee FREE).
    private static readonly Dictionary<string, string> StateClipFallbackPaths = new()
    {
        { "Idle", AnimationFolder + "battle stance.anim" },
        { "HorizontalSlash", HumanMeleeHorizontalPath },
        { "VerticalSlash", AnimationFolder + "slash9.anim" },
        { "Kick", AnimationFolder + "slash8.anim" },
        { "Guard", AnimationFolder + "deffensive stance.anim" },
        { "Parry", HumanMeleeParryPath },
        { "DodgeCrouch", AnimationFolder + "deffensive stance.anim" },
        { "DodgeLeft", AnimationFolder + "slash7.anim" },
        { "Hit", HumanMeleeDamagePath },
        { "Stagger", HumanMeleeDamagePath },
        { "Dead", AnimationFolder + "damaged (tired) stance.anim" }
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

        AnimationClip idleClip = LoadStateClip("Idle");
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

        foreach (KeyValuePair<string, string> entry in StateClipPaths)
        {
            if (entry.Key == "Idle")
                continue;

            AnimationClip clip = LoadStateClip(entry.Key);
            AnimatorState state = actionMachine.AddState(entry.Key);
            state.motion = clip;
            state.writeDefaultValues = true;
            // The shield strike is longer than the combat parry window. Play
            // its authored shoulder/chest motion quickly while IK keeps the
            // shield hand physically attached to the guard-to-sweep target.
            if (entry.Key == "Parry" && clip != null)
                state.speed = Mathf.Max(1f, clip.length / 0.42f);
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

        EnsureHumanoidRig(FighterPath);
        foreach (string file in MixamoAnimationFiles)
            EnsureHumanoidRig(MixamoFolder + file);

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
            AssetDatabase.LoadAssetAtPath<GameObject>(SciFiModularFolder + "WallIntersection.prefab");
        library.dungeonGate =
            AssetDatabase.LoadAssetAtPath<GameObject>(SciFiModularFolder + "Door.prefab");
        library.dungeonCorridor =
            AssetDatabase.LoadAssetAtPath<GameObject>(SciFiModularFolder + "Floor1.prefab");
        library.arenaFloorLight =
            AssetDatabase.LoadAssetAtPath<GameObject>(SciFiModularFolder + "Light1.prefab");
        library.skyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        library.slashSprite = LoadParticleSprite("slash_02.png");
        library.impactSprite = LoadParticleSprite("star_03.png");
        library.guardSprite = LoadParticleSprite("circle_03.png");
        library.parrySprite = LoadParticleSprite("spark_04.png");
        library.slashVfxHorizontal =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "white-blue bolder.prefab");
        library.slashVfxVertical =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "slash-green bolder.prefab");
        // Null on purpose: BossDuelPrototype falls back to procedural
        // emissive energy blade/shield primitives when these are unset.
        library.kevinSwordPrefab = null;
        library.kevinShieldPrefab = null;
        // Dedicated finished whoosh clips (not a raw build-up layer) for a
        // punchier swing sound.
        library.swordSlice = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiWhooshAudioFolder + "TII_DSGNWhsh_SCIFIScratchWhoosh_Normal_DESIGNED_01.wav");
        library.swordSliceHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiWhooshAudioFolder + "TII_DSGNWhsh_SCIFIScratchWhoosh_Normal_DESIGNED_08.wav");
        library.swordDraw = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiWeaponAudioFolder + "Mix Samples/TII_SCIWeap_BeamSaber_Simple_MixDown_DESIGNED.wav");
        library.swordHit = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiWeaponAudioFolder + "Step3/TII_SCIWeap_LaserHit_Simple_Step3_DESIGNED_01.wav");
        library.shieldBlock = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiShieldAudioFolder + "Mix Samples/TII_SCIEnrg_EnergyShield_Normal_MixDown_DESIGNED_01.wav");
        library.shieldBlockHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiShieldAudioFolder + "Mix Samples/TII_SCIEnrg_EnergyShield_Normal_MixDown_DESIGNED_02.wav");
        library.parryBell = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiShieldAudioFolder + "Step3/TII_SCIEnrg_EnergyShieldImpact_Normal_Step3_DESIGNED_01.wav");
        library.guardBreak = AssetDatabase.LoadAssetAtPath<AudioClip>(
            SciFiShieldAudioFolder + "Step3/TII_SCIEnrg_EnergyShieldDown_Normal_Step3_DESIGNED_01.wav");
        library.bodyImpactHeavy =
            AssetDatabase.LoadAssetAtPath<AudioClip>(LaserAudioFolder + "heavy_blast_001.wav");
        library.bodyImpactMedium =
            AssetDatabase.LoadAssetAtPath<AudioClip>(LaserAudioFolder + "light_blast_1.wav");
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

    private static AnimationClip LoadStateClip(string stateName)
    {
        string primaryPath = StateClipPaths[stateName];
        AnimationClip clip = LoadAnimationClip(primaryPath, false);
        if (clip != null)
            return clip;

        if (StateClipFallbackPaths.TryGetValue(stateName, out string fallbackPath))
        {
            Debug.LogWarning(
                $"[BossDuel] Mixamo clip is missing for {stateName}; using the older " +
                $"EEJANAI/Kevin Iglesias fallback. Download mixamo.com's \"Sword And " +
                $"Shield\" pack into Assets/Mixamo/Animations/ (see pc-game/README.md) " +
                $"and rebuild the asset library to enable the upgraded motion.");
            return LoadAnimationClip(fallbackPath, true);
        }

        Debug.LogWarning($"[BossDuel] Animation clip is missing: {primaryPath}");
        return null;
    }

    private static AnimationClip LoadAnimationClip(string path, bool warnIfMissing)
    {
        AnimationClip direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (direct != null)
            return direct;

        // FBX animation clips are sub-assets. LoadAllAssetsAtPath keeps the
        // builder compatible with both standalone .anim clips and Asset Store
        // FBX clips without depending on their generated local file IDs.
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip &&
                !clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
                return clip;
        }

        if (warnIfMissing)
            Debug.LogWarning($"[BossDuel] Animation clip is missing: {path}");
        return null;
    }

    private static void EnsureFolder(string path, string folderName)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = path[..path.LastIndexOf('/')];
        AssetDatabase.CreateFolder(parent, folderName);
    }

    // Cyber Soldier ships as a rigged-but-unanimated FBX from 2016; its
    // import defaults aren't guaranteed to be Humanoid, and the whole combat
    // rig (IK hands/feet, clip retargeting from unrelated asset packs)
    // depends on that being true.
    private static void EnsureHumanoidRig(string path)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            return;

        if (importer.animationType == ModelImporterAnimationType.Human)
            return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.SaveAndReimport();
    }
}

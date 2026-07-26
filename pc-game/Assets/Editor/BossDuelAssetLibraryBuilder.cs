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
    // Mixamo's own "Knight D Pelegrini" character (Adobe, free w/ account), paired
    // natively with every "Sword And Shield" clip below — all captured on the exact
    // same Mixamo skeleton/proportions, so there is zero cross-rig retargeting error
    // (unlike the earlier pass that retargeted this pack onto the unrelated
    // CyberSoldier model, which visibly mismatched). Humanoid rig is still forced
    // below since Mixamo FBX imports default to Generic.
    private const string MixamoCharacterFolder = "Assets/Mixamo/Character/";
    private const string FighterPath =
        MixamoCharacterFolder + "Knight D Pelegrini - Sword And Shield Idle (FighterMesh+Idle).fbx";
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
    private const string MixamoFolder = "Assets/Mixamo/Animations/";
    private static readonly string[] MixamoAnimationFiles =
    {
        "Knight - Sword And Shield Slash - Cross Slash (Horizontal).fbx",
        "Knight - Sword And Shield Slash - Downward Slash (Vertical).fbx",
        "Knight - Sword And Shield Kick - Sparta Kick (Kick).fbx",
        "Knight - Sword And Shield Block Idle (Guard).fbx",
        "Knight - Sword And Shield Block - Idle To Block (Parry).fbx",
        "Knight - Sword And Shield Crouch Block Idle (DodgeCrouch).fbx",
        "Knight - Sword And Shield Strafe - Left Walk (DodgeLeft).fbx",
        "Knight - Sword And Shield Impact - Unblocked (Hit-Stagger).fbx",
        "Knight - Sword And Shield Death - Falling Back (Dead).fbx"
    };
    // Danvil "Sword and Shield" (free) - a single game-ready medieval weapon set,
    // replacing the procedural energy-weapon primitives.
    private const string SwordShieldFolder = "Assets/Danvil/Kit01SwordAndShield/Prefabs/";
    // Polytope Studio "Low Poly Environment - Nature Free" (free) - scattered around
    // the duel platform for an outdoor look, replacing the built dungeon arena.
    private const string NatureTreeFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/";
    private const string NatureRockFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/";
    private const string ParticleFolder =
        "Assets/ThirdParty/KenneyParticlePack/PNG/";
    private const string SlashVfxFolder =
        "Assets/slash5-HungNguyen/prefab/slash/";
    // Kenney CC0 audio (kenney.nl) - public domain, safe to commit and redistribute,
    // replacing a sci-fi laser/energy-shield set (TII_SoundLibrary_3Steps, Laser
    // Weapons Sound Pack) that never matched this medieval reskin thematically.
    private const string ImpactAudioFolder = "Assets/KenneyAudio/ImpactSounds/";
    private const string RpgAudioFolder = "Assets/KenneyAudio/RPGAudio/";

    private static readonly Dictionary<string, string> StateClipPaths = new()
    {
        // FighterPath's own FBX already carries the Idle clip alongside the mesh.
        { "Idle", FighterPath },
        { "HorizontalSlash", MixamoFolder + "Knight - Sword And Shield Slash - Cross Slash (Horizontal).fbx" },
        { "VerticalSlash", MixamoFolder + "Knight - Sword And Shield Slash - Downward Slash (Vertical).fbx" },
        { "Kick", MixamoFolder + "Knight - Sword And Shield Kick - Sparta Kick (Kick).fbx" },
        { "Guard", MixamoFolder + "Knight - Sword And Shield Block Idle (Guard).fbx" },
        { "Parry", MixamoFolder + "Knight - Sword And Shield Block - Idle To Block (Parry).fbx" },
        { "DodgeCrouch", MixamoFolder + "Knight - Sword And Shield Crouch Block Idle (DodgeCrouch).fbx" },
        { "DodgeLeft", MixamoFolder + "Knight - Sword And Shield Strafe - Left Walk (DodgeLeft).fbx" },
        { "Hit", MixamoFolder + "Knight - Sword And Shield Impact - Unblocked (Hit-Stagger).fbx" },
        { "Stagger", MixamoFolder + "Knight - Sword And Shield Impact - Unblocked (Hit-Stagger).fbx" },
        { "Dead", MixamoFolder + "Knight - Sword And Shield Death - Falling Back (Dead).fbx" }
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

        // Also add a base-layer (full body, unmasked) Dead state. Every other combat
        // state only needs the upper-body layer below because legs are separately
        // IK-driven (grounding/kick/dodge) while standing, but death has no such
        // per-motion leg handling - without this, dying left the legs stuck on
        // whatever the base layer's Idle pose was while only the torso collapsed.
        AnimationClip deadClip = LoadStateClip("Dead");
        AnimatorState deadState = baseMachine.AddState("Dead");
        deadState.motion = deadClip;
        deadState.writeDefaultValues = true;

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
        // Outdoor nature setting + plain default sky, per feedback that the built
        // dungeon/SF arenas were unwanted clutter - just place the duel on open ground.
        library.natureTreeA =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureTreeFolder + "PT_Fruit_Tree_01_green.prefab");
        library.natureTreeB =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureTreeFolder + "PT_Pine_Tree_03_green.prefab");
        library.natureRock =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureRockFolder + "PT_Generic_Rock_01.prefab");
        library.skyboxMaterial = null;
        library.slashSprite = LoadParticleSprite("slash_02.png");
        library.impactSprite = LoadParticleSprite("star_03.png");
        library.guardSprite = LoadParticleSprite("circle_03.png");
        library.parrySprite = LoadParticleSprite("spark_04.png");
        library.slashVfxHorizontal =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "white-blue bolder.prefab");
        library.slashVfxVertical =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "slash-green bolder.prefab");
        library.swordPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(SwordShieldFolder + "MedievalSword.prefab");
        library.shieldPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(SwordShieldFolder + "MedievalShield.prefab");
        // Kenney CC0 medieval/RPG foley + impact clips, replacing a sci-fi laser/
        // energy-shield set left over from the original SF theme pass - it never
        // matched this medieval reskin thematically.
        library.swordSlice = AssetDatabase.LoadAssetAtPath<AudioClip>(
            RpgAudioFolder + "knifeSlice.ogg");
        library.swordSliceHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            RpgAudioFolder + "knifeSlice2.ogg");
        library.swordDraw = AssetDatabase.LoadAssetAtPath<AudioClip>(
            RpgAudioFolder + "drawKnife1.ogg");
        library.swordHit = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ImpactAudioFolder + "impactMetal_light_000.ogg");
        library.shieldBlock = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ImpactAudioFolder + "impactPlate_medium_000.ogg");
        library.shieldBlockHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ImpactAudioFolder + "impactPlate_heavy_000.ogg");
        library.parryBell = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ImpactAudioFolder + "impactBell_heavy_000.ogg");
        library.guardBreak = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ImpactAudioFolder + "impactMetal_heavy_002.ogg");
        library.bodyImpactHeavy =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPunch_heavy_000.ogg");
        library.bodyImpactMedium =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPunch_medium_000.ogg");
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

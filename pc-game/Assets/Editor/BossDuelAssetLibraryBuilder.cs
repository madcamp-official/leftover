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
    private const string NaturePlantFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Plants/";
    private const string NatureShrubFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Shrubs/";
    private const string NatureFlowerFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Flowers/";
    private const string ParticleFolder =
        "Assets/ThirdParty/KenneyParticlePack/PNG/";
    private const string SlashVfxFolder =
        "Assets/slash5-HungNguyen/prefab/slash/";
    // Travis Game Assets "Hit Impact Effects FREE" (Asset Store, free) - richer
    // particle-based impact bursts (shockwave/light rays/smoke) layered on top of
    // the flat Kenney sprite hits for a punchier, less prototype-y look.
    private const string HitImpactFolder =
        "Assets/Travis Game Assets/Hit Impact Effects/Prefabs/";
    // Kenney CC0 audio (kenney.nl) - public domain, safe to commit and redistribute,
    // replacing a sci-fi laser/energy-shield set (TII_SoundLibrary_3Steps, Laser
    // Weapons Sound Pack) that never matched this medieval reskin thematically.
    private const string ImpactAudioFolder = "Assets/KenneyAudio/ImpactSounds/";
    private const string RpgAudioFolder = "Assets/KenneyAudio/RPGAudio/";
    // freesound.org CC0 clips (public domain), each individually picked for its exact
    // combat role - a real sword unsheathe, a "Metal_Sword_Parry_Impact_Hit" hit
    // specifically tagged for parries, a "shield guard" block, etc - instead of the
    // Kenney set's more generic RPG/impact foley. The round that tried the Unity
    // Asset Store's "Middle Age - Medieval Action Sound FX Pack" (mariobastos) could
    // not get past a stuck in-app browser to actually license/import it, so this is a
    // CC0 fallback per that round's own instructions. See ATTRIBUTION.txt in this
    // folder for the author/source/license link of every clip (all CC0, attribution
    // not required but kept for traceability). Downloaded via each sound's public
    // "-hq" preview stream (128kbps mp3, no login required) rather than the original
    // uploaded master, since fetching the original does require a Freesound account
    // login this session did not have.
    private const string FreesoundMedievalFolder = "Assets/FreesoundAudio/Medieval/";
    // Unity Asset Store "Middle Age - Medieval Action Sound FX Pack" (mariobastos,
    // free) - the round-3 attempt at this exact pack got stuck in the in-app
    // browser and fell back to the Freesound clips above; this round it imported
    // cleanly via Package Manager > My Assets. It is a general battle-atmosphere
    // pack (crowd, arrows, horses, animals, 34 clips total) with no clips
    // specifically tagged for shield-block/parry/body-impact, but its "Sword
    // Swish" pair and "Sword 1-7" set are a direct upgrade over the generic
    // knifeSlice/impactMetal fallbacks for the two roles they actually cover
    // (swing whoosh, blade-on-blade clash) - see the swordSlice/swordSliceHeavy/
    // swordHit wiring below. Everything else (draw, shield block, parry bell,
    // guard break, body impact) has no clear match in this pack and stays on the
    // curated Freesound CC0 clips.
    private const string MedievalActionFxFolder = "Assets/Medieval Action - FX Pack 2.0/";

    private static readonly Dictionary<string, string> StateClipPaths = new()
    {
        // FighterPath's own bundled "Idle" clip turned out to have real foot
        // travel baked in (measured ~18cm of foot drift in well under a second
        // once it was played live instead of frozen on frame 0) - it's more a
        // "settle into stance" transition than a stationary loop, which is
        // exactly why the old frozen-frame hack always looked like a paused
        // mid-step rather than a standing pose. The Block Idle (Guard) clip is
        // a real in-place ready stance - measured foot drift of ~5mm/second
        // (noise-level) with the same subtle live weight-shift/breathing sway -
        // and reads as a proper sword-and-shield "on guard" stance at rest,
        // which is exactly the Tekken-style ready-stance look asked for. Reused
        // as-is rather than sourcing a separate clip since it already covers
        // both roles well.
        { "Idle", MixamoFolder + "Knight - Sword And Shield Block Idle (Guard).fbx" },
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
        // Idle now plays live instead of freezing on frame 0 (see PlayAssetAnimation),
        // but Mixamo FBX imports default clips to non-looping - without this it would
        // just play once and hold on the LAST frame, swapping one frozen pose for
        // another instead of actually looping the ready-stance sway.
        EnsureLoopingClip(StateClipPaths["Idle"]);

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
        // A second rock silhouette plus grass/shrub/flower detail from the same pack,
        // scattered in the mid-ground per feedback that the arena read as too bare -
        // same art style as the trees/rock above, so nothing clashes.
        library.natureRockB =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureRockFolder + "PT_River_Rock_Pile_02.prefab");
        library.natureGrass =
            AssetDatabase.LoadAssetAtPath<GameObject>(NaturePlantFolder + "PT_Grass_02.prefab");
        library.natureShrub =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureShrubFolder + "PT_Generic_Shrub_01_green.prefab");
        library.natureFlower =
            AssetDatabase.LoadAssetAtPath<GameObject>(NatureFlowerFolder + "PT_Poppy_02.prefab");
        library.skyboxMaterial = null;
        library.slashSprite = LoadParticleSprite("slash_02.png");
        library.impactSprite = LoadParticleSprite("star_03.png");
        library.guardSprite = LoadParticleSprite("circle_03.png");
        library.parrySprite = LoadParticleSprite("spark_04.png");
        library.slashVfxHorizontal =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "white-blue bolder.prefab");
        library.slashVfxVertical =
            AssetDatabase.LoadAssetAtPath<GameObject>(SlashVfxFolder + "slash-green bolder.prefab");
        // Broad circular shockwave for the wide horizontal cut, a sharper directional
        // light-ray burst for the downward vertical cut, a smoky punch-flash for the
        // kick, and the pack's own guard-trail glow for blocks.
        library.hitImpactHorizontal =
            AssetDatabase.LoadAssetAtPath<GameObject>(HitImpactFolder + "Hits/Hit_03.prefab");
        library.hitImpactVertical =
            AssetDatabase.LoadAssetAtPath<GameObject>(HitImpactFolder + "Hits/Hit_04.prefab");
        library.hitImpactKick =
            AssetDatabase.LoadAssetAtPath<GameObject>(HitImpactFolder + "Hits/Hit_01.prefab");
        library.guardImpactVfx =
            AssetDatabase.LoadAssetAtPath<GameObject>(HitImpactFolder + "Guards/Guard_01.prefab");
        library.swordPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(SwordShieldFolder + "MedievalSword.prefab");
        library.shieldPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(SwordShieldFolder + "MedievalShield.prefab");
        // freesound.org CC0 clips, each picked for its specific combat role (see the
        // FreesoundMedievalFolder comment above and ATTRIBUTION.txt for sources).
        // swordHit, shieldBlock/Heavy, bodyImpactHeavy/Medium previously used Kenney's
        // generic impact set and still fall back to it here if a freesound file is
        // ever missing (e.g. a teammate's checkout is missing the new folder).
        // Sword Swish 1/2 have a slower, ramping attack (peak ~12-13% into the
        // clip - a real whoosh building up) versus the Sword 1-7 set's near-
        // instant attack (peak in the first 0-2%), measured via an RMS envelope
        // pass over each clip - i.e. these two are actual swing sounds, not
        // clashes, unlike the flatter single-shot knifeSlice fallback.
        library.swordSlice = AssetDatabase.LoadAssetAtPath<AudioClip>(
            MedievalActionFxFolder + "Sword Swish 1.wav") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(
                FreesoundMedievalFolder + "507466_swordSlice_swoosh.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "knifeSlice.ogg");
        library.swordSliceHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            MedievalActionFxFolder + "Sword Swish 2.wav") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(
                FreesoundMedievalFolder + "733891_swordSliceHeavy_whooshTriple.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "knifeSlice2.ogg");
        library.swordDraw = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "107589_swordDraw_unsheathe.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(RpgAudioFolder + "drawKnife1.ogg");
        // "Sword 2.wav" - one of a near-identical trio (Sword 1/2/3, peak RMS
        // within 1% of each other) with the fastest, cleanest single-transient
        // attack in the pack, i.e. a real blade-on-blade clash rather than the
        // Kenney generic metal-impact fallback.
        library.swordHit = AssetDatabase.LoadAssetAtPath<AudioClip>(
            MedievalActionFxFolder + "Sword 2.wav") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(
                FreesoundMedievalFolder + "334169_swordHit_clash.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactMetal_light_000.ogg");
        library.shieldBlock = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "370203_shieldBlock_guard.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPlate_medium_000.ogg");
        library.shieldBlockHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "636103_shieldBlockHeavy_hit1.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPlate_heavy_000.ogg");
        library.parryBell = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "760636_parryBell_metalParry.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactBell_heavy_000.ogg");
        library.guardBreak = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "653750_guardBreak_titanMetal.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactMetal_heavy_002.ogg");
        library.bodyImpactHeavy = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "517744_bodyImpactHeavy_punch.mp3") ??
            AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioFolder + "impactPunch_heavy_000.ogg");
        library.bodyImpactMedium = AssetDatabase.LoadAssetAtPath<AudioClip>(
            FreesoundMedievalFolder + "276600_bodyImpactMedium_bodyHit.mp3") ??
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

    private static void EnsureLoopingClip(string path)
    {
        if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
            return;

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (!clips[i].loopTime)
            {
                clips[i].loopTime = true;
                changed = true;
            }
        }
        if (!changed)
            return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }
}

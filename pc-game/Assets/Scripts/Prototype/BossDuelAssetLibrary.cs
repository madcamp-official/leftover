using UnityEngine;

[CreateAssetMenu(menuName = "Boss Duel/Asset Library")]
public sealed class BossDuelAssetLibrary : ScriptableObject
{
    public GameObject fighterPrefab;
    public RuntimeAnimatorController combatController;
    // Sci-fi modular corridor pieces (formerly Kenney dungeon set).
    public GameObject dungeonRoom;
    public GameObject dungeonGate;
    public GameObject dungeonCorridor;
    public GameObject arenaFloorLight;
    public Material skyboxMaterial;
    public Sprite slashSprite;
    public Sprite impactSprite;
    public Sprite guardSprite;
    public Sprite parrySprite;
    public GameObject slashVfxHorizontal;
    public GameObject slashVfxVertical;
    // Left null on purpose so sword/shield fall back to the procedural
    // emissive energy-weapon primitives built in BossDuelPrototype.
    public GameObject kevinSwordPrefab;
    public GameObject kevinShieldPrefab;
    public AudioClip swordSlice;
    public AudioClip swordSliceHeavy;
    public AudioClip swordDraw;
    public AudioClip swordHit;
    public AudioClip shieldBlock;
    public AudioClip shieldBlockHeavy;
    public AudioClip parryBell;
    public AudioClip guardBreak;
    public AudioClip bodyImpactHeavy;
    public AudioClip bodyImpactMedium;
}

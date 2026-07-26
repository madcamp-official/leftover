using UnityEngine;

[CreateAssetMenu(menuName = "Boss Duel/Asset Library")]
public sealed class BossDuelAssetLibrary : ScriptableObject
{
    public GameObject fighterPrefab;
    public RuntimeAnimatorController combatController;
    // Low Poly Environment - Nature Free (Polytope Studio, free) - scattered around
    // the duel platform for an outdoor look instead of a built arena.
    public GameObject natureTreeA;
    public GameObject natureTreeB;
    public GameObject natureRock;
    public Material skyboxMaterial;
    public Sprite slashSprite;
    public Sprite impactSprite;
    public Sprite guardSprite;
    public Sprite parrySprite;
    public GameObject slashVfxHorizontal;
    public GameObject slashVfxVertical;
    // Game-ready medieval sword/shield meshes (Danvil "Sword and Shield", free). Null
    // falls back to the procedural emissive energy-weapon primitives in BossDuelPrototype.
    public GameObject swordPrefab;
    public GameObject shieldPrefab;
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

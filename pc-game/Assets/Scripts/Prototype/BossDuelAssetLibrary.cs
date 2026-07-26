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
    // Additional set-dressing from the same pack (same art style, so no visual
    // clash) - fills in the bare mid-ground between the tree ring and the fight
    // itself, which read as too sparse/empty per a prior QA pass.
    public GameObject natureRockB;
    public GameObject natureGrass;
    public GameObject natureShrub;
    public GameObject natureFlower;
    public Material skyboxMaterial;
    public Sprite slashSprite;
    public Sprite impactSprite;
    public Sprite guardSprite;
    public Sprite parrySprite;
    public GameObject slashVfxHorizontal;
    public GameObject slashVfxVertical;
    // Travis Game Assets "Hit Impact Effects FREE" (Asset Store, free) - layered on
    // top of the flat sprite bursts above for a punchier, more AAA-looking impact:
    // real particle shockwaves/light rays/smoke instead of a single flat quad. Tinted
    // at runtime to match the existing amber (horizontal) / violet (vertical) coding.
    public GameObject hitImpactHorizontal;
    public GameObject hitImpactVertical;
    public GameObject hitImpactKick;
    public GameObject guardImpactVfx;
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

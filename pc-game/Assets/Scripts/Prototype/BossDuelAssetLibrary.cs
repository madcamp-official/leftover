using UnityEngine;

[CreateAssetMenu(menuName = "Boss Duel/Asset Library")]
public sealed class BossDuelAssetLibrary : ScriptableObject
{
    public GameObject fighterPrefab;
    public RuntimeAnimatorController combatController;
    public GameObject dungeonRoom;
    public GameObject dungeonGate;
    public GameObject dungeonCorridor;
    public Sprite slashSprite;
    public Sprite impactSprite;
    public Sprite guardSprite;
    public Sprite parrySprite;
    public GameObject kevinSwordPrefab;
    public GameObject kevinShieldPrefab;
}

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A self-contained vertical slice for the boss-duel project.
/// It builds a small arena and two stylised fighters at runtime, then connects
/// the existing CombatInputHub to a readable combat loop.
/// </summary>
public sealed class BossDuelPrototype : MonoBehaviour
{
    private enum Motion
    {
        Idle,
        PrepareHorizontal,
        PrepareVertical,
        PrepareKick,
        HorizontalSlash,
        VerticalSlash,
        Kick,
        Guard,
        Parry,
        DodgeCrouch,
        DodgeLeft,
        Hit,
        Stagger,
        Dead
    }

    private enum EnemyPhase
    {
        Waiting,
        TelegraphHorizontal,
        TelegraphVertical,
        TelegraphKick,
        AttackingHorizontal,
        AttackingVertical,
        AttackingKick,
        Guarding,
        Parrying,
        DodgingCrouch,
        DodgingLeft,
        Staggered,
        Dead
    }

    private sealed class Fighter
    {
        public Transform root;
        public Transform body;
        public Transform head;
        public Transform swordPivot;
        public Transform shieldPivot;
        public Renderer bodyRenderer;
        public Renderer swordRenderer;
        public Renderer shieldRenderer;
        public Material baseMaterial;
        public Material flashMaterial;
        public Material guardMaterial;
        public float hp = 100f;
        public float defenseGauge = 3f;
        public Motion motion;
        public float motionStarted;
        public float motionDuration = 0.7f;
        public bool hitResolved;
        public bool facesRight;
        public Vector3 basePosition;
        public Quaternion baseRotation;
        public Vector3 baseScale = Vector3.one;
        public bool usesAssetModel;
        public Animator animator;
        public Renderer[] renderers;
        public Color[] rendererBaseColors;
        public GroundedFighterRig groundedRig;
        public AssetShieldFollower shieldFollower;
        public AssetSwordFollower swordFollower;
        public TrailRenderer swordTrail;
        public Transform kickTarget;
        public float dodgeDirection = -1f;
    }

    private enum EffectKind
    {
        HorizontalHit,
        VerticalHit,
        KickHit,
        GuardHorizontal,
        GuardVertical,
        GuardBreak,
        ParryHorizontal,
        ParryVertical,
        ParryKick,
        DodgeCrouch,
        DodgeSide,
        SwordTrade,
        KickTrade
    }

    private const float MaxHealth = 100f;
    private const float PlayerAttackDamage = 18f;
    private const float EnemyAttackDamage = 22f;
    private const float MaxDefenseGauge = 3f;
    private const float GuardGaugeCost = 1f;
    private const float ParryGaugeCost = 0.5f;
    private const float GaugeRecoveryPerSecond = 0.38f;
    private const float ParryWindow = 0.42f;
    // Long enough to read which attack is coming (see AssetSwordFollower's
    // explicit prepare pose and the blade telegraph tint in AnimateAssetFighter),
    // short enough to still demand a fast reaction.
    private const float AttackWindup = 0.36f;

    private Fighter _player;
    private Fighter _enemy;
    private CombatInputHub _input;
    private EnemyPhase _enemyPhase;
    private float _enemyPhaseEnds;
    private float _playerParryEnds;
    private float _playerDodgeEnds;
    private float _playerStaggerEnds;
    private Motion _queuedPlayerAttack = Motion.Idle;
    private float _queuedPlayerAttackAt;
    private bool _crouchWasPressed;
    private LateralPosition _previousLateralPosition;
    private float _roundEndedAt;
    private string _banner = "READY";
    private string _detail = "Read the enemy telegraph and choose your response.";
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _smallStyle;
    private Material _playerMaterial;
    private Material _enemyMaterial;
    private Material _stoneDark;
    private Material _stoneLight;
    private Material _goldMaterial;
    private Material _dangerMaterial;
    private Material _parryMaterial;
    private Material _dodgeMaterial;
    private Material _bladeCoreMaterial;
    private AudioSource _audioSource;
    private AudioSource _audioLayerA;
    private AudioSource _audioLayerB;
    private AudioClip _slashSound;
    private AudioClip _guardSound;
    private AudioClip _parrySound;
    private AudioClip _dodgeSound;
    private AudioClip _hitSound;
    private BossDuelAssetLibrary _assetLibrary;
    private int _playerHorizontalUses;
    private int _playerVerticalUses;
    private int _playerKickUses;
    private int _playerCrouchUses;
    private int _playerLeftDodgeUses;
    private int _playerParryUses;
    private Motion _lastPlayerAttack = Motion.Idle;
    private int _repeatedPlayerAttack;
    private Motion _lastEnemyAttack = Motion.Idle;
    private int _enemyComboPressure;
    private Camera _mainCamera;
    private Vector3 _cameraRestPosition;
    private float _cameraShakeEnds;
    private float _cameraShakeStrength;
    private float _screenFlashEnds;
    private Color _screenFlashColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene")
            return;

        if (FindAnyObjectByType<BossDuelPrototype>() != null)
            return;

        var host = new GameObject("Boss Duel Prototype");
        host.AddComponent<BossDuelPrototype>();
    }

    private void Start()
    {
        _assetLibrary = Resources.Load<BossDuelAssetLibrary>("BossDuel/BossDuelAssetLibrary");
        CreateMaterials();
        CreateAudio();
        ConfigureScene();
        CreateArena();
        _player = CreateFighter("PLAYER", new Vector3(-2.75f, 0.25f, 0f), true, _playerMaterial);
        _enemy = CreateFighter("RIVAL", new Vector3(2.75f, 0.25f, 0f), false, _enemyMaterial);
        ConnectInput();
        ResetRound();
    }

    private void OnDestroy()
    {
        if (_input == null)
            return;

        _input.OnSwingHorizontal -= PlayerHorizontalSlash;
        _input.OnSwingVertical -= PlayerVerticalSlash;
        _input.OnKick -= PlayerKick;
        _input.OnParry -= PlayerParry;
    }

    private void Update()
    {
        if (_player == null || _enemy == null)
            return;

        if (RestartPressed())
            ResetRound();

        UpdateDodgeInput();
        RecoverDefenseGauges();
        UpdatePlayerState();
        UpdateEnemyAI();
        AnimateFighter(_player);
        AnimateFighter(_enemy);
        UpdateCameraFeedback();
    }

    private static bool RestartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }

    private void ConnectInput()
    {
        _input = CombatInputHub.Instance;
        if (_input == null)
        {
            var inputObject = new GameObject("CombatInput");
            _input = inputObject.AddComponent<CombatInputHub>();
        }

        // 키보드는 카메라 없이도 바로 테스트할 수 있는 폴백으로 항상 붙여둔다.
        // NetworkInputProvider(vision-server/MediaPipe, UDP 9002)도 함께 붙여서
        // Play를 누르는 순간부터 웹캠 모션 인식으로도 플레이할 수 있게 한다. 두
        // 프로바이더는 KeyboardInputProvider가 자기 자신의 마지막 전송값만 기준으로
        // 변화가 있을 때만 Hub를 건드리도록 되어 있어 함께 켜둬도 서로 덮어쓰지 않는다.
        if (FindAnyObjectByType<KeyboardInputProvider>() == null)
            _input.gameObject.AddComponent<KeyboardInputProvider>();

        if (FindAnyObjectByType<NetworkInputProvider>() == null)
            _input.gameObject.AddComponent<NetworkInputProvider>();

        _input.OnSwingHorizontal += PlayerHorizontalSlash;
        _input.OnSwingVertical += PlayerVerticalSlash;
        _input.OnKick += PlayerKick;
        _input.OnParry += PlayerParry;
    }

    private void ResetRound()
    {
        _player.hp = MaxHealth;
        _enemy.hp = MaxHealth;
        _player.defenseGauge = MaxDefenseGauge;
        _enemy.defenseGauge = MaxDefenseGauge;
        _player.motion = Motion.Idle;
        _enemy.motion = Motion.Idle;
        _player.root.localScale = _player.baseScale;
        _enemy.root.localScale = _enemy.baseScale;
        _player.root.position = new Vector3(-2.75f, 0.25f, 0f);
        _enemy.root.position = new Vector3(2.75f, 0.25f, 0f);
        _player.basePosition = _player.root.position;
        _enemy.basePosition = _enemy.root.position;
        RestoreFighterAppearance(_player);
        RestoreFighterAppearance(_enemy);
        if (_player.swordRenderer != null) _player.swordRenderer.enabled = true;
        if (_enemy.swordRenderer != null) _enemy.swordRenderer.enabled = true;
        if (_player.shieldRenderer != null) _player.shieldRenderer.enabled = true;
        if (_enemy.shieldRenderer != null) _enemy.shieldRenderer.enabled = true;
        PlayAssetAnimation(_player, Motion.Idle);
        PlayAssetAnimation(_enemy, Motion.Idle);
        _enemyPhase = EnemyPhase.Waiting;
        _enemyPhaseEnds = Time.time + 1.6f;
        _playerParryEnds = 0f;
        _playerDodgeEnds = 0f;
        _playerStaggerEnds = 0f;
        _queuedPlayerAttack = Motion.Idle;
        _queuedPlayerAttackAt = 0f;
        _crouchWasPressed = false;
        _previousLateralPosition = LateralPosition.Center;
        _roundEndedAt = 0f;
        _banner = "DUEL START";
        _detail = "J/K: slash  L: kick  Space: guard  F: parry  S/A: dodge";
    }

    private void UpdatePlayerState()
    {
        if (_player.motion == Motion.Dead)
            return;

        if (_queuedPlayerAttack != Motion.Idle && Time.time >= _queuedPlayerAttackAt)
        {
            Motion attack = _queuedPlayerAttack;
            _queuedPlayerAttack = Motion.Idle;
            StartPlayerAttack(attack);
        }

        if (Time.time < _playerStaggerEnds)
        {
            SetMotion(_player, Motion.Stagger, _playerStaggerEnds - Time.time);
            return;
        }

        if (IsDodgeMotion(_player.motion) && Time.time < _playerDodgeEnds)
            return;

        if (_input != null && _input.IsGuarding && Time.time >= _playerParryEnds &&
            !IsAttackMotion(_player.motion) && !IsPrepareMotion(_player.motion) &&
            !IsDodgeMotion(_player.motion))
        {
            if (_player.motion != Motion.Guard)
            {
                if (_player.defenseGauge < GuardGaugeCost)
                {
                    _banner = "NO GUARD GAUGE";
                    _detail = "Wait for the defense gauge to recover.";
                    return;
                }
                _player.defenseGauge -= GuardGaugeCost;
                SetMotion(_player, Motion.Guard, 0.12f);
            }
            return;
        }

        if (_player.motion == Motion.Guard && (_input == null || !_input.IsGuarding))
            SetMotion(_player, Motion.Idle, 0.2f);

        ResolveAttackIfNeeded(_player, true);

        if (MotionFinished(_player) &&
            _player.motion != Motion.Dead &&
            Time.time >= _playerStaggerEnds &&
            Time.time >= _playerParryEnds)
        {
            SetMotion(_player, Motion.Idle, 0.2f);
        }
    }

    private void UpdateDodgeInput()
    {
        if (_input == null)
            return;

        bool crouching = _input.IsCrouching;
        LateralPosition lateral = _input.LateralPosition;

        if (crouching && !_crouchWasPressed)
            BeginPlayerDodge(Motion.DodgeCrouch);
        if (lateral != LateralPosition.Center &&
            lateral != _previousLateralPosition)
        {
            _player.dodgeDirection = lateral == LateralPosition.Left ? -1f : 1f;
            BeginPlayerDodge(Motion.DodgeLeft);
        }

        _crouchWasPressed = crouching;
        _previousLateralPosition = lateral;
    }

    private void PlayerHorizontalSlash()
    {
        BeginPlayerAttack(Motion.HorizontalSlash);
    }

    private void PlayerVerticalSlash()
    {
        BeginPlayerAttack(Motion.VerticalSlash);
    }

    private void PlayerKick()
    {
        BeginPlayerAttack(Motion.Kick);
    }

    private void BeginPlayerAttack(Motion attack)
    {
        if (!CanPlayerAct())
            return;

        if (attack == Motion.HorizontalSlash) _playerHorizontalUses++;
        else if (attack == Motion.VerticalSlash) _playerVerticalUses++;
        else if (attack == Motion.Kick) _playerKickUses++;

        _repeatedPlayerAttack = attack == _lastPlayerAttack ? _repeatedPlayerAttack + 1 : 1;
        _lastPlayerAttack = attack;

        _queuedPlayerAttack = attack;
        _queuedPlayerAttackAt = Time.time + AttackWindup;
        SetMotion(_player, PrepareFor(attack), AttackWindup);
        _banner = attack == Motion.HorizontalSlash
            ? "WIND-UP: HORIZONTAL"
            : attack == Motion.VerticalSlash ? "WIND-UP: VERTICAL" : "WIND-UP: KICK";
        _detail = "0.2 second readable preparation.";

        float familiarity = GetAttackFamiliarity(attack);
        float pressureBonus = (MaxHealth - _enemy.hp) / MaxHealth * 0.13f;
        float repeatBonus = Mathf.Clamp01((_repeatedPlayerAttack - 1) * 0.12f);
        float parryChance = 0.10f + familiarity * 0.24f + pressureBonus + repeatBonus;
        float guardChance = 0.26f + familiarity * 0.16f + pressureBonus;
        float dodgeChance = 0.24f + familiarity * 0.08f;
        float reaction = UnityEngine.Random.value;
        if (_enemyPhase == EnemyPhase.Waiting && reaction < parryChance &&
            _enemy.defenseGauge >= ParryGaugeCost)
        {
            _enemy.defenseGauge -= ParryGaugeCost;
            _enemyPhase = EnemyPhase.Parrying;
            _enemyPhaseEnds = Time.time + AttackWindup + 0.48f;
            SetMotion(_enemy, Motion.Parry, AttackWindup + 0.48f);
            _detail = _repeatedPlayerAttack > 1
                ? "The rival recognized your repeated attack."
                : "The rival read your attack pattern.";
        }
        else if (_enemyPhase == EnemyPhase.Waiting &&
                 reaction < parryChance + guardChance &&
                 _enemy.defenseGauge >= GuardGaugeCost)
        {
            _enemy.defenseGauge -= GuardGaugeCost;
            _enemyPhase = EnemyPhase.Guarding;
            _enemyPhaseEnds = Time.time + AttackWindup + 0.85f;
            SetMotion(_enemy, Motion.Guard, AttackWindup + 0.85f);
        }
        else if (_enemyPhase == EnemyPhase.Waiting &&
                 reaction < parryChance + guardChance + dodgeChance)
        {
            Motion dodge = attack == Motion.HorizontalSlash
                ? Motion.DodgeCrouch : Motion.DodgeLeft;
            _enemyPhase = dodge == Motion.DodgeCrouch
                ? EnemyPhase.DodgingCrouch : EnemyPhase.DodgingLeft;
            _enemyPhaseEnds = Time.time + AttackWindup + 0.58f;
            SetMotion(_enemy, dodge, AttackWindup + 0.58f);
        }
    }

    private void StartPlayerAttack(Motion attack)
    {
        float duration = AttackDuration(attack);
        SetMotion(_player, attack, duration);
        _banner = attack == Motion.HorizontalSlash
            ? "HORIZONTAL SLASH"
            : attack == Motion.VerticalSlash ? "VERTICAL SLASH" : "KICK";
        _detail = "Strike!";
        PlayAttackSound(attack);
    }

    private static Motion PrepareFor(Motion attack)
    {
        return attack == Motion.HorizontalSlash ? Motion.PrepareHorizontal
            : attack == Motion.VerticalSlash ? Motion.PrepareVertical
            : Motion.PrepareKick;
    }

    private static float AttackDuration(Motion attack)
    {
        return attack == Motion.HorizontalSlash ? 0.62f
            : attack == Motion.VerticalSlash ? 0.84f
            : 0.58f;
    }

    private void BeginPlayerDodge(Motion dodge)
    {
        if (!CanPlayerAct())
            return;

        if (dodge == Motion.DodgeCrouch) _playerCrouchUses++;
        else _playerLeftDodgeUses++;

        _playerDodgeEnds = Time.time + 0.58f;
        SetMotion(_player, dodge, 0.58f);
        _banner = dodge == Motion.DodgeCrouch ? "CROUCH DODGE" : "SIDE DODGE";
        _detail = dodge == Motion.DodgeCrouch
            ? "Avoid horizontal attacks during the low stance."
            : "Sidestep vertical slashes and kicks.";
        PlaySound(_dodgeSound, 0.72f);
    }

    private void PlayerParry()
    {
        if (!CanPlayerAct() || _player.defenseGauge < ParryGaugeCost)
        {
            _banner = "NO PARRY GAUGE";
            _detail = "Parry requires 0.5 defense gauge.";
            return;
        }

        _player.defenseGauge -= ParryGaugeCost;
        _playerParryUses++;
        _playerParryEnds = Time.time + ParryWindow;
        SetMotion(_player, Motion.Parry, ParryWindow);
        _banner = "PARRY";
        _detail = "A short timing window is active.";
    }

    private float GetAttackFamiliarity(Motion attack)
    {
        int total = Mathf.Max(1, _playerHorizontalUses + _playerVerticalUses + _playerKickUses);
        int uses = attack == Motion.HorizontalSlash
            ? _playerHorizontalUses
            : attack == Motion.VerticalSlash ? _playerVerticalUses : _playerKickUses;
        return uses / (float)total;
    }

    private void RecoverDefenseGauges()
    {
        if (_player != null && _player.motion != Motion.Guard && _player.motion != Motion.Parry)
            _player.defenseGauge = Mathf.Min(MaxDefenseGauge,
                _player.defenseGauge + GaugeRecoveryPerSecond * Time.deltaTime);
        if (_enemy != null && _enemy.motion != Motion.Guard && _enemy.motion != Motion.Parry)
            _enemy.defenseGauge = Mathf.Min(MaxDefenseGauge,
                _enemy.defenseGauge + GaugeRecoveryPerSecond * Time.deltaTime);
    }

    private bool CanPlayerAct()
    {
        return _player != null &&
               _player.motion != Motion.Dead &&
               _enemy.motion != Motion.Dead &&
               Time.time >= _playerStaggerEnds &&
               !IsAttackMotion(_player.motion) &&
               !IsPrepareMotion(_player.motion) &&
               !IsDodgeMotion(_player.motion) &&
               _player.motion != Motion.Hit;
    }

    private void ResolveAttackIfNeeded(Fighter attacker, bool playerAttacking)
    {
        if (!IsAttackMotion(attacker.motion) || attacker.hitResolved)
            return;

        float progress = MotionProgress(attacker);
        if (progress < (attacker.motion == Motion.Kick ? 0.44f : 0.48f))
            return;

        attacker.hitResolved = true;
        Fighter defender = playerAttacking ? _enemy : _player;
        if (attacker.motion == Motion.HorizontalSlash || attacker.motion == Motion.VerticalSlash)
            SpawnDirectionalSlash(attacker, attacker.motion == Motion.HorizontalSlash);

        bool parried = playerAttacking
            ? _enemyPhase == EnemyPhase.Parrying
            : Time.time <= _playerParryEnds;
        bool guarded = playerAttacking
            ? _enemyPhase == EnemyPhase.Guarding
            : defender.motion == Motion.Guard;

        if (parried)
        {
            StaggerAttacker(attacker, playerAttacking, 1.2f);
            _banner = playerAttacking ? "RIVAL PARRIED" : "PERFECT PARRY";
            _detail = "Parry beats every attack. The attacker is staggered.";
            SpawnActionEffect(Vector3.Lerp(attacker.root.position, defender.root.position, 0.5f) +
                Vector3.up * 1.35f, ParryEffectFor(attacker.motion), 1.45f);
            PlayParrySound(attacker.motion);
            return;
        }

        if (guarded)
        {
            if (attacker.motion == Motion.Kick)
            {
                float chip = AttackDamage(attacker) * 0.25f;
                Damage(defender, chip, "GUARD BROKEN",
                    "Kick breaks basic guard: 0.25 damage and stagger.");
                SetMotion(defender, Motion.Stagger, 0.75f);
                if (!playerAttacking)
                    _playerStaggerEnds = Time.time + 0.75f;
            }
            else
            {
                _banner = "BLOCKED";
                _detail = "Basic guard stops horizontal and vertical slashes.";
            }
            SpawnActionEffect(defender.root.position + Vector3.up * 1.35f,
                attacker.motion == Motion.Kick
                    ? EffectKind.GuardBreak
                    : GuardEffectFor(attacker.motion), 1.1f);
            if (attacker.motion == Motion.Kick)
                PlayGuardBreakSound();
            else
                PlayGuardSound(attacker.motion);
            return;
        }

        if (IsDodgeMotion(defender.motion))
        {
            bool correctDodge = IsCorrectDodge(defender.motion, attacker.motion);
            if (correctDodge)
            {
                bool staggerSlashAttacker = attacker.motion != Motion.Kick;
                if (staggerSlashAttacker)
                    StaggerAttacker(attacker, playerAttacking, 0.9f);
                _banner = defender.motion == Motion.DodgeCrouch ? "CROUCH EVADE" : "SIDE EVADE";
                _detail = staggerSlashAttacker
                    ? "Correct dodge. The slash attacker is staggered."
                    : "Side movement avoids the kick.";
                SpawnActionEffect(defender.root.position + Vector3.up,
                    defender.motion == Motion.DodgeCrouch
                        ? EffectKind.DodgeCrouch
                        : EffectKind.DodgeSide, 1.15f);
                PlaySound(_dodgeSound, 1.05f);
                return;
            }

            float failedDodgeDamage = attacker.motion == Motion.Kick
                ? AttackDamage(attacker) * 0.5f : AttackDamage(attacker);
            Damage(defender, failedDodgeDamage, "DODGE FAILED",
                attacker.motion == Motion.Kick
                    ? "Crouching loses to kick: 0.5 damage."
                    : "Wrong dodge direction: 1.0 damage.");
            SpawnActionEffect(defender.root.position + Vector3.up * 1.2f,
                HitEffectFor(attacker.motion), 1.15f);
            PlayHitSound(attacker.motion);
            return;
        }

        if (IsAttackMotion(defender.motion) && !defender.hitResolved)
        {
            defender.hitResolved = true;
            bool attackerKick = attacker.motion == Motion.Kick;
            bool defenderKick = defender.motion == Motion.Kick;
            if (attackerKick && !defenderKick)
            {
                Damage(attacker, AttackDamage(defender), "KICK INTERRUPTED",
                    "A sword attack beats kick: 1.0 damage.");
            }
            else if (!attackerKick && defenderKick)
            {
                Damage(defender, AttackDamage(attacker), "SLASH BEATS KICK",
                    "The slash lands; the kick fails.");
            }
            else
            {
                float attackerTaken = AttackDamage(defender) * (attackerKick ? 0.5f : 1f);
                float defenderTaken = AttackDamage(attacker) * (attackerKick ? 0.5f : 1f);
                Damage(attacker, attackerTaken, "TRADE", "Both attacks connect.");
                Damage(defender, defenderTaken, "TRADE", "Both attacks connect.");
                _banner = attackerKick ? "KICK TRADE x0.5" : "SWORD TRADE x1.0";
            }
            SpawnActionEffect(Vector3.Lerp(attacker.root.position, defender.root.position, 0.5f) +
                Vector3.up * 1.25f,
                attackerKick || defenderKick ? EffectKind.KickTrade : EffectKind.SwordTrade,
                1.35f);
            PlayTradeSound(attackerKick || defenderKick);
            return;
        }

        float damage = attacker.motion == Motion.Kick
            ? AttackDamage(attacker) * 0.5f : AttackDamage(attacker);
        Damage(defender, damage,
            attacker.motion == Motion.Kick ? "KICK HIT x0.5"
                : attacker.motion == Motion.HorizontalSlash ? "HORIZONTAL HIT"
                : "VERTICAL HIT",
            "Clean attack according to the action matrix.");
        SpawnActionEffect(defender.root.position + Vector3.up * 1.3f,
            HitEffectFor(attacker.motion), 1.2f);
        PlayHitSound(attacker.motion);
    }

    private static bool IsCorrectDodge(Motion dodge, Motion attack)
    {
        return (dodge == Motion.DodgeCrouch && attack == Motion.HorizontalSlash) ||
               (dodge == Motion.DodgeLeft &&
                (attack == Motion.VerticalSlash || attack == Motion.Kick));
    }

    private static float AttackDamage(Fighter attacker)
    {
        return attacker.facesRight ? PlayerAttackDamage : EnemyAttackDamage;
    }

    private static EffectKind HitEffectFor(Motion motion)
    {
        return motion == Motion.HorizontalSlash ? EffectKind.HorizontalHit
            : motion == Motion.VerticalSlash ? EffectKind.VerticalHit
            : EffectKind.KickHit;
    }

    private static EffectKind GuardEffectFor(Motion motion)
    {
        return motion == Motion.HorizontalSlash
            ? EffectKind.GuardHorizontal
            : EffectKind.GuardVertical;
    }

    private static EffectKind ParryEffectFor(Motion motion)
    {
        return motion == Motion.HorizontalSlash ? EffectKind.ParryHorizontal
            : motion == Motion.VerticalSlash ? EffectKind.ParryVertical
            : EffectKind.ParryKick;
    }

    private void StaggerAttacker(Fighter attacker, bool playerAttacking, float duration)
    {
        SetMotion(attacker, Motion.Stagger, duration);
        if (playerAttacking)
            _playerStaggerEnds = Time.time + duration;
        else
        {
            _enemyPhase = EnemyPhase.Staggered;
            _enemyPhaseEnds = Time.time + duration;
        }
    }

    private void Damage(Fighter target, float amount, string banner, string detail)
    {
        target.hp = Mathf.Max(0f, target.hp - amount);
        _banner = banner;
        _detail = detail;

        if (target.hp <= 0f)
        {
            SetMotion(target, Motion.Dead, 99f);
            if (target == _enemy)
            {
                _enemyPhase = EnemyPhase.Dead;
                _banner = "VICTORY";
                _detail = "Press R to duel again.";
            }
            else
            {
                _banner = "DEFEAT";
                _detail = "Press R to try again.";
            }
            _roundEndedAt = Time.time;
            return;
        }

        SetMotion(target, Motion.Hit, 0.42f);
        if (target.usesAssetModel)
            TintFighter(target, new Color(1f, 0.16f, 0.10f));
        else if (target.bodyRenderer != null)
            target.bodyRenderer.material = target.flashMaterial;
    }

    private void UpdateEnemyAI()
    {
        if (_enemy.motion == Motion.Dead || _player.motion == Motion.Dead)
            return;

        ResolveAttackIfNeeded(_enemy, false);

        if (Time.time < _enemyPhaseEnds)
            return;

        switch (_enemyPhase)
        {
            case EnemyPhase.Waiting:
                int attack = ChooseEnemyAttack();
                _enemyPhase = attack == 0
                    ? EnemyPhase.TelegraphHorizontal
                    : attack == 1 ? EnemyPhase.TelegraphVertical : EnemyPhase.TelegraphKick;
                float telegraphTime = AttackWindup;
                _enemyPhaseEnds = Time.time + telegraphTime;
                _banner = attack == 0
                    ? "INCOMING: HORIZONTAL"
                    : attack == 1 ? "INCOMING: VERTICAL" : "INCOMING: KICK";
                _detail = attack == 0
                    ? "Press S to crouch, or guard/parry."
                    : attack == 1
                        ? "Press A to dodge left, or guard/parry."
                        : "Move sideways or parry. Basic guard loses to kick.";
                SetMotion(_enemy, attack == 0 ? Motion.PrepareHorizontal
                    : attack == 1 ? Motion.PrepareVertical : Motion.PrepareKick,
                    telegraphTime);
                break;

            case EnemyPhase.TelegraphHorizontal:
                _enemyPhase = EnemyPhase.AttackingHorizontal;
                _enemyPhaseEnds = Time.time + AttackDuration(Motion.HorizontalSlash);
                SetMotion(_enemy, Motion.HorizontalSlash,
                    AttackDuration(Motion.HorizontalSlash));
                PlayAttackSound(Motion.HorizontalSlash);
                break;

            case EnemyPhase.TelegraphVertical:
                _enemyPhase = EnemyPhase.AttackingVertical;
                _enemyPhaseEnds = Time.time + AttackDuration(Motion.VerticalSlash);
                SetMotion(_enemy, Motion.VerticalSlash,
                    AttackDuration(Motion.VerticalSlash));
                PlayAttackSound(Motion.VerticalSlash);
                break;

            case EnemyPhase.TelegraphKick:
                _enemyPhase = EnemyPhase.AttackingKick;
                _enemyPhaseEnds = Time.time + 0.58f;
                SetMotion(_enemy, Motion.Kick, 0.58f);
                PlayAttackSound(Motion.Kick);
                break;

            case EnemyPhase.AttackingHorizontal:
            case EnemyPhase.AttackingVertical:
            case EnemyPhase.AttackingKick:
            case EnemyPhase.Guarding:
            case EnemyPhase.Parrying:
            case EnemyPhase.DodgingCrouch:
            case EnemyPhase.DodgingLeft:
            case EnemyPhase.Staggered:
                _enemyPhase = EnemyPhase.Waiting;
                float recovery = UnityEngine.Random.Range(1.05f, 1.75f) -
                                 Mathf.Min(0.38f, _enemyComboPressure * 0.10f);
                _enemyPhaseEnds = Time.time + recovery;
                SetMotion(_enemy, Motion.Idle, 0.2f);
                break;
        }
    }

    private int ChooseEnemyAttack()
    {
        // The rival observes which defensive answer the player prefers and
        // weights attacks that punish that habit, while avoiding obvious loops.
        float horizontal = 1f + _playerLeftDodgeUses * 0.32f;
        float vertical = 1f + _playerCrouchUses * 0.25f;
        float kick = 1f + _playerCrouchUses * 0.34f + _playerParryUses * 0.08f;

        if (_lastEnemyAttack == Motion.HorizontalSlash) horizontal *= 0.34f;
        if (_lastEnemyAttack == Motion.VerticalSlash) vertical *= 0.34f;
        if (_lastEnemyAttack == Motion.Kick) kick *= 0.34f;

        float roll = UnityEngine.Random.value * (horizontal + vertical + kick);
        int choice = roll < horizontal ? 0 : roll < horizontal + vertical ? 1 : 2;
        _lastEnemyAttack = choice == 0
            ? Motion.HorizontalSlash
            : choice == 1 ? Motion.VerticalSlash : Motion.Kick;
        return choice;
    }

    private void SetMotion(Fighter fighter, Motion motion, float duration)
    {
        if (fighter.motion == motion && motion == Motion.Guard)
        {
            fighter.motionStarted = Time.time;
            fighter.motionDuration = duration;
            return;
        }

        fighter.motion = motion;
        fighter.motionStarted = Time.time;
        fighter.motionDuration = Mathf.Max(0.01f, duration);
        fighter.hitResolved = false;

        if (motion != Motion.Hit)
            RestoreFighterAppearance(fighter);

        PlayAssetAnimation(fighter, motion);
    }

    private static bool IsAttackMotion(Motion motion)
    {
        return motion == Motion.HorizontalSlash ||
               motion == Motion.VerticalSlash ||
               motion == Motion.Kick;
    }

    private static bool IsPrepareMotion(Motion motion)
    {
        return motion == Motion.PrepareHorizontal ||
               motion == Motion.PrepareVertical ||
               motion == Motion.PrepareKick;
    }

    private static bool IsDodgeMotion(Motion motion)
    {
        return motion == Motion.DodgeCrouch || motion == Motion.DodgeLeft;
    }

    private static bool MotionFinished(Fighter fighter)
    {
        return Time.time >= fighter.motionStarted + fighter.motionDuration;
    }

    private static float MotionProgress(Fighter fighter)
    {
        return Mathf.Clamp01((Time.time - fighter.motionStarted) / fighter.motionDuration);
    }

    private void AnimateFighter(Fighter fighter)
    {
        if (fighter.usesAssetModel)
        {
            AnimateAssetFighter(fighter);
            return;
        }

        float t = MotionProgress(fighter);
        float side = fighter.facesRight ? 1f : -1f;
        Vector3 rootEuler = Vector3.zero;
        Vector3 swordEuler = new Vector3(0f, 0f, -22f * side);
        Vector3 shieldEuler = new Vector3(90f, 0f, 0f);
        Vector3 bodyScale = Vector3.one;
        Vector3 bodyPosition = new Vector3(0f, 1.05f, 0f);
        Vector3 rootPosition = fighter.basePosition;
        Vector3 swordPosition = new Vector3(0.48f * side, 1.45f, 0f);

        switch (fighter.motion)
        {
            case Motion.HorizontalSlash:
            {
                float arc = t < 0.28f
                    ? Mathf.Lerp(-35f, -125f, t / 0.28f)
                    : Mathf.Lerp(-125f, 115f, Mathf.SmoothStep(0f, 1f, (t - 0.28f) / 0.72f));
                swordEuler = new Vector3(5f, 0f, arc * side);
                rootEuler.z = Mathf.Sin(t * Mathf.PI) * -7f * side;
                break;
            }

            case Motion.VerticalSlash:
            {
                float arc = t < 0.3f
                    ? Mathf.Lerp(0f, -120f, t / 0.3f)
                    : Mathf.Lerp(-120f, 65f, Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.7f));
                swordEuler = new Vector3(arc, 0f, -8f * side);
                rootEuler.x = Mathf.Sin(t * Mathf.PI) * 10f;
                break;
            }

            case Motion.Kick:
            {
                float drive = Mathf.Sin(t * Mathf.PI);
                rootPosition.x += drive * 0.14f * side;
                bodyPosition.x -= drive * 0.10f * side;
                rootEuler.z = 8f * drive * side;
                break;
            }

            case Motion.Guard:
                shieldEuler = new Vector3(90f, 0f, 0f);
                fighter.shieldPivot.localPosition = new Vector3(0.12f * side, 1.4f, -0.38f);
                bodyScale = new Vector3(0.96f, 0.96f, 0.96f);
                break;

            case Motion.Parry:
            {
                float parryReach = Mathf.Sin(t * Mathf.PI);
                shieldEuler = new Vector3(90f, 0f, -62f * parryReach * side);
                fighter.shieldPivot.localPosition = new Vector3(
                    (-0.08f - 0.78f * parryReach) * side,
                    1.40f + 0.10f * parryReach,
                    -0.38f);
                fighter.shieldRenderer.material = fighter.guardMaterial;
                break;
            }

            case Motion.DodgeCrouch:
            {
                float dip = Mathf.Sin(t * Mathf.PI);
                rootPosition.y -= dip * 0.34f;
                bodyPosition.y -= dip * 0.38f;
                bodyScale = new Vector3(1.05f, Mathf.Lerp(1f, 0.56f, dip), 1.05f);
                swordEuler.z = -35f * side;
                shieldEuler.y = 25f * side;
                break;
            }

            case Motion.DodgeLeft:
            {
                float sidestep = Mathf.Sin(t * Mathf.PI);
                rootPosition.z += sidestep * 1.35f * fighter.dodgeDirection;
                rootEuler.x = -11f * sidestep * fighter.dodgeDirection;
                rootEuler.z = 12f * sidestep * side * fighter.dodgeDirection;
                swordEuler.z = -48f * side;
                break;
            }

            case Motion.Hit:
                rootEuler.z = Mathf.Sin(t * Mathf.PI * 3f) * 10f * side;
                bodyPosition.x = -Mathf.Sin(t * Mathf.PI) * 0.3f * side;
                break;

            case Motion.Stagger:
                rootEuler.z = -16f * side + Mathf.Sin(Time.time * 24f) * 2f;
                bodyPosition.x = -0.25f * side;
                break;

            case Motion.Dead:
                rootEuler.z = Mathf.Lerp(0f, -82f * side, Mathf.Clamp01(t * 1.8f));
                bodyPosition.y = Mathf.Lerp(1.05f, 0.5f, Mathf.Clamp01(t * 1.8f));
                break;

            default:
                bodyPosition.y += Mathf.Sin(Time.time * 2.2f + (fighter.facesRight ? 0f : 1f)) * 0.025f;
                fighter.shieldRenderer.material = fighter.baseMaterial;
                break;
        }

        if (fighter.motion != Motion.Parry)
            fighter.shieldRenderer.material = fighter.baseMaterial;

        fighter.root.localRotation = Quaternion.Euler(rootEuler);
        fighter.root.position = rootPosition;
        fighter.body.localPosition = bodyPosition;
        fighter.body.localScale = bodyScale;
        fighter.swordPivot.localPosition = swordPosition;
        fighter.swordPivot.localRotation = Quaternion.Euler(swordEuler);
        fighter.shieldPivot.localRotation = Quaternion.Euler(shieldEuler);

        if (fighter.motion != Motion.Guard && fighter.motion != Motion.Parry)
            fighter.shieldPivot.localPosition = new Vector3(-0.68f * side, 1.22f, -0.05f);

        if (fighter.motion == Motion.Hit && MotionFinished(fighter))
            fighter.bodyRenderer.material = fighter.baseMaterial;
    }

    private Fighter CreateFighter(string fighterName, Vector3 position, bool facesRight, Material bodyMaterial)
    {
        if (_assetLibrary != null && _assetLibrary.fighterPrefab != null)
            return CreateAssetFighter(fighterName, position, facesRight, bodyMaterial);

        var fighter = new Fighter
        {
            facesRight = facesRight,
            baseMaterial = bodyMaterial,
            flashMaterial = CreateMaterial(new Color(1f, 0.18f, 0.12f), 0f, 0.35f),
            guardMaterial = _goldMaterial
        };

        var root = new GameObject(fighterName).transform;
        root.SetParent(transform);
        root.position = position;
        fighter.root = root;
        fighter.basePosition = position;
        fighter.baseRotation = Quaternion.identity;

        fighter.body = CreatePrimitive(PrimitiveType.Capsule, "Body", root,
            new Vector3(0f, 1.05f, 0f), new Vector3(0.72f, 0.88f, 0.55f), bodyMaterial);
        fighter.bodyRenderer = fighter.body.GetComponent<Renderer>();

        fighter.head = CreatePrimitive(PrimitiveType.Sphere, "Head", root,
            new Vector3(0f, 2.17f, 0f), new Vector3(0.58f, 0.58f, 0.58f), bodyMaterial);
        CreatePrimitive(PrimitiveType.Cube, "Visor", fighter.head,
            new Vector3(0f, 0.04f, -0.46f), new Vector3(0.58f, 0.16f, 0.09f), _goldMaterial);

        CreatePrimitive(PrimitiveType.Capsule, "Leg L", root,
            new Vector3(-0.24f, 0.2f, 0f), new Vector3(0.23f, 0.62f, 0.23f), bodyMaterial);
        CreatePrimitive(PrimitiveType.Capsule, "Leg R", root,
            new Vector3(0.24f, 0.2f, 0f), new Vector3(0.23f, 0.62f, 0.23f), bodyMaterial);

        fighter.swordPivot = new GameObject("Sword Pivot").transform;
        fighter.swordPivot.SetParent(root);
        fighter.swordPivot.localPosition = new Vector3(0.48f * (facesRight ? 1f : -1f), 1.45f, 0f);
        CreatePrimitive(PrimitiveType.Cube, "Sword Grip", fighter.swordPivot,
            new Vector3(0f, 0.15f, 0f), new Vector3(0.13f, 0.42f, 0.13f), _stoneDark);
        Transform blade = CreatePrimitive(PrimitiveType.Cube, "Sword Blade", fighter.swordPivot,
            new Vector3(0f, 1.0f, 0f), new Vector3(0.12f, 1.35f, 0.18f), _goldMaterial);
        fighter.swordRenderer = blade.GetComponent<Renderer>();
        CreatePrimitive(PrimitiveType.Cube, "Sword Guard", fighter.swordPivot,
            new Vector3(0f, 0.4f, 0f), new Vector3(0.62f, 0.10f, 0.14f), _stoneLight);

        fighter.shieldPivot = new GameObject("Shield Pivot").transform;
        fighter.shieldPivot.SetParent(root);
        fighter.shieldPivot.localPosition = new Vector3(-0.42f * (facesRight ? 1f : -1f), 1.25f, 0f);
        Transform shield = CreatePrimitive(PrimitiveType.Cylinder, "Shield", fighter.shieldPivot,
            Vector3.zero, new Vector3(0.82f, 0.10f, 0.82f), bodyMaterial);
        fighter.shieldRenderer = shield.GetComponent<Renderer>();
        CreatePrimitive(PrimitiveType.Cylinder, "Shield Boss", shield,
            new Vector3(0f, 0.6f, 0f), new Vector3(0.26f, 0.18f, 0.26f), _goldMaterial);

        return fighter;
    }

    private Fighter CreateAssetFighter(
        string fighterName,
        Vector3 position,
        bool facesRight,
        Material teamMaterial)
    {
        var fighter = new Fighter
        {
            facesRight = facesRight,
            baseMaterial = teamMaterial,
            flashMaterial = _dangerMaterial,
            guardMaterial = _goldMaterial,
            basePosition = position,
            baseRotation = Quaternion.Euler(0f, facesRight ? 90f : -90f, 0f),
            baseScale = Vector3.one,
            usesAssetModel = true
        };

        Transform root = new GameObject(fighterName).transform;
        root.SetParent(transform);
        root.position = position;
        root.rotation = fighter.baseRotation;
        fighter.root = root;

        GameObject model = Instantiate(_assetLibrary.fighterPrefab, root);
        model.name = fighterName + " Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        fighter.body = model.transform;
        fighter.head = model.transform;

        Animator[] animators = model.GetComponentsInChildren<Animator>(true);
        foreach (Animator candidate in animators)
        {
            if (fighter.animator == null || candidate.avatar != null)
                fighter.animator = candidate;
            if (candidate.avatar != null)
                break;
        }
        if (fighter.animator == null)
            fighter.animator = model.AddComponent<Animator>();
        fighter.animator.runtimeAnimatorController = _assetLibrary.combatController;
        fighter.animator.applyRootMotion = false;
        fighter.groundedRig = fighter.animator.gameObject.GetComponent<GroundedFighterRig>();
        if (fighter.groundedRig == null)
            fighter.groundedRig = fighter.animator.gameObject.AddComponent<GroundedFighterRig>();
        fighter.groundedRig.Configure(fighter.animator, root);
        fighter.kickTarget = new GameObject("Right Foot Kick Target").transform;
        fighter.kickTarget.SetParent(root);
        fighter.kickTarget.position = root.position + root.up * 0.25f + root.forward * 0.2f;
        fighter.groundedRig.ConfigureKickFoot(fighter.kickTarget);

        Transform leftHand = fighter.animator.isHuman
            ? fighter.animator.GetBoneTransform(HumanBodyBones.LeftHand)
            : FindTransformContaining(model.transform, "LeftHand");
        Transform rightHand = fighter.animator.isHuman
            ? fighter.animator.GetBoneTransform(HumanBodyBones.RightHand)
            : FindTransformContaining(model.transform, "RightHand");
        Transform originalSword = FindTransformContaining(model.transform, "Sword");
        if (originalSword != null)
        {
            foreach (Renderer swordPart in originalSword.GetComponentsInChildren<Renderer>(true))
                swordPart.enabled = false;
        }
        CreateAssetShield(fighter, leftHand, root, teamMaterial);
        CreateAssetSword(fighter, rightHand, root);
        fighter.groundedRig.ConfigureCombatHands(fighter.swordPivot, fighter.shieldPivot);

        fighter.renderers = model.GetComponentsInChildren<Renderer>(true);
        fighter.rendererBaseColors = new Color[fighter.renderers.Length];
        Color teamColor = facesRight ? new Color(0.18f, 0.55f, 1f) : new Color(1f, 0.23f, 0.18f);
        ConvertFighterMaterialsToUrp(fighter.renderers, teamColor);
        for (int i = 0; i < fighter.renderers.Length; i++)
        {
            // ConvertFighterMaterialsToUrp already applied the team hint; read
            // it back as-is instead of blending a second time (that compounded
            // into a flat, detail-less team color).
            Material shared = fighter.renderers[i].sharedMaterial;
            fighter.rendererBaseColors[i] = shared != null && shared.HasProperty("_BaseColor")
                ? shared.GetColor("_BaseColor")
                : shared != null && shared.HasProperty("_Color")
                    ? shared.color
                    : Color.white;
        }
        fighter.bodyRenderer = fighter.renderers.Length > 0 ? fighter.renderers[0] : null;
        RestoreFighterAppearance(fighter);
        return fighter;
    }

    private void CreateAssetShield(
        Fighter fighter,
        Transform leftHand,
        Transform fighterRoot,
        Material teamMaterial)
    {
        if (leftHand == null)
            return;

        var mount = new GameObject("Left Hand Shield");
        mount.transform.SetParent(fighterRoot);
        AssetShieldFollower follower = mount.AddComponent<AssetShieldFollower>();
        follower.Configure(leftHand, fighterRoot);
        fighter.shieldFollower = follower;

        fighter.shieldPivot = mount.transform;
        if (_assetLibrary != null && _assetLibrary.kevinShieldPrefab != null)
        {
            GameObject shield = Instantiate(_assetLibrary.kevinShieldPrefab, mount.transform);
            shield.name = "Kevin Iglesias Shield";
            shield.transform.localPosition = Vector3.zero;
            shield.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            shield.transform.localScale = Vector3.one * 1.12f;
            Renderer[] renderers = shield.GetComponentsInChildren<Renderer>(true);
            ConvertFighterMaterialsToUrp(renderers, teamMaterial.color);
            fighter.shieldRenderer = renderers.Length > 0 ? renderers[0] : null;
        }
        else
        {
            Transform shield = CreatePrimitive(PrimitiveType.Cylinder, "Shield Face", mount.transform,
                Vector3.zero, new Vector3(0.43f, 0.065f, 0.43f), teamMaterial);
            shield.localRotation = Quaternion.identity;
            fighter.shieldRenderer = shield.GetComponent<Renderer>();

            Transform rim = CreatePrimitive(PrimitiveType.Cylinder, "Shield Rim", mount.transform,
                new Vector3(0f, 0.012f, 0f), new Vector3(0.49f, 0.045f, 0.49f), _goldMaterial);
            rim.SetSiblingIndex(0);
            CreatePrimitive(PrimitiveType.Cylinder, "Shield Boss", shield,
                new Vector3(0f, 0.58f, 0f), new Vector3(0.30f, 0.16f, 0.30f), _goldMaterial);
        }
    }

    private void CreateAssetSword(Fighter fighter, Transform rightHand, Transform fighterRoot)
    {
        if (rightHand == null)
            return;

        Transform mount = new GameObject("Right Hand Sword (Constrained)").transform;
        mount.SetParent(fighterRoot);
        AssetSwordFollower follower = mount.gameObject.AddComponent<AssetSwordFollower>();
        follower.Configure(rightHand, fighterRoot);
        fighter.swordFollower = follower;
        fighter.swordPivot = mount;

        if (_assetLibrary != null && _assetLibrary.kevinSwordPrefab != null)
        {
            GameObject sword = Instantiate(_assetLibrary.kevinSwordPrefab, mount);
            sword.name = "Kevin Iglesias Sword";
            sword.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            // Kevin's sword mesh is authored along local X, while the constrained
            // combat rig treats local Y as the blade axis.
            sword.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            sword.transform.localScale = Vector3.one * 1.18f;
            Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
            ConvertFighterMaterialsToUrp(renderers,
                fighter.facesRight ? new Color(0.18f, 0.62f, 1f) :
                    new Color(1f, 0.28f, 0.16f));
            fighter.swordRenderer = renderers.Length > 0 ? renderers[0] : null;
        }
        else
        {
            CreatePrimitive(PrimitiveType.Cylinder, "Sword Grip", mount,
                new Vector3(0f, 0.12f, 0f), new Vector3(0.055f, 0.22f, 0.055f), _stoneDark);
            CreatePrimitive(PrimitiveType.Cube, "Sword Crossguard", mount,
                new Vector3(0f, 0.34f, 0f), new Vector3(0.42f, 0.055f, 0.07f), _goldMaterial);
            Material bladeMaterial = fighter.facesRight ? _parryMaterial : _dangerMaterial;
            Transform blade = CreatePrimitive(PrimitiveType.Cube, "Sword Blade", mount,
                new Vector3(0f, 0.98f, 0f), new Vector3(0.10f, 1.34f, 0.055f), bladeMaterial);
            fighter.swordRenderer = blade.GetComponent<Renderer>();
            CreatePrimitive(PrimitiveType.Cube, "Sword Energy Core", mount,
                new Vector3(0f, 0.98f, -0.03f), new Vector3(0.026f, 1.25f, 0.018f),
                _bladeCoreMaterial);
        }

        // A trail on the blade tip traces whatever arc the sword actually
        // swings through - horizontal cuts read as a horizontal streak,
        // vertical cuts as a vertical one, for free from the real motion
        // (see AssetSwordFollower) instead of a separate canned effect.
        Transform tip = new GameObject("Sword Blade Tip").transform;
        tip.SetParent(mount);
        tip.localPosition = new Vector3(0f, 1.68f, 0f);
        tip.localRotation = Quaternion.identity;
        TrailRenderer trail = tip.gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.22f;
        trail.minVertexDistance = 0.03f;
        trail.startWidth = 0.18f;
        trail.endWidth = 0.01f;
        trail.numCornerVertices = 4;
        trail.numCapVertices = 4;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.emitting = false;
        Color trailColor = fighter.facesRight
            ? new Color(0.15f, 0.85f, 1f, 0.95f)
            : new Color(1f, 0.28f, 0.1f, 0.95f);
        trail.startColor = trailColor;
        trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        fighter.swordTrail = trail;
    }

    private static Transform FindTransformContaining(Transform root, string text)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
        }
        return null;
    }

    private static void ConvertFighterMaterialsToUrp(Renderer[] renderers, Color teamColor)
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null)
            litShader = Shader.Find("Standard");
        if (litShader == null)
            return;

        var converted = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null)
                    continue;

                if (!converted.TryGetValue(source, out Material replacement))
                {
                    replacement = new Material(litShader) { name = source.name + " Duel URP" };
                    Texture texture = null;
                    if (source.HasProperty("_BaseMap"))
                        texture = source.GetTexture("_BaseMap");
                    if (texture == null && source.HasProperty("_MainTex"))
                        texture = source.GetTexture("_MainTex");
                    if (texture != null)
                    {
                        if (replacement.HasProperty("_BaseMap"))
                            replacement.SetTexture("_BaseMap", texture);
                        if (replacement.HasProperty("_MainTex"))
                            replacement.SetTexture("_MainTex", texture);
                    }

                    Color sourceColor = source.HasProperty("_BaseColor")
                        ? source.GetColor("_BaseColor")
                        : source.HasProperty("_Color") ? source.color : Color.white;
                    // Light hint only - a strong blend here (plus the old second
                    // blend in CreateAssetFighter) washed the source texture out
                    // into a flat team-colored blob.
                    Color finalColor = Color.Lerp(sourceColor, teamColor, 0.1f);
                    if (replacement.HasProperty("_BaseColor"))
                        replacement.SetColor("_BaseColor", finalColor);
                    if (replacement.HasProperty("_Color"))
                        replacement.SetColor("_Color", finalColor);
                    if (replacement.HasProperty("_Metallic"))
                        replacement.SetFloat("_Metallic", 0.16f);
                    if (replacement.HasProperty("_Smoothness"))
                        replacement.SetFloat("_Smoothness", 0.48f);
                    converted[source] = replacement;
                }
                materials[i] = replacement;
            }
            renderer.materials = materials;
        }
    }

    private void AnimateAssetFighter(Fighter fighter)
    {
        if (fighter.groundedRig != null)
            fighter.groundedRig.lockFeet = fighter.motion != Motion.Dead;

        float t = MotionProgress(fighter);
        float side = fighter.facesRight ? 1f : -1f;
        Vector3 position = fighter.basePosition;
        Vector3 scale = fighter.baseScale;
        Quaternion actionRotation = Quaternion.identity;

        switch (fighter.motion)
        {
            case Motion.Kick:
                actionRotation = Quaternion.Euler(0f, 0f,
                    8f * Mathf.Sin(t * Mathf.PI) * side);
                break;
            case Motion.DodgeCrouch:
                float dip = Mathf.Sin(t * Mathf.PI);
                actionRotation = Quaternion.Euler(7f * dip, 0f, 0f);
                break;
            case Motion.DodgeLeft:
                float sidestep = Mathf.Sin(t * Mathf.PI);
                actionRotation = Quaternion.Euler(
                    -8f * sidestep * fighter.dodgeDirection,
                    0f,
                    13f * sidestep * side * fighter.dodgeDirection);
                break;
            case Motion.Hit:
                position.x -= Mathf.Sin(t * Mathf.PI) * 0.28f * side;
                actionRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * Mathf.PI * 3f) * 7f * side);
                break;
            case Motion.Stagger:
                position.x -= 0.22f * side;
                actionRotation = Quaternion.Euler(0f, 0f, -13f * side);
                break;
            case Motion.Dead:
                actionRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(0f, -82f * side, Mathf.Clamp01(t * 1.8f)));
                position.y -= Mathf.Lerp(0f, 0.55f, Mathf.Clamp01(t * 1.8f));
                break;
        }

        fighter.root.position = position;
        fighter.root.rotation = fighter.baseRotation * actionRotation;
        fighter.root.localScale = scale;
        if (fighter.kickTarget != null)
        {
            float kickDrive = Mathf.Sin(t * Mathf.PI);
            fighter.kickTarget.position = fighter.root.position + fighter.root.up *
                (0.24f + kickDrive * 0.62f) + fighter.root.forward *
                (0.22f + kickDrive * 1.02f) + fighter.root.right * 0.16f;
        }
        UpdateBladeTelegraph(fighter);
        SwordPose swordPose = fighter.motion switch
        {
            Motion.PrepareHorizontal => SwordPose.PrepareHorizontal,
            Motion.PrepareVertical => SwordPose.PrepareVertical,
            Motion.HorizontalSlash => SwordPose.StrikeHorizontal,
            Motion.VerticalSlash => SwordPose.StrikeVertical,
            _ => SwordPose.Idle
        };
        if (fighter.swordFollower != null)
            fighter.swordFollower.SetPose(swordPose, t);
        if (fighter.swordTrail != null)
            fighter.swordTrail.emitting = swordPose != SwordPose.Idle;
        if (fighter.shieldFollower != null)
            fighter.shieldFollower.SetPose(
                fighter.motion == Motion.Guard,
                fighter.motion == Motion.Parry,
                t);
        if (fighter.groundedRig != null)
        {
            // The sword hand is IK-driven (via AssetSwordFollower's explicit
            // pose) only while winding up or striking, so the readable
            // horizontal/vertical geometry actually reaches the visible
            // blade instead of whatever the authored clip's hand does.
            fighter.groundedRig.lockRightHand = swordPose != SwordPose.Idle;
            // The shield mount is always the left-hand target, keeping the
            // shield attached while resting outside the torso as well as
            // during the guard-to-parry extension.
            fighter.groundedRig.lockLeftHand = fighter.shieldFollower != null;
            fighter.groundedRig.kickActive = fighter.motion == Motion.Kick;
            fighter.groundedRig.crouchWeight = fighter.motion == Motion.DodgeCrouch
                ? Mathf.Sin(t * Mathf.PI) : 0f;
            fighter.groundedRig.lateralWeight = fighter.motion == Motion.DodgeLeft
                ? Mathf.Sin(t * Mathf.PI) : 0f;
            fighter.groundedRig.lateralDirection = fighter.dodgeDirection;
        }

        if (fighter.motion == Motion.Hit && MotionFinished(fighter))
            RestoreFighterAppearance(fighter);
    }

    private static readonly Color TelegraphHorizontalColor = new Color(1f, 0.55f, 0.08f);
    private static readonly Color TelegraphVerticalColor = new Color(0.55f, 0.2f, 1f);

    // Beyond the pose itself (see SampleAuthoredSwordAnimation), the blade's
    // own glow color/pulse tells the opponent which attack is charging —
    // amber for the horizontal cut, violet for the vertical one — so the
    // read doesn't depend entirely on catching a subtle animation difference.
    private static void UpdateBladeTelegraph(Fighter fighter)
    {
        if (fighter.swordRenderer == null)
            return;

        if (fighter.motion == Motion.PrepareHorizontal || fighter.motion == Motion.PrepareVertical)
        {
            Color tint = fighter.motion == Motion.PrepareHorizontal
                ? TelegraphHorizontalColor
                : TelegraphVerticalColor;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 16f);
            var block = new MaterialPropertyBlock();
            fighter.swordRenderer.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", tint * (2.6f * pulse));
            fighter.swordRenderer.SetPropertyBlock(block);
        }
        else
        {
            fighter.swordRenderer.SetPropertyBlock(null);
        }
    }

    private void PlayAssetAnimation(Fighter fighter, Motion motion)
    {
        if (!fighter.usesAssetModel || fighter.animator == null ||
            fighter.animator.runtimeAnimatorController == null)
            return;

        if (motion == Motion.Idle)
        {
            fighter.animator.speed = 1f;
            if (fighter.animator.layerCount > 1)
                fighter.animator.SetLayerWeight(1, 0f);
            fighter.animator.Play(Animator.StringToHash("Idle"), 0, 0f);
            return;
        }

        if (motion == Motion.PrepareKick || motion == Motion.Kick ||
            motion == Motion.Guard)
        {
            fighter.animator.speed = 1f;
            if (fighter.animator.layerCount > 1)
                fighter.animator.SetLayerWeight(1, 0f);
            return;
        }

        bool preparing = IsPrepareMotion(motion);
        Motion clipMotion = motion == Motion.PrepareHorizontal ? Motion.HorizontalSlash
            : motion == Motion.PrepareVertical ? Motion.VerticalSlash
            : motion;
        int state = Animator.StringToHash(clipMotion.ToString());
        if (fighter.animator.layerCount > 1 && fighter.animator.HasState(1, state))
        {
            fighter.animator.SetLayerWeight(1, 1f);
            if (preparing || IsAttackMotion(motion))
            {
                // Attack poses are sampled deterministically in
                // SampleAuthoredSwordAnimation for authored anticipation,
                // acceleration and recovery timing.
                fighter.animator.speed = 0f;
                fighter.animator.Play(state, 1, preparing ? 0.02f : 0.18f);
            }
            else
            {
                fighter.animator.speed = 1f;
                fighter.animator.Play(state, 1, 0f);
            }
        }
    }

    private static void TintFighter(Fighter fighter, Color color)
    {
        if (fighter.renderers == null)
            return;

        var block = new MaterialPropertyBlock();
        for (int i = 0; i < fighter.renderers.Length; i++)
        {
            Renderer renderer = fighter.renderers[i];
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }
    }

    private static void RestoreFighterAppearance(Fighter fighter)
    {
        if (!fighter.usesAssetModel)
        {
            if (fighter.bodyRenderer != null && fighter.baseMaterial != null)
                fighter.bodyRenderer.material = fighter.baseMaterial;
            return;
        }

        if (fighter.renderers == null || fighter.rendererBaseColors == null)
            return;

        var block = new MaterialPropertyBlock();
        for (int i = 0; i < fighter.renderers.Length; i++)
        {
            Renderer renderer = fighter.renderers[i];
            renderer.GetPropertyBlock(block);
            Color color = fighter.rendererBaseColors[Mathf.Min(i, fighter.rendererBaseColors.Length - 1)];
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }
    }

    private void ConfigureScene()
    {
        Camera camera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        // Player-side over-the-shoulder view: the blue fighter stays in the
        // foreground while the rival and its attack telegraphs remain readable.
        camera.transform.position = new Vector3(-6.4f, 3.15f, -3.25f);
        camera.transform.LookAt(new Vector3(1.15f, 1.28f, 0f));
        camera.fieldOfView = 58f;
        camera.backgroundColor = new Color(0.025f, 0.035f, 0.06f);
        Material skybox = _assetLibrary != null ? _assetLibrary.skyboxMaterial : null;
        if (skybox != null)
        {
            RenderSettings.skybox = skybox;
            camera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
        }
        _mainCamera = camera;
        _cameraRestPosition = camera.transform.position;

        Light[] lights = FindObjectsByType<Light>();
        foreach (Light light in lights)
            light.enabled = false;

        var keyLightObject = new GameObject("Arena Key Light");
        keyLightObject.transform.SetParent(transform);
        keyLightObject.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        Light keyLight = keyLightObject.AddComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.color = new Color(0.72f, 0.82f, 1f);
        keyLight.intensity = 1.65f;
        keyLight.shadows = LightShadows.Soft;

        CreatePointLight(new Vector3(-4.5f, 3.2f, 1.4f), new Color(0.1f, 0.45f, 1f), 8f, 5f);
        CreatePointLight(new Vector3(4.5f, 3.2f, 1.4f), new Color(1f, 0.14f, 0.05f), 8f, 5f);
    }

    private void CreateArena()
    {
        var arena = new GameObject("Arena").transform;
        arena.SetParent(transform);

        if (_assetLibrary != null && _assetLibrary.dungeonGate != null)
        {
            GameObject gate = Instantiate(_assetLibrary.dungeonGate, arena);
            gate.name = "Sci-Fi Modular Door";
            gate.transform.localPosition = new Vector3(2.25f, 0f, 4.05f);
            gate.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            gate.transform.localScale = Vector3.one * 1.4f;
            ConvertDungeonMaterials(gate.GetComponentsInChildren<Renderer>(true));
        }

        if (_assetLibrary != null && _assetLibrary.dungeonRoom != null)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject wall = Instantiate(_assetLibrary.dungeonRoom, arena);
                wall.name = "Sci-Fi Modular Backdrop";
                wall.transform.localPosition = new Vector3(5.4f * i, 0f, 2.6f);
                wall.transform.localRotation = Quaternion.Euler(0f, i < 0 ? 200f : 160f, 0f);
                wall.transform.localScale = Vector3.one * 1.6f;
                ConvertDungeonMaterials(wall.GetComponentsInChildren<Renderer>(true));
            }
        }

        if (_assetLibrary != null && _assetLibrary.dungeonCorridor != null)
        {
            GameObject floor = Instantiate(_assetLibrary.dungeonCorridor, arena);
            floor.name = "Sci-Fi Modular Approach Floor";
            floor.transform.localPosition = new Vector3(0f, -0.16f, 4.6f);
            floor.transform.localScale = new Vector3(5.5f, 1f, 2.4f);
            ConvertDungeonMaterials(floor.GetComponentsInChildren<Renderer>(true));
        }

        CreatePrimitive(PrimitiveType.Cylinder, "Duel Platform", arena,
            new Vector3(0f, -0.15f, 0f), new Vector3(5.6f, 0.28f, 5.6f), _stoneDark);
        CreatePrimitive(PrimitiveType.Cylinder, "Platform Inlay", arena,
            new Vector3(0f, 0.03f, 0f), new Vector3(4.75f, 0.06f, 4.75f), _stoneLight);

        // Real sci-fi light props ring the platform instead of the old
        // primitive gold/black bars.
        GameObject floorLightPrefab = _assetLibrary != null ? _assetLibrary.arenaFloorLight : null;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 2f / 8f;
            Vector3 point = new Vector3(Mathf.Cos(angle) * 4.6f, 0.05f, Mathf.Sin(angle) * 4.6f);
            if (floorLightPrefab != null)
            {
                GameObject prop = Instantiate(floorLightPrefab, arena);
                prop.name = "Sci-Fi Modular Floor Light";
                prop.transform.localPosition = point;
                prop.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                prop.transform.localScale = Vector3.one * 0.8f;
                ConvertDungeonMaterials(prop.GetComponentsInChildren<Renderer>(true));
            }
            else
            {
                CreatePrimitive(PrimitiveType.Cylinder, "Arena Floor Marker", arena,
                    point, new Vector3(0.22f, 0.05f, 0.22f), _parryMaterial);
            }
        }

        for (int i = -1; i <= 1; i += 2)
        {
            CreatePrimitive(PrimitiveType.Cylinder, "Pillar", arena,
                new Vector3(6.6f * i, 1.4f, 3.6f), new Vector3(0.55f, 1.4f, 0.55f), _stoneDark);
            CreatePrimitive(PrimitiveType.Sphere, "Pillar Flame", arena,
                new Vector3(6.6f * i, 3.05f, 3.6f), new Vector3(0.3f, 0.45f, 0.3f),
                i < 0 ? _playerMaterial : _enemyMaterial);
        }

        CreatePrimitive(PrimitiveType.Cube, "Back Wall", arena,
            new Vector3(0f, 1.75f, 4.2f), new Vector3(13f, 3.5f, 0.35f), _stoneDark);
        for (int i = -5; i <= 5; i += 2)
        {
            CreatePrimitive(PrimitiveType.Cube, "Wall Accent", arena,
                new Vector3(i, 2.1f, 3.98f), new Vector3(0.16f, 2.1f, 0.08f), _goldMaterial);
        }
    }

    private void ConvertDungeonMaterials(Renderer[] renderers)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                var replacement = new Material(shader);
                Texture texture = source != null && source.HasProperty("_MainTex")
                    ? source.mainTexture : null;
                if (texture != null && replacement.HasProperty("_BaseMap"))
                    replacement.SetTexture("_BaseMap", texture);
                // Keep the sci-fi pack's own tint/metal/gloss instead of a
                // flat gray recolor, so its panel and emissive detailing survives
                // the Standard -> URP shader swap.
                Color sourceColor = source != null && source.HasProperty("_Color")
                    ? source.color : new Color(0.6f, 0.66f, 0.74f);
                if (replacement.HasProperty("_BaseColor"))
                    replacement.SetColor("_BaseColor", sourceColor);
                float sourceMetallic = source != null && source.HasProperty("_Metallic")
                    ? source.GetFloat("_Metallic") : 0.55f;
                float sourceSmoothness = source != null && source.HasProperty("_Glossiness")
                    ? source.GetFloat("_Glossiness") : 0.5f;
                replacement.SetFloat("_Metallic", sourceMetallic);
                replacement.SetFloat("_Smoothness", sourceSmoothness);
                if (source != null && source.HasProperty("_EmissionColor"))
                {
                    Color emission = source.GetColor("_EmissionColor");
                    if (emission.maxColorComponent > 0f)
                    {
                        replacement.EnableKeyword("_EMISSION");
                        replacement.globalIlluminationFlags =
                            MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        replacement.SetColor("_EmissionColor", emission);
                    }
                }
                materials[i] = replacement;
            }
            renderer.materials = materials;
        }
    }

    private void CreatePointLight(Vector3 position, Color color, float range, float intensity)
    {
        var lightObject = new GameObject("Arena Accent Light");
        lightObject.transform.SetParent(transform);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
    }

    private Transform CreatePrimitive(
        PrimitiveType primitiveType,
        string objectName,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject instance = GameObject.CreatePrimitive(primitiveType);
        instance.name = objectName;
        instance.transform.SetParent(parent);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = localScale;

        Collider collider = instance.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer renderer = instance.GetComponent<Renderer>();
        renderer.material = material;
        return instance.transform;
    }

    private void SpawnActionEffect(Vector3 position, EffectKind kind, float size)
    {
        Sprite slash = _assetLibrary != null ? _assetLibrary.slashSprite : null;
        Sprite impact = _assetLibrary != null ? _assetLibrary.impactSprite : null;
        Sprite circle = _assetLibrary != null ? _assetLibrary.guardSprite : null;
        Sprite spark = _assetLibrary != null ? _assetLibrary.parrySprite : null;
        Vector3 front = _mainCamera != null ? _mainCamera.transform.forward * -0.18f : Vector3.zero;
        position += front;

        switch (kind)
        {
            case EffectKind.HorizontalHit:
                SpawnAnimeSprite(slash, position, new Color(1f, 0.16f, 0.04f, 0.95f),
                    new Vector3(0.7f, 0.16f, 1f) * size,
                    new Vector3(3.8f, 0.92f, 1f) * size, 0f, 0.30f, -18f);
                SpawnAnimeSprite(impact, position, new Color(1f, 0.75f, 0.18f, 1f),
                    Vector3.one * size * 0.28f, Vector3.one * size * 1.55f,
                    18f, 0.20f, 120f);
                SpawnSparkFan(spark, position, new Color(1f, 0.42f, 0.08f, 1f),
                    12, 4.8f, -28f, 28f, size * 0.22f);
                SpawnEnergyRing(position, _dangerMaterial, size * 1.2f, 0.24f);
                break;

            case EffectKind.VerticalHit:
                SpawnAnimeSprite(slash, position, new Color(0.72f, 0.22f, 1f, 0.95f),
                    new Vector3(0.58f, 0.14f, 1f) * size,
                    new Vector3(4.1f, 0.82f, 1f) * size, 90f, 0.34f, 22f);
                SpawnAnimeSprite(impact, position + Vector3.up * 0.08f,
                    new Color(1f, 0.38f, 0.84f, 1f),
                    Vector3.one * size * 0.30f, Vector3.one * size * 1.7f,
                    45f, 0.22f, -145f);
                SpawnSparkFan(spark, position, new Color(0.72f, 0.42f, 1f, 1f),
                    12, 5.2f, 62f, 118f, size * 0.22f);
                SpawnEnergyRing(position, _parryMaterial, size * 1.25f, 0.25f);
                break;

            case EffectKind.KickHit:
                SpawnAnimeSprite(impact, position, new Color(1f, 0.56f, 0.04f, 1f),
                    Vector3.one * size * 0.42f, Vector3.one * size * 2.0f,
                    12f, 0.28f, 175f);
                SpawnAnimeSprite(circle, position, new Color(1f, 0.20f, 0.04f, 0.82f),
                    Vector3.one * size * 0.35f, Vector3.one * size * 1.7f,
                    0f, 0.32f, -90f);
                SpawnSparkFan(spark, position, new Color(1f, 0.70f, 0.16f, 1f),
                    16, 4.4f, 155f, 385f, size * 0.20f);
                SpawnEnergyRing(position, _goldMaterial, size * 1.45f, 0.28f);
                break;

            case EffectKind.GuardHorizontal:
            case EffectKind.GuardVertical:
            {
                bool vertical = kind == EffectKind.GuardVertical;
                SpawnAnimeSprite(circle, position, new Color(1f, 0.68f, 0.10f, 0.92f),
                    Vector3.one * size * 0.72f, Vector3.one * size * 1.35f,
                    0f, 0.36f, 24f);
                SpawnAnimeSprite(slash, position, new Color(1f, 0.94f, 0.62f, 0.95f),
                    new Vector3(0.52f, 0.12f, 1f) * size,
                    new Vector3(2.4f, 0.55f, 1f) * size,
                    vertical ? 90f : 0f, 0.20f, vertical ? 38f : -38f);
                SpawnSparkFan(spark, position, new Color(1f, 0.80f, 0.28f, 1f),
                    14, 4.2f, vertical ? 22f : -68f, vertical ? 158f : 68f,
                    size * 0.18f);
                SpawnEnergyRing(position, _goldMaterial, size * 1.5f, 0.34f);
                break;
            }

            case EffectKind.GuardBreak:
                SpawnAnimeSprite(circle, position, new Color(1f, 0.10f, 0.02f, 0.90f),
                    Vector3.one * size * 0.65f, Vector3.one * size * 2.1f,
                    0f, 0.38f, -80f);
                SpawnAnimeSprite(impact, position, new Color(1f, 0.82f, 0.18f, 1f),
                    Vector3.one * size * 0.32f, Vector3.one * size * 2.15f,
                    22f, 0.24f, 210f);
                SpawnSparkFan(spark, position, new Color(1f, 0.24f, 0.03f, 1f),
                    22, 6.0f, 0f, 360f, size * 0.24f);
                SpawnEnergyRing(position, _dangerMaterial, size * 1.8f, 0.30f);
                SpawnEnergyRing(position, _goldMaterial, size * 1.3f, 0.20f);
                break;

            case EffectKind.ParryHorizontal:
            case EffectKind.ParryVertical:
            case EffectKind.ParryKick:
            {
                float attackAngle = kind == EffectKind.ParryVertical ? 90f
                    : kind == EffectKind.ParryKick ? -35f : 0f;
                SpawnAnimeSprite(circle, position, new Color(0.05f, 0.92f, 1f, 0.92f),
                    Vector3.one * size * 0.62f, Vector3.one * size * 2.0f,
                    0f, 0.40f, 55f);
                SpawnAnimeSprite(spark, position, Color.white,
                    Vector3.one * size * 0.38f, Vector3.one * size * 1.8f,
                    attackAngle + 42f, 0.22f, 260f);
                SpawnAnimeSprite(slash, position, new Color(0.25f, 1f, 1f, 0.96f),
                    new Vector3(0.55f, 0.12f, 1f) * size,
                    new Vector3(3.0f, 0.68f, 1f) * size,
                    attackAngle, 0.24f, -65f);
                SpawnAnimeSprite(slash, position, new Color(0.72f, 0.42f, 1f, 0.82f),
                    new Vector3(0.45f, 0.10f, 1f) * size,
                    new Vector3(2.5f, 0.56f, 1f) * size,
                    attackAngle + 90f, 0.22f, 70f);
                SpawnSparkFan(spark, position, new Color(0.38f, 1f, 1f, 1f),
                    26, 7.2f, 0f, 360f, size * 0.20f);
                SpawnEnergyRing(position, _parryMaterial, size * 2.0f, 0.32f);
                SpawnEnergyRing(position, _goldMaterial, size * 1.25f, 0.22f);
                break;
            }

            case EffectKind.DodgeCrouch:
                SpawnAnimeSprite(slash, position - Vector3.up * 0.35f,
                    new Color(0.20f, 1f, 0.52f, 0.72f),
                    new Vector3(0.8f, 0.10f, 1f) * size,
                    new Vector3(3.4f, 0.34f, 1f) * size,
                    0f, 0.34f, 0f, Vector3.down * 0.5f);
                SpawnSparkFan(spark, position - Vector3.up * 0.3f,
                    new Color(0.32f, 1f, 0.66f, 0.8f),
                    8, 2.5f, 160f, 380f, size * 0.14f);
                break;

            case EffectKind.DodgeSide:
                SpawnAnimeSprite(slash, position, new Color(0.18f, 0.82f, 1f, 0.72f),
                    new Vector3(0.55f, 0.10f, 1f) * size,
                    new Vector3(2.8f, 0.42f, 1f) * size,
                    -18f, 0.34f, -25f,
                    (_mainCamera != null ? _mainCamera.transform.right : Vector3.right) * 0.9f);
                SpawnSparkFan(spark, position, new Color(0.18f, 1f, 0.78f, 0.78f),
                    10, 3.0f, 135f, 225f, size * 0.15f);
                break;

            case EffectKind.SwordTrade:
            case EffectKind.KickTrade:
            {
                Color tradeColor = kind == EffectKind.SwordTrade
                    ? new Color(1f, 0.18f, 0.52f, 1f)
                    : new Color(1f, 0.46f, 0.08f, 1f);
                SpawnAnimeSprite(slash, position, tradeColor,
                    new Vector3(0.62f, 0.12f, 1f) * size,
                    new Vector3(3.5f, 0.68f, 1f) * size, 35f, 0.28f, 45f);
                SpawnAnimeSprite(slash, position, Color.Lerp(tradeColor, Color.cyan, 0.5f),
                    new Vector3(0.62f, 0.12f, 1f) * size,
                    new Vector3(3.5f, 0.68f, 1f) * size, -35f, 0.28f, -45f);
                SpawnAnimeSprite(impact, position, Color.white,
                    Vector3.one * size * 0.35f, Vector3.one * size * 2.0f,
                    0f, 0.22f, 220f);
                SpawnSparkFan(spark, position, tradeColor, 22, 6f,
                    0f, 360f, size * 0.20f);
                SpawnEnergyRing(position, _dangerMaterial, size * 1.8f, 0.28f);
                break;
            }
        }
        TriggerCombatFeedback(kind);
    }

    private void SpawnSparkFan(
        Sprite sprite,
        Vector3 position,
        Color color,
        int count,
        float speed,
        float startAngle,
        float endAngle,
        float size)
    {
        if (sprite == null || _mainCamera == null)
            return;

        for (int i = 0; i < count; i++)
        {
            float angle = count <= 1 ? startAngle
                : Mathf.Lerp(startAngle, endAngle, i / (float)(count - 1));
            angle += UnityEngine.Random.Range(-7f, 7f);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 velocity = (_mainCamera.transform.right * Mathf.Cos(radians) +
                                _mainCamera.transform.up * Mathf.Sin(radians)) *
                               UnityEngine.Random.Range(speed * 0.72f, speed * 1.15f);
            float particleSize = size * UnityEngine.Random.Range(0.72f, 1.2f);
            SpawnAnimeSprite(sprite, position,
                new Color(color.r, color.g, color.b, color.a * UnityEngine.Random.Range(0.72f, 1f)),
                new Vector3(particleSize * 1.8f, particleSize * 0.34f, 1f),
                new Vector3(particleSize * 0.28f, particleSize * 0.08f, 1f),
                angle, UnityEngine.Random.Range(0.24f, 0.44f),
                UnityEngine.Random.Range(-260f, 260f), velocity, 105);
        }
    }

    private void SpawnDirectionalSlash(Fighter attacker, bool horizontal)
    {
        Color color = attacker.facesRight
            ? new Color(0.12f, 0.82f, 1f, 1f)
            : new Color(1f, 0.20f, 0.06f, 1f);
        float side = attacker.facesRight ? 1f : -1f;
        // Anchored to the attacker's actual position; the old fixed
        // world-space offsets only lined up by coincidence near the origin.
        Vector3 center = attacker.root.position + new Vector3(0.9f * side, 1.42f, 0.18f);

        GameObject prefab = _assetLibrary != null
            ? (horizontal ? _assetLibrary.slashVfxHorizontal : _assetLibrary.slashVfxVertical)
            : null;
        if (prefab != null)
        {
            Quaternion facing = _mainCamera != null
                ? Quaternion.LookRotation(_mainCamera.transform.forward)
                : Quaternion.identity;
            Quaternion rotation = facing * Quaternion.Euler(0f, 0f, horizontal ? 0f : 90f);
            GameObject vfx = Instantiate(prefab, center, rotation, transform);
            vfx.transform.localScale = Vector3.one * 1.15f;
            Destroy(vfx, 1.2f);
            return;
        }

        // Fallback if the stylized slash prefabs are missing from the library.
        Sprite slashSprite = _assetLibrary != null ? _assetLibrary.slashSprite : null;
        Vector3 start = horizontal ? new Vector3(0.8f, 0.18f, 1f) : new Vector3(0.18f, 0.8f, 1f);
        Vector3 end = horizontal ? new Vector3(4.2f, 0.75f, 1f) : new Vector3(0.75f, 4.2f, 1f);
        SpawnAnimeSprite(slashSprite, center, color,
            start, end, horizontal ? 0f : 90f, 0.30f);
        SpawnAnimeSprite(slashSprite, center + Vector3.forward * 0.04f,
            Color.Lerp(color, Color.white, 0.72f),
            start * 0.72f, end * 0.78f, horizontal ? 0f : 90f, 0.18f);
    }

    private void SpawnAnimeSprite(
        Sprite sprite,
        Vector3 position,
        Color color,
        Vector3 startScale,
        Vector3 endScale,
        float rotationZ,
        float life,
        float spin = 0f,
        Vector3 velocity = default,
        int sortingOrder = 100)
    {
        if (sprite == null || _mainCamera == null)
            return;

        var effectObject = new GameObject("Anime 2D Combat FX");
        effectObject.transform.SetParent(transform);
        effectObject.transform.position = position;
        effectObject.transform.rotation = _mainCamera.transform.rotation *
                                          Quaternion.Euler(0f, 0f, rotationZ);
        effectObject.transform.localScale = startScale;
        SpriteRenderer renderer = effectObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        PrototypeSpriteEffect effect = effectObject.AddComponent<PrototypeSpriteEffect>();
        effect.startScale = startScale;
        effect.endScale = endScale;
        effect.life = life;
        effect.spin = spin;
        effect.velocity = velocity;
    }

    private void SpawnEnergyRing(Vector3 position, Material material, float radius, float life)
    {
        var ringObject = new GameObject("Impact Energy Ring");
        ringObject.transform.SetParent(transform);
        ringObject.transform.position = position;
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 40;
        ring.material = material;
        ring.startWidth = 0.09f;
        ring.endWidth = 0.09f;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }
        PrototypeRingEffect effect = ringObject.AddComponent<PrototypeRingEffect>();
        effect.targetRadius = radius;
        effect.life = life;
    }

    private void TriggerCombatFeedback(EffectKind kind)
    {
        bool parry = kind == EffectKind.ParryHorizontal ||
                     kind == EffectKind.ParryVertical ||
                     kind == EffectKind.ParryKick;
        bool guard = kind == EffectKind.GuardHorizontal ||
                     kind == EffectKind.GuardVertical;
        bool dodge = kind == EffectKind.DodgeCrouch ||
                     kind == EffectKind.DodgeSide;
        bool heavy = kind == EffectKind.GuardBreak ||
                     kind == EffectKind.SwordTrade ||
                     kind == EffectKind.KickTrade;
        _cameraShakeStrength = parry ? 0.19f
            : heavy ? 0.17f
            : guard ? 0.10f
            : dodge ? 0.045f
            : 0.135f;
        _cameraShakeEnds = Time.time + (parry || heavy ? 0.22f : 0.14f);
        _screenFlashColor = parry ? new Color(0.08f, 0.92f, 1f, 0.26f)
            : guard ? new Color(1f, 0.62f, 0.08f, 0.20f)
            : dodge ? new Color(0.2f, 1f, 0.55f, 0.12f)
            : kind == EffectKind.VerticalHit ? new Color(0.68f, 0.18f, 1f, 0.20f)
            : new Color(1f, 0.10f, 0.03f, heavy ? 0.24f : 0.19f);
        _screenFlashEnds = Time.time + (parry || heavy ? 0.16f : 0.11f);
    }

    private void UpdateCameraFeedback()
    {
        if (_mainCamera == null)
            return;

        if (Time.time < _cameraShakeEnds)
        {
            float fade = Mathf.InverseLerp(_cameraShakeEnds, _cameraShakeEnds - 0.12f, Time.time);
            Vector3 shake = UnityEngine.Random.insideUnitSphere * (_cameraShakeStrength * fade);
            shake.z *= 0.35f;
            _mainCamera.transform.position = _cameraRestPosition + shake;
        }
        else
        {
            _mainCamera.transform.position = _cameraRestPosition;
        }
    }

    private void SpawnImpact(Vector3 position, Material material, float size)
    {
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        impact.name = "Impact";
        impact.transform.SetParent(transform);
        impact.transform.position = position;
        impact.transform.localScale = Vector3.one * 0.15f;
        Destroy(impact.GetComponent<Collider>());
        impact.GetComponent<Renderer>().material = material;
        PrototypePulse pulse = impact.AddComponent<PrototypePulse>();
        pulse.targetScale = size;
        pulse.life = 0.32f;
    }

    private void CreateMaterials()
    {
        _playerMaterial = CreateMaterial(new Color(0.05f, 0.42f, 1f), 0.55f, 0.75f);
        _enemyMaterial = CreateMaterial(new Color(0.92f, 0.08f, 0.07f), 0.55f, 0.75f);
        // Dark gunmetal hull plating instead of stone.
        _stoneDark = CreateMaterial(new Color(0.045f, 0.05f, 0.065f), 0.75f, 0.55f);
        _stoneLight = CreateMaterial(new Color(0.14f, 0.16f, 0.2f), 0.7f, 0.6f);
        // Amber energy trim, glowing, in place of the old flat gold accent.
        _goldMaterial = CreateMaterial(new Color(1f, 0.62f, 0.1f), 0.4f, 0.85f,
            new Color(1f, 0.5f, 0.05f) * 2.2f);
        _dangerMaterial = CreateMaterial(new Color(1f, 0.1f, 0.05f), 0.3f, 0.85f,
            new Color(1f, 0.08f, 0.02f) * 2.4f);
        _parryMaterial = CreateMaterial(new Color(0.1f, 1f, 1f), 0.3f, 0.9f,
            new Color(0.1f, 1f, 1f) * 2.4f);
        _dodgeMaterial = CreateMaterial(new Color(0.35f, 1f, 0.58f), 0.2f, 0.75f,
            new Color(0.3f, 1f, 0.5f) * 1.8f);
        _bladeCoreMaterial = CreateMaterial(Color.white, 0.1f, 0.95f, Color.white * 3f);
    }

    private void CreateAudio()
    {
        _audioSource = CreateAudioLayer(0.78f);
        _audioLayerA = CreateAudioLayer(0.92f);
        _audioLayerB = CreateAudioLayer(0.72f);

        // These low layers sit under the real CC0 foley clips and add weight.
        _slashSound = CreateProceduralSound("Slash Low Air", 185f, 52f, 0.28f, 0.52f, 0.66f);
        _guardSound = CreateProceduralSound("Shield Low Ring", 148f, 82f, 0.34f, 0.62f, 0.18f);
        _parrySound = CreateProceduralSound("Parry Resonance", 620f, 1480f, 0.38f, 0.46f, 0.08f);
        _dodgeSound = CreateProceduralSound("Dodge Air", 160f, 38f, 0.26f, 0.42f, 0.78f);
        _hitSound = CreateProceduralSound("Impact Sub", 92f, 42f, 0.34f, 0.74f, 0.42f);
    }

    private AudioSource CreateAudioLayer(float volume)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = volume;
        source.dopplerLevel = 0f;
        return source;
    }

    private static AudioClip CreateProceduralSound(
        string clipName,
        float startFrequency,
        float endFrequency,
        float duration,
        float volume,
        float noiseAmount)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        var random = new System.Random(clipName.GetHashCode());
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float frequency = Mathf.Lerp(startFrequency, endFrequency, t);
            phase += frequency / sampleRate * Mathf.PI * 2f;
            float tone = Mathf.Sin(phase) * (1f - noiseAmount);
            float noise = ((float)random.NextDouble() * 2f - 1f) * noiseAmount;
            float envelope = Mathf.Pow(1f - t, 2f) * Mathf.Min(1f, t * 28f);
            samples[i] = (tone + noise) * envelope * volume;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlaySound(AudioClip clip, float pitch)
    {
        PlayOnLayer(_audioSource, clip, pitch, 1f);
    }

    private static void PlayOnLayer(
        AudioSource source,
        AudioClip clip,
        float pitch,
        float volumeScale)
    {
        if (source == null || clip == null)
            return;

        source.pitch = pitch;
        source.PlayOneShot(clip, volumeScale);
    }

    private void PlayAttackSound(Motion motion)
    {
        if (motion == Motion.HorizontalSlash)
        {
            PlayOnLayer(_audioLayerA, _assetLibrary != null ? _assetLibrary.swordSlice : null,
                0.92f, 1f);
            PlayOnLayer(_audioLayerB, _assetLibrary != null ? _assetLibrary.swordDraw : null,
                0.78f, 0.58f);
            PlayOnLayer(_audioSource, _slashSound, 0.86f, 0.72f);
        }
        else if (motion == Motion.VerticalSlash)
        {
            PlayOnLayer(_audioLayerA,
                _assetLibrary != null ? _assetLibrary.swordSliceHeavy : null,
                0.82f, 1f);
            PlayOnLayer(_audioLayerB, _assetLibrary != null ? _assetLibrary.swordDraw : null,
                0.66f, 0.62f);
            PlayOnLayer(_audioSource, _slashSound, 0.70f, 0.86f);
        }
        else
        {
            PlayOnLayer(_audioLayerA,
                _assetLibrary != null ? _assetLibrary.bodyImpactMedium : null,
                0.72f, 0.35f);
            PlayOnLayer(_audioLayerB, _dodgeSound, 0.62f, 0.82f);
            PlayOnLayer(_audioSource, _hitSound, 0.58f, 0.38f);
        }
    }

    private void PlayHitSound(Motion motion)
    {
        if (motion == Motion.Kick)
        {
            PlayOnLayer(_audioLayerA,
                _assetLibrary != null ? _assetLibrary.bodyImpactHeavy : null,
                0.78f, 1f);
            PlayOnLayer(_audioLayerB,
                _assetLibrary != null ? _assetLibrary.bodyImpactMedium : null,
                0.92f, 0.82f);
            PlayOnLayer(_audioSource, _hitSound, 0.62f, 1f);
            return;
        }

        float pitch = motion == Motion.VerticalSlash ? 0.82f : 0.96f;
        PlayOnLayer(_audioLayerA, _assetLibrary != null ? _assetLibrary.swordHit : null,
            pitch, 1f);
        PlayOnLayer(_audioLayerB,
            _assetLibrary != null ? _assetLibrary.bodyImpactMedium : null,
            pitch * 0.92f, 0.78f);
        PlayOnLayer(_audioSource, _hitSound, pitch * 0.72f, 0.92f);
    }

    private void PlayGuardSound(Motion attack)
    {
        bool vertical = attack == Motion.VerticalSlash;
        PlayOnLayer(_audioLayerA,
            _assetLibrary != null
                ? (vertical ? _assetLibrary.shieldBlockHeavy : _assetLibrary.shieldBlock)
                : null,
            vertical ? 0.84f : 0.98f, 1f);
        PlayOnLayer(_audioLayerB,
            _assetLibrary != null
                ? (vertical ? _assetLibrary.shieldBlock : _assetLibrary.shieldBlockHeavy)
                : null,
            vertical ? 0.68f : 0.80f, 0.58f);
        PlayOnLayer(_audioSource, _guardSound, vertical ? 0.72f : 0.88f, 0.84f);
    }

    private void PlayGuardBreakSound()
    {
        PlayOnLayer(_audioLayerA, _assetLibrary != null ? _assetLibrary.guardBreak : null,
            0.78f, 1f);
        PlayOnLayer(_audioLayerB,
            _assetLibrary != null ? _assetLibrary.bodyImpactHeavy : null,
            0.68f, 0.88f);
        PlayOnLayer(_audioSource, _hitSound, 0.52f, 1f);
    }

    private void PlayParrySound(Motion attack)
    {
        float attackPitch = attack == Motion.VerticalSlash ? 0.88f
            : attack == Motion.Kick ? 0.78f : 1f;
        PlayOnLayer(_audioLayerA, _assetLibrary != null ? _assetLibrary.parryBell : null,
            attackPitch, 1f);
        PlayOnLayer(_audioLayerB,
            _assetLibrary != null ? _assetLibrary.shieldBlockHeavy : null,
            attackPitch * 1.08f, 0.86f);
        PlayOnLayer(_audioSource, _parrySound, attackPitch * 0.92f, 0.92f);
    }

    private void PlayTradeSound(bool includesKick)
    {
        PlayOnLayer(_audioLayerA,
            _assetLibrary != null
                ? (includesKick ? _assetLibrary.bodyImpactHeavy : _assetLibrary.shieldBlockHeavy)
                : null,
            includesKick ? 0.74f : 0.88f, 1f);
        PlayOnLayer(_audioLayerB,
            _assetLibrary != null ? _assetLibrary.swordHit : null,
            includesKick ? 0.82f : 1.04f, 0.9f);
        PlayOnLayer(_audioSource, _hitSound, 0.58f, 1f);
    }

    private static Material CreateMaterial(
        Color color, float metallic, float smoothness, Color? emission = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", emission.Value);
        }
        return material;
    }

    private void CreateGuiStyles()
    {
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 30,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        _centerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 19,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.75f, 0.25f) }
        };
        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 15,
            normal = { textColor = new Color(0.82f, 0.86f, 0.95f) }
        };
    }

    private void OnGUI()
    {
        if (_player == null || _enemy == null)
            return;

        if (_titleStyle == null)
            CreateGuiStyles();

        const float referenceWidth = 1600f;
        const float referenceHeight = 900f;
        float scale = Mathf.Min(Screen.width / referenceWidth, Screen.height / referenceHeight);
        Matrix4x4 oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * Mathf.Max(0.45f, scale));

        float width = Screen.width / Mathf.Max(0.45f, scale);
        float height = Screen.height / Mathf.Max(0.45f, scale);

        if (Time.time < _screenFlashEnds)
        {
            float flashFade = Mathf.Clamp01((_screenFlashEnds - Time.time) / 0.11f);
            GUI.color = new Color(_screenFlashColor.r, _screenFlashColor.g,
                _screenFlashColor.b, _screenFlashColor.a * flashFade);
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        DrawHealthBar(new Rect(70f, 58f, 470f, 34f), _player.hp / MaxHealth,
            new Color(0.06f, 0.48f, 1f), "PLAYER", true);
        DrawHealthBar(new Rect(width - 540f, 58f, 470f, 34f), _enemy.hp / MaxHealth,
            new Color(0.95f, 0.10f, 0.08f), "RIVAL", false);
        DrawGaugeBar(new Rect(70f, 99f, 470f, 12f),
            _player.defenseGauge / MaxDefenseGauge, true);
        DrawGaugeBar(new Rect(width - 540f, 99f, 470f, 12f),
            _enemy.defenseGauge / MaxDefenseGauge, false);

        GUI.Label(new Rect(width * 0.5f - 290f, 38f, 580f, 52f), _banner, _titleStyle);
        GUI.Label(new Rect(width * 0.5f - 350f, 91f, 700f, 34f), _detail, _centerStyle);

        GUI.color = new Color(0.025f, 0.035f, 0.065f, 0.92f);
        GUI.DrawTexture(new Rect(width * 0.5f - 385f, referenceHeight - 92f, 770f, 56f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(width * 0.5f - 380f, referenceHeight - 87f, 760f, 26f),
            "J HORIZONTAL   K VERTICAL   L KICK   SPACE GUARD (1.0)   F PARRY (0.5)", _smallStyle);
        GUI.Label(new Rect(width * 0.5f - 380f, referenceHeight - 63f, 760f, 22f),
            "S CROUCH DODGE   A / D SIDE DODGE   •   R RESTART", _smallStyle);

        if (_player.motion == Motion.Dead || _enemy.motion == Motion.Dead)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.74f);
            GUI.DrawTexture(new Rect(width * 0.5f - 300f, 300f, 600f, 180f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(width * 0.5f - 290f, 320f, 580f, 70f),
                _enemy.motion == Motion.Dead ? "VICTORY" : "DEFEAT", _titleStyle);
            GUI.Label(new Rect(width * 0.5f - 280f, 395f, 560f, 40f),
                "Press R to restart the duel", _centerStyle);
        }

        GUI.matrix = oldMatrix;
    }

    private void DrawHealthBar(Rect rect, float fraction, Color fillColor, string label, bool alignLeft)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        Rect inner = new Rect(rect.x + 4f, rect.y + 4f, (rect.width - 8f) * Mathf.Clamp01(fraction), rect.height - 8f);
        if (!alignLeft)
            inner.x = rect.xMax - 4f - inner.width;
        GUI.color = fillColor;
        GUI.DrawTexture(inner, Texture2D.whiteTexture);
        GUI.color = Color.white;

        string text = $"{label}   {Mathf.CeilToInt(fraction * MaxHealth)}";
        GUIStyle style = new GUIStyle(_labelStyle)
        {
            alignment = alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight
        };
        GUI.Label(new Rect(rect.x + 10f, rect.y - 1f, rect.width - 20f, rect.height), text, style);
    }

    private static void DrawGaugeBar(Rect rect, float fraction, bool alignLeft)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        float width = (rect.width - 4f) * Mathf.Clamp01(fraction);
        Rect fill = new Rect(alignLeft ? rect.x + 2f : rect.xMax - 2f - width,
            rect.y + 2f, width, rect.height - 4f);
        GUI.color = new Color(0.15f, 0.95f, 0.82f, 1f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}

public sealed class PrototypePulse : MonoBehaviour
{
    [NonSerialized] public float targetScale = 1f;
    [NonSerialized] public float life = 0.3f;
    private float _started;

    private void Start()
    {
        _started = Time.time;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _started) / life);
        transform.localScale = Vector3.one * Mathf.Lerp(0.15f, targetScale, Mathf.Sin(t * Mathf.PI));
        if (t >= 1f)
            Destroy(gameObject);
    }
}

public sealed class PrototypeBurstParticle : MonoBehaviour
{
    [NonSerialized] public Vector3 velocity;
    [NonSerialized] public float spin;
    [NonSerialized] public float life = 0.4f;
    private float _started;
    private Vector3 _initialScale;

    private void Start()
    {
        _started = Time.time;
        _initialScale = transform.localScale;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _started) / life);
        transform.position += velocity * Time.deltaTime;
        velocity += Vector3.down * (2.4f * Time.deltaTime);
        transform.Rotate(0f, 0f, spin * Time.deltaTime, Space.Self);
        transform.localScale = _initialScale * (1f - t);

        if (t >= 1f)
            Destroy(gameObject);
    }
}

public sealed class PrototypeSpriteEffect : MonoBehaviour
{
    [NonSerialized] public Vector3 startScale = Vector3.one;
    [NonSerialized] public Vector3 endScale = Vector3.one * 2f;
    [NonSerialized] public float life = 0.25f;
    [NonSerialized] public float spin;
    [NonSerialized] public Vector3 velocity;
    private SpriteRenderer _renderer;
    private float _started;
    private Color _startColor;

    private void Start()
    {
        _renderer = GetComponent<SpriteRenderer>();
        _started = Time.time;
        _startColor = _renderer.color;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _started) / life);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.localScale = Vector3.LerpUnclamped(startScale, endScale, eased);
        transform.position += velocity * Time.deltaTime;
        velocity *= Mathf.Exp(-4.5f * Time.deltaTime);
        transform.Rotate(0f, 0f, spin * Time.deltaTime, Space.Self);
        _renderer.color = new Color(_startColor.r, _startColor.g, _startColor.b,
            _startColor.a * (1f - t));
        if (t >= 1f)
            Destroy(gameObject);
    }
}

public sealed class PrototypeLineFade : MonoBehaviour
{
    [NonSerialized] public float life = 0.35f;
    private LineRenderer _line;
    private float _started;
    private float _startWidth;
    private float _endWidth;

    private void Start()
    {
        _line = GetComponent<LineRenderer>();
        _started = Time.time;
        _startWidth = _line.startWidth;
        _endWidth = _line.endWidth;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _started) / life);
        _line.startWidth = _startWidth * (1f - t);
        _line.endWidth = _endWidth * (1f - t);
        if (t >= 1f)
            Destroy(gameObject);
    }
}

public sealed class PrototypeRingEffect : MonoBehaviour
{
    [NonSerialized] public float targetRadius = 1f;
    [NonSerialized] public float life = 0.4f;
    private LineRenderer _line;
    private float _started;

    private void Start()
    {
        _line = GetComponent<LineRenderer>();
        _started = Time.time;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - _started) / life);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.08f, targetRadius, eased);
        _line.startWidth = _line.endWidth = 0.11f * (1f - t);
        if (t >= 1f)
            Destroy(gameObject);
    }
}

public sealed class GroundedFighterRig : MonoBehaviour
{
    private Animator _animator;
    private Transform _fighterRoot;
    private Transform _leftFoot;
    private Transform _rightFoot;
    private Vector3 _leftFootAnchor;
    private Vector3 _rightFootAnchor;
    private Quaternion _leftFootRotation;
    private Quaternion _rightFootRotation;
    private bool _ready;
    private Transform _rightHandTarget;
    private Transform _leftHandTarget;
    private Transform _kickFootTarget;
    public bool lockFeet = true;
    public bool lockRightHand;
    public bool lockLeftHand;
    public bool kickActive;
    public float crouchWeight;
    public float lateralWeight;
    public float lateralDirection = -1f;

    public void Configure(Animator animator, Transform fighterRoot)
    {
        _animator = animator;
        _fighterRoot = fighterRoot;
    }

    public void ConfigureCombatHands(Transform rightHandTarget, Transform leftHandTarget)
    {
        _rightHandTarget = rightHandTarget;
        _leftHandTarget = leftHandTarget;
    }

    public void ConfigureKickFoot(Transform kickFootTarget)
    {
        _kickFootTarget = kickFootTarget;
    }

    private void LateUpdate()
    {
        if (_ready || _animator == null || !_animator.isHuman || _fighterRoot == null)
            return;

        _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (_leftFoot == null || _rightFoot == null)
            return;

        _leftFootAnchor = _fighterRoot.InverseTransformPoint(_leftFoot.position);
        _rightFootAnchor = _fighterRoot.InverseTransformPoint(_rightFoot.position);
        _leftFootRotation = Quaternion.Inverse(_fighterRoot.rotation) * _leftFoot.rotation;
        _rightFootRotation = Quaternion.Inverse(_fighterRoot.rotation) * _rightFoot.rotation;
        _ready = true;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!_ready || !lockFeet || _animator == null || _fighterRoot == null)
            return;

        _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 1f);
        _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 1f);
        _animator.SetIKPosition(AvatarIKGoal.LeftFoot, _fighterRoot.TransformPoint(_leftFootAnchor));
        _animator.SetIKPosition(AvatarIKGoal.RightFoot, _fighterRoot.TransformPoint(_rightFootAnchor));
        _animator.SetIKRotation(AvatarIKGoal.LeftFoot, _fighterRoot.rotation * _leftFootRotation);
        _animator.SetIKRotation(AvatarIKGoal.RightFoot, _fighterRoot.rotation * _rightFootRotation);
        if (kickActive && _kickFootTarget != null)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0.2f);
            _animator.SetIKPosition(AvatarIKGoal.RightFoot, _kickFootTarget.position);
        }

        Vector3 bodyPosition = _animator.bodyPosition;
        bodyPosition -= _fighterRoot.up * (0.42f * crouchWeight);
        bodyPosition += _fighterRoot.right * (0.28f * lateralWeight * lateralDirection);
        _animator.bodyPosition = bodyPosition;

        float rightHandWeight = lockRightHand ? 1f : 0f;
        float leftHandWeight = lockLeftHand ? 1f : 0f;
        if (_rightHandTarget != null)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightHandWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rightHandWeight);
            _animator.SetIKPosition(AvatarIKGoal.RightHand, _rightHandTarget.position);
            _animator.SetIKRotation(AvatarIKGoal.RightHand, _rightHandTarget.rotation);
        }
        if (_leftHandTarget != null)
        {
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftHandWeight);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftHandWeight);
            _animator.SetIKPosition(AvatarIKGoal.LeftHand, _leftHandTarget.position);
            _animator.SetIKRotation(AvatarIKGoal.LeftHand, _leftHandTarget.rotation);
        }
    }
}

public enum SwordPose { Idle, PrepareHorizontal, PrepareVertical, StrikeHorizontal, StrikeVertical }

// Drives the sword pivot explicitly from the fighter's own axes - the same
// approach AssetShieldFollower uses for the shield - instead of sampling an
// imported clip's hand track. That's what makes the two attacks readable as
// distinct shapes: horizontal is a full left-to-right sweep, vertical is a
// straight overhead raise followed by a downward chop.
public sealed class AssetSwordFollower : MonoBehaviour
{
    private Transform _fighterRoot;
    private SwordPose _pose;
    private float _progress;

    public void Configure(Transform hand, Transform fighterRoot)
    {
        _fighterRoot = fighterRoot;
    }

    public void SetPose(SwordPose pose, float progress)
    {
        _pose = pose;
        _progress = Mathf.Clamp01(progress);
    }

    private void LateUpdate()
    {
        if (_fighterRoot == null)
            return;

        Vector3 forward = _fighterRoot.forward;
        Vector3 up = _fighterRoot.up;
        Vector3 right = _fighterRoot.right;

        // Blade rest position/pointing when nothing is happening - hangs at
        // the hip, angled forward-down.
        Vector3 position = _fighterRoot.position + up * 0.95f + right * 0.32f - forward * 0.05f;
        Vector3 bladeDirection = (up * 0.4f - forward * 0.6f).normalized;

        switch (_pose)
        {
            case SwordPose.PrepareHorizontal:
            {
                // Wind up by pulling the blade all the way out to the
                // fighter's own left, roughly level with the chest.
                float wind = Mathf.SmoothStep(0f, 1f, _progress);
                Vector3 restPos = _fighterRoot.position + up * 0.95f + right * 0.32f - forward * 0.05f;
                Vector3 leftPos = _fighterRoot.position + up * 1.3f - right * 0.95f + forward * 0.3f;
                position = Vector3.Lerp(restPos, leftPos, wind);
                bladeDirection = Vector3.Lerp((up * 0.4f - forward * 0.6f).normalized, -right, wind);
                break;
            }
            case SwordPose.StrikeHorizontal:
            {
                // Sweep a full 180 degrees from the left extreme through the
                // center to the right extreme.
                float swing = _progress;
                Vector3 leftPos = _fighterRoot.position + up * 1.3f - right * 0.95f + forward * 0.3f;
                Vector3 rightPos = _fighterRoot.position + up * 1.3f + right * 0.95f + forward * 0.3f;
                float arc = Mathf.Sin(swing * Mathf.PI) * 0.35f;
                position = Vector3.Lerp(leftPos, rightPos, swing) + forward * arc;
                bladeDirection = Vector3.Lerp(-right, right, swing).normalized;
                break;
            }
            case SwordPose.PrepareVertical:
            {
                // Raise the blade straight up above the head.
                float wind = Mathf.SmoothStep(0f, 1f, _progress);
                Vector3 restPos = _fighterRoot.position + up * 0.95f + right * 0.32f - forward * 0.05f;
                Vector3 overhead = _fighterRoot.position + up * 2.05f + forward * 0.2f;
                position = Vector3.Lerp(restPos, overhead, wind);
                bladeDirection = Vector3.Lerp((up * 0.4f - forward * 0.6f).normalized, up, wind);
                break;
            }
            case SwordPose.StrikeVertical:
            {
                // Chop straight down from overhead to a forward-low finish.
                float swing = _progress;
                Vector3 overhead = _fighterRoot.position + up * 2.05f + forward * 0.2f;
                Vector3 lowFinish = _fighterRoot.position + up * 0.4f + forward * 1.1f;
                position = Vector3.Lerp(overhead, lowFinish, swing);
                bladeDirection = Vector3.Lerp(up, (forward + up * 0.15f).normalized, swing);
                break;
            }
        }

        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, bladeDirection.normalized);
        float positionSpeed = _pose == SwordPose.Idle ? 20f : 40f;
        float rotationSpeed = _pose == SwordPose.Idle ? 20f : 46f;
        transform.position = Vector3.Lerp(transform.position, position,
            1f - Mathf.Exp(-positionSpeed * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation,
            1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
    }
}

public sealed class AssetShieldFollower : MonoBehaviour
{
    private Transform _hand;
    private Transform _fighterRoot;
    private bool _guarding;
    private bool _parrying;
    private float _progress;

    public void Configure(Transform hand, Transform fighterRoot)
    {
        _hand = hand;
        _fighterRoot = fighterRoot;
    }

    public void SetPose(bool guarding, bool parrying, float progress)
    {
        _guarding = guarding;
        _parrying = parrying;
        _progress = progress;
    }

    private void LateUpdate()
    {
        if (_hand == null || _fighterRoot == null)
            return;

        Vector3 up = _fighterRoot.up;
        Vector3 forward = _fighterRoot.forward;
        Vector3 left = -_fighterRoot.right;

        // The shield primitive's flat face normal runs along its local Y
        // axis, so aligning that with "forward" points the face squarely at
        // the opponent - the read a block needs.
        Quaternion faceForward = Quaternion.FromToRotation(Vector3.up, forward);

        Vector3 restPosition = _fighterRoot.position + up * 1.05f - forward * 0.1f + left * 0.42f;
        Quaternion restRotation = faceForward * Quaternion.AngleAxis(35f, forward);

        Vector3 guardPosition = _fighterRoot.position + up * 1.32f + forward * 0.5f;
        Quaternion guardRotation = faceForward;

        Vector3 position = restPosition;
        Quaternion rotation = restRotation;

        if (_guarding)
        {
            position = guardPosition;
            rotation = guardRotation;
        }
        else if (_parrying)
        {
            // Hold the guard face, then bat it outward with a fast yaw sweep
            // around the vertical axis - a deflect, not a lunge - and spring
            // back to guard.
            float sweep = Mathf.Sin(Mathf.Clamp01(_progress) * Mathf.PI);
            position = guardPosition + left * (0.55f * sweep) + forward * (0.25f * sweep);
            rotation = guardRotation * Quaternion.AngleAxis(100f * sweep, up);
        }

        float positionSpeed = _parrying ? 46f : 30f;
        float rotationSpeed = _parrying ? 52f : 34f;
        transform.position = Vector3.Lerp(transform.position, position,
            1f - Mathf.Exp(-positionSpeed * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation,
            1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
    }
}

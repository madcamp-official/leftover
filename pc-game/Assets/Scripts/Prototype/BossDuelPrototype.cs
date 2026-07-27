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
        public TrailRenderer swordTrail;
        public Transform kickTarget;
        public float dodgeDirection = -1f;
        // Gauge burned by the parry currently in its judging window. Refunded in full if the
        // parry actually blocks an attack; stays spent if the window whiffs.
        public float pendingParryCost;
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
    // Reinhardt shield model: holding basic guard continuously drains the gauge instead of a
    // flat activation cost, and it only refills while the shield is down (not guarding or
    // mid-parry). Parry has no flat cost either — it burns half of whatever is currently
    // banked, so it stays available but a full-gauge parry costs more than a low-gauge one.
    private const float GuardDrainPerSecond = 0.3f;
    private const float GaugeRecoveryPerSecond = 0.19f;
    private const float ParryMinGauge = 0.2f;
    private const float ParryWindow = 0.42f;
    // Long enough to read which attack is coming (the windup clip itself plus the
    // blade telegraph tint in AnimateAssetFighter), short enough to still demand a
    // fast reaction.
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
    private Material _groundMaterial;
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
    private Quaternion _cameraRestRotation;
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
        _player.pendingParryCost = 0f;
        _enemy.pendingParryCost = 0f;
        _player.motion = Motion.Idle;
        _enemy.motion = Motion.Idle;
        _player.root.localScale = _player.baseScale;
        _enemy.root.localScale = _enemy.baseScale;
        _player.root.position = new Vector3(-2.75f, 0f, 0f);
        _enemy.root.position = new Vector3(2.75f, 0f, 0f);
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
                if (_player.defenseGauge <= 0f)
                {
                    _banner = "NO GUARD GAUGE";
                    _detail = "Wait for the defense gauge to recover.";
                    return;
                }
                SetMotion(_player, Motion.Guard, 0.12f);
            }
            return;
        }

        if (_player.motion == Motion.Guard &&
            (_input == null || !_input.IsGuarding || _player.defenseGauge <= 0f))
        {
            if (_player.defenseGauge <= 0f)
            {
                _banner = "SHIELD DEPLETED";
                _detail = "Out of defense gauge — the shield drops.";
            }
            SetMotion(_player, Motion.Idle, 0.2f);
        }

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
            _enemy.defenseGauge > ParryMinGauge)
        {
            float enemyParryCost = _enemy.defenseGauge * 0.5f;
            _enemy.defenseGauge -= enemyParryCost;
            _enemy.pendingParryCost = enemyParryCost;
            _enemyPhase = EnemyPhase.Parrying;
            _enemyPhaseEnds = Time.time + AttackWindup + 0.48f;
            SetMotion(_enemy, Motion.Parry, AttackWindup + 0.48f);
            _detail = _repeatedPlayerAttack > 1
                ? "The rival recognized your repeated attack."
                : "The rival read your attack pattern.";
        }
        else if (_enemyPhase == EnemyPhase.Waiting &&
                 reaction < parryChance + guardChance &&
                 _enemy.defenseGauge > 0f)
        {
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
        // On top of the sword-tip trail (which only starts drawing once the blade is
        // already moving), fire the dedicated horizontal/vertical streak the instant the
        // strike begins so which attack is coming reads immediately, not just from the
        // trail shape as it develops.
        if (attack == Motion.HorizontalSlash || attack == Motion.VerticalSlash)
            SpawnDirectionalSlash(_player, attack == Motion.HorizontalSlash, 2.0f, 0.75f);
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
        if (!CanPlayerAct() || _player.defenseGauge <= ParryMinGauge)
        {
            _banner = "NO PARRY GAUGE";
            _detail = "Parry burns half your defense gauge — not enough saved up.";
            return;
        }

        float parryCost = _player.defenseGauge * 0.5f;
        _player.defenseGauge -= parryCost;
        _player.pendingParryCost = parryCost;
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
        UpdateDefenseGauge(_player);
        UpdateDefenseGauge(_enemy);
    }

    private static void UpdateDefenseGauge(Fighter fighter)
    {
        if (fighter == null)
            return;

        if (fighter.motion == Motion.Guard)
        {
            // Reinhardt shield: holding basic guard up continuously drains the gauge.
            fighter.defenseGauge = Mathf.Max(0f,
                fighter.defenseGauge - GuardDrainPerSecond * Time.deltaTime);
        }
        else if (fighter.motion != Motion.Parry)
        {
            // Only refills once the shield is fully down — not guarding, not mid-parry.
            fighter.defenseGauge = Mathf.Min(MaxDefenseGauge,
                fighter.defenseGauge + GaugeRecoveryPerSecond * Time.deltaTime);
        }
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
            defender.defenseGauge = Mathf.Min(MaxDefenseGauge,
                defender.defenseGauge + defender.pendingParryCost);
            defender.pendingParryCost = 0f;
            StaggerAttacker(attacker, playerAttacking, 1.2f);
            _banner = playerAttacking ? "RIVAL PARRIED" : "PERFECT PARRY";
            _detail = "Parry beats every attack. The gauge spent on it is refunded.";
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
                SpawnDirectionalSlash(_enemy, true, 2.0f, 0.75f);
                break;

            case EnemyPhase.TelegraphVertical:
                _enemyPhase = EnemyPhase.AttackingVertical;
                _enemyPhaseEnds = Time.time + AttackDuration(Motion.VerticalSlash);
                SetMotion(_enemy, Motion.VerticalSlash,
                    AttackDuration(Motion.VerticalSlash));
                PlayAttackSound(Motion.VerticalSlash);
                SpawnDirectionalSlash(_enemy, false, 2.0f, 0.75f);
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
        // The incoming spawn height (0.25) was tuned for the old procedural capsule
        // body, which needed lifting off the ground to sit at its pivot. The Mixamo
        // rig's own feet already sit at y=0 in its rest pose when the root is at
        // y=0, so keeping that +0.25 here just floats the whole character above
        // the ground plane with a visible gap under the feet.
        position.y = 0f;
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

        // Captured now, before the shield/sword get parented onto the hand bones
        // below, so this only ever contains the 3 body submeshes the Mixamo model
        // actually ships (Body/Head_Hands/Lower_Armor - confirmed via the live
        // SkinnedMeshRenderers in Play mode; there is no separate cape/cloak mesh).
        // GetComponentsInChildren called *after* CreateAssetShield/CreateAssetSword
        // would also sweep up the shield and sword meshes, and the recolor loop
        // further down would then flatten their own steel/wood materials to the
        // same tone as the torso - the actual cause of the "everything looks
        // brown-cloth-tinted" report, since the shield/sword names don't contain
        // "lower" or "head" and fell into that loop's default case. It also meant
        // the shield/sword (and the sword-tip TrailRenderer) were being converted to
        // URP/Lit *twice* - once here, once already in CreateAssetShield/
        // CreateAssetSword - which is wasted work and, for the trail's translucent
        // material, actively wrong.
        Renderer[] bodyRenderers = model.GetComponentsInChildren<Renderer>(true);

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
        // The forearm bone (elbow-to-wrist), not the hand/wrist bone. Rounds 3-4
        // both mounted the shield on LeftHand and only ever adjusted its position -
        // see the much longer explanation in CreateAssetShield for why that was
        // never going to fully solve this, and why the mount now lives here instead.
        Transform leftForearm = fighter.animator.isHuman
            ? fighter.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)
            : FindTransformContaining(model.transform, "LeftForeArm");
        Transform rightHand = fighter.animator.isHuman
            ? fighter.animator.GetBoneTransform(HumanBodyBones.RightHand)
            : FindTransformContaining(model.transform, "RightHand");
        Transform originalSword = FindTransformContaining(model.transform, "Sword");
        if (originalSword != null)
        {
            foreach (Renderer swordPart in originalSword.GetComponentsInChildren<Renderer>(true))
                swordPart.enabled = false;
        }
        CreateAssetShield(fighter, leftHand, leftForearm, root, teamMaterial);
        CreateAssetSword(fighter, rightHand, root);

        fighter.renderers = bodyRenderers;
        fighter.rendererBaseColors = new Color[fighter.renderers.Length];
        Color teamColor = facesRight ? new Color(0.18f, 0.55f, 1f) : new Color(1f, 0.23f, 0.18f);
        ConvertFighterMaterialsToUrp(fighter.renderers, teamColor);
        for (int i = 0; i < fighter.renderers.Length; i++)
        {
            // Only the "Body" submesh (the torso tabard/cloth - the closest thing
            // this model has to a separate cape/cloak, since it has no dedicated
            // cape geometry or material slot at all) carries the team color, and at
            // full saturation rather than a light wash. Head_Hands (face/hands) and
            // Lower_Armor (leg plates/boots) get fixed, realistic tones instead -
            // real skin and real gunmetal steel - with no team-color blend
            // whatsoever, replacing the old scheme that washed the whole body
            // (including the shield/sword, before the capture-order fix above) in
            // the same brown-ish team tint.
            string n = fighter.renderers[i].name.ToLowerInvariant();
            Color color;
            if (n.Contains("lower"))
                color = new Color(0.47f, 0.48f, 0.51f); // realistic gunmetal/steel leg armor
            else if (n.Contains("head"))
                color = new Color(0.80f, 0.62f, 0.49f); // realistic human skin tone
            else
                color = teamColor; // Body/tabard - the cape-substitute, 100% team color
            fighter.rendererBaseColors[i] = color;
        }
        fighter.bodyRenderer = fighter.renderers.Length > 0 ? fighter.renderers[0] : null;
        RestoreFighterAppearance(fighter);
        return fighter;
    }

    private void CreateAssetShield(
        Fighter fighter,
        Transform leftWrist,
        Transform leftElbow,
        Transform fighterRoot,
        Material teamMaterial)
    {
        Transform anchorBone = leftElbow != null ? leftElbow : leftWrist;
        if (anchorBone == null)
            return;

        // Rounds 3-6 all mounted the shield from a guessed axis constant (an
        // idle-yaw, then a wrist offset with the wrong sign, then "local Y is
        // the arm axis") and kept flipping between "elbow exposed" and "arm
        // pokes through the shield face"/"handle facing the opponent" as that
        // constant got re-tuned. The user's own read of round 6: the mesh's
        // two physical strap handles baked into the prefab - a single straight
        // hand-grip bar ("MedievalShieldHandle") and a pair of crossed straps
        // ("MedievalShieldHandle (1)"/"(2)") that read as an X-brace for the
        // forearm - should be the calibration points: grip bar to the wrist,
        // X-strap to the elbow. So instead of assuming which local axis is
        // "the arm axis" or "the front," we measure the real local-space
        // vector between those two named parts on the actual mesh and solve
        // the mount transform from that, plus the mesh's known thin axis
        // (local Z, confirmed via its ~1.17 x 1.17 x 0.13 localBounds) for the
        // face normal - flipped to point the strap side inward, since round 6
        // had that backwards. See the prefab-branch below for the actual fit;
        // this generic midpoint/axis math still backs the no-prefab primitive
        // fallback, which has no handle geometry to calibrate against.
        Vector3 worldElbow = leftElbow != null ? leftElbow.position : leftWrist.position;
        Vector3 worldWrist = leftWrist != null ? leftWrist.position : leftElbow.position;
        Vector3 armAxis = worldWrist - worldElbow;
        if (armAxis.sqrMagnitude < 0.0001f)
            armAxis = anchorBone.up; // degenerate rig fallback, keeps this from ever going NaN
        armAxis.Normalize();

        // "Outward" reference: the character's own forward direction, flattened
        // onto the plane perpendicular to the arm axis. If the forearm happens
        // to be pointing almost exactly forward (near-parallel to root.forward,
        // making that projection near-zero), fall back to the character's
        // outward lateral direction instead so the face normal never collapses.
        Vector3 outwardRef = fighterRoot.forward;
        Vector3 faceNormal = Vector3.ProjectOnPlane(outwardRef, armAxis);
        if (faceNormal.sqrMagnitude < 0.05f)
        {
            outwardRef = fighterRoot.right * (fighter.facesRight ? -1f : 1f);
            faceNormal = Vector3.ProjectOnPlane(outwardRef, armAxis);
        }
        faceNormal.Normalize();
        // Make sure the face actually points away from the body (toward the
        // front hemisphere) rather than back through the torso.
        if (Vector3.Dot(faceNormal, fighterRoot.forward) < 0f)
            faceNormal = -faceNormal;

        var mount = new GameObject("Left Hand Shield");
        mount.transform.SetParent(anchorBone);
        fighter.shieldPivot = mount.transform;
        const float shieldScale = 0.55f;

        if (_assetLibrary != null && _assetLibrary.shieldPrefab != null)
        {
            GameObject shield = Instantiate(_assetLibrary.shieldPrefab, mount.transform);
            shield.name = "Medieval Shield";
            shield.transform.localPosition = Vector3.zero;
            shield.transform.localRotation = Quaternion.identity;
            shield.transform.localScale = Vector3.one * shieldScale;

            Transform gripHandle = null;
            Vector3 crossHandleSum = Vector3.zero;
            int crossHandleCount = 0;
            foreach (Transform t in shield.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "MedievalShieldHandle")
                    gripHandle = t;
                else if (t.name.StartsWith("MedievalShieldHandle ("))
                {
                    crossHandleSum += t.localPosition;
                    crossHandleCount++;
                }
            }

            Quaternion worldShieldRotation;
            Vector3 worldMountPosition;
            if (gripHandle != null && crossHandleCount > 0)
            {
                Vector3 localHand = gripHandle.localPosition;
                Vector3 localElbow = crossHandleSum / crossHandleCount;
                Vector3 localAxis = (localElbow - localHand).normalized;

                // Align the mesh's face-normal axis (local +Z) to point the
                // strap side inward (-faceNormal, i.e. toward the arm/body) so
                // the blocking face reads outward toward the opponent - the
                // exact reverse of round 6, which pointed local +Z outward and
                // so showed the handles to the opponent instead.
                Quaternion alignFace = Quaternion.FromToRotation(Vector3.forward, -faceNormal);
                Vector3 rotatedAxis = alignFace * localAxis;
                // Twist around that now-aligned face axis until the mesh's own
                // measured grip->cross-strap line matches the real wrist->elbow
                // line, so the shield's span follows the actual forearm angle
                // in this pose rather than a guessed roll.
                float twistAngle = Vector3.SignedAngle(rotatedAxis, armAxis, -faceNormal);
                worldShieldRotation = Quaternion.AngleAxis(twistAngle, -faceNormal) * alignFace;

                Vector3 localMid = Vector3.Lerp(localHand, localElbow, 0.5f);
                Vector3 worldMid = Vector3.Lerp(worldWrist, worldElbow, 0.5f);
                // Small clearance so the mesh's inner face doesn't z-fight/
                // intersect the forearm mesh.
                worldMountPosition = worldMid - worldShieldRotation * (localMid * shieldScale) + faceNormal * 0.05f;
            }
            else
            {
                // Defensive fallback if the prefab's handle parts are ever
                // renamed/removed - same inward-strap fix, generic midpoint mount.
                worldShieldRotation = Quaternion.LookRotation(-faceNormal, armAxis);
                worldMountPosition = Vector3.Lerp(worldElbow, worldWrist, 0.5f) + faceNormal * 0.05f;
            }

            mount.transform.position = worldMountPosition;
            mount.transform.rotation = worldShieldRotation;

            Renderer[] renderers = shield.GetComponentsInChildren<Renderer>(true);
            ConvertFighterMaterialsToUrp(renderers, teamMaterial.color);
            fighter.shieldRenderer = renderers.Length > 0 ? renderers[0] : null;
        }
        else
        {
            Quaternion worldShieldRotation = Quaternion.LookRotation(faceNormal, armAxis);
            mount.transform.position = Vector3.Lerp(worldElbow, worldWrist, 0.5f) + faceNormal * 0.05f;
            mount.transform.rotation = worldShieldRotation;
            Transform shield = CreatePrimitive(PrimitiveType.Cylinder, "Shield Face", mount.transform,
                Vector3.zero, new Vector3(0.43f, 0.065f, 0.43f), teamMaterial);
            // The primitive cylinder's face-normal is its local Y (it's squashed
            // flat on Y), not Z like the asset mesh above, so it needs its own
            // extra 90-degree correction to line its face up with the mount's Z
            // (the derived faceNormal direction) instead of appearing edge-on.
            shield.localRotation = Quaternion.Euler(90f, 0f, 0f);
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

        // Parented directly to the hand bone (not a procedurally-driven pivot) so the
        // sword simply follows whatever the real animation clip (Idle/slash/etc) is
        // doing this frame.
        Transform mount = new GameObject("Right Hand Sword").transform;
        mount.SetParent(rightHand);
        mount.localPosition = new Vector3(0f, 0.05f, 0f);
        mount.localRotation = Quaternion.identity;
        fighter.swordPivot = mount;

        if (_assetLibrary != null && _assetLibrary.swordPrefab != null)
        {
            GameObject sword = Instantiate(_assetLibrary.swordPrefab, mount);
            sword.name = "Medieval Sword";
            // The prefab's own pivot sits right at the guard (measured via its
            // MeshRenderer.localBounds: grip runs from y=-0.294 to y=0 below the
            // pivot, blade above it), so the hand reads as gripping the decorative
            // guard cap instead of the grip itself. Shift the mesh up by half the
            // grip length so the grip's midpoint lands on the hand instead.
            sword.transform.localPosition = new Vector3(0f, 0.147f, 0f);
            // The prefab's default twist around the grip axis pointed the blade out
            // through the middle/ring-finger side of the fist instead of the
            // index/thumb side a real grip would use - rotate it 90 degrees around
            // that same axis to correct which way the blade faces from the hand.
            sword.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            sword.transform.localScale = Vector3.one;
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

        // A trail on the blade tip traces whatever arc the sword actually swings
        // through - horizontal cuts read as a horizontal streak, vertical cuts as a
        // vertical one, for free from the real animation clip's hand motion instead
        // of a separate canned effect.
        Transform tip = new GameObject("Sword Blade Tip").transform;
        tip.SetParent(mount);
        // +0.147 matches the grip-centering shift applied to the sword mesh above,
        // so the trail still starts right at the physical blade tip instead of
        // floating past it in empty air.
        tip.localPosition = new Vector3(0f, 1.68f + 0.147f, 0f);
        tip.localRotation = Quaternion.identity;
        TrailRenderer trail = tip.gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.32f;
        trail.minVertexDistance = 0.03f;
        trail.startWidth = 0.28f;
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
            {
                // Motion.Dead runs with a 99s "duration" (it's the match-over hold, not
                // a real clip length), so the normalized t above crawls and any tip
                // keyed off it is imperceptible for the first several seconds. Time the
                // fall off raw elapsed time instead so it actually plays out quickly.
                // The clip alone (no applyRootMotion) rotates the hips/spine backward
                // without ever lowering them, so the torso arcs into a backbend that
                // hovers at hip height instead of reaching the ground - rotating the
                // whole root additionally, hinged at its own origin (which sits at
                // ground level now that fighters spawn at y=0), brings the entire rig
                // down to lying flat regardless of what the clip's local joints do.
                float fallElapsed = Time.time - fighter.motionStarted;
                float fallProgress = Mathf.Clamp01(fallElapsed / 1.1f);
                actionRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Lerp(0f, -88f * side, Mathf.SmoothStep(0f, 1f, fallProgress)));
                break;
            }
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
        // Sword/shield are bone-parented to the actual hand transforms (see
        // CreateAssetSword/CreateAssetShield), so they simply move with whatever
        // the Mixamo clip's real arm animation is doing this frame - no procedural
        // hand IK override, no separate pose follower.
        bool horizontalAttack = fighter.motion == Motion.PrepareHorizontal ||
            fighter.motion == Motion.HorizontalSlash;
        bool verticalAttack = fighter.motion == Motion.PrepareVertical ||
            fighter.motion == Motion.VerticalSlash;
        bool swordActive = horizontalAttack || verticalAttack;
        if (fighter.swordTrail != null)
        {
            fighter.swordTrail.emitting = swordActive;
            // Recolor the trail itself to match the horizontal/vertical telegraph
            // hue (instead of a fixed per-team color) so the swing's own streak
            // reads as which attack is coming, not just the blade glow.
            if (horizontalAttack || verticalAttack)
            {
                Color trailColor = horizontalAttack ? TelegraphHorizontalColor : TelegraphVerticalColor;
                fighter.swordTrail.startColor = trailColor;
                fighter.swordTrail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            }
        }
        if (fighter.groundedRig != null)
        {
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
            // A held, motionless pose instead of a perpetually-looping mocap clip -
            // even subtle idle sway reads as constant jitter once the fighter is
            // otherwise still, so freeze on the clip's first frame.
            if (fighter.animator.layerCount > 1)
                fighter.animator.SetLayerWeight(1, 0f);
            fighter.animator.Play(Animator.StringToHash("Idle"), 0, 0f);
            fighter.animator.speed = 1f;
            fighter.animator.Update(0f);
            fighter.animator.speed = 0f;
            return;
        }

        if (motion == Motion.PrepareKick || motion == Motion.Kick)
        {
            fighter.animator.speed = 1f;
            if (fighter.animator.layerCount > 1)
                fighter.animator.SetLayerWeight(1, 0f);
            return;
        }

        if (motion == Motion.Dead)
        {
            // Every other combat state only needs the upper-body-masked layer
            // because legs are separately IK-driven while standing (grounding/
            // kick/dodge) - but nothing drives the legs during death, so playing
            // Dead there only collapsed the torso while the legs stayed on
            // whatever the base layer's Idle pose was. Play the same clip on the
            // base (full-body, unmasked) layer too so the whole body falls.
            fighter.animator.speed = 1f;
            fighter.animator.Play(Animator.StringToHash("Dead"), 0, 0f);
            if (fighter.animator.layerCount > 1 && fighter.animator.HasState(1, Animator.StringToHash("Dead")))
            {
                fighter.animator.SetLayerWeight(1, 1f);
                fighter.animator.Play(Animator.StringToHash("Dead"), 1, 0f);
            }
            return;
        }

        // Horizontal/vertical slash each have one continuous swing clip covering
        // both the windup and the strike - only (re)start it when entering the
        // windup (PrepareHorizontal/PrepareVertical). The follow-up
        // HorizontalSlash/VerticalSlash motion just lets that same clip keep
        // playing, so the swing reads as one motion instead of rewinding to
        // frame 0 partway through.
        Motion clipMotion = motion == Motion.PrepareHorizontal ? Motion.HorizontalSlash
            : motion == Motion.PrepareVertical ? Motion.VerticalSlash
            : motion;
        int state = Animator.StringToHash(clipMotion.ToString());
        if (fighter.animator.layerCount > 1 && fighter.animator.HasState(1, state))
        {
            fighter.animator.SetLayerWeight(1, 1f);
            fighter.animator.speed = 1f;
            bool continuesWindup = motion == Motion.HorizontalSlash || motion == Motion.VerticalSlash;
            // A short blend into the new clip instead of a hard frame-0 cut - these
            // are all real-time, speed=1 clips (unlike the frozen Idle hold or the
            // IK-driven Kick), so blending source→destination pose over a few frames
            // is safe here and removes the pose "pop" between combat states.
            if (!continuesWindup)
                fighter.animator.CrossFadeInFixedTime(state, 0.08f, 1, 0f);
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

        // Pokemon-battle style framing: camera sits close behind/beside the player so
        // only their upper body looms in the lower-left foreground, while the rival
        // stands further off to the right, fully visible and looking larger/closer
        // than the old wide establishing shot.
        // Y values dropped 0.25 to match the fighters' spawn height moving from
        // 0.25 (old procedural capsule pivot) down to 0 (grounded Mixamo rig).
        camera.transform.position = new Vector3(-4.4f, 1.75f, -1.9f);
        camera.transform.LookAt(new Vector3(1.7f, 0.75f, 0.3f));
        camera.fieldOfView = 42f;
        camera.backgroundColor = new Color(0.53f, 0.7f, 0.87f);
        // Ordinary sky: always clear to the skybox, but only override RenderSettings.skybox
        // when the library supplies a custom one. Otherwise this leaves whatever skybox the
        // Scene's own Lighting settings already have (Unity's default procedural sky), rather
        // than forcing a specific look here.
        Material skybox = _assetLibrary != null ? _assetLibrary.skyboxMaterial : null;
        camera.clearFlags = CameraClearFlags.Skybox;
        if (skybox != null)
            RenderSettings.skybox = skybox;
        _mainCamera = camera;
        _cameraRestPosition = camera.transform.position;
        _cameraRestRotation = camera.transform.rotation;

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

        // Just open outdoor ground with a light scatter of the Low Poly Nature pack's
        // trees/rocks around the duel - no built structure, no platform disc, per
        // feedback that the enclosed dungeon/SF sets and floor disc were unwanted
        // clutter. First pass: place the fight straight on the field.
        CreatePrimitive(PrimitiveType.Plane, "Ground", arena,
            Vector3.zero, new Vector3(6f, 1f, 6f), _groundMaterial);

        GameObject treeA = _assetLibrary != null ? _assetLibrary.natureTreeA : null;
        GameObject treeB = _assetLibrary != null ? _assetLibrary.natureTreeB : null;
        GameObject rock = _assetLibrary != null ? _assetLibrary.natureRock : null;
        ScatterNatureProp(treeA, arena, new Vector3(-7.5f, 0f, 3.5f), 60f);
        ScatterNatureProp(treeB, arena, new Vector3(7.5f, 0f, 4f), -40f);
        ScatterNatureProp(treeA, arena, new Vector3(-8f, 0f, -3f), 160f);
        ScatterNatureProp(treeB, arena, new Vector3(8.5f, 0f, -4.5f), 200f);
        ScatterNatureProp(rock, arena, new Vector3(-4.5f, 0f, 6f), 20f);
        ScatterNatureProp(rock, arena, new Vector3(5f, 0f, -6.5f), 100f);

        // More set-dressing from the same pack (same art style, so nothing clashes) in
        // the mid-ground ring between the tree line and the fight itself - that band
        // read as bare open ground per a prior QA pass. Kept outside roughly a 2.5m
        // radius of the origin so nothing overlaps the fighters or the camera framing.
        GameObject rockB = _assetLibrary != null ? _assetLibrary.natureRockB : null;
        GameObject grass = _assetLibrary != null ? _assetLibrary.natureGrass : null;
        GameObject shrub = _assetLibrary != null ? _assetLibrary.natureShrub : null;
        GameObject flower = _assetLibrary != null ? _assetLibrary.natureFlower : null;
        ScatterNatureProp(rockB, arena, new Vector3(6.5f, 0f, 2f), 75f);
        ScatterNatureProp(rockB, arena, new Vector3(-6f, 0f, -5.5f), 200f);
        ScatterNatureProp(shrub, arena, new Vector3(-3.5f, 0f, -2.6f), 15f);
        ScatterNatureProp(shrub, arena, new Vector3(3.8f, 0f, 2.6f), 95f);
        ScatterNatureProp(shrub, arena, new Vector3(-6.5f, 0f, 1.8f), 250f);
        ScatterNatureProp(grass, arena, new Vector3(-2.6f, 0f, 3.4f), 10f);
        ScatterNatureProp(grass, arena, new Vector3(3.2f, 0f, -3.6f), 140f);
        ScatterNatureProp(grass, arena, new Vector3(-4.2f, 0f, 0.6f), 60f);
        ScatterNatureProp(grass, arena, new Vector3(4.6f, 0f, 0.9f), 300f);
        ScatterNatureProp(grass, arena, new Vector3(0.4f, 0f, 5.4f), 190f);
        ScatterNatureProp(flower, arena, new Vector3(-2.2f, 0f, -3.4f), 40f);
        ScatterNatureProp(flower, arena, new Vector3(2.8f, 0f, 3.1f), 220f);
    }

    private static void ScatterNatureProp(GameObject prefab, Transform parent, Vector3 position, float yaw)
    {
        if (prefab == null)
            return;
        GameObject instance = Instantiate(prefab, parent);
        instance.transform.localPosition = position;
        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        ConvertNaturePropMaterialsToUrp(instance.GetComponentsInChildren<Renderer>(true));
    }

    // The nature pack's custom Built-in RP shaders render magenta ("missing shader") under
    // URP unless its separate URP sub-package is imported too. Rebuild each instance's
    // materials as URP/Lit instead - but unlike the fighter materials, these source
    // materials carry no usable _Color/_MainTex (the pack paints trunk/foliage color via
    // per-vertex color that a plain URP/Lit shader never samples), so blending toward a
    // light hint color leaves everything flat white. Infer a plausible tint from the
    // material name instead.
    private static void ConvertNaturePropMaterialsToUrp(Renderer[] renderers)
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
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
                    string n = source.name.ToLowerInvariant();
                    Color tint = n.Contains("leaf") || n.Contains("leaves") || n.Contains("foliage") ||
                                 n.Contains("grass") || n.Contains("shrub")
                        ? new Color(0.28f, 0.5f, 0.16f)
                        : n.Contains("trunk") || n.Contains("bark")
                            ? new Color(0.35f, 0.24f, 0.14f)
                            : n.Contains("rock")
                                ? new Color(0.45f, 0.44f, 0.42f)
                                : n.Contains("poppy") || n.Contains("flower")
                                    ? new Color(0.78f, 0.16f, 0.14f)
                                    : new Color(0.6f, 0.6f, 0.6f);
                    if (replacement.HasProperty("_BaseColor"))
                        replacement.SetColor("_BaseColor", tint);
                    if (replacement.HasProperty("_Color"))
                        replacement.SetColor("_Color", tint);
                    if (replacement.HasProperty("_Smoothness"))
                        replacement.SetFloat("_Smoothness", 0.1f);
                    converted[source] = replacement;
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
                SpawnHitImpactPrefab(_assetLibrary != null ? _assetLibrary.hitImpactHorizontal : null,
                    position, new Color(1f, 0.75f, 0.18f, 1f), size * 1.5f);
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
                SpawnHitImpactPrefab(_assetLibrary != null ? _assetLibrary.hitImpactVertical : null,
                    position + Vector3.up * 0.08f, new Color(1f, 0.38f, 0.84f, 1f), size * 1.5f);
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
                SpawnHitImpactPrefab(_assetLibrary != null ? _assetLibrary.hitImpactKick : null,
                    position, new Color(1f, 0.56f, 0.04f, 1f), size * 1.7f);
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
                SpawnHitImpactPrefab(_assetLibrary != null ? _assetLibrary.guardImpactVfx : null,
                    position, new Color(1f, 0.68f, 0.10f, 0.92f), size * 1.3f);
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
                SpawnHitImpactPrefab(_assetLibrary != null ? _assetLibrary.guardImpactVfx : null,
                    position, new Color(1f, 0.10f, 0.02f, 0.90f), size * 2.0f);
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

    private static Sprite _directionalBarSprite;

    // A large, unambiguous horizontal/vertical streak baked directly into a runtime
    // texture (a soft-edged bar: a bright band across the middle, feathered top/
    // bottom and feathered at both ends) instead of relying on a hunted-down "slash
    // mark" asset. The previously wired prefabs (slash5-HungNguyen "bolder" pack)
    // read as a small, ambiguous curved mark rather than a clean bar, and no amount
    // of scale multiplier fixed the *shape* complaint - only authoring the shape
    // ourselves guarantees "horizontal" and "vertical" are unmistakable. Square
    // texture with pixelsPerUnit == its own size, so a Sprite at localScale (sx, sy)
    // is exactly sx by sy world units - the same convention the old fallback sprite
    // path below used to rely on.
    private static Sprite GetDirectionalBarSprite()
    {
        if (_directionalBarSprite != null)
            return _directionalBarSprite;

        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            float vFromCenter = Mathf.Abs(v - 0.5f) * 2f;
            // Bright core band across the middle, feathering to fully transparent
            // above/below - this is what reads as "a bar" rather than a soft blob.
            float crossFade = Mathf.Clamp01(1f - Mathf.Pow(vFromCenter / 0.55f, 2.2f));
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float endFade = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.08f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(crossFade * endFade) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        _directionalBarSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        return _directionalBarSprite;
    }

    private void SpawnDirectionalSlash(Fighter attacker, bool horizontal, float scale = 1.15f, float life = 1.2f)
    {
        if (_mainCamera == null)
            return;

        Color tint = Color.Lerp(
            horizontal ? TelegraphHorizontalColor : TelegraphVerticalColor, Color.white, 0.22f);
        float side = attacker.facesRight ? 1f : -1f;
        // Centered on the fighter's own torso (not offset out to the side of them,
        // like the old small hit-spark placement) - a streak this large needs to
        // actually overlap the body to read as "the character's own swing".
        Vector3 center = attacker.root.position + new Vector3(0.1f * side, 1.1f, 0.22f);

        // Sized to run from roughly ankle height to well above the head (vertical)
        // or clear past both shoulders (horizontal) - explicitly large per feedback
        // that the previous slash mark read as a small localized burst rather than
        // a cut spanning the whole fighter.
        float length = 1.35f * scale;
        float thickness = 0.24f * scale;
        Vector3 startScale = new Vector3(length * 0.3f, thickness * 0.55f, 1f);
        Vector3 endScale = new Vector3(length, thickness, 1f);
        float rotationZ = horizontal ? 0f : 90f;

        Sprite bar = GetDirectionalBarSprite();
        SpawnAnimeSprite(bar, center, tint, startScale, endScale, rotationZ, life);
        // A tighter, brighter core streak layered on top for a hot-edge sword-flash
        // look, using the same bar shape at a smaller/whiter pass.
        SpawnAnimeSprite(bar, center + new Vector3(0f, 0f, 0.03f),
            Color.Lerp(tint, Color.white, 0.6f), startScale * 0.55f, endScale * 0.58f,
            rotationZ, life * 0.75f);
    }

    // Layers a real particle-based burst (Travis Game Assets "Hit Impact Effects
    // FREE") on top of the flat SpawnAnimeSprite quads below - those alone read as
    // a single flat decal, this adds actual volumetric smoke/shockwave/light-ray
    // particles for a much less prototype-y hit. Tinted to the same color already
    // used for that hit kind so the amber-horizontal/violet-vertical coding holds.
    private void SpawnHitImpactPrefab(GameObject prefab, Vector3 position, Color tint, float scale, float life = 1.3f)
    {
        if (prefab == null)
            return;

        Quaternion facing = _mainCamera != null
            ? Quaternion.LookRotation(_mainCamera.transform.forward)
            : Quaternion.identity;
        GameObject vfx = Instantiate(prefab, position, facing, transform);
        vfx.transform.localScale = Vector3.one * scale;
        foreach (ParticleSystem particles in vfx.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint);
        }
        Destroy(vfx, life);
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

        Vector3 targetPosition = _cameraRestPosition;
        Quaternion targetRotation = _cameraRestRotation;
        bool matchOver = _player.motion == Motion.Dead || _enemy.motion == Motion.Dead;
        if (matchOver)
        {
            // The close "Pokemon battle" rest camera sits right behind/beside the
            // player, and the death fall tips the whole fighter (shield included)
            // over toward roughly that side - without this the falling shield ends
            // up clipped right against the lens instead of the fall being visible.
            // Pull back to a wide, centered view for the last moment of the duel.
            float pullback = Mathf.Clamp01((Time.time - _roundEndedAt) / 0.8f);
            float blend = Mathf.SmoothStep(0f, 1f, pullback);
            Vector3 deathCameraPosition = new Vector3(0f, 3.2f, -6.5f);
            targetPosition = Vector3.Lerp(_cameraRestPosition, deathCameraPosition, blend);
            Quaternion deathLookRotation = Quaternion.LookRotation(
                new Vector3(0f, 0.6f, 0f) - deathCameraPosition);
            targetRotation = Quaternion.Slerp(_cameraRestRotation, deathLookRotation, blend);
        }

        if (Time.time < _cameraShakeEnds)
        {
            float fade = Mathf.InverseLerp(_cameraShakeEnds, _cameraShakeEnds - 0.12f, Time.time);
            Vector3 shake = UnityEngine.Random.insideUnitSphere * (_cameraShakeStrength * fade);
            shake.z *= 0.35f;
            _mainCamera.transform.position = targetPosition + shake;
        }
        else
        {
            _mainCamera.transform.position = targetPosition;
        }

        // Always explicitly set rotation (even to the same rest value) instead of
        // only touching it while matchOver - leaving it untouched the rest of the
        // time meant that once a death LookAt fired, the camera stayed aimed at
        // that death framing forever afterward, including into the next restarted
        // round, since nothing ever pointed it back at the normal rest orientation.
        _mainCamera.transform.rotation = targetRotation;
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
        _groundMaterial = CreateMaterial(new Color(0.24f, 0.42f, 0.16f), 0f, 0.15f);
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
            // Pitched down from the source recording's natural pitch - these are
            // small foley knife sounds by origin, and playing them back near their
            // recorded pitch reads as a thin/small blade rather than a two-handed
            // sword. The procedural sub-bass layer underneath is also turned up to
            // compensate with more low-end weight than the real clip has on its own.
            PlayOnLayer(_audioLayerA, _assetLibrary != null ? _assetLibrary.swordSlice : null,
                0.66f, 1f);
            PlayOnLayer(_audioLayerB, _assetLibrary != null ? _assetLibrary.swordDraw : null,
                0.58f, 0.58f);
            PlayOnLayer(_audioSource, _slashSound, 0.86f, 0.9f);
        }
        else if (motion == Motion.VerticalSlash)
        {
            PlayOnLayer(_audioLayerA,
                _assetLibrary != null ? _assetLibrary.swordSliceHeavy : null,
                0.6f, 1f);
            PlayOnLayer(_audioLayerB, _assetLibrary != null ? _assetLibrary.swordDraw : null,
                0.5f, 0.62f);
            PlayOnLayer(_audioSource, _slashSound, 0.70f, 1f);
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
            "J HORIZONTAL   K VERTICAL   L KICK   SPACE GUARD (drains/sec)   F PARRY (half of current gauge)", _smallStyle);
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
    private bool _ready;
    private Transform _kickFootTarget;
    public bool lockFeet = true;
    public bool kickActive;
    public float crouchWeight;
    public float lateralWeight;
    public float lateralDirection = -1f;

    public void Configure(Animator animator, Transform fighterRoot)
    {
        _animator = animator;
        _fighterRoot = fighterRoot;
    }

    public void ConfigureKickFoot(Transform kickFootTarget)
    {
        _kickFootTarget = kickFootTarget;
    }

    private void LateUpdate()
    {
        if (_animator == null || !_animator.isHuman || _fighterRoot == null)
            return;

        if (_leftFoot == null || _rightFoot == null)
        {
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (_leftFoot == null || _rightFoot == null)
                return;
        }

        // Keep tracking the live animated foot pose while standing normally, instead of
        // freezing a single snapshot forever — a one-time snapshot fights the continuously
        // looping Idle clip's natural leg motion (feet pinned while hips/knees keep moving
        // per the clip), which reads as floating/bent legs on more dynamic mocap sources.
        // Only hold the anchor still while a body-offset pose (crouch/dodge) is active, so
        // the hip can shift relative to planted feet without them sliding across the floor.
        // Also hold it while a kick is active - otherwise this captures the kicking foot's
        // IK-driven raised position as the new "standing" anchor, and since that anchor then
        // drives next frame's IK too, the raised pose becomes self-reinforcing and the foot
        // never comes back down once a kick gets interrupted mid-swing (e.g. by taking a hit).
        // Also hold it while lockFeet is off (Dead): without this, the death-fall rotation
        // keeps recapturing the feet's LOCAL offset from the tilting root every frame, so by
        // the time the character finishes falling the "standing" anchor has been overwritten
        // with a fallen-pose offset. Restarting then re-enables lockFeet and immediately
        // re-plants both feet using that stale fallen-pose anchor - legs snap into a
        // crouched/forward pose instead of the actual Idle stance.
        if (lockFeet && !kickActive && Mathf.Approximately(crouchWeight, 0f) && Mathf.Approximately(lateralWeight, 0f))
        {
            _leftFootAnchor = _fighterRoot.InverseTransformPoint(_leftFoot.position);
            _rightFootAnchor = _fighterRoot.InverseTransformPoint(_rightFoot.position);
        }
        _ready = true;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null || _fighterRoot == null)
            return;

        if (!_ready || !lockFeet)
        {
            // IK weights are sticky Animator state, not tied to any clip - if this
            // just stops calling SetIKPositionWeight (e.g. once Dead turns lockFeet
            // off), whatever weight/position was last set (mid-kick, mid-dodge...)
            // stays in effect forever, including across an R restart back to Idle.
            // That's what left a foot pinned in mid-air after dying mid-kick.
            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            return;
        }

        // Only the foot POSITION is IK-driven (for grounding during the crouch/dodge hip
        // offset below). Rotation is intentionally left to the Animator — continuously
        // reading a live foot rotation back into IK the same frame it was applied created
        // a feedback loop that showed up as the ankle spinning in place.
        _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 1f);
        _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
        _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 1f);
        _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        _animator.SetIKPosition(AvatarIKGoal.LeftFoot, _fighterRoot.TransformPoint(_leftFootAnchor));
        _animator.SetIKPosition(AvatarIKGoal.RightFoot, _fighterRoot.TransformPoint(_rightFootAnchor));
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
    }
}


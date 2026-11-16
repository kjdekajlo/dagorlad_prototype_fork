using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared unit logic: stats, attack slots, Warcraft-3 style combat loop, death and animation.
/// Side-specific behaviour (how targets are found, when to give up, where to stand when idle)
/// lives in <see cref="AllyUnit"/> and <see cref="EnemyUnit"/>.
///
/// Combat loop (runs every FixedUpdate):
///   no target  -> periodically AcquireTarget()
///   out of range -> chase (melee: to a free attack slot around the target; ranged: straight at it)
///   in range   -> stop, face target, start a swing when the cooldown is ready
///   swing      -> after <see cref="damagePoint"/> seconds the hit lands (melee) or a projectile is fired (ranged)
/// Ranges are measured edge to edge (minus both units' body radii), like in WC3.
/// </summary>
[RequireComponent(typeof(UnitMovement))]
public abstract class OurUnit : MonoBehaviour
{
    public enum AttackKind { Melee, Ranged }

    // ---- Definition (set on the prefab) ----
    public string unitName;
    public Sprite Icon;
    public Sprite InfoSprite;
    public Utility.UnitTypes type;
    public int minimumBuildingTier;
    public Amount[] cost;
    public GameObject bloodParticle;
    public Sprite circle;

    [Header("Combat")]
    public AttackKind attackKind = AttackKind.Melee;
    [Tooltip("Edge-to-edge distance at which the unit can hit its target.")]
    public float attackRange = 0.5f;
    [Tooltip("Seconds from the start of the swing until the hit lands / the projectile is released. Cooldown comes from the AttackSpeed stat.")]
    public float damagePoint = 0.3f;

    [Header("Ranged only")]
    [Tooltip("Optional. If empty a simple procedural projectile is used.")]
    public Projectile projectilePrefab;
    public float projectileSpeed = 7f;
    [Tooltip("Height of the projectile's arc (0 = straight line).")]
    public float projectileArc = 0.3f;

    // ---- Runtime state ----
    public Utility.UnitStatus status;
    public Structure home;
    public Transform Rally_Point;
    public StatsManager statsManager;
    public StatsManager currentTarget;
    public Vector2 lastMoveDirection;

    public delegate void OnSpawned(OurUnit sender);
    public event OnSpawned onSpawned;

    public bool IsDead => status == Utility.UnitStatus.Dead;
    public bool IsAttacking => currentTarget != null;
    public float BodyRadius => agent != null ? agent.radius : DEFAULT_BODY_RADIUS;

    protected UnitMovement Movement { get; private set; }
    protected Animator UnitAnimator { get; private set; }

    // Combat tuning
    private const float DEFAULT_BODY_RADIUS = 0.25f;
    private const float MIN_MELEE_RANGE = 0.3f;      // melee always reaches at least this far
    private const float RANGE_BUFFER = 0.3f;         // once engaged, target must leave range + buffer before we chase again
    private const float ACQUIRE_INTERVAL = 0.4f;     // how often an idle unit looks for a target
    private const float ATTACK_SLOT_GAP = 0.3f;      // distance from the body edge to the attack slots
    private const float FACE_LERP = 0.25f;

    private NavMeshAgent agent;
    private Vector2 previousPosition;

    private float cooldownTimer;   // time until next swing may start
    private float swingTimer;      // time until the current swing's hit lands
    private bool isSwinging;
    private bool isEngaged;        // standing in range of the target
    private float acquireTimer;

    // ---------------------------------------------------------------------
    // Attack slots: positions around this unit that melee attackers walk to,
    // so they surround the target instead of stacking on one side.
    // ---------------------------------------------------------------------

    [System.Serializable]
    public class Attacker
    {
        public Transform position;
        public Transform attacker;
    }
    public List<Attacker> attackersSlots = new();

    private static readonly Vector2[] AttackerSlotDirections =
    {
        new Vector2(-0.87f,  0.5f), new Vector2( 0.87f, -0.5f),
        new Vector2( 0f,     1f  ), new Vector2( 0f,    -1f  ),
        new Vector2(-0.87f, -0.5f), new Vector2( 0.87f,  0.5f),
    };

    /// <summary>
    /// Returns the slot reserved for <paramref name="attacker"/>, reserving a free one if needed.
    /// With a null attacker, only peeks for any free slot. Returns null when full.
    /// </summary>
    public Transform AvailableAttackerPosition(Transform attacker)
    {
        if (attacker != null)
        {
            var existing = attackersSlots.Find(x => x.attacker == attacker);
            if (existing != null) return existing.position;
        }

        var free = attackersSlots.Find(x => x.attacker == null);
        if (free == null) return null;
        if (attacker != null) free.attacker = attacker;
        return free.position;
    }

    public void RemoveAttacker(Transform attacker)
    {
        var slot = attackersSlots.Find(x => x.attacker == attacker);
        if (slot != null) slot.attacker = null;
    }

    private void CreateAttackerSlots()
    {
        var directions = (Vector2[])AttackerSlotDirections.Clone();
        Shuffle(new System.Random(), directions);
        float distance = BodyRadius + ATTACK_SLOT_GAP;
        foreach (var dir in directions)
        {
            var slot = new GameObject("attk_pos").transform;
            slot.parent = transform;
            slot.localPosition = dir.normalized * distance;
            attackersSlots.Add(new Attacker { position = slot, attacker = null });
        }
    }

    private static void Shuffle<T>(System.Random rng, T[] array)
    {
        for (int n = array.Length; n > 1;)
        {
            int k = rng.Next(n--);
            (array[n], array[k]) = (array[k], array[n]);
        }
    }

    // ---------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------

    protected virtual void Awake()
    {
        Movement = GetComponent<UnitMovement>();
        statsManager = GetComponent<StatsManager>();
        UnitAnimator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        statsManager.On_Death += HandleDeath;
    }

    protected virtual void Start()
    {
        onSpawned?.Invoke(this);
        CreateAttackerSlots();
        acquireTimer = Random.Range(0f, ACQUIRE_INTERVAL); // spread searches over frames
    }

    private void FixedUpdate()
    {
        if (IsDead) return;
        TickCombat(Time.fixedDeltaTime);
        UpdateWalkAnimation();
    }

    // ---------------------------------------------------------------------
    // Side-specific hooks
    // ---------------------------------------------------------------------

    /// <summary>Pick a new target, or null if there is nothing to fight.</summary>
    protected abstract StatsManager AcquireTarget();

    /// <summary>Extra rules for dropping a target that is still alive (left the area, leash, ...).</summary>
    protected virtual bool IsTargetStillValid(StatsManager target) => true;

    /// <summary>Where the unit goes once it has nothing to fight.</summary>
    protected abstract void ReturnToPost();

    // ---------------------------------------------------------------------
    // Orders
    // ---------------------------------------------------------------------

    /// <summary>
    /// Engage <paramref name="enemy"/>. Like WC3 auto-acquire, a unit that is already fighting
    /// keeps its current target unless <paramref name="force"/> is set.
    /// </summary>
    public void Attack(GameObject enemy, bool force = false)
    {
        if (enemy == null || IsDead) return;
        if (IsAttacking && !force) return;
        if (!enemy.TryGetComponent<StatsManager>(out var stats) || !IsAlive(stats)) return;
        SetTarget(stats);
    }

    /// <summary>Drop the current target and go back to post.</summary>
    public void StopAttack()
    {
        ClearTarget();
        ReturnToPost();
    }

    // ---------------------------------------------------------------------
    // Combat loop
    // ---------------------------------------------------------------------

    private void TickCombat(float dt)
    {
        if (cooldownTimer > 0f) cooldownTimer -= dt;

        if (currentTarget != null && !(IsAlive(currentTarget) && IsTargetStillValid(currentTarget)))
            OnTargetLost();

        if (currentTarget == null)
        {
            acquireTimer -= dt;
            if (acquireTimer > 0f) return;
            acquireTimer = ACQUIRE_INTERVAL;
            var found = AcquireTarget();
            if (found == null) return;
            SetTarget(found);
        }

        // Mid-swing: stand still and let the swing finish.
        if (isSwinging)
        {
            FaceTarget();
            swingTimer -= dt;
            if (swingTimer <= 0f)
            {
                isSwinging = false;
                ReleaseHit();
            }
            return;
        }

        float reach = EffectiveRange + (isEngaged ? RANGE_BUFFER : 0f);
        if (EdgeDistanceTo(currentTarget) > reach)
        {
            isEngaged = false;
            Chase();
            return;
        }

        if (!isEngaged)
        {
            isEngaged = true;
            Movement.Stop();
        }
        FaceTarget();

        if (cooldownTimer <= 0f) StartSwing();
    }

    private float EffectiveRange => attackKind == AttackKind.Melee ? Mathf.Max(attackRange, MIN_MELEE_RANGE) : attackRange;

    private void Chase()
    {
        if (attackKind == AttackKind.Melee && currentTarget.TryGetComponent<OurUnit>(out var targetUnit))
        {
            var slot = targetUnit.AvailableAttackerPosition(transform);
            Movement.Follow(slot != null ? slot : currentTarget.transform);
        }
        else
        {
            Movement.Follow(currentTarget.transform);
        }
    }

    private void StartSwing()
    {
        var attackSpeed = statsManager.GetStat(Utility.StatsTypes.AttackSpeed);
        float cooldown = attackSpeed != null ? attackSpeed.value : 1f;

        isSwinging = true;
        swingTimer = Mathf.Min(damagePoint, cooldown);
        cooldownTimer = cooldown;
        UnitAnimator.SetTrigger("isAttacking");
    }

    private void ReleaseHit()
    {
        if (currentTarget == null || !IsAlive(currentTarget)) return;

        var attackStat = statsManager.GetStat(Utility.StatsTypes.Attack);
        if (attackStat == null)
        {
            Debug.LogWarning(name + " tried to attack without an Attack stat");
            return;
        }
        float damage = attackStat.value;

        GameManager.Instance.GetComponent<AudioManager>().Play(unitName + "_attack");

        if (attackKind == AttackKind.Ranged)
        {
            Projectile.Fire(projectilePrefab, this, currentTarget, projectileSpeed, projectileArc,
                target => ApplyDamage(target, damage));
        }
        else if (EdgeDistanceTo(currentTarget) <= EffectiveRange + RANGE_BUFFER)
        {
            ApplyDamage(currentTarget, damage);
        }
    }

    private static void ApplyDamage(StatsManager target, float damage)
    {
        if (IsAlive(target)) target.TakeRawDamage(damage);
    }

    private void SetTarget(StatsManager target)
    {
        if (target == currentTarget) return;
        ReleaseSlotOnTarget();
        currentTarget = target;
        isEngaged = false;
        status = Utility.UnitStatus.Attacking;
    }

    private void ClearTarget()
    {
        ReleaseSlotOnTarget();
        currentTarget = null;
        isSwinging = false;
        isEngaged = false;
        status = Utility.UnitStatus.GoingToFlag;
    }

    private void OnTargetLost()
    {
        ClearTarget();
        var next = AcquireTarget();
        if (next != null) SetTarget(next);
        else ReturnToPost();
    }

    private void ReleaseSlotOnTarget()
    {
        if (currentTarget != null && currentTarget.TryGetComponent<OurUnit>(out var targetUnit))
            targetUnit.RemoveAttacker(transform);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public static bool IsAlive(StatsManager stats)
    {
        if (stats == null || stats.dead) return false;
        var health = stats.GetStat(Utility.StatsTypes.Health);
        return health == null || health.value > 0f;
    }

    protected float EdgeDistanceTo(Component other)
    {
        float otherRadius = other.TryGetComponent<OurUnit>(out var unit) ? unit.BodyRadius : 0f;
        return Vector2.Distance(transform.position, other.transform.position) - BodyRadius - otherRadius;
    }

    private void FaceTarget()
    {
        if (currentTarget == null) return;
        Vector2 toTarget = ((Vector2)currentTarget.transform.position - (Vector2)transform.position).normalized;
        lastMoveDirection = Vector2.Lerp(lastMoveDirection, toTarget, FACE_LERP);
        SetFacing(lastMoveDirection);
    }

    private void UpdateWalkAnimation()
    {
        Vector2 position = transform.position;
        if (position == previousPosition)
        {
            UnitAnimator.SetBool("isWalking", false);
            return;
        }

        Vector2 rawDirection = (position - previousPosition).normalized;
        previousPosition = position;
        UnitAnimator.SetBool("isWalking", true);

        if (!isEngaged)
        {
            lastMoveDirection = Vector2.Lerp(lastMoveDirection, rawDirection, 0.12f);
            SetFacing(lastMoveDirection);
        }
    }

    private void SetFacing(Vector2 direction)
    {
        UnitAnimator.SetFloat("x", direction.x);
        UnitAnimator.SetFloat("y", direction.y);
    }

    private void HandleDeath(GameObject sender)
    {
        ClearTarget();
        Movement.Stop();
        status = Utility.UnitStatus.Dead;
        UnitAnimator.SetBool("isDead", true);
        foreach (var col in GetComponents<CircleCollider2D>()) col.enabled = false;

        if (Rally_Point != null)
        {
            var rally = Rally_Point.GetComponent<RallyPoint>();
            rally.currentArmy.Remove(this);
            rally.ManageTargets();
        }

        GameManager.Instance.GetComponent<AudioManager>().Play(unitName + "_death");
        Destroy(sender, 3f);
    }

    public void Bleed()
    {
        var blood = Instantiate(bloodParticle, transform.position, Quaternion.identity);
        Destroy(blood, blood.GetComponent<ParticleSystem>().main.duration);
    }
}

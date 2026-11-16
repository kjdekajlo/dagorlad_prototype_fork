using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared unit logic: stats, attack slots, melee combat, death and animation.
/// Side-specific behaviour (where to go when not fighting, how to find targets)
/// lives in <see cref="AllyUnit"/> and <see cref="EnemyUnit"/>.
/// </summary>
[RequireComponent(typeof(UnitMovement))]
public abstract class OurUnit : MonoBehaviour
{
    // ---- Definition (set on the prefab) ----
    public string unitName;
    public Sprite Icon;
    public Sprite InfoSprite;
    public Utility.UnitTypes type;
    public int minimumBuildingTier;
    public Amount[] cost;
    public float attackRange;
    public GameObject bloodParticle;
    public Sprite circle;

    // ---- Runtime state ----
    public Utility.UnitStatus status;
    public Structure home;
    public Transform Rally_Point;
    public Timer attackTimer;
    public StatsManager statsManager;
    public StatsManager currentTarget;
    public Vector2 lastMoveDirection;

    public delegate void OnSpawned(OurUnit sender);
    public event OnSpawned onSpawned;

    public bool IsDead => status == Utility.UnitStatus.Dead;
    public bool IsAttacking => currentTarget != null;

    protected UnitMovement Movement { get; private set; }
    protected Animator UnitAnimator { get; private set; }

    // Combat tuning
    private const float TARGET_LOCK_DURATION = 0.5f;   // don't re-evaluate the same target faster than this
    private const float MELEE_STOP_TOLERANCE = 0.3f;   // stop walking when within attackRange + this

    private float targetLockTimer;
    private int attackId;
    private Vector2 previousPosition;

    // ---------------------------------------------------------------------
    // Attack slots: positions around this unit that attackers walk to.
    // ---------------------------------------------------------------------

    [System.Serializable]
    public class Attacker
    {
        public Transform position;
        public Transform attacker;
    }
    public List<Attacker> attackersSlots = new();

    private static readonly Vector3[] AttackerSlotOffsets =
    {
        new Vector3(-0.78f,  0.45f, 0f) * 0.8f,
        new Vector3( 0.78f, -0.45f, 0f) * 0.8f,
        new Vector3( 0.00f,  0.90f, 0f) * 0.8f,
        new Vector3( 0.00f, -0.90f, 0f) * 0.8f,
        new Vector3(-0.78f, -0.45f, 0f) * 0.8f,
        new Vector3( 0.78f,  0.45f, 0f) * 0.8f,
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
        var offsets = (Vector3[])AttackerSlotOffsets.Clone();
        Shuffle(new System.Random(), offsets);
        foreach (var offset in offsets)
        {
            var slot = new GameObject("attk_pos").transform;
            slot.parent = transform;
            slot.localPosition = offset;
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
        statsManager.On_Death += HandleDeath;
    }

    protected virtual void Start()
    {
        onSpawned?.Invoke(this);
        CreateAttackerSlots();
    }

    private void FixedUpdate()
    {
        if (currentTarget != null) targetLockTimer += Time.fixedDeltaTime;
        UpdateWalkAnimation();
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

        // Face the target while fighting, otherwise the walk direction; lerp to avoid jitter.
        if (currentTarget != null)
        {
            Vector2 toTarget = ((Vector2)currentTarget.transform.position - position).normalized;
            lastMoveDirection = Vector2.Lerp(lastMoveDirection, toTarget, 0.08f);
        }
        else
        {
            lastMoveDirection = Vector2.Lerp(lastMoveDirection, rawDirection, 0.12f);
        }
        SetFacing(lastMoveDirection);
    }

    private void SetFacing(Vector2 direction)
    {
        UnitAnimator.SetFloat("x", direction.x);
        UnitAnimator.SetFloat("y", direction.y);
    }

    private void HandleDeath(GameObject sender)
    {
        ReleaseSlotOnTarget();
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

    // ---------------------------------------------------------------------
    // Side-specific hooks
    // ---------------------------------------------------------------------

    /// <summary>Where the unit goes once it has nothing to fight.</summary>
    protected abstract void ReturnToPost();

    // ---------------------------------------------------------------------
    // Combat
    // ---------------------------------------------------------------------

    public void Attack(GameObject enemy)
    {
        if (enemy == null || IsDead) return;

        var enemyStats = enemy.GetComponent<StatsManager>();
        if (enemyStats == null) return;

        // Target lock: don't re-evaluate the same target too often.
        if (currentTarget == enemyStats && targetLockTimer < TARGET_LOCK_DURATION) return;

        var enemyUnit = enemy.GetComponent<OurUnit>();
        Transform attackPosition = enemyUnit != null ? enemyUnit.AvailableAttackerPosition(transform) : null;
        if (attackPosition == null) return;

        if (currentTarget != enemyStats)
        {
            ReleaseSlotOnTarget();
            currentTarget = enemyStats;
            targetLockTimer = 0f;
            status = Utility.UnitStatus.Attacking;
            Movement.Follow(attackPosition);
        }
        else if (Vector2.Distance(transform.position, enemy.transform.position) < attackRange + MELEE_STOP_TOLERANCE)
        {
            Movement.Stop();
        }
        else
        {
            Movement.Follow(attackPosition);
        }

        ScheduleNextHit();
    }

    public void StopAttack()
    {
        ReleaseSlotOnTarget();
        DestroyAttackTimer();

        currentTarget = null;
        status = Utility.UnitStatus.GoingToFlag;
        Movement.HoldPosition = false;
        ReturnToPost();
    }

    private void ScheduleNextHit()
    {
        DestroyAttackTimer();

        var attackSpeed = statsManager.GetStat(Utility.StatsTypes.AttackSpeed);
        if (attackSpeed == null)
        {
            Debug.LogWarning(gameObject.name + " has no AttackSpeed stat!");
            return;
        }

        attackTimer = gameObject.AddComponent<Timer>();
        attackTimer.AddTimer("Attacking" + attackId, attackSpeed.value, false);
        attackTimer.On_Duration_End += DealDamage;
    }

    public void DealDamage(Timer timer)
    {
        Destroy(timer);
        if (IsDead) return;

        if (currentTarget == null || currentTarget.dead)
        {
            currentTarget = null;
            return;
        }

        var attackStat = statsManager.GetStat(Utility.StatsTypes.Attack);
        if (attackStat == null)
        {
            UIManager.Instance.DialogWindow("Tried to deal damage without owning attack");
            return;
        }

        bool killed = false;
        if (Vector2.Distance(currentTarget.transform.position, transform.position) < attackRange)
        {
            lastMoveDirection = ((Vector2)currentTarget.transform.position - (Vector2)transform.position).normalized;
            SetFacing(lastMoveDirection);
            UnitAnimator.SetTrigger("isAttacking");
            GameManager.Instance.GetComponent<AudioManager>().Play(unitName + "_attack");
            Movement.HoldPosition = true;
            killed = currentTarget.TakeRawDamage(attackStat.value);
        }
        else
        {
            Movement.HoldPosition = false;
        }

        if (killed)
        {
            Debug.Log(gameObject.name + " - KILLED - " + currentTarget.name);
            currentTarget = null;
        }
        else
        {
            StartCoroutine(AttackAgain(0));
        }
    }

    private IEnumerator AttackAgain(float delay)
    {
        yield return new WaitForSeconds(delay);
        attackId++;
        if (currentTarget != null && !IsDead) Attack(currentTarget.gameObject);
    }

    private void ReleaseSlotOnTarget()
    {
        if (currentTarget != null && currentTarget.TryGetComponent<OurUnit>(out var targetUnit))
            targetUnit.RemoveAttacker(transform);
    }

    private void DestroyAttackTimer()
    {
        if (attackTimer != null) Destroy(attackTimer);
        attackTimer = null;
    }

    public void Bleed()
    {
        var blood = Instantiate(bloodParticle, transform.position, Quaternion.identity);
        Destroy(blood, blood.GetComponent<ParticleSystem>().main.duration);
    }
}

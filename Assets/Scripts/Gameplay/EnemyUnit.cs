using UnityEngine;

/// <summary>
/// AI unit guarding its spawn area, like a WC3 creep camp: attacks allies that come
/// within its leash radius, and gives up and walks home when pulled too far away.
/// </summary>
public class EnemyUnit : OurUnit
{
    [SerializeField] private float detectionRange = 5f;
    [Tooltip("Fallback leash radius. Overridden by the rally point's Point_Range collider if there is one.")]
    [SerializeField] private float leashRadius = 3f;
    [Tooltip("How far past the leash radius the unit may chase before giving up.")]
    [SerializeField] private float leashSlack = 1f;

    private Vector2 spawnPoint;

    protected override void Start()
    {
        spawnPoint = transform.position;
        leashRadius = ReadLeashRadiusFromRallyPoint(leashRadius);
        base.Start();
    }

    protected override StatsManager AcquireTarget()
    {
        // Pulled too far: walk home first, don't pick fights on the way.
        if (!IsInsideLeash(transform.position, leashSlack)) return null;

        AllyUnit closest = null;
        float closestDistance = detectionRange;

        foreach (var ally in FindObjectsOfType<AllyUnit>())
        {
            if (ally.IsDead || !IsInsideLeash(ally.transform.position, 0f)) continue;

            float distance = Vector2.Distance(transform.position, ally.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = ally;
            }
        }
        return closest != null ? closest.statsManager : null;
    }

    protected override bool IsTargetStillValid(StatsManager target) =>
        IsInsideLeash(transform.position, leashSlack) && IsInsideLeash(target.transform.position, leashSlack);

    protected override void ReturnToPost() => Movement.MoveTo(spawnPoint);

    private bool IsInsideLeash(Vector2 position, float slack) =>
        Vector2.Distance(position, spawnPoint) <= leashRadius + slack;

    private float ReadLeashRadiusFromRallyPoint(float fallback)
    {
        if (Rally_Point == null) return fallback;
        var range = Rally_Point.Find("Point_Range");
        if (range == null || !range.TryGetComponent<CircleCollider2D>(out var col)) return fallback;
        return col.radius * range.localScale.x;
    }
}

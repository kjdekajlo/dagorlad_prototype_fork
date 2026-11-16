using System.Collections;
using UnityEngine;

/// <summary>
/// AI unit guarding its spawn area. Periodically looks for the closest ally inside its
/// leash radius, attacks it, and walks back to its spawn point when pulled too far away.
/// </summary>
public class EnemyUnit : OurUnit
{
    [SerializeField] private float detectionRange = 5f;
    [SerializeField] private float searchInterval = 0.4f;
    [Tooltip("Fallback leash radius. Overridden by the rally point's Point_Range collider if there is one.")]
    [SerializeField] private float leashRadius = 3f;

    private const float RETARGET_MARGIN = 0.5f; // only switch to a new target this much closer

    private Vector2 spawnPoint;

    protected override void Start()
    {
        base.Start();
        spawnPoint = transform.position;
        leashRadius = ReadLeashRadiusFromRallyPoint(leashRadius);
        StartCoroutine(GuardLoop());
    }

    protected override void ReturnToPost()
    {
        Movement.MoveTo(spawnPoint);
    }

    private float ReadLeashRadiusFromRallyPoint(float fallback)
    {
        if (Rally_Point == null) return fallback;
        var range = Rally_Point.Find("Point_Range");
        if (range == null || !range.TryGetComponent<CircleCollider2D>(out var col)) return fallback;
        return col.radius * range.localScale.x;
    }

    private bool IsInsideLeash(Vector2 position) => Vector2.Distance(position, spawnPoint) <= leashRadius;

    private IEnumerator GuardLoop()
    {
        var wait = new WaitForSeconds(searchInterval);
        while (!IsDead)
        {
            yield return wait;
            if (IsDead) yield break;

            // Pulled out of our area: give up and walk home.
            if (!IsInsideLeash(transform.position))
            {
                if (IsAttacking) StopAttack();
                else ReturnToPost();
                continue;
            }

            // Current target ran out of our area.
            if (IsAttacking && !IsInsideLeash(currentTarget.transform.position))
                StopAttack();

            var closest = FindClosestAllyInLeash();
            if (closest == null) continue;

            float distance = Vector2.Distance(transform.position, closest.transform.position);
            if (!IsAttacking ||
                distance < Vector2.Distance(transform.position, currentTarget.transform.position) - RETARGET_MARGIN)
            {
                Attack(closest.gameObject);
            }
        }
    }

    private AllyUnit FindClosestAllyInLeash()
    {
        AllyUnit closest = null;
        float closestDistance = detectionRange;

        foreach (var ally in FindObjectsOfType<AllyUnit>())
        {
            if (ally.IsDead || !IsInsideLeash(ally.transform.position)) continue;

            float distance = Vector2.Distance(transform.position, ally.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = ally;
            }
        }
        return closest;
    }
}

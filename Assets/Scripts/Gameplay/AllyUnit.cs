using UnityEngine;

/// <summary>
/// Player-controlled unit. Fights enemies in (or just around) its rally point's area.
/// When a fight ends it stays where it is; it only re-forms when the flag is moved.
/// </summary>
public class AllyUnit : OurUnit
{
    [Tooltip("How far outside the flag's area a target may be before the unit gives up on it.")]
    [SerializeField] private float areaSlack = 1f;

    private RallyPoint Rally => Rally_Point != null ? Rally_Point.GetComponent<RallyPoint>() : null;

    protected override StatsManager AcquireTarget()
    {
        var rally = Rally;
        if (rally == null) return null;
        var enemy = rally.FindClosestEnemy(transform.position);
        return enemy != null && enemy.TryGetComponent<StatsManager>(out var stats) && IsAlive(stats) ? stats : null;
    }

    // Keep fighting while the target is in the flag's area or just outside it, so enemies
    // pushed over the edge (or hitting us from just outside) aren't ignored.
    protected override bool IsTargetStillValid(StatsManager target)
    {
        var rally = Rally;
        if (rally == null) return false;
        return rally.EnemiesInRange.Contains(target.gameObject) || rally.IsInArea(target.transform.position, areaSlack);
    }

    // Fight over: stand where we are. RallyPoint sends us back into formation when the flag moves.
    protected override void ReturnToPost() => Movement.Stop();
}

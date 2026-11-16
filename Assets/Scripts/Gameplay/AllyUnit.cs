using UnityEngine;

/// <summary>
/// Player-controlled unit. Fights enemies inside its rally point's range and
/// otherwise stands in formation at the flag.
/// </summary>
public class AllyUnit : OurUnit
{
    private RallyPoint Rally => Rally_Point != null ? Rally_Point.GetComponent<RallyPoint>() : null;

    protected override StatsManager AcquireTarget()
    {
        var rally = Rally;
        if (rally == null) return null;
        var enemy = rally.FindClosestEnemy(transform.position);
        return enemy != null && enemy.TryGetComponent<StatsManager>(out var stats) && IsAlive(stats) ? stats : null;
    }

    // Allies only fight what is inside their flag's area; chasing ends when the enemy leaves it.
    protected override bool IsTargetStillValid(StatsManager target)
    {
        var rally = Rally;
        return rally != null && rally.EnemiesInRange.Contains(target.gameObject);
    }

    protected override void ReturnToPost()
    {
        var rally = Rally;
        if (rally != null) Movement.MoveTo(rally.GetFormationPosition(this));
    }
}

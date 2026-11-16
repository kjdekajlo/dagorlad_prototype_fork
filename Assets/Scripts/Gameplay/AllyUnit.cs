using UnityEngine;

/// <summary>
/// Player-controlled unit. Fights whatever enters its rally point's range
/// (driven by <see cref="RallyPoint.ManageTargets"/>) and otherwise stands in formation at the flag.
/// </summary>
public class AllyUnit : OurUnit
{
    protected override void ReturnToPost()
    {
        // RallyPoint usually follows up with an exact formation slot; this is the fallback.
        if (Rally_Point != null) Movement.MoveTo(Rally_Point.position);
    }
}

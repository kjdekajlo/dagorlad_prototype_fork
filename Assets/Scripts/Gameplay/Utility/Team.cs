using UnityEngine;

/// <summary>Which side something belongs to. Serialized as an int: keep the numbers stable.</summary>
public enum Team
{
    Neutral = 0,
    Player = 1,
    Enemy = 2,
}

public static class Teams
{
    /// <summary>
    /// Unity tag used by trigger/collision filters (OnTrigger, OnCollision) for units of this team.
    /// Tags are only a physics filter; game logic should compare <see cref="Team"/> values.
    /// </summary>
    public static string Tag(Team team) => team switch
    {
        Team.Player => "Player",
        Team.Enemy => "Enemy",
        _ => "Untagged",
    };

    /// <summary>The side this team fights. Neutral fights nobody.</summary>
    public static Team Opponent(Team team) => team switch
    {
        Team.Player => Team.Enemy,
        Team.Enemy => Team.Player,
        _ => Team.Neutral,
    };

    public static bool AreHostile(Team a, Team b) =>
        a != Team.Neutral && b != Team.Neutral && a != b;

    /// <summary>Team of a GameObject (via its StatsManager), Neutral if it has none.</summary>
    public static Team Of(GameObject go) =>
        go != null && go.TryGetComponent<StatsManager>(out var stats) ? stats.team : Team.Neutral;
}

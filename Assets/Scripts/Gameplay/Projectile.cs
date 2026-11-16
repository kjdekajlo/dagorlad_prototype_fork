using System;
using UnityEngine;

/// <summary>
/// Prototype projectile for ranged units. Homes on its target (WC3 style: it can't miss
/// unless the target dies first), optionally flying in an arc, and calls back on impact.
///
/// Use <see cref="Fire"/>. If no prefab is given, a thin red line is created,
/// so ranged units work without any art. To use real art, make a prefab with a
/// SpriteRenderer + this component (sprite pointing right) and assign it on the unit.
/// </summary>
public class Projectile : MonoBehaviour
{
    private const float HIT_DISTANCE = 0.05f;
    private const float MAX_LIFETIME = 5f;

    private StatsManager target;
    private Action<StatsManager> onHit;
    private float speed;
    private float arcHeight;

    private Vector2 groundPosition;  // position without the arc offset
    private Vector2 lastTargetPosition;
    private float totalDistance;
    private float lifetime;

    public static Projectile Fire(Projectile prefab, OurUnit shooter, StatsManager target,
                                  float speed, float arcHeight, Action<StatsManager> onHit)
    {
        Vector3 origin = shooter.transform.position;
        Projectile projectile = prefab != null
            ? Instantiate(prefab, origin, Quaternion.identity)
            : CreateDefault(origin, shooter.GetComponent<SpriteRenderer>());

        projectile.target = target;
        projectile.onHit = onHit;
        projectile.speed = Mathf.Max(0.1f, speed);
        projectile.arcHeight = arcHeight;
        projectile.groundPosition = origin;
        projectile.lastTargetPosition = target.transform.position;
        projectile.totalDistance = Mathf.Max(0.01f, Vector2.Distance(origin, target.transform.position));
        return projectile;
    }

    private void Update()
    {
        lifetime += Time.deltaTime;
        if (lifetime > MAX_LIFETIME) { Destroy(gameObject); return; }

        bool targetAlive = OurUnit.IsAlive(target);
        if (targetAlive) lastTargetPosition = target.transform.position;

        Vector2 previous = transform.position;
        groundPosition = Vector2.MoveTowards(groundPosition, lastTargetPosition, speed * Time.deltaTime);

        // Parabolic arc: 0 at start and end, arcHeight in the middle.
        float remaining = Vector2.Distance(groundPosition, lastTargetPosition);
        float t = Mathf.Clamp01(1f - remaining / Mathf.Max(totalDistance, remaining));
        Vector2 position = groundPosition + Vector2.up * (arcHeight * 4f * t * (1f - t));
        transform.position = position;

        Vector2 velocity = position - previous;
        if (velocity.sqrMagnitude > 0.000001f)
            transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg);

        if (remaining <= HIT_DISTANCE)
        {
            if (targetAlive) onHit?.Invoke(target);
            Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------------
    // Procedural fallback visual
    // ---------------------------------------------------------------------

    private static Sprite defaultSprite;

    private static Projectile CreateDefault(Vector3 origin, SpriteRenderer shooterRenderer)
    {
        var go = new GameObject("Projectile");
        go.transform.position = origin;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = GetDefaultSprite();
        renderer.color = Color.red;
        if (shooterRenderer != null)
        {
            renderer.sortingLayerID = shooterRenderer.sortingLayerID;
            renderer.sortingOrder = shooterRenderer.sortingOrder + 10;
        }

        return go.AddComponent<Projectile>();
    }

    private static Sprite GetDefaultSprite()
    {
        if (defaultSprite != null) return defaultSprite;

        // Thin line: 16x1 px white texture -> 0.4 x 0.025 world units at 40 ppu, pointing right.
        // Tinted red by the SpriteRenderer.
        const int width = 16, height = 1;
        var texture = new Texture2D(width, height) { filterMode = FilterMode.Point };
        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();
        defaultSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 40f);
        return defaultSprite;
    }
}

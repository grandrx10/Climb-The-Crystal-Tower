using System;
using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    // --------------------------------------------------------------------------
    // Concrete emitters. Compose several in one BulletPatternSO for layered attacks.
    // Each Generate() call is deterministic given its seeded RNG, so every client rebuilds
    // the identical schedule. Runtime-only decisions (which live player to target, exactly
    // where a bubble bursts) are resolved later by the bullet's behaviour.
    // --------------------------------------------------------------------------

    /// <summary>
    /// Doc's first-boss attack: bubbles stream toward the arena at random angles within a
    /// spread. e.g. rate 8/s, spread ±25°, 8 damage, 10s duration.
    /// </summary>
    [Serializable]
    public class RandomSpreadEmitter : BulletEmitter
    {
        [Header("Fire rate")]
        public float bulletsPerSecond = 8f;
        public int bulletsPerVolley = 1;

        [Header("Aim")]
        [Tooltip("Half-angle of the random spread, in degrees, around the aim direction.")]
        public float spreadDegrees = 25f;

        [Header("Bullet")]
        public float speed = 4f;
        public int damage = 8;
        public float radius = 0.25f;
        public float visualSize = 1f;
        public bool destroyOnHit = true;
        public float lifetime = 6f;
        public Color color = new Color(0.4f, 0.8f, 1f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (bulletsPerSecond <= 0f) return;
            float interval = 1f / bulletsPerSecond;
            for (float t = 0f; t < duration; t += interval)
            {
                for (int i = 0; i < bulletsPerVolley; i++)
                {
                    float angle = RandRange(rng, -spreadDegrees, spreadDegrees);
                    output.Add(new BulletSpawnData
                    {
                        time = startTime + t,
                        originOffset = Vector2.zero,
                        velocity = Rotate(ctx.aim, angle) * speed,
                        damage = damage,
                        radius = radius,
                        visualSize = visualSize,
                        destroyOnHit = destroyOnHit,
                        lifetime = lifetime,
                        color = color,
                        sprite = sprite
                    });
                }
            }
        }

        public override string Describe() =>
            $"Spread stream: {bulletsPerSecond}/s, ±{spreadDegrees}°, {damage} dmg, {duration}s";
    }

    /// <summary>Evenly-spaced rings fired outward at a fixed cadence (bullet-hell staple).</summary>
    [Serializable]
    public class RadialBurstEmitter : BulletEmitter
    {
        public int bulletsPerRing = 16;
        public float ringsPerSecond = 1f;
        [Tooltip("Rotate each successive ring by this many degrees for a spiral.")]
        public float spinPerRing = 7f;

        public float speed = 3f;
        public int damage = 6;
        public float radius = 0.22f;
        public float visualSize = 1f;
        public bool destroyOnHit = true;
        public float lifetime = 6f;
        public Color color = new Color(1f, 0.5f, 0.3f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (ringsPerSecond <= 0f || bulletsPerRing <= 0) return;
            float interval = 1f / ringsPerSecond;
            int ringIndex = 0;
            for (float t = 0f; t < duration; t += interval, ringIndex++)
            {
                float baseAngle = spinPerRing * ringIndex;
                for (int i = 0; i < bulletsPerRing; i++)
                {
                    float angle = baseAngle + i * (360f / bulletsPerRing);
                    output.Add(new BulletSpawnData
                    {
                        time = startTime + t,
                        originOffset = Vector2.zero,
                        velocity = Rotate(Vector2.right, angle) * speed,
                        damage = damage,
                        radius = radius,
                        visualSize = visualSize,
                        destroyOnHit = destroyOnHit,
                        lifetime = lifetime,
                        color = color,
                        sprite = sprite
                    });
                }
            }
        }

        public override string Describe() =>
            $"Radial burst: {bulletsPerRing}×{ringsPerSecond}/s, spin {spinPerRing}°, {damage} dmg";
    }

    /// <summary>Tight aimed volley — a wall of piercing lasers straight at the arena.</summary>
    [Serializable]
    public class AimedVolleyEmitter : BulletEmitter
    {
        public int volleys = 3;
        public int bulletsPerVolley = 5;
        [Tooltip("Vertical spacing between bullets in a volley (world units).")]
        public float spacing = 0.6f;

        public float speed = 6f;
        public int damage = 10;
        public float radius = 0.2f;
        public float visualSize = 1.3f;
        public bool destroyOnHit = false; // piercing
        public float lifetime = 4f;
        public Color color = new Color(1f, 0.3f, 0.5f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (volleys <= 0) return;
            float step = volleys > 1 ? duration / volleys : 0f;
            Vector2 perpendicular = new Vector2(-ctx.aim.y, ctx.aim.x);
            for (int v = 0; v < volleys; v++)
            {
                float t = startTime + v * step;
                for (int i = 0; i < bulletsPerVolley; i++)
                {
                    float offset = (i - (bulletsPerVolley - 1) * 0.5f) * spacing;
                    output.Add(new BulletSpawnData
                    {
                        time = t,
                        originOffset = perpendicular * offset,
                        velocity = ctx.aim * speed,
                        damage = damage,
                        radius = radius,
                        visualSize = visualSize,
                        destroyOnHit = destroyOnHit,
                        lifetime = lifetime,
                        color = color,
                        sprite = sprite
                    });
                }
            }
        }

        public override string Describe() =>
            $"Aimed volley: {volleys}× {bulletsPerVolley} piercing, {damage} dmg";
    }

    // ==========================================================================
    //  Buble's kit (first boss). All three explode into an expanding red shock circle.
    // ==========================================================================

    /// <summary>
    /// Attack 1 — Exploding bubble. One bubble at a time flies in from the boss, then curves up
    /// or down and bursts on hitting a player or the arena edge, leaving an expanding red circle.
    /// A new bubble launches every <see cref="interval"/> seconds measured from the start of the
    /// previous launch, so a fast interval lets bubbles overlap before the earlier one bursts.
    /// </summary>
    [Serializable]
    public class ExplodingBubbleEmitter : BulletEmitter
    {
        [Header("Bubble")]
        [Tooltip("Horizontal travel speed of the incoming bubble (world units/s).")]
        public float speed = 4f;
        [Tooltip("Collision/visual radius of the flying bubble.")]
        public float bubbleRadius = 0.4f;
        public int damage = 8;
        [Tooltip("Seconds the bubble takes to sweep from horizontal to fully vertical once it starts curving.")]
        public float curveDuration = 0.4f;
        [Tooltip("How far into the arena the bubble travels before curving, as a fraction of the way to the centre. Randomised per bubble.")]
        [Range(0f, 1f)] public float minTravelFraction = 0.1f;
        [Range(0f, 1f)] public float maxTravelFraction = 0.9f;
        public Color bubbleColor = new Color(0.4f, 0.8f, 1f);

        [Header("Pacing")]
        [Tooltip("Seconds between the start of one bubble launching and the next. A new bubble spawns " +
                 "every interval even if the previous one hasn't burst yet, so they can overlap.")]
        public float interval = 1.5f;

        [Header("Explosion")]
        public float explosionRadius = 0.8f;
        [Tooltip("Seconds for the burst to expand from nothing to its full radius.")]
        public float explosionExpand = 0.25f;
        [Tooltip("Total lifetime of the burst before it fades away.")]
        public float explosionLifetime = 0.6f;
        public int explosionDamage = 8;
        public Color explosionColor = new Color(1f, 0.25f, 0.2f);
        [Tooltip("Art for the burst (null = plain circle). Auto-sized to the explosion radius and fades out with it.")]
        public Sprite explosionSprite;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (speed <= 0f) return;
            float rightEdge = ctx.arenaBounds.xMax;
            float entry = Mathf.Max(0f, ctx.muzzle.x - rightEdge);        // muzzle sits outside the box
            float inBoxSpan = Mathf.Max(0.01f, rightEdge - ctx.center.x); // right wall -> centre

            float t = 0f;
            int guard = 0;
            while (t < duration && guard++ < 200)
            {
                float frac = RandRange(rng, minTravelFraction, maxTravelFraction);
                float xDist = entry + frac * inBoxSpan;
                int dir = rng.Next(2) == 0 ? 1 : -1;                      // curve up or down

                var data = new BulletSpawnData
                {
                    time = startTime + t,
                    behavior = BulletBehavior.CurvedExploder,
                    originOffset = Vector2.zero,
                    velocity = ctx.aim * speed,
                    speed = speed,
                    damage = damage,
                    radius = bubbleRadius,
                    visualSize = 1f,
                    destroyOnHit = false,
                    explodeOnContact = true,
                    lifetime = 30f,                                       // safety; it bursts at the edge first
                    color = bubbleColor,
                    sprite = sprite,
                    travelTime = speed > 0f ? xDist / speed : 0f,
                    curveDuration = curveDuration,
                    curveDir = dir,
                    explosionRadius = explosionRadius,
                    explosionExpand = explosionExpand,
                    explosionLifetime = explosionLifetime,
                    explosionDamage = explosionDamage,
                    explosionColor = explosionColor,
                    explosionSprite = explosionSprite,
                };
                output.Add(data);

                t += Mathf.Max(0.1f, interval);
            }
        }

        public override string Describe() =>
            $"Exploding bubbles: every {interval}s, {damage}/{explosionDamage} dmg";
    }

    /// <summary>
    /// Attack 2 — Appearing exploding bubbles. Every <see cref="interval"/> seconds a bubble
    /// appears at a random spot, grows from nothing to full size (faded + harmless), holds
    /// (solid + harmful), then bursts into an expanding red circle.
    /// </summary>
    [Serializable]
    public class AppearingBubbleEmitter : BulletEmitter
    {
        [Header("Spawn cadence")]
        [Tooltip("Seconds between each appearing bubble.")]
        public float interval = 1f;

        [Header("Bubble")]
        [Tooltip("Final radius the bubble grows to.")]
        public float bubbleRadius = 0.4f;
        [Tooltip("Seconds to grow from 0 to full size (harmless + faded during this).")]
        public float growDuration = 0.5f;
        [Tooltip("Seconds it sits fully grown (harmful) before bursting.")]
        public float holdDuration = 0.5f;
        public int damage = 8;
        public Color bubbleColor = new Color(0.4f, 0.8f, 1f);

        [Header("Explosion")]
        public float explosionRadius = 0.8f;
        public float explosionExpand = 0.25f;
        public float explosionLifetime = 0.6f;
        public int explosionDamage = 8;
        public Color explosionColor = new Color(1f, 0.25f, 0.2f);
        [Tooltip("Art for the burst (null = plain circle). Auto-sized to the explosion radius and fades out with it.")]
        public Sprite explosionSprite;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (interval <= 0f) return;
            var inner = ctx.arenaInner;
            // Keep the whole bubble inside the box.
            float minX = inner.xMin + bubbleRadius, maxX = inner.xMax - bubbleRadius;
            float minY = inner.yMin + bubbleRadius, maxY = inner.yMax - bubbleRadius;

            for (float t = 0f; t < duration; t += interval)
            {
                Vector2 world = new Vector2(RandRange(rng, minX, maxX), RandRange(rng, minY, maxY));
                output.Add(new BulletSpawnData
                {
                    time = startTime + t,
                    behavior = BulletBehavior.GrowExplode,
                    originOffset = world - ctx.muzzle,     // system spawns at muzzle + originOffset
                    velocity = Vector2.zero,
                    damage = damage,
                    radius = bubbleRadius,
                    visualSize = 1f,
                    destroyOnHit = false,
                    explodeOnContact = false,
                    lifetime = growDuration + holdDuration + 5f,   // safety; it self-terminates on burst
                    color = bubbleColor,
                    sprite = sprite,
                    growDuration = growDuration,
                    holdDuration = holdDuration,
                    explosionRadius = explosionRadius,
                    explosionExpand = explosionExpand,
                    explosionLifetime = explosionLifetime,
                    explosionDamage = explosionDamage,
                    explosionColor = explosionColor,
                    explosionSprite = explosionSprite,
                });
            }
        }

        public override string Describe() =>
            $"Appearing bubbles: every {interval}s, grow {growDuration}s + hold {holdDuration}s, {explosionDamage} dmg";
    }

    /// <summary>
    /// Attack 3 — Targeted bubbles. Each cycle, <see cref="bubblesPerCycle"/> bubbles spawn at the
    /// boss muzzle, drift out to random points around the boss, then all lock onto one randomly
    /// chosen player and fly straight at them. A new cycle starts every <see cref="interval"/>
    /// seconds regardless of whether the previous cycle's bubbles have landed, so cycles overlap.
    /// Repeats for the duration.
    /// </summary>
    [Serializable]
    public class TargetedBubbleEmitter : BulletEmitter
    {
        [Header("Cycle")]
        public int bubblesPerCycle = 3;
        [Tooltip("Radius around the boss muzzle the bubbles drift out to before launching.")]
        public float bossRadius = 1f;
        [Tooltip("Seconds for the bubbles to drift out to their staging points.")]
        public float outwardTime = 0.4f;
        [Tooltip("Seconds between the start of successive cycles. A new batch spawns every interval " +
                 "even if the previous batch hasn't landed yet, so cycles overlap.")]
        public float interval = 2f;
        [Tooltip("Seconds between each individual bubble launching at the target within a cycle. " +
                 "0 means the whole batch fires together.")]
        public float bulletDelay = 0.35f;

        [Header("Bubble")]
        public float speed = 6f;
        public float bubbleRadius = 0.4f;
        public int damage = 8;
        public Color bubbleColor = new Color(0.6f, 0.5f, 1f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (bubblesPerCycle <= 0) return;
            float cycleLength = Mathf.Max(0.1f, interval);

            float t = 0f;
            int guard = 0;
            while (t < duration && guard++ < 200)
            {
                float select = (float)rng.NextDouble();   // one shared target for the whole cycle
                for (int i = 0; i < bubblesPerCycle; i++)
                {
                    // Uniform random point within bossRadius of the muzzle.
                    float ang = RandRange(rng, 0f, Mathf.PI * 2f);
                    float rad = bossRadius * Mathf.Sqrt((float)rng.NextDouble());
                    Vector2 stage = ctx.muzzle + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;

                    output.Add(new BulletSpawnData
                    {
                        time = startTime + t,
                        behavior = BulletBehavior.Targeted,
                        originOffset = Vector2.zero,        // spawn at the muzzle
                        velocity = Vector2.zero,
                        speed = speed,
                        damage = damage,
                        radius = bubbleRadius,
                        visualSize = 1f,
                        destroyOnHit = true,               // "it just does damage and disappears"
                        explodeOnContact = false,
                        lifetime = 20f,
                        color = bubbleColor,
                        sprite = sprite,
                        stagePoint = stage,
                        outwardTime = outwardTime,
                        launchDelay = i * Mathf.Max(0f, bulletDelay),
                        targetSelect = select,
                    });
                }
                t += cycleLength;
            }
        }

        public override string Describe() =>
            $"Targeted bubbles: {bubblesPerCycle}/cycle at one player, {damage} dmg";
    }
}

using System;
using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    // ==========================================================================
    //  Ryomi (boss 2) emitters. Each builds a deterministic schedule; the runtime
    //  resolves live targets/positions in Bullet (client-sided — every client
    //  hit-tests only its own dodge icon). New emitters auto-appear in the pattern
    //  "Add Emitter" dropdown.
    // ==========================================================================

    /// <summary>Marked Strike: barrages of crosshair→cross slashes on one player; +1 crosshair per barrage.</summary>
    [Serializable]
    public class MarkedStrikeEmitter : BulletEmitter
    {
        [Header("Barrage")]
        [Tooltip("Crosshairs in the FIRST barrage; each later barrage adds one more.")]
        public int initialCount = 1;
        [Tooltip("Seconds between barrages.")]
        public float barrageInterval = 1.5f;
        [Tooltip("Seconds between the spawn of each crosshair within a barrage.")]
        public float spawnStagger = 0.3f;

        [Header("Placement")]
        [Tooltip("The crosshair appears within this radius of the targeted player.")]
        public float spawnRadius = 5f;

        [Header("Cross slash")]
        [Tooltip("Crosshair fade-in (telegraph) before the slash triggers.")]
        public float telegraphDuration = 1.5f;
        [Tooltip("Half-length of each slash bar (its reach).")]
        public float armLength = 2f;
        [Tooltip("Bar thickness at full extension.")]
        public float thickness = 0.25f;
        public float growDuration = 0.25f;   // 0 -> full thickness
        public float holdDuration = 0f;
        public float fadeDuration = 0.1f;
        public int damage = 10;
        public Color color = new Color(1f, 0.85f, 0.4f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            float select = (float)rng.NextDouble();   // one target for the whole attack ("originally targeted")
            int barrage = 0;
            for (float t = 0f; t < duration; t += Mathf.Max(0.1f, barrageInterval), barrage++)
            {
                int count = Mathf.Max(1, initialCount + barrage);
                for (int i = 0; i < count; i++)
                {
                    float ang = RandRange(rng, 0f, Mathf.PI * 2f);
                    float dist = spawnRadius * Mathf.Sqrt((float)rng.NextDouble());
                    output.Add(new BulletSpawnData
                    {
                        time = startTime + t + i * Mathf.Max(0f, spawnStagger),
                        behavior = BulletBehavior.MarkedStrike,
                        hitShape = BulletHitShape.Cross,
                        originOffset = Vector2.zero,
                        damage = damage,
                        color = color,
                        sprite = sprite,
                        lifetime = telegraphDuration + growDuration + holdDuration + fadeDuration + 1f,
                        targetSelect = select,
                        spawnRadius = spawnRadius,
                        spawnAngle = ang,
                        spawnDist = dist,
                        rotationDeg = RandRange(rng, 0f, 360f),
                        telegraphDuration = telegraphDuration,
                        crossArmLength = armLength,
                        crossThickness = thickness,
                        growDuration = growDuration,
                        holdDuration = holdDuration,
                        fadeDuration = fadeDuration,
                    });
                }
            }
        }

        public override string Describe() => $"Marked Strike: from {initialCount} crosshair(s), +1 each barrage, every {barrageInterval}s";
    }

    /// <summary>Cut: a tall rectangle sliding right→left along the top or bottom (50/50), leaving afterimages.</summary>
    [Serializable]
    public class SlidingCutEmitter : BulletEmitter
    {
        [Header("Cadence")]
        public float interval = 2f;
        [Tooltip("Each successive slash comes this much sooner.")]
        public float intervalDecrease = 0.2f;
        public float intervalFloor = 0.4f;

        [Header("Slash")]
        public float speed = 6f;
        [Tooltip("Half-width (thin) of the vertical slash.")]
        public float slashHalfWidth = 0.15f;
        [Tooltip("Half-height (tall) of the slash — it hugs the chosen edge.")]
        public float slashHalfHeight = 1f;
        public int damage = 10;
        [Tooltip("Seconds between afterimage drops behind the slash.")]
        public float afterImageInterval = 0.05f;
        public Color color = new Color(0.85f, 0.9f, 1f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            Rect a = ctx.arenaBounds;
            float sp = Mathf.Max(0.1f, speed);
            float startX = a.xMax + slashHalfWidth + 0.5f;
            float endX = a.xMin - slashHalfWidth - 0.5f;
            float life = (startX - endX) / sp + 0.5f;

            float t = 0f, gap = Mathf.Max(intervalFloor, interval);
            int guard = 0;
            while (t < duration && guard++ < 500)
            {
                bool top = rng.Next(2) == 0;
                float y = top ? a.yMax - slashHalfHeight : a.yMin + slashHalfHeight;
                output.Add(new BulletSpawnData
                {
                    time = startTime + t,
                    behavior = BulletBehavior.SlidingCut,
                    hitShape = BulletHitShape.Box,
                    originOffset = new Vector2(startX, y) - ctx.muzzle,
                    velocity = new Vector2(-sp, 0f),
                    boxHalfExtents = new Vector2(slashHalfWidth, slashHalfHeight),
                    damage = damage,
                    color = color,
                    sprite = sprite,
                    afterImageInterval = afterImageInterval,
                    lifetime = life,
                    rotationDeg = 0f,
                });
                gap = Mathf.Max(intervalFloor, gap - intervalDecrease);
                t += gap;
            }
        }

        public override string Describe() => $"Cut: slash every {interval}s (−{intervalDecrease}s each), top/bottom";
    }

    /// <summary>Ricochet Bullets: a bullet aimed at a random player that bounces off the walls, then expires.</summary>
    [Serializable]
    public class RicochetEmitter : BulletEmitter
    {
        [Header("Fire")]
        public float interval = 2f;
        public float speed = 5f;
        [Tooltip("How long the bullet bounces before disappearing.")]
        public float bulletLifetime = 8f;
        public float bulletRadius = 0.3f;
        public int damage = 8;
        public Color color = new Color(1f, 0.7f, 0.4f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (interval <= 0f) return;
            for (float t = 0f; t < duration; t += Mathf.Max(0.1f, interval))
            {
                output.Add(new BulletSpawnData
                {
                    time = startTime + t,
                    behavior = BulletBehavior.Ricochet,
                    hitShape = BulletHitShape.Circle,
                    originOffset = Vector2.zero,
                    speed = speed,
                    radius = bulletRadius,
                    visualSize = 1f,
                    damage = damage,
                    lifetime = bulletLifetime,
                    color = color,
                    sprite = sprite,
                    targetSelect = (float)rng.NextDouble(),
                });
            }
        }

        public override string Describe() => $"Ricochet: every {interval}s, bounces {bulletLifetime}s";
    }

    /// <summary>Tracking Cut: a spinning crosshair that homes on a player and detonates a stationary cross,
    /// then repeats a little faster. The crosshair is harmless; the detonated cross deals the damage.</summary>
    [Serializable]
    public class TrackingCutEmitter : BulletEmitter
    {
        [Header("Crosshair")]
        public float fillDuration = 2f;
        [Tooltip("Seconds shaved off the fill each detonation.")]
        public float fillSpeedup = 0.2f;
        public float fillFloor = 0.4f;
        public float spinSpeedDeg = 10f;
        [Tooltip("Homing acceleration toward the target (higher = tighter tracking).")]
        public float homingAccel = 8f;
        [Tooltip("Max homing speed (allows overshoots and curved chases).")]
        public float homingMaxSpeed = 4f;

        [Header("Detonated cross")]
        public float armLength = 2f;
        public float thickness = 0.25f;
        public float growDuration = 0.25f;
        public float holdDuration = 0f;
        public float fadeDuration = 0.1f;
        public int damage = 10;
        public Color color = new Color(1f, 0.6f, 0.6f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            output.Add(MakeTracking((float)rng.NextDouble()));
        }

        /// <summary>One tracking-crosshair spawn (shared so LassoEmitter can reuse this config).</summary>
        public BulletSpawnData MakeTracking(float select)
        {
            return new BulletSpawnData
            {
                time = startTime,
                behavior = BulletBehavior.TrackingCut,
                hitShape = BulletHitShape.None,   // crosshair is harmless; its detonation cross hits
                originOffset = Vector2.zero,
                damage = damage,
                color = color,
                sprite = sprite,
                lifetime = duration,
                targetSelect = select,
                fillDuration = fillDuration,
                fillSpeedup = fillSpeedup,
                fillFloor = fillFloor,
                spinSpeedDeg = spinSpeedDeg,
                homingAccel = homingAccel,
                homingMaxSpeed = homingMaxSpeed,
                rotationDeg = 0f,
                crossArmLength = armLength,
                crossThickness = thickness,
                growDuration = growDuration,
                holdDuration = holdDuration,
                fadeDuration = fadeDuration,
            };
        }

        public override string Describe() => $"Tracking Cut: homing crosshair, detonates every {fillDuration}s (−{fillSpeedup}s)";
    }

    /// <summary>Lasso Tracking Cut: two tracking cuts on two players, plus a "lasso" that drags the whole
    /// defend box up and down (players pinned at the edges) for the round.</summary>
    [Serializable]
    public class LassoEmitter : BulletEmitter
    {
        [Header("Lasso (arena drag)")]
        [Tooltip("Vertical drag speed of the defend box (world units/sec).")]
        public float lassoSpeed = 3f;
        [Tooltip("How far up/down the box drags from its home position.")]
        public float lassoRange = 1.5f;

        [Header("Tracking cuts (2 players)")]
        public TrackingCutEmitter tracking = new TrackingCutEmitter();

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            // Invisible controller: drags the arena for the whole round.
            output.Add(new BulletSpawnData
            {
                time = startTime,
                behavior = BulletBehavior.Lasso,
                hitShape = BulletHitShape.None,
                originOffset = Vector2.zero,
                lifetime = duration,
                lassoSpeed = lassoSpeed,
                lassoRange = lassoRange,
            });

            // Two tracking crosshairs on two players (select 0 vs ~1 -> first vs last alive by seat;
            // the same player if solo). They share the nested tracking config.
            tracking.startTime = startTime;
            tracking.duration = duration;
            output.Add(tracking.MakeTracking(0f));
            output.Add(tracking.MakeTracking(0.999f));
        }

        public override string Describe() => $"Lasso: box drags ±{lassoRange} @ {lassoSpeed} + 2 tracking cuts";
    }
}

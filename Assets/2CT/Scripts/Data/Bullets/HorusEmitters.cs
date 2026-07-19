using System;
using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    // ==========================================================================
    //  Horus (boss 4) emitters. A horse guarding the apple of divine knowledge —
    //  apple + horse themed attacks. Most fit the normal bullet model (gravity
    //  apples, lane racers), but Joint Horse Rider switches the defend box into a
    //  side-scroller "ride mode" (jump over obstacles) via a controller bullet.
    //  All randomness is rolled here (deterministic per seed) so every client
    //  plays the identical attack.
    // ==========================================================================

    /// <summary>Shared apple maths (a gravity arc launched from the boss toward a battlefield point).</summary>
    internal static class HorusApple
    {
        public static float Rand(System.Random rng, float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        public static BulletSpawnData Arc(System.Random rng, in PatternContext ctx, float time,
                                          float gravity, float flightTime, int damage, float radius, Color color, Sprite sprite)
        {
            Vector2 s = ctx.muzzle;
            Rect inner = ctx.arenaInner;
            float tx = Rand(rng, inner.xMin, inner.xMax);
            float ty = Rand(rng, inner.yMin, inner.yMax);   // "fall down somewhere within the battlefield"
            float T = Mathf.Max(0.2f, flightTime);
            // Projectile motion: reach (tx,ty) at t=T, then keep falling out under gravity.
            float vx = (tx - s.x) / T;
            float vy = (ty - s.y) / T + 0.5f * gravity * T;
            return new BulletSpawnData
            {
                time = time, behavior = BulletBehavior.GravityApple, hitShape = BulletHitShape.Circle,
                originOffset = Vector2.zero, velocity = new Vector2(vx, vy), gravity = gravity,
                damage = damage, radius = radius, visualSize = 1f, color = color, sprite = sprite, lifetime = 8f,
            };
        }
    }

    /// <summary>Apple Chuck: an apple every <see cref="interval"/>s (accelerating) that arcs into the box and
    /// falls out under gravity.</summary>
    [Serializable]
    public class AppleChuckEmitter : BulletEmitter
    {
        [Header("Cadence")]
        [Tooltip("Seconds between apples at the start.")]
        public float interval = 1f;
        [Tooltip("Each successive apple comes this much sooner.")]
        public float intervalDecrease = 0.05f;
        public float intervalFloor = 0.3f;

        [Header("Apple")]
        public float gravity = 16f;
        [Tooltip("Time to reach the target point before it keeps falling.")]
        public float flightTime = 1f;
        public int damage = 6;
        public float radius = 0.25f;
        [Tooltip("Apple spin in flight (deg/sec; random direction per apple, 0 = none).")]
        public float appleSpinDeg = 360f;
        public Color color = new Color(0.9f, 0.2f, 0.2f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            float t = 0f, gap = Mathf.Max(intervalFloor, interval);
            int guard = 0;
            while (t < duration && guard++ < 500)
            {
                var apple = HorusApple.Arc(rng, ctx, startTime + t, gravity, flightTime, damage, radius, color, sprite);
                apple.spinSpeedDeg = (rng.Next(2) == 0 ? -1f : 1f) * appleSpinDeg;
                output.Add(apple);
                gap = Mathf.Max(intervalFloor, gap - intervalDecrease);
                t += gap;
            }
        }

        public override string Describe() => $"Apple Chuck: apple every {interval}s (−{intervalDecrease}s each)";
    }

    /// <summary>Explosive Apples (Phase 2): like Apple Chuck, but each apple bursts into a shower of upward
    /// apples when it hits the floor (which then fall back out).</summary>
    [Serializable]
    public class ExplosiveApplesEmitter : BulletEmitter
    {
        [Header("Cadence")]
        public float interval = 1f;
        public float intervalDecrease = 0.05f;
        public float intervalFloor = 0.3f;

        [Header("Thrown apple (explosive)")]
        public float gravity = 16f;
        public float flightTime = 1f;
        public int damage = 6;
        public float radius = 0.25f;
        public Color color = new Color(1f, 0.5f, 0.1f);
        [Tooltip("Sprite for the explosive apple that's thrown (null = placeholder). The burst spawns 'regular' apples.")]
        public Sprite explosiveSprite;

        [Tooltip("Bomb tumble in flight (deg/sec; random direction per bomb, 0 = none).")]
        public float bombSpinDeg = 360f;

        [Header("Burst")]
        [Tooltip("Regular apples spawned when the explosive apple hits the floor.")]
        public int burstCount = 6;
        public float burstUpSpeed = 8f;
        [Tooltip("± random vertical speed added per burst apple (so they don't all peak together).")]
        public float burstUpVariance = 3f;
        [Tooltip("Max |horizontal| speed given to burst apples (random per apple).")]
        public float burstSpeedX = 3f;
        [Tooltip("Radius of the burst apples (0 = same as the bomb). Burst apples also tumble.")]
        public float burstAppleRadius = 0.25f;
        [Tooltip("Sprite for the regular apples produced by the burst (null = placeholder).")]
        public Sprite regularSprite;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            float t = 0f, gap = Mathf.Max(intervalFloor, interval);
            int guard = 0;
            while (t < duration && guard++ < 500)
            {
                var apple = HorusApple.Arc(rng, ctx, startTime + t, gravity, flightTime, damage, radius, color,
                                           explosiveSprite != null ? explosiveSprite : sprite);
                apple.spinSpeedDeg = (rng.Next(2) == 0 ? -1f : 1f) * bombSpinDeg;   // the bomb tumbles too
                apple.burstOnFloor = true;
                apple.burstCount = burstCount;
                apple.burstUpSpeed = burstUpSpeed;
                apple.burstUpVariance = Mathf.Max(0f, burstUpVariance);
                apple.burstSpeedX = burstSpeedX;
                apple.burstRadius = Mathf.Max(0f, burstAppleRadius);
                apple.effectSprite = regularSprite;      // burst children use the regular apple sprite
                apple.randomSeed = rng.Next();           // deterministic burst velocities (same on every client)
                output.Add(apple);
                gap = Mathf.Max(intervalFloor, gap - intervalDecrease);
                t += gap;
            }
        }

        public override string Describe() => $"Explosive Apples: apple every {interval}s, bursts into {burstCount} on the floor";
    }

    /// <summary>Horse Race: down a random one of three lanes, an apple runs right→left; when it reaches the
    /// left a horse charges the same lane (faster). Both are occluded outside the box so they slide into frame.</summary>
    [Serializable]
    public class HorseRaceEmitter : BulletEmitter
    {
        [Header("Cadence")]
        [Tooltip("Seconds between cycles (start to start).")]
        public float raceInterval = 2f;
        [Tooltip("Number of horizontal lanes the battlefield is split into.")]
        public int laneCount = 3;

        [Header("Racers")]
        public float appleSpeed = 4f;
        public float horseSpeed = 6f;
        [Tooltip("Seconds after the apple before the chasing horse spawns behind it.")]
        public float horseDelay = 1f;
        [Tooltip("Horse walk-wobble tilt amplitude (deg; matches the free-roam feel, 0 = none).")]
        public float horseWobbleDeg = 7f;
        [Tooltip("Horse walk-wobble frequency (rad/sec; free roam uses 12).")]
        public float horseWobbleFreq = 12f;
        public int appleDamage = 8;
        public int horseDamage = 10;
        public float appleRadius = 0.3f;
        [Tooltip("Half-size of the horse's box hitbox.")]
        public Vector2 horseHalfExtents = new Vector2(0.5f, 0.4f);
        [Tooltip("How far outside the box the racers spawn/despawn (so they enter/leave off-screen).")]
        public float offMargin = 1.2f;
        public Sprite appleSprite;
        public Sprite horseSprite;
        public Color appleColor = new Color(0.9f, 0.2f, 0.2f);
        public Color horseColor = new Color(0.6f, 0.45f, 0.3f);

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (raceInterval <= 0f) return;
            Rect a = ctx.arenaBounds;
            int lanes = Mathf.Max(1, laneCount);
            float startX = a.xMax + offMargin;
            float endX = a.xMin - offMargin;
            float travel = startX - endX;

            for (float t = 0f; t < duration; t += Mathf.Max(0.1f, raceInterval))
            {
                int lane = rng.Next(lanes);
                float y = a.yMin + a.height * ((lane + 0.5f) / lanes);

                // Apple ROLLS down the lane right -> left (true rolling: spin matched to speed/radius;
                // moving left = counter-clockwise).
                output.Add(new BulletSpawnData
                {
                    time = startTime + t, behavior = BulletBehavior.Linear, hitShape = BulletHitShape.Circle,
                    originOffset = new Vector2(startX, y) - ctx.muzzle,
                    velocity = new Vector2(-Mathf.Max(0.1f, appleSpeed), 0f),
                    spinSpeedDeg = (Mathf.Max(0.1f, appleSpeed) / Mathf.Max(0.05f, appleRadius)) * Mathf.Rad2Deg,
                    damage = appleDamage, radius = appleRadius, visualSize = 1f,
                    color = appleColor, sprite = appleSprite, maskInside = true,
                    lifetime = travel / Mathf.Max(0.1f, appleSpeed) + 0.5f,
                });

                // The chasing horse spawns behind the apple after a short delay, wobbling as it runs.
                output.Add(new BulletSpawnData
                {
                    time = startTime + t + Mathf.Max(0f, horseDelay),
                    behavior = BulletBehavior.Linear, hitShape = BulletHitShape.Box,
                    originOffset = new Vector2(startX, y) - ctx.muzzle,
                    velocity = new Vector2(-Mathf.Max(0.1f, horseSpeed), 0f),
                    wobbleDeg = horseWobbleDeg, wobbleFreq = horseWobbleFreq,
                    damage = horseDamage, boxHalfExtents = horseHalfExtents, visualSize = 1f,
                    color = horseColor, sprite = horseSprite, maskInside = true,
                    lifetime = travel / Mathf.Max(0.1f, horseSpeed) + 0.5f,
                });
            }
        }

        public override string Describe() => $"Horse Race: apple→horse down a random lane every {raceInterval}s";
    }

    /// <summary>Joint Horse Rider: switches the box into ride mode (each player jumps their horse with W) and
    /// scrolls obstacles in from the right every 2–3s. Hard mode adds a matching top obstacle with a capped gap.</summary>
    [Serializable]
    public class JointHorseRiderEmitter : BulletEmitter
    {
        [Header("Ride feel (jump physics)")]
        public float rideGravity = 20f;
        public float rideJumpVelocity = 8f;
        [Tooltip("How long holding W keeps the jump floaty (variable jump height).")]
        public float rideMaxJumpHold = 0.25f;
        [Tooltip("Gravity multiplier while a held jump is still rising (<1 = higher jumps).")]
        public float rideLowGravityFactor = 0.45f;
        [Tooltip("The ridden horse sprite (null = brown placeholder).")]
        public Sprite horseSprite;
        [Tooltip("Extra battlefield height during the ride (the ceiling rises so held jumps have room to matter).")]
        public float arenaExtraHeight = 2f;
        [Tooltip("Horizontal steer speed multiplier while riding (1 = normal dodge speed).")]
        public float rideSpeedMultiplier = 1.5f;

        [Header("Obstacles")]
        public float minInterval = 2f;
        public float maxInterval = 3f;
        public float scrollSpeed = 5f;
        public float obstacleWidth = 0.5f;
        public float minHeight = 0.5f;
        [Tooltip("Tallest obstacle (from the floor). With the raised ceiling, make this taller than a tap-jump clears.")]
        public float maxHeight = 2.6f;
        public int obstacleDamage = 8;
        public float offMargin = 1.2f;
        public Sprite obstacleSprite;
        public Color obstacleColor = new Color(0.4f, 0.3f, 0.2f);

        [Header("Hard mode (Phase 2)")]
        [Tooltip("Also drop a top obstacle so players must thread the gap between them.")]
        public bool hardMode = false;
        public float topMinHeight = 0.4f;
        public float topMaxHeight = 1f;
        [Tooltip("Guaranteed vertical space left between the bottom and top obstacles (must fit a jump-through).")]
        public float minGap = 1.2f;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            // Controller: holds the arena in ride mode for the whole round.
            output.Add(new BulletSpawnData
            {
                time = startTime, behavior = BulletBehavior.HorseRide, hitShape = BulletHitShape.None,
                lifetime = duration,
                rideGravity = rideGravity, rideJumpVelocity = rideJumpVelocity,
                rideMaxJumpHold = rideMaxJumpHold, rideLowGravityFactor = rideLowGravityFactor,
                sprite = horseSprite, rideArenaExtraHeight = Mathf.Max(0f, arenaExtraHeight),
                rideSpeedMul = Mathf.Max(0.1f, rideSpeedMultiplier),
            });

            // Generate against the EXTENDED box: the ride raises the ceiling by arenaExtraHeight
            // (floor stays put), so top obstacles hang from — and the gap math uses — the raised top.
            Rect a = ctx.arenaBounds;
            a.yMax += Mathf.Max(0f, arenaExtraHeight);
            float startX = a.xMax + offMargin;
            float endX = a.xMin - offMargin;
            float travel = startX - endX;
            float life = travel / Mathf.Max(0.1f, scrollSpeed) + 0.5f;

            float t = 1f;   // small lead-in before the first obstacle
            int guard = 0;
            while (t < duration && guard++ < 500)
            {
                float h = RandRange(rng, minHeight, maxHeight);
                // Bottom obstacle, sitting on the floor.
                output.Add(MakeObstacle(startTime + t, startX - ctx.muzzle.x, a.yMin + h * 0.5f - ctx.muzzle.y, h, life));

                if (hardMode)
                {
                    // Cap the top obstacle so a gap of at least minGap always remains to pass through.
                    float maxTop = a.height - h - Mathf.Max(0.1f, minGap);
                    if (maxTop > 0.1f)
                    {
                        float th = Mathf.Min(RandRange(rng, topMinHeight, topMaxHeight), maxTop);
                        output.Add(MakeObstacle(startTime + t, startX - ctx.muzzle.x, a.yMax - th * 0.5f - ctx.muzzle.y, th, life));
                    }
                }
                t += RandRange(rng, minInterval, maxInterval);
            }
        }

        private BulletSpawnData MakeObstacle(float time, float offX, float offY, float height, float life)
        {
            return new BulletSpawnData
            {
                time = time,
                behavior = BulletBehavior.Linear, hitShape = BulletHitShape.Box,
                originOffset = new Vector2(offX, offY),
                velocity = new Vector2(-Mathf.Max(0.1f, scrollSpeed), 0f),
                boxHalfExtents = new Vector2(obstacleWidth * 0.5f, height * 0.5f),
                damage = obstacleDamage, visualSize = 1f,
                color = obstacleColor, sprite = obstacleSprite, maskInside = true, lifetime = life,
            };
        }

        public override string Describe() => hardMode
            ? $"Joint Horse Rider (Hard): two-part obstacles every {minInterval}–{maxInterval}s"
            : $"Joint Horse Rider: jump obstacles every {minInterval}–{maxInterval}s";
    }
}

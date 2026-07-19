using System;
using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    // ==========================================================================
    //  Marnu (boss 3) emitters. Marnu summons "spell pages" (1:1 squares) that
    //  fly out rotating, then detonate (a 0.3s squash/stretch + fade) and reveal a
    //  random spell. The four attacks below are just DELIVERY patterns for those
    //  pages; every spell's tuning lives in ONE shared asset (MarnuSpellBookSO,
    //  SpellBook_Marnu.asset) that all four attacks reference, so each of the six
    //  spells (Firebomb / Lightning / Water / Earth / Wind / Mana) is edited in a
    //  single place. All randomness is rolled here (deterministic per seed) so
    //  every client plays the identical attack.
    // ==========================================================================

    /// <summary>Tuning for all six spells + the page visual. Lives in the shared
    /// <see cref="MarnuSpellBookSO"/> asset that every Marnu attack references.</summary>
    [Serializable]
    public class MarnuSpellConfig
    {
        [Header("Which spells this attack can cast (uniform among the enabled ones)")]
        public bool useFirebomb = true;
        public bool useLightning = true;
        public bool useWater = true;
        public bool useEarth = true;
        public bool useWind = true;
        public bool useMana = true;

        [Header("Spell page (1:1 square)")]
        [Tooltip("Page size in world units (pages are 1:1 squares).")]
        public float pageWidth = 0.45f;
        [Tooltip("Contact damage of the page itself (its rotating box hitbox), on top of whatever its spell does.")]
        public int pageDamage = 8;
        [Tooltip("How fast the page spins while in flight (deg/sec).")]
        public float pageSpin = 200f;
        [Tooltip("Length of the squash/stretch + fade detonation before the effect is revealed.")]
        public float pageDetonateTime = 0.3f;
        [Tooltip("Page art (null = flat coloured rectangle placeholder). It's tinted the spell colour regardless.")]
        public Sprite pageSprite;

        [Header("Firebomb (red)")]
        public Color firebombColor = new Color(1f, 0.3f, 0.2f);
        public Sprite firebombSprite;
        [Tooltip("Explosion art shown when the bomb bursts (null = flat circle). Separate from the bomb art.")]
        public Sprite fbExplosionSprite;
        [Tooltip("Seconds between the fading trail images the bomb leaves behind (0 = no trail).")]
        public float fbTrailInterval = 0.05f;
        [Tooltip("How fast the bomb spins as it flies (deg/sec).")]
        public float fbSpinDeg = 360f;
        public float fbProjSpeed = 4f;
        public float fbProjRadius = 0.2f;
        public float fbExplosionRadius = 0.9f;
        public float fbExplosionExpand = 0.25f;
        public float fbExplosionLifetime = 0.8f;
        public int fbExplosionDamage = 10;

        [Header("Lightning (yellow)")]
        public Color lightningColor = new Color(1f, 0.9f, 0.25f);
        [Tooltip("Bolt art (kept at its own aspect; its bottom is the strike point). Null = tall placeholder.")]
        public Sprite lightningSprite;
        public float ltWarning = 1f;
        public float ltStrikeRadius = 0.7f;
        public float ltStrikeDuration = 0.3f;
        [Tooltip("Seconds the bolt takes to fade out after the strike ends (harmless while fading).")]
        public float ltFadeSeconds = 0.25f;
        [Tooltip("Placeholder bolt height as a multiple of the strike diameter (ignored when a bolt sprite is set).")]
        public float ltStrikeHeightMul = 5f;
        public int ltDamage = 10;

        [Header("Water (blue)")]
        public Color waterColor = new Color(0.3f, 0.6f, 1f, 1f);
        public Sprite waterSprite;
        [Tooltip("Fraction of the box height the flood rises per Water cast.")]
        public float wtRise = 0.1f;
        [Tooltip("Seconds the water takes to rise to its new level (0 = instant).")]
        public float wtRiseSeconds = 0.5f;
        [Tooltip("Maximum flood height (fraction of the box).")]
        public float wtMax = 0.5f;
        [Tooltip("Damage dealt while standing in the water (with the usual i-frames).")]
        public int wtDamage = 6;

        [Header("Earth (brown)")]
        public Color earthColor = new Color(0.55f, 0.4f, 0.25f);
        public Sprite earthSprite;
        [Tooltip("Pillar half-size (16:64 aspect → thin & tall). Full size = 2×.")]
        public Vector2 earthHalfExtents = new Vector2(0.4f, 1.6f);
        public float eaRiseSpeed = 3f;
        public int eaDamage = 10;

        [Header("Wind (white)")]
        public Color windColor = new Color(1f, 1f, 1f);
        public Sprite windSprite;
        [Tooltip("Horizontal force pulling the player icons (world units/sec).")]
        public float wiStrength = 3f;
        [Tooltip("How long the wind blows.")]
        public float wiDuration = 3f;
        [Tooltip("Seconds between the drifting wind-streak visuals (0 = none).")]
        public float wiStreakInterval = 0.2f;

        [Header("Mana (purple)")]
        public Color manaColor = new Color(0.7f, 0.4f, 1f);
        public Sprite manaSprite;
        [Tooltip("Projectiles in the fan (design: 5 → 0, ±step, ±2·step).")]
        public int mnCount = 5;
        [Tooltip("Angle step between fan projectiles (design: 15°).")]
        public float mnStep = 15f;
        public float mnProjSpeed = 5f;
        [Tooltip("Mana-shot half-size: x = half-LENGTH along travel, y = half-thickness (the art is a 64×16 left-pointing bolt).")]
        public Vector2 manaHalfExtents = new Vector2(0.4f, 0.1f);
        public int mnDamage = 8;
    }

    /// <summary>Shared spell-page builders used by all four Marnu attacks.</summary>
    public static class MarnuSpells
    {
        public static float Rand(System.Random rng, float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        /// <summary>Pick a spell among the ones enabled in the config (all six if none are).
        /// Weighted: Wind is HALF as likely as any other enabled spell.</summary>
        public static SpellType Roll(MarnuSpellConfig c, System.Random rng)
        {
            const int W = 2, WIND_W = 1;   // wind is half as common as the rest
            var pool = new List<(SpellType type, int weight)>(6);
            if (c.useFirebomb) pool.Add((SpellType.Firebomb, W));
            if (c.useLightning) pool.Add((SpellType.Lightning, W));
            if (c.useWater) pool.Add((SpellType.Water, W));
            if (c.useEarth) pool.Add((SpellType.Earth, W));
            if (c.useWind) pool.Add((SpellType.Wind, WIND_W));
            if (c.useMana) pool.Add((SpellType.Mana, W));
            if (pool.Count == 0) return SpellType.Firebomb;
            int total = 0;
            foreach (var e in pool) total += e.weight;
            int roll = rng.Next(total);
            foreach (var e in pool) { roll -= e.weight; if (roll < 0) return e.type; }
            return pool[pool.Count - 1].type;
        }

        /// <summary>A fresh spell page with the common visual fields filled in (spell fields added by <see cref="FillSpell"/>).</summary>
        public static BulletSpawnData NewPage(MarnuSpellConfig c, float time)
        {
            return new BulletSpawnData
            {
                time = time,
                behavior = BulletBehavior.SpellPage,
                hitShape = BulletHitShape.Box,   // a rotating rectangular hitbox that matches the page visual
                boxHalfExtents = new Vector2(c.pageWidth * 0.5f, c.pageWidth * 0.5f),   // 1:1 square page
                pageDamage = c.pageDamage,
                spinSpeedDeg = c.pageSpin,
                pageDetonateTime = c.pageDetonateTime,
                sprite = c.pageSprite,
                visualSize = 1f,
                lifetime = 40f,
            };
        }

        /// <summary>Stamp the chosen spell's colour + parameters onto a page (and pre-roll any per-cast randomness).</summary>
        public static void FillSpell(ref BulletSpawnData d, MarnuSpellConfig c, SpellType spell, System.Random rng, in PatternContext ctx)
        {
            d.spell = spell;
            // Lightning (x,y) and Earth (x only) strike at this pre-rolled random point inside the
            // battlefield — independent of where the page itself detonates.
            Rect inner = ctx.arenaInner;
            d.effectPoint = new Vector2(Rand(rng, inner.xMin, inner.xMax), Rand(rng, inner.yMin, inner.yMax));

            switch (spell)
            {
                case SpellType.Firebomb:
                    d.color = c.firebombColor; d.effectSprite = c.firebombSprite;
                    d.projSpeed = c.fbProjSpeed; d.radius = c.fbProjRadius;
                    d.afterImageInterval = c.fbTrailInterval; d.effectSpinDeg = c.fbSpinDeg;
                    d.explosionRadius = c.fbExplosionRadius; d.explosionExpand = c.fbExplosionExpand;
                    d.explosionLifetime = c.fbExplosionLifetime; d.explosionDamage = c.fbExplosionDamage;
                    d.explosionColor = c.firebombColor; d.explosionSprite = c.fbExplosionSprite;
                    break;
                case SpellType.Lightning:
                    d.color = c.lightningColor; d.effectSprite = c.lightningSprite;
                    d.damage = c.ltDamage; d.strikeRadius = c.ltStrikeRadius;
                    d.warningDuration = c.ltWarning; d.strikeDuration = c.ltStrikeDuration;
                    d.strikeHeightMul = c.ltStrikeHeightMul; d.fadeDuration = c.ltFadeSeconds;
                    break;
                case SpellType.Water:
                    d.color = c.waterColor; d.effectSprite = c.waterSprite;
                    d.waterRise = c.wtRise; d.waterMax = c.wtMax; d.damage = c.wtDamage;
                    d.waterRiseSeconds = c.wtRiseSeconds;
                    break;
                case SpellType.Earth:
                    d.color = c.earthColor; d.effectSprite = c.earthSprite;
                    d.effectHalfExtents = c.earthHalfExtents; d.riseSpeed = c.eaRiseSpeed; d.damage = c.eaDamage;
                    break;
                case SpellType.Wind:
                    d.color = c.windColor; d.effectSprite = c.windSprite;
                    d.windStrength = c.wiStrength; d.windDuration = c.wiDuration;
                    d.windStreakInterval = c.wiStreakInterval;
                    d.windDir = rng.Next(2) == 0 ? -1f : 1f;   // blow left or right
                    break;
                case SpellType.Mana:
                    d.color = c.manaColor; d.effectSprite = c.manaSprite;
                    d.manaCount = c.mnCount; d.manaSpreadStepDeg = c.mnStep;
                    d.projSpeed = c.mnProjSpeed; d.effectHalfExtents = c.manaHalfExtents; d.damage = c.mnDamage;
                    break;
            }
        }
    }

    /// <summary>Base for Marnu's page emitters: all four attacks reference the ONE shared spell book
    /// asset, so a spell edit there applies to every attack at once.</summary>
    [Serializable]
    public abstract class MarnuPageEmitter : BulletEmitter
    {
        [Tooltip("The shared spell tuning asset (SpellBook_Marnu). Every Marnu attack points at the same book.")]
        public MarnuSpellBookSO spellBook;

        private static readonly MarnuSpellConfig sDefaults = new MarnuSpellConfig();

        /// <summary>The book's spell config, or coded defaults if the reference is missing.</summary>
        protected MarnuSpellConfig Spells => spellBook != null ? spellBook.spells : sDefaults;
    }

    /// <summary>Crazy Spells: a spell page every <see cref="interval"/>s that flies from the boss to a random
    /// point within <see cref="radius"/> of him, then detonates in place.</summary>
    [Serializable]
    public class CrazySpellsEmitter : MarnuPageEmitter
    {
        [Tooltip("Seconds between spell pages.")]
        public float interval = 2f;
        [Tooltip("The page flies to a random point within this radius of the boss before detonating.")]
        public float radius = 2f;
        [Tooltip("Seconds the page takes to fly from the boss to its detonation point.")]
        public float flyTime = 0.6f;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (interval <= 0f) return;
            var spells = Spells;
            for (float t = 0f; t < duration; t += Mathf.Max(0.1f, interval))
            {
                var page = MarnuSpells.NewPage(spells, startTime + t);
                page.originOffset = Vector2.zero;   // spawns at the boss muzzle
                float ang = MarnuSpells.Rand(rng, 0f, Mathf.PI * 2f);
                float dist = radius * Mathf.Sqrt((float)rng.NextDouble());
                page.stagePoint = ctx.muzzle + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                page.pageMoveTime = Mathf.Max(0f, flyTime);
                page.pageHoldTime = 0f;
                page.pageLaunches = false;
                page.targetSelect = (float)rng.NextDouble();
                MarnuSpells.FillSpell(ref page, spells, MarnuSpells.Roll(spells, rng), rng, ctx);
                output.Add(page);
            }
        }

        public override string Describe() => $"Crazy Spells: a page every {interval}s within {radius} of the boss";
    }

    /// <summary>Surround Spells: an oval ring of <see cref="spellCount"/> pages around the box; one detonates
    /// at random every <see cref="detonateInterval"/>s until none are left.</summary>
    [Serializable]
    public class SurroundSpellsEmitter : MarnuPageEmitter
    {
        [Tooltip("Pages placed around the ring.")]
        public int spellCount = 6;
        [Tooltip("Seconds between detonations.")]
        public float detonateInterval = 2f;
        [Tooltip("How far outside the box edge the ring sits.")]
        public float ringMargin = 0.8f;
        [Tooltip("How fast the ring of pages circles the battlefield centre (deg/sec; 0 = stationary).")]
        public float orbitDegPerSec = 30f;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            var spells = Spells;
            int n = Mathf.Max(1, spellCount);
            Rect b = ctx.arenaBounds;
            float rx = b.width * 0.5f + ringMargin;
            float ry = b.height * 0.5f + ringMargin;

            // Random detonation order (Fisher–Yates).
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }

            for (int slot = 0; slot < n; slot++)
            {
                int i = order[slot];
                float ang = (Mathf.PI * 2f) * i / n;
                Vector2 ringPt = (Vector2)b.center + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry);
                float detonateAt = (slot + 1) * Mathf.Max(0.2f, detonateInterval);

                var page = MarnuSpells.NewPage(spells, startTime);   // all appear at once on the ring
                page.originOffset = ringPt - ctx.muzzle;
                page.stagePoint = ringPt;
                page.pageMoveTime = 0f;
                page.pageHoldTime = detonateAt;                      // then detonates in place
                page.pageLaunches = false;
                page.orbitCenter = b.center;                         // the ring circles the battlefield centre
                page.orbitRadii = new Vector2(rx, ry);
                page.orbitStartDeg = ang * Mathf.Rad2Deg;
                page.orbitDegPerSec = orbitDegPerSec;
                page.targetSelect = (float)rng.NextDouble();
                MarnuSpells.FillSpell(ref page, spells, MarnuSpells.Roll(spells, rng), rng, ctx);
                output.Add(page);
            }
        }

        public override string Describe() => $"Surround Spells: {spellCount} on a ring, one every {detonateInterval}s";
    }

    /// <summary>Targeted Spells: waves of <see cref="spellsPerWave"/> pages staged near the boss, then launched
    /// at a chosen player; each detonates on leaving the box or striking the target.</summary>
    [Serializable]
    public class TargetedSpellsEmitter : MarnuPageEmitter
    {
        [Tooltip("Pages launched per wave.")]
        public int spellsPerWave = 3;
        [Tooltip("Delay between the spells within one wave.")]
        public float withinWaveDelay = 0.1f;
        [Tooltip("Seconds between waves (barrages).")]
        public float barrageInterval = 2.5f;
        [Tooltip("Pages stage within this radius of the boss before launching.")]
        public float stageRadius = 1f;
        [Tooltip("Seconds to fly out to the staging point.")]
        public float flyTime = 0.35f;
        [Tooltip("Launch speed toward the target player.")]
        public float launchSpeed = 6f;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            if (barrageInterval <= 0f) return;
            var spells = Spells;
            int perWave = Mathf.Max(1, spellsPerWave);
            for (float t = 0f; t < duration; t += Mathf.Max(0.2f, barrageInterval))
            {
                float sel = (float)rng.NextDouble();   // one target for the whole wave
                for (int i = 0; i < perWave; i++)
                {
                    var page = MarnuSpells.NewPage(spells, startTime + t + i * Mathf.Max(0f, withinWaveDelay));
                    page.originOffset = Vector2.zero;
                    float ang = MarnuSpells.Rand(rng, 0f, Mathf.PI * 2f);
                    float dist = stageRadius * Mathf.Sqrt((float)rng.NextDouble());
                    page.stagePoint = ctx.muzzle + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    page.pageMoveTime = Mathf.Max(0f, flyTime);
                    page.pageHoldTime = 0.05f;
                    page.pageLaunches = true;
                    page.pageAimAtPlayer = true;
                    page.pageLaunchSpeed = launchSpeed;
                    page.targetSelect = sel;
                    MarnuSpells.FillSpell(ref page, spells, MarnuSpells.Roll(spells, rng), rng, ctx);
                    output.Add(page);
                }
            }
        }

        public override string Describe() => $"Targeted Spells: {spellsPerWave}/wave every {barrageInterval}s at a player";
    }

    /// <summary>Sea of Spells (Phase 2 / Rage): a row of <see cref="spellCount"/> pages below the box; one shoots
    /// straight up every <see cref="launchInterval"/>s, detonating on a player or on clearing the top.</summary>
    [Serializable]
    public class SeaOfSpellsEmitter : MarnuPageEmitter
    {
        [Tooltip("Pages in the row below the box.")]
        public int spellCount = 8;
        [Tooltip("Seconds between each page launching upward.")]
        public float launchInterval = 1f;
        [Tooltip("Upward launch speed.")]
        public float launchSpeed = 5f;
        [Tooltip("How far below the floor the row sits before launching.")]
        public float belowMargin = 0.6f;

        public override void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx)
        {
            var spells = Spells;
            int n = Mathf.Max(1, spellCount);
            Rect b = ctx.arenaBounds;
            float floorY = b.yMin - Mathf.Max(0f, belowMargin);
            for (int i = 0; i < n; i++)
            {
                float tx = n > 1 ? (float)i / (n - 1) : 0.5f;
                float x = Mathf.Lerp(b.xMin + 0.4f, b.xMax - 0.4f, tx);
                var page = MarnuSpells.NewPage(spells, startTime);
                page.originOffset = new Vector2(x, floorY) - ctx.muzzle;   // sit below the floor
                page.stagePoint = new Vector2(x, floorY);
                page.pageMoveTime = 0f;
                page.pageHoldTime = (i + 1) * Mathf.Max(0.2f, launchInterval);   // staggered rise
                page.pageLaunches = true;
                page.pageAimAtPlayer = false;
                page.velocity = Vector2.up;
                page.pageLaunchSpeed = launchSpeed;
                page.targetSelect = (float)rng.NextDouble();
                MarnuSpells.FillSpell(ref page, spells, MarnuSpells.Roll(spells, rng), rng, ctx);
                output.Add(page);
            }
        }

        public override string Describe() => $"Sea of Spells: {spellCount} rise one-by-one every {launchInterval}s";
    }
}

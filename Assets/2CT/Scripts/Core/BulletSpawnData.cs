using UnityEngine;

namespace TwoCT.Core
{
    /// <summary>
    /// How a scheduled bullet moves and reacts once it is spawned. Most bullets are
    /// <see cref="Linear"/> (constant velocity). The others run a small runtime state machine
    /// in <see cref="TwoCT.Bullets.Bullet"/> so bosses can do curving, exploding, growing and
    /// player-targeted attacks while still generating from the same deterministic schedule.
    /// </summary>
    public enum BulletBehavior
    {
        /// <summary>Constant-velocity bullet (the original behaviour). Uses <see cref="BulletSpawnData.velocity"/>.</summary>
        Linear = 0,
        /// <summary>Buble's exploding bubble: fly along the aim, then curve up/down and burst on hitting a player or the arena edge.</summary>
        CurvedExploder,
        /// <summary>Buble's appearing bubble: grow from nothing (harmless, faded), hold (harmful), then explode.</summary>
        GrowExplode,
        /// <summary>Buble's targeted bubble: drift out to a staging point, then fly straight at one chosen player.</summary>
        Targeted,
        /// <summary>The expanding red shock circle spawned when a bubble bursts. Pierces and lingers.</summary>
        Explosion,

        // ---- Ryomi (boss 2) ------------------------------------------------
        /// <summary>Marked Strike: a crosshair fades in at a frozen point near a player (telegraph),
        /// then slashes a cross (two rotated bars grow, hold, fade — damaging the whole time).</summary>
        MarkedStrike,
        /// <summary>A stationary cross slash (the bars grow/hold/fade). Spawned in place by a Tracking
        /// Cut detonation; Marked Strike runs the same slash itself after its telegraph.</summary>
        SlashCross,
        /// <summary>Cut: a long rectangle sliding right→left along the top or bottom, leaving afterimages.</summary>
        SlidingCut,
        /// <summary>A brief, harmless fading rectangle — the afterimage trail behind a sliding Cut.</summary>
        AfterImage,
        /// <summary>Ricochet: a bullet that bounces off the arena walls and expires after its lifetime.</summary>
        Ricochet,
        /// <summary>Tracking Cut: a spinning crosshair that homes on a player (accelerating), periodically
        /// detonating a stationary SlashCross at its position, then repeats a little faster. No contact damage.</summary>
        TrackingCut,
        /// <summary>Lasso: an invisible controller that drags the defend arena up and down for the round.</summary>
        Lasso,

        // ---- Marnu (boss 3) ------------------------------------------------
        /// <summary>A "spell page" (2:1 rectangle): flies out rotating, then does a 0.3s squash/stretch+fade
        /// detonation and reveals its <see cref="BulletSpawnData.spell"/> effect (spawned as child bullet(s)).</summary>
        SpellPage,
        /// <summary>Firebomb: a projectile aimed at a player that bursts into an expanding Explosion on
        /// hitting a player or the arena edge.</summary>
        Firebomb,
        /// <summary>Lightning: a warning circle fades in at a spot, then a bolt strikes for damage in a radius
        /// (the tall bolt sprite extends up above the strike area; the hit is only the bottom radius).</summary>
        Lightning,
        /// <summary>Earth pillar: a tall rock rectangle rises from the arena floor to the ceiling, then sinks
        /// back down and despawns.</summary>
        EarthPillar,
        /// <summary>Wind: an invisible controller that blows a horizontal force on the player icons (and streaks)
        /// for its lifetime, then clears the force.</summary>
        Wind,
        /// <summary>Water: a persistent zone rising from the arena floor (its height = the arena's accumulated
        /// water level); standing in it deals damage. Raised 10% per Water cast, capped at 50%.</summary>
        Water,

        // ---- Horus (boss 4) ------------------------------------------------
        /// <summary>Horus's apple: launched in a gravity arc; falls out of the battlefield (Apple Chuck), or
        /// bursts into a shower of upward apples on hitting the floor (Explosive Apples).</summary>
        GravityApple,
        /// <summary>Horus's Joint Horse Rider: an invisible controller that switches the defend arena into
        /// "ride mode" (players jump their horses over scrolling obstacles) for its lifetime.</summary>
        HorseRide,
    }

    /// <summary>The six spells a Marnu spell page can cast when it detonates. Each tints the page a unique
    /// colour (see <see cref="TwoCT.Bullets.Bullet"/>/emitters).</summary>
    public enum SpellType
    {
        Firebomb = 0,   // red
        Lightning,      // yellow
        Water,          // blue
        Earth,          // brown
        Wind,           // white
        Mana,           // purple
    }

    /// <summary>Collision shape a bullet hit-tests against the dodge icon.</summary>
    public enum BulletHitShape
    {
        Circle = 0,     // uses radius (the default for every legacy bullet)
        Box,            // an oriented rectangle (SlidingCut)
        Cross,          // two oriented bars (Marked Strike / SlashCross)
        None,           // never hits (telegraph-only crosshair, Lasso controller, afterimage)
    }

    /// <summary>
    /// One scheduled bullet, produced by a bullet pattern on the server AND on every client
    /// from the same shared seed. Because generation is deterministic, all clients render an
    /// identical pattern — yet each client simulates and hit-tests locally, so nobody is hit
    /// by a bullet they never saw ("client-sided bullets").
    ///
    /// The struct carries fields for every <see cref="BulletBehavior"/>; a Linear bullet simply
    /// leaves the behaviour-specific ones at their zero defaults.
    /// </summary>
    public struct BulletSpawnData
    {
        // ---- Scheduling / spawn -------------------------------------------
        public float time;          // seconds after pattern start to spawn
        public Vector2 originOffset;// spawn offset from the emitter muzzle (world units)

        // ---- Motion --------------------------------------------------------
        public Vector2 velocity;    // world units / second (Linear; also the initial heading for CurvedExploder)
        public float speed;         // scalar move speed for steered behaviours (CurvedExploder / Targeted)

        // ---- Combat --------------------------------------------------------
        public int damage;
        public float radius;        // collision radius against the player icon
        public float visualSize;    // sprite scale multiplier
        public bool destroyOnHit;   // false = piercing bullet that keeps going
        public bool explodeOnContact; // CurvedExploder: burst (spawn an Explosion) when it strikes the player
        public float lifetime;      // seconds before auto-despawn
        public Color color;         // tint; used as the flat fill when no sprite is set, else left white so art shows its own colours
        public Sprite sprite;       // this bullet's art (null = the fallback flat circle). Auto-sized to the collision radius.

        // ---- Behaviour -----------------------------------------------------
        public BulletBehavior behavior;

        // CurvedExploder ----------------------------------------------------
        public float travelTime;    // seconds moving along the aim before the curve begins
        public float curveDuration; // seconds to rotate from horizontal to fully vertical
        public float curveDir;      // +1 = curve up, -1 = curve down

        // GrowExplode (appearing bubble) ------------------------------------
        public float growDuration;  // 0 -> radius, faded & harmless
        public float holdDuration;  // stays at radius (harmful), then explodes

        // Explosion parameters ----------------------------------------------
        // Used by CurvedExploder/GrowExplode to configure the child they spawn, and by the
        // Explosion bullet itself to drive its expansion.
        public float explosionRadius;   // final collision/visual radius of the burst
        public float explosionExpand;   // seconds to expand from 0 -> explosionRadius
        public float explosionLifetime; // total lifetime of the burst
        public int explosionDamage;     // damage the burst deals
        public Color explosionColor;    // burst tint (flat fill when no explosionSprite, else white)
        public Sprite explosionSprite;  // art for the burst child this bullet spawns (null = flat circle)

        // Targeted -----------------------------------------------------------
        public Vector2 stagePoint;  // world point to drift to before launching
        public float outwardTime;   // seconds to reach stagePoint
        public float launchDelay;   // extra wait at stagePoint before flying at the player (the stagger)
        public float targetSelect;  // 0..1 -> resolved to a live player index at launch time

        // ---- Ryomi shared hitbox (Marked Strike / SlashCross / SlidingCut) --
        public BulletHitShape hitShape;   // Circle (default) unless a Ryomi attack overrides it
        public float rotationDeg;         // orientation of a Box/Cross hitbox + sprite
        public Vector2 boxHalfExtents;    // half-size of a Box hitbox (SlidingCut)

        // Cross slash (Marked Strike telegraph→slash, SlashCross, Tracking Cut detonation) ----
        public float telegraphDuration;   // MarkedStrike: crosshair fade-in before the slash (0 = slash now)
        public float crossArmLength;      // half-length of each bar (reach of the slash)
        public float crossThickness;      // full bar thickness once grown
        public float crosshairSize;       // diameter of the small crosshair telegraph marker
        public Sprite crosshairSprite;    // the crosshair marker's normal art (null = placeholder circle)
        public Sprite windupSprite;       // TrackingCut: art shown during the pre-shot windup (null = flash white)
        public float fadeDuration;        // seconds the bars fade out after the hold
        public float spawnRadius;         // MarkedStrike: crosshair appears within this of the target
        public float spawnAngle;          // deterministic offset angle (rad) within spawnRadius
        public float spawnDist;           // deterministic offset distance within spawnRadius

        // Tracking Cut -------------------------------------------------------
        public float fillDuration;        // seconds for opacity 0.25→1 before a detonation
        public float fillSpeedup;         // seconds shaved off fillDuration each cycle
        public float fillFloor;           // minimum fillDuration
        public float spinSpeedDeg;        // crosshair spin (deg/sec)
        public float homingAccel;         // acceleration toward the target (units/sec^2)
        public float homingMaxSpeed;      // max homing speed (units/sec)
        public float windupDuration;      // TrackingCut: pause (frozen + white) before each detonation

        // Sliding Cut / Lasso ------------------------------------------------
        public float afterImageInterval;  // seconds between afterimage drops (SlidingCut trail)
        public bool flipXOnSpawn;         // mirror the sprite horizontally at spawn (e.g. a left-facing sprite)
        public bool flipYOnSpawn;         // mirror the sprite vertically at spawn
        public float lassoSpeed;          // arena drag speed (units/sec)
        public float lassoRange;          // how far up/down the arena drags from centre
        public float lassoRopeWidth;      // width of the visible lasso ropes (0 = invisible)

        // ---- Marnu (boss 3): spell pages + spells --------------------------
        public SpellType spell;           // which spell a SpellPage casts on detonation
        public Sprite effectSprite;       // art for the revealed effect (bolt/pillar/firebomb/mana shot); null = placeholder

        // Page delivery -----------------------------------------------------
        public float pageMoveTime;        // seconds flying from the spawn point to stagePoint
        public float pageHoldTime;        // seconds held at stagePoint before it launches / detonates
        public float pageDetonateTime;    // seconds of the squash/stretch+fade detonation (design: 0.3)
        public bool  pageLaunches;        // after the hold, launch as a projectile (Targeted/Sea) vs detonate in place (Crazy/Surround)
        public bool  pageAimAtPlayer;     // a launched page flies at the targetSelect player (else it uses `velocity`)
        public float pageLaunchSpeed;     // speed of a launched page (kept separate from the spell's own projSpeed)
        public int   pageDamage;          // contact damage of the page itself (its rotating box hitbox), separate from the revealed spell

        // Firebomb / projectile ---------------------------------------------
        public float projSpeed;           // speed of the spawned projectile(s)

        // Lightning ----------------------------------------------------------
        public float warningDuration;     // warning indicator fades in (opacity 0.25 -> 1) over this
        public float strikeRadius;        // damage radius at the strike point (bottom of the bolt)
        public float strikeDuration;      // how long the bolt is shown / deals damage after the warning
        public float strikeHeightMul;     // bolt height as a multiple of the strike diameter (visual reach up)

        // Water --------------------------------------------------------------
        public float waterRise;           // fraction of the battlefield raised per cast (design: 0.1)
        public float waterMax;            // cap as a fraction of total height (design: 0.5)
        public float waterRiseSeconds;    // seconds the water takes to reach its new level (0 = instant)

        // Earth pillar / Mana shot box --------------------------------------
        public Vector2 effectHalfExtents; // box half-size of the spawned effect (Earth pillar, Mana shot)
        public float riseSpeed;           // Earth pillar rise/sink speed

        // Lightning strike point (independent of the page's own position) ----
        public Vector2 effectPoint;       // world point a spell strikes (Lightning x,y / Earth x); zero = clamp the page position
        public float effectSpinDeg;       // spin of the spawned effect projectile (Firebomb), deg/sec

        // Spell-page orbit (Surround Spells: the ring circles the battlefield centre) ----
        public Vector2 orbitCenter;       // world centre the page orbits
        public Vector2 orbitRadii;        // oval radii (x, y) of the orbit
        public float orbitStartDeg;       // angle on the oval at spawn
        public float orbitDegPerSec;      // orbit speed (0 = no orbit, page just holds its stage point)

        // Wind ---------------------------------------------------------------
        public float windStrength;        // horizontal force applied to player icons (units/sec)
        public float windDir;             // +1 = blow right, -1 = blow left
        public float windDuration;        // seconds the wind blows
        public float windStreakInterval;  // seconds between the drifting wind-streak visuals (0 = none)

        // Mana fan -----------------------------------------------------------
        public int   manaCount;           // projectiles in the fan (design: 5)
        public float manaSpreadStepDeg;   // angle step between fan projectiles (design: 15)

        // ---- Horus (boss 4): apples + horse ride ---------------------------
        public float gravity;             // downward accel for a GravityApple (units/sec^2)
        public bool  burstOnFloor;        // Explosive apple: burst into upward apples on hitting the floor
        public int   burstCount;          // apples spawned by the burst
        public float burstUpSpeed;        // upward launch speed of the burst apples
        public float burstSpeedX;         // max |random horizontal| speed of the burst apples
        public float burstUpVariance;     // ± random vertical speed added per burst apple
        public float burstRadius;         // radius of the burst apples (0 = inherit the bomb's radius)
        public float wobbleDeg;           // walk-wobble tilt amplitude for Linear movers (Horse Race horses)
        public float wobbleFreq;          // wobble frequency (rad/sec; FreeRoamPlayer uses 12)
        public int   randomSeed;          // deterministic seed for runtime randomness (burst velocities)
        public bool  maskInside;          // render only inside the arena mask (Horse Race / obstacles "come into frame")
        public bool  maskOutside;         // render only OUTSIDE the arena mask (Marnu wind streaks: the box stays clear)

        // Horse ride controller ---------------------------------------------
        public float rideGravity;         // gravity pulling the ridden icons down
        public float rideJumpVelocity;    // initial upward velocity of a jump (press W)
        public float rideMaxJumpHold;     // how long holding W keeps the jump floaty (variable jump height)
        public float rideLowGravityFactor;// gravity multiplier while a held jump is still rising (<1 = higher jumps)
        public float rideArenaExtraHeight;// extra box height during the ride (raised ceiling, floor stays put)
        public float rideSpeedMul;        // horizontal steer speed multiplier while riding (0 = 1×)
    }

    /// <summary>
    /// World-space layout handed to every emitter so it can place bullets relative to the boss
    /// muzzle and the defend box. Identical on all clients (same scene objects), so generation
    /// stays deterministic.
    /// </summary>
    public struct PatternContext
    {
        public Vector2 muzzle;      // world muzzle (bullets spawn at muzzle + originOffset)
        public Vector2 center;      // arena centre (world)
        public Vector2 aim;         // unit vector from muzzle toward the arena centre
        public Rect arenaBounds;    // world-space defend box (outer)
        public Rect arenaInner;     // padded inner bounds (where icons live)
    }

    /// <summary>
    /// Pure motion maths shared between the runtime bullet and the schedule builder (so the
    /// emitter can predict where a curving bubble ends up without simulating a live object).
    /// </summary>
    public static class BulletMotion
    {
        /// <summary>
        /// Instantaneous velocity of a <see cref="BulletBehavior.CurvedExploder"/> at the given age.
        /// It flies along its initial heading for <c>travelTime</c>, then sweeps toward straight
        /// up or down over <c>curveDuration</c> using a "backwards sqrt" ease (gentle, then sharp).
        /// </summary>
        public static Vector2 CurvedVelocity(in BulletSpawnData d, float age)
        {
            if (age < d.travelTime) return d.velocity;
            float ct = d.curveDuration > 0f ? Mathf.Clamp01((age - d.travelTime) / d.curveDuration) : 1f;
            float ease = 1f - Mathf.Sqrt(1f - ct);                                 // backwards sqrt
            float startAngle = Mathf.Atan2(d.velocity.y, d.velocity.x) * Mathf.Rad2Deg; // ~180° (leftward)
            float endAngle = d.curveDir >= 0f ? 90f : 270f;                        // up / down, keeping the leftward sweep
            float ang = Mathf.Lerp(startAngle, endAngle, ease) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d.speed;
        }

        /// <summary>
        /// Deterministically estimate when a curved bubble will burst against the arena edge (no
        /// player interception). Used to schedule the next bubble a fixed gap after this one pops,
        /// consistently on every client.
        /// </summary>
        public static float EstimateCurvedFlightTime(in PatternContext ctx, in BulletSpawnData d)
        {
            const float dt = 1f / 60f;
            Vector2 pos = ctx.muzzle + d.originOffset;
            Rect b = ctx.arenaBounds;
            float age = 0f;
            for (int i = 0; i < 60 * 30; i++)   // hard cap at 30s
            {
                pos += CurvedVelocity(d, age) * dt;
                age += dt;
                if (pos.x <= b.xMax &&          // only once it has entered the box horizontally
                    (pos.y >= b.yMax || pos.y <= b.yMin || pos.x <= b.xMin))
                    return age;
            }
            return age;
        }

        /// <summary>Does a circle overlap an oriented box (OBB)? Used for the Ryomi slash/cut hitboxes.
        /// Transforms the circle centre into the box's local frame, clamps to the box, and compares.</summary>
        public static bool CircleOverlapsBox(Vector2 circleCenter, float circleRadius,
                                             Vector2 boxCenter, Vector2 halfExtents, float angleDeg)
        {
            if (halfExtents.x <= 0f || halfExtents.y <= 0f) return false;
            float rad = -angleDeg * Mathf.Deg2Rad;                 // rotate the point INTO box space
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 d = circleCenter - boxCenter;
            Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            float cx = Mathf.Clamp(local.x, -halfExtents.x, halfExtents.x);
            float cy = Mathf.Clamp(local.y, -halfExtents.y, halfExtents.y);
            float dx = local.x - cx, dy = local.y - cy;
            return dx * dx + dy * dy <= circleRadius * circleRadius;
        }
    }
}

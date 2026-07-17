using System.Collections.Generic;
using TwoCT.Combat;
using TwoCT.Core;
using TwoCT.Data;
using UnityEngine;

namespace TwoCT.Bullets
{
    /// <summary>
    /// Runs the defend-phase bullet pattern locally on each client. Given a pattern + shared
    /// seed it rebuilds the identical deterministic schedule, spawns pooled bullets over time,
    /// moves them and hit-tests ONLY the local player's icon — so a player is only ever hit by
    /// bullets their own machine actually rendered (client-sided bullets).
    ///
    /// Bullets run per-behaviour state machines (see <see cref="Bullet"/>): bubbles can curve,
    /// grow, target a player, and burst into expanding shock circles spawned here at runtime.
    /// </summary>
    public class BulletSystem : MonoBehaviour
    {
        [SerializeField] private DefendArena arena;
        [SerializeField] private Sprite bulletSprite;
        [SerializeField] private int poolSize = 512;
        [Tooltip("Bullets are culled once this far from the arena centre.")]
        [SerializeField] private float cullRadius = 20f;

        private readonly List<Bullet> _pool = new List<Bullet>();
        private readonly List<Bullet> _active = new List<Bullet>();
        private readonly List<PlayerDodgeIcon> _targetScratch = new List<PlayerDodgeIcon>();
        private readonly List<int> _hitNotify = new List<int>();   // ids to broadcast AFTER the sim loop (see Update)

        private List<BulletSpawnData> _schedule;
        private int _nextIndex;
        private float _clock;
        private float _patternDuration;
        private bool _running;
        private Vector2 _muzzle;
        private PatternContext _ctx;
        private int _epoch;          // bumped each pattern so stale cross-client destroy messages are ignored

        /// <summary>The world-space defend box, used by curving bubbles to know where the walls are.</summary>
        public Rect ArenaBounds => arena != null ? arena.Bounds : _ctx.arenaBounds;

        private void Awake()
        {
            if (arena == null) arena = FindFirstObjectByType<DefendArena>();
            if (bulletSprite == null) bulletSprite = PlayerDodgeIcon.MakeCircleSprite();
            PrewarmPool();
        }

        private void PrewarmPool()
        {
            for (int i = 0; i < poolSize; i++) _pool.Add(CreateBullet());
        }

        private Bullet CreateBullet()
        {
            var go = new GameObject("Bullet");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = bulletSprite;
            sr.sortingOrder = 50;
            var b = go.AddComponent<Bullet>();
            b.Configure(sr, bulletSprite);   // bulletSprite is the fallback when a bullet has no art
            b.Despawn();
            return b;
        }

        private Bullet Rent()
        {
            foreach (var b in _pool) if (!b.Active) return b;
            var extra = CreateBullet(); _pool.Add(extra); return extra; // pool grows if exhausted
        }

        private void SpawnBullet(in BulletSpawnData data, Vector2 worldPos, int id)
        {
            var b = Rent();
            b.Spawn(data, worldPos);
            b.Id = id;
            _active.Add(b);
        }

        public int CurrentEpoch => _epoch;

        // =====================================================================
        //  Public API (called by CombatManager's defend RPCs)
        // =====================================================================
        public void BeginPattern(BulletPatternSO pattern, int seed, Vector2 muzzleWorld, Vector2 arenaCenterWorld)
        {
            ClearBullets();                 // don't collapse the box here — ShowIcons re-opens it
            if (pattern == null) return;
            _muzzle = muzzleWorld;
            Vector2 aim = arenaCenterWorld - muzzleWorld;
            aim = aim.sqrMagnitude < 0.0001f ? Vector2.left : aim.normalized;
            _ctx = new PatternContext
            {
                muzzle = muzzleWorld,
                center = arenaCenterWorld,
                aim = aim,
                arenaBounds = arena != null ? arena.Bounds : new Rect(arenaCenterWorld - new Vector2(3f, 1.75f), new Vector2(6f, 3.5f)),
                arenaInner = arena != null ? arena.InnerBounds : new Rect(arenaCenterWorld - new Vector2(2.8f, 1.55f), new Vector2(5.6f, 3.1f)),
            };
            _schedule = pattern.BuildSchedule(seed, _ctx);
            _patternDuration = pattern.duration;
            _nextIndex = 0;
            _clock = 0f;
            _epoch++;
            _running = true;
            if (arena != null && !arena.IsOpen) arena.ShowIcons();   // usually already opened by OpenArena()
        }

        /// <summary>Pop the defend box open (expand + spawn icons) without starting any bullets — the
        /// "brace yourself" beat before the attack.</summary>
        public void OpenArena()
        {
            if (arena != null) arena.ShowIcons();
        }

        /// <summary>Stop the sim and despawn bullets, and collapse the arena box shut.</summary>
        public void StopAndClear()
        {
            ClearBullets();
            if (arena != null) arena.CollapseAndHide();
        }

        /// <summary>Stop the sim and despawn bullets without touching the arena box.</summary>
        private void ClearBullets()
        {
            _running = false;
            _schedule = null;
            for (int i = _active.Count - 1; i >= 0; i--) _active[i].Despawn();
            _active.Clear();
            if (arena != null) arena.ResetLasso();   // undo any Lasso arena drag
        }

        /// <summary>Ryomi's Lasso: drag the defend arena vertically by <paramref name="offset"/> from
        /// its home position. Called each frame by the Lasso controller bullet.</summary>
        public void SetArenaLassoOffset(float offset)
        {
            if (arena != null) arena.SetLassoOffset(offset);
        }

        /// <summary>Clamp a point inside the defend box (crosshair explosions spawn within the battlefield).</summary>
        public Vector2 ClampToArena(Vector2 p) => arena != null ? arena.Clamp(p) : p;

        private void Update()
        {
            if (!_running) return;
            float dt = Time.deltaTime;
            _clock += dt;

            // Spawn everything scheduled up to the current time. The schedule index is the bullet's
            // stable cross-client id (same on every machine because the seed + schedule are shared).
            while (_schedule != null && _nextIndex < _schedule.Count && _schedule[_nextIndex].time <= _clock)
            {
                int id = _nextIndex;
                var data = _schedule[_nextIndex++];
                SpawnBullet(data, _muzzle + data.originOffset, id);
            }

            var localIcon = arena != null ? arena.LocalIcon : null;
            // No damage once the attack's own duration is up: the box is collapsing / the round is
            // transitioning, and players must not be hit by lingering explosions during that beat.
            bool damageActive = _clock <= _patternDuration;

            int count = _active.Count;   // children spawned this frame (explosions) are appended and skipped until next frame
            for (int i = count - 1; i >= 0; i--)
            {
                var b = _active[i];
                bool alive = b.Tick(dt, this);

                // Let a bullet spawn one child this frame (Tracking Cut detonation cross, Cut afterimage).
                if (b.HasPendingChild) { SpawnBullet(b.PendingChild, b.PendingChildPos, -1); b.ClearPendingChild(); }

                if (alive && damageActive && b.CurrentDamage > 0 &&
                    localIcon != null && !localIcon.IsInvincible && !localIcon.IsDead)
                {
                    if (b.Overlaps((Vector2)localIcon.transform.position, localIcon.Radius))
                    {
                        localIcon.RegisterHit(b.CurrentDamage);
                        bool destroyed = false;
                        if (b.Data.explodeOnContact) { b.ExplodeRequested = true; alive = false; destroyed = true; }
                        else if (b.Data.destroyOnHit) { alive = false; destroyed = true; }
                        // Tell the other clients to remove this bullet too — they never saw MY hit,
                        // so without this it would keep flying on their screens. DEFERRED: sending now
                        // would, on the host, re-enter ForceDestroy() synchronously and mutate _active
                        // mid-iteration — which orphaned an innocent bullet (removed from the list but
                        // never despawned, so it froze on screen forever). Flush after the loop instead.
                        if (destroyed && b.Id >= 0)
                            _hitNotify.Add(b.Id);
                    }
                }

                if (!alive)
                {
                    if (b.ExplodeRequested) SpawnExplosion(b.transform.position, b.Data);
                    b.Despawn();
                    _active.RemoveAt(i);
                }
            }

            // Broadcast hits now that the loop is done and _active is stable — a synchronous
            // ForceDestroy on the host can no longer corrupt the iteration above.
            if (_hitNotify.Count > 0)
            {
                var cm = CombatManager.Instance;
                for (int k = 0; k < _hitNotify.Count; k++) cm?.NotifyBulletHitServerRpc(_epoch, _hitNotify[k]);
                _hitNotify.Clear();
            }
        }

        // =====================================================================
        //  Runtime services used by Bullet behaviours
        // =====================================================================
        /// <summary>True once a position is far enough from the arena to be culled.</summary>
        public bool IsCulled(Vector2 pos)
        {
            Vector2 c = arena != null ? (Vector2)arena.Center : _muzzle;
            return (pos - c).sqrMagnitude > cullRadius * cullRadius;
        }

        /// <summary>Spawn the expanding red shock circle where a bubble burst (from its explosion params).</summary>
        private void SpawnExplosion(Vector2 pos, in BulletSpawnData parent)
        {
            var d = new BulletSpawnData
            {
                behavior = BulletBehavior.Explosion,
                originOffset = Vector2.zero,
                velocity = Vector2.zero,
                damage = parent.explosionDamage,
                radius = parent.explosionRadius,
                visualSize = 1f,
                destroyOnHit = false,       // "the explosion does not disappear on hit"
                explodeOnContact = false,
                lifetime = parent.explosionLifetime,
                color = parent.explosionColor,
                sprite = parent.explosionSprite,   // the burst's own art (null = flat circle)
                explosionRadius = parent.explosionRadius,
                explosionExpand = parent.explosionExpand,
                explosionLifetime = parent.explosionLifetime,
                explosionDamage = parent.explosionDamage,
                explosionColor = parent.explosionColor,
            };
            SpawnBullet(d, pos, -1);   // runtime child, not part of the shared schedule
        }

        /// <summary>
        /// Mirror a teammate's hit: remove the bullet with this schedule id (if still present),
        /// bursting it into an explosion if it was an on-contact exploder — same as a local hit,
        /// minus the damage (each client only takes damage from its own hits). Ignores stale
        /// messages from a previous pattern via <paramref name="epoch"/>.
        /// </summary>
        public void ForceDestroy(int epoch, int id)
        {
            if (!_running || epoch != _epoch || id < 0) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var b = _active[i];
                if (b.Id != id) continue;
                if (b.Data.explodeOnContact) SpawnExplosion(b.transform.position, b.Data);
                b.Despawn();
                _active.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// Resolve a targeted bubble's chosen player to a live world position. Alive icons are
        /// ordered by seat so <paramref name="select"/> maps to the same player on every client;
        /// if the target is gone, aim at the arena centre.
        /// </summary>
        public Vector2 ResolveTargetPosition(float select, Vector2 fallback)
        {
            if (arena != null)
            {
                _targetScratch.Clear();
                foreach (var ic in arena.Icons)
                    if (ic != null && !ic.IsDead) _targetScratch.Add(ic);
                _targetScratch.Sort((a, b) =>
                    (a.Player != null ? a.Player.Slot.Value : 0).CompareTo(b.Player != null ? b.Player.Slot.Value : 0));
                if (_targetScratch.Count > 0)
                {
                    int idx = Mathf.Clamp((int)(select * _targetScratch.Count), 0, _targetScratch.Count - 1);
                    return _targetScratch[idx].transform.position;
                }
                return arena.Center;
            }
            return fallback + Vector2.left * 5f;
        }
    }
}

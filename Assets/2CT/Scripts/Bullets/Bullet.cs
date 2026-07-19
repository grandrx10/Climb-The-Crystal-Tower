using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Bullets
{
    /// <summary>
    /// A single client-side bullet. Purely visual/local — never a NetworkObject. Pooled and
    /// driven by <see cref="BulletSystem"/>. Its motion + lifecycle is chosen by
    /// <see cref="BulletSpawnData.behavior"/>; simple bullets are <see cref="BulletBehavior.Linear"/>.
    ///
    /// The system reads <see cref="CurrentRadius"/>/<see cref="CurrentDamage"/> each frame for
    /// hit-tests (they change over time for growing/exploding bubbles), and <see cref="ExplodeRequested"/>
    /// to know when a bubble has burst and a shock circle should be spawned in its place.
    /// </summary>
    public class Bullet : MonoBehaviour
    {
        public BulletSpawnData Data;
        public float Age;
        public bool Active;

        /// <summary>Stable cross-client id = this bullet's index in the deterministic schedule, so a
        /// hit on one client can be mirrored on the others. -1 for runtime children (explosions).</summary>
        public int Id = -1;

        /// <summary>Live collision radius (may differ from Data.radius while growing/exploding).</summary>
        public float CurrentRadius;
        /// <summary>Live contact damage (0 while a bubble is a harmless telegraph).</summary>
        public int CurrentDamage;
        /// <summary>Set true the frame a bubble bursts, so the system can spawn its Explosion child.</summary>
        public bool ExplodeRequested;

        private SpriteRenderer _sr;
        private Sprite _defaultSprite;   // fallback circle when a bullet has no art assigned
        private Vector2 _spawnPos;
        private bool _launched;      // Targeted: has it locked its heading and started flying?
        private Vector2 _flyDir;     // Targeted: locked heading
        private Transform _hitbox;   // dev overlay showing the true collision circle (radius = CurrentRadius)
        private SpriteRenderer _hitboxSr;

        // ---- Ryomi (boss 2) runtime state ----------------------------------
        private SpriteRenderer _barH, _barV;   // cross-slash bar visuals (children; local axes, parent rotates)
        private SpriteRenderer _crosshair;     // small telegraph marker (child; not the long bars)
        private SpriteRenderer _ropeTop, _ropeBottom;   // Lasso: visible ropes from screen edges to the box
        private float _crossThickness;         // current bar thickness (grows 0→crossThickness)
        private Vector2 _homingVel;            // TrackingCut homing velocity
        private float _fillClock;              // TrackingCut: progress toward the next detonation
        private float _fillDur;                // TrackingCut: current fill duration (shrinks each cycle)
        private bool _winding;                 // TrackingCut: in the pre-shot windup (frozen + white)?
        private float _windupClock;            // TrackingCut: time spent in the windup
        private float _afterTimer;             // SlidingCut: time since the last afterimage drop (also Wind streak timer)

        // ---- Marnu (boss 3) runtime state ----------------------------------
        private bool _detonating;              // SpellPage: playing the 0.3s squash/fade detonation
        private float _detonateClock;          // SpellPage: time into the detonation
        private bool _pageEntered;             // SpellPage (launched) / Firebomb: has it been inside the box yet? (so one that starts outside doesn't detonate on frame 1)
        private float _insideClock;            // Firebomb: time spent inside the box (wall bursts arm only after a short grace past entry)
        private Vector2 _vel;                  // GravityApple: current velocity (integrates gravity)
        private Transform _bolt;               // Lightning: the tall bolt sprite (child; bottom pinned at the strike point)
        private SpriteRenderer _boltSr;

        // Children a bullet asks the system to spawn this frame (TrackingCut detonation cross, SlidingCut
        // afterimage, Marnu spell-page detonation — which can reveal several, e.g. Mana's 5-shot fan).
        // Read + cleared by BulletSystem after Tick.
        public struct PendingSpawn { public BulletSpawnData data; public Vector2 pos; }
        private readonly List<PendingSpawn> _pendingChildren = new List<PendingSpawn>();
        public bool HasPendingChild => _pendingChildren.Count > 0;
        public IReadOnlyList<PendingSpawn> PendingChildren => _pendingChildren;
        public void ClearPendingChild() => _pendingChildren.Clear();
        private void RequestChild(in BulletSpawnData d, Vector2 pos) => _pendingChildren.Add(new PendingSpawn { data = d, pos = pos });

        private bool IsCrossShape => Data.hitShape == BulletHitShape.Cross;

        public void Configure(SpriteRenderer sr, Sprite defaultSprite)
        {
            _sr = sr;
            _defaultSprite = defaultSprite;
        }

        public void Spawn(in BulletSpawnData data, Vector2 worldPos)
        {
            Data = data;
            Age = 0f;
            Active = true;
            ExplodeRequested = false;
            _launched = false;
            _flyDir = Vector2.zero;
            _spawnPos = worldPos;
            CurrentRadius = data.radius;
            CurrentDamage = data.damage;
            transform.position = worldPos;
            // Reset Ryomi runtime state.
            _crossThickness = 0f;
            _homingVel = Vector2.zero;
            _fillClock = 0f;
            _fillDur = Mathf.Max(0.05f, data.fillDuration);
            _winding = false;
            _windupClock = 0f;
            _afterTimer = 0f;
            _detonating = false;
            _detonateClock = 0f;
            _pageEntered = false;
            _insideClock = 0f;
            _vel = data.velocity;
            _pendingChildren.Clear();
            if (_bolt != null) _bolt.gameObject.SetActive(false);
            if (_barH != null) _barH.gameObject.SetActive(false);
            if (_barV != null) _barV.gameObject.SetActive(false);
            if (_crosshair != null) _crosshair.gameObject.SetActive(false);

            // Orientation: explosions spin randomly (purely visual, circle hitbox); Box/Cross slashes
            // use their authored rotation; everything else stays upright (also clears pooled spin).
            if (data.behavior == BulletBehavior.Explosion)
                transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            else if (data.hitShape == BulletHitShape.Box || data.hitShape == BulletHitShape.Cross
                     || data.behavior == BulletBehavior.AfterImage)
                transform.rotation = Quaternion.Euler(0f, 0f, data.rotationDeg);   // slashes + trail keep their facing
            else
                transform.rotation = Quaternion.identity;
            if (_sr != null)
            {
                // Real art shows its own colours (rendered white, tint only carries alpha for fades);
                // the placeholder uses the flat `color`. Box slashes fall back to a SQUARE (not the
                // circle) so an untextured Cut renders as a rectangle rather than an ellipse.
                // A spell page is a rectangle too, so it falls back to the square (not the circle) placeholder.
                bool wantSquare = data.hitShape == BulletHitShape.Box || data.behavior == BulletBehavior.SpellPage;
                Sprite fallback = wantSquare ? PlayerDodgeIcon.MakeSquareSprite() : _defaultSprite;
                _sr.sprite = data.sprite != null ? data.sprite : fallback;
                _sr.color = data.sprite != null ? Color.white : data.color;
                _sr.flipX = data.flipXOnSpawn; _sr.flipY = data.flipYOnSpawn;   // base flip (also clears pooled state)
                // Occlusion: Horse Race apples/horses + ride obstacles render only inside the arena mask so
                // they slide INTO frame rather than popping in at their off-screen spawn point.
                _sr.maskInteraction = data.maskInside ? SpriteMaskInteraction.VisibleInsideMask
                                    : data.maskOutside ? SpriteMaskInteraction.VisibleOutsideMask
                                    : SpriteMaskInteraction.None;
                _sr.enabled = true;
            }
            // Cross slashes + crosshairs draw via dedicated children (bars / crosshair marker), NOT the
            // main sprite — so the placeholder circle never flashes alongside the rectangular slash.
            if (_sr != null && (data.behavior == BulletBehavior.MarkedStrike || data.behavior == BulletBehavior.SlashCross
                || data.behavior == BulletBehavior.TrackingCut || data.behavior == BulletBehavior.Lasso))
                _sr.enabled = false;
            // Circle bullets size the sprite to their radius. Box bullets size to their extents NOW (not
            // in the first Tick) so they don't flash as a 1×1 square for one frame. Cross/None keep
            // scale 1 (bars/crosshair children size themselves).
            if (data.hitShape == BulletHitShape.Circle)
                ApplyVisual(data.behavior == BulletBehavior.GrowExplode || data.behavior == BulletBehavior.Explosion ? 0f : data.radius);
            else if (data.hitShape == BulletHitShape.Box)
                ApplyBoxVisual(1f);
            else
                transform.localScale = Vector3.one;
            // Invisible controllers (drive the arena / environment, draw nothing themselves).
            if ((data.behavior == BulletBehavior.Lasso || data.behavior == BulletBehavior.Wind
                 || data.behavior == BulletBehavior.HorseRide) && _sr != null) _sr.enabled = false;
            UpdateHitboxOverlay();
            gameObject.SetActive(true);
        }

        public void Despawn()
        {
            Active = false;
            if (_sr != null) _sr.enabled = false;
            if (_barH != null) _barH.gameObject.SetActive(false);
            if (_barV != null) _barV.gameObject.SetActive(false);
            if (_crosshair != null) _crosshair.gameObject.SetActive(false);
            if (_ropeTop != null) _ropeTop.gameObject.SetActive(false);
            if (_ropeBottom != null) _ropeBottom.gameObject.SetActive(false);
            if (_bolt != null) _bolt.gameObject.SetActive(false);
            if (_hitbox != null && _hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(false);
            _pendingChildren.Clear();
            gameObject.SetActive(false);
        }

        /// <summary>Advance the bullet one frame. Returns false when it should be despawned (at which
        /// point the system honours <see cref="ExplodeRequested"/>).</summary>
        public bool Tick(float dt, BulletSystem sys)
        {
            Age += dt;
            bool alive;
            switch (Data.behavior)
            {
                case BulletBehavior.CurvedExploder: alive = TickCurved(dt, sys); break;
                case BulletBehavior.GrowExplode:    alive = TickGrow(dt); break;
                case BulletBehavior.Targeted:       alive = TickTargeted(dt, sys); break;
                case BulletBehavior.Explosion:      alive = TickExplosion(dt); break;
                case BulletBehavior.MarkedStrike:   alive = TickMarkedStrike(dt, sys); break;
                case BulletBehavior.SlashCross:     alive = TickSlashCross(dt, 0f); break;
                case BulletBehavior.SlidingCut:     alive = TickSlidingCut(dt, sys); break;
                case BulletBehavior.AfterImage:     alive = TickAfterImage(dt); break;
                case BulletBehavior.Ricochet:       alive = TickRicochet(dt, sys); break;
                case BulletBehavior.TrackingCut:    alive = TickTrackingCut(dt, sys); break;
                case BulletBehavior.Lasso:          alive = TickLasso(dt, sys); break;
                case BulletBehavior.SpellPage:      alive = TickSpellPage(dt, sys); break;
                case BulletBehavior.Firebomb:       alive = TickFirebomb(dt, sys); break;
                case BulletBehavior.Lightning:      alive = TickLightning(dt); break;
                case BulletBehavior.EarthPillar:    alive = TickEarthPillar(dt, sys); break;
                case BulletBehavior.Wind:           alive = TickWind(dt, sys); break;
                case BulletBehavior.Water:          alive = TickWater(dt, sys); break;
                case BulletBehavior.GravityApple:   alive = TickGravityApple(dt, sys); break;
                case BulletBehavior.HorseRide:      alive = TickHorseRide(dt, sys); break;
                default:                            alive = TickLinear(dt, sys); break;
            }
            if (alive) UpdateHitboxOverlay();   // track the live radius (bubbles grow/explode)
            return alive;
        }

        // ---- Behaviours ----------------------------------------------------

        private bool TickLinear(float dt, BulletSystem sys)
        {
            transform.position += (Vector3)(Data.velocity * dt);
            if (Data.spinSpeedDeg != 0f)                       // rolling/tumbling (Horse Race apples)
                transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);
            else if (Data.wobbleDeg != 0f)                     // walk-wobble (Horse Race horses), like free roam
                transform.rotation = Quaternion.Euler(0f, 0f,
                    Data.rotationDeg + Mathf.Sin(Age * Data.wobbleFreq) * Data.wobbleDeg);
            if (Age >= Data.lifetime) return false;
            return !sys.IsCulled(transform.position);
        }

        private bool TickCurved(float dt, BulletSystem sys)
        {
            transform.position += (Vector3)(BulletMotion.CurvedVelocity(Data, Age) * dt);

            // Burst on reaching the arena edge (once it's actually entered the box horizontally).
            Rect b = sys.ArenaBounds;
            Vector2 p = transform.position;
            if (p.x <= b.xMax && (p.y >= b.yMax || p.y <= b.yMin || p.x <= b.xMin))
            {
                ExplodeRequested = true;
                return false;
            }
            if (Age >= Data.lifetime) { ExplodeRequested = true; return false; }  // safety net
            return true;
        }

        private bool TickGrow(float dt)
        {
            if (Age < Data.growDuration)
            {
                // Growing: harmless telegraph, drawn faded so players can read "move away".
                float p = Data.growDuration > 0f ? Age / Data.growDuration : 1f;
                CurrentRadius = Data.radius * p;
                CurrentDamage = 0;
                ApplyVisual(CurrentRadius);
                SetAlpha(0.35f);
                return true;
            }
            if (Age < Data.growDuration + Data.holdDuration)
            {
                // Fully grown: solid and harmful, sitting there before it pops.
                CurrentRadius = Data.radius;
                CurrentDamage = Data.damage;
                ApplyVisual(CurrentRadius);
                SetAlpha(1f);
                return true;
            }
            ExplodeRequested = true;   // pop -> the system spawns the expanding shock circle
            return false;
        }

        private bool TickTargeted(float dt, BulletSystem sys)
        {
            float outEnd = Data.outwardTime;
            float launchAt = Data.outwardTime + Data.launchDelay;

            if (Age < outEnd)
            {
                // Drift out from the muzzle to the staging point near the boss.
                float p = outEnd > 0f ? Age / outEnd : 1f;
                transform.position = Vector2.Lerp(_spawnPos, Data.stagePoint, p);
                return true;
            }
            if (Age < launchAt)
            {
                transform.position = Data.stagePoint;   // hold, staggered per bubble
                return true;
            }

            if (!_launched)
            {
                _launched = true;
                Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, Data.stagePoint);
                _flyDir = (target - (Vector2)transform.position);
                _flyDir = _flyDir.sqrMagnitude > 0.0001f ? _flyDir.normalized : Vector2.left;
            }
            transform.position += (Vector3)(_flyDir * (Data.speed * dt));
            if (Age >= Data.lifetime) return false;
            return !sys.IsCulled(transform.position);   // gone once it flies off screen
        }

        private bool TickExplosion(float dt)
        {
            float p = Data.explosionExpand > 0f ? Mathf.Clamp01(Age / Data.explosionExpand) : 1f;
            CurrentRadius = Data.explosionRadius * p;
            CurrentDamage = Data.explosionDamage;
            ApplyVisual(CurrentRadius);

            // Hold full, then fade the tint out over the back half of its life.
            float life = Data.explosionLifetime;
            if (life > 0f)
            {
                float fade = Age < life * 0.5f ? 1f : Mathf.Clamp01(1f - (Age - life * 0.5f) / (life * 0.5f));
                SetAlpha(0.35f + 0.65f * fade);
                if (Age >= life) return false;
            }
            return true;
        }

        // ---- Ryomi (boss 2) behaviours -------------------------------------

        // Marked Strike: a small crosshair fades in (telegraph, harmless) at its frozen spot INSIDE the
        // battlefield, then it's replaced by the long cross slash.
        private bool TickMarkedStrike(float dt, BulletSystem sys)
        {
            if (!_launched)
            {
                // Freeze the crosshair near the target (spawn position + offset), clamped into the box.
                _launched = true;
                Vector2 tp = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
                Vector2 off = new Vector2(Mathf.Cos(Data.spawnAngle), Mathf.Sin(Data.spawnAngle)) * Data.spawnDist;
                transform.position = sys.ClampToArena(tp + off);
            }
            if (Age < Data.telegraphDuration)
            {
                CurrentDamage = 0;
                _crossThickness = 0f;
                if (_barH != null) _barH.gameObject.SetActive(false);
                if (_barV != null) _barV.gameObject.SetActive(false);
                float p = Data.telegraphDuration > 0f ? Age / Data.telegraphDuration : 1f;
                ShowCrosshair(0.2f + 0.6f * Mathf.Clamp01(p));   // small marker fades in
                return true;
            }
            HideCrosshair();                                     // the slash replaces the crosshair
            return TickSlashCross(dt, Age - Data.telegraphDuration);
        }

        // A cross slash: two rotated bars grow to full thickness, hold, then fade — damaging throughout.
        // Marked Strike drives it via `clock` after its telegraph; a standalone SlashCross uses its own Age.
        private bool TickSlashCross(float dt, float clock)
        {
            float t = Data.behavior == BulletBehavior.SlashCross ? Age : clock;
            float grow = Mathf.Max(0.0001f, Data.growDuration);
            float hold = Mathf.Max(0f, Data.holdDuration);
            float fade = Mathf.Max(0.0001f, Data.fadeDuration);
            float total = grow + hold + fade;
            if (t >= total) return false;

            float thicknessFrac, alpha;
            if (t < grow) { thicknessFrac = t / grow; alpha = 1f; }
            else if (t < grow + hold) { thicknessFrac = 1f; alpha = 1f; }
            else { thicknessFrac = 1f; alpha = 1f - (t - grow - hold) / fade; }

            _crossThickness = Data.crossThickness * thicknessFrac;
            CurrentDamage = Data.damage;   // damaging the whole time the bars are visible
            UpdateCrossBars(alpha);
            if (Data.behavior == BulletBehavior.SlashCross) SetAlpha(0f);   // no crosshair sprite for a bare cross
            return true;
        }

        // Sliding Cut: a long rectangle sweeping right→left, dropping fading afterimages behind it.
        private bool TickSlidingCut(float dt, BulletSystem sys)
        {
            transform.position += (Vector3)(Data.velocity * dt);
            CurrentDamage = Data.damage;
            ApplyBoxVisual(1f);

            _afterTimer += dt;
            if (Data.afterImageInterval > 0f && _afterTimer >= Data.afterImageInterval)
            {
                _afterTimer = 0f;
                var ai = Data;
                ai.behavior = BulletBehavior.AfterImage;
                ai.damage = 0;                          // harmless (Box + 0 damage = never hit-tested)
                ai.hitShape = BulletHitShape.Box;       // render as the same RECTANGLE, not a circle
                ai.velocity = Vector2.zero;
                ai.lifetime = Mathf.Max(0.15f, Data.afterImageInterval * 3f);
                RequestChild(ai, transform.position);
            }

            if (Age >= Data.lifetime) return false;
            return !sys.IsCulled(transform.position);
        }

        private bool TickAfterImage(float dt)
        {
            CurrentDamage = 0;
            float a = Mathf.Clamp01(1f - Age / Mathf.Max(0.0001f, Data.lifetime));   // fade out
            if (Data.boxHalfExtents.x > 0f || Data.boxHalfExtents.y > 0f)
                ApplyBoxVisual(a);                       // Cut trail: rectangle
            else { ApplyVisual(Data.radius); SetAlpha(a); }   // Ricochet trail: circle
            return Age < Data.lifetime;
        }

        // Ricochet: linear (velocity aimed at a random battlefield point by the emitter), reflecting off
        // the arena walls; expires after its lifetime.
        private bool TickRicochet(float dt, BulletSystem sys)
        {
            Vector2 v = Data.velocity;
            Vector2 p = (Vector2)transform.position + v * dt;
            Rect b = sys.ArenaBounds;
            float r = CurrentRadius;
            if (p.x - r < b.xMin && v.x < 0) { p.x = b.xMin + r; v.x = -v.x; }
            else if (p.x + r > b.xMax && v.x > 0) { p.x = b.xMax - r; v.x = -v.x; }
            if (p.y - r < b.yMin && v.y < 0) { p.y = b.yMin + r; v.y = -v.y; }
            else if (p.y + r > b.yMax && v.y > 0) { p.y = b.yMax - r; v.y = -v.y; }
            Data.velocity = v;
            transform.position = p;

            // Always rotate to face the travel direction (visual only — circle hitbox is rotation-
            // invariant). atan2 assumes a right-facing sprite, so a left-facing one wants Flip X On Spawn.
            if (v.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
            CurrentDamage = Data.damage;
            return Age < Data.lifetime;
        }

        // Tracking Cut: a spinning crosshair that homes on a player (accelerating, capped), periodically
        // detonating a stationary SlashCross and speeding up. The crosshair itself never deals damage.
        private bool TickTrackingCut(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;   // harmless; the detonated cross is the hazard

            if (_winding)
            {
                // Pre-shot: freeze in place, crosshair goes solid white, THEN detonate + resume.
                _windupClock += dt;
                ShowCrosshair(1f, windup: true);
                if (_windupClock >= Mathf.Max(0f, Data.windupDuration))
                {
                    _winding = false;
                    _windupClock = 0f;
                    _fillClock = 0f;
                    _fillDur = Mathf.Max(Data.fillFloor, _fillDur - Data.fillSpeedup);   // faster each cycle
                    var cross = Data;
                    cross.behavior = BulletBehavior.SlashCross;
                    cross.hitShape = BulletHitShape.Cross;
                    cross.rotationDeg = transform.eulerAngles.z;   // detonate at the crosshair's current spin
                    cross.telegraphDuration = 0f;
                    RequestChild(cross, sys.ClampToArena(transform.position));   // stationary cross inside the box
                }
                return Age < Data.lifetime;
            }

            // Homing + spin while the opacity fills.
            Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
            Vector2 toTarget = target - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                _homingVel += toTarget.normalized * (Data.homingAccel * dt);
            if (Data.homingMaxSpeed > 0f && _homingVel.magnitude > Data.homingMaxSpeed)
                _homingVel = _homingVel.normalized * Data.homingMaxSpeed;
            transform.position += (Vector3)(_homingVel * dt);
            transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);

            _fillClock += dt;
            float p = _fillDur > 0f ? Mathf.Clamp01(_fillClock / _fillDur) : 1f;
            ShowCrosshair(Mathf.Lerp(0.25f, 1f, p));
            if (_fillClock >= _fillDur) { _winding = true; _windupClock = 0f; _homingVel = Vector2.zero; }   // stop + begin windup
            return Age < Data.lifetime;
        }

        // Lasso: controller that drags the defend arena up/down + shows the ropes tying it to the screen.
        private bool TickLasso(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;
            float range = Mathf.Max(0f, Data.lassoRange);
            float period = (range > 0f && Data.lassoSpeed > 0f) ? 4f * range / Data.lassoSpeed : 1f;
            float phase = Mathf.Repeat(Age, period) / period;   // 0..1, deterministic from Age
            float tri = phase < 0.5f ? Mathf.Lerp(-1f, 1f, phase * 2f) : Mathf.Lerp(1f, -1f, (phase - 0.5f) * 2f);
            sys.SetArenaLassoOffset(tri * range);
            UpdateLassoRopes(sys);
            if (Age >= Data.lifetime) { sys.SetArenaLassoOffset(0f); return false; }
            return true;
        }

        // Two ropes from the top/bottom of the view down to the box's top/bottom edges; they stretch as
        // the box is dragged. Placeholder rectangles (assign Data.sprite for real rope art).
        private void UpdateLassoRopes(BulletSystem sys)
        {
            float width = Data.lassoRopeWidth;
            if (width <= 0f) return;   // invisible if unset
            if (_ropeTop == null) { _ropeTop = MakeRope("LassoRopeTop"); _ropeBottom = MakeRope("LassoRopeBottom"); }

            Rect b = sys.ArenaBounds;
            var cam = Camera.main;
            float top = cam != null ? cam.transform.position.y + cam.orthographicSize + 0.5f : b.yMax + 6f;
            float bottom = cam != null ? cam.transform.position.y - cam.orthographicSize - 0.5f : b.yMin - 6f;
            float cx = b.center.x;

            PlaceRope(_ropeTop, cx, b.yMax, top, width);      // from box top up to the screen top
            PlaceRope(_ropeBottom, cx, b.yMin, bottom, width);// from box bottom down to the screen bottom
        }

        private SpriteRenderer MakeRope(string ropeName)
        {
            var go = new GameObject(ropeName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Data.sprite != null ? Data.sprite : PlayerDodgeIcon.MakeSquareSprite();
            sr.color = Data.color.a > 0f ? Data.color : new Color(0.6f, 0.45f, 0.3f);   // tan rope
            sr.sortingOrder = 22;   // above the arena fill/border
            return sr;
        }

        private void PlaceRope(SpriteRenderer rope, float x, float edgeY, float anchorY, float width)
        {
            rope.gameObject.SetActive(true);
            float h = Mathf.Max(0.01f, Mathf.Abs(anchorY - edgeY));
            rope.transform.position = new Vector3(x, (edgeY + anchorY) * 0.5f, 0f);   // world-space
            rope.transform.rotation = Quaternion.identity;
            rope.transform.localScale = new Vector3(width, h, 1f);
        }

        // ---- Marnu (boss 3) behaviours -------------------------------------

        // A "spell page": a 1:1 square that flies out (rotating), holds, then either launches as a
        // projectile (Targeted / Sea of Spells) or detonates in place (Crazy / Surround). Detonation is a
        // 0.3s squash/stretch + fade, after which the page reveals its spell as child bullet(s).
        private bool TickSpellPage(float dt, BulletSystem sys)
        {
            CurrentDamage = Data.pageDamage;   // the page's rotating box hitbox hurts on contact; its detonation then reveals the spell
            if (_detonating) return TickPageDetonation(dt, sys);

            float moveEnd = Mathf.Max(0f, Data.pageMoveTime);
            float holdEnd = moveEnd + Mathf.Max(0f, Data.pageHoldTime);

            if (Age < moveEnd)                                    // fly from spawn -> staging point
            {
                float p = moveEnd > 0f ? Age / moveEnd : 1f;
                transform.position = Vector2.Lerp(_spawnPos, Data.stagePoint, Mathf.Clamp01(p));
                transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);
                RenderPage(Data.boxHalfExtents.x, Data.boxHalfExtents.y, 1f);
                return true;
            }
            if (Age < holdEnd)                                    // held at the staging point, still spinning
            {
                if (Data.orbitDegPerSec != 0f)                    // Surround: the whole ring circles the battlefield centre
                {
                    float a = (Data.orbitStartDeg + Data.orbitDegPerSec * Age) * Mathf.Deg2Rad;
                    transform.position = Data.orbitCenter
                        + new Vector2(Mathf.Cos(a) * Data.orbitRadii.x, Mathf.Sin(a) * Data.orbitRadii.y);
                }
                else transform.position = Data.stagePoint;
                transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);
                RenderPage(Data.boxHalfExtents.x, Data.boxHalfExtents.y, 1f);
                return true;
            }

            if (Data.pageLaunches)                                // Targeted / Sea: fly until it leaves the box or strikes you
            {
                if (!_launched)
                {
                    _launched = true;
                    if (Data.pageAimAtPlayer)
                    {
                        Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
                        Vector2 d = target - (Vector2)transform.position;
                        _flyDir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.left;
                    }
                    else _flyDir = Data.velocity.sqrMagnitude > 0.0001f ? Data.velocity.normalized : Vector2.up;
                }
                transform.position += (Vector3)(_flyDir * (Mathf.Max(0.1f, Data.pageLaunchSpeed) * dt));
                transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);
                RenderPage(Data.boxHalfExtents.x, Data.boxHalfExtents.y, 1f);
                // Detonate on leaving the box (only once it has actually entered — Sea pages start below
                // the floor) or on striking the local player.
                bool inside = sys.InArena(transform.position);
                if (inside) _pageEntered = true;
                if ((_pageEntered && !inside) || HitsLocalIcon(sys, Data.boxHalfExtents.x))
                    BeginPageDetonation();
                return true;
            }

            BeginPageDetonation();                                // Crazy / Surround: detonate where it sits
            return true;
        }

        private void BeginPageDetonation() { _detonating = true; _detonateClock = 0f; }

        private bool TickPageDetonation(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;   // the stretching page is harmless — only the revealed spell hurts
            _detonateClock += dt;
            float dur = Mathf.Max(0.05f, Data.pageDetonateTime);
            float p = Mathf.Clamp01(_detonateClock / dur);
            float squash = 1f + 0.1f * Mathf.Sin(p * Mathf.PI);            // just a little squeeze & stretch, then ease back
            RenderPage(Data.boxHalfExtents.x / squash, Data.boxHalfExtents.y * squash, 1f - p);   // fade out fast
            if (p >= 1f) { SpawnSpellEffect(sys); return false; }          // reveal the spell; page is gone
            return true;
        }

        // Draw the page rectangle at the given half-size + alpha, tinted to its spell colour.
        private void RenderPage(float halfW, float halfH, float alpha)
        {
            if (_sr == null) return;
            _sr.enabled = true;
            Vector2 sz = _sr.sprite != null ? (Vector2)_sr.sprite.bounds.size : Vector2.one;
            float sx = sz.x > 0.0001f ? (halfW * 2f) / sz.x : halfW * 2f;
            float sy = sz.y > 0.0001f ? (halfH * 2f) / sz.y : halfH * 2f;
            transform.localScale = new Vector3(sx, sy, 1f);
            Data.boxHalfExtents = new Vector2(halfW, halfH);    // keep the box hitbox matched to the drawn rectangle (incl. the squash)
            Color c = Data.color; c.a = alpha; _sr.color = c;   // the spell always tints the page, art or not
        }

        // Reveal the page's spell as runtime child bullet(s) / an environment change at the page position.
        private void SpawnSpellEffect(BulletSystem sys)
        {
            Vector2 at = transform.position;
            switch (Data.spell)
            {
                case SpellType.Firebomb:
                    RequestChild(BuildFirebomb(sys, at), at);
                    break;
                case SpellType.Lightning:
                {
                    Vector2 p = Data.effectPoint != Vector2.zero ? Data.effectPoint : sys.ClampToArena(at);
                    RequestChild(BuildLightning(p), p);
                    break;
                }
                case SpellType.Water:
                    sys.RaiseWater(Data.waterRise, Data.waterMax, Data.waterRiseSeconds, Data.damage, Data.color, Data.effectSprite);
                    break;
                case SpellType.Earth:
                {
                    Rect b = sys.ArenaBounds;
                    // The pillar rises at the pre-rolled random x, not where the page detonated.
                    float x = Data.effectPoint != Vector2.zero ? Data.effectPoint.x : at.x;
                    Vector2 p = new Vector2(Mathf.Clamp(x, b.xMin, b.xMax), b.yMin);
                    RequestChild(BuildEarthPillar(p), p);
                    break;
                }
                case SpellType.Wind:
                    RequestChild(BuildWind(), at);
                    break;
                case SpellType.Mana:
                    SpawnManaFan(sys, at);
                    break;
            }
        }

        // Firebomb: a projectile aimed at a player that bursts into an expanding Explosion on hitting a
        // player or the arena edge. The projectile is harmless; the burst carries the damage.
        private bool TickFirebomb(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;
            transform.position += (Vector3)(Data.velocity * (Mathf.Max(0.1f, Data.projSpeed) * dt));
            transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);   // spin in flight (visual only — circle hitbox)
            ApplyVisual(Data.radius);
            // Trail: drop fading afterimages behind the bomb (harmless, visual only).
            _afterTimer += dt;
            if (Data.afterImageInterval > 0f && _afterTimer >= Data.afterImageInterval)
            {
                _afterTimer = 0f;
                var ai = Data;
                ai.behavior = BulletBehavior.AfterImage;
                ai.damage = 0;                          // damage 0 = never hit-tested
                ai.hitShape = BulletHitShape.Circle;    // circle render path, sized at Spawn (no 1-frame flash)
                ai.velocity = Vector2.zero;
                ai.rotationDeg = transform.eulerAngles.z;   // freeze the bomb's spin angle at this instant
                ai.lifetime = Mathf.Max(0.15f, Data.afterImageInterval * 3f);
                RequestChild(ai, transform.position);
            }
            // A bomb revealed OUTSIDE the box must first ENTER it before walls can burst it, and gets a
            // short grace after entry so the wall it just came through doesn't pop it immediately.
            bool inside = sys.InArena(transform.position);
            if (inside) { _pageEntered = true; _insideClock += dt; }
            bool wallsArmed = _pageEntered && _insideClock >= 0.15f;
            if ((wallsArmed && !inside) || HitsLocalIcon(sys, CurrentRadius) || Age >= Data.lifetime)
            {
                ExplodeRequested = true;   // BulletSystem spawns the shock circle from the explosion* fields
                return false;
            }
            return true;
        }

        private BulletSpawnData BuildFirebomb(BulletSystem sys, Vector2 at)
        {
            Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, at);
            Vector2 dir = target - at; dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.left;
            return new BulletSpawnData
            {
                behavior = BulletBehavior.Firebomb, hitShape = BulletHitShape.Circle,
                damage = 0, radius = Mathf.Max(0.1f, Data.radius), visualSize = 1f,
                velocity = dir, projSpeed = Mathf.Max(0.1f, Data.projSpeed),
                color = Data.color, sprite = Data.effectSprite, lifetime = 8f,
                afterImageInterval = Data.afterImageInterval, spinSpeedDeg = Data.effectSpinDeg,
                explosionRadius = Data.explosionRadius, explosionExpand = Data.explosionExpand,
                explosionLifetime = Data.explosionLifetime, explosionDamage = Data.explosionDamage,
                explosionColor = Data.explosionColor, explosionSprite = Data.explosionSprite,   // its own art, not the bomb's
            };
        }

        // Lightning: a warning circle fades in at the strike point, then a bolt strikes for damage in a
        // radius (the tall bolt sprite extends up above; the hit is only the bottom radius).
        private bool TickLightning(float dt)
        {
            float warn = Mathf.Max(0.01f, Data.warningDuration);
            float strike = Mathf.Max(0.05f, Data.strikeDuration);
            CurrentRadius = Data.strikeRadius;
            if (Age < warn)                                   // warning: harmless, grows 0 -> full radius while fading 0.25 -> 1
            {
                CurrentDamage = 0;
                if (_bolt != null && _bolt.gameObject.activeSelf) _bolt.gameObject.SetActive(false);
                if (_sr != null) _sr.enabled = true;
                float t = Mathf.Clamp01(Age / warn);
                ApplyVisual(Data.strikeRadius * t);
                SetAlpha(0.25f + 0.75f * t);
                return true;
            }
            if (Age < warn + strike)                          // strike: damage in the bottom radius
            {
                CurrentDamage = Data.damage;
                if (_sr != null) _sr.enabled = false;         // hide the marker; the bolt sprite is the visual
                ShowBolt(1f);
                return true;
            }
            float fade = Mathf.Max(0f, Data.fadeDuration);    // afterglow: the bolt fades out, harmless
            if (Age < warn + strike + fade)
            {
                CurrentDamage = 0;
                if (_sr != null) _sr.enabled = false;
                ShowBolt(1f - (Age - warn - strike) / Mathf.Max(0.0001f, fade));
                return true;
            }
            return false;
        }

        private BulletSpawnData BuildLightning(Vector2 p)
        {
            return new BulletSpawnData
            {
                behavior = BulletBehavior.Lightning, hitShape = BulletHitShape.Circle,
                damage = Data.damage, radius = Data.strikeRadius, visualSize = 1f,
                strikeRadius = Data.strikeRadius, warningDuration = Data.warningDuration,
                strikeDuration = Data.strikeDuration, strikeHeightMul = Data.strikeHeightMul,
                fadeDuration = Data.fadeDuration,
                color = Data.color, effectSprite = Data.effectSprite,
                lifetime = Data.warningDuration + Data.strikeDuration + Data.fadeDuration + 0.5f,
            };
        }

        // The tall bolt sprite: bottom pinned at the strike point, extending up with its aspect preserved.
        private void ShowBolt(float alpha)
        {
            transform.localScale = Vector3.one;               // so the child's world size isn't scaled by the marker
            transform.rotation = Quaternion.identity;
            if (_bolt == null)
            {
                var go = new GameObject("Bolt");
                go.transform.SetParent(transform, false);
                _boltSr = go.AddComponent<SpriteRenderer>();
                _boltSr.sortingOrder = 51;
                _bolt = go.transform;
            }
            Sprite spr = Data.effectSprite != null ? Data.effectSprite : PlayerDodgeIcon.MakeSquareSprite();
            _boltSr.sprite = spr;
            Color bc = Data.effectSprite != null ? Color.white : Data.color;
            bc.a = Mathf.Clamp01(alpha);
            _boltSr.color = bc;
            _bolt.gameObject.SetActive(true);

            float width = Mathf.Max(0.05f, Data.strikeRadius * 2f);
            Vector2 sz = spr.bounds.size;
            // Preserve the sprite's aspect: fit WIDTH to the strike diameter; height follows. Without art,
            // use strikeHeightMul as the aspect so the placeholder still reaches up above the strike.
            float aspect = (Data.effectSprite != null && sz.x > 0.0001f) ? sz.y / sz.x : Mathf.Max(1f, Data.strikeHeightMul);
            float height = width * aspect;
            _bolt.localScale = new Vector3(sz.x > 0.0001f ? width / sz.x : width,
                                           sz.y > 0.0001f ? height / sz.y : height, 1f);
            _bolt.localPosition = new Vector3(0f, height * 0.5f, 0f);   // bottom sits at the strike point
            _bolt.localRotation = Quaternion.identity;
        }

        // Earth pillar: a tall rock rectangle rises from below the floor to the ceiling, then sinks out.
        private bool TickEarthPillar(float dt, BulletSystem sys)
        {
            Rect b = sys.ArenaBounds;
            float halfH = Mathf.Max(0.05f, Data.boxHalfExtents.y);
            float topCenter = b.yMax - halfH;      // centre y where the pillar's top touches the ceiling
            float startCenter = b.yMin - halfH;    // centre y where the pillar's top is at the floor
            Vector2 pos = transform.position;
            if (!_launched) { _launched = true; pos.y = startCenter; _homingVel = Vector2.up; }
            pos.y += _homingVel.y * Data.riseSpeed * dt;
            if (_homingVel.y > 0f && pos.y >= topCenter) { pos.y = topCenter; _homingVel = Vector2.down; }
            transform.position = pos;
            CurrentDamage = Data.damage;
            ApplyBoxVisual(1f);
            if (_homingVel.y < 0f && pos.y <= startCenter - halfH) return false;   // fully sunk out
            return Age < Data.lifetime;
        }

        private BulletSpawnData BuildEarthPillar(Vector2 floorPoint)
        {
            return new BulletSpawnData
            {
                behavior = BulletBehavior.EarthPillar, hitShape = BulletHitShape.Box,
                damage = Data.damage, boxHalfExtents = Data.effectHalfExtents,
                riseSpeed = Mathf.Max(0.1f, Data.riseSpeed), visualSize = 1f,
                color = Data.color, sprite = Data.effectSprite, lifetime = 30f,
                maskInside = true,   // occluded to the battlefield: no pillar poking out above/below the box
            };
        }

        // Wind: an invisible controller that blows a horizontal force on the player icons + spawns drifting
        // streak visuals for its lifetime, then clears the force.
        private bool TickWind(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;
            sys.SetWind(new Vector2(Data.windDir * Data.windStrength, 0f));
            _afterTimer += dt;
            if (Data.windStreakInterval > 0f && _afterTimer >= Data.windStreakInterval)
            {
                _afterTimer = 0f;
                SpawnWindStreak(sys);
            }
            if (Age >= Data.lifetime) { sys.SetWind(Vector2.zero); return false; }
            return true;
        }

        private BulletSpawnData BuildWind()
        {
            return new BulletSpawnData
            {
                behavior = BulletBehavior.Wind, hitShape = BulletHitShape.None,
                windStrength = Data.windStrength, windDir = Data.windDir >= 0f ? 1f : -1f,
                windStreakInterval = Data.windStreakInterval,
                color = Data.color, lifetime = Mathf.Max(0.5f, Data.windDuration),
            };
        }

        private void SpawnWindStreak(BulletSystem sys)
        {
            // Streaks blow across the WHOLE screen but are stencil-hidden inside the battlefield
            // (VisibleOutsideMask vs the arena mask), so the box itself stays visually calm.
            Rect v = sys.ViewBounds;
            float y = Random.Range(v.yMin, v.yMax);              // visual only -> runtime Random is fine (not hit-tested)
            float x = Data.windDir > 0f ? v.xMin - 0.6f : v.xMax + 0.6f;
            float speed = Mathf.Abs(Data.windStrength) * 0.6f + 3f;
            var s = new BulletSpawnData
            {
                behavior = BulletBehavior.Linear, hitShape = BulletHitShape.Box, damage = 0,
                boxHalfExtents = new Vector2(0.5f, 0.04f), visualSize = 1f,
                velocity = new Vector2(Data.windDir * speed, 0f),
                color = new Color(1f, 1f, 1f, 0.35f),
                lifetime = (v.width + 1.2f) / speed + 0.5f,      // long enough to cross the view
                maskOutside = true,
            };
            RequestChild(s, new Vector2(x, y));
        }

        // Mana: a fan of straight projectiles (0, ±step, ±2·step …) whose centre is aimed at a player.
        private void SpawnManaFan(BulletSystem sys, Vector2 at)
        {
            Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, at);
            Vector2 baseDir = target - at; baseDir = baseDir.sqrMagnitude > 0.0001f ? baseDir.normalized : Vector2.left;
            float baseAng = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
            int n = Mathf.Max(1, Data.manaCount);
            float step = Data.manaSpreadStepDeg;
            for (int i = 0; i < n; i++)
            {
                float ang = baseAng;
                if (i > 0) { int k = (i + 1) / 2; float sign = (i % 2 == 1) ? 1f : -1f; ang += sign * k * step; }
                float r = ang * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
                var m = new BulletSpawnData
                {
                    behavior = BulletBehavior.Linear, hitShape = BulletHitShape.Box, damage = Data.damage,
                    velocity = dir * Mathf.Max(0.1f, Data.projSpeed),
                    boxHalfExtents = Data.effectHalfExtents, visualSize = 1f,   // x = half-length along travel, y = half-thickness
                    // The 64×16 bolt art points LEFT: flipX turns it right-facing, then rotate to travel.
                    rotationDeg = ang, flipXOnSpawn = true,
                    color = Data.color, sprite = Data.effectSprite, lifetime = 8f,
                };
                RequestChild(m, at);
            }
        }

        // Water: a persistent zone rising from the floor (height = the arena's accumulated water level);
        // standing in it deals damage. The single zone bullet lives for the whole round (see BulletSystem).
        private bool TickWater(float dt, BulletSystem sys)
        {
            Rect b = sys.ArenaBounds;
            float level = Mathf.Clamp01(sys.WaterLevel01);
            float h = b.height * level;
            if (h <= 0.001f)
            {
                CurrentDamage = 0;
                if (_sr != null) _sr.enabled = false;
                return Age < Data.lifetime;
            }
            float halfH = h * 0.5f;
            transform.position = new Vector2(b.center.x, b.yMin + halfH);
            transform.rotation = Quaternion.identity;
            Data.boxHalfExtents = new Vector2(b.width * 0.5f, halfH);   // grows with the level (Overlaps reads this)
            CurrentDamage = Data.damage;
            ApplyBoxVisual(0.5f);                                       // translucent water fill
            return Age < Data.lifetime;
        }

        // Does the local dodge icon overlap a circle of the given radius at this bullet's position?
        private bool HitsLocalIcon(BulletSystem sys, float radius)
        {
            var ic = sys.LocalIcon;
            if (ic == null || ic.IsDead) return false;
            float rr = Mathf.Max(0.01f, radius) + ic.Radius;
            return ((Vector2)transform.position - (Vector2)ic.transform.position).sqrMagnitude <= rr * rr;
        }

        // ---- Horus (boss 4) behaviours -------------------------------------

        // A thrown apple under gravity. Apple Chuck: arcs into the box then falls out (culled off-screen).
        // Explosive Apples: bursts into a shower of upward apples on hitting the floor.
        private bool TickGravityApple(float dt, BulletSystem sys)
        {
            _vel.y -= Data.gravity * dt;
            transform.position += (Vector3)(_vel * dt);
            transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);   // spin/tumble in flight (visual only)
            CurrentDamage = Data.damage;
            ApplyVisual(Data.radius);

            Rect b = sys.ArenaBounds;
            if (Data.burstOnFloor && _vel.y < 0f && transform.position.y <= b.yMin)
            {
                BurstApples(sys, b);
                return false;
            }
            if (Age >= Data.lifetime) return false;
            return !sys.IsCulled(transform.position);   // falls out of the battlefield -> culled
        }

        // Explosive burst: spawn `burstCount` apples going up with deterministic random x velocities (seeded
        // per apple so every client bursts identically), which then fall back down and out.
        private void BurstApples(BulletSystem sys, Rect b)
        {
            var rng = new System.Random(Data.randomSeed);
            int n = Mathf.Max(1, Data.burstCount);
            float baseX = transform.position.x;
            for (int i = 0; i < n; i++)
            {
                float vx = (float)(rng.NextDouble() * 2.0 - 1.0) * Data.burstSpeedX;
                float vy = Data.burstUpSpeed + (float)(rng.NextDouble() * 2.0 - 1.0) * Data.burstUpVariance;
                float tumble = ((float)rng.NextDouble() * 480f + 240f) * (rng.Next(2) == 0 ? -1f : 1f);
                var a = new BulletSpawnData
                {
                    behavior = BulletBehavior.GravityApple, hitShape = BulletHitShape.Circle,
                    damage = Data.damage, visualSize = 1f,
                    radius = Data.burstRadius > 0f ? Data.burstRadius : Data.radius,
                    gravity = Data.gravity, velocity = new Vector2(vx, Mathf.Max(0.5f, vy)),
                    spinSpeedDeg = tumble,
                    color = Data.color, sprite = Data.effectSprite,   // burst -> the "regular" apple sprite
                    maskInside = Data.maskInside, lifetime = 8f, burstOnFloor = false,
                };
                RequestChild(a, new Vector2(baseX, b.yMin + 0.05f));
            }
        }

        // Joint Horse Rider controller: switches the arena into ride mode (players jump their horses over
        // scrolling obstacles) for its whole life, then restores normal free movement.
        private bool TickHorseRide(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;
            sys.SetRideMode(true, Data.rideGravity, Data.rideJumpVelocity, Data.rideMaxJumpHold, Data.rideLowGravityFactor, Data.sprite, Data.rideArenaExtraHeight, Data.rideSpeedMul);
            if (Age >= Data.lifetime) { sys.SetRideMode(false, 0f, 0f, 0f, 0f, null, 0f, 1f); return false; }
            return true;
        }

        // ---- Ryomi hitbox + slash visuals ----------------------------------

        /// <summary>Does the dodge icon (small circle) overlap this bullet's current hitbox?</summary>
        public bool Overlaps(Vector2 iconCenter, float iconRadius)
        {
            switch (Data.hitShape)
            {
                case BulletHitShape.None:
                    return false;
                case BulletHitShape.Box:
                    return BulletMotion.CircleOverlapsBox(iconCenter, iconRadius, transform.position, Data.boxHalfExtents, transform.eulerAngles.z);
                case BulletHitShape.Cross:
                {
                    float half = _crossThickness * 0.5f;
                    if (half <= 0f) return false;
                    float ang = transform.eulerAngles.z;
                    Vector2 c = transform.position;
                    return BulletMotion.CircleOverlapsBox(iconCenter, iconRadius, c, new Vector2(Data.crossArmLength, half), ang)
                        || BulletMotion.CircleOverlapsBox(iconCenter, iconRadius, c, new Vector2(half, Data.crossArmLength), ang);
                }
                default: // Circle
                    float rr = CurrentRadius + iconRadius;
                    return ((Vector2)transform.position - iconCenter).sqrMagnitude <= rr * rr;
            }
        }

        private void EnsureBars()
        {
            if (_barH != null) return;
            var sq = PlayerDodgeIcon.MakeSquareSprite();
            _barH = MakeBar("BarH", sq);
            _barV = MakeBar("BarV", sq);
        }

        private SpriteRenderer MakeBar(string barName, Sprite sq)
        {
            var go = new GameObject(barName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sq;
            sr.sortingOrder = 51;   // just above bullets (50)
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;   // clip the long arms to the arena
            return sr;
        }

        // Small crosshair telegraph marker (child; kept off the mask so it's always visible inside the box).
        // During the pre-shot windup it swaps to `windupSprite` (if set) to signal "about to fire";
        // with no windup sprite it falls back to flashing solid white.
        private void ShowCrosshair(float alpha, bool windup = false)
        {
            if (_crosshair == null)
            {
                var go = new GameObject("Crosshair");
                go.transform.SetParent(transform, false);
                _crosshair = go.AddComponent<SpriteRenderer>();
                _crosshair.sortingOrder = 52;   // above the bars
            }
            bool useWindupArt = windup && Data.windupSprite != null;
            Sprite normal = Data.crosshairSprite != null ? Data.crosshairSprite
                          : (Data.sprite != null ? Data.sprite : PlayerDodgeIcon.MakeCircleSprite());
            _crosshair.sprite = useWindupArt ? Data.windupSprite : normal;

            float size = Data.crosshairSize > 0f ? Data.crosshairSize : 0.5f;
            _crosshair.gameObject.SetActive(true);
            _crosshair.transform.localScale = new Vector3(size, size, 1f);

            bool hasArt = useWindupArt || Data.crosshairSprite != null || Data.sprite != null;
            Color c = hasArt ? Color.white                        // real art shows its own colours
                     : (windup ? Color.white : Data.color);       // placeholder: white flash during windup
            c.a = alpha;
            _crosshair.color = c;
        }

        private void HideCrosshair()
        {
            if (_crosshair != null && _crosshair.gameObject.activeSelf) _crosshair.gameObject.SetActive(false);
        }

        // Position/scale the two cross bars in LOCAL space; the parent's rotation orients the cross.
        private void UpdateCrossBars(float alpha)
        {
            EnsureBars();
            float len = Data.crossArmLength * 2f;
            float th = Mathf.Max(0.001f, _crossThickness);
            Color col = Data.color; col.a = alpha;
            _barH.gameObject.SetActive(true); _barV.gameObject.SetActive(true);
            _barH.transform.localPosition = Vector3.zero; _barV.transform.localPosition = Vector3.zero;
            _barH.transform.localRotation = Quaternion.identity; _barV.transform.localRotation = Quaternion.identity;
            _barH.transform.localScale = new Vector3(len, th, 1f);   // long in local X
            _barV.transform.localScale = new Vector3(th, len, 1f);   // long in local Y
            _barH.color = col; _barV.color = col;
        }

        // Size the main sprite to the box (SlidingCut / AfterImage) and fade by alpha.
        private void ApplyBoxVisual(float alpha)
        {
            if (_sr == null) return;
            Vector2 sz = _sr.sprite != null ? (Vector2)_sr.sprite.bounds.size : Vector2.one;
            float sx = sz.x > 0.0001f ? (Data.boxHalfExtents.x * 2f) / sz.x : Data.boxHalfExtents.x * 2f;
            float sy = sz.y > 0.0001f ? (Data.boxHalfExtents.y * 2f) / sz.y : Data.boxHalfExtents.y * 2f;
            transform.localScale = new Vector3(sx, sy, 1f);
            _sr.enabled = true;
            var c = _sr.color; c.a = alpha; _sr.color = c;
        }

        // ---- Visual helpers ------------------------------------------------

        /// <summary>Scale the sprite so its rendered diameter matches the collision diameter
        /// (2·radius·visualSize) — regardless of the sprite's native pixel size. So the art sits
        /// exactly over the hitbox by default; <see cref="BulletSpawnData.visualSize"/> (usually 1)
        /// remains a deliberate over/undersize knob.</summary>
        private void ApplyVisual(float radius)
        {
            float targetDiameter = Mathf.Max(0.05f, radius * 2f * Data.visualSize);
            Vector2 sz = _sr != null && _sr.sprite != null ? (Vector2)_sr.sprite.bounds.size : Vector2.one;
            float basis = Mathf.Max(sz.x, sz.y);                       // fit the sprite's larger side to the diameter
            float scale = basis > 0.0001f ? targetDiameter / basis : targetDiameter;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void SetAlpha(float a)
        {
            if (_sr == null) return;
            var c = _sr.color;
            c.a = a;
            _sr.color = c;
        }

        /// <summary>Dev overlay: a translucent circle drawn at the TRUE collision radius, on top of
        /// the art, toggled by the dev panel's "Show hitboxes". Countervails the parent's sprite-fit
        /// scale so its world diameter is always exactly 2·<see cref="CurrentRadius"/>.</summary>
        private void UpdateHitboxOverlay()
        {
            // The dev overlay is a circle; only meaningful for circle-shaped bullets. (Box/Cross
            // slashes render their own solid shape, which already reads as the hitbox.)
            bool show = DebugView.ShowHitboxes && Active && Data.hitShape == BulletHitShape.Circle;
            if (!show)
            {
                if (_hitbox != null && _hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(false);
                return;
            }
            if (_hitbox == null)
            {
                var go = new GameObject("Hitbox");
                go.transform.SetParent(transform, false);
                _hitboxSr = go.AddComponent<SpriteRenderer>();
                _hitboxSr.sprite = PlayerDodgeIcon.MakeCircleSprite();  // unit circle (bounds 1×1)
                _hitboxSr.color = new Color(1f, 0.25f, 0.25f, 0.45f);
                _hitboxSr.sortingOrder = 60;                            // above the bullet art (50)
                _hitbox = go.transform;
            }
            if (!_hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(true);
            _hitbox.rotation = Quaternion.identity;
            float px = Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.x));
            float py = Mathf.Max(0.0001f, Mathf.Abs(transform.localScale.y));
            float d = 2f * CurrentRadius;
            _hitbox.localScale = new Vector3(d / px, d / py, 1f);       // world diameter == 2·radius
        }
    }
}

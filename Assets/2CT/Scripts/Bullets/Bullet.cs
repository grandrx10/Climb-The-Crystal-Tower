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
        private float _crossThickness;         // current bar thickness (grows 0→crossThickness)
        private Vector2 _homingVel;            // TrackingCut homing velocity
        private float _fillClock;              // TrackingCut: progress toward the next detonation
        private float _fillDur;                // TrackingCut: current fill duration (shrinks each cycle)
        private float _afterTimer;             // SlidingCut: time since the last afterimage drop

        // One child a bullet can ask the system to spawn this frame (TrackingCut detonation cross,
        // SlidingCut afterimage). Read + cleared by BulletSystem after Tick.
        private bool _pendingChild;
        private BulletSpawnData _childData;
        private Vector2 _childPos;
        public bool HasPendingChild => _pendingChild;
        public BulletSpawnData PendingChild => _childData;
        public Vector2 PendingChildPos => _childPos;
        public void ClearPendingChild() => _pendingChild = false;
        private void RequestChild(in BulletSpawnData d, Vector2 pos) { _childData = d; _childPos = pos; _pendingChild = true; }

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
            _afterTimer = 0f;
            _pendingChild = false;
            if (_barH != null) _barH.gameObject.SetActive(false);
            if (_barV != null) _barV.gameObject.SetActive(false);

            // Orientation: explosions spin randomly (purely visual, circle hitbox); Box/Cross slashes
            // use their authored rotation; everything else stays upright (also clears pooled spin).
            if (data.behavior == BulletBehavior.Explosion)
                transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            else if (data.hitShape == BulletHitShape.Box || data.hitShape == BulletHitShape.Cross)
                transform.rotation = Quaternion.Euler(0f, 0f, data.rotationDeg);
            else
                transform.rotation = Quaternion.identity;
            if (_sr != null)
            {
                // Real art shows its own colours (rendered white, tint only carries alpha for fades);
                // the placeholder circle uses the flat `color`. Either way SetAlpha drives the fade.
                _sr.sprite = data.sprite != null ? data.sprite : _defaultSprite;
                _sr.color = data.sprite != null ? Color.white : data.color;
                _sr.enabled = true;
            }
            // Circle bullets size the sprite to their radius. Box/Cross/None (Ryomi slashes, crosshairs,
            // afterimages, lasso) keep the transform at scale 1 and size their sprite/bars in Tick.
            if (data.hitShape == BulletHitShape.Circle)
                ApplyVisual(data.behavior == BulletBehavior.GrowExplode || data.behavior == BulletBehavior.Explosion ? 0f : data.radius);
            else
                transform.localScale = Vector3.one;
            if (data.behavior == BulletBehavior.Lasso && _sr != null) _sr.enabled = false;   // invisible controller
            UpdateHitboxOverlay();
            gameObject.SetActive(true);
        }

        public void Despawn()
        {
            Active = false;
            if (_sr != null) _sr.enabled = false;
            if (_barH != null) _barH.gameObject.SetActive(false);
            if (_barV != null) _barV.gameObject.SetActive(false);
            if (_hitbox != null && _hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(false);
            _pendingChild = false;
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
                default:                            alive = TickLinear(dt, sys); break;
            }
            if (alive) UpdateHitboxOverlay();   // track the live radius (bubbles grow/explode)
            return alive;
        }

        // ---- Behaviours ----------------------------------------------------

        private bool TickLinear(float dt, BulletSystem sys)
        {
            transform.position += (Vector3)(Data.velocity * dt);
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

        // Marked Strike: crosshair fades in (telegraph, harmless) at its frozen spot, then slashes a cross.
        private bool TickMarkedStrike(float dt, BulletSystem sys)
        {
            if (!_launched)
            {
                // Freeze the crosshair near the target: its live position at spawn + the authored offset.
                _launched = true;
                Vector2 tp = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
                Vector2 off = new Vector2(Mathf.Cos(Data.spawnAngle), Mathf.Sin(Data.spawnAngle)) * Data.spawnDist;
                transform.position = tp + off;
            }
            if (Age < Data.telegraphDuration)
            {
                CurrentDamage = 0;
                _crossThickness = 0f;
                if (_barH != null) _barH.gameObject.SetActive(false);
                if (_barV != null) _barV.gameObject.SetActive(false);
                float p = Data.telegraphDuration > 0f ? Age / Data.telegraphDuration : 1f;
                SetAlpha(0.15f + 0.55f * Mathf.Clamp01(p));   // crosshair fades in
                return true;
            }
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
                ai.damage = 0;
                ai.hitShape = BulletHitShape.None;
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
            ApplyBoxVisual(Mathf.Clamp01(1f - Age / Mathf.Max(0.0001f, Data.lifetime)));   // fade behind the cut
            return Age < Data.lifetime;
        }

        // Ricochet: linear, reflecting off the arena walls; expires after its lifetime.
        private bool TickRicochet(float dt, BulletSystem sys)
        {
            if (!_launched)
            {
                // Aim at a random player at spawn (then it just bounces).
                _launched = true;
                Vector2 t = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
                Vector2 dir = t - (Vector2)transform.position;
                Data.velocity = (dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.left) * Data.speed;
            }
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
            CurrentDamage = Data.damage;
            return Age < Data.lifetime;
        }

        // Tracking Cut: a spinning crosshair that homes on a player (accelerating, capped), periodically
        // detonating a stationary SlashCross and speeding up. The crosshair itself never deals damage.
        private bool TickTrackingCut(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;   // harmless; the detonated cross is the hazard

            Vector2 target = sys.ResolveTargetPosition(Data.targetSelect, transform.position);
            Vector2 toTarget = target - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                _homingVel += toTarget.normalized * (Data.homingAccel * dt);
            if (Data.homingMaxSpeed > 0f && _homingVel.magnitude > Data.homingMaxSpeed)
                _homingVel = _homingVel.normalized * Data.homingMaxSpeed;
            transform.position += (Vector3)(_homingVel * dt);
            transform.Rotate(0f, 0f, Data.spinSpeedDeg * dt);   // spin

            _fillClock += dt;
            float p = _fillDur > 0f ? Mathf.Clamp01(_fillClock / _fillDur) : 1f;
            SetAlpha(Mathf.Lerp(0.25f, 1f, p));
            if (_fillClock >= _fillDur)
            {
                _fillClock = 0f;
                _fillDur = Mathf.Max(Data.fillFloor, _fillDur - Data.fillSpeedup);   // faster each cycle
                var cross = Data;
                cross.behavior = BulletBehavior.SlashCross;
                cross.hitShape = BulletHitShape.Cross;
                cross.rotationDeg = transform.eulerAngles.z;   // detonate at the crosshair's current spin
                cross.telegraphDuration = 0f;
                RequestChild(cross, transform.position);        // stationary cross left behind
            }
            return Age < Data.lifetime;
        }

        // Lasso: invisible controller that drags the defend arena up/down for the pattern's duration.
        private bool TickLasso(float dt, BulletSystem sys)
        {
            CurrentDamage = 0;
            float range = Mathf.Max(0f, Data.lassoRange);
            float period = (range > 0f && Data.lassoSpeed > 0f) ? 4f * range / Data.lassoSpeed : 1f;
            float phase = Mathf.Repeat(Age, period) / period;   // 0..1, deterministic from Age
            float tri = phase < 0.5f ? Mathf.Lerp(-1f, 1f, phase * 2f) : Mathf.Lerp(1f, -1f, (phase - 0.5f) * 2f);
            sys.SetArenaLassoOffset(tri * range);
            if (Age >= Data.lifetime) { sys.SetArenaLassoOffset(0f); return false; }
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
            return sr;
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

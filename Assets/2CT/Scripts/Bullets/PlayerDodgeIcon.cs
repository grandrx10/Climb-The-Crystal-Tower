using TwoCT.Combat;
using TwoCT.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TwoCT.Bullets
{
    /// <summary>
    /// A player's small controllable icon inside the defend arena (the "heart"). The local
    /// player moves it with WASD/arrows/left-stick and it writes its position for peers; remote
    /// icons just follow the synced position. Handles the 0.5s post-hit invincibility + blink.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerDodgeIcon : MonoBehaviour
    {
        // Dodge speed = the player's character move-speed stat × the arena's slow multiplier.
        [Tooltip("Collision radius — the TRUE hitbox, independent of the icon's display size.")]
        [SerializeField] private float baseRadius = 0.175f;   // 1.75× the original 0.1
        [Tooltip("Display scale for a character's dodge icon (the circle fallback uses the hitbox size instead).")]
        [SerializeField] private float iconScale = 1.4f;
        [SerializeField] private float invincibleSeconds = 0.5f;
        [SerializeField] private float enlargeScale = 1.1f;

        private PlayerCombatant _player;
        private DefendArena _arena;
        private SpriteRenderer _sr;
        private float _invincibleUntil;
        private float _blinkClock;
        private bool _hasCustomIcon;
        private Transform _hitbox;      // dev overlay showing the true collision circle

        // ---- Horus: Joint Horse Rider ride-mode state ----------------------
        private const float HorseWorldW = 0.9f;   // ridden horse world size — part of the rider's "body":
        private const float HorseWorldH = 0.6f;   // the floor sits under the HORSE's hooves, not the icon
        private bool _wasRiding;        // were we in ride mode last frame? (detects enter/exit)
        private float _rideFacingX = 1f;   // horizontal facing (+1 right, -1 left); horse art faces LEFT
        private float _prevXForFacing;  // last frame's x, to derive facing (works for local + remote icons)
        private float _vy;              // vertical velocity (jump physics)
        private bool _grounded;
        private bool _holdingJump;      // in the "floaty" part of a held jump (variable jump height)
        private float _jumpHold;        // time spent holding the current jump
        private bool _prevJump;         // jump key state last frame (edge-detect the takeoff press)
        private Transform _horse;       // the ridden horse sprite (child, shown in ride mode)
        private SpriteRenderer _horseSr;

        public bool IsLocal => _player != null && _player.IsOwner;
        public bool IsInvincible => Time.time < _invincibleUntil;
        public bool IsDead => _player == null || !_player.IsAlive;
        public PlayerCombatant Player => _player;
        public float Radius => baseRadius * (_player != null && _player.EnlargeActive.Value ? enlargeScale : 1f);

        public void Bind(PlayerCombatant player, DefendArena arena, Color color, Vector2 startPos)
        {
            _player = player;
            _arena = arena;
            _sr = GetComponent<SpriteRenderer>();

            // Use the chosen character's dodge icon (untinted) if it has one; else the slot-coloured circle.
            var icon = player != null && player.Character != null ? player.Character.dodgeIcon : null;
            if (icon != null) { _sr.sprite = icon; _sr.color = Color.white; _hasCustomIcon = true; }
            else { if (_sr.sprite == null) _sr.sprite = MakeCircleSprite(); _sr.color = color; _hasCustomIcon = false; }

            _sr.sortingOrder = 60;                       // draw above bullets so you always see yourself
            transform.position = startPos;
            EnsureHitboxOverlay();
            ApplyScale();
            if (IsLocal) _player.ArenaPos.Value = startPos;
        }

        /// <summary>Visual size — a custom icon uses its own display scale; the circle fallback matches
        /// the collision diameter. The two are deliberately independent (image ≠ hitbox).</summary>
        private void ApplyScale()
        {
            float enlarge = _player != null && _player.EnlargeActive.Value ? enlargeScale : 1f;
            transform.localScale = _hasCustomIcon ? Vector3.one * (iconScale * enlarge)
                                                  : Vector3.one * (Radius * 2f);
            UpdateHitboxOverlay();
        }

        private void EnsureHitboxOverlay()
        {
            if (_hitbox != null) return;
            var go = new GameObject("Hitbox");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeCircleSprite();
            sr.color = new Color(1f, 0.25f, 0.25f, 0.5f);
            sr.sortingOrder = 70;                        // above the icon
            _hitbox = go.transform;
            go.SetActive(DebugView.ShowHitboxes);
        }

        private void UpdateHitboxOverlay()
        {
            if (_hitbox == null) return;
            if (_hitbox.gameObject.activeSelf != DebugView.ShowHitboxes) _hitbox.gameObject.SetActive(DebugView.ShowHitboxes);
            if (!DebugView.ShowHitboxes) return;
            // Counter the parent's visual scale so the overlay's WORLD diameter == 2*Radius (the real hitbox).
            float parent = Mathf.Max(0.0001f, transform.localScale.x);
            _hitbox.localScale = Vector3.one * ((2f * Radius) / parent);
        }

        private void Update()
        {
            if (_player == null) return;
            ApplyScale();   // visual size (decoupled from the hitbox) + refresh the hitbox overlay

            if (IsDead)
            {
                // Knocked out mid-defend: freeze in place, go grey, and stop absorbing hits
                // (BulletSystem skips hit-tests on a dead icon).
                if (_sr != null) _sr.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                return;
            }

            // Horus's Joint Horse Rider swaps free movement for jump physics while ride mode is on.
            bool riding = _arena != null && _arena.RideMode;
            if (riding && !_wasRiding) EnterRide();
            else if (!riding && _wasRiding) ExitRide();
            if (riding) ShowHorse();

            if (IsLocal)
            {
                if (riding) RidePhysics();
                else FreeMove();
            }
            else
            {
                // Remote icon: smoothly follow the owner's synced position (free-move or ride, same channel).
                transform.position = Vector2.Lerp(transform.position, _player.ArenaPos.Value, 20f * Time.deltaTime);
            }

            // Blink while invincible.
            if (IsInvincible)
            {
                _blinkClock += Time.deltaTime;
                var c = _sr.color; c.a = Mathf.PingPong(_blinkClock * 12f, 1f) > 0.5f ? 0.25f : 1f; _sr.color = c;
            }
            else
            {
                var c = _sr.color; c.a = 1f; _sr.color = c;
            }
        }

        // Free WASD dodging (the normal defend mode). Base speed is the character stat, slowed by the arena;
        // Marnu's Wind spell adds a horizontal push (zero when no wind).
        private void FreeMove()
        {
            Vector2 dir = ReadMoveInput();
            Vector2 speed = _player.MoveSpeed * _arena.SpeedMultiplier;
            Vector2 step = new Vector2(dir.x * speed.x, dir.y * speed.y) * Time.deltaTime;
            step += _arena.WindForce * Time.deltaTime;
            Vector2 next = _arena.Clamp((Vector2)transform.position + step);
            transform.position = next;
            _player.ArenaPos.Value = next;   // replicate to peers
        }

        // ---- Joint Horse Rider (Horus) -------------------------------------
        private void EnterRide()
        {
            _wasRiding = true;
            var inner = _arena.InnerBounds;
            // Start near the left, one spot per seat, so every rider is visible and obstacles scroll in
            // from the right toward them (endless-runner style). A/D still steers left/right after this.
            float startX = Mathf.Clamp(inner.xMin + inner.width * 0.2f + _player.Slot.Value * 0.6f,
                                       inner.xMin + Radius, inner.xMax - Radius);
            _vy = 0f; _grounded = true; _holdingJump = false; _jumpHold = 0f;
            _prevJump = JumpHeld();   // don't auto-jump if W happened to be held on entry
            _rideFacingX = 1f;        // face the incoming obstacles until the player steers
            _prevXForFacing = startX;
            if (IsLocal)
            {
                Vector2 start = new Vector2(startX, RideFloorY(inner));
                transform.position = start;
                _player.ArenaPos.Value = start;
            }
        }

        // The rider's floor: the horse under the icon is part of the body, so the icon rests a full
        // horse-height above the battlefield floor (hooves on the ground, never through it).
        private float RideFloorY(Rect inner) => inner.yMin + HorseWorldH + Radius;

        private void ExitRide()
        {
            _wasRiding = false;
            if (_horse != null) _horse.gameObject.SetActive(false);
        }

        // Gravity + variable-height jump: tap W for a hop, hold it (up to RideMaxJumpHold) for a higher
        // jump. A/D still steers the horse left/right at the normal dodge speed.
        private void RidePhysics()
        {
            var inner = _arena.InnerBounds;
            float floorY = RideFloorY(inner);
            float ceilY = inner.yMax - Radius;
            bool jump = JumpHeld();

            if (_grounded && jump && !_prevJump)   // fresh press while grounded -> take off
            {
                _vy = _arena.RideJumpVelocity;
                _grounded = false; _holdingJump = true; _jumpHold = 0f;
            }
            if (_holdingJump)
            {
                _jumpHold += Time.deltaTime;
                if (!jump || _jumpHold > _arena.RideMaxJumpHold || _vy <= 0f) _holdingJump = false;
            }
            float g = _arena.RideGravity * (_holdingJump ? Mathf.Max(0f, _arena.RideLowGravityFactor) : 1f);
            _vy -= g * Time.deltaTime;

            float y = transform.position.y + _vy * Time.deltaTime;
            if (y >= ceilY) { y = ceilY; if (_vy > 0f) _vy = 0f; }
            if (y <= floorY) { y = floorY; _vy = 0f; _grounded = true; _holdingJump = false; }

            float x = transform.position.x + ReadMoveInput().x * _player.MoveSpeed.x * _arena.SpeedMultiplier
                      * _arena.RideSpeedMul * Time.deltaTime;
            x = Mathf.Clamp(x, inner.xMin + Radius, inner.xMax - Radius);

            Vector2 next = new Vector2(x, y);
            transform.position = next;
            _player.ArenaPos.Value = next;
            _prevJump = jump;
        }

        private static bool JumpHeld()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed || kb.spaceKey.isPressed)) return true;
            var gp = Gamepad.current;
            if (gp != null && (gp.buttonSouth.isPressed || gp.leftStick.up.isPressed)) return true;
            return false;
        }

        // The ridden horse: a child sprite beneath the rider, shown while ride mode is on. Its world size is
        // kept stable by countering the icon's own display scale.
        private void ShowHorse()
        {
            if (_horse == null)
            {
                var go = new GameObject("Horse");
                go.transform.SetParent(transform, false);
                _horseSr = go.AddComponent<SpriteRenderer>();
                _horseSr.sortingOrder = 58;   // just under the rider icon (60)
                _horse = go.transform;
            }
            if (!_horse.gameObject.activeSelf) _horse.gameObject.SetActive(true);
            Sprite spr = _arena.RideHorseSprite != null ? _arena.RideHorseSprite : MakeSquareSprite();
            _horseSr.sprite = spr;
            _horseSr.color = _arena.RideHorseSprite != null ? Color.white : new Color(0.5f, 0.35f, 0.2f);   // brown placeholder
            // Face the way we're moving (derived from position so it also works for remote icons).
            float dx = transform.position.x - _prevXForFacing;
            if (Mathf.Abs(dx) > 0.0005f) _rideFacingX = Mathf.Sign(dx);
            _prevXForFacing = transform.position.x;
            _horseSr.flipX = _rideFacingX > 0f;   // the art faces LEFT; flip it to face right
            float parent = Mathf.Max(0.0001f, transform.localScale.x);
            Vector2 sz = spr.bounds.size;
            _horse.localScale = new Vector3((HorseWorldW / Mathf.Max(0.0001f, sz.x)) / parent,
                                            (HorseWorldH / Mathf.Max(0.0001f, sz.y)) / parent, 1f);
            _horse.localPosition = new Vector3(0f, -(Radius + HorseWorldH * 0.5f) / parent, 0f);   // beneath the rider
            _horse.localRotation = Quaternion.identity;
        }

        private static Vector2 ReadMoveInput()
        {
            Vector2 v = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1;
            }
            var gp = Gamepad.current;
            if (gp != null && v == Vector2.zero) v = gp.leftStick.ReadValue();
            return v; // per-axis: no normalisation, so holding two directions dodges faster diagonally
        }

        /// <summary>Called by the local bullet sim when this icon is hit. Applies i-frames + reports damage.</summary>
        public void RegisterHit(int rawDamage)
        {
            if (!IsLocal || IsInvincible || IsDead) return;
            _invincibleUntil = Time.time + invincibleSeconds;
            _blinkClock = 0f;
            _player.ReportBulletHitServerRpc(rawDamage);
        }

        // ---- Placeholder art helpers ---------------------------------------
        public static PlayerDodgeIcon CreateFallback(Transform parent)
        {
            var go = new GameObject("DodgeIcon");
            go.transform.SetParent(parent, false);
            var icon = go.AddComponent<PlayerDodgeIcon>();
            return icon;
        }

        private static Sprite _circle;
        public static Sprite MakeCircleSprite()
        {
            if (_circle != null) return _circle;
            const int r = 16;
            var tex = new Texture2D(r * 2, r * 2, TextureFormat.RGBA32, false);
            for (int y = 0; y < r * 2; y++)
                for (int x = 0; x < r * 2; x++)
                {
                    float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
                    tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
                }
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, r * 2, r * 2), new Vector2(0.5f, 0.5f), r * 2);
            return _circle;
        }

        private static Sprite _square;
        /// <summary>A 1×1 white square sprite (1 world unit at scale 1). Used for the arena frame/bars.</summary>
        public static Sprite MakeSquareSprite()
        {
            if (_square != null) return _square;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            return _square;
        }
    }
}

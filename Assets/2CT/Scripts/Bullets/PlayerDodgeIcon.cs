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

            if (IsLocal)
            {
                Vector2 dir = ReadMoveInput();
                // Base speed is the player's character stat; the arena slows it via its multiplier.
                Vector2 speed = _player.MoveSpeed * _arena.SpeedMultiplier;
                Vector2 step = new Vector2(dir.x * speed.x, dir.y * speed.y) * Time.deltaTime;
                Vector2 next = _arena.Clamp((Vector2)transform.position + step);
                transform.position = next;
                _player.ArenaPos.Value = next; // replicate to peers
            }
            else
            {
                // Remote icon: smoothly follow the owner's synced position.
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

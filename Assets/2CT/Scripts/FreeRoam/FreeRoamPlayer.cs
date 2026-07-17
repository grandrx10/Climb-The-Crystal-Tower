using TwoCT.Bullets;
using TwoCT.Combat;
using TwoCT.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Free-roam movement for a player. Owner-authoritative: the local owner reads input, moves,
    /// and replicates position + facing; peers interpolate. No sprite animation — the body
    /// "wobbles" (tilts) while walking and flips on the X axis when changing direction (all
    /// characters authored facing right). Active only inside a FreeRoamContext scene.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class FreeRoamPlayer : NetworkBehaviour
    {
        public static FreeRoamPlayer Local { get; private set; }

        // Move speed is a per-character stat (CharacterData.moveSpeed), read via PlayerCombatant.MoveSpeed.
        [SerializeField] private float wobbleAmplitude = 7f;
        [SerializeField] private float wobbleFrequency = 12f;
        [SerializeField] private float baseScale = 0.8f;
        [Tooltip("Collision radius against free-roam walls (the true hitbox, independent of the sprite).")]
        [SerializeField] private float collisionRadius = 0.4f;
        [Tooltip("Offset of the collision circle from the sprite's origin, in world units. Negative " +
                 "Y drops the hitbox toward the character's feet (bottom of the sprite). Tune with " +
                 "the dev-panel 'Show Hitboxes' overlay on.")]
        [SerializeField] private Vector2 hitboxOffset = new Vector2(0f, -0.5f);

        private readonly NetworkVariable<Vector2> _netPos = new(default, default, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> _facingRight = new(true, default, NetworkVariableWritePermission.Owner);

        private SpriteRenderer _sr;
        private PlayerCombatant _pc;
        private float _wobbleClock;
        private bool _facing = true;
        private FreeRoamContext _lastContext;
        private Transform _hitbox;      // dev overlay showing the true collision circle

        public bool IsMoving { get; private set; }
        private bool Active => FreeRoamContext.Current != null;

        /// <summary>World-space centre of the collision circle — offset toward the feet, and kept
        /// unrotated so the hitbox never swings with the walk wobble.</summary>
        private Vector2 HitboxCenter => (Vector2)transform.position + hitboxOffset;

        public override void OnNetworkSpawn()
        {
            _sr = GetComponent<SpriteRenderer>();
            _pc = GetComponent<PlayerCombatant>();
            ApplyCharacterSprite();
            // CharacterIndex is set by the lobby at run-start and replicated, so it may already be
            // valid here or arrive shortly after; re-apply whenever it changes.
            if (_pc != null) _pc.CharacterIndex.OnValueChanged += OnCharacterChanged;
            if (IsOwner) { Local = this; _netPos.Value = transform.position; }
        }

        public override void OnNetworkDespawn()
        {
            if (_pc != null) _pc.CharacterIndex.OnValueChanged -= OnCharacterChanged;
            if (Local == this) Local = null;
        }

        private void OnCharacterChanged(int _, int __) => ApplyCharacterSprite();

        /// <summary>Show the chosen character's sprite (untinted, so the pixel art keeps its own
        /// colours). Falls back to a plain circle when no character/sprite is assigned.</summary>
        private void ApplyCharacterSprite()
        {
            if (_sr == null) return;
            var reg = ContentRegistry.Instance;
            int ci = _pc != null ? _pc.CharacterIndex.Value : -1;
            var ch = reg != null && ci >= 0 && ci < reg.characters.Count ? reg.characters[ci] : null;
            if (ch != null && ch.baseSprite != null)
            {
                _sr.sprite = ch.baseSprite;
                _sr.color = Color.white;
                _sr.flipX = ch.flipX;
                _sr.flipY = ch.flipY;
            }
            else
            {
                _sr.flipX = false; _sr.flipY = false;
                if (_sr.sprite == null) _sr.sprite = PlayerDodgeIcon.MakeCircleSprite();
            }
        }

        private void Update()
        {
            if (!Active)                               // combat/lobby: PlayerAvatar owns the sprite
            {
                if (_hitbox != null && _hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(false);
                return;
            }
            if (_sr != null) _sr.enabled = true;

            // First frame in a new level: place the owner. Normally that's the level spawn point,
            // but when returning from a won fight we resume the exact pre-combat spot. The player
            // object persists across the combat scene load and its Update is inactive during combat,
            // so _netPos still holds where we were standing when the fight began.
            if (IsOwner && FreeRoamContext.Current != _lastContext)
            {
                _lastContext = FreeRoamContext.Current;
                if (SessionData.ReturningFromCombat)
                {
                    SessionData.ReturningFromCombat = false;
                    transform.position = _netPos.Value;
                }
                else
                {
                    // Character-specific spawn if this level defines one, else the default spawn
                    // spread horizontally by seat so players don't stack.
                    int index = _pc != null && _pc.Slot.Value >= 0 ? _pc.Slot.Value : 0;
                    int count = Mathf.Max(1, PlayerRegistry.All.Count);
                    Vector3 spawn = FreeRoamContext.Current.SpawnPositionFor(_pc != null ? _pc.Character : null, index, count);
                    transform.position = spawn;
                    _netPos.Value = spawn;
                }
            }

            if (IsOwner) UpdateOwner();
            else transform.position = Vector2.Lerp(transform.position, _netPos.Value, 15f * Time.deltaTime);

            UpdateVisual();
            UpdateHitboxOverlay();

            // Top-down depth: sort by the feet so the player slips behind things above it and in
            // front of things below it. Uses the same feet point as the hitbox ("bottom = origin").
            if (_sr != null) _sr.sortingOrder = FreeRoamSort.OrderFor(HitboxCenter.y, transform.position.z);
        }

        private void UpdateOwner()
        {
            // Freeze while a conversation is on screen — you can't walk off mid-dialogue. Also stops
            // the owner writing _netPos, so peers see you stand still too.
            if (DialogueBox.AnyOpen) { IsMoving = false; return; }

            Vector2 dir = ReadInput();
            IsMoving = dir.sqrMagnitude > 0.01f;
            if (dir.x != 0)
            {
                bool right = dir.x > 0;
                if (right != _facing) { _facing = right; _facingRight.Value = right; }
            }
            if (IsMoving)
            {
                // Per-axis: apply each axis's speed independently (no normalisation), so pressing
                // A/D + W/S together yields the faster diagonal. Speed comes from the character stat.
                Vector2 speed = _pc != null ? _pc.MoveSpeed : new Vector2(4.5f, 4.5f);
                Vector2 step = new Vector2(dir.x * speed.x, dir.y * speed.y) * Time.deltaTime;
                Vector2 p = transform.position;
                // Resolve each axis separately so you slide along a wall instead of sticking to it.
                Vector2 tryX = new Vector2(p.x + step.x, p.y);
                if (!BlockedByWall(tryX)) p.x = tryX.x;
                Vector2 tryY = new Vector2(p.x, p.y + step.y);
                if (!BlockedByWall(tryY)) p.y = tryY.y;
                Vector2 next = FreeRoamContext.Current.ClampToBounds(p);
                transform.position = next;
                _netPos.Value = next;
            }
        }

        /// <summary>True if the character's collision circle overlaps any wall box when the sprite
        /// origin is at <paramref name="p"/>. The circle sits at the feet (p + hitboxOffset).</summary>
        private bool BlockedByWall(Vector2 p)
        {
            Vector2 c = p + hitboxOffset;   // collide from the feet, not the sprite centre
            var walls = FreeRoamWall.All;
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i] == null) continue;
                var b = walls[i].Bounds;
                float cx = Mathf.Clamp(c.x, b.xMin, b.xMax);
                float cy = Mathf.Clamp(c.y, b.yMin, b.yMax);
                float dx = c.x - cx, dy = c.y - cy;
                if (dx * dx + dy * dy < collisionRadius * collisionRadius) return true;
            }
            return false;
        }

        private void UpdateHitboxOverlay()
        {
            if (!DebugView.ShowHitboxes)
            {
                if (_hitbox != null && _hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(false);
                return;
            }
            if (_hitbox == null)
            {
                var go = new GameObject("Hitbox");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlayerDodgeIcon.MakeCircleSprite();
                sr.color = new Color(1f, 0.25f, 0.25f, 0.5f);
                sr.sortingOrder = FreeRoamSort.OverlayOrder;   // dev overlay: on top of the world band
                _hitbox = go.transform;
            }
            if (!_hitbox.gameObject.activeSelf) _hitbox.gameObject.SetActive(true);
            // Pin the overlay to the true hitbox: at the feet, upright, and the right world size —
            // overriding the parent's wobble/flip (set world position + rotation, counter lossyScale).
            _hitbox.position = HitboxCenter;
            _hitbox.rotation = Quaternion.identity;
            float px = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
            float py = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
            _hitbox.localScale = new Vector3((2f * collisionRadius) / px, (2f * collisionRadius) / py, 1f);
        }

        private void UpdateVisual()
        {
            bool moving = IsOwner ? IsMoving : ((Vector2)transform.position - _netPos.Value).sqrMagnitude > 0.0004f;
            float sign = (IsOwner ? _facing : _facingRight.Value) ? 1f : -1f;

            if (moving)
            {
                _wobbleClock += Time.deltaTime;
                float tilt = Mathf.Sin(_wobbleClock * wobbleFrequency) * wobbleAmplitude;
                transform.rotation = Quaternion.Euler(0, 0, tilt);
            }
            else
            {
                _wobbleClock = 0f;
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.identity, 12f * Time.deltaTime);
            }
            transform.localScale = new Vector3(sign * baseScale, baseScale, 1f);
        }

        private static Vector2 ReadInput()
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
            return v;
        }
    }
}

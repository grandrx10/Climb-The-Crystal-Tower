using TwoCT.Bullets;
using TwoCT.Core;
using TwoCT.FreeRoam;
using Unity.Netcode;
using UnityEngine;

namespace TwoCT.Combat
{
    /// <summary>
    /// Cosmetic on-field representation of a player during combat: lines the avatar up on the
    /// left (Deltarune-style) by seat, tints by slot, dims on knockout. Later this also drives
    /// the free-roam wobble/flip and the cartoonish "speaking" deform.
    /// </summary>
    [RequireComponent(typeof(PlayerCombatant))]
    public class PlayerAvatar : NetworkBehaviour
    {
        [SerializeField] private Vector2 lineupOrigin = new Vector2(-6f, -1.5f);
        [SerializeField] private float lineupSpacing = 1.6f;
        [SerializeField] private Color[] slotColors = { Color.red, new Color(0.3f, 0.6f, 1f), new Color(0.4f, 1f, 0.4f) };

        [Header("Damage shake (the character, not the dodge icon)")]
        [Tooltip("Seconds the shake lasts.")]
        [SerializeField] private float shakeDuration = 0.28f;
        [Tooltip("Peak jitter (world units) at the min/max damage thresholds below.")]
        [SerializeField] private float minShake = 0.08f;
        [SerializeField] private float maxShake = 0.45f;
        [Tooltip("Damage that maps to minShake / maxShake (clamped outside this range).")]
        [SerializeField] private int minDamage = 4;
        [SerializeField] private int maxDamage = 15;

        private PlayerCombatant _pc;
        private SpriteRenderer _sr;
        private float _shakeTimeLeft;
        private float _shakeMagnitude;

        public override void OnNetworkSpawn()
        {
            _pc = GetComponent<PlayerCombatant>();
            _sr = GetComponent<SpriteRenderer>();
            if (_sr == null) _sr = gameObject.AddComponent<SpriteRenderer>();
            if (_sr.sprite == null) _sr.sprite = PlayerDodgeIcon.MakeCircleSprite();
            _pc.Slot.OnValueChanged += (_, __) => Reposition();
            _pc.CharacterIndex.OnValueChanged += (_, __) => ApplyCharacterSprite();
            CombatEvents.DamageNumber += OnDamage;
            Reposition();
            ApplyCharacterSprite();
        }

        public override void OnNetworkDespawn()
        {
            CombatEvents.DamageNumber -= OnDamage;
        }

        // Any player taking damage shakes THAT player's character sprite, scaled by how much was lost.
        private void OnDamage(int slot, int amount, Vector3 _)
        {
            if (_pc == null || slot != _pc.Slot.Value || amount <= 0) return;
            float t = Mathf.InverseLerp(minDamage, maxDamage, amount);   // clamped 0..1
            _shakeMagnitude = Mathf.Lerp(minShake, maxShake, t);
            _shakeTimeLeft = shakeDuration;
        }

        private Vector2 LineupPosition(int slot) => lineupOrigin + Vector2.up * Mathf.Max(0, slot) * lineupSpacing;

        private void Reposition()
        {
            int slot = _pc != null ? _pc.Slot.Value : 0;
            transform.position = LineupPosition(slot);
            if (_sr != null) _sr.sortingOrder = 10 - Mathf.Max(0, slot); // lower on screen draws in front
            ApplyCharacterSprite();
        }

        /// <summary>Use the chosen character's sprite if one is assigned; else a slot-coloured circle.</summary>
        private void ApplyCharacterSprite()
        {
            if (_sr == null) return;
            var reg = ContentRegistry.Instance;
            int ci = _pc != null ? _pc.CharacterIndex.Value : -1;
            var character = reg != null && ci >= 0 && ci < reg.characters.Count ? reg.characters[ci] : null;
            int slot = _pc != null && _pc.Slot.Value >= 0 ? _pc.Slot.Value : 0;

            if (character != null && character.baseSprite != null)
            {
                _sr.sprite = character.baseSprite;
                _sr.color = Color.white;   // show the pixel art's own colours; `tint` is the UI/theme colour
                _sr.flipX = character.flipX;
                _sr.flipY = character.flipY;
            }
            else
            {
                _sr.flipX = false; _sr.flipY = false;
                if (_sr.sprite == null) _sr.sprite = PlayerDodgeIcon.MakeCircleSprite();
                _sr.color = slot < slotColors.Length ? slotColors[slot] : Color.white;
            }
        }

        private void Update()
        {
            if (_pc == null || _sr == null) return;

            // Sprite ownership is context-based (this object persists across lobby/level/combat):
            //  - Combat  → this avatar owns the sprite (lineup position, tint, KO dim).
            //  - FreeRoam → FreeRoamPlayer owns it; leave it alone.
            //  - Lobby    → nobody's driving it; hide it.
            if (CombatManager.Instance != null)
            {
                _sr.enabled = true;
                transform.rotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                // Re-assert the lineup position + draw order every frame. Free roam moves this
                // transform (and, on a second combat, Slot is unchanged so Reposition never fires),
                // so without this the avatar would sit at its stale free-roam position. A damage
                // shake is layered on top as a decaying random offset.
                Vector2 shake = Vector2.zero;
                if (_shakeTimeLeft > 0f)
                {
                    _shakeTimeLeft -= Time.deltaTime;
                    float damper = shakeDuration > 0f ? Mathf.Clamp01(_shakeTimeLeft / shakeDuration) : 0f;
                    shake = Random.insideUnitCircle * (_shakeMagnitude * damper);
                }
                transform.position = LineupPosition(_pc.Slot.Value) + shake;
                _sr.sortingOrder = 10 - Mathf.Max(0, _pc.Slot.Value);
                var c = _sr.color;
                c.a = _pc.IsAlive ? 1f : 0.35f;   // dim when knocked out
                _sr.color = c;
            }
            else if (FreeRoamContext.Current == null)
            {
                _sr.enabled = false;              // lobby
            }
        }
    }
}

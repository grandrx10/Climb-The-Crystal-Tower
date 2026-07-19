using System.Collections;
using System.Collections.Generic;
using TwoCT.Combat;
using UnityEngine;

namespace TwoCT.Bullets
{
    /// <summary>
    /// The rectangular defend arena (the "Undertale box"). Holds the bounds, spawns one dodge
    /// icon per player at the start of a defend round and hides them afterwards. Each client
    /// controls its own icon; peers' icons are shown from their synced ArenaPos.
    /// </summary>
    public class DefendArena : MonoBehaviour
    {
        [Header("Bounds (world units, centred on this transform)")]
        public Vector2 size = new Vector2(6f, 3.5f);
        [Tooltip("Inset so icons stay fully inside the visible box.")]
        public float padding = 0.2f;

        [Header("Icon spawning")]
        [SerializeField] private PlayerDodgeIcon iconPrefab;
        [SerializeField] private Color[] slotColors = { Color.red, new Color(0.3f, 0.6f, 1f), new Color(0.4f, 1f, 0.4f) };
        [Tooltip("How much the defend box slows a player relative to their character's base move " +
                 "speed (0–1). 1 = full speed; ~0.55 keeps the old 2.5-vs-4.5 feel.")]
        [Range(0f, 1f)]
        [SerializeField] private float dodgeSpeedMultiplier = 0.55f;

        /// <summary>Combat slow applied on top of each player's base (character) move speed.</summary>
        public float SpeedMultiplier => dodgeSpeedMultiplier;

        [Header("Frame visuals")]
        [SerializeField] private Color fillColor = new Color(0.15f, 0.18f, 0.30f, 0.25f);
        [SerializeField] private Color borderColor = new Color(0.6f, 0.85f, 1f, 0.95f);
        [SerializeField] private float borderThickness = 0.08f;
        [Tooltip("Seconds for the box to expand open at the start of the defend phase.")]
        [SerializeField] private float expandDuration = 0.25f;
        [Tooltip("Seconds for the box to collapse shut after the attack ends.")]
        [SerializeField] private float collapseDuration = 0.25f;

        private readonly List<PlayerDodgeIcon> _icons = new List<PlayerDodgeIcon>();
        private Transform _frame;
        private Transform _fill;
        private Transform _mask;
        private Transform[] _borders;
        private Coroutine _frameRoutine;

        public Vector2 Center => transform.position;
        // The box can be temporarily taller (ride mode raises the ceiling): the extra height is added
        // to the TOP only — the floor stays put.
        public Rect Bounds => new Rect(Center - size * 0.5f, new Vector2(size.x, size.y + _extraHeight));
        public Rect InnerBounds
        {
            get { var b = Bounds; return new Rect(b.x + padding, b.y + padding, b.width - padding * 2f, b.height - padding * 2f); }
        }

        public PlayerDodgeIcon LocalIcon
        {
            get { foreach (var i in _icons) if (i.IsLocal) return i; return null; }
        }
        public IReadOnlyList<PlayerDodgeIcon> Icons => _icons;

        /// <summary>True while the box is open (expanding or fully open), false once collapsed/hidden.</summary>
        public bool IsOpen => _frame != null && _frame.gameObject.activeSelf;

        public Vector2 Clamp(Vector2 p)
        {
            var r = InnerBounds;
            return new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
        }

        // ---- Lasso (Ryomi): drag the whole box vertically -------------------
        private float _lassoBaseY;
        private bool _lassoBaseCaptured;

        /// <summary>Ryomi's Lasso: offset the arena vertically from its home position. Player icons
        /// re-clamp to the moved bounds each frame (via <see cref="Clamp"/>), so anyone at an edge is
        /// pulled along; bullets that read <see cref="Bounds"/> (e.g. Ricochet) bounce off the moved walls.</summary>
        public void SetLassoOffset(float y)
        {
            if (!_lassoBaseCaptured) { _lassoBaseY = transform.localPosition.y; _lassoBaseCaptured = true; }
            var p = transform.localPosition; p.y = _lassoBaseY + y; transform.localPosition = p;
        }

        /// <summary>Restore the arena to its home position after a Lasso attack.</summary>
        public void ResetLasso()
        {
            if (!_lassoBaseCaptured) return;
            var p = transform.localPosition; p.y = _lassoBaseY; transform.localPosition = p;
        }

        // ---- Marnu: rising water + wind force -------------------------------
        private float _waterLevel01;      // fraction of the box height currently flooded (0..cap), eases toward the target
        private float _waterTarget01;     // level the water is rising toward
        private float _waterRisePerSec;   // rise speed while easing (fraction of height per second)
        private Vector2 _windForce;       // horizontal force added to the local icon's movement

        /// <summary>Marnu's Water spell: how much of the box (bottom-up) is flooded, 0–0.5.
        /// Eases toward the latest cast's target rather than jumping instantly.</summary>
        public float WaterLevel01 => _waterLevel01;

        /// <summary>Raise the flood target by <paramref name="rise"/> (fraction of height), capped at
        /// <paramref name="max"/>; the level then rises there over <paramref name="riseSeconds"/> (0 = instant).</summary>
        public void RaiseWater(float rise, float max, float riseSeconds)
        {
            _waterTarget01 = Mathf.Min(Mathf.Max(0f, max), _waterTarget01 + Mathf.Max(0f, rise));
            if (riseSeconds <= 0f) _waterLevel01 = _waterTarget01;
            else _waterRisePerSec = Mathf.Max(0.0001f, rise) / riseSeconds;
        }
        public void ResetWater() { _waterLevel01 = 0f; _waterTarget01 = 0f; }

        private void Update()
        {
            if (_waterLevel01 < _waterTarget01)
                _waterLevel01 = Mathf.MoveTowards(_waterLevel01, _waterTarget01, _waterRisePerSec * Time.deltaTime);

            // Ride-mode ceiling raise: ease toward the target extra height and re-fit the frame visuals.
            if (!Mathf.Approximately(_extraHeight, RideExtraHeight))
            {
                _extraHeight = Mathf.MoveTowards(_extraHeight, RideExtraHeight, 8f * Time.deltaTime);
                if (_frame != null) LayoutFrame();
            }
        }

        /// <summary>Marnu's Wind spell: the horizontal force (world units/sec) pulling the local dodge icon;
        /// the icon adds this to its movement each frame. Zero when no wind is active.</summary>
        public Vector2 WindForce => _windForce;
        public void SetWind(Vector2 force) => _windForce = force;
        public void ResetWind() => _windForce = Vector2.zero;

        // ---- Horus: Joint Horse Rider "ride mode" ---------------------------
        // While ride mode is on, each dodge icon rides a horse at a fixed lane x and obeys jump physics
        // (gravity + variable-height W jump) instead of free WASD movement, dodging scrolling obstacles.
        public bool RideMode { get; private set; }
        public float RideGravity { get; private set; }
        public float RideJumpVelocity { get; private set; }
        public float RideMaxJumpHold { get; private set; }
        public float RideLowGravityFactor { get; private set; }
        public Sprite RideHorseSprite { get; private set; }
        /// <summary>Target extra box height while riding (the ceiling rises so held jumps matter).</summary>
        public float RideExtraHeight { get; private set; }
        /// <summary>Horizontal steer speed multiplier while riding (1 when not riding / unset).</summary>
        public float RideSpeedMul { get; private set; } = 1f;
        private float _extraHeight;   // animated current value, eases toward RideExtraHeight

        public void SetRideMode(bool on, float gravity, float jumpVel, float maxHold, float lowG, Sprite horse,
                                float extraHeight, float speedMul)
        {
            RideMode = on;
            RideGravity = gravity;
            RideJumpVelocity = jumpVel;
            RideMaxJumpHold = maxHold;
            RideLowGravityFactor = lowG;
            RideHorseSprite = horse;
            RideExtraHeight = on ? Mathf.Max(0f, extraHeight) : 0f;
            RideSpeedMul = on && speedMul > 0f ? speedMul : 1f;
        }
        public void ResetRide() { RideMode = false; RideExtraHeight = 0f; RideSpeedMul = 1f; }

        // =====================================================================
        //  Visible frame + expansion animation
        // =====================================================================
        private void EnsureFrame()
        {
            if (_frame != null) return;
            var sq = PlayerDodgeIcon.MakeSquareSprite();

            var frameGO = new GameObject("ArenaFrame");
            _frame = frameGO.transform;
            _frame.SetParent(transform, false);

            var fillGO = new GameObject("Fill");
            _fill = fillGO.transform; _fill.SetParent(_frame, false);
            var fillSr = fillGO.AddComponent<SpriteRenderer>();
            fillSr.sprite = sq; fillSr.color = fillColor; fillSr.sortingOrder = 20;

            // Stencil mask covering the box: cross-slash bars render VisibleInsideMask, so their long
            // arms are clipped to the battlefield. Child of the frame → it moves with the Lasso drag.
            var maskGO = new GameObject("ArenaMask");
            _mask = maskGO.transform;
            _mask.SetParent(_frame, false);
            var mask = maskGO.AddComponent<SpriteMask>();
            mask.sprite = sq;

            // Four border strips (top, bottom, left, right).
            _borders = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var b = new GameObject("Border" + i);
                b.transform.SetParent(_frame, false);
                var sr = b.AddComponent<SpriteRenderer>();
                sr.sprite = sq; sr.color = borderColor; sr.sortingOrder = 21;
                _borders[i] = b.transform;
            }
            LayoutFrame();
            _frame.gameObject.SetActive(false);
        }

        // Fit the fill, mask and borders to the current box size (incl. any ride-mode extra height,
        // which grows the box UPWARD only — the floor border stays put).
        private void LayoutFrame()
        {
            float ex = _extraHeight;
            float h = size.y + ex;
            float cy = ex * 0.5f;                 // vertical centre of the (possibly taller) box
            _fill.localScale = new Vector3(size.x, h, 1f);
            _fill.localPosition = new Vector3(0f, cy, 0f);
            if (_mask != null)
            {
                _mask.localScale = new Vector3(size.x, h, 1f);
                _mask.localPosition = new Vector3(0f, cy, 0f);
            }
            float t = borderThickness;
            // top, bottom
            _borders[0].localPosition = new Vector3(0, size.y * 0.5f + ex, 0); _borders[0].localScale = new Vector3(size.x + t, t, 1);
            _borders[1].localPosition = new Vector3(0, -size.y * 0.5f, 0); _borders[1].localScale = new Vector3(size.x + t, t, 1);
            // left, right
            _borders[2].localPosition = new Vector3(-size.x * 0.5f, cy, 0); _borders[2].localScale = new Vector3(t, h + t, 1);
            _borders[3].localPosition = new Vector3(size.x * 0.5f, cy, 0); _borders[3].localScale = new Vector3(t, h + t, 1);
        }

        private IEnumerator ExpandFrame()
        {
            _frame.gameObject.SetActive(true);
            _frame.localScale = Vector3.zero;                   // start from nothing
            float clock = 0f;
            while (clock < expandDuration)
            {
                clock += Time.deltaTime;
                float p = Mathf.Clamp01(clock / expandDuration);
                float e = 1f - Mathf.Pow(1f - p, 3f);           // ease-out cubic
                _frame.localScale = new Vector3(e, e, 1f);
                yield return null;
            }
            _frame.localScale = Vector3.one;
        }

        private IEnumerator CollapseFrame()
        {
            float clock = 0f;
            while (clock < collapseDuration)
            {
                clock += Time.deltaTime;
                float p = Mathf.Clamp01(clock / collapseDuration);
                float e = Mathf.Pow(1f - p, 2f);                // ease-in shrink toward nothing
                _frame.localScale = new Vector3(e, e, 1f);
                yield return null;
            }
            _frame.localScale = Vector3.zero;
            _frame.gameObject.SetActive(false);
        }

        /// <summary>Spawn/refresh icons for every current player and pop the box open. Called by the
        /// bullet system on defend start.</summary>
        public void ShowIcons()
        {
            HideIcons();                                        // instant reset of any prior state
            EnsureFrame();
            if (_frameRoutine != null) StopCoroutine(_frameRoutine);
            _frameRoutine = StartCoroutine(ExpandFrame());
            var players = PlayerRegistry.All;
            for (int i = 0; i < players.Count; i++)
            {
                var pc = players[i];
                if (pc == null || !pc.IsAlive) continue;
                var icon = iconPrefab != null ? Instantiate(iconPrefab, transform) : PlayerDodgeIcon.CreateFallback(transform);
                Color c = slotColors != null && pc.Slot.Value >= 0 && pc.Slot.Value < slotColors.Length ? slotColors[pc.Slot.Value] : Color.white;
                // Spread starting positions across the bottom of the arena.
                var inner = InnerBounds;
                float t = players.Count > 1 ? (float)i / (players.Count - 1) : 0.5f;
                Vector2 start = new Vector2(Mathf.Lerp(inner.xMin, inner.xMax, t), inner.yMin + inner.height * 0.25f);
                icon.Bind(pc, this, c, start);
                _icons.Add(icon);
            }
        }

        /// <summary>Instantly remove icons and hide the frame (used to reset before a new round).</summary>
        public void HideIcons()
        {
            if (_frameRoutine != null) { StopCoroutine(_frameRoutine); _frameRoutine = null; }
            foreach (var i in _icons) if (i != null) Destroy(i.gameObject);
            _icons.Clear();
            if (_frame != null) _frame.gameObject.SetActive(false);
        }

        /// <summary>Animate the box collapsing shut, then hide it. Icons are removed immediately
        /// (they're uncontrollable once the attack ends). Called when the defend phase finishes.</summary>
        public void CollapseAndHide()
        {
            foreach (var i in _icons) if (i != null) Destroy(i.gameObject);
            _icons.Clear();
            if (_frame == null || !_frame.gameObject.activeSelf) { if (_frame != null) _frame.gameObject.SetActive(false); return; }
            if (_frameRoutine != null) StopCoroutine(_frameRoutine);
            _frameRoutine = StartCoroutine(CollapseFrame());
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Center, size);
        }
    }
}

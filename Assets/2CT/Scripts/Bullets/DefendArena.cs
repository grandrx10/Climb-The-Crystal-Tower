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
        private Transform[] _borders;
        private Coroutine _frameRoutine;

        public Vector2 Center => transform.position;
        public Rect Bounds => new Rect(Center - size * 0.5f, size);
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
            _fill.localScale = new Vector3(size.x, size.y, 1f);

            // Stencil mask covering the box: cross-slash bars render VisibleInsideMask, so their long
            // arms are clipped to the battlefield. Child of the frame → it moves with the Lasso drag.
            var maskGO = new GameObject("ArenaMask");
            maskGO.transform.SetParent(_frame, false);
            var mask = maskGO.AddComponent<SpriteMask>();
            mask.sprite = sq;
            maskGO.transform.localScale = new Vector3(size.x, size.y, 1f);

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
            LayoutBorders();
            _frame.gameObject.SetActive(false);
        }

        private void LayoutBorders()
        {
            float t = borderThickness;
            // top, bottom
            _borders[0].localPosition = new Vector3(0, size.y * 0.5f, 0); _borders[0].localScale = new Vector3(size.x + t, t, 1);
            _borders[1].localPosition = new Vector3(0, -size.y * 0.5f, 0); _borders[1].localScale = new Vector3(size.x + t, t, 1);
            // left, right
            _borders[2].localPosition = new Vector3(-size.x * 0.5f, 0, 0); _borders[2].localScale = new Vector3(t, size.y + t, 1);
            _borders[3].localPosition = new Vector3(size.x * 0.5f, 0, 0); _borders[3].localScale = new Vector3(t, size.y + t, 1);
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

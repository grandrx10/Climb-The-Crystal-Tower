using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Extends a sprite indefinitely by <b>duplicating and mirroring</b> it instead of stretching the
    /// pixels. Attach to a GameObject with a <see cref="SpriteRenderer"/>, then "stretch" it left/right
    /// (and optionally up/down) by dragging the Scene-view handles or typing into <see cref="span"/>:
    /// the source stays the centre tile at native size, and mirrored copies fan out from it to fill the
    /// span. Neighbours alternate flip so their touching edges are identical — a seamless, endlessly
    /// repeatable backdrop.
    ///
    /// Keep the transform's scale at (1,1,1) — the span is authored in world units, like FreeRoamWall.
    /// Generated tiles inherit the source's sprite/material/colour/sorting and are marked DontSave, so
    /// they never clutter the saved scene; they regenerate in the editor and at runtime.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class MirrorTiledSprite : MonoBehaviour
    {
        [Tooltip("Total area to fill, in world units, centred on this transform. Set X larger than the " +
                 "sprite to tile left/right; set Y larger to tile up/down. Values <= the sprite size " +
                 "leave that axis as a single tile.")]
        public Vector2 span = new Vector2(20f, 0f);

        [Tooltip("Alternate-flip each neighbour so touching edges match seamlessly. Turn off for a " +
                 "plain repeat (only seamless if the sprite already tiles).")]
        public bool mirror = true;

        [Tooltip("If the transform gets a non-1 scale (e.g. you dragged the Scale tool), fold that " +
                 "stretch into 'span' and snap the scale back to 1. Lets you stretch with the Scale " +
                 "gizmo and have it tile instead of distort.")]
        public bool absorbTransformScale = false;

        private SpriteRenderer _source;
        private Transform _container;
        private readonly List<SpriteRenderer> _tiles = new List<SpriteRenderer>();

        // Cached signature so we only rebuild when something actually changed.
        private Sprite _lastSprite;
        private Vector2 _lastSpan;
        private bool _lastMirror;
        private Color _lastColor;
        private int _lastSortOrder;
        private int _lastSortLayer;
        private bool _lastFlipX, _lastFlipY;

        private const string ContainerName = "__MirrorTiles";

        private void OnEnable()
        {
            _source = GetComponent<SpriteRenderer>();
            EnsureContainer();
            Rebuild();
        }

        private void OnDisable() => ClearContainer();

        private void LateUpdate()
        {
            if (_source == null) _source = GetComponent<SpriteRenderer>();
            if (_source == null) return;

            if (absorbTransformScale) AbsorbScale();

            if (NeedsRebuild()) Rebuild();
        }

        /// <summary>Detects any change to the inputs that affect the tiling, so LateUpdate can rebuild lazily.</summary>
        private bool NeedsRebuild()
        {
            return _lastSprite != _source.sprite
                || _lastSpan != span
                || _lastMirror != mirror
                || _lastColor != _source.color
                || _lastSortOrder != _source.sortingOrder
                || _lastSortLayer != _source.sortingLayerID
                || _lastFlipX != _source.flipX
                || _lastFlipY != _source.flipY
                || _container == null
                || _tiles.Count == 0 && span.sqrMagnitude > 0f && SpriteSize().x > 0f;
        }

        /// <summary>Folds a non-1 transform scale into <see cref="span"/> and resets the scale to 1.</summary>
        private void AbsorbScale()
        {
            Vector3 s = transform.localScale;
            if (Mathf.Approximately(s.x, 1f) && Mathf.Approximately(s.y, 1f)) return;
            Vector2 size = SpriteSize();
            span = new Vector2(
                Mathf.Max(span.x, size.x) * Mathf.Abs(s.x),
                Mathf.Max(span.y, size.y) * Mathf.Abs(s.y));
            transform.localScale = new Vector3(1f, 1f, s.z);
        }

        private Vector2 SpriteSize()
        {
            if (_source == null || _source.sprite == null) return Vector2.zero;
            return _source.sprite.bounds.size;   // local units (respects pixels-per-unit)
        }

        private void EnsureContainer()
        {
            if (_container != null) return;
            // Reuse a leftover container (e.g. after a domain reload) before making a new one.
            var existing = transform.Find(ContainerName);
            if (existing != null)
            {
                _container = existing;
            }
            else
            {
                var go = new GameObject(ContainerName) { hideFlags = HideFlags.DontSave };
                _container = go.transform;
                _container.SetParent(transform, false);
            }
            CollectTiles();
        }

        private void CollectTiles()
        {
            _tiles.Clear();
            if (_container == null) return;
            _container.GetComponentsInChildren(true, _tiles);
        }

        private void ClearContainer()
        {
            if (_container != null) DestroyGO(_container.gameObject);
            _container = null;
            _tiles.Clear();
        }

        /// <summary>Rebuilds the mirrored tile grid to fill <see cref="span"/> around the source tile.</summary>
        public void Rebuild()
        {
            if (_source == null) _source = GetComponent<SpriteRenderer>();
            EnsureContainer();

            Vector2 size = SpriteSize();
            if (size.x <= 0f || size.y <= 0f || _source.sprite == null)
            {
                SetTileCount(0);
                CacheSignature();
                return;
            }

            // How many tiles each side of centre are needed to cover the span (centre tile = the source).
            int sideX = size.x >= span.x ? 0 : Mathf.CeilToInt((span.x - size.x) / (2f * size.x));
            int sideY = size.y >= span.y ? 0 : Mathf.CeilToInt((span.y - size.y) / (2f * size.y));

            int cols = 2 * sideX + 1;
            int rows = 2 * sideY + 1;
            SetTileCount(cols * rows - 1);   // minus the source, which is the (0,0) centre tile

            Vector2 center = _source.sprite.bounds.center;   // pivot offset, for mirror compensation
            int i = 0;
            for (int cy = -sideY; cy <= sideY; cy++)
            {
                for (int cx = -sideX; cx <= sideX; cx++)
                {
                    if (cx == 0 && cy == 0) continue;   // the source renderer is the centre tile

                    bool flipX = mirror && (Mathf.Abs(cx) & 1) == 1 ? !_source.flipX : _source.flipX;
                    bool flipY = mirror && (Mathf.Abs(cy) & 1) == 1 ? !_source.flipY : _source.flipY;

                    // Placing at cx*width tiles the edges; when a tile is flipped relative to the source
                    // its pivot mirrors, so shift by 2*pivotOffset to keep the visual centre on the grid.
                    float x = cx * size.x + (flipX != _source.flipX ? 2f * center.x : 0f);
                    float y = cy * size.y + (flipY != _source.flipY ? 2f * center.y : 0f);

                    var t = _tiles[i++];
                    t.sprite = _source.sprite;
                    t.sharedMaterial = _source.sharedMaterial;
                    t.color = _source.color;
                    t.flipX = flipX;
                    t.flipY = flipY;
                    t.sortingLayerID = _source.sortingLayerID;
                    t.sortingOrder = _source.sortingOrder;
                    t.transform.localPosition = new Vector3(x, y, 0f);
                    t.transform.localRotation = Quaternion.identity;
                    t.transform.localScale = Vector3.one;
                }
            }

            CacheSignature();
        }

        /// <summary>Grows or shrinks the pool of managed tile renderers to exactly <paramref name="count"/>.</summary>
        private void SetTileCount(int count)
        {
            CollectTiles();
            for (int i = _tiles.Count - 1; i >= count; i--)
            {
                if (_tiles[i] != null) DestroyGO(_tiles[i].gameObject);
                _tiles.RemoveAt(i);
            }
            while (_tiles.Count < count)
            {
                var go = new GameObject("Tile", typeof(SpriteRenderer)) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(_container, false);
                _tiles.Add(go.GetComponent<SpriteRenderer>());
            }
        }

        private void CacheSignature()
        {
            _lastSprite = _source.sprite;
            _lastSpan = span;
            _lastMirror = mirror;
            _lastColor = _source.color;
            _lastSortOrder = _source.sortingOrder;
            _lastSortLayer = _source.sortingLayerID;
            _lastFlipX = _source.flipX;
            _lastFlipY = _source.flipY;
        }

        private static void DestroyGO(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.9f, 1f, 0.6f);
            Vector3 s = new Vector3(Mathf.Max(span.x, SpriteSize().x), Mathf.Max(span.y, SpriteSize().y), 0.01f);
            Gizmos.DrawWireCube(transform.position, s);
        }
    }
}

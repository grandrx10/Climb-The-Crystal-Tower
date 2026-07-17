using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Flood a selected tilemap area with weighted-random tiles at random 90° orientations.
    ///
    /// Workflow: in the Tile Palette (or scene) pick the marquee <b>Select</b> tool and drag a
    /// rectangle over the tilemap — that stores a <see cref="UnityEditor.Tilemaps.GridSelection"/>.
    /// Open this window (<b>2CT ▸ Tilemap Random Fill</b>), drop in the tiles to draw from, tune
    /// their weights, and hit <b>Fill Selection</b>.
    ///
    /// Each cell gets a tile chosen by weight (a weight-3 tile is 3× as likely as a weight-1),
    /// spun 0/90/180/270° (and optionally mirrored). One undo step reverts the whole fill.
    /// </summary>
    public class TilemapRandomFill : EditorWindow
    {
        /// <summary>One candidate tile and how heavily it's favoured relative to the others.</summary>
        [Serializable]
        public class WeightedTile
        {
            public TileBase tile;
            public float weight = 1f;
            public WeightedTile(TileBase t) { tile = t; }
        }

        [SerializeField] private Tilemap _tilemap;
        [SerializeField] private List<WeightedTile> _entries = new List<WeightedTile>();
        [SerializeField] private bool _randomRotation = true;
        [SerializeField] private bool _randomFlip = false;
        [SerializeField] private bool _onlyEmptyCells = false;
        [SerializeField] private int _seed = 0; // 0 = fresh randomness each fill

        private Vector2 _scroll;

        [MenuItem("2CT/Tilemap Random Fill", priority = 40)]
        public static void Open()
        {
            var win = GetWindow<TilemapRandomFill>("Random Fill");
            win.minSize = new Vector2(340, 360);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            // Offer to grab the tilemap from the current grid selection / hierarchy selection.
            using (new EditorGUILayout.HorizontalScope())
            {
                _tilemap = (Tilemap)EditorGUILayout.ObjectField("Tilemap", _tilemap, typeof(Tilemap), true);
                if (GUILayout.Button("Use Selected", GUILayout.Width(90)))
                    _tilemap = ResolveSelectedTilemap() ?? _tilemap;
            }

            EditorGUILayout.Space(6);
            DrawTileList();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            _randomRotation = EditorGUILayout.Toggle(new GUIContent("Random Rotation", "Spin each tile 0/90/180/270°."), _randomRotation);
            _randomFlip = EditorGUILayout.Toggle(new GUIContent("Random Flip", "Randomly mirror tiles horizontally."), _randomFlip);
            _onlyEmptyCells = EditorGUILayout.Toggle(new GUIContent("Only Empty Cells", "Skip cells that already have a tile."), _onlyEmptyCells);
            _seed = EditorGUILayout.IntField(new GUIContent("Seed", "0 = random every time. Any other value gives repeatable results."), _seed);

            EditorGUILayout.Space(8);
            DrawFillButton();
        }

        private void DrawTileList()
        {
            EditorGUILayout.LabelField($"Tiles to draw from ({_entries.Count})", EditorStyles.boldLabel);

            // Drag-and-drop bin: drop Tile assets (e.g. select the whole generated set and drag).
            var drop = GUILayoutUtility.GetRect(0, 46, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop Tile assets here", EditorStyles.helpBox);
            HandleDrop(drop);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected (Project)")) AddTiles(Selection.objects);
                if (GUILayout.Button("Reset Weights")) foreach (var e in _entries) e.weight = 1f;
                if (GUILayout.Button("Clear")) _entries.Clear();
            }

            if (_entries.Count == 0) return;

            // Total of positive weights, so each row can show its real spawn chance.
            float total = 0f;
            foreach (var e in _entries)
                if (e.tile != null && e.weight > 0f) total += e.weight;

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(150));
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.tile = (TileBase)EditorGUILayout.ObjectField(entry.tile, typeof(TileBase), false);

                    EditorGUILayout.LabelField("w", GUILayout.Width(14));
                    entry.weight = Mathf.Max(0f, EditorGUILayout.FloatField(entry.weight, GUILayout.Width(46)));

                    float pct = (total > 0f && entry.tile != null && entry.weight > 0f) ? entry.weight / total * 100f : 0f;
                    EditorGUILayout.LabelField($"{pct:0.#}%", GUILayout.Width(46));

                    if (GUILayout.Button("✕", GUILayout.Width(24))) { _entries.RemoveAt(i); i--; }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawFillButton()
        {
            bool hasSelection = UnityEditor.Tilemaps.GridSelection.active;
            var valid = GetValidEntries();

            if (_tilemap == null) EditorGUILayout.HelpBox("Assign a Tilemap (or click Use Selected).", MessageType.Warning);
            else if (valid.Count == 0) EditorGUILayout.HelpBox("Add at least one tile with a weight above 0.", MessageType.Warning);
            else if (!hasSelection) EditorGUILayout.HelpBox("Pick the marquee Select tool in the Tile Palette and drag an area over the tilemap first.", MessageType.Info);

            using (new EditorGUI.DisabledScope(_tilemap == null || valid.Count == 0 || !hasSelection))
            {
                if (GUILayout.Button("Fill Selection", GUILayout.Height(32)))
                    Fill(valid);
            }
        }

        private void Fill(List<WeightedTile> valid)
        {
            BoundsInt area = UnityEditor.Tilemaps.GridSelection.position;
            var rng = _seed != 0 ? new System.Random(_seed) : new System.Random();

            double totalWeight = 0;
            foreach (var e in valid) totalWeight += e.weight;

            Undo.RegisterCompleteObjectUndo(_tilemap, "Random Fill Tilemap");
            int painted = 0;

            foreach (var pos in area.allPositionsWithin)
            {
                if (_onlyEmptyCells && _tilemap.HasTile(pos)) continue;

                _tilemap.SetTile(pos, PickWeighted(valid, totalWeight, rng));

                if (_randomRotation || _randomFlip)
                {
                    int deg = _randomRotation ? rng.Next(4) * 90 : 0;
                    float flipX = (_randomFlip && rng.Next(2) == 1) ? -1f : 1f;
                    var m = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, deg), new Vector3(flipX, 1f, 1f));
                    _tilemap.SetTransformMatrix(pos, m);
                }
                painted++;
            }

            EditorUtility.SetDirty(_tilemap);
            Debug.Log($"[TilemapRandomFill] Painted {painted} cells in {area.size.x}×{area.size.y} area on '{_tilemap.name}'.");
        }

        /// <summary>Walk the cumulative weights and return the tile whose band the roll lands in.</summary>
        private static TileBase PickWeighted(List<WeightedTile> valid, double totalWeight, System.Random rng)
        {
            double roll = rng.NextDouble() * totalWeight;
            for (int i = 0; i < valid.Count; i++)
            {
                roll -= valid[i].weight;
                if (roll <= 0) return valid[i].tile;
            }
            return valid[valid.Count - 1].tile; // float rounding fallthrough
        }

        // --- helpers ---

        private List<WeightedTile> GetValidEntries()
        {
            var valid = new List<WeightedTile>();
            foreach (var e in _entries)
                if (e.tile != null && e.weight > 0f) valid.Add(e);
            return valid;
        }

        /// <summary>Prefer the tilemap behind the active grid-selection, else the hierarchy selection.</summary>
        private static Tilemap ResolveSelectedTilemap()
        {
            if (UnityEditor.Tilemaps.GridSelection.active && UnityEditor.Tilemaps.GridSelection.target != null)
            {
                var tm = UnityEditor.Tilemaps.GridSelection.target.GetComponent<Tilemap>();
                if (tm != null) return tm;
            }
            if (Selection.activeGameObject != null)
                return Selection.activeGameObject.GetComponent<Tilemap>();
            return null;
        }

        private void HandleDrop(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddTiles(DragAndDrop.objectReferences);
            }
            e.Use();
        }

        private void AddTiles(UnityEngine.Object[] objs)
        {
            foreach (var o in objs)
                if (o is TileBase t && !_entries.Exists(e => e.tile == t))
                    _entries.Add(new WeightedTile(t));
        }
    }
}

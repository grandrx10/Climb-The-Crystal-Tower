using System.Collections.Generic;
using TwoCT.Data;
using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Marks a scene as a free-roam level and defines its walkable bounds + spawn point. Its
    /// presence is how player/camera scripts know they're in free roam (vs. combat/lobby).
    /// </summary>
    public class FreeRoamContext : MonoBehaviour
    {
        public static FreeRoamContext Current { get; private set; }

        [Tooltip("Walkable area size (world units), centred on this transform.")]
        public Vector2 boundsSize = new Vector2(40f, 10f);
        public Transform spawnPoint;

        /// <summary>Optional per-character spawn override for this level.</summary>
        [System.Serializable]
        public class CharacterSpawn
        {
            public CharacterData character;
            public Transform point;
        }

        [Header("Per-character spawns")]
        [Tooltip("Optional. Give specific characters their own spawn point in this level. Any character " +
                 "not listed (or with no point) falls back to the default spawn, spread horizontally so " +
                 "players don't stack on top of each other.")]
        public List<CharacterSpawn> characterSpawns = new List<CharacterSpawn>();

        [Tooltip("Horizontal spacing between players who use the default spawn (world units).")]
        public float defaultSpawnSpread = 1.2f;

        [Header("Depth sorting")]
        [Tooltip("Y-sort world sprites so lower ones draw in front (top-down depth). Sprites at " +
                 "sortingOrder < 0 (backgrounds/parallax/ground) are left alone.")]
        public bool ySortWorldSprites = true;

        private void OnEnable() { Current = this; }
        private void OnDisable() { if (Current == this) Current = null; }

        private void Start()
        {
            if (ySortWorldSprites) SortStaticWorldSprites();
        }

        /// <summary>
        /// One-time Y-sort of the level's static world sprites (props, doors, decorations) by their
        /// bottom edge. Skips background/parallax/ground (negative sorting order) and players (they
        /// re-sort themselves every frame). Call again if you spawn static props after load.
        /// </summary>
        public void SortStaticWorldSprites()
        {
            var renderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            foreach (var sr in renderers)
            {
                if (sr == null || sr.sortingOrder < 0) continue;              // background band
                if (sr.GetComponent<ParallaxLayer>() != null) continue;       // scrolling backdrop
                if (sr.GetComponentInParent<FreeRoamPlayer>() != null) continue; // players self-sort
                // Higher world Z forces a sprite in front regardless of Y (manual depth override).
                sr.sortingOrder = FreeRoamSort.OrderFor(sr.bounds.min.y, sr.transform.position.z);
            }
        }

        public Rect Bounds => new Rect((Vector2)transform.position - boundsSize * 0.5f, boundsSize);
        public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;

        /// <summary>
        /// Where a given player should appear on entering this level. If the character has an
        /// authored per-character spawn point, that wins. Otherwise everyone shares the default
        /// spawn, spread horizontally and centred by <paramref name="index"/> of <paramref name="count"/>
        /// so players don't overlap.
        /// </summary>
        public Vector3 SpawnPositionFor(CharacterData character, int index, int count)
        {
            if (character != null && characterSpawns != null)
                foreach (var cs in characterSpawns)
                    if (cs != null && cs.character == character && cs.point != null)
                        return cs.point.position;

            Vector3 basePos = SpawnPosition;
            if (count <= 1) return basePos;
            float offset = (index - (count - 1) * 0.5f) * defaultSpawnSpread;   // centred fan-out
            return basePos + new Vector3(offset, 0f, 0f);
        }

        public Vector2 ClampToBounds(Vector2 p)
        {
            var b = Bounds;
            return new Vector2(Mathf.Clamp(p.x, b.xMin, b.xMax), Mathf.Clamp(p.y, b.yMin, b.yMax));
        }

        private void OnDrawGizmos()
        {
            // Walkable bounds — always visible (like FreeRoamWall), so you can see the level box
            // without selecting anything.
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(transform.position, boundsSize);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.06f);
            Gizmos.DrawCube(transform.position, boundsSize);

            // Spawn point — where players appear on entering this level. Green marker at the
            // resolved SpawnPosition (falls back to this transform when no spawnPoint is assigned).
            Vector3 s = SpawnPosition;
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(s, 0.35f);
            Gizmos.DrawLine(s + Vector3.left * 0.6f, s + Vector3.right * 0.6f);
            Gizmos.DrawLine(s + Vector3.down * 0.6f, s + Vector3.up * 0.6f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = new Color(0.3f, 1f, 0.4f, 0.9f);
            UnityEditor.Handles.Label(s + Vector3.up * 0.5f,
                spawnPoint != null ? "Spawn" : "Spawn (no spawnPoint → context origin)");
#endif

            // Per-character spawn overrides — orange markers labelled with the character name.
            if (characterSpawns != null)
                foreach (var cs in characterSpawns)
                {
                    if (cs == null || cs.point == null) continue;
                    Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                    Gizmos.DrawWireSphere(cs.point.position, 0.3f);
#if UNITY_EDITOR
                    UnityEditor.Handles.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                    UnityEditor.Handles.Label(cs.point.position + Vector3.up * 0.5f,
                        cs.character != null ? cs.character.name : "Spawn (no character)");
#endif
                }
        }
    }
}

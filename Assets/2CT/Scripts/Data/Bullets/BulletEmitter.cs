using System;
using System.Collections.Generic;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// Base class for a bullet-pattern emitter module. A pattern is a list of these; each
    /// appends its bullets to the shared schedule. Add a new bullet behaviour by subclassing —
    /// it appears in the pattern inspector dropdown automatically (BulletPatternEditor).
    ///
    /// IMPORTANT: generation MUST be deterministic given <paramref name="rng"/> so every client
    /// produces the identical pattern. Never call UnityEngine.Random or Time here.
    /// </summary>
    [Serializable]
    public abstract class BulletEmitter
    {
        [Tooltip("Emitter is only active while pattern time is within [startTime, startTime+duration].")]
        public float startTime = 0f;
        public float duration = 10f;

        [Tooltip("Art for these bullets. Leave empty for the plain circle placeholder. The sprite is " +
                 "auto-scaled to match the collision radius and fades exactly as the bullet does; turn on " +
                 "'Show hitboxes' in the dev panel to see the true collision circle over the art.")]
        public Sprite sprite;

        /// <summary>Append this emitter's scheduled bullets to <paramref name="output"/>.</summary>
        /// <param name="rng">Seeded RNG shared by all clients. Deterministic.</param>
        /// <param name="ctx">World-space layout (muzzle, arena centre/bounds, aim direction).</param>
        public abstract void Generate(List<BulletSpawnData> output, System.Random rng, in PatternContext ctx);

        public abstract string Describe();

        // --- helpers ---------------------------------------------------------
        protected static float RandRange(System.Random rng, float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        protected static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}

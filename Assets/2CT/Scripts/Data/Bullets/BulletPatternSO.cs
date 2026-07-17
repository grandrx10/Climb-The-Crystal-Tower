using System.Collections.Generic;
using System.Linq;
using System.Text;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// A complete boss attack: a set of emitters plus a total duration. The defend phase asks
    /// this for the full deterministic bullet schedule (given the round's shared seed) and
    /// replays it identically on every client.
    /// </summary>
    [CreateAssetMenu(fileName = "Pattern_", menuName = "2CT/Bullet Pattern", order = 10)]
    public class BulletPatternSO : ScriptableObject
    {
        [Header("Timing")]
        [Tooltip("Total length of the defend phase for this attack, in seconds.")]
        public float duration = 10f;

        [Header("Emitters")]
        [SerializeReference] public List<BulletEmitter> emitters = new List<BulletEmitter>();

        /// <summary>
        /// Produce the full bullet schedule. <paramref name="seed"/> is broadcast by the server so
        /// every client generates the same list. <paramref name="ctx"/> supplies the world layout
        /// (muzzle on the right, defend box on the left) so emitters can place bullets precisely.
        /// </summary>
        public List<BulletSpawnData> BuildSchedule(int seed, in PatternContext ctx)
        {
            var rng = new System.Random(seed);
            var result = new List<BulletSpawnData>(256);
            if (emitters != null)
                foreach (var e in emitters)
                    e?.Generate(result, rng, ctx);
            // STABLE sort: a bullet's cross-client Id is its index in this list, so bullets that
            // share a timestamp (e.g. a targeted-bubble cohort spawned together) must keep their
            // deterministic insertion order on every client. List.Sort is NOT stable and would
            // shuffle those ties per-machine, so a hit's ForceDestroy(id) could remove the wrong
            // cohort bubble elsewhere. OrderBy is a documented stable sort.
            return result.OrderBy(b => b.time).ToList();
        }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append($"{duration}s | ");
                if (emitters != null)
                    for (int i = 0; i < emitters.Count; i++)
                        if (emitters[i] != null) sb.Append(emitters[i].Describe()).Append(i < emitters.Count - 1 ? "  |  " : "");
                return sb.ToString();
            }
        }
    }
}

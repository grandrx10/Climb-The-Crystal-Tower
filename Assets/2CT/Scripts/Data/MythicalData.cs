using System.Collections.Generic;
using System.Text;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// A mythical: an extremely powerful artifact. Passives apply their effects once when
    /// combat starts; actives are castable each attack turn for a mana cost. Both reuse the
    /// same composable CardEffect list as cards.
    /// </summary>
    [CreateAssetMenu(fileName = "Mythical_", menuName = "2CT/Mythical", order = 1)]
    public class MythicalData : ScriptableObject
    {
        [Header("Identity")]
        public string mythicalName = "New Mythical";
        public Sprite artwork;
        [TextArea(2, 4)] public string overrideText = "";

        [Header("Behaviour")]
        public MythicalKind kind = MythicalKind.Passive;
        [Tooltip("Active only: mana cost to use it during an attack turn.")]
        public int manaCost = 0;
        public TargetType targetType = TargetType.None;
        public string vfxKey = "";

        [Header("Effects")]
        [Tooltip("Passive: applied once to the owner at combat start. Active: applied each time it is used.")]
        [SerializeReference] public List<CardEffect> effects = new List<CardEffect>();

        public bool NeedsTargetSelection =>
            targetType == TargetType.Ally || targetType == TargetType.AllyOrSelf || targetType == TargetType.DeadAlly;

        public string RulesText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(overrideText)) return overrideText;
                var sb = new StringBuilder();
                if (effects != null)
                    foreach (var e in effects)
                    {
                        if (e == null) continue;
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append(e.Describe());
                    }
                return sb.ToString();
            }
        }

        public void Resolve(ICombatContext ctx)
        {
            if (effects == null) return;
            foreach (var e in effects) e?.Apply(ctx);
        }
    }
}

using System.Collections.Generic;
using System.Text;
using TwoCT.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace TwoCT.Data
{
    /// <summary>
    /// A single card definition. Fully data-driven: mana cost, targeting and the ordered
    /// list of effects are all authored in the inspector. Use the CardData context menu or
    /// the Card Creator window to make new cards.
    /// </summary>
    [CreateAssetMenu(fileName = "Card_", menuName = "2CT/Card", order = 0)]
    public class CardData : ScriptableObject
    {
        [Header("Identity")]
        public string cardName = "New Card";
        public CardRarity rarity = CardRarity.Common;
        [Tooltip("School the card belongs to (Neutral/Incineration/Life/Wild). Organisational for now.")]
        public CardCategory category = CardCategory.Neutral;
        public Sprite artwork;
        [Tooltip("Stable id used for network sync and save data. Must be unique. Auto-filled from the asset name if blank.")]
        public string cardId;

        [Header("Cost & Targeting")]
        public int manaCost = 10;
        public TargetType targetType = TargetType.None;
        [Tooltip("Bubble Power: this card's cost is halved (rounded down) while the caster has a shield.")]
        public bool halfCostWhenShielded = false;
        [Tooltip("Severance: when this card is force-discarded (Slice/Sever/etc.), its effects fire as if played (free). Ending your turn with it in hand does NOT trigger it.")]
        public bool activateWhenDiscarded = false;

        [Header("Effects (resolved top-to-bottom on the server)")]
        [SerializeReference] public List<CardEffect> effects = new List<CardEffect>();

        [Header("Presentation")]
        [Tooltip("VFX played on card resolution. Looked up by CombatVfxLibrary using this key.")]
        public string vfxKey = "";
        [TextArea(2, 4)]
        [FormerlySerializedAs("overrideText")]
        [Tooltip("The card's rules text, shown on the card. Edit it freely here. If you leave it blank, " +
                 "it's auto-generated from the effects (with 'If discarded, activate me.' appended for " +
                 "Severance cards).")]
        public string cardText = "";

        public bool NeedsTargetSelection =>
            targetType == TargetType.Ally || targetType == TargetType.AllyOrSelf || targetType == TargetType.DeadAlly;

        public string RulesText
        {
            get
            {
                // Author-written text wins (edit it in the inspector).
                if (!string.IsNullOrWhiteSpace(cardText)) return cardText;

                // Otherwise auto-generate from the effects…
                var sb = new StringBuilder();
                if (effects != null)
                    for (int i = 0; i < effects.Count; i++)
                    {
                        if (effects[i] == null) continue;
                        if (sb.Length > 0) sb.Append(". ");
                        sb.Append(effects[i].Describe());
                    }
                if (sb.Length > 0) sb.Append(".");
                // …and always spell out the Severance discard trigger.
                if (activateWhenDiscarded)
                {
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append("If discarded, activate me.");
                }
                return sb.ToString();
            }
        }

        /// <summary>Resolve every effect in order. Caller guarantees this runs on the server.</summary>
        public void Resolve(ICombatContext ctx)
        {
            if (effects == null) return;
            foreach (var e in effects)
                e?.Apply(ctx);
        }

        public string Id => string.IsNullOrEmpty(cardId) ? name : cardId;

        /// <summary>True if this is a Copy card (it duplicates your last-played card rather than being
        /// one itself). Used so playing it doesn't overwrite "last card played" with Copy.</summary>
        public bool IsCopyCard
        {
            get
            {
                if (effects == null) return false;
                foreach (var e in effects) if (e is CopyLastCardEffect) return true;
                return false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(cardId)) cardId = name;
        }
#endif
    }
}

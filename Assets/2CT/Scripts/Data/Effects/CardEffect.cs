using System;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// Base class for a single, tunable card effect. Cards hold a <c>[SerializeReference]</c>
    /// list of these, so a designer composes a card from primitive effects entirely in the
    /// inspector (see CardDataEditor for the "Add Effect" dropdown). Add a new mechanic by
    /// creating a new subclass — it appears in the dropdown automatically.
    /// </summary>
    [Serializable]
    public abstract class CardEffect
    {
        /// <summary>Runs on the server when the card resolves. Never touch Netcode here.</summary>
        public abstract void Apply(ICombatContext ctx);

        /// <summary>One-line human summary used by tooling / auto-generated card text.</summary>
        public abstract string Describe();

        /// <summary>Resolves the intended recipient for combatant-targeted effects.</summary>
        protected static ICombatant ResolveRecipient(ICombatContext ctx, EffectTarget target)
        {
            switch (target)
            {
                case EffectTarget.Caster: return ctx.Caster;
                case EffectTarget.ChosenTarget: return ctx.Target ?? ctx.Caster;
                default: return ctx.Caster;
            }
        }
    }

    /// <summary>Which combatant a combatant-facing effect lands on.</summary>
    public enum EffectTarget
    {
        Caster,
        ChosenTarget
    }
}

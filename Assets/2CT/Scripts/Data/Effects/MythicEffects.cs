using System;
using TwoCT.Core;

namespace TwoCT.Data
{
    // Effects used mainly by mythicals (but reusable by any card).

    /// <summary>Permanently add spell-damage bonus (Spellmaster: +5 to all spells).</summary>
    [Serializable]
    public class SpellDamageBonusEffect : CardEffect
    {
        public int amount = 5;
        public override void Apply(ICombatContext ctx) => ctx.Caster.AddSpellDamageBonus(amount);
        public override string Describe() => $"All spells deal +{amount} damage";
    }

    /// <summary>Cheat Death: you may not drop below 1 HP this defending round.</summary>
    [Serializable]
    public class CheatDeathEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.Caster.SetCheatDeathThisRound(true);
        public override string Describe() => "You may not drop below 1 HP this defending round";
    }
}

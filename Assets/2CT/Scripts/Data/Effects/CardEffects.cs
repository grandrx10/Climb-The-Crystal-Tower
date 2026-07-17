using System;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Data
{
    // ---------------------------------------------------------------------
    // Concrete card effects. Each is a small, self-describing, inspector-tunable
    // unit. Compose them in a CardData's effect list to build any card in the doc.
    // ---------------------------------------------------------------------

    /// <summary>Deal damage to the boss. Flag it as a spell so Spellmaster/lifesteal apply.</summary>
    [Serializable]
    public class DealDamageEffect : CardEffect
    {
        public int damage = 10;
        [Tooltip("Spells receive SpellDamageBonus and can trigger lifesteal. Attacks (e.g. Loyal Blade) do not.")]
        public bool countsAsSpell = true;

        public override void Apply(ICombatContext ctx) => ctx.DealDamageToEnemy(damage, countsAsSpell);
        public override string Describe() => $"Deal {damage} damage";
    }

    /// <summary>Grant a shield that soaks defend-phase damage.</summary>
    [Serializable]
    public class GainShieldEffect : CardEffect
    {
        public EffectTarget target = EffectTarget.Caster;
        public int shield = 10;
        [Tooltip("How many defend rounds the shield persists before expiring. 0 = until consumed.")]
        public int roundsDuration = 0;

        public override void Apply(ICombatContext ctx) => ResolveRecipient(ctx, target).GainShield(shield, roundsDuration);
        public override string Describe() => $"Gain a {shield} HP shield" + (roundsDuration > 0 ? $" for {roundsDuration} round(s)" : "");
    }

    /// <summary>Heal a combatant.</summary>
    [Serializable]
    public class HealEffect : CardEffect
    {
        public EffectTarget target = EffectTarget.ChosenTarget;
        public int amount = 10;

        public override void Apply(ICombatContext ctx) => ResolveRecipient(ctx, target).Heal(amount);
        public override string Describe() => $"Restore {amount} HP";
    }

    /// <summary>Deal damage to yourself (Searing Power).</summary>
    [Serializable]
    public class SelfDamageEffect : CardEffect
    {
        public int amount = 10;
        public override void Apply(ICombatContext ctx) => ctx.Caster.TakeDamage(amount);
        public override string Describe() => $"Take {amount} damage";
    }

    /// <summary>Gain mana immediately this round.</summary>
    [Serializable]
    public class GainManaEffect : CardEffect
    {
        public int amount = 10;
        public override void Apply(ICombatContext ctx) => ctx.Caster.GainMana(amount);
        public override string Describe() => $"Gain {amount} mana";
    }

    /// <summary>Permanently gain mana income every future round (Searing Power / Manablessed).</summary>
    [Serializable]
    public class GainManaPerTurnEffect : CardEffect
    {
        public int amount = 10;
        public override void Apply(ICombatContext ctx) => ctx.Caster.GainManaPerTurn(amount);
        public override string Describe() => $"Gain +{amount} mana per turn";
    }

    /// <summary>Draw extra cards every future round (Preparation / Greed Spell).</summary>
    [Serializable]
    public class GainCardDrawEffect : CardEffect
    {
        public int amount = 1;
        public override void Apply(ICombatContext ctx) => ctx.Caster.GainPersistentCardDraw(amount);
        public override string Describe() => $"Gain +{amount} card draw";
    }

    /// <summary>Draw cards right now (Vine Strike).</summary>
    [Serializable]
    public class DrawCardsNowEffect : CardEffect
    {
        public int amount = 1;
        public override void Apply(ICombatContext ctx) => ctx.DrawCardsNow(ctx.Caster, amount);
        public override string Describe() => $"Draw {amount} card(s)";
    }

    /// <summary>Discard the whole hand, then draw 1 + (number discarded) (Divination).</summary>
    [Serializable]
    public class DiscardAndDrawEffect : CardEffect
    {
        public int baseDraw = 1;
        public override void Apply(ICombatContext ctx)
        {
            int discarded = ctx.DiscardHand(ctx.Caster);
            ctx.DrawCardsNow(ctx.Caster, baseDraw + discarded);
        }
        public override string Describe() => $"Discard your hand, draw {baseDraw} + discarded";
    }

    /// <summary>Apply stacks of Fire to the boss (Bolt of Flame).</summary>
    [Serializable]
    public class ApplyFireEffect : CardEffect
    {
        public int stacks = 1;
        public int turns = 2;
        public override void Apply(ICombatContext ctx) => ctx.Enemy.ApplyFire(stacks, turns);
        public override string Describe() => $"Apply {stacks} stack(s) of Fire for {turns} turn(s)";
    }

    /// <summary>Revive a knocked-out ally (Heart Starter). They may act immediately.</summary>
    [Serializable]
    public class ReviveEffect : CardEffect
    {
        public int hp = 1;
        public bool canActImmediately = true;
        public override void Apply(ICombatContext ctx) => ResolveRecipient(ctx, EffectTarget.ChosenTarget).ReviveWithHealth(hp, canActImmediately);
        public override string Describe() => $"Revive an ally with {hp} HP" + (canActImmediately ? " (may act)" : "");
    }

    /// <summary>Reduce incoming damage next defend round (Feral Growth).</summary>
    [Serializable]
    public class DamageReductionEffect : CardEffect
    {
        [Range(0f, 1f)] public float reduction = 0.5f;
        public bool alsoEnlarge = true;
        public override void Apply(ICombatContext ctx)
        {
            ctx.Caster.SetDamageReductionNextRound(reduction);
            if (alsoEnlarge) ctx.Caster.SetEnlargeNextDefend(true);
        }
        public override string Describe() => $"Take {Mathf.RoundToInt(reduction * 100)}% less damage next round" + (alsoEnlarge ? " (Enlarge)" : "");
    }

    /// <summary>Arm lifesteal on the caster's next spell (Lifestealer Aura).</summary>
    [Serializable]
    public class ArmLifestealEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.Caster.ArmLifestealNextSpell();
        public override string Describe() => "Your next spell gains Lifesteal";
    }

    /// <summary>Draw and immediately cast a random spell from the deck (Mask of Wild Magic).</summary>
    [Serializable]
    public class CastRandomSpellEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.CastRandomSpellFromDeck(ctx.Caster);
        public override string Describe() => "Cast a random spell from your deck";
    }

    /// <summary>Discard your whole hand and redraw your normal start-of-turn hand (Divination rework).
    /// The discard is "forced", so it fires any Severance activate-on-discard cards.</summary>
    [Serializable]
    public class DiscardAndRedrawEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx)
        {
            ctx.DiscardHandTriggering(ctx.Caster);
            ctx.DrawCardsNow(ctx.Caster, ctx.Caster.CardsToDrawThisRound);
        }
        public override string Describe() => "Discard your hand and redraw a full hand";
    }

    /// <summary>Force-discard N cards from your hand (Slice). Fires Severance activate-on-discard cards.</summary>
    [Serializable]
    public class DiscardCardsEffect : CardEffect
    {
        public int count = 2;
        public override void Apply(ICombatContext ctx) => ctx.DiscardCardsTriggering(ctx.Caster, count);
        public override string Describe() => $"Discard {count} card(s) from your hand";
    }

    /// <summary>Discard your whole hand and deal damage per card discarded (Sever). Fires Severance cards.</summary>
    [Serializable]
    public class DiscardHandForDamageEffect : CardEffect
    {
        public int damagePerCard = 10;
        public bool countsAsSpell = false;
        public override void Apply(ICombatContext ctx)
        {
            int n = ctx.DiscardHandTriggering(ctx.Caster);
            if (n > 0) ctx.DealDamageToEnemy(damagePerCard * n, countsAsSpell);
        }
        public override string Describe() => $"Discard your hand; deal {damagePerCard} damage per card discarded";
    }

    /// <summary>Turn this card into a (tinted) copy of the last card you played this turn (Copy).</summary>
    [Serializable]
    public class CopyLastCardEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.CopyLastPlayedIntoHand(ctx.Caster);
        public override string Describe() => "Become a copy of the last card you played this turn";
    }

    /// <summary>Arm Flawless: if you take no damage next defend round, halve your card costs next turn.</summary>
    [Serializable]
    public class ArmFlawlessEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.Caster.ArmFlawless();
        public override string Describe() => "If you take no damage next turn, halve your card costs next turn";
    }

    /// <summary>Deal damage equal to a multiple of your current shield, then remove your shield (Bubble Pop).</summary>
    [Serializable]
    public class BubblePopEffect : CardEffect
    {
        public int shieldMultiplier = 2;
        public bool countsAsSpell = true;
        public override void Apply(ICombatContext ctx)
        {
            int dmg = ctx.Caster.ShieldValue * shieldMultiplier;
            if (dmg > 0) ctx.DealDamageToEnemy(dmg, countsAsSpell);
            ctx.Caster.RemoveShield();
        }
        public override string Describe() => $"Deal {shieldMultiplier}× your shield as damage, then lose your shield";
    }

    /// <summary>Double (or more) all shield you gain for the rest of this turn (Film Form).</summary>
    [Serializable]
    public class ShieldGainMultiplierEffect : CardEffect
    {
        public int multiplier = 2;
        public override void Apply(ICombatContext ctx) => ctx.Caster.SetShieldGainMultiplierThisTurn(multiplier);
        public override string Describe() => $"Shield you gain this turn is {(multiplier == 2 ? "doubled" : $"×{multiplier}")}";
    }

    /// <summary>Also gain shield at the start of next turn (Bubble Shield's "and next turn" half).</summary>
    [Serializable]
    public class GainShieldNextTurnEffect : CardEffect
    {
        public int shield = 15;
        public override void Apply(ICombatContext ctx) => ctx.Caster.AddPendingShieldNextTurn(shield);
        public override string Describe() => $"Gain a {shield} HP shield next turn";
    }

    /// <summary>Set your hand aside and return it next turn on top of your draw (Bubble Storage). Not a discard.</summary>
    [Serializable]
    public class StoreHandEffect : CardEffect
    {
        public override void Apply(ICombatContext ctx) => ctx.Caster.StoreHandForNextTurn();
        public override string Describe() => "Store your hand; return it next turn on top of your draw";
    }
}

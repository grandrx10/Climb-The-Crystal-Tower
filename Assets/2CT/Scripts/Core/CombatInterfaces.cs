namespace TwoCT.Core
{
    /// <summary>
    /// A combatant a card can affect (a player). Implemented by PlayerCombatant.
    /// All mutating calls are expected to run on the server (host); the interface
    /// keeps card-effect code free of Netcode types so effects stay data-only.
    /// </summary>
    public interface ICombatant
    {
        int PlayerIndex { get; }
        bool IsAlive { get; }

        /// <summary>Current shield value (for Bubble Pop, which spends it as damage).</summary>
        int ShieldValue { get; }

        /// <summary>How many cards this combatant draws at the start of a turn (for Divination's redraw).</summary>
        int CardsToDrawThisRound { get; }

        /// <summary>Bonus damage added to every *spell* this combatant casts (Spellmaster).</summary>
        int SpellDamageBonus { get; }
        void AddSpellDamageBonus(int amount);

        /// <summary>Cheat Death: cannot drop below 1 HP for the coming defend round.</summary>
        void SetCheatDeathThisRound(bool enabled);

        void TakeDamage(int amount);
        void Heal(int amount);
        void GainShield(int amount, int roundsDuration);
        void GainMana(int amount);
        void GainManaPerTurn(int amount);

        /// <summary>Extra cards drawn every future round (Preparation / Greed Spell).</summary>
        void GainPersistentCardDraw(int amount);

        void ReviveWithHealth(int hp, bool canActImmediately);
        void SetDamageReductionNextRound(float pct);
        void SetEnlargeNextDefend(bool enabled);

        /// <summary>Zero out the current shield (Bubble Pop, after converting it to damage).</summary>
        void RemoveShield();

        /// <summary>Bubble Shield: also grant this much shield at the start of next turn.</summary>
        void AddPendingShieldNextTurn(int amount);

        /// <summary>Film Form: multiply all shield GAINED for the rest of this turn (e.g. 2 = doubled).</summary>
        void SetShieldGainMultiplierThisTurn(int multiplier);

        /// <summary>Bubble Storage: set the current hand aside and return it next turn (not a discard).</summary>
        void StoreHandForNextTurn();

        /// <summary>Flawless: if this combatant takes no damage in the coming defend round, halve its
        /// card costs on the following attack turn.</summary>
        void ArmFlawless();

        /// <summary>Arms lifesteal on the next spell this combatant casts this turn.</summary>
        void ArmLifestealNextSpell();

        /// <summary>Returns true (and disarms) if lifesteal was armed. Used by the context on spell damage.</summary>
        bool ConsumeLifestealIfArmed();
    }

    /// <summary>The enemy being fought (a boss). Implemented by BossController.</summary>
    public interface IEnemy
    {
        bool IsAlive { get; }
        void ApplyFire(int stacks, int turns);
    }

    /// <summary>
    /// Everything a card effect needs at resolution time. Built and executed on the
    /// server. Centralises shared rules (spell-damage bonus, lifesteal, draw) so
    /// individual effects stay tiny and declarative.
    /// </summary>
    public interface ICombatContext
    {
        ICombatant Caster { get; }
        ICombatant Target { get; }   // may be null when the card needs no target
        IEnemy Enemy { get; }

        /// <summary>
        /// Deal damage to the boss, routing through shared rules: when <paramref name="isSpell"/>
        /// the caster's SpellDamageBonus is added, and an armed lifesteal heals the caster.
        /// </summary>
        void DealDamageToEnemy(int amount, bool isSpell);

        void DrawCardsNow(ICombatant who, int amount);

        /// <summary>Discard the combatant's whole hand; returns how many were discarded.</summary>
        int DiscardHand(ICombatant who);

        /// <summary>Force-discard the whole hand, firing each Severance card's activate-on-discard
        /// effect; returns how many were discarded (Sever, Divination).</summary>
        int DiscardHandTriggering(ICombatant who);

        /// <summary>Force-discard up to <paramref name="count"/> cards from the hand, firing each
        /// Severance card's activate-on-discard effect; returns how many were discarded (Slice).</summary>
        int DiscardCardsTriggering(ICombatant who, int count);

        /// <summary>Copy: add a tinted duplicate of the combatant's last-played card to its hand.</summary>
        void CopyLastPlayedIntoHand(ICombatant who);

        /// <summary>Draw one random card from the deck and immediately resolve it for free.</summary>
        void CastRandomSpellFromDeck(ICombatant who);
    }
}

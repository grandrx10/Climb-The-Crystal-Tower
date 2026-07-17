using System.Collections.Generic;
using TwoCT.Core;
using TwoCT.Data;
using Unity.Netcode;
using UnityEngine;

namespace TwoCT.Combat
{
    /// <summary>
    /// One player's combat state. Networked and server-authoritative: all HP/mana/shield live in
    /// NetworkVariables written only by the host. The owning client reads them for its HUD and
    /// controls its own dodge icon in the defend phase. Implements <see cref="ICombatant"/> so
    /// card effects can mutate it without touching Netcode types.
    /// </summary>
    public class PlayerCombatant : NetworkBehaviour, ICombatant
    {
        // ---- Networked, replicated state -----------------------------------
        public readonly NetworkVariable<int> Slot = new(-1);          // seat 0..2
        public readonly NetworkVariable<int> CharacterIndex = new(-1);
        public readonly NetworkVariable<int> HP = new(30);
        public readonly NetworkVariable<int> MaxHP = new(30);
        public readonly NetworkVariable<int> Shield = new(0);
        public readonly NetworkVariable<int> Mana = new(0);
        public readonly NetworkVariable<bool> KnockedOut = new(false);
        /// <summary>Whether this player may play cards this attack turn (false while revived-but-waiting).</summary>
        public readonly NetworkVariable<bool> CanAct = new(true);
        public readonly NetworkVariable<bool> HasEndedTurn = new(false);
        /// <summary>How many times this turn's card costs are halved (Flawless). Replicated so the
        /// owner's UI can show the reduced cost.</summary>
        public readonly NetworkVariable<int> CostHalvings = new(0);
        public readonly NetworkVariable<bool> EnlargeActive = new(false);
        /// <summary>Local dodge-icon position in the arena, written by the owner so peers can render it.</summary>
        public readonly NetworkVariable<Vector2> ArenaPos = new(default, default, NetworkVariableWritePermission.Owner);

        // ---- Server-only runtime -------------------------------------------
        private CharacterData _character;
        private readonly Deck _deck = new Deck();
        private int _manaPerRound;
        private int _baseCardsPerRound;
        private int _extraCardDraw;              // persistent bonus (Preparation / Greed)
        private int _manaPerTurnBonus;           // persistent bonus (Searing Power / Manablessed)
        private int _spellDamageBonus;           // Spellmaster
        private float _pendingDamageReduction;   // set by cards, consumed next defend
        private float _activeDamageReduction;     // in effect for the current defend round
        private bool _pendingEnlarge;
        private bool _lifestealArmed;
        private bool _cheatDeathArmedNext;       // set by card, applied to next defend
        private bool _cheatDeathActive;          // active during current defend round
        private int _shieldExpiryRound = int.MaxValue;
        private int _pendingCostHalvings;        // halvings to apply next attack turn (Flawless payoff)
        private bool _flawlessArmed;             // watching the coming defend round for zero damage
        private bool _tookDamageThisDefend;      // did this player take any damage this defend round?
        private int _shieldMultiplierThisTurn = 1;   // Film Form: multiplies shield gained this turn
        private int _pendingShieldNextTurn;      // Bubble Shield: shield granted at next turn start
        private bool _returnStoredNextTurn;      // Bubble Storage: return the stored hand next turn
        private string _lastPlayedCardId;        // Copy: the last non-copy card played this turn

        public const int MaxCardsPerRound = 10;

        public Deck Deck => _deck;
        public int PlayerIndex => Slot.Value;
        public bool IsAlive => !KnockedOut.Value && HP.Value > 0;
        public int SpellDamageBonus => _spellDamageBonus;
        public int ManaValue => Mana.Value;
        public int ShieldValue => Shield.Value;
        public string LastPlayedCardId => _lastPlayedCardId;
        public void SetLastPlayedCard(string id) { if (IsServer) _lastPlayedCardId = id; }

        /// <summary>Effective mana cost of a card for this player right now: base cost halved (rounded
        /// down, compounding) once per active Flawless halving, and once more for a
        /// <see cref="CardData.halfCostWhenShielded"/> card while shielded. Never below 0. Reads the
        /// replicated <see cref="CostHalvings"/> + <see cref="Shield"/>, so it's correct on client + server.</summary>
        public int EffectiveCost(CardData card)
        {
            if (card == null) return 0;
            int cost = card.manaCost;
            int halvings = CostHalvings.Value;
            if (card.halfCostWhenShielded && Shield.Value > 0) halvings++;
            for (int i = 0; i < halvings && cost > 0; i++) cost /= 2;   // round down, stack
            return cost;
        }

        /// <summary>Runtime multiplier on move speed (1 = normal). Hook for future speed cards/mythicals.</summary>
        public float MoveSpeedMultiplier { get; set; } = 1f;

        /// <summary>The chosen character, resolved via the replicated <see cref="CharacterIndex"/> so
        /// it's available on every client (the private <c>_character</c> is server-only).</summary>
        public CharacterData Character
        {
            get
            {
                var reg = ContentRegistry.Instance;
                int ci = CharacterIndex.Value;
                return reg != null && ci >= 0 && ci < reg.characters.Count ? reg.characters[ci] : null;
            }
        }

        /// <summary>Effective per-axis move speed = character base × runtime multiplier. Callers
        /// (free-roam, dodge icon) apply any context scaling (e.g. the arena slow) on top.</summary>
        public Vector2 MoveSpeed => (Character != null ? Character.moveSpeed : new Vector2(4.5f, 4.5f)) * MoveSpeedMultiplier;

        public override void OnNetworkSpawn() => PlayerRegistry.Register(this);
        public override void OnNetworkDespawn() => PlayerRegistry.Unregister(this);

        // =====================================================================
        //  Server setup
        // =====================================================================
        public void ServerInitialize(int slot, CharacterData character, IReadOnlyList<CardData> startingDeck, int deckSeed)
        {
            if (!IsServer) return;
            _character = character;
            Slot.Value = slot;
            MaxHP.Value = character.maxHP;
            HP.Value = character.maxHP;
            Mana.Value = 0;
            Shield.Value = 0;
            KnockedOut.Value = false;
            CanAct.Value = true;
            _manaPerRound = character.manaPerRound;
            _baseCardsPerRound = character.baseCardsPerRound;
            _extraCardDraw = 0;
            _manaPerTurnBonus = 0;
            _deck.Initialize(startingDeck, deckSeed);
            // Sync which character this is, so every client's avatar can show the right sprite.
            var reg = ContentRegistry.Instance;
            CharacterIndex.Value = reg != null ? reg.characters.IndexOf(character) : -1;
        }

        public int CardsToDrawThisRound => Mathf.Min(_baseCardsPerRound + _extraCardDraw, MaxCardsPerRound);

        /// <summary>Begin an attack turn: gain mana income and draw the hand (server-side).</summary>
        public void ServerBeginAttackTurn()
        {
            if (!IsServer) return;
            HasEndedTurn.Value = false;
            // Per-turn buff bookkeeping (reset each turn; Flawless carries a payoff from last defend).
            CostHalvings.Value = _pendingCostHalvings;   // replicated so the owner UI shows the discount
            _pendingCostHalvings = 0;
            _shieldMultiplierThisTurn = 1;
            _lastPlayedCardId = null;
            if (_pendingShieldNextTurn > 0) { GainShield(_pendingShieldNextTurn, 0); _pendingShieldNextTurn = 0; }  // Bubble Shield

            if (!IsAlive) { HasEndedTurn.Value = true; return; }
            Mana.Value += _manaPerRound + _manaPerTurnBonus;
            if (CanAct.Value)
            {
                _deck.DrawCards(CardsToDrawThisRound);
                if (_returnStoredNextTurn) { _deck.ReturnStoredToHand(); _returnStoredNextTurn = false; }  // Bubble Storage
            }
            else
                HasEndedTurn.Value = true; // revived-but-waiting players sit this turn out
        }

        /// <summary>End the attack turn: reshuffle used + unused cards to the bottom.</summary>
        public void ServerEndAttackTurn()
        {
            if (!IsServer) return;
            _deck.EndRound();
            HasEndedTurn.Value = true;
        }

        public bool ServerTrySpendMana(int cost)
        {
            if (!IsServer || Mana.Value < cost) return false;
            Mana.Value -= cost;
            return true;
        }

        // =====================================================================
        //  Defend-phase lifecycle
        // =====================================================================
        public void ServerBeginDefend(int defendRoundNumber)
        {
            if (!IsServer) return;
            _tookDamageThisDefend = false;               // Flawless watches this round for zero damage
            _activeDamageReduction = _pendingDamageReduction;
            _pendingDamageReduction = 0f;
            _cheatDeathActive = _cheatDeathArmedNext;
            _cheatDeathArmedNext = false;
            EnlargeActive.Value = _pendingEnlarge;
            _pendingEnlarge = false;
            if (defendRoundNumber >= _shieldExpiryRound)
            {
                Shield.Value = 0;
                _shieldExpiryRound = int.MaxValue;
            }
        }

        /// <summary>Called after a defend round ends; clears one-round buffs and un-benches revived players.</summary>
        public void ServerEndDefend()
        {
            if (!IsServer) return;
            // Flawless payoff: took no damage this round → halve card costs next attack turn.
            if (_flawlessArmed && !_tookDamageThisDefend) _pendingCostHalvings++;
            _flawlessArmed = false;
            _activeDamageReduction = 0f;
            _cheatDeathActive = false;
            EnlargeActive.Value = false;
            if (IsAlive) CanAct.Value = true; // survived a defend round -> may act again
        }

        /// <summary>
        /// Server entry point for bullet damage reported by the owning client. Applies damage
        /// reduction, then shield, then HP, honouring Cheat Death. Returns actual HP+shield lost.
        /// </summary>
        public int ServerApplyBulletDamage(int rawAmount)
        {
            if (!IsServer || !IsAlive) return 0;
            int amount = Mathf.RoundToInt(rawAmount * (1f - _activeDamageReduction));
            int before = HP.Value + Shield.Value;

            int fromShield = Mathf.Min(Shield.Value, amount);
            Shield.Value -= fromShield;
            int toHP = amount - fromShield;
            int newHP = HP.Value - toHP;

            if (_cheatDeathActive && newHP < 1) newHP = 1;
            HP.Value = Mathf.Max(newHP, 0);

            int lost = before - (HP.Value + Shield.Value);
            if (lost > 0) _tookDamageThisDefend = true;   // breaks Flawless
            if (HP.Value <= 0) Knockout();
            NotifyDamageClientRpc(lost);
            return lost;
        }

        private void Knockout()
        {
            KnockedOut.Value = true;
            CanAct.Value = false;
        }

        // =====================================================================
        //  ICombatant (server-side, called by card effects)
        // =====================================================================
        public void TakeDamage(int amount)
        {
            if (!IsServer || !IsAlive || amount <= 0) return;
            // Self-damage from card costs (e.g. Searing Power) is absorbed by shield first, then HP —
            // same as any other damage. (It skips the defend-phase damage-reduction / cheat-death,
            // which are bullet-defense mechanics, not card costs.)
            int fromShield = Mathf.Min(Shield.Value, amount);
            Shield.Value -= fromShield;
            int toHP = amount - fromShield;
            if (toHP > 0) HP.Value = Mathf.Max(HP.Value - toHP, 0);
            if (HP.Value <= 0) Knockout();
        }

        public void Heal(int amount)
        {
            if (!IsServer || !IsAlive) return;      // healing does not revive; use ReviveWithHealth
            HP.Value = Mathf.Min(HP.Value + amount, MaxHP.Value);
        }

        public void GainShield(int amount, int roundsDuration)
        {
            if (!IsServer) return;
            Shield.Value += Mathf.Max(0, amount) * _shieldMultiplierThisTurn;   // Film Form doubles gains this turn
            _shieldExpiryRound = roundsDuration > 0 ? CombatManager.CurrentDefendRound + roundsDuration : int.MaxValue;
        }

        public void RemoveShield() { if (IsServer) Shield.Value = 0; }   // Bubble Pop, after spending it as damage

        /// <summary>Film Form: multiply shield gained for the rest of this attack turn.</summary>
        public void SetShieldGainMultiplierThisTurn(int multiplier) { if (IsServer) _shieldMultiplierThisTurn = Mathf.Max(1, multiplier); }

        /// <summary>Bubble Shield: also grant this much shield when the next attack turn begins.</summary>
        public void AddPendingShieldNextTurn(int amount) { if (IsServer) _pendingShieldNextTurn += Mathf.Max(0, amount); }

        /// <summary>Bubble Storage: set the current hand aside; it returns next turn on top of the draw.</summary>
        public void StoreHandForNextTurn() { if (!IsServer) return; _deck.StoreHand(); _returnStoredNextTurn = true; }

        /// <summary>Flawless: watch the coming defend round; if no damage is taken, halve costs next turn.</summary>
        public void ArmFlawless() { if (IsServer) _flawlessArmed = true; }

        public void GainMana(int amount) { if (IsServer) Mana.Value += Mathf.Max(0, amount); }
        public void GainManaPerTurn(int amount) { if (IsServer) _manaPerTurnBonus += amount; }

        public void GainPersistentCardDraw(int amount)
        {
            if (!IsServer) return;
            _extraCardDraw = Mathf.Min(_extraCardDraw + amount, MaxCardsPerRound - _baseCardsPerRound);
        }

        public void ReviveWithHealth(int hp, bool canActImmediately)
        {
            if (!IsServer || IsAlive) return;
            KnockedOut.Value = false;
            HP.Value = Mathf.Clamp(hp, 1, MaxHP.Value);
            CanAct.Value = canActImmediately;
            if (canActImmediately)
            {
                // Heart Starter: give them a fresh hand so they can act this turn.
                Mana.Value += _manaPerRound + _manaPerTurnBonus;
                _deck.DrawCards(CardsToDrawThisRound);
                HasEndedTurn.Value = false;
            }
        }

        public void SetDamageReductionNextRound(float pct) { if (IsServer) _pendingDamageReduction = Mathf.Clamp01(pct); }
        public void SetEnlargeNextDefend(bool enabled) { if (IsServer) _pendingEnlarge = enabled; }
        public void ArmLifestealNextSpell() { if (IsServer) _lifestealArmed = true; }
        public bool ConsumeLifestealIfArmed()
        {
            if (!_lifestealArmed) return false;
            _lifestealArmed = false;
            return true;
        }
        public void AddSpellDamageBonus(int amount) { if (IsServer) _spellDamageBonus += amount; }
        public void SetCheatDeathThisRound(bool enabled) { if (IsServer) _cheatDeathArmedNext = enabled; }

        /// <summary>
        /// Called by the OWNER's local bullet sim when its dodge icon is struck. Because bullets
        /// are simulated client-side, only the client that actually saw the hit reports it — you
        /// can never be hit by a bullet you didn't see. Co-op (non-competitive) so the host trusts it.
        /// </summary>
        [ServerRpc]
        public void ReportBulletHitServerRpc(int rawDamage)
        {
            if (CombatManager.Instance == null || !CombatManager.Instance.IsDefendPhase) return;
            ServerApplyBulletDamage(rawDamage);
        }

        // =====================================================================
        //  Client feedback
        // =====================================================================
        [ClientRpc]
        private void NotifyDamageClientRpc(int amount)
        {
            if (amount <= 0) return;
            CombatEvents.RaiseDamageNumber(Slot.Value, amount, transform.position);
        }
    }
}

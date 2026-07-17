using System.Collections;
using System.Collections.Generic;
using TwoCT.Bullets;
using TwoCT.Core;
using TwoCT.Data;
using TwoCT.FreeRoam;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TwoCT.Combat
{
    /// <summary>
    /// Authoritative combat director. Owns the Intro → (Attack → Defend)* → Victory/Defeat state
    /// machine, validates and resolves card plays, and orchestrates the client-sided defend phase.
    /// Runs its logic on the server; clients receive replicated phase state plus targeted RPCs
    /// (their hand, VFX cues, defend cues).
    /// </summary>
    public class CombatManager : NetworkBehaviour, ICombatContext
    {
        public static CombatManager Instance { get; private set; }

        [Header("Scene references (wire in the combat scene)")]
        [SerializeField] private BossController boss;
        [SerializeField] private BulletSystem bulletSystem;
        [SerializeField] private Transform bossMuzzle;
        [SerializeField] private DefendArena arena;

        [Header("Test encounter (host uses this when starting solo)")]
        [SerializeField] private BossData testBoss;
        [SerializeField] private List<CharacterData> defaultCharacters = new List<CharacterData>();

        [Header("Pacing")]
        [Tooltip("Beat after the defend box appears before the boss's bullets begin.")]
        [SerializeField] private float preAttackPause = 0.6f;
        [Tooltip("Beat at the start of each attack turn before the hand is drawn.")]
        [SerializeField] private float preDrawPause = 0.6f;

        [Header("Victory")]
        [Tooltip("Seconds the boss takes to fade out after speaking its last words.")]
        [SerializeField] private float bossFadeDuration = 1.2f;
        [Tooltip("Seconds the VICTORY screen holds before returning to the level.")]
        [SerializeField] private float victoryBannerSeconds = 3f;
        [Tooltip("Screen fade-to-black time when leaving combat back to the level.")]
        [SerializeField] private float returnFadeDuration = 0.5f;

        [Header("Rewards")]
        [Tooltip("How many cards each player permanently adds to their deck after a boss win.")]
        [SerializeField] private int rewardPicks = 4;
        [Tooltip("How many cards are offered per pick (choose 1 of these).")]
        [SerializeField] private int rewardChoices = 3;
        [Tooltip("Safety cap: proceed to the level even if someone hasn't finished picking after this long.")]
        [SerializeField] private float rewardTimeout = 120f;

        public readonly NetworkVariable<CombatPhase> Phase = new(CombatPhase.Intro);
        public readonly NetworkVariable<int> RoundNumber = new(0);

        /// <summary>Mirrors the current defend round on the server (used by shield expiry timing).</summary>
        public static int CurrentDefendRound { get; private set; }

        private readonly List<PlayerCombatant> _players = new List<PlayerCombatant>();
        private readonly Dictionary<ulong, PlayerCombatant> _byClient = new Dictionary<ulong, PlayerCombatant>();
        private bool _combatRunning;

        public bool IsDefendPhase => Phase.Value == CombatPhase.Defend;
        public BossController Boss => boss;

        // ---- ICombatContext transient state (set per resolution) -----------
        private PlayerCombatant _ctxCaster;
        private PlayerCombatant _ctxTarget;
        ICombatant ICombatContext.Caster => _ctxCaster;
        ICombatant ICombatContext.Target => _ctxTarget;
        IEnemy ICombatContext.Enemy => boss;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += (_, p) => CombatEvents.RaisePhaseChanged(p);
            // Auto-start whenever this scene is hosted (solo works with no extra clicks). The
            // encounter opens with the boss's intro conversation before the first attack turn.
            if (IsServer) StartCoroutine(AutoStartWhenReady());
        }

        private IEnumerator AutoStartWhenReady()
        {
            yield return new WaitUntil(() =>
            {
                if (NetworkManager.ConnectedClients.Count == 0) return false;
                foreach (var kv in NetworkManager.ConnectedClients)
                    if (kv.Value.PlayerObject == null) return false;
                return true;
            });
            yield return null; // let PlayerObjects finish spawning components
            Debug.Log($"[2CT] Auto-starting encounter with {NetworkManager.ConnectedClients.Count} player(s).");
            ServerBeginTestEncounter();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // =====================================================================
        //  Encounter setup (server)
        // =====================================================================
        /// <summary>Registers combatants, seats them and starts the encounter. Works with 1–3 players.</summary>
        public void ServerStartCombat(BossData bossData, IReadOnlyList<(ulong clientId, PlayerCombatant combatant, CharacterData character)> roster)
        {
            if (!IsServer || _combatRunning) return;
            _players.Clear();
            _byClient.Clear();
            var reg = ContentRegistry.Instance;
            int seed = Random.Range(int.MinValue, int.MaxValue);
            for (int i = 0; i < roster.Count; i++)
            {
                var (clientId, combatant, character) = roster[i];
                combatant.ServerInitialize(i, character, ComposeDeck(reg, clientId, character), seed + i * 7919);
                _players.Add(combatant);
                _byClient[clientId] = combatant;
            }
            boss.ServerInitialize(bossData);
            _combatRunning = true;
            StartCoroutine(RunCombat());
        }

        /// <summary>
        /// Convenience for the dev panel: builds a roster from everyone currently connected
        /// (1–3 players) using the default characters, and starts the test boss encounter.
        /// </summary>
        public void ServerBeginTestEncounter()
        {
            if (!IsServer) return;
            var reg = ContentRegistry.Instance;

            // Boss selection order:
            //  1) the boss the combat trigger picked (SessionData.SelectedBossId) — this makes the
            //     combat scene universal: one scene, whichever boss the encounter asked for;
            //  2) the scene-wired testBoss (standalone/dev runs of the combat scene);
            //  3) the first registry boss (scene built before content existed).
            BossData bossData = null;
            if (!string.IsNullOrEmpty(SessionData.SelectedBossId) && reg != null)
                bossData = reg.GetBoss(SessionData.SelectedBossId);
            if (bossData == null) bossData = testBoss;
            if (bossData == null && reg != null && reg.bosses.Count > 0) bossData = reg.bosses[0];
            if (bossData == null) { Debug.LogError("[2CT] No boss available. Assign CombatManager.testBoss or run '2CT ▸ Generate Sample Content'."); return; }

            var roster = new List<(ulong, PlayerCombatant, CharacterData)>();
            int idx = 0;
            foreach (var kv in NetworkManager.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                var pc = po != null ? po.GetComponent<PlayerCombatant>() : null;
                if (pc == null) continue;

                // Character: lobby pick → scene defaults → registry (in that order).
                CharacterData ch = null;
                if (SessionData.CharacterByClient.TryGetValue(kv.Key, out var ci) && reg != null && ci >= 0 && ci < reg.characters.Count)
                    ch = reg.characters[ci];
                if (ch == null && defaultCharacters.Count > 0) ch = defaultCharacters[idx % defaultCharacters.Count];
                if (ch == null && reg != null && reg.characters.Count > 0) ch = reg.characters[idx % reg.characters.Count];
                if (ch == null) { Debug.LogError("[2CT] No character available. Run '2CT ▸ Generate Sample Content'."); return; }
                roster.Add((kv.Key, pc, ch));
                idx++;
            }
            if (roster.Count == 0) { Debug.LogWarning("[2CT] No player combatants to start combat with."); return; }
            Debug.Log($"[2CT] Starting encounter: boss='{bossData.bossName}', players={roster.Count}.");
            ServerStartCombat(bossData, roster);
        }

        public bool CombatRunning => _combatRunning;

        /// <summary>A player's deck for this fight: the shared starter deck plus every reward card
        /// earned so far this run. Falls back to the character's own deck if the registry starter
        /// deck hasn't been built yet.</summary>
        private static List<CardData> ComposeDeck(ContentRegistry reg, ulong clientId, CharacterData character)
        {
            var deck = new List<CardData>();
            if (reg != null && reg.starterDeck != null && reg.starterDeck.Count > 0)
                deck.AddRange(reg.starterDeck);
            else if (character != null && character.startingDeck != null)
                deck.AddRange(character.startingDeck);   // pre-registry fallback

            if (reg != null && SessionData.AcquiredCardIds.TryGetValue(clientId, out var ids))
                foreach (var id in ids) { var c = reg.GetCard(id); if (c != null) deck.Add(c); }
            return deck;
        }

        private IEnumerator RunCombat()
        {
            // ---- Intro: boss speaks, then attacks -----------------------------
            SetPhase(CombatPhase.Intro);
            var intro = boss.IntroLines;
            if (intro != null)
                foreach (var line in intro)
                {
                    boss.SayClientRpc(line.text, line.autoAdvanceSeconds);
                    yield return new WaitForSeconds(Mathf.Max(0.1f, line.autoAdvanceSeconds));
                }

            // ---- Alternating rounds: attack first, then defend ---------------
            int defendRound = 0;
            while (true)
            {
                yield return RunAttackPhase();
                if (!boss.IsAlive) { yield return WinSequence(); yield break; }

                defendRound++;
                CurrentDefendRound = defendRound;
                yield return RunDefendPhase(defendRound);
                if (AllPlayersDown()) { Finish(false); yield break; }
            }
        }

        private IEnumerator RunAttackPhase()
        {
            RoundNumber.Value++;
            SetPhase(CombatPhase.Attack);
            // Beat before drawing so the hand visibly deals in (also spaces rounds apart).
            if (preDrawPause > 0f) yield return new WaitForSeconds(preDrawPause);
            foreach (var p in _players)
            {
                p.ServerBeginAttackTurn();
                SendHandTo(p);
            }
            // Wait until every living, acting player has ended their turn — OR the boss drops to 0 HP
            // mid-turn (a lethal card), in which case we stop the attack phase at once.
            yield return new WaitUntil(() =>
            {
                if (!boss.IsAlive) return true;
                foreach (var p in _players)
                    if (!p.HasEndedTurn.Value) return false;
                return true;
            });

            // Boss killed during the turn: force everyone's turn to end and bail straight to the
            // win/death sequence (RunCombat sees !boss.IsAlive next). Skip end-of-round upkeep + Fire.
            if (!boss.IsAlive)
            {
                foreach (var p in _players) p.HasEndedTurn.Value = true;
                yield break;
            }

            foreach (var p in _players) p.ServerEndAttackTurn();
            boss.ServerTickFire();   // Fire deals damage at end of the attack round
        }

        private IEnumerator RunDefendPhase(int defendRound)
        {
            SetPhase(CombatPhase.Defend);
            foreach (var p in _players) p.ServerBeginDefend(defendRound);
            OpenArenaClientRpc();                                  // box expands + dodge icons appear
            // Telegraph beat: the box is open but bullets hold for a moment so players can brace.
            if (preAttackPause > 0f) yield return new WaitForSeconds(preAttackPause);

            var pattern = boss.ServerNextPattern();
            float duration = 2f;
            if (pattern != null)
            {
                int seed = Random.Range(int.MinValue, int.MaxValue);
                duration = pattern.duration;
                BeginDefendClientRpc(pattern.name, seed, duration, defendRound);
            }
            yield return new WaitForSeconds(duration + 0.25f);

            EndDefendClientRpc();
            foreach (var p in _players) p.ServerEndDefend();
        }

        private void Finish(bool victory)
        {
            _combatRunning = false;
            SetPhase(victory ? CombatPhase.Victory : CombatPhase.Defeat);
        }

        /// <summary>
        /// Boss defeated: it speaks its last words, fades out, the VICTORY screen holds, then we
        /// fade to black and load the level the fight was started from — players resume right where
        /// they triggered combat (their pre-combat free-roam position is preserved on the persistent
        /// player object). Server-driven; clients follow the replicated phase + RPCs.
        /// </summary>
        private IEnumerator WinSequence()
        {
            _combatRunning = false;

            // Death beat: hide the hand and block further play (Intro shows no banner), but let the
            // boss keep talking through the dialogue box.
            SetPhase(CombatPhase.Intro);

            var lastWords = boss.DefeatLines;
            if (lastWords != null)
                foreach (var line in lastWords)
                {
                    boss.SayClientRpc(line.text, line.autoAdvanceSeconds);
                    yield return new WaitForSeconds(Mathf.Max(0.1f, line.autoAdvanceSeconds));
                }

            // Boss dissolves away.
            boss.FadeOutClientRpc(bossFadeDuration);
            yield return new WaitForSeconds(bossFadeDuration + 0.1f);

            // Victory screen.
            SetPhase(CombatPhase.Victory);
            yield return new WaitForSeconds(Mathf.Max(0.25f, victoryBannerSeconds));

            // Rewards: each player permanently adds cards to their deck (pick 1 of 3, several times).
            yield return RewardSequence();

            // Record this encounter as cleared (server + every client) so the origin scene applies
            // its post-victory world state (disable/enable entities) on load. Must go out BEFORE the
            // scene load so clients have the flag when their scene objects start.
            if (!string.IsNullOrEmpty(SessionData.PendingEncounterId))
            {
                SessionData.MarkEncounterComplete(SessionData.PendingEncounterId);
                MarkEncounterCompleteClientRpc(SessionData.PendingEncounterId);
                SessionData.PendingEncounterId = null;
            }

            // Fade to black, flag the return, then load the level we came from.
            ReturningFromCombatClientRpc();
            FadeToBlackClientRpc(returnFadeDuration);
            yield return new WaitForSeconds(returnFadeDuration + 0.1f);

            string scene = !string.IsNullOrEmpty(SessionData.ReturnScene)
                ? SessionData.ReturnScene : SessionData.FirstLevelScene;
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        [ClientRpc] private void FadeToBlackClientRpc(float duration) => ScreenFader.FadeOut(duration);

        // Runs on every client (and host): the returning free-roam player will resume its
        // pre-combat position instead of snapping to the level spawn point.
        [ClientRpc] private void ReturningFromCombatClientRpc() => SessionData.ReturningFromCombat = true;

        // Mirror the cleared-encounter flag onto every client so their copy of the origin scene
        // applies the same post-victory world state (SessionData is static per-process, not synced).
        [ClientRpc] private void MarkEncounterCompleteClientRpc(string encounterId) => SessionData.MarkEncounterComplete(encounterId);

        // =====================================================================
        //  Post-victory rewards (server-driven, per-player, simultaneous)
        // =====================================================================
        private readonly Dictionary<ulong, string[]> _rewardOptions = new Dictionary<ulong, string[]>();
        private readonly Dictionary<ulong, int> _rewardPicksMade = new Dictionary<ulong, int>();
        private System.Random _rewardRng;

        /// <summary>Each player independently picks <see cref="rewardPicks"/> cards, 1-of-<see cref="rewardChoices"/>
        /// each time, rolled per player. Finishes when everyone is done (or the safety timeout hits).</summary>
        private IEnumerator RewardSequence()
        {
            var reg = ContentRegistry.Instance;
            if (reg == null || reg.cards == null || reg.cards.Count == 0 || rewardPicks <= 0) yield break;

            SetPhase(CombatPhase.Reward);
            _rewardRng = new System.Random(unchecked(RoundNumber.Value * 31 + _players.Count + 1));
            _rewardOptions.Clear();
            _rewardPicksMade.Clear();

            foreach (var kv in _byClient)   // everyone starts picking at once
            {
                _rewardPicksMade[kv.Key] = 0;
                OfferNextReward(kv.Key);
            }

            float deadline = Time.time + Mathf.Max(5f, rewardTimeout);
            yield return new WaitUntil(() => AllRewardsDone() || Time.time > deadline);
        }

        private bool AllRewardsDone()
        {
            foreach (var kv in _byClient)
                if (!_rewardPicksMade.TryGetValue(kv.Key, out var n) || n < rewardPicks) return false;
            return true;
        }

        /// <summary>Offer the next set of choices to one player, or tell them they're finished.</summary>
        private void OfferNextReward(ulong clientId)
        {
            if (!_byClient.TryGetValue(clientId, out var pc)) return;
            int made = _rewardPicksMade.TryGetValue(clientId, out var m) ? m : 0;
            if (made >= rewardPicks)
            {
                _rewardOptions.Remove(clientId);
                RewardsCompleteClientRpc(ToOwner(pc));
                return;
            }
            var options = RollRewardOptions();
            _rewardOptions[clientId] = options;
            OfferRewardsClientRpc(made, rewardPicks, string.Join("|", options), ToOwner(pc));
        }

        /// <summary>Distinct random sample of card ids from the whole pool (any category, for now).</summary>
        private string[] RollRewardOptions()
        {
            var pool = ContentRegistry.Instance.cards;
            var ids = new List<string>(pool.Count);
            foreach (var c in pool) if (c != null) ids.Add(c.Id);
            int n = Mathf.Min(rewardChoices, ids.Count);
            for (int i = 0; i < n; i++)          // partial Fisher–Yates: first n are the sample
            {
                int j = i + _rewardRng.Next(ids.Count - i);
                (ids[i], ids[j]) = (ids[j], ids[i]);
            }
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = ids[i];
            return result;
        }

        [ServerRpc(RequireOwnership = false)]
        public void SubmitRewardChoiceServerRpc(string cardId, ServerRpcParams rpc = default)
        {
            if (Phase.Value != CombatPhase.Reward) return;
            ulong clientId = rpc.Receive.SenderClientId;
            if (!_rewardOptions.TryGetValue(clientId, out var opts)) return;   // not currently choosing
            bool valid = false;
            foreach (var o in opts) if (o == cardId) { valid = true; break; }
            if (!valid) return;                                               // must be one of the offered cards

            SessionData.AddAcquiredCard(clientId, cardId);                    // persists into future fights
            _rewardOptions.Remove(clientId);
            _rewardPicksMade[clientId] = (_rewardPicksMade.TryGetValue(clientId, out var m) ? m : 0) + 1;
            OfferNextReward(clientId);
        }

        [ClientRpc]
        private void OfferRewardsClientRpc(int pickIndex, int totalPicks, string joinedIds, ClientRpcParams _ = default)
        {
            var ids = string.IsNullOrEmpty(joinedIds) ? System.Array.Empty<string>() : joinedIds.Split('|');
            CombatEvents.RaiseRewardOffered(pickIndex, totalPicks, ids);
        }

        [ClientRpc]
        private void RewardsCompleteClientRpc(ClientRpcParams _ = default) => CombatEvents.RaiseRewardsComplete();

        private bool AllPlayersDown()
        {
            foreach (var p in _players) if (p.IsAlive) return false;
            return true;
        }

        private void SetPhase(CombatPhase p) => Phase.Value = p;

        // =====================================================================
        //  Card play (owner client -> server)
        // =====================================================================
        [ServerRpc(RequireOwnership = false)]
        public void PlayCardServerRpc(int handIndex, int targetSlot, ServerRpcParams rpc = default)
        {
            if (Phase.Value != CombatPhase.Attack) return;
            if (!_byClient.TryGetValue(rpc.Receive.SenderClientId, out var caster)) return;
            if (!caster.IsAlive || !caster.CanAct.Value || caster.HasEndedTurn.Value) return;

            var card = caster.Deck.PeekHand(handIndex);
            if (card == null) return;
            int cost = caster.EffectiveCost(card);   // Flawless / Bubble Power discounts
            if (caster.ManaValue < cost) { ToastClientRpc("Not enough mana", ToOwner(caster)); return; }

            caster.ServerTrySpendMana(cost);
            caster.Deck.PlayCardAt(handIndex);

            _ctxCaster = caster;
            _ctxTarget = ResolveTarget(caster, card.targetType, targetSlot);
            card.Resolve(this);
            _ctxCaster = _ctxTarget = null;

            // Track the last card played this turn for Copy — but a Copy card is transparent (it
            // duplicates whatever you last played, without itself becoming the thing to copy).
            if (!card.IsCopyCard) caster.SetLastPlayedCard(card.Id);

            PlayCardVfxClientRpc(caster.Slot.Value, card.vfxKey);
            SendHandTo(caster);
        }

        [ServerRpc(RequireOwnership = false)]
        public void EndTurnServerRpc(ServerRpcParams rpc = default)
        {
            if (Phase.Value != CombatPhase.Attack) return;
            if (_byClient.TryGetValue(rpc.Receive.SenderClientId, out var caster))
                caster.HasEndedTurn.Value = true;
        }

        private PlayerCombatant ResolveTarget(PlayerCombatant caster, TargetType type, int targetSlot)
        {
            switch (type)
            {
                case TargetType.Self: return caster;
                case TargetType.Ally:
                case TargetType.AllyOrSelf:
                case TargetType.DeadAlly:
                    foreach (var p in _players) if (p.Slot.Value == targetSlot) return p;
                    return caster;
                default: return null;
            }
        }

        // =====================================================================
        //  ICombatContext (server-side shared rules)
        // =====================================================================
        void ICombatContext.DealDamageToEnemy(int amount, bool isSpell)
        {
            if (isSpell && _ctxCaster != null) amount += _ctxCaster.SpellDamageBonus;
            boss.ServerTakeDamage(amount);
            if (isSpell && _ctxCaster != null && _ctxCaster.ConsumeLifestealIfArmed())
                _ctxCaster.Heal(amount);
        }

        void ICombatContext.DrawCardsNow(ICombatant who, int amount)
        {
            var pc = who as PlayerCombatant; if (pc == null) return;
            int drawn = pc.Deck.DrawCards(amount);
            if (drawn < amount) ToastClientRpc("Deck is empty", ToOwner(pc));
            SendHandTo(pc);
        }

        int ICombatContext.DiscardHand(ICombatant who)
        {
            var pc = who as PlayerCombatant;
            return pc != null ? pc.Deck.DiscardHand() : 0;
        }

        int ICombatContext.DiscardHandTriggering(ICombatant who)
        {
            var pc = who as PlayerCombatant; if (pc == null) return 0;
            var removed = pc.Deck.RemoveWholeHand();
            ResolveDiscardTriggers(pc, removed);
            SendHandTo(pc);
            return removed.Count;
        }

        int ICombatContext.DiscardCardsTriggering(ICombatant who, int count)
        {
            var pc = who as PlayerCombatant; if (pc == null) return 0;
            var removed = pc.Deck.RemoveLastHandCards(count);
            ResolveDiscardTriggers(pc, removed);
            SendHandTo(pc);
            return removed.Count;
        }

        // Fire the activate-on-discard effect of any Severance card that was just force-discarded.
        // The Deck already routed the cards to the used pile / dropped copies; we only run effects.
        private void ResolveDiscardTriggers(PlayerCombatant caster, System.Collections.Generic.List<CardData> discarded)
        {
            if (discarded == null) return;
            var savedTarget = _ctxTarget;
            foreach (var c in discarded)
            {
                if (c == null || !c.activateWhenDiscarded) continue;
                _ctxTarget = caster;         // self-target for safety
                c.Resolve(this);
            }
            _ctxTarget = savedTarget;
        }

        void ICombatContext.CopyLastPlayedIntoHand(ICombatant who)
        {
            var pc = who as PlayerCombatant; if (pc == null) return;
            var reg = ContentRegistry.Instance;
            var last = reg != null ? reg.GetCard(pc.LastPlayedCardId) : null;
            if (last == null) { ToastClientRpc("Nothing to copy yet", ToOwner(pc)); return; }
            pc.Deck.AddCardToHand(last, true);   // tinted copy
            SendHandTo(pc);
        }

        void ICombatContext.CastRandomSpellFromDeck(ICombatant who)
        {
            var pc = who as PlayerCombatant; if (pc == null) return;
            var card = pc.Deck.DrawRandomFromPile();
            if (card == null) { ToastClientRpc("Deck is empty", ToOwner(pc)); return; }
            RevealCardClientRpc(pc.Slot.Value, card.Id, ToOwner(pc));   // show the caster which card was pulled
            var savedTarget = _ctxTarget;
            _ctxTarget = pc; // best-effort self target for the random spell
            card.Resolve(this);
            _ctxTarget = savedTarget;
            PlayCardVfxClientRpc(pc.Slot.Value, card.vfxKey);
        }

        // =====================================================================
        //  Server -> client messaging
        // =====================================================================
        private void SendHandTo(PlayerCombatant p)
        {
            var hand = p.Deck.Hand;
            var copy = p.Deck.HandIsCopy;
            var ids = new string[hand.Count];
            for (int i = 0; i < hand.Count; i++)
            {
                string id = hand[i] != null ? hand[i].Id : "";
                // A trailing '*' marks a Copy-created duplicate so the client tints it (ids never contain '*').
                ids[i] = (i < copy.Count && copy[i]) ? id + "*" : id;
            }
            // NGO RPCs can't serialize string[]; send one delimited string and split on the client.
            UpdateHandClientRpc(string.Join("|", ids), ToOwner(p));
        }

        private ClientRpcParams ToOwner(PlayerCombatant p) => new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { p.OwnerClientId } }
        };

        [ClientRpc]
        private void UpdateHandClientRpc(string joinedIds, ClientRpcParams _ = default)
        {
            var ids = string.IsNullOrEmpty(joinedIds) ? System.Array.Empty<string>() : joinedIds.Split('|');
            CombatEvents.RaiseHandUpdated(ids);
        }

        [ClientRpc]
        private void ToastClientRpc(string msg, ClientRpcParams _ = default) => CombatEvents.RaiseToast(msg);

        [ClientRpc]
        private void RevealCardClientRpc(int casterSlot, string cardId, ClientRpcParams _ = default)
            => CombatEvents.RaiseCardRevealed(casterSlot, cardId);

        [ClientRpc]
        private void PlayCardVfxClientRpc(int casterSlot, string vfxKey)
        {
            var caster = PlayerRegistry.BySlot(casterSlot);
            Vector3 pos = caster != null ? caster.transform.position : Vector3.zero;
            CombatEvents.RaiseCardVfx(casterSlot, vfxKey, pos);
        }

        [ClientRpc]
        private void BeginDefendClientRpc(string patternName, int seed, float duration, int defendRound)
        {
            CombatEvents.RaiseDefendStarted(defendRound, duration);
            var pattern = ContentRegistry.Instance != null ? ContentRegistry.Instance.GetPattern(patternName) : null;
            if (pattern != null && bulletSystem != null)
                bulletSystem.BeginPattern(pattern, seed, BossMuzzleWorld, ArenaCenterWorld);
        }

        [ClientRpc]
        private void OpenArenaClientRpc()
        {
            if (bulletSystem != null) bulletSystem.OpenArena();
        }

        [ClientRpc]
        private void EndDefendClientRpc()
        {
            if (bulletSystem != null) bulletSystem.StopAndClear();
        }

        // Bullet existence sync: a client whose dodge icon was hit reports the bullet's schedule id;
        // the server fans it out so every client removes (and, if it explodes, bursts) that same
        // bullet. Positions/timing aren't synced — they're already deterministic from the shared seed.
        [ServerRpc(RequireOwnership = false)]
        public void NotifyBulletHitServerRpc(int epoch, int bulletId) => BulletHitClientRpc(epoch, bulletId);

        [ClientRpc]
        private void BulletHitClientRpc(int epoch, int bulletId)
        {
            if (bulletSystem != null) bulletSystem.ForceDestroy(epoch, bulletId);
        }

        public Vector2 BossMuzzleWorld
        {
            get
            {
                float x = bossMuzzle != null ? bossMuzzle.position.x : 5f;
                // Emit from the arena's vertical centre so the bullet stream is aligned with the
                // box, independent of where the boss sprite/muzzle happens to sit vertically.
                float y = arena != null ? arena.Center.y : (bossMuzzle != null ? bossMuzzle.position.y : 0f);
                return new Vector2(x, y);
            }
        }
        public Vector2 ArenaCenterWorld => arena != null ? (Vector2)arena.Center : Vector2.zero;
    }
}

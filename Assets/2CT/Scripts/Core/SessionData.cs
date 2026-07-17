using System.Collections.Generic;

namespace TwoCT.Core
{
    /// <summary>
    /// Plain-static run/session state that must survive scene loads (lobby → level → combat).
    /// The persistent NetworkManager keeps the connection alive across scenes; this keeps the
    /// lightweight choices (who picked which character, whether a run is underway) alongside it.
    /// Server writes it at lobby "Start"; combat reads it to seat the right characters.
    /// </summary>
    public static class SessionData
    {
        /// <summary>True once a run has started from the lobby (drives combat auto-start on scene load).</summary>
        public static bool InRun;

        /// <summary>clientId → index into ContentRegistry.characters chosen in the lobby.</summary>
        public static readonly Dictionary<ulong, int> CharacterByClient = new();

        /// <summary>clientId → card ids permanently added to that player's deck this run (boss rewards).
        /// Combat rebuilds each deck as the shared starter deck + this list, so picks stick between fights.</summary>
        public static readonly Dictionary<ulong, List<string>> AcquiredCardIds = new();

        public static void AddAcquiredCard(ulong clientId, string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return;
            if (!AcquiredCardIds.TryGetValue(clientId, out var list))
                AcquiredCardIds[clientId] = list = new List<string>();
            list.Add(cardId);
        }

        public static string FirstLevelScene = "Level";

        /// <summary>The free-roam level combat was launched from (server-side). After a win, combat
        /// loads this scene again so players resume where they triggered the fight.</summary>
        public static string ReturnScene;

        /// <summary>Which boss the (universal) combat scene should load — set by the combat trigger
        /// (its <c>bossToFight</c> asset name) just before loading combat. Combat resolves it via
        /// <see cref="ContentRegistry.GetBoss"/>; null falls back to the scene's test boss.</summary>
        public static string SelectedBossId;

        /// <summary>The encounter currently being fought (the trigger's encounter id). Marked
        /// complete in <see cref="CompletedEncounters"/> when the boss is beaten, so the origin
        /// scene can permanently enable/disable entities based on it.</summary>
        public static string PendingEncounterId;

        /// <summary>Encounter ids the party has cleared this run. Server-authoritative; synced to
        /// clients (see CombatManager) so every client applies the same post-victory world state.</summary>
        public static readonly HashSet<string> CompletedEncounters = new();

        public static bool IsEncounterComplete(string id) =>
            !string.IsNullOrEmpty(id) && CompletedEncounters.Contains(id);

        public static void MarkEncounterComplete(string id)
        {
            if (!string.IsNullOrEmpty(id)) CompletedEncounters.Add(id);
        }

        /// <summary>Set on every client just before the post-victory scene load. Tells the returning
        /// free-roam player to resume its pre-combat position instead of snapping to the spawn point.</summary>
        public static bool ReturningFromCombat;

        public static void Reset()
        {
            InRun = false;
            ReturnScene = null;
            ReturningFromCombat = false;
            SelectedBossId = null;
            PendingEncounterId = null;
            CharacterByClient.Clear();
            AcquiredCardIds.Clear();
            CompletedEncounters.Clear();
        }
    }
}

using System;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.Combat
{
    /// <summary>
    /// Process-local event bus that decouples authoritative combat logic from presentation
    /// (HUD, VFX, dialogue box). The server raises the networked calls; each client raises
    /// these locally in response so UI never reaches into combat internals.
    /// </summary>
    public static class CombatEvents
    {
        public static event Action<CombatPhase> PhaseChanged;
        public static event Action<int, int, Vector3> DamageNumber;   // slot, amount, worldPos
        public static event Action<int, string, Vector3> CardVfx;     // casterSlot, vfxKey, worldPos
        public static event Action<int, int> BossHealthChanged;       // current, max
        public static event Action<string, float> BossSay;            // text, seconds (0 = manual)
        public static event Action<string> Toast;                     // transient message ("Deck is empty")
        public static event Action<string[]> HandUpdated;              // local player's hand card ids
        public static event Action<int, float> DefendStarted;          // defendRound, duration (client sim cue)
        public static event Action<int, string> CardRevealed;          // casterSlot, cardId (random-cast reveal)
        public static event Action<int, int, string[]> RewardOffered;  // pickIndex, totalPicks, offered card ids
        public static event Action RewardsComplete;                    // local player finished all reward picks

        public static void RaisePhaseChanged(CombatPhase p) => PhaseChanged?.Invoke(p);
        public static void RaiseDamageNumber(int slot, int amount, Vector3 pos) => DamageNumber?.Invoke(slot, amount, pos);
        public static void RaiseCardVfx(int slot, string key, Vector3 pos) => CardVfx?.Invoke(slot, key, pos);
        public static void RaiseBossHealth(int cur, int max) => BossHealthChanged?.Invoke(cur, max);
        public static void RaiseBossSay(string text, float seconds) => BossSay?.Invoke(text, seconds);
        public static void RaiseToast(string msg) => Toast?.Invoke(msg);
        public static void RaiseHandUpdated(string[] ids) => HandUpdated?.Invoke(ids);
        public static void RaiseDefendStarted(int round, float duration) => DefendStarted?.Invoke(round, duration);
        public static void RaiseCardRevealed(int slot, string id) => CardRevealed?.Invoke(slot, id);
        public static void RaiseRewardOffered(int i, int total, string[] ids) => RewardOffered?.Invoke(i, total, ids);
        public static void RaiseRewardsComplete() => RewardsComplete?.Invoke();
    }
}

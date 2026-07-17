using System.Collections.Generic;

namespace TwoCT.Combat
{
    /// <summary>
    /// Process-local list of every spawned PlayerCombatant, maintained on all peers so UI and
    /// the bullet system can find players by seat without server lookups.
    /// </summary>
    public static class PlayerRegistry
    {
        private static readonly List<PlayerCombatant> _all = new List<PlayerCombatant>();
        public static IReadOnlyList<PlayerCombatant> All => _all;

        public static void Register(PlayerCombatant p) { if (!_all.Contains(p)) _all.Add(p); }
        public static void Unregister(PlayerCombatant p) => _all.Remove(p);

        public static PlayerCombatant BySlot(int slot)
        {
            foreach (var p in _all) if (p.Slot.Value == slot) return p;
            return null;
        }

        /// <summary>The PlayerCombatant this client owns (its local player), or null.</summary>
        public static PlayerCombatant Local
        {
            get { foreach (var p in _all) if (p.IsOwner) return p; return null; }
        }
    }
}

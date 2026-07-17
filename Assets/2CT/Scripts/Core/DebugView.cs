namespace TwoCT.Core
{
    /// <summary>
    /// Process-local debug-view toggles, flipped from the dev panel. Shared across combat and
    /// free-roam so a single "Show hitboxes" switch drives every collision overlay (dodge icons,
    /// the free-roam player, and walls) regardless of which system owns them.
    /// </summary>
    public static class DebugView
    {
        public static bool ShowHitboxes;
    }
}

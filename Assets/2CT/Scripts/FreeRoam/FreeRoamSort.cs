using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Shared top-down depth sorting for free roam. World sprites live on a big POSITIVE sorting
    /// band, so they always draw in front of the negative-order background/parallax/ground layers.
    /// Within that band, a sprite's <b>Z</b> picks a depth step (lower Z = in front, higher Z =
    /// further back) and its bottom <b>Y</b> sorts within a step (lower on screen = in front).
    ///
    /// KEY: a positive Z can NEVER push a world sprite behind the backgrounds — the result is
    /// clamped to stay positive. Backgrounds (sortingOrder &lt; 0) are always behind; order those
    /// among themselves with their Order-in-Layer, not Z.
    /// </summary>
    public static class FreeRoamSort
    {
        /// <summary>Centre of the world band (Z = 0). High so several Z steps fit below it while
        /// staying positive (above the negative background layers).</summary>
        public const int BaseOrder = 20000;
        /// <summary>Sorting granularity within a Z step: orders per world unit of Y.</summary>
        public const float OrdersPerUnit = 100f;
        /// <summary>Orders reserved per whole-number Z step. Must exceed the Y spread within a step so
        /// Z always dominates. 2000 ≈ ±10 world units of Y range per step.</summary>
        public const int LayerBand = 2000;
        /// <summary>Floor so even a high Z / high object never dips into the background band (&lt; 0).</summary>
        public const int MinOrder = 1;

        // SpriteRenderer.sortingOrder is a signed 16-bit value; keep results inside it.
        private const int MaxOrder = 32767;

        /// <summary>Order for dev overlays (hitboxes) — on top of the whole world band.</summary>
        public const int OverlayOrder = MaxOrder;

        /// <summary>Sorting order for an object whose bottom is at <paramref name="worldBottomY"/> at
        /// Z = 0. Lower Y → higher order → drawn in front.</summary>
        public static int OrderForBottomY(float worldBottomY) => OrderFor(worldBottomY, 0f);

        /// <summary>
        /// Sorting order from the object's world <paramref name="z"/> and its bottom Y. A HIGHER
        /// (rounded) Z draws behind everything at a lower Z; within a Z step, lower Y draws in front.
        /// Always stays in the positive world band, so a world sprite can't fall behind the
        /// (negative-order) backgrounds no matter how large its Z.
        /// </summary>
        public static int OrderFor(float worldBottomY, float z)
        {
            int step = Mathf.RoundToInt(z);   // whole-number Z = depth step; +Z = further back
            // Y within a step: lower on screen (smaller Y) → higher order → in front. Clamped to half
            // a band so it can never cross into an adjacent Z step.
            int y = Mathf.Clamp(Mathf.RoundToInt(-worldBottomY * OrdersPerUnit),
                                -(LayerBand / 2 - 1), LayerBand / 2 - 1);
            long order = (long)BaseOrder - (long)step * LayerBand + y;
            return (int)Mathf.Clamp(order, MinOrder, MaxOrder);
        }
    }
}

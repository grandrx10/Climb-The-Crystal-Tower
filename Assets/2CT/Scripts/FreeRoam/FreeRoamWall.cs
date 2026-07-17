using System.Collections.Generic;
using TwoCT.Bullets;
using TwoCT.Core;
using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// A solid rectangular obstacle in free roam. The collision box (<see cref="size"/>, centred on
    /// this transform) is authored here, independent of any sprite — so art and collision can differ.
    /// Add this component to any GameObject and set Size. A Scene-view gizmo always shows the box,
    /// and the dev "Show hitboxes" toggle overlays it at runtime. FreeRoamPlayer blocks against these.
    /// </summary>
    public class FreeRoamWall : MonoBehaviour
    {
        [Tooltip("Collision box size in world units, centred on this transform. Keep the transform unrotated.")]
        public Vector2 size = new Vector2(2f, 1f);

        /// <summary>All enabled walls, queried by FreeRoamPlayer for collision.</summary>
        public static readonly List<FreeRoamWall> All = new List<FreeRoamWall>();

        public Rect Bounds => new Rect((Vector2)transform.position - size * 0.5f, size);

        private GameObject _overlay;

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            if (_overlay != null) Destroy(_overlay);
        }

        private void Update()
        {
            if (!DebugView.ShowHitboxes)
            {
                if (_overlay != null && _overlay.activeSelf) _overlay.SetActive(false);
                return;
            }
            if (_overlay == null)
            {
                _overlay = new GameObject(name + "_HitboxOverlay");   // world-space, so parent scale is irrelevant
                var sr = _overlay.AddComponent<SpriteRenderer>();
                sr.sprite = PlayerDodgeIcon.MakeSquareSprite();
                sr.color = new Color(1f, 0.3f, 0.2f, 0.35f);
                sr.sortingOrder = FreeRoamSort.OverlayOrder;   // dev overlay: on top of the world band
            }
            if (!_overlay.activeSelf) _overlay.SetActive(true);
            _overlay.transform.position = transform.position;
            _overlay.transform.rotation = Quaternion.identity;
            _overlay.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(transform.position, size);
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.12f);
            Gizmos.DrawCube(transform.position, size);
        }
    }
}

using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Follows the local free-roam player, clamped so the orthographic view never leaves the
    /// level bounds. Does nothing when there's no local player / no free-roam context (combat,
    /// lobby), leaving the camera fixed.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private float smooth = 8f;
        [SerializeField] private float zDepth = -10f;

        private Camera _cam;

        private void Awake() => _cam = GetComponent<Camera>();

        private void LateUpdate()
        {
            var player = FreeRoamPlayer.Local;
            var ctx = FreeRoamContext.Current;
            if (player == null || ctx == null) return;

            Vector3 target = player.transform.position;
            target.z = zDepth;

            if (_cam != null && _cam.orthographic)
            {
                var b = ctx.Bounds;
                float halfH = _cam.orthographicSize;
                float halfW = halfH * _cam.aspect;
                // Only clamp on axes where the level is larger than the view.
                if (b.width > halfW * 2f) target.x = Mathf.Clamp(target.x, b.xMin + halfW, b.xMax - halfW);
                else target.x = b.center.x;
                if (b.height > halfH * 2f) target.y = Mathf.Clamp(target.y, b.yMin + halfH, b.yMax - halfH);
                else target.y = b.center.y;
            }

            transform.position = Vector3.Lerp(transform.position, target, smooth * Time.deltaTime);
        }
    }
}

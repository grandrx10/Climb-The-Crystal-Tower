using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Simple parallax: shifts a background layer a fraction of the camera's movement. Attach to
    /// each background layer and set <see cref="factor"/> (0 = locked to camera / infinitely far,
    /// 1 = moves with the world / foreground).
    /// </summary>
    [ExecuteAlways]
    public class ParallaxLayer : MonoBehaviour
    {
        [Range(0f, 1f)] public float factor = 0.5f;
        [Tooltip("Keep the layer vertically locked (common for side-scroller skies).")]
        public bool lockY = false;

        private Camera _cam;
        private Vector3 _startPos;
        private Vector3 _camStart;

        private void OnEnable()
        {
            _cam = Camera.main;
            _startPos = transform.position;
            if (_cam != null) _camStart = _cam.transform.position;
        }

        private void LateUpdate()
        {
            // In the editor (not playing) don't fight the user: keep re-basing the "home" position to
            // wherever they place the layer, so you can freely move it — including its Z — and that
            // position is captured for play. Parallax only actually offsets the layer during play.
            // (This is why Z appeared un-editable before: LateUpdate snapped position back every frame.)
            if (!Application.isPlaying)
            {
                _startPos = transform.position;
                if (_cam != null) _camStart = _cam.transform.position;
                return;
            }

            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; _camStart = _cam.transform.position; }
            Vector3 camDelta = _cam.transform.position - _camStart;
            var p = _startPos + camDelta * factor;
            if (lockY) p.y = _startPos.y;
            p.z = _startPos.z;   // keep the authored Z (captured above); parallax only shifts X/Y
            transform.position = p;
        }
    }
}

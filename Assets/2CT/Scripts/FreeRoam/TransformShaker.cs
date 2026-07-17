using UnityEngine;

namespace TwoCT.FreeRoam
{
    /// <summary>
    /// Shakes a transform's <c>localPosition</c> with a decaying random jitter for a fixed duration,
    /// then snaps it back to where it started. Drop-in reusable juice: call <see cref="Shake"/> to
    /// (re)start. Added on demand by <see cref="TwoCT.Data.ShakeAction"/> (dialogue action) so
    /// designers can shake any scene object mid-conversation without wiring a component up-front.
    ///
    /// Mirrors the boss damage-shake (see BossController.Shake): jitter magnitude eases to zero over
    /// the duration. Uses Update rather than a coroutine so it works when added at runtime, and
    /// disables itself while idle. NOTE: it drives <c>localPosition</c>, so it fights any other code
    /// that also writes localPosition every frame (e.g. FreeRoamPlayer) — intended for static-ish
    /// scene props (statues, NPCs, doors), matching how dialogue actions are used.
    /// </summary>
    [DisallowMultipleComponent]
    public class TransformShaker : MonoBehaviour
    {
        private Vector3 _rest;
        private bool _shaking;
        private float _t;
        private float _duration;
        private float _magnitude;

        /// <summary>(Re)start a shake. Capturing the rest position only when idle means a shake
        /// retriggered mid-shake still returns to the true resting spot, not a jittered one.</summary>
        public void Shake(float duration, float magnitude)
        {
            if (!_shaking) _rest = transform.localPosition;
            _shaking = true;
            _t = 0f;
            _duration = Mathf.Max(0.0001f, duration);
            _magnitude = magnitude;
            enabled = true;
        }

        private void Update()
        {
            if (!_shaking) return;

            _t += Time.deltaTime;
            if (_t >= _duration)
            {
                transform.localPosition = _rest;
                _shaking = false;
                enabled = false;             // sleep until the next Shake()
                return;
            }

            float damper = 1f - Mathf.Clamp01(_t / _duration);   // jitter decays to nothing
            Vector2 off = Random.insideUnitCircle * (_magnitude * damper);
            transform.localPosition = _rest + (Vector3)off;
        }

        private void OnDisable()
        {
            // If disabled externally mid-shake, don't leave the object stuck off-centre.
            if (_shaking)
            {
                transform.localPosition = _rest;
                _shaking = false;
            }
        }
    }
}

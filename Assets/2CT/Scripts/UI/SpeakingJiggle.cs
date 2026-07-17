using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.UI
{
    /// <summary>
    /// Subtle squash-and-stretch "talking" wobble for a character sprite. While speaking, the
    /// sprite jitters vertically with a counter-squash on X (so its volume reads as roughly
    /// preserved), driven by smooth Perlin noise so it looks like random mouth movement rather
    /// than a fixed pulse. Call <see cref="Talk"/> to keep it going for a short window; it eases
    /// back to its authored rest scale when talking stops.
    ///
    /// Two ways to drive it:
    ///  • Directly — hold a reference and call <c>Talk(seconds)</c> (combat boss does this).
    ///  • By name — set <see cref="speakerName"/> on the component and the dialogue box wobbles
    ///    it whenever a line's speaker matches (attach one to each NPC sprite).
    /// </summary>
    [DisallowMultipleComponent]
    public class SpeakingJiggle : MonoBehaviour
    {
        [Tooltip("Transform to squash/stretch. Defaults to this object's transform.")]
        [SerializeField] private Transform target;
        [Tooltip("Optional. If set, DialogueBox drives this jiggle when a line's speaker name matches.")]
        [SerializeField] private string speakerName;
        [Tooltip("Peak stretch as a fraction of the base scale (0.08 = up to ±8%).")]
        [SerializeField, Range(0f, 0.4f)] private float amount = 0.08f;
        [Tooltip("Mouth speed — higher wobbles faster.")]
        [SerializeField] private float speed = 13f;
        [Tooltip("Seconds to ease in and out of the talking wobble.")]
        [SerializeField] private float settle = 0.1f;

        private Vector3 _baseScale = Vector3.one;
        private float _talkUntil;
        private float _env;          // 0..1 eased "how much talking" for smooth start/stop
        private float _noiseSeed;

        private void Awake()
        {
            if (target == null) target = transform;
            _baseScale = target.localScale;
            _noiseSeed = (GetInstanceID() & 0xffff) * 0.013f;   // per-object phase so many talkers desync
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(speakerName)) Register(speakerName, this);
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(speakerName)) Unregister(speakerName, this);
            if (target != null) target.localScale = _baseScale;   // never leave it deformed
        }

        /// <summary>Keep the wobble alive for <paramref name="seconds"/> more. Safe to call every
        /// frame while a line is typing — the window just keeps refreshing.</summary>
        public void Talk(float seconds)
        {
            _talkUntil = Mathf.Max(_talkUntil, Time.unscaledTime + Mathf.Max(0.02f, seconds));
        }

        public void StopTalking() => _talkUntil = 0f;

        private void LateUpdate()
        {
            bool talking = Time.unscaledTime < _talkUntil;
            float step = settle > 0f ? Time.unscaledDeltaTime / settle : 1f;
            _env = Mathf.MoveTowards(_env, talking ? 1f : 0f, step);
            if (_env <= 0f) { target.localScale = _baseScale; return; }

            float n = Mathf.PerlinNoise(Time.unscaledTime * speed + _noiseSeed, _noiseSeed) * 2f - 1f; // smooth -1..1
            float stretch = n * amount * _env;

            var s = _baseScale;
            s.y = _baseScale.y * (1f + stretch);
            s.x = _baseScale.x * (1f - stretch * 0.5f);   // counter-squash keeps volume steady
            target.localScale = s;
        }

        // ---- name registry so DialogueBox can drive characters by their line's speaker ----
        private static readonly Dictionary<string, List<SpeakingJiggle>> _byName =
            new Dictionary<string, List<SpeakingJiggle>>();

        private static void Register(string n, SpeakingJiggle j)
        {
            if (!_byName.TryGetValue(n, out var l)) _byName[n] = l = new List<SpeakingJiggle>();
            if (!l.Contains(j)) l.Add(j);
        }

        private static void Unregister(string n, SpeakingJiggle j)
        {
            if (_byName.TryGetValue(n, out var l)) l.Remove(j);
        }

        /// <summary>Wobble every character registered under <paramref name="name"/> for a moment.</summary>
        public static void TalkByName(string name, float seconds)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_byName.TryGetValue(name, out var l))
                foreach (var j in l) if (j != null) j.Talk(seconds);
        }
    }
}

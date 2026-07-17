using System.Collections;
using System.Collections.Generic;
using TwoCT.Core;
using TwoCT.Data;
using Unity.Netcode;
using UnityEngine;

namespace TwoCT.Combat
{
    /// <summary>
    /// The boss. Server-authoritative HP and phase; drives its own attack-pattern rotation and
    /// Fire damage-over-time. Implements <see cref="IEnemy"/> for card effects.
    /// </summary>
    public class BossController : NetworkBehaviour, IEnemy
    {
        public readonly NetworkVariable<int> HP = new(100);
        public readonly NetworkVariable<int> MaxHP = new(100);
        public readonly NetworkVariable<int> PhaseIndex = new(0);
        public readonly NetworkVariable<int> BossIndex = new(-1);   // index into ContentRegistry.bosses (for sprite)

        [Header("Hit reaction")]
        [Tooltip("Duration of the shake when the boss takes damage.")]
        [SerializeField] private float shakeDuration = 0.22f;
        [Tooltip("Peak positional jitter (world units) at the start of the shake.")]
        [SerializeField] private float shakeMagnitude = 0.22f;

        private BossData _data;
        private int _rotationIndex;
        private BossPhase _currentPhase;
        private Vector3 _restPos;
        private Coroutine _shakeRoutine;

        private struct FireStack { public int stacks; public int turnsRemaining; }
        private readonly List<FireStack> _fire = new List<FireStack>();

        public bool IsAlive => HP.Value > 0;
        public BossData Data => _data;
        public int TotalFireStacks
        {
            get { int s = 0; foreach (var f in _fire) s += f.stacks; return s; }
        }

        public override void OnNetworkSpawn()
        {
            _restPos = transform.localPosition;            // shake returns here (authored scene position)
            HP.OnValueChanged += OnHpChanged;
            BossIndex.OnValueChanged += (_, __) => ApplyBossSprite();
            ApplyBossSprite();
        }

        public override void OnNetworkDespawn()
        {
            HP.OnValueChanged -= OnHpChanged;
        }

        private void OnHpChanged(int prev, int cur)
        {
            CombatEvents.RaiseBossHealth(cur, MaxHP.Value);
            if (cur < prev)                                 // took damage → shake (not the travel wobble)
            {
                if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
                _shakeRoutine = StartCoroutine(Shake());
            }
        }

        private IEnumerator Shake()
        {
            float t = 0f;
            while (t < shakeDuration)
            {
                t += Time.deltaTime;
                float damper = 1f - Mathf.Clamp01(t / shakeDuration);   // jitter decays to nothing
                Vector2 off = Random.insideUnitCircle * (shakeMagnitude * damper);
                transform.localPosition = _restPos + (Vector3)off;
                yield return null;
            }
            transform.localPosition = _restPos;
        }

        public void ServerInitialize(BossData data)
        {
            if (!IsServer) return;
            _data = data;
            MaxHP.Value = data.maxHP;
            HP.Value = data.maxHP;
            _rotationIndex = 0;
            _fire.Clear();
            var reg = ContentRegistry.Instance;
            BossIndex.Value = reg != null ? reg.bosses.IndexOf(data) : -1;
            RefreshPhase();
        }

        /// <summary>Apply the boss's authored sprite (if any) to this object's SpriteRenderer on all clients.</summary>
        private void ApplyBossSprite()
        {
            var reg = ContentRegistry.Instance;
            int i = BossIndex.Value;
            var data = reg != null && i >= 0 && i < reg.bosses.Count ? reg.bosses[i] : null;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && data != null)
            {
                if (data.sprite != null)
                {
                    sr.sprite = data.sprite;
                    sr.color = Color.white;   // show the art's own colours (the scene sets a red placeholder tint)
                }
                sr.flipX = data.flipX;
                sr.flipY = data.flipY;
            }
        }

        // =====================================================================
        //  IEnemy / damage
        // =====================================================================
        public void ServerTakeDamage(int amount)
        {
            if (!IsServer || !IsAlive) return;
            HP.Value = Mathf.Max(HP.Value - amount, 0);
            RefreshPhase();
        }

        public void ApplyFire(int stacks, int turns)
        {
            if (!IsServer || stacks <= 0) return;
            _fire.Add(new FireStack { stacks = stacks, turnsRemaining = turns });
        }

        /// <summary>End-of-attack-round Fire tick: 5 damage per active stack, then decrement durations.</summary>
        public void ServerTickFire()
        {
            if (!IsServer || _fire.Count == 0) return;
            int total = TotalFireStacks;
            if (total > 0) ServerTakeDamage(total * 5);
            for (int i = _fire.Count - 1; i >= 0; i--)
            {
                var f = _fire[i];
                f.turnsRemaining--;
                if (f.turnsRemaining <= 0) _fire.RemoveAt(i);
                else _fire[i] = f;
            }
        }

        // =====================================================================
        //  Phases & attack selection
        // =====================================================================
        private void RefreshPhase()
        {
            if (_data == null) return;
            var phase = _data.GetPhaseForHealth(HP.Value);
            if (phase != _currentPhase)
            {
                bool wasFirst = _currentPhase == null;
                _currentPhase = phase;
                PhaseIndex.Value = Mathf.Max(0, _data.IndexOfPhase(phase));
                _rotationIndex = 0;
                if (!wasFirst && phase != null && phase.transitionLines != null)
                    foreach (var line in phase.transitionLines)
                        SayClientRpc(line.text, line.autoAdvanceSeconds);
            }
        }

        /// <summary>Get the next attack pattern for the current phase and advance the rotation.</summary>
        public BulletPatternSO ServerNextPattern()
        {
            if (!IsServer) return null;
            RefreshPhase();
            var rotation = _currentPhase?.attackRotation;
            if (rotation == null || rotation.Count == 0) return null;
            var pattern = rotation[_rotationIndex % rotation.Count];
            _rotationIndex++;
            return pattern;
        }

        public IReadOnlyList<DialogueLine> IntroLines => _data != null ? _data.introLines : null;
        public IReadOnlyList<DialogueLine> DefeatLines => _data != null ? _data.defeatLines : null;

        [ClientRpc]
        public void SayClientRpc(string text, float seconds) => CombatEvents.RaiseBossSay(text, seconds);

        /// <summary>Fade the boss sprite out over <paramref name="duration"/> seconds (on defeat).</summary>
        [ClientRpc]
        public void FadeOutClientRpc(float duration)
        {
            if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }
            transform.localPosition = _restPos;
            StartCoroutine(FadeOut(duration));
        }

        private IEnumerator FadeOut(float duration)
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null) yield break;
            Color c = sr.color;
            float a0 = c.a, t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(a0, 0f, duration > 0f ? t / duration : 1f);
                sr.color = c;
                yield return null;
            }
            c.a = 0f; sr.color = c;
        }
    }
}

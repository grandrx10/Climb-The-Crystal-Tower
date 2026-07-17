using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// A boss phase: a bracket of the boss's health with its own attack-pattern rotation.
    /// Each defend turn advances an index through the rotation (looping).
    /// </summary>
    [Serializable]
    public class BossPhase
    {
        public string phaseName = "Phase 1";

        [Range(0f, 1f)]
        [Tooltip("This phase becomes active when the boss's HP fraction drops to or below this value. Order phases from highest threshold (1) to lowest.")]
        public float enterAtHealthFraction = 1f;

        [Tooltip("Attack patterns cycled through, one per defend turn, looping.")]
        public List<BulletPatternSO> attackRotation = new List<BulletPatternSO>();

        [Tooltip("Optional lines the boss says once when this phase begins.")]
        public List<DialogueLine> transitionLines = new List<DialogueLine>();
    }

    /// <summary>
    /// A boss encounter definition: health, opening dialogue and health-gated phases with
    /// distinct attack rotations. Lives in a stage; drop one into a StageData later.
    /// </summary>
    [CreateAssetMenu(fileName = "Boss_", menuName = "2CT/Boss", order = 3)]
    public class BossData : ScriptableObject
    {
        [Header("Identity")]
        public string bossName = "New Boss";
        public Sprite sprite;
        [Tooltip("Mirror the boss sprite left↔right.")]
        public bool flipX;
        [Tooltip("Mirror the boss sprite up↔down.")]
        public bool flipY;
        public int maxHP = 100;

        [Header("Opening")]
        [Tooltip("Lines shown above the boss during the Intro phase before its first attack.")]
        public List<DialogueLine> introLines = new List<DialogueLine>();

        [Header("Defeat")]
        [Tooltip("Last words the boss speaks when defeated, just before it fades out and the " +
                 "victory screen appears. Leave empty to skip straight to the fade.")]
        public List<DialogueLine> defeatLines = new List<DialogueLine>();

        [Header("Phases (highest health fraction first)")]
        public List<BossPhase> phases = new List<BossPhase>();

        /// <summary>Selects the active phase for the given current health.</summary>
        public BossPhase GetPhaseForHealth(int currentHP)
        {
            if (phases == null || phases.Count == 0) return null;
            float frac = maxHP > 0 ? (float)currentHP / maxHP : 0f;
            BossPhase chosen = phases[0];
            foreach (var p in phases)
                if (frac <= p.enterAtHealthFraction)
                    chosen = p; // phases ordered high->low, so the last matching is the deepest reached
            return chosen;
        }

        public int IndexOfPhase(BossPhase phase) => phases != null ? phases.IndexOf(phase) : -1;
    }
}

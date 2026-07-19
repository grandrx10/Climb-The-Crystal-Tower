using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// One line of dialogue. Boss combat lines (intro/defeat/transition) auto-advance after
    /// <see cref="autoAdvanceSeconds"/>; free-roam dialogue ignores it (advances on a press).
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        public string speaker = "";
        [TextArea(2, 5)] public string text = "";

        [Tooltip("Boss/cutscene lines auto-advance after this many seconds. (Free-roam dialogue advances on a press and ignores this.)")]
        public float autoAdvanceSeconds = 2.5f;

        [SerializeReference]
        [Tooltip("Things that happen the moment this line appears — swap a sprite, remove a wall, … " +
                 "(only fire for free-roam dialogue; scene references are null on boss/asset lines).")]
        public List<DialogueAction> actions = new List<DialogueAction>();

        /// <summary>Runs every action attached to this line. Safe to call when the list is empty/null.</summary>
        public void FireActions()
        {
            if (actions == null) return;
            for (int i = 0; i < actions.Count; i++) actions[i]?.Execute();
        }
    }
}

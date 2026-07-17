using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// One line of dialogue. Used by boss combat intros (auto-advancing after
    /// <see cref="autoAdvanceSeconds"/>) and, later, by free-roam dialogue trees.
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        public string speaker = "";
        [TextArea(2, 5)] public string text = "";

        [Tooltip("Boss/cutscene lines auto-advance after this many seconds. 0 = wait for a skip press.")]
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

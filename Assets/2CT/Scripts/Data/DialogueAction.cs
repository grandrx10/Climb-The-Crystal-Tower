using System;
using TwoCT.FreeRoam;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// Base class for something that happens the moment a <see cref="DialogueLine"/> is shown —
    /// swap a sprite, remove a wall, etc. A line holds a <c>[SerializeReference]</c> list of these,
    /// so a designer composes mid-conversation events entirely in the inspector (see
    /// InteractableEditor for the "Add Action" dropdown). Add a new event by creating a new
    /// subclass — it appears in the dropdown automatically.
    ///
    /// Actions run locally on each client (free-roam group dialogue fires them per-client from
    /// the identical scene <see cref="Interactable.lines"/> list). They reference SCENE objects, so
    /// they only do anything on lines authored on an in-scene component (e.g. Interactable); on a
    /// BossData asset the scene references are null and the actions no-op.
    /// </summary>
    [Serializable]
    public abstract class DialogueAction
    {
        /// <summary>Runs on every client when the owning line appears. Guard your own nulls.</summary>
        public abstract void Execute();

        /// <summary>One-line human summary shown in the inspector.</summary>
        public abstract string Describe();
    }

    /// <summary>
    /// Change a target renderer's sprite mid-conversation (e.g. an NPC reacts, a statue cracks).
    /// Drag any world object with a SpriteRenderer into <see cref="target"/>. Leaving
    /// <see cref="newSprite"/> empty hides the renderer instead.
    /// </summary>
    [Serializable]
    public class SwapSpriteAction : DialogueAction
    {
        [Tooltip("The SpriteRenderer to change. Drag a scene object here (its SpriteRenderer is used).")]
        public SpriteRenderer target;

        [Tooltip("The sprite to switch to. Leave empty to just hide the renderer.")]
        public Sprite newSprite;

        public override void Execute()
        {
            if (target == null) return;
            if (newSprite != null) { target.sprite = newSprite; target.enabled = true; }
            else target.enabled = false;
        }

        public override string Describe()
        {
            if (target == null) return "Swap sprite (no target set)";
            return newSprite == null ? $"Hide {target.name}" : $"Swap {target.name} → {newSprite.name}";
        }
    }

    /// <summary>
    /// Remove a <see cref="FreeRoamWall"/> mid-conversation so the player can walk through where it
    /// was (e.g. a barrier drops after a boss agrees to let you pass). Deactivating the wall's
    /// GameObject removes both its collision (via FreeRoamWall.OnDisable) and its art.
    /// </summary>
    [Serializable]
    public class RemoveWallAction : DialogueAction
    {
        [Tooltip("The wall to remove. Drag a FreeRoamWall (or its GameObject) here.")]
        public FreeRoamWall wall;

        [Tooltip("Hide the whole GameObject (art + collision). Uncheck to drop only the collision, leaving any sprite visible.")]
        public bool hideObject = true;

        public override void Execute()
        {
            if (wall == null) return;
            if (hideObject) wall.gameObject.SetActive(false); // OnDisable removes it from the collision registry
            else wall.enabled = false;                        // component off → out of registry, its sprite stays
        }

        public override string Describe()
            => wall == null ? "Remove wall (none set)" : $"Remove wall '{wall.name}'";
    }

    /// <summary>
    /// Shake a scene object for a duration mid-conversation (e.g. the ground rumbles, a statue
    /// trembles before it cracks). Drag any world object into <see cref="target"/>; it's jittered
    /// with a decaying wobble that eases back to rest. Runs via an on-demand
    /// <see cref="FreeRoamWall"/>-namespace <c>TransformShaker</c> added to the target, so no
    /// component wiring is needed up-front. Best on static-ish props (see TransformShaker note).
    /// </summary>
    [Serializable]
    public class ShakeAction : DialogueAction
    {
        [Tooltip("The object to shake. Drag any scene object here (its Transform is jittered).")]
        public Transform target;

        [Tooltip("How long the shake lasts, in seconds.")]
        public float duration = 0.4f;

        [Tooltip("Peak positional jitter (world units) at the start; decays to zero over the duration.")]
        public float magnitude = 0.2f;

        public override void Execute()
        {
            if (target == null) return;
            var shaker = target.GetComponent<TransformShaker>();
            if (shaker == null) shaker = target.gameObject.AddComponent<TransformShaker>();
            shaker.Shake(duration, magnitude);
        }

        public override string Describe()
            => target == null ? "Shake (no target set)" : $"Shake {target.name} for {duration:0.##}s";
    }
}

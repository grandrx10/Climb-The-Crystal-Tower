using System.Collections.Generic;
using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// A playable character. Players pick one of three unique characters in the lobby.
    /// Holds base stats and the starting deck (list, so duplicate cards are allowed).
    /// </summary>
    [CreateAssetMenu(fileName = "Character_", menuName = "2CT/Character", order = 2)]
    public class CharacterData : ScriptableObject
    {
        [Header("Identity")]
        public string characterName = "New Character";
        [Tooltip("Base sprite. All characters are authored facing RIGHT.")]
        public Sprite baseSprite;
        [Tooltip("Character-select background / theme colour. Does NOT tint the sprite.")]
        public Color tint = Color.white;
        [Tooltip("Mirror the sprite left↔right. Note: composes with the free-roam facing flip.")]
        public bool flipX;
        [Tooltip("Mirror the sprite up↔down.")]
        public bool flipY;
        [Tooltip("Small icon used as this character's dodge marker in the defend arena. Falls back " +
                 "to a coloured circle. Its display size is independent of the collision hitbox.")]
        public Sprite dodgeIcon;

        [Header("Stats")]
        public int maxHP = 30;
        public int startingMana = 10;
        public int manaPerRound = 10;
        [Tooltip("Cards drawn at the start of each attack turn before bonuses.")]
        public int baseCardsPerRound = 3;

        [Header("Movement")]
        [Tooltip("Base per-axis move speed (units/sec): X = A/D, Y = W/S. Free-roam uses this " +
                 "directly; the defend arena scales it by DefendArena.dodgeSpeedMultiplier.")]
        public Vector2 moveSpeed = new Vector2(4.5f, 4.5f);

        [Header("Deck")]
        public List<CardData> startingDeck = new List<CardData>();
    }
}

using UnityEngine;

namespace TwoCT.Data
{
    /// <summary>
    /// The single shared tuning asset for Marnu's spell pages. Every Marnu attack pattern
    /// references this one asset, so editing a spell here (colours, sprites, damage, speeds)
    /// changes it everywhere at once — no per-pattern copies to keep in sync.
    /// </summary>
    [CreateAssetMenu(fileName = "SpellBook_Marnu", menuName = "2CT/Marnu Spell Book")]
    public class MarnuSpellBookSO : ScriptableObject
    {
        public MarnuSpellConfig spells = new MarnuSpellConfig();
    }
}

using System.Collections.Generic;
using TwoCT.Data;
using UnityEngine;

namespace TwoCT.Core
{
    /// <summary>
    /// Central lookup for all content by id/name. Because the server only syncs lightweight
    /// ids (a hand is sent as card ids; a defend round as a pattern name + seed), every client
    /// resolves the actual ScriptableObject through this shared registry.
    ///
    /// Place a single instance at <c>Assets/2CT/Resources/ContentRegistry.asset</c>. Use
    /// "2CT ▸ Rebuild Content Registry" to auto-populate it from every asset in the project.
    /// </summary>
    [CreateAssetMenu(fileName = "ContentRegistry", menuName = "2CT/Content Registry", order = 100)]
    public class ContentRegistry : ScriptableObject
    {
        public List<CardData> cards = new List<CardData>();
        public List<MythicalData> mythicals = new List<MythicalData>();
        public List<BossData> bosses = new List<BossData>();
        public List<BulletPatternSO> patterns = new List<BulletPatternSO>();
        public List<CharacterData> characters = new List<CharacterData>();

        [Tooltip("Every player's opening deck (2 copies of each Neutral starter card). Rebuilt by " +
                 "'2CT ▸ Rebuild Content Registry'.")]
        public List<CardData> starterDeck = new List<CardData>();

        private Dictionary<string, CardData> _cardsById;
        private Dictionary<string, BulletPatternSO> _patternsByName;
        private Dictionary<string, BossData> _bossesByName;

        private static ContentRegistry _instance;
        public static ContentRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<ContentRegistry>("ContentRegistry");
                    if (_instance == null)
                        Debug.LogError("[2CT] ContentRegistry not found in a Resources folder. Run '2CT ▸ Rebuild Content Registry'.");
                    else
                        _instance.BuildIndices();
                }
                return _instance;
            }
        }

        public void BuildIndices()
        {
            _cardsById = new Dictionary<string, CardData>();
            foreach (var c in cards) if (c != null) _cardsById[c.Id] = c;
            _patternsByName = new Dictionary<string, BulletPatternSO>();
            foreach (var p in patterns) if (p != null) _patternsByName[p.name] = p;
            _bossesByName = new Dictionary<string, BossData>();
            foreach (var b in bosses) if (b != null) _bossesByName[b.name] = b;
        }

        public CardData GetCard(string id)
        {
            if (_cardsById == null) BuildIndices();
            return id != null && _cardsById.TryGetValue(id, out var c) ? c : null;
        }

        public BulletPatternSO GetPattern(string patternName)
        {
            if (_patternsByName == null) BuildIndices();
            return patternName != null && _patternsByName.TryGetValue(patternName, out var p) ? p : null;
        }

        /// <summary>Look up a boss by its asset name (the id combat triggers store in SessionData).</summary>
        public BossData GetBoss(string id)
        {
            if (_bossesByName == null) BuildIndices();
            return id != null && _bossesByName.TryGetValue(id, out var b) ? b : null;
        }

        private void OnEnable() { if (Application.isPlaying) BuildIndices(); }
    }
}

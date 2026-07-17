using System.Collections.Generic;
using System.IO;
using TwoCT.Core;
using TwoCT.Data;
using UnityEditor;
using UnityEngine;

namespace TwoCT.EditorTools
{
    /// <summary>
    /// Content authoring menus. "Generate Sample Content" builds every card/mythical/boss/
    /// pattern/character from the design doc as editable .asset files. "Rebuild Content
    /// Registry" scans the project and repopulates the runtime lookup used for network sync.
    /// Both are idempotent — safe to re-run.
    /// </summary>
    public static class ContentTools
    {
        private const string Root = "Assets/2CT/Content";
        private const string ResourcesDir = "Assets/2CT/Resources";

        [MenuItem("2CT/Rebuild Content Registry", priority = 20)]
        public static ContentRegistry RebuildRegistry()
        {
            var reg = LoadOrCreate<ContentRegistry>(ResourcesDir, "ContentRegistry");
            reg.cards = FindAll<CardData>();
            reg.mythicals = FindAll<MythicalData>();
            reg.bosses = FindAll<BossData>();
            reg.patterns = FindAll<BulletPatternSO>();
            reg.characters = FindAll<CharacterData>();
            reg.starterDeck = BuildStarterDeck(reg.cards);
            reg.BuildIndices();
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            Debug.Log($"[2CT] Registry rebuilt: {reg.cards.Count} cards, {reg.mythicals.Count} mythicals, " +
                      $"{reg.bosses.Count} bosses, {reg.patterns.Count} patterns, {reg.characters.Count} characters.");
            return reg;
        }

        [MenuItem("2CT/Generate Sample Content", priority = 0)]
        public static void GenerateSampleContent()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                var cards = BuildCards();
                BuildMythicals();
                var patterns = BuildPatterns();
                BuildBoss(patterns);
                BuildCharacters(cards);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            RebuildRegistry();
            Debug.Log("[2CT] Sample content generated under " + Root);
        }

        /// <summary>
        /// CARDS ONLY: (re)build every card .asset from <see cref="BuildCards"/>, then rebuild the
        /// registry (which just re-collects assets + the starter deck — it never edits them). Bosses,
        /// characters, patterns and mythicals are left completely untouched, so your Inspector tuning
        /// on those survives. Use this instead of "Generate Sample Content" once you've tuned content.
        /// </summary>
        [MenuItem("2CT/Generate Card Content", priority = 2)]
        public static void GenerateCardContent()
        {
            AssetDatabase.StartAssetEditing();
            try { BuildCards(); }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            RebuildRegistry();   // re-collects all assets (non-destructive) + rebuilds the starter deck
            Debug.Log("[2CT] Card content generated (bosses/characters/patterns/mythicals untouched).");
        }

        /// <summary>
        /// Additive, NON-destructive: creates Buble's three bubble-attack patterns only if they
        /// don't already exist, appends any missing ones to Phase 1's rotation, and renames the
        /// boss to "Buble" only if it's still the default name. Everything you've hand-tuned —
        /// existing patterns, other phases, cards, characters — is left untouched. Use this
        /// instead of "Generate Sample Content" when you've been editing content in the Inspector.
        /// </summary>
        [MenuItem("2CT/Add Buble Bubble Attacks", priority = 1)]
        public static void AddBubleBubbleAttacks()
        {
            var boss = FindAll<BossData>().Find(b => b != null && b.name == "Boss_FirstGuardian");
            if (boss == null)
            {
                Debug.LogError("[2CT] 'Boss_FirstGuardian' not found. Run '2CT ▸ Generate Sample Content' once first.");
                return;
            }

            var created = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                EnsureFolder(Root + "/Patterns");
                var patterns = new List<BulletPatternSO>();
                foreach (var name in BubblePatternNames)
                {
                    string path = $"{Root}/Patterns/{name}.asset";
                    var p = AssetDatabase.LoadAssetAtPath<BulletPatternSO>(path);
                    if (p == null)                                   // create only if missing (preserve tuning)
                    {
                        p = ScriptableObject.CreateInstance<BulletPatternSO>();
                        AssetDatabase.CreateAsset(p, path);
                        ConfigureBublePattern(name, p);
                        EditorUtility.SetDirty(p);
                        created.Add(name);
                    }
                    patterns.Add(p);
                }

                if (boss.phases == null || boss.phases.Count == 0)
                {
                    Debug.LogError("[2CT] Boss has no phases to add attacks to. Run 'Generate Sample Content' first.");
                    return;
                }
                var phase1 = boss.phases[0];
                if (phase1.attackRotation == null) phase1.attackRotation = new List<BulletPatternSO>();
                foreach (var p in patterns)
                    if (p != null && !phase1.attackRotation.Contains(p)) phase1.attackRotation.Add(p);
                if (boss.bossName == "The First Guardian") boss.bossName = "Buble";
                EditorUtility.SetDirty(boss);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            RebuildRegistry();
            Debug.Log($"[2CT] Buble bubble attacks ready. Created {created.Count} new pattern(s): " +
                      $"{(created.Count > 0 ? string.Join(", ", created) : "none (all existed)")}. Phase 1 rotation now has {boss.phases[0].attackRotation.Count} attack(s).");
        }

        // =====================================================================
        //  Ryomi (boss 2) — additive / non-destructive
        // =====================================================================
        private static readonly string[] RyomiPatternNames =
        {
            "Pattern_Ryomi_MarkedStrike", "Pattern_Ryomi_Cut", "Pattern_Ryomi_Ricochet",
            "Pattern_Ryomi_TrackingCut", "Pattern_Ryomi_Lasso"
        };

        /// <summary>Coded default for each Ryomi attack pattern (emitter fields carry the design defaults).</summary>
        private static void ConfigureRyomiPattern(string name, BulletPatternSO p)
        {
            switch (name)
            {
                case "Pattern_Ryomi_MarkedStrike":
                    p.duration = 9f;
                    p.emitters = new List<BulletEmitter> { new MarkedStrikeEmitter { startTime = 0f, duration = 9f, damage = 10 } };
                    break;
                case "Pattern_Ryomi_Cut":
                    p.duration = 10f;
                    p.emitters = new List<BulletEmitter> { new SlidingCutEmitter { startTime = 0f, duration = 10f, damage = 10 } };
                    break;
                case "Pattern_Ryomi_Ricochet":
                    p.duration = 10f;
                    p.emitters = new List<BulletEmitter> { new RicochetEmitter { startTime = 0f, duration = 10f, damage = 8 } };
                    break;
                case "Pattern_Ryomi_TrackingCut":
                    p.duration = 10f;
                    p.emitters = new List<BulletEmitter> { new TrackingCutEmitter { startTime = 0f, duration = 10f, damage = 10 } };
                    break;
                case "Pattern_Ryomi_Lasso":
                    p.duration = 12f;
                    p.emitters = new List<BulletEmitter> { new LassoEmitter { startTime = 0f, duration = 12f } };
                    break;
            }
        }

        /// <summary>
        /// Additive: creates Ryomi (boss 2, 150 HP, 2 phases) + her five attack patterns only if they
        /// don't already exist, then rebuilds the registry so a combat trigger can select her. Existing
        /// assets (incl. Buble and any Ryomi tuning) are left untouched. Point an Interactable's
        /// "Boss To Fight" at Boss_Ryomi to fight her in the universal combat scene.
        /// </summary>
        [MenuItem("2CT/Add Ryomi (Boss 2)", priority = 4)]
        public static void AddRyomi()
        {
            var created = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                EnsureFolder(Root + "/Patterns");
                EnsureFolder(Root + "/Bosses");
                var patterns = new Dictionary<string, BulletPatternSO>();
                foreach (var name in RyomiPatternNames)
                {
                    string path = $"{Root}/Patterns/{name}.asset";
                    var p = AssetDatabase.LoadAssetAtPath<BulletPatternSO>(path);
                    if (p == null)                                     // create only if missing (preserve tuning)
                    {
                        p = ScriptableObject.CreateInstance<BulletPatternSO>();
                        AssetDatabase.CreateAsset(p, path);
                        ConfigureRyomiPattern(name, p);
                        EditorUtility.SetDirty(p);
                        created.Add(name);
                    }
                    patterns[name] = p;
                }

                string bossPath = $"{Root}/Bosses/Boss_Ryomi.asset";
                var boss = AssetDatabase.LoadAssetAtPath<BossData>(bossPath);
                if (boss == null)
                {
                    boss = ScriptableObject.CreateInstance<BossData>();
                    AssetDatabase.CreateAsset(boss, bossPath);
                    boss.bossName = "Ryomi";
                    boss.maxHP = 150;
                    boss.introLines = new List<DialogueLine>
                    {
                        new DialogueLine { speaker = "Ryomi", text = "Winter's bounty is mine — y'all just picked the wrong hill.", autoAdvanceSeconds = 3f },
                        new DialogueLine { speaker = "Ryomi", text = "Draw.", autoAdvanceSeconds = 1.5f },
                    };
                    boss.defeatLines = new List<DialogueLine>
                    {
                        new DialogueLine { speaker = "Ryomi", text = "Heh... reckon that bounty'll have to wait.", autoAdvanceSeconds = 3f },
                    };
                    boss.phases = new List<BossPhase>
                    {
                        new BossPhase { phaseName = "Phase 1", enterAtHealthFraction = 1f,
                            attackRotation = new List<BulletPatternSO> {
                                patterns["Pattern_Ryomi_MarkedStrike"], patterns["Pattern_Ryomi_Cut"],
                                patterns["Pattern_Ryomi_Ricochet"], patterns["Pattern_Ryomi_TrackingCut"] },
                            transitionLines = new List<DialogueLine>() },
                        new BossPhase { phaseName = "Phase 2", enterAtHealthFraction = 0.4f,
                            attackRotation = new List<BulletPatternSO> { patterns["Pattern_Ryomi_Lasso"] },
                            transitionLines = new List<DialogueLine> { new DialogueLine { speaker = "Ryomi", text = "Now yer roped in.", autoAdvanceSeconds = 1.5f } } },
                    };
                    EditorUtility.SetDirty(boss);
                    created.Add("Boss_Ryomi");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            RebuildRegistry();
            EditorUtility.DisplayDialog("2CT",
                (created.Count == 0 ? "Ryomi + patterns already existed (left as-is)." : "Created: " + string.Join(", ", created)) +
                "\n\nRegistry rebuilt. Set a combat trigger's 'Boss To Fight' to Boss_Ryomi. (No art yet — she uses the placeholder box; assign BossData.sprite when ready.)",
                "OK");
        }

        // =====================================================================
        //  Cards
        // =====================================================================
        /// <summary>The standard opening deck for every player: N copies of each Neutral starter.</summary>
        private static readonly string[] StarterCardIds = { "Mana Bolt", "Preparation", "Shield Hex", "Loyal Blade", "Focus" };
        private const int StarterCopiesEach = 2;

        private static List<CardData> BuildStarterDeck(List<CardData> allCards)
        {
            var deck = new List<CardData>();
            foreach (var id in StarterCardIds)
            {
                var card = allCards.Find(c => c != null && c.Id == id);
                if (card == null) { Debug.LogWarning($"[2CT] Starter card '{id}' not found — registry starter deck will be incomplete."); continue; }
                for (int i = 0; i < StarterCopiesEach; i++) deck.Add(card);
            }
            return deck;
        }

        private static Dictionary<string, CardData> BuildCards()
        {
            var map = new Dictionary<string, CardData>();

            // `text` is written into CardData.cardText — the editable rules text shown on the card.
            // Edit any card's wording in the inspector; re-running this tool restores these defaults.
            CardData Card(string name, int mana, TargetType target, CardRarity rarity, CardCategory category, string text, params CardEffect[] effects)
            {
                var c = LoadOrCreate<CardData>(Root + "/Cards", "Card_" + Safe(name));
                c.cardName = name; c.manaCost = mana; c.targetType = target; c.rarity = rarity;
                c.category = category;
                c.cardId = name; c.vfxKey = Safe(name);
                c.cardText = text;
                c.halfCostWhenShielded = false; c.activateWhenDiscarded = false;   // reset; helpers below opt-in
                c.effects = new List<CardEffect>(effects);
                EditorUtility.SetDirty(c);
                map[name] = c;
                return c;
            }

            // Helpers to set the extra CardData flags after Card() builds the asset.
            CardData Discardable(CardData c) { c.activateWhenDiscarded = true; EditorUtility.SetDirty(c); return c; }
            CardData ShieldDiscount(CardData c) { c.halfCostWhenShielded = true; EditorUtility.SetDirty(c); return c; }

            // Neutral — standard starters (2 copies of each form every player's opening deck) + utility.
            Card("Mana Bolt", 10, TargetType.None, CardRarity.Common, CardCategory.Neutral, "Deal 10 damage.",
                new DealDamageEffect { damage = 10 });
            Card("Shield Hex", 10, TargetType.None, CardRarity.Common, CardCategory.Neutral, "Gain a 10 HP shield.",
                new GainShieldEffect { shield = 10 });
            Card("Preparation", 10, TargetType.None, CardRarity.Common, CardCategory.Neutral, "Draw 1 extra card every turn.",
                new GainCardDrawEffect { amount = 1 });
            Card("Loyal Blade", 0, TargetType.None, CardRarity.Common, CardCategory.Neutral, "Deal 3 damage.",
                new DealDamageEffect { damage = 3, countsAsSpell = false });
            Card("Focus", 10, TargetType.None, CardRarity.Common, CardCategory.Neutral, "Gain +5 mana per turn.",
                new GainManaPerTurnEffect { amount = 5 });
            Card("Acceleration", 20, TargetType.None, CardRarity.Uncommon, CardCategory.Neutral, "Gain +15 mana per turn.",
                new GainManaPerTurnEffect { amount = 15 });
            Card("Flawless", 5, TargetType.None, CardRarity.Uncommon, CardCategory.Neutral,
                "If you take no damage next turn, your cards cost half as much next turn.", new ArmFlawlessEffect());
            Card("Copy", 5, TargetType.None, CardRarity.Uncommon, CardCategory.Neutral,
                "Turn this card into a copy of the last card you played this turn.", new CopyLastCardEffect());

            // Incineration — fire / burn.
            Card("Bolt of Flame", 20, TargetType.None, CardRarity.Uncommon, CardCategory.Incineration,
                "Deal 15 damage and apply 1 stack of Fire.",
                new DealDamageEffect { damage = 15 }, new ApplyFireEffect { stacks = 1, turns = 2 });
            Card("Fireball", 30, TargetType.None, CardRarity.Rare, CardCategory.Incineration,
                "Deal 30 damage and apply 2 stacks of Fire.",
                new DealDamageEffect { damage = 30 }, new ApplyFireEffect { stacks = 2, turns = 2 });
            Card("Scorching Barrier", 20, TargetType.None, CardRarity.Uncommon, CardCategory.Incineration,
                "Gain a 25 HP shield for 1 round.",
                new GainShieldEffect { shield = 25, roundsDuration = 1 });
            Card("Searing Power", 10, TargetType.None, CardRarity.Uncommon, CardCategory.Incineration,
                "Take 10 damage. Gain +10 mana per turn.",
                new SelfDamageEffect { amount = 10 }, new GainManaPerTurnEffect { amount = 10 });

            // Life — heal / growth / revive.
            Card("Feral Growth", 10, TargetType.None, CardRarity.Common, CardCategory.Life,
                "Take 50% less damage next round.",
                new DamageReductionEffect { reduction = 0.5f, alsoEnlarge = true });
            Card("Wound Be Gone", 10, TargetType.Ally, CardRarity.Common, CardCategory.Life,
                "Restore 10 HP to an ally.",
                new HealEffect { target = EffectTarget.ChosenTarget, amount = 10 });
            Card("Heart Starter", 10, TargetType.DeadAlly, CardRarity.Uncommon, CardCategory.Life,
                "Revive a knocked-out ally with 1 HP. They may act this turn.",
                new ReviveEffect { hp = 1, canActImmediately = true });
            Card("Vine Strike", 10, TargetType.None, CardRarity.Common, CardCategory.Life,
                "Deal 5 damage, restore 5 HP, and draw a card.",
                new DealDamageEffect { damage = 5 }, new HealEffect { target = EffectTarget.Caster, amount = 5 }, new DrawCardsNowEffect { amount = 1 });

            // Wild — chaos / utility / lifesteal.
            Card("Divination", 5, TargetType.None, CardRarity.Common, CardCategory.Wild,
                "Discard your hand and redraw a full hand.", new DiscardAndRedrawEffect());
            Card("Mask of Wild Magic", 15, TargetType.None, CardRarity.Rare, CardCategory.Wild,
                "Cast a random spell from your deck.", new CastRandomSpellEffect());
            Card("Lifestealer Aura", 5, TargetType.None, CardRarity.Uncommon, CardCategory.Wild,
                "Your next spell heals you for the damage it deals.", new ArmLifestealEffect());

            // Severance — deal damage + discard synergies. Cut/Carve/Divide ALSO fire when force-discarded.
            Card("Slice", 10, TargetType.None, CardRarity.Common, CardCategory.Severance,
                "Deal 15 damage. Discard 2 cards from your hand.",
                new DealDamageEffect { damage = 15 }, new DiscardCardsEffect { count = 2 });
            Discardable(Card("Cut", 5, TargetType.None, CardRarity.Common, CardCategory.Severance,
                "Deal 5 damage. If discarded, activate me.",
                new DealDamageEffect { damage = 5 }));
            Discardable(Card("Carve", 5, TargetType.None, CardRarity.Common, CardCategory.Severance,
                "Deal 3 damage and draw a card. If discarded, activate me.",
                new DealDamageEffect { damage = 3 }, new DrawCardsNowEffect { amount = 1 }));
            Discardable(Card("Divide", 5, TargetType.None, CardRarity.Common, CardCategory.Severance,
                "Gain a 7 HP shield. If discarded, activate me.",
                new GainShieldEffect { shield = 7 }));
            Card("Sever", 20, TargetType.None, CardRarity.Uncommon, CardCategory.Severance,
                "Discard your hand. Deal 10 damage for each card discarded.",
                new DiscardHandForDamageEffect { damagePerCard = 10 });

            // Bubble — shields, storage, and shield payoffs.
            Card("Bubble Shield", 15, TargetType.None, CardRarity.Common, CardCategory.Bubble,
                "Gain a 15 HP shield this turn and next turn.",
                new GainShieldEffect { shield = 15 }, new GainShieldNextTurnEffect { shield = 15 });
            Card("Bubble Storage", 5, TargetType.None, CardRarity.Uncommon, CardCategory.Bubble,
                "Store your hand. Next turn, return the stored cards on top of your normal draw.", new StoreHandEffect());
            Card("Bubble Pop", 10, TargetType.None, CardRarity.Uncommon, CardCategory.Bubble,
                "Deal damage equal to twice your shield, then lose your shield.",
                new BubblePopEffect { shieldMultiplier = 2 });
            Card("Film Form", 10, TargetType.None, CardRarity.Uncommon, CardCategory.Bubble,
                "Any shield you gain this turn is doubled.", new ShieldGainMultiplierEffect { multiplier = 2 });
            Card("Bubble For My Friends", 10, TargetType.Ally, CardRarity.Common, CardCategory.Bubble,
                "Grant a 10 HP shield to any ally.",
                new GainShieldEffect { target = EffectTarget.ChosenTarget, shield = 10 });
            ShieldDiscount(Card("Bubble Power", 20, TargetType.None, CardRarity.Rare, CardCategory.Bubble,
                "Gain +10 mana per turn. Costs half as much while you have a shield.",
                new GainManaPerTurnEffect { amount = 10 }));

            return map;
        }

        // =====================================================================
        //  Mythicals
        // =====================================================================
        private static void BuildMythicals()
        {
            void Mythic(string name, MythicalKind kind, int mana, TargetType target, params CardEffect[] effects)
            {
                var m = LoadOrCreate<MythicalData>(Root + "/Mythicals", "Mythical_" + Safe(name));
                m.mythicalName = name; m.kind = kind; m.manaCost = mana; m.targetType = target;
                m.vfxKey = Safe(name);
                m.effects = new List<CardEffect>(effects);
                EditorUtility.SetDirty(m);
            }

            Mythic("Spellmaster", MythicalKind.Passive, 0, TargetType.None, new SpellDamageBonusEffect { amount = 5 });
            Mythic("Manablessed", MythicalKind.Passive, 0, TargetType.None, new GainManaPerTurnEffect { amount = 5 });
            Mythic("Cheat Death", MythicalKind.Active, 20, TargetType.None, new CheatDeathEffect());
            Mythic("Greed Spell", MythicalKind.Active, 10, TargetType.None, new GainCardDrawEffect { amount = 2 });
            Mythic("Pillar of Super Awesome Divine Judgement", MythicalKind.Active, 50, TargetType.None, new DealDamageEffect { damage = 100 });
        }

        // =====================================================================
        //  Bullet patterns
        // =====================================================================
        private static Dictionary<string, BulletPatternSO> BuildPatterns()
        {
            var map = new Dictionary<string, BulletPatternSO>();

            // Doc's first-boss attack: bubbles at ±25°, 8/s, 8 dmg, 10s.
            var bubbles = LoadOrCreate<BulletPatternSO>(Root + "/Patterns", "Pattern_Bubbles");
            bubbles.duration = 10f;
            bubbles.emitters = new List<BulletEmitter>
            {
                new RandomSpreadEmitter { startTime = 0f, duration = 10f, bulletsPerSecond = 8f, spreadDegrees = 25f, speed = 4f, damage = 8, radius = 0.25f, lifetime = 6f, color = new Color(0.4f, 0.8f, 1f) }
            };
            EditorUtility.SetDirty(bubbles);
            map["Pattern_Bubbles"] = bubbles;

            // Buble's three new attacks (see ConfigureBuble* for the tuning). These reset the
            // pattern to code defaults, same as every other asset in a full Generate.
            foreach (var name in BubblePatternNames)
            {
                var p = LoadOrCreate<BulletPatternSO>(Root + "/Patterns", name);
                ConfigureBublePattern(name, p);
                EditorUtility.SetDirty(p);
                map[name] = p;
            }

            // Phase-2 attack: faster bubbles layered with a slow spiral.
            var spiral = LoadOrCreate<BulletPatternSO>(Root + "/Patterns", "Pattern_Spiral");
            spiral.duration = 12f;
            spiral.emitters = new List<BulletEmitter>
            {
                new RandomSpreadEmitter { startTime = 0f, duration = 12f, bulletsPerSecond = 10f, spreadDegrees = 30f, speed = 5f, damage = 8, radius = 0.24f, lifetime = 6f, color = new Color(0.6f, 0.9f, 1f) },
                new RadialBurstEmitter { startTime = 1f, duration = 11f, bulletsPerRing = 14, ringsPerSecond = 0.8f, spinPerRing = 9f, speed = 2.6f, damage = 6, radius = 0.22f, lifetime = 7f, color = new Color(1f, 0.5f, 0.3f) }
            };
            EditorUtility.SetDirty(spiral);
            map["Pattern_Spiral"] = spiral;

            return map;
        }

        /// <summary>The three bubble attacks added to Buble's Phase 1.</summary>
        private static readonly string[] BubblePatternNames =
        {
            "Pattern_ExplodingBubbles", "Pattern_AppearingBubbles", "Pattern_TargetedBubbles"
        };

        /// <summary>Set a Buble attack pattern to its coded default (single source of truth, used by
        /// both the full Generate and the additive "Add Buble Bubble Attacks" menu).</summary>
        private static void ConfigureBublePattern(string name, BulletPatternSO p)
        {
            p.duration = 6f;
            switch (name)
            {
                case "Pattern_ExplodingBubbles": // attack 1
                    p.emitters = new List<BulletEmitter>
                    {
                        new ExplodingBubbleEmitter { startTime = 0f, duration = 6f, interval = 1.5f, damage = 8, explosionDamage = 8, bubbleRadius = 0.4f, explosionRadius = 0.8f }
                    };
                    break;
                case "Pattern_AppearingBubbles": // attack 2
                    p.emitters = new List<BulletEmitter>
                    {
                        new AppearingBubbleEmitter { startTime = 0f, duration = 6f, interval = 1f, growDuration = 0.5f, holdDuration = 0.5f, damage = 8, explosionDamage = 8, bubbleRadius = 0.4f, explosionRadius = 0.8f }
                    };
                    break;
                case "Pattern_TargetedBubbles": // attack 3
                    p.emitters = new List<BulletEmitter>
                    {
                        new TargetedBubbleEmitter { startTime = 0f, duration = 6f, bubblesPerCycle = 3, bossRadius = 1f, damage = 8, bubbleRadius = 0.4f }
                    };
                    break;
            }
        }

        // =====================================================================
        //  Boss
        // =====================================================================
        private static void BuildBoss(Dictionary<string, BulletPatternSO> patterns)
        {
            var boss = LoadOrCreate<BossData>(Root + "/Bosses", "Boss_FirstGuardian");
            boss.bossName = "Buble";
            boss.maxHP = 100;
            boss.introLines = new List<DialogueLine>
            {
                new DialogueLine { speaker = "Guardian", text = "So... climbers. You reek of ambition.", autoAdvanceSeconds = 2.5f },
                new DialogueLine { speaker = "Guardian", text = "The Crystal Tower does not suffer the weak. Show me.", autoAdvanceSeconds = 2.5f }
            };
            boss.phases = new List<BossPhase>
            {
                new BossPhase { phaseName = "Phase 1", enterAtHealthFraction = 1f, attackRotation = new List<BulletPatternSO> { patterns["Pattern_Bubbles"], patterns["Pattern_ExplodingBubbles"], patterns["Pattern_AppearingBubbles"], patterns["Pattern_TargetedBubbles"] }, transitionLines = new List<DialogueLine>() },
                new BossPhase { phaseName = "Phase 2", enterAtHealthFraction = 0.5f, attackRotation = new List<BulletPatternSO> { patterns["Pattern_Spiral"], patterns["Pattern_Bubbles"] },
                    transitionLines = new List<DialogueLine> { new DialogueLine { speaker = "Guardian", text = "Enough games!", autoAdvanceSeconds = 1.5f } } }
            };
            EditorUtility.SetDirty(boss);
        }

        // =====================================================================
        //  Characters
        // =====================================================================
        private static void BuildCharacters(Dictionary<string, CardData> cards)
        {
            List<CardData> Deck(params string[] names)
            {
                var list = new List<CardData>();
                foreach (var n in names) if (cards.TryGetValue(n, out var c)) list.Add(c);
                return list;
            }

            // name = asset filename AND in-game name; tint = character-select background colour;
            // spriteName/dodgeIconName are PNGs in Assets/2CT/Art (null = leave whatever's assigned).
            void Character(string name, Color tint, string spriteName, string dodgeIconName, List<CardData> deck,
                           bool flipX = false, bool flipY = false, float speedX = 4.5f, float speedY = 4.5f)
            {
                var ch = LoadOrCreate<CharacterData>(Root + "/Characters", Safe(name));
                ch.characterName = name; ch.tint = tint; ch.maxHP = 30; ch.startingMana = 10; ch.manaPerRound = 10; ch.baseCardsPerRound = 3;
                ch.startingDeck = deck;
                ch.flipX = flipX; ch.flipY = flipY;
                ch.moveSpeed = new Vector2(speedX, speedY);
                if (!string.IsNullOrEmpty(spriteName))
                {
                    var sprite = LoadSprite(spriteName);
                    if (sprite != null) ch.baseSprite = sprite;
                    else Debug.LogWarning($"[2CT] Sprite '{spriteName}.png' not found in Assets/2CT/Art for character '{name}'.");
                }
                if (!string.IsNullOrEmpty(dodgeIconName))
                {
                    var icon = LoadSprite(dodgeIconName);
                    if (icon != null) ch.dodgeIcon = icon;
                    else Debug.LogWarning($"[2CT] Dodge icon '{dodgeIconName}.png' not found in Assets/2CT/Art for character '{name}'.");
                }
                EditorUtility.SetDirty(ch);
            }

            Character("Fiore", new Color(1f, 0.45f, 0.3f), "Fiore", "FioreSmall",
                Deck("Mana Bolt", "Mana Bolt", "Bolt of Flame", "Bolt of Flame", "Searing Power", "Shield Hex", "Loyal Blade", "Divination", "Mask of Wild Magic", "Lifestealer Aura"));
            Character("Wylta", new Color(0.85f, 0.85f, 0.9f), "Wylta", "WyltaSmall",
                Deck("Shield Hex", "Shield Hex", "Scorching Barrier", "Feral Growth", "Wound Be Gone", "Wound Be Gone", "Mana Bolt", "Loyal Blade", "Vine Strike", "Heart Starter"),
                flipX: true);
            Character("Leafy", new Color(0.45f, 0.85f, 0.35f), null, null,
                Deck("Preparation", "Divination", "Vine Strike", "Vine Strike", "Mana Bolt", "Loyal Blade", "Loyal Blade", "Mask of Wild Magic", "Lifestealer Aura", "Heart Starter"));
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static string Safe(string s) => s.Replace(" ", "").Replace("'", "");

        /// <summary>Load a Sprite by file name (no extension) from Assets/2CT/Art. Works for both
        /// single- and multiple-mode sprite textures (returns the first sprite sub-asset).</summary>
        private static Sprite LoadSprite(string fileNameNoExt)
        {
            string path = "Assets/2CT/Art/" + fileNameNoExt + ".png";
            foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (rep is Sprite s) return s;
            return AssetDatabase.LoadAssetAtPath<Sprite>(path); // single-mode fallback
        }

        private static List<T> FindAll<T>() where T : Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static T LoadOrCreate<T>(string folder, string name) where T : ScriptableObject
        {
            EnsureFolder(folder);
            var path = $"{folder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            return so;
        }

        private static void EnsureFolder(string folder)
        {
            // Use the physical filesystem, not AssetDatabase.IsValidFolder: inside
            // StartAssetEditing() a just-created folder isn't registered yet, so IsValidFolder
            // returns false and CreateFolder makes duplicate "Cards 1", "Cards 2"… folders.
            if (Directory.Exists(folder)) return;
            var parts = folder.Split('/');
            string cur = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!Directory.Exists(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}

namespace TwoCT.Core
{
    /// <summary>High-level state of the whole game session.</summary>
    public enum GameState
    {
        Lobby,
        FreeRoam,
        Combat,
        Reward,      // choosing cards / mythicals after a boss
        GameOver
    }

    /// <summary>The two alternating sub-phases of a combat encounter.</summary>
    public enum CombatPhase
    {
        Intro,       // boss says its opening lines
        Attack,      // players play cards
        Defend,      // bullet-hell survival
        Victory,
        Defeat,
        Reward       // post-victory card picks, before returning to the level
    }

    /// <summary>
    /// The school a card belongs to. Purely organisational for now (drives future rarity/pack
    /// weighting and deck filtering); rewards may currently offer any category. Extend freely.
    /// </summary>
    public enum CardCategory
    {
        Neutral,
        Incineration,
        Life,
        Wild,
        Severance,
        Bubble
    }

    /// <summary>Who a card is allowed to be aimed at when it is played.</summary>
    public enum TargetType
    {
        None,        // no target needed (self / enemy implied by effects)
        Self,        // always the caster
        Ally,        // pick any living ally (self allowed)
        AllyOrSelf,  // alias kept for readability in card authoring
        DeadAlly,    // pick a knocked-out ally (revive cards)
        Enemy        // the boss (single boss for now)
    }

    /// <summary>Card rarity, used purely for pack-generation weighting later.</summary>
    public enum CardRarity
    {
        Common,
        Uncommon,
        Rare
    }

    public enum MythicalKind
    {
        Passive,
        Active
    }
}

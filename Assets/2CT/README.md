# Climbing the Crystal Tower (2CT) — Prototype

A 3-player co-op pixel bullet-hell card roguelike, Unity 6000.3 (2D URP), Netcode for GameObjects.

This first pass implements the **combat core** deeply: the attack (cards) ⇄ defend (bullet-hell)
turn loop, boss phases + dialogue, and **client-sided bullets**, all data-driven via ScriptableObjects
with authoring tools. Free-roam, lobby UI, and post-boss rewards are scaffolded in the data model but
not yet wired (see Roadmap).

## Get it running (2 steps)

1. Open the project in Unity `6000.3.16f1`. Let Package Manager resolve **Netcode for GameObjects**.
   *(If the pinned version `2.4.4` in `Packages/manifest.json` isn't found, open Package Manager →
   install "Netcode for GameObjects"; it will pick a compatible version.)*
2. Menu **`2CT ▸ Scenes ▸ Build All Scenes`**. This generates all sample content (cards, mythicals,
   the first boss, bullet patterns, 3 characters + registry), builds the shared `Player` and
   `NetworkManager` prefabs, and creates **three wired scenes in `Assets/Scenes/`**: `Lobby`, `Level`, `Combat`.

**Full flow:** open `Assets/Scenes/Lobby.unity`, press **Play** → **Create Lobby (Host)** → pick a
character → **Ready** → **START RUN**. You spawn into the **Level** (free roam): walk with
**WASD/arrows/stick**, press **E** at the sign / gate / door. The group **Gate** (all players must
gather) and the **Portal** both lead into **Combat**:
- **Attack phase:** drag cards up into the play zone; some ask for a target. Click **End Turn**.
- **Defend phase:** move your icon with **WASD / arrows / left-stick** to dodge the bullets.

**3-player:** build/ParrelSync two more instances, **Join** `127.0.0.1` in the lobby.

**One scene in isolation:** open any scene and press Play — each has a dev panel to **Host** standalone.
Opening `Combat` directly: **Host**, then **Begin Encounter**.

## Tools (under the `2CT` menu)

| Menu | What it does |
|------|--------------|
| **Generate Sample Content** | Creates/updates every card, mythical, boss, pattern and character from the design doc as editable assets. Idempotent. |
| **Rebuild Content Registry** | Rescans the project and repopulates the runtime lookup (needed after adding assets). |
| **Scenes ▸ Build All Scenes** | Generates content + prefabs and builds Lobby, Level, Combat into `Assets/Scenes`. |
| **Scenes ▸ Build Lobby / Level / Combat Scene** | Build one scene individually. |
| **Bullet Pattern Preview** | Animated, in-editor preview of any `BulletPatternSO` (scrub / play / reseed). |

Authoring is inspector-first: `CardData`, `MythicalData` and `BulletPatternSO` have custom inspectors
with a typed **"Add Effect / Add Emitter" dropdown** — every new effect/emitter subclass shows up
automatically. Right-click ▸ Create ▸ **2CT** for the asset menu.

## Architecture

```
Assets/2CT/
  Scripts/
    Core/        interfaces + enums + shared structs (no Unity/Netcode coupling for card effects)
    Data/        ScriptableObjects: CardData, MythicalData, BossData, CharacterData,
                 Bullets/ (BulletPatternSO + emitters), Effects/ ([SerializeReference] CardEffects)
    Combat/      NGO: CombatManager (state machine + ICombatContext), PlayerCombatant, BossController,
                 Deck (plain C#), CombatEvents (UI/VFX bus), PlayerRegistry, PlayerAvatar
    Bullets/     DefendArena, PlayerDodgeIcon, Bullet, BulletSystem (client-sided sim)
    FreeRoam/    FreeRoamPlayer (move/wobble/flip), CameraFollow, ParallaxLayer, ScreenFader,
                 Portal (fade+scene load), Interactable + DialogueBox (solo & group dialogue)
    Lobby/       LobbyController (networked slots, unique pick, ready/start), LobbyUI (styled uGUI)
    Net/         ConnectionManager (host/join, 3-player cap), NetworkBootstrap (one persistent NM), DevControlPanel
    UI/          HandView + CardView (Hearthstone-style drag), CombatHUD (IMGUI)
  Editor/        SerializeReference dropdown, custom inspectors, content + scene generators, preview window
  Content/       generated ScriptableObject assets (safe to edit)
  Resources/     ContentRegistry.asset (runtime id → asset lookup)
  Prefabs/       Player, NetworkManager (built by the scene tool)

Assets/Scenes/   Lobby.unity, Level.unity, Combat.unity (generated, wired)
```

### Scene flow & networking
One **persistent NetworkManager** (a prefab spawned by `NetworkBootstrap` in each scene, kept via
DontDestroyOnLoad) drives everything, so scenes hand off cleanly: **Lobby → Level → Combat** are loaded
through NGO's `SceneManager`, and the player + connection persist across them. `SessionData` (static)
carries the lobby's character picks and "run in progress" flag into combat, which then auto-starts.
Each scene also contains its in-scene `NetworkObject`s (LobbyController / Portal+Interactable /
CombatManager+Boss) and can be opened standalone for testing.

### Key design decisions
- **Server-authoritative combat.** All HP/mana/shield/boss-HP live in `NetworkVariable`s written only
  by the host. Cards resolve on the server through `ICombatContext`, which centralises shared rules
  (spell-damage bonus, lifesteal, draw) so each effect stays a tiny declarative unit.
- **Client-sided bullets.** The server broadcasts only *pattern name + seed + duration*. Each client
  deterministically rebuilds the identical bullet schedule and simulates locally, hit-testing **only its
  own** dodge icon — so you're never hit by a bullet you didn't see. Hits are reported to the server,
  which applies damage-reduction / shield / Cheat Death and replicates HP.
- **Composable content.** A card is a list of `[SerializeReference]` effects; a bullet pattern is a list
  of emitters; a boss is health-gated phases each with a pattern rotation. No code needed to author
  content — and one new C# subclass extends the whole authoring surface.
- **No custom asmdefs** — everything compiles into `Assembly-CSharp` so all package APIs resolve
  automatically; editor tools live in the special `Editor/` folder.

### Rules implemented
30 HP default • 10 mana/round • draw 3 (cap 10) • played+unplayed reshuffle to bottom • "Deck is empty"
• KO at ≤0 HP + revived-must-survive-one-defend (Heart Starter bypasses) • Fire (5 dmg/stack at end of
attack round) • Enlarge • Lifesteal • Shields • damage reduction • 0.5s i-frames + blink • boss phases +
intro/transition dialogue • all listed base cards & mythicals.

## Done in this pass
- **Combat core** — attack (cards) ⇄ defend (client-sided bullet hell) loop, boss phases + dialogue, all base cards & mythicals.
- **Free roam (basic)** — movement wobble/flip, camera follow, parallax, portals with fade + scene load, solo & group dialogue, group-dialogue-triggers-combat.
- **Lobby (basic)** — host/join, unique character selection, ready/start → loads the level.
- **Tools** — sample-content generator, content registry, 3 scene builders, bullet-pattern preview.

## Roadmap (next)
- Lobby: UGS **Lobby + Relay** behind `ConnectionManager` (real code join / public server list), lobby codes, public/private.
- Disconnect handling: pause + wait-for-full-team + host migration.
- Post-boss rewards: card-pack picker (4×) and mythical picker (odd levels), multi-level progression, permadeath reset.
- Dialogue polish: cartoon "speaking" sprite deform, per-line speaker portraits.
- Advanced card triggers (e.g. Scorching Barrier's "if hit, apply Fire" is currently a plain shield).
- Real pixel art + VFX (hook `vfxKey` into a VFX library).
```

# Importing your sprites (2CT)

The prototype ships with **placeholder art** (colored circles/squares generated in code). Everything
that shows art reads a `Sprite` field on a ScriptableObject, so swapping in real pixel art is just:
**import the image → drag it onto the right field → press Play.** No code changes needed.

---

## 1. Import settings for pixel art

Drop your `.png` files anywhere under `Assets/` (e.g. make `Assets/2CT/Art/Characters/`). Select the
texture and set these in the Inspector, then **Apply**:

| Setting | Value |
|---|---|
| Texture Type | **Sprite (2D and UI)** |
| Sprite Mode | **Single** (one sprite per file) — or **Multiple** + Sprite Editor for a sheet |
| Pixels Per Unit | **match your art** (e.g. 16, 32, or 100). Higher PPU = smaller in world. |
| Filter Mode | **Point (no filter)** — keeps pixels crisp |
| Compression | **None** |
| Mesh Type | **Full Rect** (recommended for UI/cards) |
| Pivot | **Bottom** for field characters/boss (feet on the ground); **Center** for card art |

> Tip: to apply once to many files, select them all and edit settings together, or set up a
> **Presets** asset for one-click pixel-art import.

**Facing:** author all characters/boss **facing right**. Free-roam flipping is automatic
(`FreeRoamPlayer` mirrors the X scale when you change direction).

---

## 2. Where each sprite goes (and where it shows)

All of these are editable assets under `Assets/2CT/Content/…`. Select one and assign your sprite:

| Asset (Inspector field) | Where it appears | Wired? |
|---|---|---|
| **CharacterData** → `Base Sprite` (+ `Tint`) | The player's avatar in the combat line-up (left side). Falls back to a slot-colored circle if empty. | ✅ |
| **BossData** → `Sprite` | The boss on the right side of the battlefield. Falls back to a red square. | ✅ |
| **CardData** → `Artwork` | The art panel on the card in your hand. Hidden if empty. | ✅ |
| **MythicalData** → `Artwork` | Reserved for the post-boss mythical picker (reward UI not built yet). | ⏳ |

The **dodge icon** in the defend phase stays a small colored sphere on purpose (it's your abstract
hitbox, quarter-size). The **bullets** and the **arena box** also use generated shapes — those are
gameplay primitives, not character art. Say the word if you want any of them to take a custom sprite.

---

## 3. Step-by-step: give the Pyromancer a sprite

1. Import `pyromancer.png` (settings above), pivot **Bottom**.
2. In the Project window open `Assets/2CT/Content/Characters/Character_Pyromancer.asset`.
3. Drag `pyromancer.png` onto the **Base Sprite** field. (Optionally set **Tint** to white so the art
   shows its true colors.)
4. Press Play → in combat, Player 1 (Pyromancer) now shows your sprite instead of the red circle.

Same idea for the boss (`Content/Bosses/Boss_FirstGuardian` → **Sprite**) and any card
(`Content/Cards/Card_ManaBolt` → **Artwork**).

---

## 4. Adding brand-new content with art

- **New card:** right-click in the Project → **Create ▸ 2CT ▸ Card**. Set name, mana, effects
  (＋ Add Effect dropdown) and **Artwork**. Then run **`2CT ▸ Rebuild Content Registry`** so the
  runtime lookup and lobby/hand see it.
- **New character/boss/pattern/mythical:** same — **Create ▸ 2CT ▸ …**, fill it in, Rebuild Registry.

`Rebuild Content Registry` is required whenever you **add or delete** an asset (not for editing an
existing one). It rescans the project and repopulates `Assets/2CT/Resources/ContentRegistry.asset`,
which is how every client resolves art/data by id over the network.

---

## 5. Notes / gotchas

- **Animations:** the doc's design is intentionally animation-free — characters "wobble" and flip
  rather than play sprite animations. A single still sprite per character is all you need.
- **Transparency:** export PNGs with alpha; the importer keeps it.
- **Sorting:** field objects sort by vertical position already; you normally don't need to touch
  sorting orders. Card art renders inside the card, behind the text.
- **Size looks off?** adjust **Pixels Per Unit** on the texture (bigger PPU → smaller on screen), or
  the object's scale. The boss uses a 2.5× scale by default (tweak on the Boss object in the scene).

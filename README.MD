# ⚔️ ARena Clash — Game Rules 

A 2-player AR duel: each player picks 3 weapon cards from their own deck, and over 3 rounds they take turns attacking and defending with a single red-tip pen. Weapon classes beat each other rock-paper-scissors style, and defending means guessing the attack direction.

## Setup
- **Player A** plays the `PLAYER 1` card and **Player B** the `PLAYER 2` card.
- Each player has their own deck of 5 weapon cards, the same weapons in both:

  | | Light | Light | Heavy | Heavy | Defense |
  |---|---|---|---|---|---|
  | **Player A** | Sword | Dagger | Axe | Hammer | Shield |
  | **Player B** | Sword II | Dagger II | Axe II | Hammer II | Shield II |

- Each player **chooses 3 of their 5 cards** (no draw) and keeps them hidden from the other player. The other 2 cards are not used this game.
- Both players start with **100 HP**.

Both decks are identical, so neither player gets luckier cards. Having only one Shield is part of the strategy: you decide whether to take it and when to play it.

## Each round (×3)
Gestures are **recorded first, in secret**, and the fight only plays when someone presses **FIGHT**.

1. **Pick:** both players place one of their chosen cards face down and flip it at the same time. A card is only detected once it is face up, so the reveal happens automatically. Then each player slides their card next to their own character to equip it (Player A's cards only equip on `PLAYER 1`, Player B's on `PLAYER 2`). Keep only the cards in play on the table: the phone tracks at most 5 cards at once.
2. **Record, one player after the other** (the player who is not recording looks away):
   1. First attacker: **attack**, then **defense** (the direction they guess the other's attack comes from).
   2. The other player: **attack**, then **defense**.

   The pen only changes hands once per round. Shout while drawing an attack: the **war cry** adds up to ×1.5 damage and is played back so its loudest moment lands on that attack's hit (see `Docs/WarCry.md`).

   Each gesture: press **Detect pen** → hold the pen tip inside the square in the middle of the screen during the 3-2-1 (it only counts down while the tip is in the square, and starts over if it leaves; after 10 s without the pen it gives up) → *"DRAG!"* → drag the pen up, down, left or right (any speed). The recorded stroke is shown as an animated dotted arrow → **Redo** or **Confirm**.
3. **FIGHT:** a big *"ROUND n — FIGHT!"* button. Put the pen away and press it.
4. **Clash:** the first attacker swings, then the other one (a K.O. cancels all remaining attacks). A defender who guessed the direction holds a block (vertical swing) or a crouch (side swing) for the whole swing. HP drops when the blade connects, and the damage floats out beside the defender, away from the attacker and clear of the name (yellow → orange with the amount; red is kept for the K.O.), tagged **CRITICAL!** (class advantage) or **Not very effective...** (disadvantage); a full block shows **BLOCKED**; hammer blocks also show penetrating damage, and weapon abilities are labelled on each strike. On a K.O. the big **K.O.!** and the winner's name under it appear at once, the K.O. sound fades into the victory music after 0.7 s and the buttons come at 1 s. The two bodies never walk into each other during the swings (`PlayerAvatar.minBodyGap`).
5. **Next round** button. The cards used this round are out of the game. A **K.O.** shows a big *K.O.!* and ends the game at once; after round 3 the winner is announced in the middle of the screen.

## Who attacks first
- Round 1: Player A.
- Rounds 2 and 3: whoever has **less HP**. If HP is tied, the order of the previous round swaps.

The first attacker also records first.

## End of the game
- A player wins by bringing the other to **0 HP**, or by having **more HP** after the 3 rounds.
- If HP is tied, the player who blocked more attacks wins. If that is also tied, it's a draw.

## Damage and weapon abilities

Both players start with **100 HP**. Damage is now lower to give blocks and weapon abilities more room over three rounds.

| Weapon | Class | Base damage | Ability |
|---|---|---:|---|
| Sword | Light | 25 | Counterattack |
| Dagger | Light | 25 | Double Strike |
| Axe | Heavy | 30 | Cleave |
| Hammer | Heavy | 30 | Crushing Blow |
| Shield | Defense | 15 | Axis Guard |

The regular and II decks have identical stats and abilities.

**Normal damage = base damage × class multiplier × war cry**, rounded to the nearest integer (halves round up).

- Class advantage: **×1.5**, Light → Heavy → Defense → Light.
- Class disadvantage: **×0.6**, the reverse cycle. Same class: **×1**.
- **War cry is unchanged**: ×1 to ×1.5, with the same microphone thresholds, capture and audio playback. For the dagger, shout during the **first** gesture; for the axe, during its **single** gesture: that same multiplier applies to the whole attack budget. Only the dagger asks for a second gesture to choose its other direction.
- Each direction is resolved separately. A normal block requires the exact same direction and deals zero damage, except for the hammer.

### Shield × Axis Guard

A defense Up **or** Down blocks **both Up and Down**. A defense Left **or** Right blocks **both Left and Right**. It does not protect the other axis. This also applies separately to each dagger or axe strike.

### Dagger × Double Strike

Record and confirm **two different attack directions** before recording defense. Opposite or adjacent directions are allowed. Each receives 50% of the normal damage budget and can be blocked independently. Since HP is integer, an odd budget is split with the extra point on the second strike (25 → 12 + 13). Combined damage never exceeds normal damage.

### Axe - Cleave

Draw and confirm **one gesture only**. The axe automatically attacks in that direction for **70%** of normal damage, then in the **opposite direction** for **30%**: Up/Down or Left/Right. No second gesture is requested. The first allocation is rounded and the second gets the remainder (30 -> 21 + 9), so the combined damage never exceeds normal damage including class and war cry. Each strike is evaluated separately: an ordinary defense can block one and let the other hit; a shield guarding that axis blocks both.


### Hammer × Crushing Blow

An unblocked hit deals normal damage with no extra bonus. A correctly blocked hit still deals **25% of normal damage**, rounded. It remains a successful block for scoring and sword counterattacks. Example against another heavy weapon: 30 normally, **8 blocked**; with full war cry: 45 normally, **11 blocked**.

### Sword × Counterattack

Each successfully blocked enemy strike triggers an immediate counterattack for **20% of the damage that strike would have dealt before blocking**, rounded. This includes the enemy's class multiplier, war cry and split allocation. Counterattacks are automatic, cannot be blocked, receive no additional multiplier and **cannot trigger another counterattack**. Blocking a hammer still counters its full pre-block damage, even though 25% penetrates the defense. A lethal hit prevents a dead defender from countering.

### Resolution and UI

The first player resolves their strikes in order, with sword counters immediately after the corresponding block; then the other player's normal attack follows. Any KO cancels all remaining strikes and counters. Each blocked direction counts once towards the block tiebreak.

A dedicated gold-bordered weapon-power panel shows each ability during selection and recording, separately from pen and war cry instructions. Axe review previews both opposite directions with 70%/30%; only the dagger requests a second direction. During each strike, the panel highlights the ability, direction, damage share, block/hit result and HP damage. During the clash, floating text identifies Double Strike/Cleave hit 1/2 and its direction, shield blocks, Crushing Blow damage through blocks and sword counterattacks. War cry audio plays once per normal attack, not once per split strike or counter.

Examples without war cry:
- Axe versus Shield: normal budget **45**, split **32 + 13**. A vertical shield guard blocks both vertical strikes; a horizontal guard lets both through.
- Dagger versus Hammer: normal budget **38**, split **19 + 19**, independently blocked.
- Hammer versus Sword: normal **18**, blocked **5**, followed by a sword counter for **4** if the sword wielder survives.
- Shield versus Sword: normal **23**.

## Roadmap

### ✅ Done
- [x] Vuforia database with 12 cards: `player1`, `player2`, Player A's deck (`sword`, `axe`, `dagger`, `hammer`, `shield`) and Player B's deck (`sword2`, `axe2`, `dagger2`, `hammer2`, `shield2`). Card labels in English. At most **5 are tracked at the same time** (`VuforiaArenaSetup.MaxTrackedCards`) so the phone doesn't stutter.
- [x] Player B's II cards use their own 3D models (`Sword2_Container`, `Axe2_Container`, `Dagger2_Container`, `Hammer2_Container`, `Shield2_Container`) with the same `WeaponStats` as Player A's.
- [x] Characters and weapons appear on their cards, scaled to the card size.
- [x] Fighting stage (`ArenaStage`, model `Assets/Arena/old-fighting-stage`): while both player cards are seen, the stage stands between them, turned so the players face each other across it and sized from the distance between the cards (`stageWidthPerGap`). The characters stand on its floor. The model is assigned in `CardWeaponManager.arenaPrefab`.
- [x] Equipping: a weapon card held next to a player card for 0.8 s goes to that character's hand.
- [x] Per-weapon pose in the hand (`WeaponGrip` component on the weapon prefab).
- [x] Pen tracking (`ColorGestureTracker`, by Marko): colour blob tracking with motion/background filtering, settling time, marker and trail, EN/PT **Settings** window (language, colour calibration, marker/trail). Reads 4 swipes (up, down, left, right). In a duel it only processes the camera while a gesture is being drawn.
- [x] Combat animations in `Assets/Animations/PlayerCombat.controller` (Mixamo, humanoid, baked in place so the character stays on its card):

  | Action | Trigger | Animation |
  |---|---|---|
  | Attack up | `AttackUp` | Down_up_Slash |
  | Attack down | `AttackDown` | Up_down_Slash |
  | Attack left | `AttackLeft` | Right_to_left_slash |
  | Attack right | `AttackRight` | Left_to_right_slash |
  | Block a vertical attack | `Block` | Block_standing |
  | Dodge a side attack | `Crouch` | Crouch |
  | KO | `Die` | Death (stays down) |

  Same slashes for every weapon; the axe and the hammer play them at 80% speed. Block and Crouch are held while the `Guarding` bool is true. Code: `PlayerAvatar.TryAttack`, `StartGuard` / `EndGuard`, `PlayDeath`.
- [x] Import rules for every FBX in `Assets/Animations` (`AnimationImporter`): humanoid rig, whole take, only the Idle loops.
- [x] Player customisation (name and armour).
- [x] Testing without a webcam: Vuforia Simulator plus the `Simulator Cards` layout (**Arena → Criar cartoes do simulador**).
- [x] Testing without the pen (Editor only, `KeyboardCombatInput` on `Vuforia Arena`): after pressing **Detect pen**, an **arrow key** counts as the pen stroke.
- [x] English / Portuguese for the player setup and the duel HUD (`Localization`, switch in **Settings**).

### ✅ 1. Weapon data and class icons
- [x] `WeaponStats` component on each weapon prefab: class (Light / Heavy / Defense), base damage. Values from the **Damage** table above. `WeaponStats.StrongAgainst` / `WeakAgainst` hold the class cycle.
- [x] Three class symbols in `Assets/Images/Symbol`: `Light_Symbol` (sword), `Heavy_Symbol` (axe), `Shield_Symbol`.
- [x] Icons floating over each weapon on its card, always facing the camera (`WeaponIcons`, added by `CardWeaponManager`):
  - **below the weapon, in white:** the icon of its own class;
  - **above the weapon, in green:** the class it is strong against;
  - **above the weapon, in red:** the class it is weak against.

  Example: the Axe shows the Heavy icon below it, the Defense icon in green and the Light icon in red above it.
- [x] The icons hide with the card's weapon once it is in a character's hand.

### 2. Combat core
- [x] Health (100 HP) for each player, with a health bar above the character next to the name label.
- [x] Damage calculation (base damage × class multiplier, 0 when blocked), as described in **Damage**.
- [x] Defense gesture: the pen gesture is stored as the defender's block direction.
- [x] Hit result floating from the defender: the damage plus CRITICAL! / Not very effective... / BLOCKED / K.O.!, with the defender holding a guard (`StartGuard`/`EndGuard`) on a block and `PlayDeath` at 0 HP.
- [ ] Test and tune the red-tip pen on a real device.

### 3. Turn and round flow
- [x] Game state machine: pick weapons → A records attack + defense → B records attack + defense → FIGHT → clash → next round → game over (see phase 6).
- [x] On-screen prompts for each step ("Player A: record your attack and your defense. Player B, don't look!", …).
- [x] The pen panel is hidden in the duel, so no direction is ever shown.
- [x] Remove the "Switch player" button: the game decides whose turn it is.
- [x] Turn order: A first in round 1, then the player with less HP (a tie swaps the previous order).
- [x] Cards used in a round can't be equipped again in the same game.
- [x] Each deck only equips onto its own character: cards without "2" on `PLAYER 1`, II cards on `PLAYER 2`.
- [x] Game over screen: a big K.O.! (winner named below) or "X wins!" by HP, tie-break by blocks, then draw. "Play again" and "Main menu" buttons, no extra result screen.

### 4. Feedback and polish
- [x] Floating damage numbers (`FloatingCombatText`), coloured by damage, with CRITICAL / Not very effective / BLOCKED / K.O. tags.
- [ ] Spark effect on a block.
- [ ] Hit reaction animation on the defender.
- [ ] Sound per weapon (swing, hit, block).
- [ ] Short in-game tutorial / rules screen.

### 5. Device and playtesting
- [ ] Android build tested on a phone with the printed cards.
- [ ] Playtest sessions to balance the numbers (damage, multipliers).
- [ ] Print sheet with all 12 cards at the right size (6.3 cm wide), the English versions.

### 6. Playtest 1 improvements
Feedback from the first phone test. Full plan and status: [Docs/Playtest1_Improvements.md](Docs/Playtest1_Improvements.md).
- [x] Pen input you can control: **Draw** → 3-2-1 → the pen is armed for 3 s → one swipe → **Redo** / **Confirm**. Moving the pen at any other time does nothing, and the direction is never shown.
- [x] Record first, fight later: each player secretly records attack + defense on their turn; nothing plays until the big **"ROUND n — FIGHT!"** button.
- [x] Clash choreography (`ClashDirector`): wind-up, slower swings (×0.7, heavy weapons slower still), the defender's block/crouch held for the whole swing, a short hit-stop on impact, HP drops when the blade connects.
- [x] At most 5 cards tracked at once (performance on the phone).
- [ ] Test on the phone (pen capture window, countdown, speeds).
- [x] Sound effects and music (`GameAudio`): menu and duel music, clicks, 3-2-1, swing, hit, block, K.O., victory, equip. Files and names in `Assets/Resources/Audio/README.md`.
- [ ] Game feel: announcer, hit/block particles, vibration (covers phase 4).
- [ ] Hit-reaction animation (needs a Mixamo "Hit Reaction" clip).
- [x] Colour calibration on the phone, mirror X / flip Y, saved settings (Marko's tracker).
- [x] Pen square: **Detect pen** → 3-2-1 with the pen held in a square → drag in any direction at any speed (`PenStroke`). Replaces the fast-swipe window.
- [x] The recorded stroke is shown as an animated dotted arrow before **Confirm**.
- [x] Recording per player: A attack → A defense → B attack → B defense (the pen changes hands once).
- [x] Cleaner HUD: no top status panels (health stays above each character), buttons left/right of the prompt, small round counter.
- [x] Bigger class icons on the cards (half the weapon's height); the corner class legend was removed as redundant.
- [x] Larger, near-white second line in the prompt; the war cry multiplier and the seconds left sit on the headline, always written "x1.25".
- [x] Red screen flash when a blade hits (none on a block, which costs no HP).
- [x] Player colours: player A cyan, player B magenta, on the name above the character and on the names in the prompts and the winner banner.

### Later ideas (not planned yet)
- Special ability per card.
- Gesture quality (speed and length of the stroke) as a damage multiplier.
- Arena marker that changes the rules of a match.
- Two pens (red and blue), one per player.

## Turn-flow implementation and validation

`DuelMatch` holds the three-round rules, health, blocks, card ownership, consumed cards and each round's secret gestures (four `Record` → `ConfirmStep` steps, attack then defense for each player → `Fight` resolves both attacks → `FinishClash` → `Continue`). `DuelController` starts after player customization and controls the HUD, the Detect pen / Redo / Confirm capture (it reads `ColorGestureTracker.Tip` and classifies the stroke with `PenStroke`) and the FIGHT button; `ClashDirector` plays the animations. A card locks when equipped; both players must equip before recording starts. Choose your three-card hand physically before the match, as in Setup above. Both player cards must be visible to press FIGHT.

Health bars (no numbers) appear under each name above the characters; they only drop when the blade connects and turn red at 25 HP or less. Each name is in its player's colour (A cyan, B magenta), above the character and in the prompts. The prompt is a short headline ("Ana: draw your attack") with a second line under it ("Bruno, don't look!"). While drawing an attack the headline shows the cry multiplier in gold and the seconds left ("Shout for more damage! x1.25 · 3 s"). During the duel the player characters stay put when a hand covers their card: a covered or jumping card is placed from the other player card, or frozen if both are covered (`CardWeaponManager`, *Duel stability*). Both cards lie on one table, so a card whose pose tilts away from the other one (the pen over it) is never trusted, however long it lasts (`tableTilt`). Weapon cards that are not equipped only show their weapon and class icons while picking. The FIGHT button is a pulsing gold button above the prompt; pressing it shows "ROUND n" for a moment. The war cry lightning on the weapon is in the player's colour and stays for the whole round. The duel music is quieter than the menu music (`GameAudio.duelMusicVolume`) and drops further while the microphone records a cry. The recorded direction is shown only to the player who drew it, before Confirm. **Play again** resets HP, blocks and used cards, keeping the customized characters.

Run the standalone rules tests without a .NET SDK using Unity's bundled compiler/runtime:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Run-DuelMatchTests.ps1
```

For another Unity installation, pass `-UnityEditor "path/to/Editor"`. Tests cover the README damage examples, all class pairings, turn order, recording order and redo, the FIGHT step, card restrictions, KO (cancels the counterattack), three-round progression and block tiebreaks. Webcam/pen recognition, animation timing, UI framing and Android still require Play Mode/device validation.

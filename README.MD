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
1. **Pick:** both players place one of their chosen cards face down and flip it at the same time. A card is only detected once it is face up, so the reveal happens automatically. Then each player slides their card next to their own character to equip it (Player A's cards only equip on `PLAYER 1`, Player B's on `PLAYER 2`).
2. **Attack 1** (A attacks, B defends):
   1. The screen shows *"Player B: choose your defense. Player A: don't look!"*.
   2. B makes the defense gesture with the pen (up, down, left or right). The screen only confirms *"Defense chosen"* and **does not show the direction**.
   3. The screen shows *"Player A: attack!"*. B hands over the pen and A makes the attack gesture.
   4. A's character plays the attack animation. The screen shows whether it was blocked and how much damage it did.
3. **Attack 2** (B attacks, A defends): same thing with the roles swapped.
4. The cards used this round are out of the game.

## Who attacks first
- Round 1: Player A.
- Round 2: Player B.
- Round 3: whoever has **less HP**. If HP is tied, Player B attacks first.

## End of the game
- A player wins by bringing the other to **0 HP**, or by having **more HP** after the 3 rounds.
- If HP is tied, the player who blocked more attacks wins. If that is also tied, it's a draw.

## Damage

**Damage = base damage × class multiplier × block**

| Weapon | Class | Base damage | Damage that gets through when blocked |
|---|---|---|---|
| Sword, Dagger | Light | 20 | 40% |
| Axe, Hammer | Heavy | 25 | 50% |
| Shield | Defense | 10 | 10% |

- **Base damage** comes from the attacker's weapon.
- **Block** comes from the defender's weapon, and only applies if the defense direction **matches** the attack direction.

**Class multiplier** (attacker's weapon against the defender's weapon):
- **×1.5** with advantage: Light → Heavy, Heavy → Defense, Defense → Light.
- **×0.6** at a disadvantage: the reverse.
- **×1** for the same class.

Damage is rounded to the nearest whole number.

**Examples**
- Axe vs Shield, not blocked: 25 × 1.5 = **38**.
- Sword vs Shield, blocked: 20 × 0.6 × 10% = **1**.
- Shield vs Sword, not blocked: 10 × 1.5 = **15**.
- Hammer vs Axe, blocked: 25 × 1 × 50% = **13**.

## Roadmap

### ✅ Done
- [x] Vuforia tracking of 12 cards at once: `player1`, `player2`, Player A's deck (`sword`, `axe`, `dagger`, `hammer`, `shield`) and Player B's deck (`sword2`, `axe2`, `dagger2`, `hammer2`, `shield2`). Card labels in English.
- [x] Player B's II cards use their own 3D models (`Sword2_Container`, `Axe2_Container`, `Dagger2_Container`, `Hammer2_Container`, `Shield2_Container`) with the same `WeaponStats` as Player A's.
- [x] Characters and weapons appear on their cards, scaled to the card size.
- [x] Equipping: a weapon card held next to a player card for 0.8 s goes to that character's hand.
- [x] Per-weapon pose in the hand (`WeaponGrip` component on the weapon prefab).
- [x] Red-tip pen tracking that reads 4 gestures (up, down, left, right). *Not tested on device yet.*
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

  Same slashes for every weapon; the axe and the hammer play them at 80% speed. Code: `PlayerAvatar.TryAttack`, `PlayDefense`, `PlayDeath`.
- [x] Import rules for every FBX in `Assets/Animations` (`AnimationImporter`): humanoid rig, whole take, only the Idle loops.
- [x] Player customisation (name and armour).
- [x] Testing without a webcam: Vuforia Simulator plus the `Simulator Cards` layout (**Arena → Criar cartoes do simulador**).
- [x] Testing without the pen (Editor only, `KeyboardCombatInput` on `Vuforia Arena`): **1/2** pick the player, **arrows** attack, **Shift+arrows** defend, **K** die, **R** back to Idle.

### ✅ 1. Weapon data and class icons
- [x] `WeaponStats` component on each weapon prefab: class (Light / Heavy / Defense), base damage, block %. Values from the **Damage** table above. `WeaponStats.StrongAgainst` / `WeakAgainst` hold the class cycle.
- [x] Three class symbols in `Assets/Images/Symbol`: `Light_Symbol` (sword), `Heavy_Symbol` (axe), `Shield_Symbol`.
- [x] Icons floating over each weapon on its card, always facing the camera (`WeaponIcons`, added by `CardWeaponManager`):
  - **below the weapon, in white:** the icon of its own class;
  - **above the weapon, in green:** the class it is strong against;
  - **above the weapon, in red:** the class it is weak against.

  Example: the Axe shows the Heavy icon below it, the Defense icon in green and the Light icon in red above it.
- [x] The icons hide with the card's weapon once it is in a character's hand.

### 2. Combat core
- [ ] Health (100 HP) for each player, with a health bar above the character next to the name label.
- [ ] Damage calculation (base damage × class multiplier × block), as described in **Damage**.
- [ ] Defense gesture: the pen gesture is stored as the defender's block direction.
- [ ] Hit result on screen: "Blocked!" or "Hit!" plus the damage, with the defender playing `PlayDefense` on a block and `PlayDeath` at 0 HP.
- [ ] Test and tune the red-tip pen on a real device.

### 3. Turn and round flow
- [ ] Game state machine: pick weapons → attack 1 (defend, then attack) → attack 2 → next round → game over.
- [ ] On-screen prompts for each step ("Player B: choose your defense. Player A: don't look!", "Player A: attack!", …).
- [ ] Pen panel: hide *"Last attack: …"* during the defense so it doesn't give away the direction.
- [ ] Remove the "Switch player" button: the game decides whose turn it is.
- [ ] Turn order: A first in round 1, B in round 2, the player with less HP in round 3.
- [ ] Cards used in a round can't be equipped again in the same game.
- [ ] Each deck only equips onto its own character: cards without "2" on `PLAYER 1`, II cards on `PLAYER 2` (today any weapon equips on the closest player).
- [ ] Game over screen: winner by KO or by HP, tie-break by blocks, then draw. "Play again" button.

### 4. Feedback and polish
- [ ] Floating damage numbers and a spark effect on a block.
- [ ] Hit reaction animation on the defender.
- [ ] Sound per weapon (swing, hit, block).
- [ ] Short in-game tutorial / rules screen.

### 5. Device and playtesting
- [ ] Android build tested on a phone with the printed cards.
- [ ] Playtest sessions to balance the numbers (damage, block %, multipliers).
- [ ] Print sheet with all 12 cards at the right size (6.3 cm wide), the English versions.

### Later ideas (not planned yet)
- Special ability per card.
- Gesture quality (speed and length of the stroke) as a damage multiplier.
- Arena marker that changes the rules of a match.
- Two pens (red and blue), one per player.

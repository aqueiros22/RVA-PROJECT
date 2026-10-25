# ⚔️ ARena Clash — Game Rules 

A 2-player AR duel: each player draws weapon cards, and over 3 rounds they take turns attacking and defending with a single red-tip pen. Weapon classes beat each other rock-paper-scissors style, and defending means guessing the attack direction.

## Setup
- Each player shuffles and draws **3 weapon cards**. If all 3 are from the same class, swap one.
- Both players start with **100 HP**.

## Each round (×3)
1. **Pick:** both players place one card face down and flip it at the same time. A card is only detected once it is face up, so the reveal happens automatically. Then each player slides their card next to their own character to equip it.
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

## Changes needed in the project
- The pen panel currently shows *"Last attack: …"*. It has to stay hidden during the defense so it doesn't give away the direction.
- The "Switch player" button is no longer needed: the game says whose turn it is.

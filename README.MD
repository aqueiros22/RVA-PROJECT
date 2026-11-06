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


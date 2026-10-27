# Playtest 1 — Improvement plan and status

Feedback from the first test on the phone, and how it was addressed (branch `integration/gameplay-pen`, merge of `gameplay` + `marko/pen-tracking`).

## What players said

| # | Problem | Cause | Status |
|---|---|---|---|
| 1 | Choosing with the pen is too hard: as soon as the pen comes near the camera it already fires, and there is no way to redo it. | The tracker sent a gesture the moment the tip moved, and the duel applied it straight away. | Fixed (step 1) |
| 2 | The hit happens while the hand is still in front of the camera. | The attack was resolved and animated in the same frame the gesture was read. | Fixed (step 2) |
| 3 | The attacks are too fast. | Each attack was resolved alone, right after its gesture, with no build-up. | Fixed (step 3) — tune on the phone |
| 4 | The block animation is too short: block and attack don't connect. | The defense played 0.35 s after the attack and the clips are ~0.6 s long. | Fixed (step 3) |
| 5 | The game feels flat: no sound, no effects, no music. | No audio or particle assets in the project. | To do (step 4) |
| — | The phone stutters with many cards. | Vuforia was tracking all 12 cards at once. | Fixed: at most 5 tracked |

## Round flow (now in the README)
1. **Pick** — reveal and equip one card each.
2. **First attacker records** (the other looks away): **Draw attack** → 3-2-1 → *"DRAW NOW!"* → swipe → *"Recorded"* → **Redo** / **Confirm**; same for **Draw defense**; **Done**.
3. **Second player records** the same way.
4. **"ROUND n — FIGHT!"** button in the middle of the screen.
5. **Clash** — both attacks in turn order (a K.O. cancels the counterattack).
6. **Result** → **Next round**.

## What was implemented

### Step 1 — Pen input you can control and redo ✅
- `ColorGestureTracker` (Marko's tracker): `Arm(seconds)` / `Disarm()`; only a swipe made while armed counts, one per `Arm`, reported through the `GestureCaptured` event. Marko's detection, settling time and filtering are unchanged. In a duel the camera image is only processed while armed or while Settings is open (saves CPU on the phone).
- `DuelController`: Draw → countdown (`countdownSeconds`, 3) → armed (`captureSeconds`, 3) → Redo / Confirm. Timeout → *"No gesture detected - try again."*
- `KeyboardCombatInput` (Editor): an arrow key counts as the swipe while drawing.

### Step 2 — Record first, FIGHT later ✅
- `DuelMatch`: phases `Pick → Record → Fight → Clash → Result → Pick | GameOver`; `Record`, `FinishRecording`, `Fight` (returns both `Exchange`s), `FinishClash`, `Continue`. Damage, blocks, turn order and winner rules unchanged.
- `Tests/DuelMatchTests.cs` updated (210 checks pass).

### Step 3 — Clash choreography ✅
- `ClashDirector`: wind-up (0.5 s) → swing at ×0.7 speed (heavy weapons already ×0.8) → the defender's guard starts with the swing and is held until it ends → hit-stop (0.12 s) at 45% of the swing, HP drops on screen → pause (0.9 s) → next exchange. All timings are in the Inspector on `Vuforia Arena`.
- `PlayerCombat.controller`: Block and Crouch stay while the bool `Guarding` is true.
- `PlayerAvatar`: `StartGuard` / `EndGuard`, swing speed scale, hit-stop pause.

### Also in the merge
- EN/PT (`Localization`, Marko): player setup and duel HUD; switch in **Settings** (top right of the HUD), which also has colour calibration and marker/trail toggles.
- At most 5 cards tracked at once (`VuforiaArenaSetup.MaxTrackedCards`, also written to `VuforiaConfiguration.asset`). Keep only the cards in play on the table.
- `.utmp/` (Android build cache) and `.idea/` removed from git and ignored.

## Still to do

### Step 4 — Game feel
- **Audio** (`Assets/Audio`, an `AudioManager`): light/heavy swing, block, hit, KO, equip, UI click, countdown beeps; announcer ("Round 1", "Fight!", "K.O.!"); calm music for pick/setup, battle loop for the clash, victory sting. CC0 sources (Kenney, freesound CC0, OpenGameArt), licences in `Assets/Audio/CREDITS.md`.
- **Particles** (`Assets/VFX`): sparks on block, red burst on hit, dust on KO, glow on equip; floating damage numbers.
- **Feedback**: phone vibration on hit, HUD shake, HP bar with a delayed "ghost" bar.
- **Hit reaction**: a Mixamo "Hit Reaction" clip played at the impact.

### Step 5 — Tuning and playtest 2
- On the phone: capture window (3 s?), countdown, `gestureDistance`/`minSwipeSpeed`, clash speed and pauses.
- Colour calibration on the phone: **Calibrate Color** only works with the PC webcam today (it reads `WebCamTexture`, not the Vuforia camera image).
- Playtest 2 with two players; write the feedback in this folder (`Playtest2_*.md`).

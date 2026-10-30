# Game sounds

`GameAudio` loads every clip in this folder by file name. To change a sound, replace the file and keep the name.
For several versions of one sound, number them (`hit_1`, `hit_2`, ...): one is picked at random each time.
A name with no file is simply silent. The war cry is not here: it is the player's own shout from the microphone.

| Name | When it plays | Current file |
|---|---|---|
| `music_menu` | main menu and character setup (loop) | music_menu.mp3 |
| `music_battle` | the duel (loop, quieter during the clash) | music_battle.mp3 |
| `click` | any button, and each number of the 3-2-1 | click.mp3 |
| `go` | when DRAG! appears | (none yet) |
| `swing` | an attack starts its swing | swing.wav (Blood Echo "Swing") |
| `hit` | an attack that connects | hit_1.mp3, hit_2.wav, hit_3.wav (Blood Echo "BodyImpact1/2") |
| `hit_critical` | a hit with class advantage; falls back to `hit` | (none yet) |
| `block` | the defender guessed the direction | block_1.mp3, block_2.wav (Blood Echo "parry1") |
| `ko` | the K.O. hit | ko.mp3 |
| `ko_fall` | 0.4 s after the K.O., the body falling | ko_fall.wav (Blood Echo "BodyDrop") |
| `victory` | the winner is announced | victory.mp3 |
| `equip` | a weapon goes into a character's hand | equip.wav (Blood Echo "BodyRoll") |

Music files are imported as *Streaming* so long tracks do not fill the memory.

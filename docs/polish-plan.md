# Polishing the game

Written 2026-10-16, after the UI revamp's first pass (`ui-revamp-plan.md`) and bogz asking for the game to be polished.
The plumbing is done and the screens have one look now. What's left between this and something that feels finished is
mostly feel, how a hit lands, how a screen arrives, what you hear, and a handful of rough edges seen on the way.

## What's rough, by how much it costs the feel

1. **There is no sound at all.** Not in the engine, not in the game. A hit, a block, a parry, a K.O., the round being
   called, a cursor moving on the roster, all silent. Nothing else on this list does as much for how the game feels.
2. **Screens arrive in pieces.** The setup screen shows black for a moment before its map has loaded, the main menu's
   duel pops in once the map is there, the stage select slid out on the first frame (fixed). The rot transition into a
   fight is black blotches over glass. The setup screen's covered frame into a fight is about a tenth of a second.
3. **The fight's moments are flat.** A K.O. is the same frame rate as a jab, the round calls are a glass box with a word
   in it, the clock doesn't say it's running out, the health bars are plain red strips with no face next to them, the
   combo counter is text.
4. **The roster is small.** A cell is 170 by 146 with the whole fighter in the middle at a third of its height, the
   faces can't be made out. Two players' cursors are told apart by the tags above the cell only.
5. **Things not looked at yet in the new look.** The result card, the join screen's keyboard, the bindings screen over
   the options, the content transfer, an online lobby.
6. **Small ones.** The menu duel is drawn over the map and not lit by it. The controls card's columns are ragged by
   the widths of the key caps. The options' description panel is mostly empty.

## The order to do it in

### 1. Sound (the engine first)

The engine gets an audio device the way it has a graphics one, `Horizon/Audio`, on OpenAL through Silk.NET (the game
already depends on Silk.NET, OpenAL Soft is the native library and is LGPL, shipped as a dll next to the exe), with
sounds loaded from .ogg and .wav, played as one shots or loops, a few buses (music, effects, UI) with their own volume,
a cap on how many of one sound play at once (a combo of twelve hits is not twelve copies stacked), and nothing played
from the render thread. The options get the three volumes. Then the game, a sound for every kind of hit result (hit,
counter hit, punish, blocked, parried, whiffed) pitched a little by the damage, the K.O., the round calls, footsteps
and landings off the movement, the menu cursor, a pick, back, lock in, and a loop of rain under the Japan map. Music
for the menus and the fight if a CC0 one fits. The sounds come off kenney.nl (Impact Sounds, Interface Sounds, CC0) and
OpenGameArt, each with its README, the way the art does.

### 2. Screens that arrive whole

The map preview fades its map in when it's loaded instead of showing black (and the setup screen asks for the map as
early as the main menu, which already has Japan up), the main menu's duel fades in on its floor, and the transition into
a fight is looked at again over the new look (the rot or a blur, bogz's options already offer both).

### 3. The fight's moments

A K.O. slows the fight down for a moment and holds the camera on it, the round calls slide and settle rather than
appearing, the clock pulses red under ten seconds, a face (the portrait out of the fighter's atlas, the setup screen
already draws one) next to each health bar, round pips that fill, the combo counter punches on every hit.

### 4. The roster

Cells show the fighter cropped to the chest up and twice the size, each player's cursor in their own colour round the
cell (blue and red, the duel's corners), a flash on lock in.

### 5. Everything else looked at

The result card, the join screen, the bindings over the options, the content transfer, the lobby, each screenshotted in
the new look and fixed where it's off. The menu duel lit by the map, the controls card lined up, the options' about
panel given something to say or made smaller.

## Decided with bogz (2026-10-16)

- **Sound, and music too.** The engine gets audio, the game its sounds and music off CC0 packs.
- **The look and feel first**, steps 2 to 5 above, and the sound after them.

## Where it stands (2026-10-16, the look and feel)

- **Screens arrive whole.** The stage select no longer slides out on the first frame of the setup screen (it is put
  away instantly as the screen is built). The first map a preview shows comes up out of the dark (`MapPreview.FadeIn`,
  the map's opacity), the main menu's duel fades in once it's stood on the floor. The transition into a fight is
  bogz's own option (Mixed rots it in), left as it is.
- **The fight's moments.** A K.O. holds for the rules' hitstop and then the launch plays at a third of the speed and
  comes back up (`FightScene.Moments.cs`, `SceneManager.TimeScale`, offline only, put back on a pause and when the scene
  goes). The calls across the screen are a band the width of the screen. Each health bar has the fighter's face next to
  it, chest up out of the first frame of their idle in a window of glass (`HealthDisplay`). The clock already went red
  and thumped under ten. The frame bars get out of the way of the result card.
- **The roster** shows every fighter chest up and big in a clipping window (`fighter_cell.hor`).
- **Looked at.** The result card (gold fret, reads), the join screen, the bindings. The setup corners are a strip of
  glass under the fighter, the fighter and the blurb on a faint wash over the map.
- **Tried and left.** The menu duel in the map's own light. In `MapPreview`'s renderer nothing of it is drawn at all,
  made up front or made late, so it stays in the scene's canvas over the map.

Next is the sound, step 1 above.

## Where it stands (2026-10-16, the sound)

Done, with one change of mind. The engine's audio is SDL's and not OpenAL. SDL is in the engine already (the Mac's
window, the gamepads) and ships its native library for every platform, OpenAL Soft would have been a new LGPL dll, and
a mixer of our own is a page of code and does exactly what a game wants.

- **The engine.** `Horizon/Audio`. `AudioMixer` is everything that plays mixed into stereo floats, 48 voices, sounds
  read whole (`Sound`, .wav by `WaveFile` and .ogg by NVorbis, MIT) and one stream of music at a time with the last one
  fading out under it (`OggStream`), each at its own volume, pitch and balance on one of three buses (music, effects,
  interface), a cap of how many of one sound play at once (the oldest gives way), fades, no garbage (it's on the hot
  path list). `AudioDevice` is SDL's queue fed off its own thread a little over 40 ms ahead, quiet when nothing plays,
  `GameEngine.Audio`. No device or `HORIZON_AUDIO=off` and nothing comes out, nothing else changes, an application
  (Hex) doesn't open one. Tested without a device in `Horizon.Tests/Audio`.
- **The sounds.** `Assets/sounds`, 3.6 MB, Kenney's Impact, Interface, RPG and Fighter voiceover packs, Ylmir's rain
  and two CC0 pieces off OpenGameArt for the music (mrpoly's menu music, HydroGene's JRPG battle theme), the README
  says which file is which.
- **The game.** `Audio/GameSounds.cs` is the library (a take picked at random a hit and its pitch nudged, so a combo
  isn't a machine gun), the music and the ambience, `Audio/FightSounds.cs` listens to a fight, every attack off the
  combat log (a hit heavier and lower the more it did, the bell on a counter hit or a punish, a block, a parry, a
  whiff), the announcer off the rounds (the round, the final round, FIGHT, time, a tie, a perfect, the winner) and the
  bell of a K.O., footsteps every stride on the ground and a landing as hard as the fall, all panned to where it
  happened, the fight's music and the rain of a rainy map. The menus click as you go (the shared driver, `MenuActions`,
  a move, a pick, a lock in, back, a refusal, the pause and the drawer opening and shutting), the menus have their
  music. The options have a Sound tab, music, effects and menus in tenths.

Not heard by me, there's nothing to listen with here. The device opens and every file decodes (`ContentTests`), what
it sounds like is bogz's to judge, the levels are in `GameSounds` and the choice of take in its README.

# Working on Fighter2D

Notes from bogz for whoever (or whatever) writes code and commits in here. The voice is the same as Horizon's
CLAUDE.md, casual, the odd swear word where it earns its keep, no "Thing: explanation" colons, no "Added X" lists in
commit bodies.

## The engine

Horizon is a submodule at `external/horizon` (bogzxo/horizon, on its `development`), with HIDL a submodule inside
of that, so clone with `--recursive` or run `git submodule update --init --recursive` after. The game and the hitbox
editor build against that copy. A change to the engine is made in the engine's own repo, pushed, and then
`git submodule update --remote external/horizon` here and the new commit committed, or the game is still built
against the old one. `Fighter2D.sln` has the game, the hitbox editor and the engine. Anything the game wants of the
GPU goes through the engine, there is no GL in here.

## Where everything is

- `App/` the entry point (`Program.cs`, `--map <name>` drops straight into a fight, `--content <folder>` points at
  other content), `GameOptions.cs` (everything on the options screen, kept in `options.hor` next to the exe, with the
  `Fancy` switch for the path traced lighting and `Bloom` for the glow round the lanterns), `Screen.cs` (the
  effects every scene puts on, the CRT, the bloom, which lighting the world gets), `DeferedStyle.cs` (every number
  of how the world is shaded that is a matter of taste, the highlights, the shadows, the bloom's threshold and
  strength, how much the corners darken out of the painted maps, the map's shape and the fighters, tune the look
  there and nowhere else).
- `Scenes/` one class per screen (the fight and its setup in `Fight/`), `Menus/` for the menu layouts, `Widgets/` for the bits they share (the character
  portrait draws out of a character's atlas). From the main menu a fight is one screen away, `FightSetupScene` has
  the gamepads, the fighters, the map and the rules on it (and is the lobby of an online fight, its `.Online.cs`),
  the gamepad that picked the fight on the main menu is player one already. The keyboard is a gamepad too
  (`GameInput.Attach` adds the engine's `KeyboardGamepad`), which is all it takes for every menu to work off the keys
  and for its prompts to show keys, so nothing in a menu reads the keyboard itself. The bindings are got to from there and
  from the options, and go back to whichever it was.
- `Fighters/` a fighter in the arena. `Player.cs` is the sprite plus the body plus the health, `Characters/CharacterArt.cs` reads
  a character's .ase once and owns its atlas (shared across scenes, trimmed), `Boxes/CharacterBoxes.cs` traces the hurt,
  hit and body boxes off the frames (everything to do with boxes is in `Boxes/`), `PlayerBody.cs` shapes the physics body like the frame being shown,
  `Control/` is the controller, moves and the stage route the dummy walks, `Inputs/` the gamepads, the dummy and the
  network.
- `Moves/`, `Combat/`, `Match/` the fighting itself, `HUD/` the overlay (`FrameBarDisplay.cs` is the two bars at
  the bottom in the middle, what each player is doing and its startup, active frames and recovery with a line
  running along them, or their stun running out, there in every mode unless frame data is off in the options), `Map/` the stages (Tiled maps, the preview,
  the fight camera), `Effects/` particles, `Weather/`, the menu duel in `Menu/`, `Networking/` the online fight (sending the game
  files over in `Content/`), `Input/` bindings.
- `Content/` what counts as content (`GameContent.cs`, the folders and extensions that are sent to whoever joins),
  `PackedFiles.cs` unpacks the engine's files next to a published exe, `HorReader.cs` reads .hor data files.

## Assets and Art

`Assets/` is exactly what the game reads and it ships by one glob in the csproj, nothing in it is optional.

- `data/` the .hor files, `characters.hor` and `maps.hor` are the lists, `*_moves.hor` the move sets.
- `maps/` the Tiled maps, `tilesets/` with `_albedo`, `_normal`, `_specular` and `_ao` images (the last made by
  `Art/tools/build_ao.py` out of the albedo, run it again when the art changes), `objects/` the templates.
- `sprites/characters/<name>/<name>.ase` the characters, one Aseprite file each, a tag per animation, read by the
  engine as they are. A folder with a `spritesheet.png` and a `definition.hor` still works for the old ones.
- `backgrounds/` the two the menus use, `ui/layouts/` the UIX layouts, `ui/logo.png`.
- `ui/localisation/` every text of the game by key, one file a language (`en-gb.hor` is the only one so far), loaded
  in `Program.Main` from `GameOptions.Language`. A layout names a text as `"@main_menu.quit"`, the code asks
  `Localisation.Get("main_menu.quit")` or `Localisation.Format("fight.takes_round", ("name", name))`. Nothing a
  player reads goes into the code or a layout as words any more, it goes in there and gets a key. The logs and the
  errors of content files are for whoever makes the game and stay in English in the code.

`Art/` is source material and never ships. The sheets the characters were cut from and `tools/build_ase.py` that
turns a sheet into an .ase, art that was drawn and then not used, two fonts nothing reads. Its README says what's what.

`Build History/` is the record of every build along the way, one folder per build, about 200 MB of exes. It is
kept on purpose and tracked on purpose, don't tidy it away.

## Building and testing

- `dotnet build -c Debug` to run it, `dotnet publish -c Release -r win-x64 -p:Platform=x64` for the one exe plus
  `Assets` that gets handed out (AOT, trimmed, the engine's files packed into the exe).
- Headless on Linux, the same way as the engine, `DISPLAY=:99 MESA_GL_VERSION_OVERRIDE=4.6
  MESA_GLSL_VERSION_OVERRIDE=460 ./Fighter2D --map underground` with `HORIZON_INPUT_SCRIPT` to quit and
  `HORIZON_SCREENSHOT=file.png@11` to see the fight (the first ten seconds are the versus screen).

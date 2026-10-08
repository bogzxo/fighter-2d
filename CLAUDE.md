# Working on Fighter2D

Notes from bogz for whoever (or whatever) writes code and commits in here. The voice is the same as Horizon's
CLAUDE.md, casual, the odd swear word where it earns its keep, no "Thing: explanation" colons, no "Added X" lists in
commit bodies.

## The engine

The game references `../horizon/Horizon/Horizon.csproj` and `../horizon/Horizon.HIDL/Horizon.HIDL.csproj` by relative
path, so the two checkouts sit side by side and the game builds against whatever branch of Horizon is checked out.
Anything the game wants of the GPU goes through the engine, there is no GL in here.

## Where everything is

- `App/` the entry point (`Program.cs`, `--map <name>` drops straight into a fight, `--content <folder>` points at
  other content), `GameOptions.cs` (everything on the options screen, kept in `options.hor` next to the exe, with the
  `Fancy` switch for the path traced lighting), `Screen.cs` (the effects every scene puts on, CRT, motion blur, which
  lighting the world gets).
- `Scenes/` one class per screen, `Menus/` for the menu layouts, `Widgets/` for the bits they share (the character
  portrait draws out of a character's atlas).
- `Fighters/` a fighter in the arena. `Player.cs` is the sprite plus the body plus the health, `CharacterArt.cs` reads
  a character's .ase once and owns its atlas (shared across scenes, trimmed), `CharacterBoxes.cs` traces the hurt,
  hit and body boxes off the frames, `PlayerBody.cs` shapes the physics body like the frame being shown,
  `Control/` is the controller, moves and the stage route the dummy walks, `Inputs/` the gamepads, the dummy and the
  network.
- `Moves/`, `Combat/`, `Match/` the fighting itself, `HUD/` the overlay, `Map/` the stages (Tiled maps, the preview,
  the fight camera), `Effects/` particles, weather, the menu duel, `Networking/` the online fight, `Input/` bindings.
- `Content/` what counts as content (`GameContent.cs`, the folders and extensions that are sent to whoever joins),
  `PackedFiles.cs` unpacks the engine's files next to a published exe, `HorReader.cs` reads .hor data files.

## Assets and Art

`Assets/` is exactly what the game reads and it ships by one glob in the csproj, nothing in it is optional.

- `data/` the .hor files, `characters.hor` and `maps.hor` are the lists, `*_moves.hor` the move sets.
- `maps/` the Tiled maps, `tilesets/` with `_albedo`, `_normal` and `_specular` images, `objects/` the templates.
- `sprites/characters/<name>/<name>.ase` the characters, one Aseprite file each, a tag per animation, read by the
  engine as they are. A folder with a `spritesheet.png` and a `definition.hor` still works for the old ones.
- `backgrounds/` the two the menus use, `ui/layouts/` the UIX layouts, `ui/logo.png`.

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

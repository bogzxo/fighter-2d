using System.Text.RegularExpressions;

using Fighter2D.Arena;
using Fighter2D.Content;
using Fighter2D.Data;
using Fighter2D.Tests.Support;

using Horizon.Content;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Tests.Content;

/// <summary>
/// The shipped content holds together. A character, a map or a text that is broken is found here in a second rather than
/// by whoever picks that fighter or opens that screen, which is the whole point of the content being files. Every
/// character in characters.hor reads, has the art every one of its moves plays and the moves the game reaches for by
/// name, every map in maps.hor loads, and every text a layout or the code names is in the English file.
/// </summary>
public partial class ContentTests
{
    public static TheoryData<string> Characters()
    {
        TestFight.UseGameContent();
        return [.. CharacterDefinition.LoadAll().Select(character => character.Id)];
    }

    public static TheoryData<string> Maps()
    {
        TestFight.UseGameContent();
        return [.. MapLoader.LoadAll().Select(map => map.FileName)];
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void ACharacterHasTheArtOfEveryMoveItPlays(string id)
    {
        TestFight.UseGameContent();
        CharacterDefinition character = CharacterDefinition.Load(id);
        Assert.Equal(id, character.Id);

        // Throws, saying which file and why, if a move file doesn't read or the moves don't make sense together
        MoveList moves = character.LoadMoves();
        CustomHitboxes.Apply(moves, character.Id);

        string sprites = GameContent.PathOf(character.Sprites);
        Assert.True(File.Exists(sprites) || Directory.Exists(sprites), $"{id}'s art isn't there, {character.Sprites}");

        SpriteSheetDefinition sheet = SpriteSheetDefinition.Open(sprites);
        string[] missing = [.. moves.Animations.Where(animation => sheet.FrameCount(animation) == 0)];
        Assert.True(missing.Length == 0, $"{id}'s moves play animations its art hasn't got, {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void ACharacterHasTheMovesTheGameReachesFor(string id)
    {
        TestFight.UseGameContent();
        MoveList moves = CharacterDefinition.Load(id).LoadMoves();

        // Idle is checked by the list itself, a hit with no hit_stun to go into wouldn't stun anybody
        Assert.True(moves.Moves.ContainsKey(MoveIds.HIT_STUN), $"{id} has no {MoveIds.HIT_STUN}, a hit would do nothing to them");

        // A hit cancel names a move, a kind or any, a typo in one is a cancel that never happens
        string[] kinds = [FightingMove.HIT_CANCEL_ANY, "normal", "special", "super"];
        foreach (FightingMove move in moves.Moves.Values)
        {
            foreach (string into in move.HitCancel ?? [])
            {
                Assert.True(kinds.Contains(into) || moves.Moves.ContainsKey(into), $"{id}'s {move.Id} hit cancels into '{into}', which is no move of theirs");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public void ACharactersMoveFilesOpenInTheMoveEditor(string id)
    {
        // Hex opens a file on its move editor when it says so, without the line it is code like any other file
        TestFight.UseGameContent();
        foreach (string file in CharacterDefinition.Load(id).MoveFiles)
        {
            bool says = File.ReadLines(GameContent.PathOf(file)).Any(line => line.Trim() == "#define fight_move");
            Assert.True(says, $"{file} ({id}'s) doesn't say #define fight_move, Hex won't open it on the move editor");
        }
    }

    [Theory]
    [MemberData(nameof(Maps))]
    public void AMapLoads(string name)
    {
        TestFight.UseGameContent();
        Assert.True(MapLoader.TryFind(name, out MapDefinition map));

        using FightingStage stage = FightingStage.Load(map, walked: true);
        Assert.NotNull(stage.Paths);
        Assert.True(stage.RightSpawn.X > stage.LeftSpawn.X, $"{name} has its spawns the wrong way round");
    }

    [Fact]
    public void EveryTextTheGameNamesIsThere()
    {
        TestFight.UseGameContent();
        string home = GameContent.Home;
        IReadOnlyDictionary<string, string> texts = Localisation.ReadFile(Path.Combine(home, "Assets", "ui", "localisation", "en-gb.hor"));
        HashSet<string> sections = [.. texts.Keys.Select(key => key[..Math.Max(0, key.IndexOf('.'))])];

        var missing = new SortedSet<string>();

        // What a layout or a data file (a character's blurb) says with an @
        IEnumerable<string> named = Directory.EnumerateFiles(Path.Combine(home, "Assets", "ui", "layouts"), "*.hor", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(home, "Assets", "data"), "*.hor", SearchOption.AllDirectories));
        foreach (string layout in named)
        {
            foreach (System.Text.RegularExpressions.Match match in LayoutKey().Matches(File.ReadAllText(layout)))
            {
                string key = match.Groups[1].Value;
                if (!texts.ContainsKey(key)) missing.Add($"{key} ({Path.GetFileName(layout)})");
            }
        }

        // What the code asks for, any string that starts with a section of the file and isn't the name of a file
        foreach (string source in GameSources(home))
        {
            foreach (System.Text.RegularExpressions.Match match in CodeKey().Matches(File.ReadAllText(source)))
            {
                string key = match.Groups[1].Value;
                if (!sections.Contains(key[..key.IndexOf('.')]) || FileName().IsMatch(key)) continue;
                if (!texts.ContainsKey(key)) missing.Add($"{key} ({Path.GetFileName(source)})");
            }
        }

        Assert.True(missing.Count == 0, $"texts named that en-gb.hor hasn't got, {string.Join(", ", missing)}");
    }

    // The game's own code, not the engine under it, the tests or what a build made
    private static IEnumerable<string> GameSources(string home)
    {
        string[] skipped = ["external", "bin", "obj", "Fighter2D.Tests", "Art", "New Art 3D"];
        return Directory.EnumerateFiles(home, "*.cs", SearchOption.AllDirectories)
            .Where(file => !skipped.Any(folder => Path.GetRelativePath(home, file).Split(Path.DirectorySeparatorChar)[0] == folder));
    }

    [GeneratedRegex("\"@([a-z][a-z0-9_]*\\.[a-z0-9_.]+)\"")]
    private static partial Regex LayoutKey();

    [GeneratedRegex("\"([a-z][a-z0-9_]*\\.[a-z0-9_.]*[a-z0-9])\"")]
    private static partial Regex CodeKey();

    [GeneratedRegex("\\.(hor|tmx|tsx|ase|aseprite|png|json|txt|wav|ogg|mp3|ttf|fnt|slang|cs|exe|dll)$")]
    private static partial Regex FileName();
}

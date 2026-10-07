using System;
using System.Collections.Generic;
using System.Linq;

using Fighter2D.Content;
using Fighter2D.Logic;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Character;

/// <summary>
/// What a character is made of, which is its sprites, its moves and how it handles.
/// All of it comes out of Assets/data/characters.hor, so a game pack can change the characters there are or bring more.
/// </summary>
internal class CharacterDefinition
{
    private const float DEFAULT_WALK_SPEED = 2000f;
    private const float DEFAULT_FRAME_RATE = 24f;
    private const float DEFAULT_STAT = 0.5f;

    public string Id { get; private init; } = string.Empty;
    public string PrettyName { get; private init; } = string.Empty;

    // The folder of the sprite sheet and its definition, relative to the content
    public string SpriteDirectory { get; private init; } = string.Empty;

    // The move files of the character, relative to the content. A later file can replace moves of an earlier one
    public string[] MoveFiles { get; private init; } = [];

    // The speed the character walks up to
    public float WalkSpeed { get; private init; } = DEFAULT_WALK_SPEED;

    // How many frames of animation a second its moves play at
    public float FrameRate { get; private init; } = DEFAULT_FRAME_RATE;

    // How much bigger or smaller than the others the character is, sprite, body and boxes alike
    public float Scale { get; private init; } = 1f;

    // The bars on the character select screen, each from 0 to 1. They are only for show
    public float Speed { get; private init; } = DEFAULT_STAT;
    public float Health { get; private init; } = DEFAULT_STAT;
    public float Bulk { get; private init; } = DEFAULT_STAT;
    public float Complexity { get; private init; } = DEFAULT_STAT;

    /// <summary>
    /// Helper method to read the move list of the character, from wherever the content is right now.
    /// </summary>
    public MoveList LoadMoves() => MoveList.Load([.. MoveFiles.Select(GameContent.PathOf)]);

    /// <summary>
    /// Helper method to read every character there is, in the order they are written down. Throws if the file makes no sense.
    /// </summary>
    public static List<CharacterDefinition> LoadAll()
    {
        string file = GameContent.PathOf(GameContent.CHARACTERS_FILE);
        var read = new List<CharacterDefinition>();

        foreach (var (id, value) in HorReader.LoadObject(file, "characters"))
        {
            if (value is not ObjectValue { Properties: { } character })
                throw new Exception($"'{file}': the character '{id}' has to be an object.");

            try
            {
                read.Add(Read(id, character));
            }
            catch (Exception e)
            {
                throw new Exception($"'{file}': the character '{id}' is wrong: {e.Message}");
            }
        }

        if (read.Count == 0) throw new Exception($"'{file}' doesn't have a single character in it.");
        return read;
    }

    private static CharacterDefinition Read(string id, Dictionary<string, IRuntimeValue> character)
    {
        if (!character.ContainsKey("sprites")) throw new Exception("it has to say where its sprites are.");
        if (!HorReader.TryObject(character, "moves", out var moveFiles)) throw new Exception("it has to list its move files.");

        HorReader.TryObject(character, "stats", out var stats);

        // Stats are out of 100 in the file
        float Stat(string key) => stats.TryGetValue(key, out var stat) && stat is NumberValue number ? Math.Clamp(number.Value / 100f, 0, 1) : DEFAULT_STAT;

        return new CharacterDefinition
        {
            Id = id,
            PrettyName = HorReader.Text(character, "pretty_name", id),
            SpriteDirectory = HorReader.Text(character, "sprites"),
            MoveFiles = [.. moveFiles.Values.OfType<StringValue>().Select(moveFile => moveFile.Value)],
            WalkSpeed = HorReader.Number(character, "walk_speed", DEFAULT_WALK_SPEED),
            FrameRate = MathF.Max(1, HorReader.Number(character, "frame_rate", DEFAULT_FRAME_RATE)),
            Scale = Math.Clamp(HorReader.Number(character, "scale", 1f), 0.25f, 4f),
            Speed = Stat("speed"),
            Health = Stat("health"),
            Bulk = Stat("bulk"),
            Complexity = Stat("complexity")
        };
    }

    /// <summary>
    /// The character everybody gets when nobody picked one, which is the first one of the file.
    /// </summary>
    public static CharacterDefinition LoadDefault() => LoadAll()[0];

    /// <summary>
    /// Helper method to find a character by its id. Asking for one that doesn't exist (or for none) gets you the first one.
    /// </summary>
    public static CharacterDefinition Load(string? id)
    {
        var all = LoadAll();
        return all.Find(character => character.Id == id) ?? all[0];
    }
}

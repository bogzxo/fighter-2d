using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Fighter2D.Content;
using Fighter2D.Logic;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D.Character;

/// <summary>
/// What a character is made of: its sprites, its moves and how it handles. All of it comes out of the content of the game
/// (see Assets/data/characters.hor), so a game pack can change the one there is or bring others.
/// </summary>
internal class CharacterDefinition
{
    public string Id { get; private init; } = string.Empty;
    public string PrettyName { get; private init; } = string.Empty;

    // The folder of the sprite sheet and its definition, relative to the content
    public string SpriteDirectory { get; private init; } = string.Empty;

    // The move files of the character, relative to the content. A later one can replace moves of an earlier one
    public string[] MoveFiles { get; private init; } = [];

    // The speed the character walks up to
    public float WalkSpeed { get; private init; } = 2000f;

    // How many frames of animation a second its moves play at
    public float FrameRate { get; private init; } = 24f;

    // How much bigger or smaller than the others the character is: its sprite, its body and what it hits with alike
    public float Scale { get; private init; } = 1f;

    // What the character select screen says about the character, each from 0 to 1. They are only for show
    public float Speed { get; private init; } = 0.5f;
    public float Health { get; private init; } = 0.5f;
    public float Bulk { get; private init; } = 0.5f;
    public float Complexity { get; private init; } = 0.5f;

    /// <summary>
    /// Helper method to read the move list of the character, from wherever the content is right now.
    /// </summary>
    public MoveList LoadMoves() => MoveList.Load([.. MoveFiles.Select(GameContent.PathOf)]);

    /// <summary>
    /// Helper method to read every character there is, in the order they are written down. Throws if the file can't be made sense of.
    /// </summary>
    public static List<CharacterDefinition> LoadAll()
    {
        string file = GameContent.PathOf(GameContent.CHARACTERS_FILE);
        if (!File.Exists(file)) throw new Exception($"The character file '{file}' is missing.");

        HIDLRuntime runtime = new();
        (bool success, string result) = runtime.Evaluate(File.ReadAllText(file));
        if (!success) throw new Exception($"'{file}': {result}");

        if (runtime.UserScope.Lookup("characters") is not ObjectValue characters)
            throw new Exception($"'{file}' has to declare an object called 'characters'.");

        var read = new List<CharacterDefinition>();
        foreach (var (id, value) in characters.Properties)
        {
            if (value is not ObjectValue { Properties: { } character })
                throw new Exception($"'{file}': the character '{id}' has to be an object.");

            if (!character.TryGetValue("sprites", out var sprites) || sprites is not StringValue spriteDirectory)
                throw new Exception($"'{file}': the character '{id}' has to say where its sprites are.");

            if (!character.TryGetValue("moves", out var moves) || moves is not ObjectValue { Properties: { } moveFiles })
                throw new Exception($"'{file}': the character '{id}' has to list its move files.");

            // Stats are out of 100 in the file
            float Stat(string key) =>
                character.TryGetValue("stats", out var stats) && stats is ObjectValue { Properties: { } all }
                && all.TryGetValue(key, out var stat) && stat is NumberValue number
                    ? Math.Clamp(number.Value / 100f, 0, 1)
                    : 0.5f;

            read.Add(new CharacterDefinition
            {
                Speed = Stat("speed"),
                Health = Stat("health"),
                Bulk = Stat("bulk"),
                Complexity = Stat("complexity"),
                Id = id,
                PrettyName = character.TryGetValue("pretty_name", out var name) && name is StringValue prettyName ? prettyName.Value : id,
                SpriteDirectory = spriteDirectory.Value,
                MoveFiles = [.. moveFiles.Values.OfType<StringValue>().Select(moveFile => moveFile.Value)],
                WalkSpeed = character.TryGetValue("walk_speed", out var speed) && speed is NumberValue walkSpeed ? walkSpeed.Value : 2000f,
                FrameRate = character.TryGetValue("frame_rate", out var rate) && rate is NumberValue frameRate ? MathF.Max(1, frameRate.Value) : 24f,
                Scale = character.TryGetValue("scale", out var size) && size is NumberValue scale ? Math.Clamp(scale.Value, 0.25f, 4f) : 1f
            });
        }

        if (read.Count == 0) throw new Exception($"'{file}' doesn't have a single character in it.");
        return read;
    }

    /// <summary>
    /// The character everybody plays until there is a screen to pick one on, the first one of the file.
    /// </summary>
    public static CharacterDefinition LoadDefault() => LoadAll()[0];

    /// <summary>
    /// Helper method to find a character by its name in the file. Whoever asks for one that isn't there (or for none) gets the first one.
    /// </summary>
    public static CharacterDefinition Load(string? id)
    {
        var all = LoadAll();
        return all.Find(character => character.Id == id) ?? all[0];
    }
}

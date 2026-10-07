using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Moves;

/// <summary>
/// Turns a move file (.hor) into moves. What every key means is written down at the top of Assets/data/fighting_moves.hor.
/// Anything wrong with a file throws with the file and the move it is in, so a broken game pack says what is broken.
/// </summary>
internal static class MoveFileReader
{
    private static readonly HashSet<string> MoveKeys =
    [
        "input", "trigger", "priority", "stances", "damage", "knockback", "hitstun", "cancellable", "steering",
        "turning", "status", "stance", "stance_after", "warns", "repeat", "phases", "stance_reroutes", "finish_reroutes"
    ];

    private static readonly HashSet<string> PhaseKeys =
    [
        "animation", "frames", "hit", "cancel", "commit", "status", "status_frame", "loop_while", "until", "impulse", "effect", "effect_frames"
    ];

    // What some keys used to be called, so an old file gets pointed at the new name instead of a shrug
    private static readonly Dictionary<string, string> RenamedKeys = new()
    {
        ["stun"] = "hitstun",
        ["interruptible"] = "cancellable",
        ["interrupt"] = "cancel",
        ["lock"] = "commit"
    };

    /// <summary>
    /// Helper method to read every move of a file.
    /// </summary>
    public static IEnumerable<FightingMove> Read(string file)
    {
        foreach (var (id, value) in HorReader.LoadObject(file, "moves"))
        {
            if (value is not ObjectValue move) throw new Exception($"'{file}': the move '{id}' has to be an object.");

            FightingMove read;
            try
            {
                read = ReadMove(id, move.Properties);
            }
            catch (Exception e)
            {
                throw new Exception($"'{file}': the move '{id}' is wrong: {e.Message}");
            }

            yield return read;
        }
    }

    private static FightingMove ReadMove(string id, Dictionary<string, IRuntimeValue> move)
    {
        HorReader.RejectUnknownKeys(move, MoveKeys, "a move", RenamedKeys);

        int damage = (int)HorReader.Number(move, "damage", 0);

        // A move you can't walk during is one you can't turn around in either, unless it says otherwise
        bool steering = HorReader.Bool(move, "steering", true);

        return new FightingMove
        {
            Id = id,
            Damage = damage,
            Knockback = HorReader.Vector(move, "knockback", Vector2.Zero),
            Hitstun = HorReader.Number(move, "hitstun", -1),
            Stances = move.TryGetValue("stances", out var stances) ? HorReader.Flags<Stance>(stances, "stances") : Stance.Standing,
            InputSignatures = ReadInputs(move),
            Trigger = move.TryGetValue("trigger", out var trigger) ? HorReader.Named<InputTrigger>(HorReader.Text(trigger, "trigger").Replace("_", ""), "trigger") : InputTrigger.Held,
            Priority = (int)HorReader.Number(move, "priority", -1),
            Cancellable = HorReader.Bool(move, "cancellable", true),
            AllowsSteering = steering,
            AllowsTurning = HorReader.Bool(move, "turning", steering),
            Status = ReadOptional<FighterStatus>(move, "status"),
            Stance = ReadOptional<Stance>(move, "stance"),
            StanceAfter = ReadOptional<Stance>(move, "stance_after"),

            // A move that hurts warns the opponent unless it says otherwise
            Warns = HorReader.Bool(move, "warns", damage > 0),
            Repeat = HorReader.Named(move, "repeat", MoveCondition.None),
            Phases = ReadPhases(move),
            StanceReroutes = ReadReroutes(move, "stance_reroutes"),
            FinishReroutes = ReadReroutes(move, "finish_reroutes")
        };
    }

    private static T? ReadOptional<T>(Dictionary<string, IRuntimeValue> properties, string key) where T : struct, Enum =>
        properties.TryGetValue(key, out var value) ? HorReader.Named<T>(HorReader.Text(value, key), key) : null;

    /// <summary>
    /// Helper method to read the inputs of a move. "A" is one button, "DPadLeft | DPadRight" is either of them and "A + B" is both together.
    /// </summary>
    private static InputFlags[] ReadInputs(Dictionary<string, IRuntimeValue> move)
    {
        if (!move.TryGetValue("input", out var value)) return [InputFlags.None];

        const StringSplitOptions tidy = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        var signatures = new List<InputFlags>();

        foreach (string alternative in HorReader.Text(value, "input").Split('|', tidy))
        {
            InputFlags signature = InputFlags.None;
            foreach (string button in alternative.Split('+', tidy))
            {
                signature |= HorReader.Named<InputFlags>(button, "input");
            }
            signatures.Add(signature);
        }

        return signatures.Count > 0 ? [.. signatures] : [InputFlags.None];
    }

    private static MovePhase[] ReadPhases(Dictionary<string, IRuntimeValue> move)
    {
        if (!move.ContainsKey("phases")) return [];

        var read = new List<MovePhase>();

        // They run in the order they are written in
        foreach (var (name, value) in HorReader.Object(move, "phases"))
        {
            if (value is not ObjectValue { Properties: { } phase }) throw new Exception($"the phase '{name}' has to be an object.");

            try
            {
                read.Add(ReadPhase(phase));
            }
            catch (Exception e)
            {
                throw new Exception($"{e.Message} (phase '{name}')");
            }
        }

        return [.. read];
    }

    private static MovePhase ReadPhase(Dictionary<string, IRuntimeValue> phase)
    {
        HorReader.RejectUnknownKeys(phase, PhaseKeys, "a phase", RenamedKeys);

        string? effect = phase.TryGetValue("effect", out var effectValue) ? HorReader.Text(effectValue, "effect") : null;
        if (effect is not null && !MoveEffects.Exists(effect)) throw new Exception($"'{effect}' isn't an effect.");

        return new MovePhase
        {
            Animation = phase.TryGetValue("animation", out var animation) ? HorReader.Text(animation, "animation") : null,
            Frames = (uint)HorReader.Number(phase, "frames", 0),
            HitFrame = (int)HorReader.Number(phase, "hit", -1),
            CancelFrame = (int)HorReader.Number(phase, "cancel", -1),
            CommitFrame = (int)HorReader.Number(phase, "commit", -1),
            Status = ReadOptional<FighterStatus>(phase, "status"),
            StatusFrame = (int)HorReader.Number(phase, "status_frame", 0),
            LoopWhile = HorReader.Named(phase, "loop_while", MoveCondition.None),
            Until = HorReader.Named(phase, "until", MoveCondition.None),
            Impulse = HorReader.Vector(phase, "impulse", Vector2.Zero),
            Effect = effect,
            EffectFrames = (uint)HorReader.Number(phase, "effect_frames", 1)
        };
    }

    private static Dictionary<Stance, string>? ReadReroutes(Dictionary<string, IRuntimeValue> move, string key)
    {
        if (!move.ContainsKey(key)) return null;

        var read = new Dictionary<Stance, string>();
        foreach (var (stance, target) in HorReader.Object(move, key))
        {
            read[HorReader.Named<Stance>(stance, key)] = HorReader.Text(target, $"{key}.{stance}");
        }

        return read;
    }
}

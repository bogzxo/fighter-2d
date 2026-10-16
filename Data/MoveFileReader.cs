using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.HIDL.Runtime;

namespace Fighter2D.Data;

/// <summary>
/// Turns a move file (.hor) into moves. What every key means is written down at the top of Assets/data/fighting_moves.hor.
/// Anything wrong with a file throws with the file and the move it is in, so a broken game pack says what is broken.
/// </summary>
internal static class MoveFileReader
{
    private static readonly HashSet<string> MoveKeys =
    [
        "input", "trigger", "priority", "stances", "damage", "launch", "launch_growth", "push", "hitstun", "blockstun", "hitstop", "hitstun_gravity",
        "level", "knockdown", "meter_cost", "walk", "drift", "turning", "status", "stance", "stance_after", "warns", "repeat",
        "landing_lag", "hit_cancel", "kind", "whiff_lag", "phases", "stance_reroutes", "finish_reroutes"
    ];

    private static readonly HashSet<string> PhaseKeys =
    [
        "animation", "start", "frames", "stretch", "art_speed", "hit", "active", "cancel", "commit", "status", "status_frame", "loop_while", "until",
        "impulse", "jump", "effect", "effect_frames"
    ];

    // What some keys used to be called, so an old file gets pointed at the new name instead of a shrug
    private static readonly Dictionary<string, string> RenamedKeys = new()
    {
        ["stun"] = "hitstun",
        ["interrupt"] = "cancel",
        ["lock"] = "commit"
    };

    // Keys that went when the numbers became frames of the fight and world units, and what took their place. They
    // didn't just change name, their numbers mean something else now, Art/tools/migrate_moves.py turns an old file into a new one
    private static readonly Dictionary<string, string> RetiredKeys = new()
    {
        ["knockback"] = "launch, how far away and how high in world units",
        ["cancellable"] = "the cancel frame of a phase, a move without one can't be cancelled",
        ["interruptible"] = "the cancel frame of a phase, a move without one can't be cancelled",
        ["steering"] = "walk and drift, 0 to 1",
        ["walk_scale"] = "walk, 0 to 1",
        ["hold"] = "art_speed, 0.5 for what hold: 2 was"
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

    /// <summary>
    /// Helper method to read one move out of what its object in a file holds, for the editor, which keeps the moves it
    /// works on as values and not as text. Throws saying what is wrong, the same as a file would.
    /// </summary>
    public static FightingMove ReadOne(string id, Dictionary<string, IRuntimeValue> move) => ReadMove(id, move);

    private static FightingMove ReadMove(string id, Dictionary<string, IRuntimeValue> move)
    {
        RejectRetiredKeys(move);
        HorReader.RejectUnknownKeys(move, MoveKeys, "a move", RenamedKeys);

        int damage = (int)HorReader.Number(move, "damage", 0);
        int meterCost = Math.Max(0, (int)HorReader.Number(move, "meter_cost", 0));

        // A move you can't walk during is one you can't turn around in either, unless it says otherwise
        float walk = Math.Clamp(HorReader.Number(move, "walk", 1.0f), 0.0f, 1.0f);

        return new FightingMove
        {
            Id = id,
            Damage = damage,
            Launch = HorReader.Vector(move, "launch", Vector2.Zero),
            LaunchGrowth = HorReader.Vector(move, "launch_growth", Vector2.Zero),
            Push = HorReader.Number(move, "push", -1),
            Hitstun = (int)MathF.Round(HorReader.Number(move, "hitstun", -1)),
            Blockstun = (int)MathF.Round(HorReader.Number(move, "blockstun", -1)),
            Hitstop = (int)MathF.Round(HorReader.Number(move, "hitstop", -1)),
            HitstunGravity = move.ContainsKey("hitstun_gravity") ? Math.Clamp(HorReader.Number(move, "hitstun_gravity", 1), 0.05f, 2.0f) : null,
            Level = HorReader.Named(move, "level", HitLevel.Mid),
            Knockdown = HorReader.Bool(move, "knockdown", false),
            MeterCost = meterCost,
            Stances = move.TryGetValue("stances", out var stances) ? HorReader.Flags<Stance>(stances, "stances") : Stance.Standing,
            InputSignatures = ReadInputs(move),
            Trigger = move.TryGetValue("trigger", out var trigger) ? HorReader.Named<InputTrigger>(HorReader.Text(trigger, "trigger").Replace("_", ""), "trigger") : InputTrigger.Held,
            Priority = (int)HorReader.Number(move, "priority", -1),
            Walk = walk,
            Drift = Math.Clamp(HorReader.Number(move, "drift", 1.0f), 0.0f, 1.0f),
            AllowsTurning = HorReader.Bool(move, "turning", walk > 0.0f),
            Status = ReadOptional<FighterStatus>(move, "status"),
            Stance = ReadOptional<Stance>(move, "stance"),
            StanceAfter = ReadOptional<Stance>(move, "stance_after"),

            // A move that hurts warns the opponent unless it says otherwise
            Warns = HorReader.Bool(move, "warns", damage > 0),
            Repeat = HorReader.Named(move, "repeat", MoveCondition.None),
            LandingLag = Math.Max(0, (int)HorReader.Number(move, "landing_lag", 0)),
            HitCancel = move.TryGetValue("hit_cancel", out var hitCancel) ? HorReader.Texts(hitCancel, "hit_cancel") : null,

            // A move that costs meter is a special unless it says, half a meter or more a super
            Kind = HorReader.Named(move, "kind", meterCost >= Player.MAX_METER / 2 ? MoveKind.Super : meterCost > 0 ? MoveKind.Special : MoveKind.Normal),
            WhiffLag = Math.Max(0, (int)HorReader.Number(move, "whiff_lag", 0)),
            Phases = ReadPhases(move),
            StanceReroutes = ReadReroutes(move, "stance_reroutes"),
            FinishReroutes = ReadReroutes(move, "finish_reroutes")
        };
    }

    private static void RejectRetiredKeys(Dictionary<string, IRuntimeValue> properties)
    {
        foreach (string key in properties.Keys)
        {
            if (RetiredKeys.TryGetValue(key, out string? instead))
                throw new Exception($"'{key}' isn't a thing any more, it's {instead}. Art/tools/migrate_moves.py turns an old file into a new one.");
        }
    }

    private static T? ReadOptional<T>(Dictionary<string, IRuntimeValue> properties, string key) where T : struct, Enum =>
        properties.TryGetValue(key, out var value) ? HorReader.Named<T>(HorReader.Text(value, key), key) : null;

    /// <summary>
    /// Helper method to read the inputs of a move. "A" is one button, "DPadLeft | DPadRight" is either of them and "A + B" is both together.
    /// The alternatives can be a list as well, ["DPadLeft", "DPadRight"].
    /// </summary>
    private static InputFlags[] ReadInputs(Dictionary<string, IRuntimeValue> move)
    {
        if (!move.TryGetValue("input", out var value)) return [InputFlags.None];

        const StringSplitOptions tidy = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        var signatures = new List<InputFlags>();

        foreach (string alternative in HorReader.Texts(value, "input"))
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
                read.Add(ReadPhase(phase, name));
            }
            catch (Exception e)
            {
                throw new Exception($"{e.Message} (phase '{name}')");
            }
        }

        return [.. read];
    }

    private static MovePhase ReadPhase(Dictionary<string, IRuntimeValue> phase, string name)
    {
        RejectRetiredKeys(phase);
        HorReader.RejectUnknownKeys(phase, PhaseKeys, "a phase", RenamedKeys);

        string? effect = phase.TryGetValue("effect", out var effectValue) ? HorReader.Text(effectValue, "effect") : null;
        if (effect is not null && !MoveEffects.Exists(effect)) throw new Exception($"'{effect}' isn't an effect.");

        return new MovePhase
        {
            Name = name,
            Animation = phase.TryGetValue("animation", out var animation) ? HorReader.Text(animation, "animation") : null,
            Start = (uint)Math.Max(0, HorReader.Number(phase, "start", 0)),
            Frames = (uint)Math.Max(0, HorReader.Number(phase, "frames", 0)),
            Stretch = HorReader.Bool(phase, "stretch", false),
            ArtSpeed = Math.Clamp(HorReader.Number(phase, "art_speed", 1.0f), 0.01f, 16.0f),
            HitFrame = (int)HorReader.Number(phase, "hit", -1),
            Active = Math.Max(0, (int)HorReader.Number(phase, "active", 0)),
            CancelFrame = (int)HorReader.Number(phase, "cancel", -1),
            CommitFrame = (int)HorReader.Number(phase, "commit", -1),
            Status = ReadOptional<FighterStatus>(phase, "status"),
            StatusFrame = (int)HorReader.Number(phase, "status_frame", 0),
            LoopWhile = HorReader.Named(phase, "loop_while", MoveCondition.None),
            Until = HorReader.Named(phase, "until", MoveCondition.None),
            Impulse = HorReader.Vector(phase, "impulse", Vector2.Zero),
            Jump = HorReader.Named(phase, "jump", JumpKind.None),
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Fighter2D.Logic.Moves;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;

namespace Fighter2D.Logic;

/// <summary>
/// Every move a character has, read from the move files of the game's content (see Assets/data/fighting_moves.hor, which explains how a move is written down).
/// Nothing about a move is in the code: a game pack can change the ones there are or bring its own.
/// </summary>
internal class MoveList
{
    public Dictionary<string, FightingMove> Moves { get; } = [];

    // The order of precedence in which the moves are matched against the player input, the first match wins.
    // Moves that need more than what another move needs have to come before it (double tap left before left etc.)
    private FightingMove[] _inputMoves = [];

    public FightingMove Idle { get; private set; } = new();

    /// <summary>
    /// Helper method to read a move list out of its files, a move of a later file replaces the one of the same name from an earlier one.
    /// Throws (saying what is wrong and where) if a file can't be made sense of.
    /// </summary>
    public static MoveList Load(params string[] files)
    {
        var list = new MoveList();

        foreach (string file in files)
        {
            if (!File.Exists(file)) throw new Exception($"The move file '{file}' is missing.");

            HIDLRuntime runtime = new();
            (bool success, string result) = runtime.Evaluate(File.ReadAllText(file));
            if (!success) throw new Exception($"'{file}': {result}");

            if (runtime.UserScope.Lookup("moves") is not ObjectValue moves)
                throw new Exception($"'{file}' has to declare an object called 'moves'.");

            foreach (var (id, value) in moves.Properties)
            {
                if (value is not ObjectValue move) throw new Exception($"'{file}': the move '{id}' has to be an object.");

                try
                {
                    list.Moves[id] = ReadMove(id, move.Properties);
                }
                catch (Exception e)
                {
                    throw new Exception($"'{file}': the move '{id}' is wrong: {e.Message}");
                }
            }
        }

        list.Finish();
        return list;
    }

    private void Finish()
    {
        if (!Moves.TryGetValue(MoveIds.IDLE, out var idle))
            throw new Exception($"There is no move called '{MoveIds.IDLE}', a player has to be able to do nothing.");

        Idle = idle;

        // A reroute to a move that isn't there would leave the player stuck, better to hear about it now
        foreach (FightingMove move in Moves.Values)
        {
            IEnumerable<string> stanceTargets = move.StanceReroutes?.Values, finishTargets = move.FinishReroutes?.Values;
            foreach (string target in (stanceTargets ?? []).Concat(finishTargets ?? []))
            {
                if (!Moves.ContainsKey(target))
                    throw new Exception($"The move '{move.Id}' reroutes to '{target}', which isn't a move.");
            }
        }

        _inputMoves = [.. Moves.Values.Where(move => move.Priority >= 0).OrderBy(move => move.Priority)];
    }

    /// <summary>
    /// Works out how long every move takes for the character that has them, see <see cref="MoveFrameData"/>.
    /// </summary>
    /// <param name="animationLength">How many frames an animation of the character has.</param>
    /// <param name="frameRate">How many frames of animation a second the character plays at.</param>
    public void Bake(Func<string?, uint> animationLength, float frameRate)
    {
        foreach (FightingMove move in Moves.Values)
        {
            move.FrameData = MoveFrameData.Of(move, animationLength, frameRate);
        }
    }

    public bool TryGetMove(string id, out FightingMove move)
    {
        if (Moves.TryGetValue(id, out var found))
        {
            move = found;
            return true;
        }

        move = Idle;
        return false;
    }

    /// <summary>
    /// Finds the move the player is asking for, as per the order of precedence.
    /// </summary>
    /// <param name="signature">The input signature of the move which matched.</param>
    public bool TryMatchInput(InputBuffer input, Stance stance, out FightingMove move, out InputFlags signature)
    {
        foreach (FightingMove candidate in _inputMoves)
        {
            // Reject all moves that are not allowed in our current stance
            if ((candidate.Stances & stance) == 0) continue;

            if (input.TryMatch(candidate, out signature))
            {
                move = candidate;
                return true;
            }
        }

        move = Idle;
        signature = InputFlags.None;
        return false;
    }

    private static FightingMove ReadMove(string id, Dictionary<string, IRuntimeValue> properties)
    {
        var known = new HashSet<string>
        {
            "input", "trigger", "priority", "stances", "damage", "knockback", "stun", "interruptible", "steering",
            "turning", "status", "stance", "stance_after", "warns", "repeat", "phases", "stance_reroutes", "finish_reroutes"
        };

        foreach (string key in properties.Keys)
        {
            if (!known.Contains(key)) throw new Exception($"'{key}' isn't something a move has.");
        }

        int damage = (int)Number(properties, "damage", 0);

        // A move that can't be walked during can't be turned around in either, unless it says otherwise
        bool steering = Bool(properties, "steering", true);

        return new FightingMove
        {
            Id = id,
            Damage = damage,
            Knockback = properties.TryGetValue("knockback", out var knockback) ? Vector(knockback, "knockback") : Vector2.Zero,
            Stun = Number(properties, "stun", -1),
            Stances = properties.TryGetValue("stances", out var stances) ? Flags<Stance>(stances, "stances") : Stance.Standing,
            InputSignatures = ReadInputs(properties),
            Trigger = properties.TryGetValue("trigger", out var trigger) ? Named<InputTrigger>(Text(trigger, "trigger").Replace("_", ""), "trigger") : InputTrigger.Held,
            Priority = (int)Number(properties, "priority", -1),
            Interruptible = Bool(properties, "interruptible", true),
            AllowsSteering = steering,
            AllowsTurning = Bool(properties, "turning", steering),
            Status = properties.TryGetValue("status", out var status) ? Named<PlayerStatusType>(Text(status, "status"), "status") : null,
            Stance = properties.TryGetValue("stance", out var stance) ? Named<Stance>(Text(stance, "stance"), "stance") : null,
            StanceAfter = properties.TryGetValue("stance_after", out var after) ? Named<Stance>(Text(after, "stance_after"), "stance_after") : null,

            // A move that hurts gives the other player their chance to block, unless it says otherwise
            Warns = Bool(properties, "warns", damage > 0),
            Repeat = properties.TryGetValue("repeat", out var repeat) ? Named<MoveCondition>(Text(repeat, "repeat"), "repeat") : MoveCondition.None,
            Phases = ReadPhases(properties),
            StanceReroutes = ReadReroutes(properties, "stance_reroutes"),
            FinishReroutes = ReadReroutes(properties, "finish_reroutes")
        };
    }

    /// <summary>
    /// Helper method to read the inputs of a move: "A", alternatives as "DPadLeft | DPadRight" and buttons that go together as "A + B".
    /// </summary>
    private static InputFlags[] ReadInputs(Dictionary<string, IRuntimeValue> properties)
    {
        if (!properties.TryGetValue("input", out var value)) return [InputFlags.None];

        const StringSplitOptions tidy = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        var signatures = new List<InputFlags>();

        foreach (string alternative in Text(value, "input").Split('|', tidy))
        {
            InputFlags signature = InputFlags.None;
            foreach (string button in alternative.Split('+', tidy))
            {
                signature |= Named<InputFlags>(button, "input");
            }
            signatures.Add(signature);
        }

        return signatures.Count > 0 ? [.. signatures] : [InputFlags.None];
    }

    private static MovePhase[] ReadPhases(Dictionary<string, IRuntimeValue> properties)
    {
        if (!properties.TryGetValue("phases", out var value)) return [];
        if (value is not ObjectValue phases) throw new Exception("phases has to be an object.");

        var known = new HashSet<string> { "animation", "frames", "hit", "interrupt", "lock", "status", "status_frame", "loop_while", "until", "impulse", "effect", "effect_frames" };
        var read = new List<MovePhase>();

        // They run in the order they are written in
        foreach (var (name, phaseValue) in phases.Properties)
        {
            if (phaseValue is not ObjectValue { Properties: { } phase }) throw new Exception($"the phase '{name}' has to be an object.");

            foreach (string key in phase.Keys)
            {
                if (!known.Contains(key)) throw new Exception($"'{key}' isn't something a phase has ({name}).");
            }

            if (phase.TryGetValue("effect", out var effectName) && !MovePlayback.IsEffect(Text(effectName, "effect")))
                throw new Exception($"'{Text(effectName, "effect")}' isn't an effect ({name}).");

            read.Add(new MovePhase
            {
                Animation = phase.TryGetValue("animation", out var animation) ? Text(animation, "animation") : null,
                Frames = (uint)Number(phase, "frames", 0),
                Hit = (int)Number(phase, "hit", -1),
                Interrupt = (int)Number(phase, "interrupt", -1),
                Lock = (int)Number(phase, "lock", -1),
                Status = phase.TryGetValue("status", out var phaseStatus) ? Named<PlayerStatusType>(Text(phaseStatus, "status"), "status") : null,
                StatusFrame = (int)Number(phase, "status_frame", 0),
                While = phase.TryGetValue("loop_while", out var loop) ? Named<MoveCondition>(Text(loop, "loop_while"), "loop_while") : MoveCondition.None,
                Until = phase.TryGetValue("until", out var until) ? Named<MoveCondition>(Text(until, "until"), "until") : MoveCondition.None,
                Impulse = phase.TryGetValue("impulse", out var impulse) ? Vector(impulse, "impulse") : Vector2.Zero,
                Effect = phase.TryGetValue("effect", out var effect) ? Text(effect, "effect") : null,
                EffectFrames = (uint)Number(phase, "effect_frames", 1)
            });
        }

        return [.. read];
    }

    private static Dictionary<Stance, string>? ReadReroutes(Dictionary<string, IRuntimeValue> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value)) return null;
        if (value is not ObjectValue reroutes) throw new Exception($"{key} has to be an object.");

        var read = new Dictionary<Stance, string>();
        foreach (var (stance, target) in reroutes.Properties)
        {
            read[Named<Stance>(stance, key)] = Text(target, $"{key}.{stance}");
        }

        return read;
    }

    /* Reading values, all of them say what they wanted when they don't get it */

    private static float Number(Dictionary<string, IRuntimeValue> properties, string key, float otherwise)
    {
        if (!properties.TryGetValue(key, out var value)) return otherwise;
        return value is NumberValue number ? number.Value : throw new Exception($"{key} has to be a number.");
    }

    private static bool Bool(Dictionary<string, IRuntimeValue> properties, string key, bool otherwise)
    {
        if (!properties.TryGetValue(key, out var value)) return otherwise;
        return value is BooleanValue boolean ? boolean.Value : throw new Exception($"{key} has to be true or false.");
    }

    private static string Text(IRuntimeValue value, string what) =>
        value is StringValue text ? text.Value : throw new Exception($"{what} has to be a text.");

    private static Vector2 Vector(IRuntimeValue value, string what) =>
        value is Vector2Value vector ? vector.Value : throw new Exception($"{what} has to be a vec(x, y).");

    private static T Named<T>(string name, string what) where T : struct, Enum
    {
        // Numbers would get through as well, and mean nothing to whoever reads the file
        if (Enum.TryParse(name.Trim(), ignoreCase: true, out T value) && Enum.IsDefined(value) && !char.IsAsciiDigit(name.Trim()[0]))
            return value;

        throw new Exception($"'{name}' isn't a {what}, it can be: {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
    }

    /// <summary>
    /// Helper method to read values that can be several at once, "standing | crouching".
    /// </summary>
    private static T Flags<T>(IRuntimeValue value, string what) where T : struct, Enum
    {
        long flags = 0;
        foreach (string name in Text(value, what).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            flags |= Convert.ToInt64(Named<T>(name, what));
        }

        return (T)Enum.ToObject(typeof(T), flags);
    }
}

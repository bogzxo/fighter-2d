using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CumInstinctDuel.Player;

using Horizon.HIDL;
using Horizon.HIDL.Runtime;
using Horizon.Input;

using Silk.NET.Input;

namespace CumInstinctDuel.Logic;

internal class MoveList
{
    public Dictionary<string, FightingMove> Moves { get; init; } = new();
    public Dictionary<ButtonName[], string> MovesLookup { get; init; } = new();

    public FightingMove Idle { get; private set; }

    public MoveList(in string file="Assets/data/moves.hor")
    {
        HIDLRuntime runtime = new HIDLRuntime();
        runtime.GlobalScope.DeclareSystem("playerJump", new NativeFunctionValue());

        if (!File.Exists(file)) throw new Exception("Handle this better");

        var (success, _) = runtime.Evaluate(File.ReadAllText(file));
        if (!success) throw new Exception("Handle this better");

        ObjectValue movesValue = (ObjectValue)runtime.UserScope.Lookup("moves");
        ParseMoves(movesValue);
    }

    private void ParseMoves(ObjectValue movesValue)
    {
        foreach (var move in movesValue.Properties)
        {
            if (move.Value is ObjectValue moveObj)
            {
                string moveName = move.Key;
                string bindraw = ((StringValue)moveObj.Properties["binding"]).Value;
                string dirraw = ((StringValue)moveObj.Properties["direction"]).Value;
                string stanceraw = ((StringValue)moveObj.Properties["stance"]).Value;
                int damage = (int)((NumberValue)moveObj.Properties["damage"]).Value;
                bool interuptable = ((BooleanValue)moveObj.Properties["interruptible"]).Value;
                
                bool loopable = false;
                if (moveObj.Properties.ContainsKey("loopable"))
                {
                    loopable = ((BooleanValue)moveObj.Properties["loopable"]).Value;
                }

                AnonymousFunctionValue? callback = null;

                if (moveObj.Properties.ContainsKey("callback"))
                {
                    if (moveObj.Properties["callback"] is AnonymousFunctionValue cb)
                    {
                        callback = cb;
                    }
                }

                string animName = ((StringValue)((ObjectValue)moveObj.Properties["animation"]).Properties["name"]).Value;
                int animHit = (int)((NumberValue)((ObjectValue)moveObj.Properties["animation"]).Properties["hit"]).Value;
                int animDur = (int)((NumberValue)((ObjectValue)moveObj.Properties["animation"]).Properties["duration"]).Value;
               
                var (anyInput, bindings) = ParseBindings(bindraw);

                MovesLookup.Add(bindings, moveName);

                var fmove = new FightingMove
                {
                    Animation = new MoveAnimation
                    {
                        HitFrame = animHit,
                        Name = animName,
                        Duration = animDur
                    },
                    Name = moveName,
                    Damage = damage,
                    Stances = ParseStance(stanceraw),
                    Directions = ParseDirection(dirraw),
                    Bindings = bindings,
                    UseAnyBindings = anyInput,
                    Interuptable = interuptable,
                    Callback = callback,
                    Loopable = loopable,
                };

                if (moveName.Equals("standing"))
                {
                    Idle = fmove;
                }

                Moves.Add(moveName, fmove);

                Console.WriteLine();
            }
        }
    }

    private (bool any, ButtonName[]) ParseBindings(string bindraw)
    {
        if (string.Equals(bindraw, "none", StringComparison.OrdinalIgnoreCase)) return (false, []);
        List<ButtonName> bindings = [];

        if (bindraw.Contains('|'))
        {
            foreach (var dir in bindraw.Split('|'))
            {
                if (!Enum.TryParse(dir.Trim(), true, out ButtonName parsedBinding)) throw new Exception();

                bindings.Add(parsedBinding);
            }
            return (true,  [.. bindings]);
        }
        else
        {
            foreach (var dir in bindraw.Split('+'))
            {
                if (!Enum.TryParse(dir.Trim(), true, out ButtonName parsedBinding)) throw new Exception();

                bindings.Add(parsedBinding);
            }
            return (false, [.. bindings]);
        }

    }

    private Direction ParseDirection(string dirraw)
    {
        if (string.Equals(dirraw, "none", StringComparison.OrdinalIgnoreCase)) return Direction.None;

        Direction final = Direction.None;
        foreach (var dir in dirraw.Split('|'))
        {
            if (!Enum.TryParse(dir.Trim(), true, out Direction parsedDir)) throw new Exception();
            
            if (final == Direction.None) final = parsedDir;
            final |= parsedDir;
        }
        return final;
    }
    private Stance ParseStance(string stanceraw)
    {
        Stance final = Stance.Standing;
        foreach (var stance in stanceraw.Split('|'))
        {
            if (!Enum.TryParse(stance.Trim(), true, out Stance parsedStance)) throw new Exception();
            final |= parsedStance;
        }
        return final;
    }
}

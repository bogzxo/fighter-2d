namespace Fighter2D.Logic;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Horizon.Engine;
using Horizon.HIDL;
using Horizon.HIDL.Runtime;

using Silk.NET.Input;


internal class MoveList
{
    private const string FIGHTING_MOVES_FILE = "Assets/data/fighting_moves.hor";
    private const string MOVEMENT_MOVES_FILE = "Assets/data/movement_moves.hor";

    public Dictionary<string, FightingMove> FightingMoves { get; init; } = new();
    public Dictionary<string, FightingMove> MovingMoves { get; init; } = new();

    public Dictionary<string, FightingMove> AllMoves { get; init; } = new();

    // Changed key to string (joined bindings) because arrays pass by reference, breaking dictionary lookups.
    public Dictionary<string, string> MovesLookup { get; init; } = new();

    public FightingMove Idle { get; private set; }

    private readonly string _filePath = string.Empty;

    public FightingMove this[string index] => this.FightingMoves.TryGetValue(index, out var item) ? item : this.MovingMoves[index];

    public bool TryGetMove(in string index, out FightingMove move)
    {
        if (AllMoves.TryGetValue(index, out var fight))
        {
            move = fight;
            return true;
        }

        move = Idle;
        return false;
    }

    public MoveList()
    {
        HIDLRuntime runtime = new();
        runtime.GlobalScope.DeclareSystem("playerJump", new NativeFunctionValue());

        if (!File.Exists(FIGHTING_MOVES_FILE)) throw new FileNotFoundException($"Move list file not found: {FIGHTING_MOVES_FILE}");
        if (!File.Exists(MOVEMENT_MOVES_FILE)) throw new FileNotFoundException($"Move list file not found: {MOVEMENT_MOVES_FILE}");

        // Load fighting moves
        var (success_fight, _) = runtime.Evaluate(File.ReadAllText(FIGHTING_MOVES_FILE));
        if (!success_fight) throw new Exception("Failed to evaluate move list script.");

        // Extract and parse fighting moves
        ObjectValue movesValue = (ObjectValue)runtime.UserScope.Lookup("moves");
        ParseMoves(movesValue, FightingMoves);

        // Reset scope for further moves
        runtime.UserScope.Reset();

        // Load movement moves
        var (success_move, _) = runtime.Evaluate(File.ReadAllText(MOVEMENT_MOVES_FILE));
        if (!success_move) throw new Exception("Failed to evaluate move list script.");


        // Extract and parse movement moves
        movesValue = (ObjectValue)runtime.UserScope.Lookup("moves");
        ParseMoves(movesValue, MovingMoves);
    }

    private void ParseMoves(ObjectValue movesValue, Dictionary<string, FightingMove> dict)
    {
        foreach (var move in movesValue.Properties)
        {
            if (move.Value is not ObjectValue moveObj) continue;

            string moveName = move.Key;

            // Extracted safe property fetching
            string bindraw = GetStringProp(moveObj, "binding", "none");
            string dirraw = GetStringProp(moveObj, "direction", "none");
            string stanceraw = GetStringProp(moveObj, "stance", "standing");
            int damage = GetIntProp(moveObj, "damage", 0);
            bool interuptable = GetBoolProp(moveObj, "interruptible", false);
            bool loopable = GetBoolProp(moveObj, "loopable", false);
            bool doubleTap = GetBoolProp(moveObj, "double_tap", false);
            string? nextMove = GetStringProp(moveObj, "next_move", null);
            string? releaseMove = GetStringProp(moveObj, "release_move", null);

            AnonymousFunctionValue? callback = null;
            if (moveObj.Properties.TryGetValue("callback", out var cbVal) && cbVal is AnonymousFunctionValue cb)
            {
                callback = cb;
            }

            // Parse Stance Reroutes
            Dictionary<Stance, string>? stanceReroutes = null;
            if (moveObj.Properties.TryGetValue("stance_reroutes", out var srVal) && srVal is ObjectValue srObj)
            {
                stanceReroutes = new Dictionary<Stance, string>();
                foreach (var kvp in srObj.Properties)
                {
                    if (Enum.TryParse(kvp.Key, true, out Stance parsedStance) && kvp.Value is StringValue sv)
                    {
                        stanceReroutes[parsedStance] = sv.Value;
                    }
                }
            }

            var animObj = (ObjectValue)moveObj.Properties["animation"];
            string animName = GetStringProp(animObj, "name", "idle");
            int animHit = GetIntProp(animObj, "hit", 0);

            var (anyInput, bindings) = ParseBindings(bindraw);


            var fmove = new FightingMove
            {
                Name = moveName,
                Damage = damage,
                Animation = new MoveAnimation { HitFrame = animHit, Name = animName },
                Stances = ParseStance(stanceraw),
                Directions = ParseDirection(dirraw),
                Bindings = bindings,
                UseAnyBindings = anyInput,
                Interuptable = interuptable,
                Callback = callback,
                Loopable = loopable,
                DoubleTap = doubleTap,
                NextMove = nextMove,
                ReleaseMove = releaseMove,
                StanceReroutes = stanceReroutes
            };

            if (moveName.Equals("idle", StringComparison.OrdinalIgnoreCase))
            {
                Idle = fmove;
            }

            AllMoves.Add(moveName, fmove);
            dict.Add(moveName, fmove);
        }
    }

    #region Safe Property Parsers
    private static string? GetStringProp(ObjectValue obj, string key, string? defaultVal) =>
        obj.Properties.TryGetValue(key, out var val) && val is StringValue sv ? sv.Value : defaultVal;

    private static int GetIntProp(ObjectValue obj, string key, int defaultVal) =>
        obj.Properties.TryGetValue(key, out var val) && val is NumberValue nv ? (int)nv.Value : defaultVal;

    private static bool GetBoolProp(ObjectValue obj, string key, bool defaultVal) =>
        obj.Properties.TryGetValue(key, out var val) && val is BooleanValue bv ? bv.Value : defaultVal;
    #endregion

    private static (bool any, ButtonName[])  ParseBindings(string bindraw)
    {
        if (string.Equals(bindraw, "none", StringComparison.OrdinalIgnoreCase)) return (false, []);

        List<ButtonName> bindings = new();
        bool isOr = bindraw.Contains('|');
        char delimiter = isOr ? '|' : '+';

        foreach (var dir in bindraw.Split(delimiter))
        {
            if (Enum.TryParse(dir.Trim(), true, out ButtonName parsedBinding))
            {
                bindings.Add(parsedBinding);
            }
        }
        return (isOr, bindings.ToArray());
    }

    private static Direction ParseDirection(string dirraw)
    {
        if (string.Equals(dirraw, "none", StringComparison.OrdinalIgnoreCase)) return Direction.None;

        Direction final = Direction.None;
        foreach (var dir in dirraw.Split('|'))
        {
            if (Enum.TryParse(dir.Trim(), true, out Direction parsedDir))
            {
                final |= parsedDir;
            }
        }
        return final;
    }

    private static Stance ParseStance(string stanceraw)
    {
        Stance final = Stance.None;
        foreach (var stance in stanceraw.Split('|'))
        {
            if (Enum.TryParse(stance.Trim(), true, out Stance parsedStance))
            {
                final |= parsedStance;
            }
        }
        return final;
    }
}
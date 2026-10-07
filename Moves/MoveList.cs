using System;
using System.Collections.Generic;
using System.Linq;

using Fighter2D.Logic.Moves;

namespace Fighter2D.Logic;

/// <summary>
/// Every move a character has, read out of the move files of the game's content.
/// Nothing about a move lives in the code, so a game pack can change the ones there are or bring its own.
/// </summary>
internal class MoveList
{
    public Dictionary<string, FightingMove> Moves { get; } = [];

    // The moves an input can start, in the order they are matched in. First match wins.
    // A move that needs more than another has to come before it (double tap left before left)
    private FightingMove[] _inputMoves = [];

    public FightingMove Idle { get; private set; } = new();

    /// <summary>
    /// Helper method to read a move list out of its files, a move from a later file replaces the one of the same name from an earlier one.
    /// Throws (saying what is wrong and where) if a file is fucked.
    /// </summary>
    public static MoveList Load(params string[] files)
    {
        var list = new MoveList();

        foreach (string file in files)
        {
            foreach (FightingMove move in MoveFileReader.Read(file))
            {
                list.Moves[move.Id] = move;
            }
        }

        list.Validate();
        return list;
    }

    private void Validate()
    {
        if (!Moves.TryGetValue(MoveIds.IDLE, out var idle))
            throw new Exception($"There is no move called '{MoveIds.IDLE}', a player has to be able to do nothing.");

        Idle = idle;

        // A reroute to a move that doesn't exist would leave the player stuck, better to hear about it now
        foreach (FightingMove move in Moves.Values)
        {
            IEnumerable<string> targets = (move.StanceReroutes?.Values ?? Enumerable.Empty<string>()).Concat(move.FinishReroutes?.Values ?? Enumerable.Empty<string>());

            foreach (string target in targets)
            {
                if (!Moves.ContainsKey(target))
                    throw new Exception($"The move '{move.Id}' reroutes to '{target}', which isn't a move.");
            }
        }

        _inputMoves = [.. Moves.Values.Where(move => move.Priority >= 0).OrderBy(move => move.Priority)];
    }

    /// <summary>
    /// Works out the frame data of every move, which needs to know how long the animations of the character are.
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
    /// Finds the move the player is asking for, going by priority.
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
}

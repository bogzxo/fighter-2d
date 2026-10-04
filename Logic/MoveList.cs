using System.Collections.Generic;
using System.Numerics;

using Fighter2D.Logic.Moves;

using Silk.NET.Input;

namespace Fighter2D.Logic;

internal class MoveList
{
    public Dictionary<MoveId, FightingMove> FightMoves { get; init; } = new()
    {
        [MoveId.Block] = new FightingMove
        {
            Id = MoveId.Block,
            InputSignatures = [InputFlags.RightBumper],
            Stances = Stance.Standing | Stance.Crouching,
            Interruptible = true,
            AllowsSteering = false,
        },

        [MoveId.HitStun] = new FightingMove
        {
            Id = MoveId.HitStun,
            Stances = Stance.Standing | Stance.Crouching | Stance.Jumping | Stance.Falling,
            Interruptible = false,
            AllowsSteering = false,
        },

        [MoveId.JumpKick] = new FightingMove
        {
            Id = MoveId.JumpKick,
            Damage = 15,
            Knockback = new Vector2(900, 1900),
            StunDuration = 3.0f,
            Stances = Stance.Jumping | Stance.Falling,
            Interruptible = false,
            FinishReroutes = new Dictionary<Stance, MoveId>
            {
                { Stance.Standing, MoveId.Idle},
                { Stance.Falling, MoveId.Fall},
            }
        },
        [MoveId.KickLeft] = new FightingMove
        {
            Id = MoveId.KickLeft,
            Damage = 10,
            InputSignatures = [InputFlags.A],
            Trigger = InputTrigger.Pressed,
            Stances = Stance.Standing | Stance.Falling | Stance.Jumping | Stance.Crouching,
            Interruptible = false,
            StanceReroutes = new Dictionary<Stance, MoveId>
            {
                { Stance.Jumping, MoveId.JumpKick },
                { Stance.Falling, MoveId.JumpKick }
            }
        },
        [MoveId.KickRight] = new FightingMove
        {
            Id = MoveId.KickRight,
            Damage = 10,
            InputSignatures = [InputFlags.B],
            Trigger = InputTrigger.Pressed,
            Stances = Stance.Standing | Stance.Falling | Stance.Jumping | Stance.Crouching,
            Interruptible = false,
            StanceReroutes = new Dictionary<Stance, MoveId>
            {
                { Stance.Jumping, MoveId.JumpKick },
                { Stance.Falling, MoveId.JumpKick }
            }
        },
    };

    public Dictionary<MoveId, FightingMove> MovementMoves { get; init; } = new()
    {
        [MoveId.Idle] = new FightingMove
        {
            Id = MoveId.Idle,
            InputSignatures = [InputFlags.None],
            Interruptible = true,
            Stances = Stance.Standing
        },

        [MoveId.Crouch] = new FightingMove
        {
            Id = MoveId.Crouch,
            InputSignatures = [InputFlags.DPadDown],
            Interruptible = true,
            AllowsSteering = false,
            Stances = Stance.Crouching | Stance.Standing
        },

        [MoveId.Fall] = new FightingMove
        {
            Id = MoveId.Fall,
            Stances = Stance.Falling,
            Interruptible = true,
        },

        [MoveId.Run] = new FightingMove
        {
            Id = MoveId.Run,
            Interruptible = true,
            Stances = Stance.Standing,
            InputSignatures = [InputFlags.DPadLeft, InputFlags.DPadRight],
            FinishReroutes = new Dictionary<Stance, MoveId>
            {
                { Stance.Standing, MoveId.Idle}
            }
        },
        [MoveId.Jump] = new FightingMove
        {
            Id = MoveId.Jump,
            InputSignatures = [InputFlags.DPadUp],
            Interruptible = true,
            Stances = Stance.Standing | Stance.Crouching,
            FinishReroutes = new Dictionary<Stance, MoveId>
            {
                { Stance.Falling, MoveId.Fall}
            }
        },

        [MoveId.DodgeRoll] = new FightingMove
        {
            Id = MoveId.DodgeRoll,
            InputSignatures = [InputFlags.DPadLeft, InputFlags.DPadRight],
            Trigger = InputTrigger.DoubleTap,
            Stances = Stance.Standing | Stance.Crouching,
            Interruptible = true,
            AllowsSteering = false,
        },
    };

    // The order of precedence in which the moves are matched against the player input, the first match wins.
    // Moves that need more than what another move needs have to come before it (double tap left before left etc.)
    public MoveId[] InputPriority { get; init; } =
    [
        MoveId.DodgeRoll,
        MoveId.KickLeft,
        MoveId.KickRight,
        MoveId.Block,
        MoveId.Jump,
        MoveId.Crouch,
        MoveId.Run,
    ];

    public FightingMove Idle => MovementMoves[MoveId.Idle];

    // InputPriority as the moves themselves, looked up once rather than on every update
    private FightingMove[]? _inputMoves;


    public bool TryGetMove(MoveId id, out FightingMove move)
    {
        if (MovementMoves.TryGetValue(id, out var found) || FightMoves.TryGetValue(id, out found))
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
        _inputMoves ??= Array.ConvertAll(InputPriority, id => TryGetMove(id, out var found) ? found : Idle);

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

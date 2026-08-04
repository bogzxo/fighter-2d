using System.Collections.Generic;

using Fighter2D.Logic.Moves;

using Silk.NET.Input;

namespace Fighter2D.Logic;

internal class MoveList
{
    public Dictionary<MoveId, FightingMove> AllMoves { get; init; } = new();

    public FightingMove Idle => AllMoves[MoveId.Idle];

    public MoveList()
    {
        // 1. Define the moves
        AllMoves = new Dictionary<MoveId, FightingMove>
        {
            [MoveId.Idle] = new FightingMove
            {
                Id = MoveId.Idle,
                AnimationName = "idle",
                Interruptible = true
            },

            [MoveId.KickLeft] = new FightingMove
            {
                Id = MoveId.KickLeft,
                Damage = 10,
                InputSignature = InputFlags.A,
                Stances = Stance.Standing | Stance.Crouching,
                Interruptible = false,
                StanceReroutes = new Dictionary<Stance, MoveId>
                {
                    { Stance.Jumping, MoveId.JumpKick },
                    { Stance.Falling, MoveId.JumpKick }
                }
            },

            [MoveId.JumpKick] = new FightingMove
            {
                Id = MoveId.JumpKick,
                Damage = 15,
                Stances = Stance.Jumping | Stance.Falling,
                Interruptible = false
            },

            [MoveId.Block] = new FightingMove
            {
                Id = MoveId.Block,
                AnimationName = "block",
                Interruptible = true
            },

            [MoveId.Jump] = new FightingMove
            {
                Id = MoveId.Jump,
                InputSignature = InputFlags.DPadUp,
                Interruptible = true
            },

            [MoveId.Run] = new FightingMove
            {
                Id = MoveId.Run,
                Interruptible = true
            },

            [MoveId.Crouch] = new FightingMove
            {
                Id = MoveId.Crouch,
                InputSignature = InputFlags.DPadDown,
                Interruptible = true
            },

            [MoveId.DodgeRoll] = new FightingMove
            {
                Id = MoveId.DodgeRoll,
                DoubleTap = true,
                Interruptible = true
            }
        };
    }

    public bool TryGetMove(MoveId id, out FightingMove move)
    {
        return AllMoves.TryGetValue(id, out move!);
    }
}
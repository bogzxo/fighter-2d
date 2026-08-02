using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Fighter2D.Logic;

using Horizon.Engine;

namespace Fighter2D.Player.Controllers
{
    internal class GamepadPlayerController(int index, MoveList moveList) : PlayerController(moveList)
    {
        private readonly PlayerInputTracker _input = new(index);

        internal override bool IsMoveHeld(FightingMove candidate)
        {
            if (candidate.Bindings.Length == 0) return false;

            return candidate.UseAnyBindings
                ? candidate.Bindings.Any(_input.IsButtonHeld)
                : candidate.Bindings.All(_input.IsButtonHeld);
        }

        public override void TryProcessNewInputs(float dt)
        {
            var buttons = _input.ConsumeFramePresses();
            if (buttons.Length == 0 || !CurrentMove.Interuptable) return;

            foreach (var candidate in MoveList.Moves.Values)
            {
                if (candidate.Bindings.Length == 0) continue;

                bool matched = candidate.UseAnyBindings
                    ? candidate.Bindings.Any(buttons.Contains)
                    : candidate.Bindings.All(buttons.Contains);

                if (matched)
                {
                    FightingMove moveToExecute = candidate;

                    if (!IsStanceValid(candidate.Stances))
                    {
                        if (candidate.StanceReroutes != null &&
                            candidate.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out string? reroutedName) &&
                            MoveList.Moves.TryGetValue(reroutedName, out var reroutedMove))
                        {
                            moveToExecute = reroutedMove;
                        }
                        else continue;
                    }

                    if (CurrentMove.Name == moveToExecute.Name) continue;

                    if (candidate.DoubleTap || moveToExecute.DoubleTap)
                    {
                        bool isDoubleTap = false;
                        foreach (var btn in candidate.Bindings.Where(buttons.Contains))
                        {
                            if (_input.HasDoubleTap(btn, 0.3f))
                            {
                                isDoubleTap = true;
                                _input.ClearButtonHistory(btn);
                                break;
                            }
                        }
                        if (!isDoubleTap) continue;
                    }

                    ChangeToMove(moveToExecute);
                    return;
                }
            }
        }

        public override void Update(float dt)
        {
            _input.Update(dt);

            var movementDir = _input.GetMovementInput();

            if (movementDir.X != 0)
            {
                Player.Flipped = movementDir.X < 0;
            }

            // Apply velocity-based movement with damping
            if (movementDir.X != 0)
            {
                var targetVelocity = movementDir.X * PlayerConfig.WALK_SPEED;
                var currentVelocityX = Player.PhysicsBody.Velocity.X;
                var velocityDiff = targetVelocity - currentVelocityX;

                // Apply force proportional to velocity difference for responsive control
                Player.PhysicsBody.ApplyForce(new Vector2(velocityDiff * Player.PhysicsBody.Mass * 5f, 0));
            }
        }
    }
}
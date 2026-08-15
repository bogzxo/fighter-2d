using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;
using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;

internal class DummyPlayerController : PlayerController
{
    public override void TryProcessNewInputs(float dt)
    {
        // Test our hitbox intersection with the controlled player, and if they are performing a kick, we will block it.
        if (this.Player.HitboxFixture.TestIntersection(FightScene.ControlledPlayer.HitboxFixture, this.Player.Transform.Position, FightScene.ControlledPlayer.Transform.Position))
        {
            if (CurrentMove != MoveList.Idle) return;

            if (FightScene.ControlledPlayer.Controller.CurrentMove is { Id: MoveId.KickLeft or MoveId.KickRight} )
            {
                ChangeToMove(MoveId.Block);
            }
            else if (FightScene.ControlledPlayer.Controller.CurrentMove.Id == MoveId.Idle)
            {
                ChangeToMove(MoveId.CounterAttack);
            }
            else if (FightScene.ControlledPlayer.Controller.CurrentMove.Id == MoveId.DodgeRoll)
            {
                ChangeToMove(MoveId.Jump);
            }
        }
    }

    public override bool IsButtonHeld(ButtonName btn)
    {
        return false;
    }

    public override void UpdatePhysics(float dt)
    {
        this.Player.Flipped = this.Player.Transform.Position.X > FightScene.ControlledPlayer.Transform.Position.X;
    }
}
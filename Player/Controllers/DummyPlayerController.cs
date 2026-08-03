using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Fighter2D.Logic;
using Fighter2D.Scenes;

namespace Fighter2D.Player.Controllers;

internal class DummyPlayerController(MoveList moveList) : PlayerController(moveList)
{
    internal override bool IsMoveHeld(FightingMove candidate)
    {
        return false;
    }

    public override void TryProcessNewInputs(float dt)
    {
        // Test our hitbox intersection with the controlled player, and if they are performing a kick, we will block it.
        if (this.Player.HitboxFixture.TestIntersection(FightScene.ControlledPlayer.HitboxFixture, this.Player.Transform.Position, FightScene.ControlledPlayer.Transform.Position))
        {
            if (CurrentMove != MoveList.Idle) return;

            if (FightScene.ControlledPlayer.Controller.CurrentMove.Name == "kick")
            {
                ChangeToMove(MoveList["block"]);
            }
            else if (FightScene.ControlledPlayer.Controller.CurrentMove.Name == "idle")
            {
                ChangeToMove(MoveList["counter_attack"]);
            }
        }
    }

    public override void Update(float dt)
    {
        this.Player.Flipped = this.Player.Transform.Position.X > FightScene.ControlledPlayer.Transform.Position.X;
    }
}

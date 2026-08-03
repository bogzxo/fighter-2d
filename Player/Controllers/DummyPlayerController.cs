using System;
using System.Collections.Generic;
using System.Text;
using Fighter2D.Logic;

namespace Fighter2D.Player.Controllers;

internal class DummyPlayerController(MoveList moveList) : PlayerController(moveList)
{
    internal override bool IsMoveHeld(FightingMove candidate)
    {
        return false;
    }

    public override void TryProcessNewInputs(float dt)
    {

    }

    public override void Update(float dt)
    {

    }
}

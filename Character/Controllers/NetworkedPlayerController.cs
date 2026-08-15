using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Scenes;
using Silk.NET.Input;

namespace Fighter2D.Character.Controllers;

internal class NetworkedPlayerController : PlayerController
{
    public override void TryProcessNewInputs(float dt)
    {
        
    }

    public override bool IsButtonHeld(ButtonName btn)
    {
        return false;
    }

    public override void UpdatePhysics(float dt)
    {

    }
}
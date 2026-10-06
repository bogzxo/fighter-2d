using System.Numerics;
using Bogz.Logging.Loggers;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Scenes;
using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Character;

/// <summary>
/// Representation of a remote player connected via Riptide network.
/// </summary>
internal class NetworkPlayer : Player
{
    public readonly string Address;

    public NetworkPlayer(string address)
    {
        this.Address = address;
        Controller = new NetworkedPlayerController();
    }
}
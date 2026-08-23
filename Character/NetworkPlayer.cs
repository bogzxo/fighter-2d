using System.Numerics;
using Bogz.Logging.Loggers;
using Egui;
using Egui.Containers;
using Egui.Widgets;
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

    public override void RenderUi(Ui root)
    {
        base.RenderUi(root);

        new Window("Network Player")
            .Show(root.Ctx, ui =>
            {
                ui.Heading("Player Information");
                ui.Label($"Address: {Address}");
                ui.Label($"Current Move: {Controller.CurrentMove.Id}");
                ui.Label($"Current Stance: {Controller.StateTracker.CurrentStance}");
                ui.Label($"Current Status: {Controller.StateTracker.CurrentStatus}");
                ui.Label($"Is Grounded: {Controller.StateTracker.IsGrounded}");
            });
    }
}
using System.Numerics;
using Bogz.Logging.Loggers;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Scenes;
using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;
using ImGuiNET;

namespace Fighter2D.Character;

internal class NetworkPlayer : Player
{
    public readonly string Address;

    public NetworkPlayer(string address)
    {
        this.Address = address;
        Controller = AddComponent(new NetworkedPlayerController());
    }

    public override void Render(float dt, object? obj = null)
    {
        this.PhysicsBody.Enabled = false;
        base.Render(dt, obj);

        if (ImGui.Begin("Network Player"))
        {
            // Display all relevant player information

            ImGui.Text("Player Information");
            ImGui.Text($"Current Move: {Controller.CurrentMove.Id}");
            ImGui.Text($"Current Stance: {Controller.StateTracker.CurrentStance}");
            ImGui.Text($"Current Status: {Controller.StateTracker.CurrentStatus}");
            ImGui.Text($"Is Grounded: {Controller.StateTracker.IsGrounded}");
        }
    }
}
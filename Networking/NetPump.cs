using Horizon.Engine;

namespace Fighter2D.Networking;

/// <summary>
/// Keeps the open session going whatever scene is up, the engine updates this once per update.
/// </summary>
internal sealed class NetPump : GameObject
{
    public NetPump()
    {
        Name = "Net Pump";
    }

    public override void UpdateState(float dt)
    {
        NetSession.Active?.Update();
        base.UpdateState(dt);
    }

    protected override void DisposeOther()
    {
        NetSession.Active?.Dispose();
        base.DisposeOther();
    }
}

using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// Whoever has to hear about our hits and whiffs on another machine, the netcode of an online fight. Null in the context
/// of a fight on one machine, there is nobody to tell.
/// </summary>
internal interface IHitReporter
{
    void SendHit(string move, float direction, Vector2 impact);
    void SendWhiff(string move);
}

using System.Numerics;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// A hit that landed on the network player, as whoever threw it saw it. Whether it really counts is up to the machine that got hit.
/// </summary>
/// <param name="Direction">The way the hit was going, -1 for left and 1 for right.</param>
/// <param name="Impact">Where it landed.</param>
internal readonly record struct HitReport(string Move, float Direction, Vector2 Impact)
{
    public void Write(Message message)
    {
        // Both machines have the same move files (the lobby saw to that), so the name is all the other one needs
        message.AddString(Move);
        message.AddFloat(Direction);
        message.AddFloat(Impact.X);
        message.AddFloat(Impact.Y);
    }

    public static HitReport Read(Message message) => new(message.GetString(), message.GetFloat(), new Vector2(message.GetFloat(), message.GetFloat()));
}

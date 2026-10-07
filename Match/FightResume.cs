using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Networking;

namespace Fighter2D.Match;

/// <summary>
/// How one fighter was doing when a fight was taken apart, which is all of them that carries over into the fight that is put back together.
/// </summary>
internal readonly record struct FighterResume(byte Health, Vector2 Position)
{
    public static FighterResume Of(Player player) => new(player.Health, player.PhysicsBody?.Position ?? player.SpawnPosition);
}

/// <summary>
/// Where a fight was when its data got reloaded, so the fight that is built from the fresh data carries on from there
/// instead of starting over. The rounds, the clock, and how and where both fighters were.
/// Whatever they were in the middle of doing is gone, a move that was half way through might not even exist any more.
/// </summary>
/// <param name="Round">How the match stood, with player one being whoever sits at this machine.</param>
internal sealed record FightResume(RoundSnapshot Round, FighterResume One, FighterResume Two);

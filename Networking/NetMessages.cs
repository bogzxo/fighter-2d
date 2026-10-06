using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Match;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// Where a player is and what they are doing, as the machine that plays them says it. Sent many times a second.
/// </summary>
internal readonly record struct PlayerSnapshot(
    Vector2 Position,
    Vector2 Velocity,
    bool Flipped,
    string MoveId,
    string Animation,
    uint Frame,
    Stance Stance,
    PlayerStatusType Status,
    bool IsGrounded,
    byte Health)
{
    /// <summary>
    /// Helper method to take down how a player of this machine is doing right now.
    /// </summary>
    public static PlayerSnapshot Of(Player player) => new(
        player.Transform.Position,
        player.PhysicsBody?.Velocity ?? Vector2.Zero,
        player.Flipped,
        player.Controller?.CurrentMove?.Id ?? MoveIds.NONE,
        player.Controller?.ActiveAnimation ?? "idle",
        player.AnimationManager[player!.Controller?.ActiveAnimation].index,
        player.Controller?.StateTracker?.CurrentStance ?? Stance.Standing,
        player.Controller?.StateTracker?.CurrentStatus ?? PlayerStatusType.Normal,
        player.Controller?.StateTracker?.IsGrounded ?? true,
        player.Health);

    public void Write(Message message)
    {
        message.AddFloat(Position.X);
        message.AddFloat(Position.Y);
        message.AddFloat(Velocity.X);
        message.AddFloat(Velocity.Y);
        message.AddBool(Flipped);
        message.AddString(MoveId);
        message.AddString(Animation);
        message.AddUInt(Frame);
        message.AddByte((byte)Stance);
        message.AddByte((byte)Status);
        message.AddBool(IsGrounded);
        message.AddByte(Health);
    }

    public static PlayerSnapshot Read(Message message) => new(
        new Vector2(message.GetFloat(), message.GetFloat()),
        new Vector2(message.GetFloat(), message.GetFloat()),
        message.GetBool(),
        message.GetString(),
        message.GetString(),
        message.GetUInt(),
        (Stance)message.GetByte(),
        (PlayerStatusType)message.GetByte(),
        message.GetBool(),
        message.GetByte());
}

/// <summary>
/// A move that landed on the player of the other machine, as whoever threw it saw it. What comes of it is up to the machine that got hit.
/// </summary>
/// <param name="Direction">The way the hit was going, -1 for left and 1 for right.</param>
internal readonly record struct HitReport(string Move, float Direction)
{
    public void Write(Message message)
    {
        // Both machines have the same move files (the lobby saw to that), so the name is all the other one needs
        message.AddString(Move);
        message.AddFloat(Direction);
    }

    public static HitReport Read(Message message) => new(message.GetString(), message.GetFloat());
}

/// <summary>
/// How the match stands, as the host says it: the phase it is in, the clock and how every round so far ended.
/// Player one in here is the host, the other machine turns it round before it goes by it (see <see cref="Mirrored"/>).
/// </summary>
/// <param name="Forfeit">Who won because the other one left (0 or 1), -1 if nobody did.</param>
internal readonly record struct RoundSnapshot(
    RoundPhase Phase,
    float PhaseTime,
    float TimeLeft,
    RoundOutcome[] Rounds,
    RoundOutcome? LastOutcome,
    bool TimedOut,
    int Forfeit)
{
    // What is written where there is nothing to write
    private const byte NONE = byte.MaxValue;

    /// <summary>
    /// Helper method to see the match from the other side: every round player one took is one player two took, and the other way round.
    /// </summary>
    public RoundSnapshot Mirrored()
    {
        var rounds = new RoundOutcome[Rounds.Length];
        for (int i = 0; i < rounds.Length; i++) rounds[i] = Mirror(Rounds[i]);

        return this with
        {
            Rounds = rounds,
            LastOutcome = LastOutcome is RoundOutcome last ? Mirror(last) : null,
            Forfeit = Forfeit < 0 ? Forfeit : 1 - Forfeit
        };
    }

    private static RoundOutcome Mirror(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.PlayerOne => RoundOutcome.PlayerTwo,
        RoundOutcome.PlayerTwo => RoundOutcome.PlayerOne,
        _ => outcome
    };

    public void Write(Message message)
    {
        message.AddByte((byte)Phase);
        message.AddFloat(PhaseTime);
        message.AddFloat(TimeLeft);

        message.AddByte((byte)Rounds.Length);
        foreach (RoundOutcome round in Rounds) message.AddByte((byte)round);

        message.AddByte(LastOutcome is RoundOutcome last ? (byte)last : NONE);
        message.AddBool(TimedOut);
        message.AddByte(Forfeit < 0 ? NONE : (byte)Forfeit);
    }

    public static RoundSnapshot Read(Message message)
    {
        var phase = (RoundPhase)message.GetByte();
        float phaseTime = message.GetFloat();
        float timeLeft = message.GetFloat();

        var rounds = new RoundOutcome[message.GetByte()];
        for (int i = 0; i < rounds.Length; i++) rounds[i] = (RoundOutcome)message.GetByte();

        byte last = message.GetByte();
        bool timedOut = message.GetBool();
        byte forfeit = message.GetByte();

        return new RoundSnapshot(phase, phaseTime, timeLeft, rounds, last == NONE ? null : (RoundOutcome)last, timedOut, forfeit == NONE ? -1 : forfeit);
    }
}

/// <summary>
/// How the rules of a match go into a message and come back out of one, the host sends them along when it starts the fight.
/// </summary>
internal static class MatchRulesMessage
{
    public static void Write(Message message, MatchRules rules)
    {
        message.AddByte((byte)rules.RoundsToWin);
        message.AddUShort((ushort)rules.RoundSeconds);
        message.AddByte((byte)rules.Weather);
    }

    public static MatchRules Read(Message message) => new()
    {
        // Never fewer than one round, whatever the other machine says
        RoundsToWin = System.Math.Max(1, (int)message.GetByte()),
        RoundSeconds = message.GetUShort(),
        Weather = (WeatherSetting)message.GetByte()
    };
}

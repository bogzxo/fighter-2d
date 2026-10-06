using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Character.Controllers;
using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using Fighter2D.Match;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// The buttons a player held on the last few ticks of their fight, as the machine that plays them says it. Sent for
/// every tick, and each one repeats the ones before it: one that gets lost is made up for by the next.
/// </summary>
/// <param name="Tick">The tick the first of the buttons were held on, each one after it is a tick older.</param>
internal readonly record struct InputReport(uint Tick, InputFlags[] NewestFirst)
{
    // How many ticks every message carries, which is how many of them in a row can get lost without a press going missing
    public const int TICKS = 8;

    /// <summary>
    /// Helper method to take down what a player of this machine held up to the tick they are on.
    /// </summary>
    public static InputReport Of(PlayerController controller)
    {
        var held = new InputFlags[Math.Min(TICKS, controller.Tick)];
        for (uint i = 0; i < held.Length; i++) held[i] = controller.InputAt(controller.Tick - i);

        return new InputReport(controller.Tick, held);
    }

    public void Write(Message message)
    {
        message.AddUInt(Tick);
        message.AddByte((byte)NewestFirst.Length);
        foreach (InputFlags held in NewestFirst) message.AddUShort((ushort)held);
    }

    public static InputReport Read(Message message)
    {
        uint tick = message.GetUInt();

        var held = new InputFlags[message.GetByte()];
        for (int i = 0; i < held.Length; i++) held[i] = (InputFlags)message.GetUShort();

        return new InputReport(tick, held);
    }
}

/// <summary>
/// How a player is doing as the machine that plays them says it: where they are, and the exact frame of the move they
/// are in. Sent a few times a second, the other machine plays them by their buttons in between and is put right by this.
/// </summary>
/// <param name="Tick">The tick of their fight this was taken down on.</param>
/// <param name="Phase">The phase of the move they are in, and <paramref name="Frame"/> how far into it they are (see MovePlayback).</param>
/// <param name="Animation">The animation that is showing, and <paramref name="Shown"/> the frame of it.</param>
/// <param name="HitsSeen">How many blows of the other machine they have heard of, which tells it whether this is from before or after its last one.</param>
internal readonly record struct PlayerSnapshot(
    uint Tick,
    Vector2 Position,
    Vector2 Velocity,
    bool Flipped,
    string MoveId,
    int Phase,
    int Frame,
    string Animation,
    uint Shown,
    PlayerStatusType Status,
    int StunTicks,
    int ComboHits,
    bool CanInterrupt,
    bool HasStruck,
    byte Health,
    byte HitsSeen)
{
    /// <summary>
    /// Helper method to take down how a player of this machine is doing right now.
    /// </summary>
    public static PlayerSnapshot Of(Player player, byte hitsSeen)
    {
        PlayerController controller = player.Controller;

        return new(
            controller.Tick,
            player.PhysicsBody.Position,
            player.PhysicsBody.Velocity,
            player.Flipped,
            controller.CurrentMove.Id,
            controller.Playback.Phase,
            (int)controller.Playback.Frame,
            controller.ActiveAnimation,
            controller.Playback.Shown,
            controller.StateTracker.CurrentStatus,
            controller.StateTracker.StunTicks,
            controller.StateTracker.ComboHits,
            controller.CanInterrupt,
            controller.HasStruck,
            player.Health,
            hitsSeen);
    }

    public void Write(Message message)
    {
        message.AddUInt(Tick);
        message.AddFloat(Position.X);
        message.AddFloat(Position.Y);
        message.AddFloat(Velocity.X);
        message.AddFloat(Velocity.Y);
        message.AddBool(Flipped);
        message.AddString(MoveId);
        message.AddSByte((sbyte)Math.Clamp(Phase, sbyte.MinValue, sbyte.MaxValue));
        message.AddUShort((ushort)Math.Clamp(Frame, 0, ushort.MaxValue));
        message.AddString(Animation);
        message.AddUShort((ushort)Math.Min(Shown, ushort.MaxValue));
        message.AddByte((byte)Status);
        message.AddUShort((ushort)Math.Clamp(StunTicks, 0, ushort.MaxValue));
        message.AddByte((byte)Math.Clamp(ComboHits, 0, byte.MaxValue));
        message.AddBool(CanInterrupt);
        message.AddBool(HasStruck);
        message.AddByte(Health);
        message.AddByte(HitsSeen);
    }

    public static PlayerSnapshot Read(Message message) => new(
        message.GetUInt(),
        new Vector2(message.GetFloat(), message.GetFloat()),
        new Vector2(message.GetFloat(), message.GetFloat()),
        message.GetBool(),
        message.GetString(),
        message.GetSByte(),
        message.GetUShort(),
        message.GetString(),
        message.GetUShort(),
        (PlayerStatusType)message.GetByte(),
        message.GetUShort(),
        message.GetByte(),
        message.GetBool(),
        message.GetBool(),
        message.GetByte(),
        message.GetByte());
}

/// <summary>
/// A move that landed on the player of the other machine, as whoever threw it saw it. What comes of it is up to the machine that got hit.
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

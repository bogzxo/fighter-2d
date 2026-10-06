using Fighter2D.Match;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// How the match stands, as the host says it. The phase it is in, the clock and how every round so far ended.
/// Player one in here is the host, so the other machine flips it before using it (see <see cref="Mirrored"/>).
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
    // What gets written where there is nothing to write
    private const byte NONE = byte.MaxValue;

    /// <summary>
    /// Helper method to see the match from the other side. Every round player one took is one player two took and the other way round.
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

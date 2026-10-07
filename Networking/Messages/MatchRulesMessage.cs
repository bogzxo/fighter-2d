using System;

using Riptide;

namespace Fighter2D.Networking;

/// <summary>
/// How the rules of a match go into a message and come back out of one. The host sends them along when it starts the fight.
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
        RoundsToWin = Math.Max(1, (int)message.GetByte()),
        RoundSeconds = message.GetUShort(),
        Weather = (WeatherSetting)message.GetByte()
    };
}

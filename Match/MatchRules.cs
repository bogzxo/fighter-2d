using System;
using System.Numerics;

namespace Fighter2D.Match;

internal enum WeatherSetting
{
    // Whatever maps.hor says the map has
    Map,

    // Nothing in the air and no storm, whatever the map says
    Clear,

    // What the map has in the air, with thunder and lightning on top of it
    Storm
}

/// <summary>
/// What a match is played by: how many rounds, how long each of them lasts and what the weather is doing.
/// The players settle on these on the screen after the map is picked (MatchRulesScene), a fight that never saw that screen plays by the defaults.
/// </summary>
internal sealed class MatchRules
{
    // What there is to choose from on the rules screen, in the order it is stepped through
    public static readonly int[] RoundChoices = [1, 2, 3, 4, 5];
    public static readonly int[] TimeChoices = [60, 99, 180, 0];
    public static readonly WeatherSetting[] WeatherChoices = [WeatherSetting.Map, WeatherSetting.Clear, WeatherSetting.Storm];

    // The storm of a map that has none of its own, for when the players ask for one anyway
    private static readonly MapLoader.StormDefinition DefaultStorm = new()
    {
        Enabled = true,
        MinInterval = 5.0f,
        MaxInterval = 12.0f,
        Color = new Vector3(0.75f, 0.82f, 1.0f),
        Brightness = 0.35f,
        Shake = 2.0f
    };

    /// <summary>
    /// How many rounds it takes to win the match, whoever gets there first has won it.
    /// </summary>
    public int RoundsToWin { get; set; } = 3;

    /// <summary>
    /// How many rounds the match can go to at the most, which is when both players are one round short of winning it.
    /// </summary>
    public int Rounds => RoundsToWin * 2 - 1;

    /// <summary>
    /// How long a round lasts in seconds, 0 for a round that only ends when somebody goes down.
    /// </summary>
    public int RoundSeconds { get; set; } = 99;

    public WeatherSetting Weather { get; set; } = WeatherSetting.Map;

    public bool IsTimed => RoundSeconds > 0;

    /// <summary>
    /// Helper method to make the map the way the rules want it, which is its weather. The map itself is left as it is.
    /// </summary>
    public MapLoader.MapDefinition Apply(MapLoader.MapDefinition map) => Weather switch
    {
        WeatherSetting.Clear => map with
        {
            Ambience = map.Ambience with { Rate = 0.0f },
            Storm = map.Storm with { Enabled = false }
        },
        WeatherSetting.Storm => map with { Storm = map.Storm.Enabled ? map.Storm : DefaultStorm },
        _ => map
    };

    /// <summary>
    /// Helper methods to write the rules out the way the menus and the HUD show them.
    /// </summary>
    public static string DescribeRounds(int roundsToWin) => roundsToWin == 1 ? "Single round" : $"First to {roundsToWin}";

    public static string DescribeTime(int seconds) => seconds > 0 ? $"{seconds} seconds" : "No time limit";

    public static string DescribeWeather(WeatherSetting weather) => weather switch
    {
        WeatherSetting.Clear => "Clear skies",
        WeatherSetting.Storm => "Thunderstorm",
        _ => "As the map has it"
    };
}

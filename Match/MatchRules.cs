using Fighter2D.Map;

namespace Fighter2D.Match;

internal enum WeatherSetting
{
    // Whatever maps.hor says the map has
    Map,

    // Nothing in the air and no storm, whatever the map says
    Clear,

    // What the map has in the air, with thunder and lightning on top
    Storm
}

/// <summary>
/// The rules a match is played by, which is how many rounds, how long each one lasts and what the weather is doing.
/// The players pick these on the screen after the map (MatchRulesScene), a fight that never saw that screen plays by the defaults.
/// </summary>
internal sealed class MatchRules
{
    // What there is to choose from on the rules screen, in the order it is stepped through
    public static readonly int[] RoundChoices = [1, 2, 3, 4, 5];
    public static readonly int[] TimeChoices = [60, 99, 180, 0];
    public static readonly WeatherSetting[] WeatherChoices = [WeatherSetting.Map, WeatherSetting.Clear, WeatherSetting.Storm];

    /// <summary>
    /// How many rounds it takes to win the match, whoever gets there first has won it.
    /// </summary>
    public int RoundsToWin { get; set; } = 3;

    /// <summary>
    /// How many rounds the match can go to at the most, which is when both players are one round short of winning.
    /// </summary>
    public int Rounds => RoundsToWin * 2 - 1;

    /// <summary>
    /// How long a round lasts in seconds, 0 for a round that only ends when somebody goes down.
    /// </summary>
    public int RoundSeconds { get; set; } = 99;

    public WeatherSetting Weather { get; set; } = WeatherSetting.Map;

    public bool IsTimed => RoundSeconds > 0;

    /// <summary>
    /// Helper method to change the weather of a map to what the rules want. The map itself is left as it is.
    /// </summary>
    public MapDefinition Apply(MapDefinition map) => Weather switch
    {
        WeatherSetting.Clear => map with
        {
            Ambience = map.Ambience with { Rate = 0.0f },
            Storm = map.Storm with { Enabled = false }
        },
        WeatherSetting.Storm => map with { Storm = map.Storm.Enabled ? map.Storm : MapLoader.DefaultStorm },
        _ => map
    };

    /* How the rules are written out in the menus and on the HUD */

    public static string DescribeRounds(int roundsToWin) => roundsToWin == 1 ? "Single round" : $"First to {roundsToWin}";

    public static string DescribeTime(int seconds) => seconds > 0 ? $"{seconds} seconds" : "No time limit";

    public static string DescribeWeather(WeatherSetting weather) => weather switch
    {
        WeatherSetting.Clear => "Clear skies",
        WeatherSetting.Storm => "Thunderstorm",
        _ => "As the map has it"
    };
}

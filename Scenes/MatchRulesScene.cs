using Fighter2D.Map;
using Fighter2D.Match;

using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the rules of the match are picked, between the map and the fight. How many rounds, how long they last and what the weather does.
/// Every rule is a row of the screen, see <see cref="SettingList.Add"/> for what it takes to add another one.
/// In an online fight this is the host's job like the map before it, the other player gets the rules along with the map when the fight starts.
/// </summary>
internal class MatchRulesScene(MatchSetup setup, MapDefinition mapDefinition) : MenuScene
{
    private const string HINT = "[icon:dpad] choose and change    [icon:pad_a] fight    [icon:pad_b] back";

    protected override string LayoutFile => MenuLayouts.MATCH_RULES;
    protected override float InputDelay => 0.3f;

    private readonly SettingList _rules = new();
    private Label _hint = null!;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/match_rules.hor, what its rows choose between is decided here
        MatchRules rules = setup.Rules;

        _rules.Add(layout, "rounds", MatchRules.RoundChoices, rules.RoundsToWin, MatchRules.DescribeRounds, rounds => rules.RoundsToWin = rounds);
        _rules.Add(layout, "time", MatchRules.TimeChoices, rules.RoundSeconds, MatchRules.DescribeTime, seconds => rules.RoundSeconds = seconds);
        _rules.Add(layout, "weather", MatchRules.WeatherChoices, rules.Weather, MatchRules.DescribeWeather, weather => rules.Weather = weather);

        layout.Get<Label>("map_name").Text = $"on {mapDefinition.PrettyName}";
        layout.Get<Button>("btn_fight").OnPressed = StartFight;
        _hint = layout.Get<Label>("hint");

        _rules.Select(0);
    }

    protected override void UpdateMenu(float dt)
    {
        // The other player left while we were making up our mind, back to the lobby to wait for the next one
        if (setup.Lobby is { PeerPresent: false } lobby)
        {
            lobby.SetChoosingMap(false);
            GoTo(new GamepadSelectorScene(setup));
            return;
        }

        // Player one drives the menus, and the hint shows the buttons the way they are printed on their gamepad
        GameInput.Manager.TryGet(setup.MenuSlot, out Gamepad? gamepad);
        _hint.Text = ButtonGlyphs.Localize(HINT, gamepad);

        if (gamepad is null || !InputReady) return;

        int vertical = MenuInput.Vertical(gamepad), horizontal = MenuInput.Horizontal(gamepad);

        if (MenuInput.ConfirmPressed(gamepad)) StartFight();
        else if (gamepad.WasPressed(GamepadInput.B)) GoTo(new MapSelectionScene(setup));
        else if (vertical != 0) _rules.Move(vertical);
        else if (horizontal != 0) _rules.Step(horizontal);
    }

    private void StartFight()
    {
        // The other machine has the same maps and goes looking for this one by its file, and plays by the rules it is sent with it
        setup.Lobby?.StartFight(mapDefinition.FileName, setup.Rules);

        GoTo(setup.CreateFight(mapDefinition), Screen.IntoFight);
    }
}

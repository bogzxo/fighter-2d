using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Fighter2D.Map;
using Fighter2D.Match;

using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the rules of the match are picked, between the map and the fight. How many rounds, how long they last and what the weather does.
/// Every rule is a row of the screen, see <see cref="AddRule"/> for what it takes to add another one.
/// In an online fight this is the host's job like the map before it, the other player gets the rules along with the map when the fight starts.
/// </summary>
internal class MatchRulesScene(MatchSetup setup, MapDefinition mapDefinition) : MenuScene
{
    private const string HINT = "[icon:dpad] choose and change    [icon:pad_a] fight    [icon:pad_b] back";

    /// <summary>One rule on the screen. What it is called, what it is set to, and what writes a choice back into the rules.</summary>
    private sealed record Rule(Label Title, Selector Selector, Action<int> Apply);

    protected override string LayoutFile => MenuLayouts.MATCH_RULES;
    protected override float InputDelay => 0.3f;

    private readonly List<Rule> _rules = [];
    private Label _hint = null!;
    private int _selected;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/match_rules.hor, what its rows choose between is decided here
        MatchRules rules = setup.Rules;

        AddRule(layout, "rounds", MatchRules.RoundChoices, rules.RoundsToWin, MatchRules.DescribeRounds, rounds => rules.RoundsToWin = rounds);
        AddRule(layout, "time", MatchRules.TimeChoices, rules.RoundSeconds, MatchRules.DescribeTime, seconds => rules.RoundSeconds = seconds);
        AddRule(layout, "weather", MatchRules.WeatherChoices, rules.Weather, MatchRules.DescribeWeather, weather => rules.Weather = weather);

        layout.Get<Label>("map_name").Text = $"on {mapDefinition.PrettyName}";
        layout.Get<Button>("btn_fight").OnPressed = StartFight;
        _hint = layout.Get<Label>("hint");

        Select(0);
    }

    /// <summary>
    /// Helper method to put a rule on the screen. The layout has to have a selector called <paramref name="name"/> and a label called that with _title after it.
    /// </summary>
    /// <param name="choices">What the rule can be set to, in the order it is stepped through.</param>
    /// <param name="current">What it is set to right now, the first choice is shown if this isn't one of them.</param>
    /// <param name="describe">How a choice is written on the screen.</param>
    /// <param name="apply">Writes a choice into the rules, called whenever another one is picked.</param>
    private void AddRule<T>(UILayout layout, string name, T[] choices, T current, Func<T, string> describe, Action<T> apply)
    {
        var selector = layout.Get<Selector>(name);
        selector.Options = [.. choices.Select(describe)];
        selector.Index = Math.Max(0, Array.IndexOf(choices, current));

        var rule = new Rule(layout.Get<Label>($"{name}_title"), selector, index => apply(choices[index]));

        // Clicking the selector steps through the choices by itself, all that is left to do is write the new one down
        selector.OnChanged = _ => rule.Apply(selector.Index);

        _rules.Add(rule);
    }

    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _rules.Count - 1);

        // The rule the gamepad is on is written in white, the others are dimmed
        for (int i = 0; i < _rules.Count; i++)
        {
            _rules[i].Title.Color = i == _selected ? Vector4.One : MenuColors.Hint;
        }
    }

    /// <summary>
    /// Helper method to set the rule the gamepad is on to the choice before or after the one it has, wrapping around at the ends.
    /// </summary>
    private void Step(int direction)
    {
        Rule rule = _rules[_selected];

        int count = rule.Selector.Options.Length;
        if (count < 2) return;

        rule.Selector.Index = (rule.Selector.Index + direction + count) % count;
        rule.Apply(rule.Selector.Index);

        rule.Selector.Punch(0.08f, 0.2f);
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
        else if (vertical != 0) Select(_selected + vertical);
        else if (horizontal != 0) Step(horizontal);
    }

    private void StartFight()
    {
        // The other machine has the same maps and goes looking for this one by its file, and plays by the rules it is sent with it
        setup.Lobby?.StartFight(mapDefinition.FileName, setup.Rules);

        GoTo(setup.CreateFight(mapDefinition));
    }
}

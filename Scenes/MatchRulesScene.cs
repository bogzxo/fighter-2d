using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Fighter2D.Match;

using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the rules of the match are settled on, between picking the map and the fight: how many rounds, how long they last and what the weather does.
/// Every rule is a row of the screen, see <see cref="AddRule"/> for what it takes to add another one.
/// In an online fight this is the hosts to do like the map before it, the other player gets the rules along with the map when the fight starts.
/// </summary>
internal class MatchRulesScene(MatchSetup setup, MapLoader.MapDefinition mapDefinition) : Scene
{
    private const float INPUT_DELAY = 0.3f;
    private const string HINT = "[icon:dpad] choose and change    [icon:pad_a] fight    [icon:pad_b] back";

    // The rule the gamepad is on is written in white, the others are dimmed
    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    /// <summary>One rule on the screen: what it is called, what it is set to, and what writes a choice back into the rules.</summary>
    private sealed record Rule(Label Title, Selector Selector, Action<int> Apply);

    public override Camera ActiveCamera { get; protected set; } = null!;

    // The glass everything is seen through
    private Renderer2D screen = null!;
    private Camera2D camera = null!;

    private readonly List<Rule> rules = [];
    private Label hint = null!;
    private int selectedIndex;
    private float _delayTimer;

    // Player one drives the menus
    private int gamepadIndex => setup.Slots.Count > 0 ? setup.Slots[0] : 0;
    private Gamepad? _gamepad;

    public override void Initialize()
    {
        ActiveCamera = camera = AddEntity<Camera2D>(new(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        Select(0);

        base.Initialize();
    }

    private void CompositeImages()
    {
        var spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new Sprite(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeUi()
    {
        // The screen is laid out in Assets/ui/layouts/match_rules.hor, what its rows choose between is decided here
        UILayout layout = MenuLayouts.Load(this, camera, MenuLayouts.MATCH_RULES);

        MatchRules matchRules = setup.Rules;

        AddRule(layout, "rounds", MatchRules.RoundChoices, matchRules.RoundsToWin, MatchRules.DescribeRounds, rounds => matchRules.RoundsToWin = rounds);
        AddRule(layout, "time", MatchRules.TimeChoices, matchRules.RoundSeconds, MatchRules.DescribeTime, seconds => matchRules.RoundSeconds = seconds);
        AddRule(layout, "weather", MatchRules.WeatherChoices, matchRules.Weather, MatchRules.DescribeWeather, weather => matchRules.Weather = weather);

        layout.Get<Label>("map_name").Text = $"on {mapDefinition.PrettyName}";
        layout.Get<Button>("btn_fight").OnPressed = StartFight;

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = layout.Get<Label>("hint");
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

        rules.Add(rule);
    }

    private void Select(int index)
    {
        selectedIndex = Math.Clamp(index, 0, rules.Count - 1);

        for (int i = 0; i < rules.Count; i++)
        {
            rules[i].Title.Color = i == selectedIndex ? Vector4.One : HintColor;
        }
    }

    /// <summary>
    /// Helper method to set the rule the gamepad is on to the choice before or after the one it has, going round at the ends.
    /// </summary>
    private void Step(int direction)
    {
        Rule rule = rules[selectedIndex];

        int count = rule.Selector.Options.Length;
        if (count < 2) return;

        rule.Selector.Index = (rule.Selector.Index + direction + count) % count;
        rule.Apply(rule.Selector.Index);

        rule.Selector.Punch(0.08f, 0.2f);
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;

        // The other player left while we were making up our mind, back to the lobby to wait for the next one
        if (setup.Lobby is { PeerPresent: false } lobby)
        {
            lobby.SetChoosingMap(false);
            Engine.SetScene(new GamepadSelectorScene(setup));
            return;
        }

        base.UpdateState(dt);

        // Attach the gamepad that was picked a few screens back
        if (_gamepad is null)
        {
            GameInput.Manager.TryGet(gamepadIndex, out _gamepad);
        }

        // The hint shows the buttons the way they are printed on that gamepad
        hint.Text = GameInput.Localize(HINT, _gamepad);

        // The button that got us here is most likely still held
        if (_gamepad is null || _delayTimer < INPUT_DELAY) return;

        if (_gamepad.WasPressed(GamepadInput.A) || _gamepad.WasPressed(GamepadInput.Start))
        {
            StartFight();
        }
        else if (_gamepad.WasPressed(GamepadInput.B))
        {
            Back();
        }
        else if (GameInput.MenuDownPressed(_gamepad))
        {
            Select(selectedIndex + 1);
        }
        else if (GameInput.MenuUpPressed(_gamepad))
        {
            Select(selectedIndex - 1);
        }
        else if (GameInput.MenuRightPressed(_gamepad))
        {
            Step(1);
        }
        else if (GameInput.MenuLeftPressed(_gamepad))
        {
            Step(-1);
        }
    }

    /// <summary>
    /// Helper method to go back to picking the map, the rules stay as they were left for the next time round.
    /// </summary>
    private void Back()
    {
        Engine.SetScene(new MapSelectionScene(setup));
    }

    private void StartFight()
    {
        // The other machine has the same maps and goes looking for this one by its file, and plays by the rules it is sent with it
        setup.Lobby?.StartFight(mapDefinition.FileName, setup.Rules);

        Engine.SetScene(setup.CreateFight(mapDefinition));
    }
}

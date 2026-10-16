using System;
using System.Collections.Generic;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HUD;

/// <summary>
/// Everything that is laid over a fight. The health bars, the clock and the rounds, the versus screen, the input display and the hit callouts.
/// Each of those is an <see cref="IHudDisplay"/>, this only loads the layout (Assets/ui/layouts/fight_overlay.hor) and keeps them all updated.
/// The input display and the callouts are only there if the player wants them, see <see cref="GameOptions"/>.
/// </summary>
internal class HUDManager : GameComponent
{
    // The calls across the screen are on a layer of their own, so they can get out of the way of the pause countdown
    private const string BANNER_LAYER = "banner";

    private readonly List<IHudDisplay> _displays = [];
    private UICompositor _compositor = null!;
    private UILayout _layout = null!;

    /// <summary>
    /// Asked every update whether the calls across the screen (ROUND 1, FIGHT!) are to stay off it. The pause menu
    /// counts down in the same spot, nobody wants the two on top of each other.
    /// </summary>
    public Func<bool>? BannerHidden { get; init; }

    /// <summary>
    /// The fight the HUD is over, for its rounds and its combat log.
    /// </summary>
    public FightContext Fight { get; init; } = null!;

    /// <summary>
    /// The rounds of that fight, which every display goes by.
    /// </summary>
    public RoundDirector Round { get; init; } = null!;

    /// <summary>
    /// What the players did over the match, for the result card.
    /// </summary>
    public MatchStats Stats { get; init; } = null!;

    /// <summary>
    /// Back to the fight setup screen with the same setup, for the result card. Null where there is no such thing (online).
    /// </summary>
    public Action? ChangeFighters { get; init; }

    public override void Initialize()
    {
        (UILayout layout, _compositor) = MenuLayouts.Load(MenuLayouts.FIGHT_HUD);
        _compositor.Initialize();
        _layout = layout;

        // The fight sets its director up before its HUD, there is nothing to show without one
        RoundDirector round = Round ?? throw new InvalidOperationException("The HUD of a fight needs its RoundDirector, which FightScene adds first.");

        _displays.Add(new HealthDisplay(layout, round));
        _displays.Add(new RoundDisplay(layout, round));
        _displays.Add(new VersusDisplay(layout, round));
        _displays.Add(new ResultCard(layout, round, Stats, ChangeFighters));

        // What these would have written into stays empty without them, there is nothing to hide
        if (GameOptions.InputDisplay) _displays.Add(new InputDisplay(layout, round));
        if (GameOptions.HitCallouts) _displays.Add(new HitCalloutDisplay(layout, round, Fight.CombatLog));

        layout.Get<UIComponent>(FrameBarDisplay.ROOT).Visible = false;
        FrameData = GameOptions.FrameData;
    }

    /// <summary>
    /// Whether the frame bars are up, what the options say to begin with and switched while the fight goes on by the lab
    /// drawer. Not saved, the options are for that. The bars are boxes with a track in them and not just text, so they
    /// are taken off the screen while nobody wants them, and start out empty when they come back.
    /// </summary>
    public bool FrameData
    {
        get => _frameBars is not null;
        set
        {
            if (value == FrameData) return;

            if (value) _displays.Add(_frameBars = new FrameBarDisplay(_layout, Round));
            else
            {
                _displays.Remove(_frameBars!);
                _frameBars = null;
            }

            _layout.Get<UIComponent>(FrameBarDisplay.ROOT).Visible = value;
        }
    }

    private FrameBarDisplay? _frameBars;

    public override void UpdateState(float dt)
    {
        foreach (IHudDisplay display in _displays)
        {
            display.Update(dt);
        }

        _layout.Module.SetLayerVisible(BANNER_LAYER, BannerHidden?.Invoke() != true);
        _compositor.UpdateState(dt);
    }

    public override void UpdatePhysics(float dt) => _compositor.UpdatePhysics(dt);

    public override void Capture() => _compositor.Capture();

    public override void Render(float dt) => _compositor.Render(dt);
}

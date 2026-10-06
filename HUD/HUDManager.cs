using System;
using System.Collections.Generic;

using Fighter2D.Match;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.Rendering.UIX;

namespace Fighter2D.HUD;

/// <summary>
/// Everything that is laid over a fight. The health bars, the clock and the rounds, the versus screen, the input display and the hit callouts.
/// Each of those is an <see cref="IHudDisplay"/>, this only loads the layout (Assets/ui/layouts/fight_overlay.hor) and keeps them all updated.
/// </summary>
internal class HUDManager : IGameComponent
{
    private readonly List<IHudDisplay> _displays = [];
    private UICompositor _compositor = null!;

    public bool Enabled { get; set; }
    public string Name { get; set; } = "HUD Manager";
    public Entity Parent { get; set; } = null!;

    public void Initialize()
    {
        var camera = new Camera2D(GameEngine.Instance.WindowManager.ViewportSize);
        camera.Render(0);

        (UILayout layout, _compositor) = MenuLayouts.Load(camera, MenuLayouts.FIGHT_HUD);
        _compositor.Initialize();

        // The fight sets its director up before its HUD, there is nothing to show without one
        RoundDirector round = Fight.Round ?? throw new InvalidOperationException("The HUD of a fight needs its RoundDirector, which FightScene adds first.");

        _displays.Add(new HealthDisplay(layout, round));
        _displays.Add(new RoundDisplay(layout, round));
        _displays.Add(new VersusDisplay(layout, round));
        _displays.Add(new InputDisplay(layout, round));
        _displays.Add(new HitCalloutDisplay(layout, round, Fight.CombatLog));
    }

    public void UpdateState(float dt)
    {
        foreach (IHudDisplay display in _displays)
        {
            display.Update(dt);
        }

        _compositor.UpdateState(dt);
    }

    public void UpdatePhysics(float dt) => _compositor.UpdatePhysics(dt);

    public void Render(float dt, object? obj = null) => _compositor.Render(dt);
}

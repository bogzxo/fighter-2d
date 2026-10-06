using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Timers;

using Fighter2D.Match;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD
{
    /// <summary>
    /// What is laid over a fight.
    /// The health of the players, the clock and the rounds, and the fighters shown off before the first one.
    /// </summary>
    internal class HUDManager : IGameComponent
    {
        private UICompositor _compositor;
        private readonly List<IHudDisplay> _displays = [];

        public void Initialize()
        {
            var camera = new Camera2D(GameEngine.Instance.WindowManager.ViewportSize);
            camera.Render(0);

            (var layout, _compositor) = MenuLayouts.Load((Camera2D)camera, MenuLayouts.FIGHTSCENE_HUD);
            _compositor.Initialize();

            // The fight sets its director up before its HUD, there is nothing to show without one
            RoundDirector round = FightScene.Round ?? throw new InvalidOperationException("The HUD of a fight needs its RoundDirector, which FightScene adds first.");

            _displays.Add(new HealthDisplay(layout, round));
            _displays.Add(new RoundDisplay(layout, round));
            _displays.Add(new VersusDisplay(layout, round));
        }

        public void Render(float dt, object? obj = null)
        {
            _compositor.Render(dt);
        }

        public void UpdateState(float dt)
        {
            foreach (IHudDisplay display in _displays)
            {
                display.Update(dt);
            }

            _compositor.UpdateState(dt);
        }

        public void UpdatePhysics(float dt)
        {
            _compositor.UpdatePhysics(dt);
        }


        public bool Enabled { get; set; }
        public string Name { get; set; } = "HUD Manager";
        public Entity Parent { get; set; }
    }
}

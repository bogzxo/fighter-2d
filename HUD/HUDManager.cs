using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Fighter2D.Scenes;
using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD
{
    internal class HUDManager : IGameComponent
    {
        private UICompositor _compositor;
        private UIModule _versusModule;

        private ProgressBar _pbP1Health, _pbP2Health;

        public void Initialize()
        {
            var camera = new Camera2D(new Vector2(1280, 720));
            camera.Render(0);
            
            _compositor = new UICompositor(camera, Constants.UI_THEME);
            _compositor.Initialize();

            // Creating a progress bar used to switch depth testing on for everything as a side effect.
            // The UI now leaves GL state as it found it, so the fight scene asks for it itself.
            GameEngine.Instance.GL.Enable(Silk.NET.OpenGL.EnableCap.DepthTest);

            CreateHealthBars();
        }

        private void CreateHealthBars()
        {
            _versusModule = _compositor.CreateModule();

            /* The bar of the skin is only 32x8, so it is stretched to a size of our own
             */

            _pbP1Health = _versusModule.AddProgressBar(new Vector2(-1280 / 3 + 128, 720/2 - 64));
            _pbP2Health = _versusModule.AddProgressBar(new Vector2(1280 / 3 - 128, 720/2 - 64));

            _pbP1Health.Size = _pbP2Health.Size = new Vector2(360, 36);
            _pbP1Health.TextScale = _pbP2Health.TextScale = 0.3f;

            _pbP1Health.Progress = _pbP2Health.Progress = 1.0f;
        }

        public void Render(float dt, object? obj = null)
        {
            ShowHealth(_pbP1Health, FightScene.ControlledPlayer.Health / 100.0f);
            ShowHealth(_pbP2Health, FightScene.OtherPlayer.Health / 100.0f);
            
            _compositor.Render(dt);
        }

        /// <summary>
        /// Helper method to put the health of a player on their bar, which rattles when it goes down.
        /// </summary>
        private static void ShowHealth(ProgressBar bar, float health)
        {
            if (health < bar.Progress - 0.001f)
            {
                bar.Shake(9.0f, 0.3f);
            }

            bar.Progress = health;
        }

        public void UpdateState(float dt)
        {
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

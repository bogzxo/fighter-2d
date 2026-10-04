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
            
            _compositor = new UICompositor(camera);
            _compositor.Initialize();

            // Creating a progress bar used to switch depth testing on for everything as a side effect.
            // The UI now leaves GL state as it found it, so the fight scene asks for it itself.
            GameEngine.Instance.GL.Enable(Silk.NET.OpenGL.EnableCap.DepthTest);

            CreateHealthBars();
        }

        private void CreateHealthBars()
        {
            _versusModule = _compositor.CreateModule();

            /* The sprite is 128x2 in the x axis
             */

            _pbP1Health = _versusModule.AddProgressBar(new Vector2(-1280 / 3 + 128, 720/2 - 64));
            _pbP2Health = _versusModule.AddProgressBar(new Vector2(1280 / 3 - 128, 720/2 - 64));

            _pbP1Health.Progress = _pbP2Health.Progress = 1.0f;
        }

        public void Render(float dt, object? obj = null)
        {
            _pbP1Health.Progress = FightScene.ControlledPlayer.Health / 100.0f;
            _pbP2Health.Progress = FightScene.OtherPlayer.Health / 100.0f;
            
            _compositor.Render(dt);
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

global using static Horizon.Rendering.Tiling<Fighter2D.Map.MapTileTexID>;

using System.Numerics;

using Box2D.NetStandard.Common;
using Box2D.NetStandard.Dynamics.World;
using Box2D.NetStandard.Dynamics.World.Callbacks;

using Horizon.Physics;
using Fighter2D.Player;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Engine;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.OpenGL.Descriptions;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UI;

namespace Fighter2D;

internal class Program
{
    static void Main(string[] args)
    {
        var eng = new GameEngine(new GameEngineConfiguration
        {
            InitialScene = typeof(MainMenuScene),
            WindowConfiguration = WindowManagerConfiguration.Default1600x900 with
            {
                WindowTitle = Constants.WINDOW_TITLE,
            }
        });

        eng.SceneManager.ChangeInstance<MainMenuScene>();

        eng.Run();
    }
}

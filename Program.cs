global using static Horizon.Rendering.Tiling<Fighter2D.Map.MapTileTexID>;

using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Engine;

namespace Fighter2D;

internal class Program
{
    private static void Main(string[] args)
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
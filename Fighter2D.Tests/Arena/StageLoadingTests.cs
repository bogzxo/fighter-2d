using Fighter2D.Arena;
using Fighter2D.Data;
using Fighter2D.Tests.Support;

namespace Fighter2D.Tests.Arena;

/// <summary>
/// A stage loads on a worker with everything its first frame would have worked out done already, and the second load
/// of the same map is handed what the first one worked out (the fight after the map preview).
/// </summary>
public class StageLoadingTests
{
    [Fact]
    public async Task AStageLoadsOnAWorker()
    {
        TestFight.UseGameContent();
        Assert.True(MapLoader.TryFind("japan", out MapDefinition japan));

        FightingStage preview = await Task.Run(() => FightingStage.Load(japan, walked: false));
        FightingStage fight = await Task.Run(() => FightingStage.Load(japan, walked: true));

        // Only the one somebody stands on has the paths across it
        Assert.Null(preview.Paths);
        Assert.NotNull(fight.Paths);

        // What blocks light by the shape of the art was worked out the once, the second load has the first one's
        int cells = preview.Map.ShadowCellsPerTile;
        Assert.NotEmpty(preview.Map.CasterTexels(cells));
        Assert.Same(preview.Map.CasterTexels(cells), fight.Map.CasterTexels(cells));

        Assert.True(fight.RightSpawn.X > fight.LeftSpawn.X);

        preview.Dispose();
        fight.Dispose();
    }
}

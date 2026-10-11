using System.Diagnostics;
using System.Numerics;

using Fighter2D.Content;
using Fighter2D.Map;

using Horizon.Rendering;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HitboxEditor;

// Fight!, which saves and starts the game straight into a fight on the content being edited, the character that's open
// against a dummy that stands there and takes it. The build of the game that comes with the editor is the one started
// (it's copied next to it, the editor references the game), so it's as new as the editor is
internal sealed partial class EditorScene
{
    private const string GAME = "Fighter2D";

    private Dropdown mapPicker = null!, opponentPicker = null!;
    private string[] mapFiles = [];
    private Process? fight;

    private void BuildFightControls(StackPanel toolbar)
    {
        toolbar.Add(new Button("Fight!") { Size = new Vector2(80, 30), LabelScale = TEXT, OnPressed = Fight,
            Tooltip = "saves, then starts the game on this character against a dummy that stands still, on the map and opponent beside" });

        List<MapDefinition> maps;
        try
        {
            maps = MapLoader.LoadAll();
        }
        catch
        {
            maps = [];
        }

        mapFiles = [.. maps.Select(m => m.FileName)];
        mapPicker = toolbar.Add(new Dropdown([.. maps.Select(m => m.PrettyName)]) { Size = new Vector2(150, 30), TextScale = SMALL, Tooltip = "the map to fight on" });
        int japan = Array.FindIndex(mapFiles, f => f.StartsWith("japan", StringComparison.OrdinalIgnoreCase));
        mapPicker.Index = Math.Max(0, japan);

        opponentPicker = toolbar.Add(new Dropdown([.. characters.Select(c => c.PrettyName)]) { Size = new Vector2(160, 30), TextScale = SMALL, Tooltip = "who stands there and takes it" });
        int man = characters.FindIndex(c => c.Id == "the_man");
        opponentPicker.Index = Math.Max(0, man);
    }

    /// <summary>What the game is started with for a fight on the character that's open.</summary>
    private List<string> FightArguments() =>
    [
        "--content", GameContent.Home,
        "--map", mapFiles.Length > 0 ? mapFiles[Math.Clamp(mapPicker.Index, 0, mapFiles.Length - 1)] : "japan",
        "--character", character.Id,
        "--opponent", characters[Math.Clamp(opponentPicker.Index, 0, characters.Count - 1)].Id,
        "--dummy", "stand",
        "--windowed"
    ];

    private void Fight()
    {
        if (fight is { HasExited: false })
        {
            Say("a fight is going already, close it first (two of them would fight over the network port)");
            return;
        }

        if (Unsaved)
        {
            Save();
            if (Unsaved) return;
        }

        string exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? GAME + ".exe" : GAME);
        if (!File.Exists(exe))
        {
            Say($"the game isn't next to the editor ({exe}), build the editor again and it will be");
            return;
        }

        var start = new ProcessStartInfo(exe) { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false };
        foreach (string argument in FightArguments()) start.ArgumentList.Add(argument);

        try
        {
            fight = Process.Start(start);
            Say($"fighting as {character.PrettyName} on {mapPicker.Value} against a {opponentPicker.Value} that stands there, with what's saved");
        }
        catch (Exception e)
        {
            Say($"the game wouldn't start, {e.Message}");
        }
    }
}

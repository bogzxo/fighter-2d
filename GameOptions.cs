using Bogz.Logging;
using System;
using System.IO;
using System.Numerics;
using System.Text;

using Fighter2D.Character.Controllers;
using Fighter2D.Content;

using Horizon.Core;

namespace Fighter2D;

/// <summary>
/// How one scene hands over to the next, for whoever has an opinion on that.
/// </summary>
internal enum TransitionStyle
{
    // The menus melt into each other and a fight rots in and out, which is what the game was made with
    Mixed,

    // One of them for everything
    Blur,
    Fade,
    Rot,

    // Hard cuts, for the impatient
    Off
}

/// <summary>
/// The settings a player can flip on the options screen. They are kept in options.hor next to the game between runs.
/// To add one give it a field here, a line in Load and Save, and a row on the options screen (OptionsScene and options.hor).
/// </summary>
internal static class GameOptions
{
    public const string FILE = "options.hor";

    // The fight runs on ticks and every tick ought to make it to the screen, so no limit on the frames goes under the tick rate
    public const int MIN_FRAME_LIMIT = (int)PlayerConfig.TICK_RATE;
    public const int NO_FRAME_LIMIT = 0;

    // The limits there are to pick from
    public static readonly int[] FrameLimits = [MIN_FRAME_LIMIT, 75, 90, 120, 144, 165, 240, NO_FRAME_LIMIT];

    // The sizes the window comes in. All of them are 16 by 9, which is the shape the menus were laid out for
    public static readonly Vector2[] Resolutions =
    [
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160)
    ];

    /* The window */

    // Whether the game takes up the whole screen, and how big its window is when it doesn't
    public static bool Fullscreen = false;
    public static Vector2 Resolution = new(1600, 900);

    // Whether frames wait for the screen, and the most of them that are drawn a second (NO_FRAME_LIMIT for as many as there is time for)
    public static bool VSync = true;
    public static int FrameLimit = NO_FRAME_LIMIT;

    /* The look */

    public static TransitionStyle Transitions = TransitionStyle.Mixed;

    // Whether the game is drawn like it is on a crusty old CRT
    public static bool Crt = true;

    // Whether moving things get smeared along the way they move, which hides that pixel art moves in steps
    public static bool MotionBlur = true;

    /* The fight */

    // Whether the fight shows what both players are pressing down the sides of the screen
    public static bool InputDisplay = true;

    // Whether the fight calls out what every attack came to (HIT, COUNTER HIT, WHIFF...)
    public static bool HitCallouts = true;

    // Whether the callouts come with the frame data of the move, for the lab rats
    public static bool FrameData = false;

    // Whether the hitboxes, hurtboxes and collision of a fight are drawn over it
    public static bool Hitboxes = false;

    /// <summary>
    /// The options of the window the way the engine wants them, see <see cref="WindowManager.Apply"/>.
    /// </summary>
    public static DisplaySettings Display => new()
    {
        Fullscreen = Fullscreen,
        WindowSize = Resolution,
        VSync = VSync,
        FramesPerSecond = FrameLimit
    };

    /// <summary>
    /// Helper method to read the options from the file. No file (or a fucked one) just means everything stays at its default.
    /// </summary>
    public static void Load()
    {
        if (!File.Exists(FILE)) return;

        try
        {
            var options = HorReader.LoadObject(FILE, "options");

            Fullscreen = HorReader.Bool(options, "fullscreen", Fullscreen);
            Resolution = SaneResolution(HorReader.Vector(options, "resolution", Resolution));
            VSync = HorReader.Bool(options, "vsync", VSync);
            FrameLimit = SaneFrameLimit((int)HorReader.Number(options, "frame_limit", FrameLimit));

            Transitions = HorReader.Named(options, "transitions", Transitions);
            Crt = HorReader.Bool(options, "crt", Crt);
            MotionBlur = HorReader.Bool(options, "motion_blur", MotionBlur);

            InputDisplay = HorReader.Bool(options, "input_display", InputDisplay);
            HitCallouts = HorReader.Bool(options, "hit_callouts", HitCallouts);
            FrameData = HorReader.Bool(options, "frame_data", FrameData);
            Hitboxes = HorReader.Bool(options, "hitboxes", Hitboxes);
        }
        catch (Exception e)
        {
            Log.Warning($"[GameOptions] {FILE} makes no sense, going with the defaults: {e.Message}");
        }
    }

    /// <summary>
    /// Helper method to write the options back to the file.
    /// </summary>
    public static void Save()
    {
        var text = new StringBuilder();
        text.AppendLine("// Written by the game, change these on the options screen instead.");
        text.AppendLine("let options = {");
        text.AppendLine($"    fullscreen: {Write(Fullscreen)},");
        text.AppendLine($"    resolution: vec({(int)Resolution.X}, {(int)Resolution.Y}),");
        text.AppendLine($"    vsync: {Write(VSync)},");
        text.AppendLine($"    frame_limit: {FrameLimit},");
        text.AppendLine($"    transitions: \"{Transitions.ToString().ToLowerInvariant()}\",");
        text.AppendLine($"    crt: {Write(Crt)},");
        text.AppendLine($"    motion_blur: {Write(MotionBlur)},");
        text.AppendLine($"    input_display: {Write(InputDisplay)},");
        text.AppendLine($"    hit_callouts: {Write(HitCallouts)},");
        text.AppendLine($"    frame_data: {Write(FrameData)},");
        text.AppendLine($"    hitboxes: {Write(Hitboxes)}");
        text.AppendLine("}");

        try
        {
            File.WriteAllText(FILE, text.ToString());
        }
        catch (Exception e)
        {
            // Not being able to save is no reason to stop the game
            Log.Warning($"[GameOptions] Could not save {FILE}: {e.Message}");
        }
    }

    /// <summary>
    /// Helper method to keep a limit somebody typed into the file from going under the tick rate.
    /// </summary>
    public static int SaneFrameLimit(int limit) => limit <= NO_FRAME_LIMIT ? NO_FRAME_LIMIT : Math.Max(MIN_FRAME_LIMIT, limit);

    // A window of nothing by nothing is no use to anybody
    private static Vector2 SaneResolution(Vector2 size) => size.X >= 320 && size.Y >= 180 ? size : Resolution;

    private static string Write(bool value) => value ? "true" : "false";
}

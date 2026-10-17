using Horizon.Content;
using Horizon.Logging;
using System;
using System.IO;
using System.Numerics;
using System.Text;

using Horizon.Core;

namespace Fighter2D.App;

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
/// How finely the pathtraced lighting works its light out, which is how many cascades it has (see
/// <see cref="Screen.TracedCascades"/>). Low is about half the cost of High.
/// </summary>
internal enum LightQuality
{
    Low,
    Medium,
    High
}

/// <summary>
/// The settings a player can flip on the options screen. They are kept in options.hor next to the game between runs.
/// To add one give it a field here, a line in Load and Save, and a row on the options screen (OptionsScene and options.hor).
/// </summary>
/// <summary>
/// The tabs of the options screen, which is what going back to the defaults goes by.
/// </summary>
// When the card of what the buttons do is in the corner of a fight (HUD/ControlsDisplay.cs)
internal enum ControlsShown
{
    Off,

    // The lab, where somebody is finding out what their character does
    Practice,

    // Every fight on this machine
    Always
}

internal enum OptionsTab
{
    Display,
    Graphics,
    Look,
    Fight,
    Sound
}

internal static class GameOptions
{
    public const string FILE = "options.hor";

    // The fight runs on ticks and every tick ought to make it to the screen, so no limit on the frames goes under the tick rate
    public const int MIN_FRAME_LIMIT = (int)FightTicks.TICK_RATE;
    public const int NO_FRAME_LIMIT = 0;

    // The limits there are to pick from
    public static readonly int[] FrameLimits = [MIN_FRAME_LIMIT, 75, 90, 120, 144, 165, 240, NO_FRAME_LIMIT];

    // The sizes the window comes in. All of them are 16 by 9, which is the shape the menus were laid out for
    // How big the UI is drawn on top of fitting the window, and how far in the camera of a fight is (lower is closer)
    public static readonly float[] GuiScales = [0.75f, 0.85f, 1.0f, 1.15f, 1.3f, 1.5f];
    public static readonly float[] CameraZooms = [0.6f, 0.75f, 0.9f, 1.0f];

    public static readonly Vector2[] Resolutions =
    [
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160)
    ];

    // Which file of Assets/ui/localisation every text of the game comes out of, there is only British English so far
    public static string Language = Localisation.DEFAULT_LANGUAGE;

    /* The window */

    // Whether the game takes up the whole screen, and how big its window is when it doesn't
    public static bool Fullscreen = true;
    public static Vector2 Resolution = new(1920, 1080);

    // Whether frames wait for the screen, and the most of them that are drawn a second (NO_FRAME_LIMIT for as many as there is time for)
    public static bool VSync = true;
    public static int FrameLimit = NO_FRAME_LIMIT;

    /* The look */

    public static TransitionStyle Transitions = TransitionStyle.Fade;

    // The skin the menus and the HUD are drawn in, one of the folders under Assets/uix, and which of its themes
    // (empty for the one the skin says is its usual one). See Screen.ApplySkin
    public static string UiSkin = Constants.UI_SKIN;
    public static string UiTheme = Constants.UI_THEME;

    // Whether the game is drawn like it is on a crusty old CRT
    public static bool Crt = true;

    // Whether the lanterns and whatever else shines in a fight get a glow round them, see Screen.AddBloom
    public static bool Bloom = true;

    // Whether the lighting is path traced, light bouncing off the arena and spilling round corners, instead of the
    // cheap kind. Off until somebody with a GPU asks for it, it is a good few passes a frame
    public static bool RenderPathtraced = false;

    // And how finely, when it is. High until somebody wants the frames back
    public static LightQuality LightQuality = LightQuality.High;

    // How big every UI is on top of fitting the window, 1 for as it was laid out
    public static float GuiScale = 1.0f;

    // How much of the arena the camera of a fight sees, 0.75 being what the game was made with. Lower is closer in
    public static float CameraZoom = 0.75f;

    /* The fight */

    // Whether the fight shows what both players are pressing down the sides of the screen
    public static bool InputDisplay = true;

    // Whether the fight calls out what every attack came to (HIT, COUNTER HIT, WHIFF...)
    public static bool HitCallouts = true;

    // Whether the callouts come with the frame data of the move, for the lab rats
    public static bool FrameData = true;

    // Whether the hitboxes, hurtboxes and collision of a fight are drawn over it
    public static bool Hitboxes = false;

    // When the card of what every button does is in the corner of a fight, with how to get the lab drawer out
    public static ControlsShown Controls = ControlsShown.Practice;

    /* Sound */

    // How loud the music, the fight and the menus are, 0 to 1 in tenths (Audio/GameSounds.cs)
    public static readonly float[] Volumes = [0.0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f];
    public static float MusicVolume = 0.7f;
    public static float EffectsVolume = 0.9f;
    public static float InterfaceVolume = 0.8f;

    /* Under the hood */

    // How much of the engine's performance overlay is up (F3 goes round them too, but that isn't saved)
    public static Horizon.UI.PerformanceDetail Performance = Horizon.UI.PerformanceDetail.Off;

    // Where the last fight that was joined was hosted, which the join screen starts out on
    public static string JoinAddress = "127.0.0.1";

    // What every option on the screen is before options.hor says anything, read off the fields above as the class is
    // made (fields are made in the order they are written, these come last), so a default is only ever written once
    private sealed record Values(
        bool Fullscreen, Vector2 Resolution, bool VSync, int FrameLimit, Horizon.UI.PerformanceDetail Performance,
        bool Crt, bool Bloom, bool RenderPathtraced, LightQuality LightQuality,
        float GuiScale, string UiSkin, string UiTheme, TransitionStyle Transitions,
        float CameraZoom, bool InputDisplay, bool HitCallouts, bool FrameData, bool Hitboxes, ControlsShown Controls,
        float MusicVolume, float EffectsVolume, float InterfaceVolume);

    private static readonly Values Defaults = new(
        Fullscreen, Resolution, VSync, FrameLimit, Performance,
        Crt, Bloom, RenderPathtraced, LightQuality,
        GuiScale, UiSkin, UiTheme, Transitions,
        CameraZoom, InputDisplay, HitCallouts, FrameData, Hitboxes, Controls,
        MusicVolume, EffectsVolume, InterfaceVolume);

    /// <summary>
    /// Puts the options of one tab of the options screen back to how the game came. Nothing is applied or saved, that is
    /// the screen's to do (the window, the skin), and the language and the join address are nobody's tab.
    /// </summary>
    public static void ResetToDefaults(OptionsTab tab)
    {
        switch (tab)
        {
            case OptionsTab.Display:
                Fullscreen = Defaults.Fullscreen;
                Resolution = Defaults.Resolution;
                VSync = Defaults.VSync;
                FrameLimit = Defaults.FrameLimit;
                Performance = Defaults.Performance;
                break;

            case OptionsTab.Graphics:
                Crt = Defaults.Crt;
                Bloom = Defaults.Bloom;
                RenderPathtraced = Defaults.RenderPathtraced;
                LightQuality = Defaults.LightQuality;
                break;

            case OptionsTab.Look:
                GuiScale = Defaults.GuiScale;
                UiSkin = Defaults.UiSkin;
                UiTheme = Defaults.UiTheme;
                Transitions = Defaults.Transitions;
                break;

            case OptionsTab.Fight:
                CameraZoom = Defaults.CameraZoom;
                InputDisplay = Defaults.InputDisplay;
                HitCallouts = Defaults.HitCallouts;
                FrameData = Defaults.FrameData;
                Hitboxes = Defaults.Hitboxes;
                Controls = Defaults.Controls;
                break;

            case OptionsTab.Sound:
                MusicVolume = Defaults.MusicVolume;
                EffectsVolume = Defaults.EffectsVolume;
                InterfaceVolume = Defaults.InterfaceVolume;
                break;
        }
    }

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

            Language = HorReader.Text(options, "language", Language);
            Fullscreen = HorReader.Bool(options, "fullscreen", Fullscreen);
            Resolution = SaneResolution(HorReader.Vector(options, "resolution", Resolution));
            VSync = HorReader.Bool(options, "vsync", VSync);
            FrameLimit = SaneFrameLimit((int)HorReader.Number(options, "frame_limit", FrameLimit));

            Transitions = HorReader.Named(options, "transitions", Transitions);
            UiSkin = HorReader.Text(options, "ui_skin", UiSkin);
            UiTheme = HorReader.Text(options, "ui_theme", UiTheme);
            Crt = HorReader.Bool(options, "crt", Crt);
            Bloom = HorReader.Bool(options, "bloom", Bloom);
            RenderPathtraced = HorReader.Bool(options, "fancy", RenderPathtraced);
            LightQuality = HorReader.Named(options, "light_quality", LightQuality);
            GuiScale = Closest(GuiScales, (float)HorReader.Number(options, "gui_scale", GuiScale));
            CameraZoom = Closest(CameraZooms, (float)HorReader.Number(options, "camera_zoom", CameraZoom));

            InputDisplay = HorReader.Bool(options, "input_display", InputDisplay);
            HitCallouts = HorReader.Bool(options, "hit_callouts", HitCallouts);
            FrameData = HorReader.Bool(options, "frame_data", FrameData);
            Hitboxes = HorReader.Bool(options, "hitboxes", Hitboxes);
            Controls = HorReader.Named(options, "controls", Controls);
            MusicVolume = Closest(Volumes, (float)HorReader.Number(options, "music_volume", MusicVolume));
            EffectsVolume = Closest(Volumes, (float)HorReader.Number(options, "effects_volume", EffectsVolume));
            InterfaceVolume = Closest(Volumes, (float)HorReader.Number(options, "interface_volume", InterfaceVolume));
            Performance = HorReader.Named(options, "performance", Performance);
            JoinAddress = HorReader.Text(options, "join_address", JoinAddress);
        }
        catch (Exception e)
        {
            Log.Warning($"[GameOptions] {FILE} makes no sense, going with the defaults: {e.Message}");
        }
    }

    /// <summary>
    /// Helper method to write the options back to the file.
    /// </summary>
    // Whichever of the choices a number out of the file is nearest to, so a hand-edited file can't set something daft
    private static float Closest(float[] choices, float value)
    {
        float best = choices[0];
        foreach (float choice in choices)
        {
            if (MathF.Abs(choice - value) < MathF.Abs(best - value)) best = choice;
        }

        return best;
    }

    public static void Save()
    {
        var text = new StringBuilder();
        text.AppendLine("// Written by the game, change these on the options screen instead.");
        text.AppendLine("let options = {");
        text.AppendLine($"    language: \"{Language}\",");
        text.AppendLine($"    fullscreen: {Write(Fullscreen)},");
        text.AppendLine($"    resolution: vec({(int)Resolution.X}, {(int)Resolution.Y}),");
        text.AppendLine($"    vsync: {Write(VSync)},");
        text.AppendLine($"    frame_limit: {FrameLimit},");
        text.AppendLine($"    transitions: \"{Transitions.ToString().ToLowerInvariant()}\",");
        text.AppendLine($"    ui_skin: \"{UiSkin}\",");
        text.AppendLine($"    ui_theme: \"{UiTheme}\",");
        text.AppendLine($"    crt: {Write(Crt)},");
        text.AppendLine($"    bloom: {Write(Bloom)},");
        text.AppendLine($"    fancy: {Write(RenderPathtraced)},");
        text.AppendLine($"    light_quality: \"{LightQuality.ToString().ToLowerInvariant()}\",");
        text.AppendLine($"    gui_scale: {GuiScale.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        text.AppendLine($"    camera_zoom: {CameraZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        text.AppendLine($"    input_display: {Write(InputDisplay)},");
        text.AppendLine($"    hit_callouts: {Write(HitCallouts)},");
        text.AppendLine($"    frame_data: {Write(FrameData)},");
        text.AppendLine($"    controls: \"{Controls.ToString().ToLowerInvariant()}\",");
        text.AppendLine($"    music_volume: {MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        text.AppendLine($"    effects_volume: {EffectsVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        text.AppendLine($"    interface_volume: {InterfaceVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        text.AppendLine($"    hitboxes: {Write(Hitboxes)},");
        text.AppendLine($"    performance: \"{Performance.ToString().ToLowerInvariant()}\",");
        text.AppendLine($"    join_address: \"{JoinAddress}\"");
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

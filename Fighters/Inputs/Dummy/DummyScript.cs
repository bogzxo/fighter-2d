using System;
using System.Collections.Generic;
using System.IO;

using Bogz.Logging;

using Horizon.HIDL;
using Horizon.HIDL.Library;
using Horizon.HIDL.Runtime;

namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// A way for the dummy to behave that is written in HIDL rather than C#, out of Assets/data/dummy_behaviours.hor. Every function
/// in that file is one, named for the training menu. It is called once a tick with what the dummy sees and hands back the buttons
/// to hold, so anybody can teach the dummy a new habit with a text editor and no compiler. What a script gets to look at and say
/// is written down at the top of the file.
/// </summary>
internal sealed class DummyScript
{
    public const string FILE = "Assets/data/dummy_behaviours.hor";
    private const string OBJECT = "behaviours";

    // What a script can call the buttons, on top of the names of the move files
    private const string TOWARDS = "towards";
    private const string AWAY = "away";
    private const string BLOCK = "block";
    private const string JUMP = "jump";
    private const string CROUCH = "crouch";
    private const string NONE = "none";

    public string Name { get; }

    private readonly HIDLRuntime _runtime;
    private readonly IRuntimeValue _function;
    private readonly ObjectValue _view, _them;
    private readonly IRuntimeValue[] _arguments;

    // What the dummy sees this tick, which the view object reads from through its natives
    private PlayerController _controller = null!;
    private DummyView _current;
    private int _tick;

    // A script that falls over is only complained about once, then it stands there like the rest of the dummies
    private bool _broken;

    private DummyScript(HIDLRuntime runtime, string name, IRuntimeValue function)
    {
        _runtime = runtime;
        Name = name;
        _function = function;

        // The view is built once and reads whatever the tick is about, no object is made per tick
        _them = Natives.Object(
            ("health", Natives.Property(() => new NumberValue(_current.Them.Player.Health))),
            ("meter", Natives.Property(() => new NumberValue(_current.Them.Player.Meter))),
            ("attacking", Natives.Property(() => Values.Bool(_current.Them.IsInStartup))),
            ("recovering", Natives.Property(() => Values.Bool(_current.Them.IsRecovering))),
            ("in_hitstun", Natives.Property(() => Values.Bool(_current.Them.State.IsInHitstun))),
            ("blocking", Natives.Property(() => Values.Bool(_current.Them.IsBlocking))),
            ("grounded", Natives.Property(() => Values.Bool(_current.Them.State.IsGrounded))),
            ("invulnerable", Natives.Property(() => Values.Bool(_current.Them.State.CurrentStatus == FighterStatus.Invulnerable))),
            ("move", Natives.Property(() => new StringValue(_current.Them.CurrentMove.Id))));

        _view = Natives.Object(
            ("tick", Natives.Property(() => new NumberValue(_tick))),
            ("distance", Natives.Property(() => new NumberValue(_current.Distance))),
            ("dx", Natives.Property(() => new NumberValue(_current.ToOpponent.X))),
            ("dy", Natives.Property(() => new NumberValue(_current.ToOpponent.Y))),
            ("in_range", Natives.Property(() => Values.Bool(_current.InRange))),
            ("can_reach", Natives.Property(() => Values.Bool(_current.CanReach))),
            ("grounded", Natives.Property(() => Values.Bool(_current.Grounded))),
            ("facing_away", Natives.Property(() => Values.Bool(_current.FacingAway))),
            ("in_control", Natives.Property(() => Values.Bool(_controller.IsInControl))),
            ("can_start_move", Natives.Property(() => Values.Bool(_controller.CanStartMove))),
            ("health", Natives.Property(() => new NumberValue(_controller.Player.Health))),
            ("meter", Natives.Property(() => new NumberValue(_controller.Player.Meter))),
            ("move", Natives.Property(() => new StringValue(_controller.CurrentMove.Id))),
            ("them", _them));

        _arguments = [_view];
    }

    /// <summary>
    /// Reads every behaviour out of the file, in the order they are written. Empty if there is no file, with a word in the log if it is broken.
    /// </summary>
    public static List<DummyScript> LoadAll()
    {
        var scripts = new List<DummyScript>();
        string path = GameContent.PathOf(FILE);
        if (!File.Exists(path)) return scripts;

        try
        {
            var runtime = new HIDLRuntime { Output = line => Log.Info($"[Dummy script] {line}") };
            runtime.RunFile(path);

            if (runtime.UserScope.Lookup(OBJECT) is not ObjectValue behaviours)
                throw new Exception($"it has to declare an object called '{OBJECT}'.");

            foreach (var (name, value) in behaviours.Properties)
            {
                if (!Values.IsFunction(value)) throw new Exception($"'{name}' has to be a function.");
                scripts.Add(new DummyScript(runtime, name, value));
            }
        }
        catch (Exception e)
        {
            Log.Error($"[Dummy script] '{path}' didn't load: {e.Message}");
        }

        return scripts;
    }

    /// <summary>
    /// Called once a tick. What the script says to hold, nothing if it says nothing or has fallen over.
    /// </summary>
    public InputFlags Read(PlayerController controller, in DummyView view)
    {
        if (_broken) return InputFlags.None;

        _controller = controller;
        _current = view;
        _tick++;

        try
        {
            return ToButtons(_runtime.Invoke(_function, _arguments), view);
        }
        catch (Exception e)
        {
            _broken = true;
            Log.Error($"[Dummy script] '{Name}' fell over and is standing still from now on: {e.Message}");
            return InputFlags.None;
        }
    }

    /// <summary>
    /// Helper method to turn what a script handed back into buttons. A name, a list of names, or nothing.
    /// </summary>
    private static InputFlags ToButtons(IRuntimeValue value, in DummyView view)
    {
        switch (value)
        {
            case StringValue name:
                return Button(name.Value, view);

            case ListValue list:
                InputFlags held = InputFlags.None;
                foreach (IRuntimeValue item in list.Items) held |= ToButtons(item, view);
                return held;

            case NullValue or BooleanValue { Value: false }:
                return InputFlags.None;

            default:
                throw new Exception($"a behaviour hands back the name of a button or a list of them, not {Values.TypeName(value)}.");
        }
    }

    private static InputFlags Button(string name, in DummyView view) => name.ToLowerInvariant() switch
    {
        TOWARDS => view.Towards,
        AWAY => view.Towards == InputFlags.DPadLeft ? InputFlags.DPadRight : InputFlags.DPadLeft,
        BLOCK => InputFlags.RightBumper,
        JUMP => InputFlags.DPadUp,
        CROUCH => InputFlags.DPadDown,
        NONE or "" => InputFlags.None,
        _ => HorReader.Named<InputFlags>(name, "button")
    };
}

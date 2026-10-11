using System.Numerics;

using Horizon.HIDL.Runtime;
using Horizon.Rendering;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HitboxEditor;

internal enum FieldKind { Number, Whole, Toggle, Choice, Stances, Vector, Inputs, Reroutes, Animation }

/// <summary>
/// One key a move or a phase can have in a move file, what kind of value it takes and what it means. The meanings are
/// the ones at the top of fighting_moves.hor, cut down to fit a tooltip.
/// </summary>
/// <param name="Default">What the game goes with when the key isn't there, null when it works that out by itself.</param>
/// <param name="Unset">What to call the key not being there, for one whose default isn't a value.</param>
internal sealed record FieldSpec(string Key, FieldKind Kind, IRuntimeValue? Default, string Help, string[]? Options = null, string Unset = "");

internal static class MoveFields
{
    private static readonly string[] Conditions = ["none", "held", "hitstun", "jumping", "falling", "always"];
    private static readonly string[] Statuses = ["attacking", "blocking", "invulnerable"];
    public static readonly string[] StanceNames = ["standing", "crouching", "jumping", "falling"];

    private static NumberValue N(float value) => new(value);
    private static BooleanValue B(bool value) => new(value);
    private static StringValue S(string value) => new(value);

    public static readonly FieldSpec[] Move =
    [
        new("input", FieldKind.Inputs, null, "The buttons that start it. \"A | B\" is either, \"A + B\" both together. A, B, X, Y, DPadUp/Down/Left/Right, LeftBumper, RightBumper, LeftTrigger, RightTrigger. Without one it can only be rerouted to.", Unset: "none, rerouted to"),
        new("trigger", FieldKind.Choice, S("held"), "How the input has to be entered. Held for as long as it is, pressed once a press, double_tap twice in a row.", ["held", "pressed", "double_tap"]),
        new("priority", FieldKind.Whole, N(-1), "The order the inputs are matched in, lowest first. A move that needs more than another goes before it. Negative for moves no input starts."),
        new("stances", FieldKind.Stances, S("standing"), "The stances it can be started from."),
        new("damage", FieldKind.Whole, N(0), "How much health a hit takes, out of 100."),
        new("knockback", FieldKind.Vector, new Vector2Value(Vector2.Zero), "The shove whoever gets hit is given, away from the attacker and up. Every 100 away slides them about 6 units. Up 1100 is off their feet, 2000 is juggle height."),
        new("hitstun", FieldKind.Number, null, "How long whoever gets hit loses the controls for, in frames of this move's animation.", Unset: "worked out from the startup"),
        new("blockstun", FieldKind.Number, null, "How long whoever blocks it is stuck in the block, in frames of this move's animation.", Unset: "a share of the hitstun"),
        new("level", FieldKind.Choice, S("mid"), "Where the hit comes in. Mid is blocked either way, low only crouching, overhead only standing.", ["mid", "low", "overhead"]),
        new("knockdown", FieldKind.Toggle, B(false), "Whether whoever gets hit ends up on the floor."),
        new("meter_cost", FieldKind.Whole, N(0), "How much of the super meter it burns, out of 100. Without the meter the input goes to the next move."),
        new("cancellable", FieldKind.Toggle, B(true), "Whether it can be cancelled into another move at all. Its phases say from which frame."),
        new("steering", FieldKind.Toggle, B(true), "Whether the player can walk during it."),
        new("turning", FieldKind.Toggle, null, "Whether the player can turn round during it.", Unset: "the same as steering"),
        new("walk_scale", FieldKind.Number, N(1), "How fast they walk during it against the character's walk speed."),
        new("status", FieldKind.Choice, null, "What the player is for as long as it lasts. Blocking bounces hits from the front, invulnerable lets them through.", Statuses, "left alone"),
        new("stance", FieldKind.Choice, null, "The stance it puts the player in.", StanceNames, "left to the physics"),
        new("stance_after", FieldKind.Choice, null, "The stance it leaves the player in.", StanceNames, "left to the physics"),
        new("warns", FieldKind.Toggle, null, "Whether the opponent gets a heads up it's coming, the dummy blocks off it.", Unset: "if it does damage"),
        new("repeat", FieldKind.Choice, S("none"), "Starts over after its last phase for as long as this holds.", Conditions),
        new("stance_reroutes", FieldKind.Reroutes, null, "Another move to do instead, by the stance the player is in when the input comes. A kick in the air is a jump kick.", Unset: "none"),
        new("finish_reroutes", FieldKind.Reroutes, null, "The move to carry on with once this one is over, by the stance the player is in by then. Idle without one.", Unset: "back to idle")
    ];

    public static readonly FieldSpec[] Phase =
    [
        new("animation", FieldKind.Animation, null, "The animation it plays, a tag of the character's .ase file. Without one the one before carries on.", Unset: "the one before"),
        new("start", FieldKind.Whole, N(0), "The frame of the animation it starts on."),
        new("hold", FieldKind.Whole, N(0), "How many frames of the move every frame of the animation is held for. 2 plays art drawn at half the frame rate the way it was made.", Unset: "1, or the phase before's"),
        new("frames", FieldKind.Whole, N(0), "How many frames it lasts, 0 for what is left of its animation."),
        new("hit", FieldKind.Whole, N(-1), "The frame the hit comes out on, counted from 0. Before it is startup, after it recovery. -1 for no hit."),
        new("cancel", FieldKind.Whole, N(-1), "The frame from which the move can be cancelled into another one."),
        new("commit", FieldKind.Whole, N(-1), "The frame from which the player is committed and can't cancel any more."),
        new("status", FieldKind.Choice, null, "A status the player only takes on during this phase, from status_frame on.", Statuses, "none"),
        new("status_frame", FieldKind.Whole, N(0), "The frame the status kicks in on."),
        new("loop_while", FieldKind.Choice, S("none"), "The phase starts over for as long as this holds.", Conditions),
        new("until", FieldKind.Choice, S("none"), "The phase ends the frame this starts holding.", Conditions),
        new("impulse", FieldKind.Vector, new Vector2Value(Vector2.Zero), "A shove the player gets as the phase starts, the way they face and up."),
        new("effect", FieldKind.Choice, null, "A bit of eye candy.", ["jump", "roll", "haze"], "none"),
        new("effect_frames", FieldKind.Whole, N(1), "How many frames the effect keeps coming for, 0 for all of them.")
    ];
}

/// <summary>
/// A row of the inspector that edits one key, its name on the left (bright when the move says it, dim when it's the
/// default) and an x on the right that takes it out again. What it edits it hands over as a value of the file.
/// </summary>
internal abstract class FieldEditor
{
    protected const float TEXT = 0.12f;
    protected const float WIDGET = 196.0f;

    private static readonly Vector4 Set = new(0.92f, 0.93f, 0.96f, 1.0f);
    private static readonly Vector4 Unset = new(0.5f, 0.52f, 0.58f, 1.0f);

    public FieldSpec Spec { get; }
    public StackPanel Root { get; }
    public Action<FieldSpec, IRuntimeValue?>? Changed { get; set; }

    protected readonly StackPanel Line;
    private readonly Label name;
    private readonly Button reset;
    private readonly Label note;
    private bool showing;

    protected FieldEditor(StackPanel into, FieldSpec spec)
    {
        Spec = spec;
        Root = into.Add(new StackPanel { Anchor = Origin.Left, Spacing = 1, Tooltip = spec.Help });
        Line = Root.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 6 });
        name = Line.Add(new Label(spec.Key) { Size = new Vector2(112, 24), TextScale = TEXT, Align = Origin.Left, Tooltip = spec.Help });
        Build(Line);
        reset = Line.Add(new Button("x") { Size = new Vector2(24, 24), LabelScale = TEXT, Tooltip = "take it out, back to the default", OnPressed = () => Changed?.Invoke(Spec, null) });
        note = Root.Add(new Label("") { Anchor = Origin.Left, Size = new Vector2(330, 0), TextScale = 0.1f, Align = Origin.Left, Color = Unset, Visible = false });
    }

    protected abstract void Build(StackPanel line);

    protected abstract void Display(IRuntimeValue? value);

    /// <summary>Shows what the move says, or the default dimmed when it doesn't say.</summary>
    public void Show(IRuntimeValue? value)
    {
        showing = true;
        name.Color = value is null ? Unset : Set;
        reset.Enabled = value is not null;
        note.Visible = value is null && Spec.Unset.Length > 0;
        note.Text = $"not set, {Spec.Unset}";
        Display(value ?? Spec.Default);
        showing = false;
    }

    /// <summary>Hands a value over, unless it's only being shown.</summary>
    protected void Emit(IRuntimeValue? value)
    {
        if (!showing) Changed?.Invoke(Spec, value);
    }

    public static FieldEditor For(StackPanel into, FieldSpec spec) => spec.Kind switch
    {
        FieldKind.Number or FieldKind.Whole => new NumberField(into, spec),
        FieldKind.Toggle => new ToggleField(into, spec),
        FieldKind.Choice or FieldKind.Animation => new ChoiceField(into, spec),
        FieldKind.Stances => new StancesField(into, spec),
        FieldKind.Vector => new VectorField(into, spec),
        FieldKind.Inputs => new TextField(into, spec),
        FieldKind.Reroutes => new ReroutesField(into, spec),
        _ => throw new ArgumentOutOfRangeException(nameof(spec))
    };

    /// <summary>For the editors that pick from the moves or the animations of the character, which change with it.</summary>
    public virtual void SetOptions(string[] options) { }

    protected static string Text(IRuntimeValue? value) => value switch
    {
        StringValue text => text.Value,
        ListValue list => string.Join(" | ", list.Items.Select(i => i is StringValue s ? s.Value : i.ToString())),
        null => "",
        _ => value.ToString() ?? ""
    };
}

internal sealed class NumberField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private NumberBox box = null!;

    protected override void Build(StackPanel line)
    {
        box = line.Add(new NumberBox { Size = new Vector2(WIDGET, 24), DragStep = Spec.Kind == FieldKind.Whole ? 1.0f : 0.1f });
        box.OnValueChanged = value => Emit(new NumberValue(Spec.Kind == FieldKind.Whole ? MathF.Round(value) : value));
    }

    protected override void Display(IRuntimeValue? value)
    {
        // Not while it is being typed into, it would fight the typing
        if (!box.IsFocused) box.Value = value is NumberValue number ? number.Value : -1;
    }
}

internal sealed class ToggleField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private ToggleButton toggle = null!;

    protected override void Build(StackPanel line)
    {
        toggle = line.Add(new ToggleButton("") { Size = new Vector2(WIDGET, 24), LabelScale = TEXT });
        toggle.OnPressed = () => Emit(new BooleanValue(toggle.State));
    }

    protected override void Display(IRuntimeValue? value)
    {
        toggle.State = value is BooleanValue { Value: true };
        toggle.Label = value is null ? "(see below)" : toggle.State ? "yes" : "no";
    }
}

/// <summary>One of a few words. A key the game has no default value for gets a "-" for not saying.</summary>
internal sealed class ChoiceField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private const string NOTHING = "-";
    private Dropdown dropdown = null!;
    private string[] options = [];

    protected override void Build(StackPanel line)
    {
        dropdown = line.Add(new Dropdown { Size = new Vector2(WIDGET, 24), TextScale = TEXT, MaxRows = 12 });
        SetOptions(Spec.Options ?? []);
        dropdown.OnChanged = picked => Emit(picked == NOTHING ? null : new StringValue(picked));
    }

    public override void SetOptions(string[] given)
    {
        options = Spec.Default is null ? [NOTHING, .. given] : given;
        dropdown.Options = options;
    }

    protected override void Display(IRuntimeValue? value)
    {
        string text = Text(value);
        int at = Array.FindIndex(options, o => o.Equals(text, StringComparison.OrdinalIgnoreCase));

        // Something the list doesn't know (an animation the art hasn't got), shown anyway so it isn't lost on a click
        if (at < 0 && text.Length > 0)
        {
            options = [.. options, text];
            dropdown.Options = options;
            at = options.Length - 1;
        }

        dropdown.Index = Math.Max(0, at);
    }
}

/// <summary>Which of the four stances, written the way the files write them, "standing | crouching".</summary>
internal sealed class StancesField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private readonly ToggleButton[] toggles = new ToggleButton[4];

    protected override void Build(StackPanel line)
    {
        var grid = line.Add(new StackPanel { Size = new Vector2(WIDGET, 0), Spacing = 2 });
        StackPanel? row = null;
        for (int i = 0; i < toggles.Length; i++)
        {
            if (i % 2 == 0) row = grid.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 4 });
            toggles[i] = row!.Add(new ToggleButton(MoveFields.StanceNames[i]) { Size = new Vector2(WIDGET / 2 - 2, 22), LabelScale = 0.105f });
            toggles[i].OnPressed = Changed;
        }

        void Changed()
        {
            var picked = MoveFields.StanceNames.Where((_, i) => toggles[i].State).ToArray();
            Emit(new StringValue(picked.Length == 0 ? "standing" : string.Join(" | ", picked)));
        }
    }

    protected override void Display(IRuntimeValue? value)
    {
        string[] named = [.. Text(value).Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant())];
        for (int i = 0; i < toggles.Length; i++) toggles[i].State = named.Contains(MoveFields.StanceNames[i]);
    }
}

internal sealed class VectorField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private NumberBox x = null!, y = null!;

    protected override void Build(StackPanel line)
    {
        x = line.Add(new NumberBox { Size = new Vector2(WIDGET / 2 - 3, 24), DragStep = 10.0f, Tooltip = "the way they face" });
        y = line.Add(new NumberBox { Size = new Vector2(WIDGET / 2 - 3, 24), DragStep = 10.0f, Tooltip = "up" });
        x.OnValueChanged = y.OnValueChanged = _ => Emit(new Vector2Value(new Vector2(x.Value, y.Value)));
    }

    protected override void Display(IRuntimeValue? value)
    {
        Vector2 v = value is Vector2Value vector ? vector.Value : Vector2.Zero;
        if (x.IsFocused || y.IsFocused) return;
        x.Value = v.X;
        y.Value = v.Y;
    }
}

/// <summary>Text as it's typed, the inputs of a move. A half typed one that doesn't read is left alone until it does.</summary>
internal sealed class TextField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private TextBox box = null!;

    protected override void Build(StackPanel line)
    {
        box = line.Add(new TextBox { Size = new Vector2(WIDGET, 24), TextScale = TEXT, MaxLength = 120 });
        box.OnChanged = text => Emit(text.Trim().Length == 0 ? null : new StringValue(text.Trim()));
    }

    protected override void Display(IRuntimeValue? value)
    {
        // Not while it's being typed into, the caret would jump to the end on every letter
        if (!box.IsFocused) box.Text = Text(value);
    }
}

/// <summary>A move by stance, as many as there are stances, "-" for none.</summary>
internal sealed class ReroutesField(StackPanel into, FieldSpec spec) : FieldEditor(into, spec)
{
    private const string NOTHING = "-";
    private readonly Dropdown[] picks = new Dropdown[4];
    private string[] options = [NOTHING];

    protected override void Build(StackPanel line)
    {
        var column = line.Add(new StackPanel { Size = new Vector2(WIDGET, 0), Spacing = 2 });
        for (int i = 0; i < picks.Length; i++)
        {
            var row = column.Add(new StackPanel { Anchor = Origin.Left, Direction = UIDirection.Horizontal, Spacing = 4 });
            row.Add(new Label(MoveFields.StanceNames[i]) { Size = new Vector2(66, 22), TextScale = 0.105f, Align = Origin.Left });
            picks[i] = row.Add(new Dropdown([NOTHING]) { Size = new Vector2(WIDGET - 70, 22), TextScale = 0.105f, MaxRows = 14 });
            picks[i].OnChanged = _ => Changed();
        }

        void Changed()
        {
            var routes = new Dictionary<string, IRuntimeValue>();
            for (int i = 0; i < picks.Length; i++)
            {
                if (picks[i].Value != NOTHING) routes[MoveFields.StanceNames[i]] = new StringValue(picks[i].Value);
            }

            Emit(routes.Count == 0 ? null : new ObjectValue(routes));
        }
    }

    public override void SetOptions(string[] given)
    {
        options = [NOTHING, .. given];
        foreach (Dropdown pick in picks) pick.Options = options;
    }

    protected override void Display(IRuntimeValue? value)
    {
        var routes = value is ObjectValue { Properties: { } properties } ? properties : [];
        for (int i = 0; i < picks.Length; i++)
        {
            string target = routes.FirstOrDefault(r => r.Key.Equals(MoveFields.StanceNames[i], StringComparison.OrdinalIgnoreCase)).Value is StringValue s ? s.Value : NOTHING;
            picks[i].Index = Math.Max(0, Array.IndexOf(options, target));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the bindings of one gamepad can be changed.
/// Leaving the scene saves the bindings of every gamepad, see <see cref="GameInput.Save"/>.
/// </summary>
internal class BindingsScene(int slot, MatchSetup setup) : Scene
{
    private const float INPUT_DELAY = 0.25f;

    // How long we wait for a button before giving up on it
    private const float LISTEN_TIME = 5.0f;

    private const string HINT_BROWSE = "[icon:dpad] choose    [icon:pad_a] change    [icon:pad_x] add another    [icon:pad_y] clear    [icon:pad_b] done";
    private const string TEXT_LISTENING = "press a button...";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    public override Camera ActiveCamera { get; protected set; }

    // The glass everything is seen through
    private Renderer2D screen = null!;

    // The rows of the actions come first, the two buttons underneath them last
    private readonly List<Button> entries = [];
    private Label hint;
    private StackPanel panel;
    private Gamepad gamepad;
    private int selectedIndex = 0;
    private float _delayTimer = 0.0f;

    // The action that is waiting for a button, -1 while none is
    private int listeningIndex = -1;
    private bool listeningToAdd;
    private float listenTimer;

    // Everything that has been held since we started waiting, and whether we have started collecting it yet
    private uint capturedMask;
    private bool captureArmed;

    // The actions are in the columns of the layout, the next one starts where the one before it is full
    private int rowsPerColumn = 1;
    private int RowsPerColumn => rowsPerColumn;

    private int ResetIndex => GameInput.Actions.Length;
    private int DoneIndex => GameInput.Actions.Length + 1;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));

        // The selector only sends us here for a gamepad that exists
        GameInput.Manager.TryGet(slot, out gamepad);

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        Refresh();
        Select(0);

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;
        base.UpdateState(dt);

        // Nothing to bind on a gamepad that has been pulled out
        if (gamepad is not { IsConnected: true })
        {
            Leave();
            return;
        }

        // The button that got us here is most likely still held
        if (_delayTimer < INPUT_DELAY) return;

        if (listeningIndex >= 0)
        {
            UpdateListening(dt);
            return;
        }

        if (GameInput.MenuDownPressed(gamepad))
        {
            Select(selectedIndex + 1);
        }
        else if (GameInput.MenuUpPressed(gamepad))
        {
            Select(selectedIndex - 1);
        }
        else if (GameInput.MenuRightPressed(gamepad))
        {
            SelectAcross(1);
        }
        else if (GameInput.MenuLeftPressed(gamepad))
        {
            SelectAcross(-1);
        }
        else if (gamepad.WasPressed(GamepadInput.A))
        {
            Activate(selectedIndex, add: false);
        }
        else if (gamepad.WasPressed(GamepadInput.X))
        {
            Activate(selectedIndex, add: true);
        }
        else if (gamepad.WasPressed(GamepadInput.Y))
        {
            Clear(selectedIndex);
        }
        else if (gamepad.WasPressed(GamepadInput.B))
        {
            Leave();
        }
    }

    /// <summary>
    /// Helper method to take an action off every button it is on, the action stays and can be put back on one.
    /// </summary>
    private void Clear(int index)
    {
        if (index >= GameInput.Actions.Length) return;

        gamepad.Bindings.Bind(GameInput.Actions[index].Name);
        entries[index].Shake(5.0f, 0.2f);
        Refresh();
    }

    private void UpdateListening(float dt)
    {
        listenTimer -= dt;
        uint held = gamepad.HeldMask;

        // Whatever was held when the row was picked has to be let go of first, it is not part of the answer
        if (!captureArmed)
        {
            captureArmed = held == 0;
            return;
        }

        if (held != 0)
        {
            // Buttons add up for as long as any of them is held, which is how A + B gets bound
            if ((capturedMask | held) != capturedMask)
            {
                capturedMask |= held;
                entries[listeningIndex].Label = GameInput.Describe(gamepad, capturedMask);
            }
            return;
        }

        if (capturedMask != 0)
        {
            int bound = listeningIndex;
            bool changed = Bind(GameInput.Actions[bound].Name, capturedMask);

            entries[bound].Punch(0.06f, 0.3f);
            StopListening();

            // On to the next action, so a whole column can be set by pressing A and a button over and over
            if (changed && bound + 1 < GameInput.Actions.Length)
            {
                Select(bound + 1);
            }
        }
        else if (listenTimer <= 0)
        {
            StopListening();
        }
    }

    /// <summary>
    /// Helper method to put an action on the buttons that were just pressed, returns false if they meant never mind instead.
    /// </summary>
    private bool Bind(string action, uint combination)
    {
        // Start and back stay out of the fight, here they are the way out of changing your mind
        const uint reserved = (1u << (int)GamepadInput.Start) | (1u << (int)GamepadInput.Back);
        if ((combination & reserved) != 0) return false;

        if (listeningToAdd)
        {
            gamepad.Bindings.AddCombination(action, combination);
        }
        else
        {
            // Whatever else was on exactly these buttons loses them, the same buttons doing two things is never what you want
            gamepad.Bindings.Rebind(action, combination);
        }

        return true;
    }

    /// <summary>
    /// Called for the entry that was clicked, or that A or X was pressed on.
    /// </summary>
    private void Activate(int index, bool add)
    {
        // A click can land on an entry while another one is still waiting for its button
        if (listeningIndex >= 0) StopListening();

        Select(index);

        if (index == ResetIndex)
        {
            gamepad.Bindings.CopyFrom(GameInput.CreateDefaultBindings());
            Refresh();

            // Every row gives a nod, they have all just changed
            for (int i = 0; i < GameInput.Actions.Length; i++)
            {
                entries[i].Punch(0.04f, 0.25f).SetDelay((i % RowsPerColumn) * 0.03f);
            }
        }
        else if (index == DoneIndex)
        {
            Leave();
        }
        else
        {
            listeningIndex = index;
            listeningToAdd = add;
            listenTimer = LISTEN_TIME;
            capturedMask = 0;
            captureArmed = false;

            entries[index].Label = TEXT_LISTENING;
            hint.Text = GameInput.Localize($"Press the button for {GameInput.Actions[index].Label.ToLowerInvariant()}, or several together    start cancels", gamepad);
        }
    }

    private void StopListening()
    {
        listeningIndex = -1;
        Refresh();
    }

    private void Leave()
    {
        GameInput.Save();
        Engine.SetScene(new GamepadSelectorScene(setup));
    }

    /// <summary>
    /// Helper method to step sideways: to the same row of the other column, or between the two buttons underneath.
    /// </summary>
    private void SelectAcross(int direction)
    {
        if (selectedIndex >= ResetIndex)
        {
            Select(direction > 0 ? DoneIndex : ResetIndex);
            return;
        }

        int target = selectedIndex + direction * RowsPerColumn;
        if (target >= 0 && target < GameInput.Actions.Length) Select(target);
    }

    private void Select(int index)
    {
        selectedIndex = Math.Clamp(index, 0, entries.Count - 1);

        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Selected = i == selectedIndex;
        }
    }

    /// <summary>
    /// Helper method to write what every action is bound to back onto its row.
    /// </summary>
    private void Refresh()
    {
        for (int i = 0; i < GameInput.Actions.Length; i++)
        {
            entries[i].Label = GameInput.Describe(gamepad, GameInput.Actions[i].Name);
        }

        hint.Text = GameInput.Localize(HINT_BROWSE, gamepad);
    }

    private void CompositeImages()
    {
        var spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new Sprite(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new System.Numerics.Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeUi()
    {
        // The screen is laid out in Assets/ui/layouts/bindings.hor, the row of an action in binding_row.hor
        UILayout layout = MenuLayouts.Load(this, (Camera2D)ActiveCamera, MenuLayouts.BINDINGS);
        panel = layout.Get<StackPanel>("panel");

        // The player is whoever picked this gamepad, which is not the same thing as the slot it is plugged into
        int player = Math.Max(0, setup.Slots.IndexOf(slot));
        layout.Get<Label>("title").Text = $"Bindings of player {player + 1}";
        layout.Get<Label>("gamepad_name").Text = gamepad?.Name ?? string.Empty;

        // The actions are shared out over however many columns the layout has, there are more of them than fit underneath each other
        var columns = new List<StackPanel>();
        while (layout.TryGet($"column_{columns.Count + 1}", out StackPanel? column))
        {
            columns.Add(column);
        }

        if (columns.Count == 0) columns.Add(layout.Get<StackPanel>("column_1"));

        rowsPerColumn = (GameInput.Actions.Length + columns.Count - 1) / columns.Count;

        for (int c = 0; c < columns.Count; c++)
        {
            int first = c * rowsPerColumn;
            var items = layout.Populate(columns[c], Math.Clamp(GameInput.Actions.Length - first, 0, rowsPerColumn));

            for (int i = 0; i < items.Count; i++)
            {
                int index = first + i;
                items[i].Get<Label>("action").Text = GameInput.Actions[index].Label;

                // Clicking a row does what A does on it, the button to bind still has to come from the gamepad
                var entry = items[i].Get<Button>("binding");
                entry.OnPressed = () => Activate(index, add: false);

                entries.Add(entry);
            }
        }

        var defaults = layout.Get<Button>("btn_defaults");
        defaults.OnPressed = () => Activate(ResetIndex, add: false);
        entries.Add(defaults);

        var done = layout.Get<Button>("btn_done");
        done.OnPressed = () => Activate(DoneIndex, add: false);
        entries.Add(done);

        // How the panel pops up and the rows slide in from the side one after the other is in the layouts
        hint = layout.Get<Label>("hint");
    }
}

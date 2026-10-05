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
/// Scene where the bindings of one gamepad are changed, every action of the fight has a row that shows what it is on.
/// Picking a row and pressing A waits for the next button on that gamepad and moves the action onto it, X adds the button to the ones it has.
/// Buttons that are held together are bound together, which is how an action ends up on A + B.
/// Leaving the scene saves the bindings of every gamepad, see <see cref="GameInput.Save"/>.
/// </summary>
internal class BindingsScene(int slot, MatchSetup setup) : Scene
{
    private const float INPUT_DELAY = 0.25f;

    // How long we wait for a button before giving up on it
    private const float LISTEN_TIME = 5.0f;

    private const string HINT_BROWSE = "[icon:dpad] choose    [icon:pad_a] change    [icon:pad_x] add another    [icon:pad_b] done";
    private const string TEXT_LISTENING = "press a button...";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    public override Camera ActiveCamera { get; protected set; }

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

    private int ResetIndex => GameInput.Actions.Length;
    private int DoneIndex => GameInput.Actions.Length + 1;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));

        // The selector only sends us here for a gamepad that exists
        GameInput.Manager.TryGet(slot, out gamepad);

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
        else if (gamepad.WasPressed(GamepadInput.A))
        {
            Activate(selectedIndex, add: false);
        }
        else if (gamepad.WasPressed(GamepadInput.X))
        {
            Activate(selectedIndex, add: true);
        }
        else if (gamepad.WasPressed(GamepadInput.B))
        {
            Leave();
        }
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
            Bind(GameInput.Actions[listeningIndex].Name, capturedMask);
            entries[listeningIndex].Punch(0.06f, 0.3f);
            StopListening();
        }
        else if (listenTimer <= 0)
        {
            StopListening();
        }
    }

    private void Bind(string action, uint combination)
    {
        // Start and back stay out of the fight, here they are the way out of changing your mind
        const uint reserved = (1u << (int)GamepadInput.Start) | (1u << (int)GamepadInput.Back);
        if ((combination & reserved) != 0) return;

        if (listeningToAdd)
        {
            gamepad.Bindings.AddCombination(action, combination);
        }
        else
        {
            // Whatever else was on exactly these buttons loses them, the same buttons doing two things is never what you want
            gamepad.Bindings.Rebind(action, combination);
        }
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
                entries[i].Punch(0.04f, 0.25f).SetDelay(i * 0.03f);
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
        var spriteBatch = AddEntity<SpriteBatch>();

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
        var compositor = AddComponent(new UICompositor((Camera2D)ActiveCamera, Constants.UI_THEME));
        panel = compositor.CreateModule().AddComponent(new StackPanel
        {
            Background = "panel",
            Padding = new UIEdges(40, 34),
            Spacing = 20
        });

        // The player is whoever picked this gamepad, which is not the same thing as the slot it is plugged into
        int player = Math.Max(0, setup.Slots.IndexOf(slot));
        panel.Add(new Label($"Bindings of player {player + 1}") { TextScale = 0.6f });
        panel.Add(new Label(gamepad?.Name ?? string.Empty) { TextScale = 0.25f, Color = HintColor });

        var list = panel.Add(new StackPanel { Spacing = 8 });
        for (int i = 0; i < GameInput.Actions.Length; i++)
        {
            int index = i;
            var row = list.Add(new StackPanel { Direction = UIDirection.Horizontal, Spacing = 16 });

            row.Add(new Label(GameInput.Actions[i].Label) { Size = new Vector2(230, 0), Align = Origin.Left, TextScale = 0.3f });

            // Clicking a row does what A does on it, the button to bind still has to come from the gamepad
            entries.Add(row.Add(new Button
            {
                Style = "button_flat",
                Size = new Vector2(520, 0),
                LabelScale = 0.3f,
                OnPressed = () => Activate(index, add: false)
            }));
        }

        var actions = panel.Add(new StackPanel { Direction = UIDirection.Horizontal, Spacing = 16 });
        entries.Add(actions.Add(new Button("Defaults") { Size = new Vector2(240, 0), OnPressed = () => Activate(ResetIndex, add: false) }));
        entries.Add(actions.Add(new Button("Done") { Size = new Vector2(180, 0), OnPressed = () => Activate(DoneIndex, add: false) }));

        hint = panel.Add(new Label
        {
            TextScale = 0.25f,
            Color = HintColor
        });

        // The panel pops up and the rows slide in from the side one after the other
        panel.PopIn(0.4f);
        for (int i = 0; i < list.Children.Count; i++)
        {
            list.Children[i].SlideIn(new Vector2(-140, 0), 0.3f, 0.15f + i * 0.04f);
        }
    }
}

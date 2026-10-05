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
/// Scene where the players pick their gamepads, one after the other: player one presses any button on the gamepad they want, then player two does.
/// A gamepad that has been picked can change its bindings from here as well, see <see cref="BindingsScene"/>.
/// </summary>
internal class GamepadSelectorScene(MatchSetup setup) : Scene
{
    private const int MAX_NAME_LENGTH = 22;
    private const float INPUT_DELAY = 0.25f;

    private const string TEXT_WAITING = "press any button";
    private const string HINT_READY = "[icon:pad_a] continue    [icon:pad_y] bindings    [icon:pad_b] undo";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 NameColor = new(0.0f, 0.86f, 1.0f, 1.0f);

    /// <summary>
    /// Everything on screen that belongs to one player.
    /// </summary>
    private sealed class Card
    {
        public StackPanel Box = null!;
        public Label Status = null!, Detail = null!;
    }

    public override Camera ActiveCamera { get; protected set; }

    private Card[] cards = [];
    private StackPanel panel;
    private Label hint;
    private float _delayTimer = 0.0f;
    private float totalTime = 0.0f;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));

        CompositeImages();
        CompositeUi();

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;
        totalTime += dt;

        DropUnplugged();
        UpdateCards();

        base.UpdateState(dt);

        // The button that got us here is most likely still held, dont let it pick a gamepad straight away
        if (_delayTimer < INPUT_DELAY) return;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Only one thing happens per update, whoever pressed first wins
            if (setup.Slots.Contains(gamepad.Slot) ? UpdatePicked(gamepad) : UpdateUnpicked(gamepad)) return;
        }
    }

    /// <summary>
    /// Called for every gamepad that belongs to a player already, returns whether it did anything.
    /// </summary>
    private bool UpdatePicked(Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.Y))
        {
            Engine.SetScene(new BindingsScene(gamepad.Slot, setup));
            return true;
        }

        if (gamepad.WasPressed(GamepadInput.B))
        {
            // The last player to pick goes back to picking
            setup.Slots.RemoveAt(setup.Slots.Count - 1);
            cards[setup.Slots.Count].Box.Shake(8.0f);
            return true;
        }

        if (setup.IsReady && gamepad.WasPressed(GamepadInput.A))
        {
            Engine.SetScene(new MapSelectionScene(setup));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called for every gamepad nobody has picked yet, returns whether it did anything.
    /// </summary>
    private bool UpdateUnpicked(Gamepad gamepad)
    {
        if (setup.IsReady || gamepad.Pressed is not { } input) return false;

        // Any button picks the gamepad, apart from the one that means back everywhere else (and only while there is nothing to undo)
        if (input == GamepadInput.B && setup.Slots.Count == 0)
        {
            Back();
            return true;
        }

        setup.Slots.Add(gamepad.Slot);

        // The card of whoever just picked jumps, so there is no doubt it went to them
        cards[setup.Slots.Count - 1].Box.Punch(0.1f, 0.3f);
        return true;
    }

    /// <summary>
    /// Helper method to take a gamepad away from its player when it is pulled out, along with everybody who picked after them.
    /// </summary>
    private void DropUnplugged()
    {
        for (int i = 0; i < setup.Slots.Count; i++)
        {
            if (GameInput.Manager.TryGet(setup.Slots[i], out Gamepad gamepad) && gamepad.IsConnected) continue;

            setup.Slots.RemoveRange(i, setup.Slots.Count - i);
            return;
        }
    }

    private void UpdateCards()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            Card card = cards[i];

            if (i < setup.Slots.Count && GameInput.Manager.TryGet(setup.Slots[i], out Gamepad gamepad))
            {
                // Holding anything turns the name white, which is how you check the gamepad in your hands is the one you picked
                card.Status.Text = Shorten(gamepad.Name);
                card.Status.Color = gamepad.AnyDown ? Vector4.One : NameColor;
                card.Detail.Text = GameInput.Localize("[icon:pad_y] bindings", gamepad);
            }
            else if (i == setup.Slots.Count)
            {
                // The player whose turn it is gets a text that pulses
                float pulse = 0.65f + 0.35f * MathF.Sin(totalTime * 6.0f);

                card.Status.Text = TEXT_WAITING;
                card.Status.Color = new Vector4(pulse, pulse, pulse, 1.0f);
                card.Detail.Text = "on your gamepad";
            }
            else
            {
                card.Status.Text = "waiting";
                card.Status.Color = HintColor;
                card.Detail.Text = $"for player {i}";
            }
        }

        // The hint is for everybody, so it shows the buttons of whichever gamepad was touched last
        Gamepad? lastUsed = GameInput.Manager.LastUsed;

        if (setup.IsReady)
        {
            hint.Text = GameInput.Localize(HINT_READY, lastUsed);
        }
        else if (GameInput.Manager.ConnectedCount <= setup.Slots.Count)
        {
            hint.Text = "Plug in a gamepad to continue";
        }
        else
        {
            hint.Text = GameInput.Localize(setup.Slots.Count == 0 ? "[icon:pad_b] back" : "[icon:pad_y] bindings    [icon:pad_b] undo", lastUsed);
        }
    }

    private void Back()
    {
        Engine.SetScene(new MainMenuScene());
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
            Spacing = 24
        });

        panel.Add(new Label(setup.Title) { TextScale = 0.6f });

        // One card per player, side by side
        var row = panel.Add(new StackPanel { Direction = UIDirection.Horizontal, Spacing = 24 });
        cards = new Card[setup.PlayerCount];
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i] = CompositeCard(row, i);
        }

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = panel.Add(new Label
        {
            Size = new Vector2(0, 24),
            TextScale = 0.25f,
            Color = HintColor
        });

        // For the mouse, a gamepad goes back with B
        panel.Add(new Button("Back") { Size = new Vector2(180, 0), OnPressed = Back });

        // The panel pops up, the cards follow one after the other
        panel.PopIn(0.4f);
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].Box.PopIn(0.35f, 0.2f + i * 0.12f);
        }
    }

    private static Card CompositeCard(StackPanel row, int player)
    {
        var box = row.Add(new StackPanel
        {
            Background = "button_flat",
            Padding = new UIEdges(28, 26),
            Spacing = 14
        });

        box.Add(new Label($"Player {player + 1}") { TextScale = 0.45f });

        // Kept at a fixed size so the cards dont jump around as the texts change
        return new Card
        {
            Box = box,
            Status = box.Add(new Label { Size = new Vector2(400, 30), TextScale = 0.3f }),
            Detail = box.Add(new Label { Size = new Vector2(400, 24), TextScale = 0.25f, Color = HintColor })
        };
    }

    /// <summary>
    /// Helper method to cut a name short, some gamepads have a lot to say about themselves.
    /// </summary>
    private static string Shorten(string name)
    {
        if (name.Length == 0) return "Gamepad";

        return name.Length <= MAX_NAME_LENGTH ? name : name[..(MAX_NAME_LENGTH - 2)] + "..";
    }
}

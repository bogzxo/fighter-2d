using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text;

using Fighter2D.Networking;
using Horizon.Engine;
using Horizon.Input2;
using Horizon.Rendering;
using Horizon.Rendering.Spriting;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the address of a server is typed in, with an on-screen keyboard that any gamepad can drive (or a real one).
/// Once the server answers we move on to its lobby, see <see cref="GamepadSelectorScene"/>.
/// </summary>
internal class JoinServerScene : Scene
{
    private const float INPUT_DELAY = 0.25f;
    private const string KEY_BACK = "BACK";
    private const string KEY_CONNECT = "CONNECT";
    private const string HINT = "[icon:dpad] move    [icon:pad_a] press    [icon:pad_b] delete    [icon:pad_y] connect";

    // The address that was typed in last, so it does not have to be typed again after every fight
    private static string lastAddress = "127.0.0.1";

    private static readonly Vector4 HintColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 ErrorColor = new(1.0f, 0.45f, 0.4f, 1.0f);

    public override Camera ActiveCamera { get; protected set; }

    // The glass everything is seen through
    private Renderer2D screen = null!;

    private Label status, hint;
    private TextBox addressBox;
    private OnScreenKeyboard keyboard;
    private float _delayTimer = 0.0f;

    // The connection we are waiting on, null while the address is still being typed
    private NetSession? session;

    public override void Initialize()
    {
        ActiveCamera = AddEntity(new Camera2D(Engine.WindowManager.WindowSize));
        Engine.GL.ClearColor(System.Drawing.Color.PaleVioletRed);

        screen = Screen.For(this);

        CompositeImages();
        CompositeUi();

        base.Initialize();
    }

    public override void UpdateState(float dt)
    {
        _delayTimer += dt;
        base.UpdateState(dt);

        // The hint shows the buttons of whichever gamepad was touched last
        hint.Text = GameInput.Localize(HINT, GameInput.Manager.LastUsed);

        if (session is not null)
        {
            UpdateConnecting();
            return;
        }

        // The button that got us here is most likely still held
        if (_delayTimer < INPUT_DELAY) return;

        // Nobody has picked a gamepad yet, so the keyboard listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Move the highlighted key around with the d-pad
            int right = (GameInput.MenuRightPressed(gamepad) ? 1 : 0) - (GameInput.MenuLeftPressed(gamepad) ? 1 : 0);
            int down = (GameInput.MenuDownPressed(gamepad) ? 1 : 0) - (GameInput.MenuUpPressed(gamepad) ? 1 : 0);
            if (right != 0 || down != 0)
            {
                keyboard.Navigate(right, down);
            }

            if (gamepad.WasPressed(GamepadInput.A))
            {
                keyboard.Press();
            }
            else if (gamepad.WasPressed(GamepadInput.B))
            {
                addressBox.Backspace();
            }
            else if (gamepad.WasPressed(GamepadInput.Y))
            {
                Connect();
            }

            // A key can have taken us somewhere else
            if (session is not null) return;
        }
    }

    /// <summary>
    /// Called every update while we wait for the server to answer.
    /// </summary>
    private void UpdateConnecting()
    {
        if (session!.IsConnected)
        {
            // We are in, the rest happens in the lobby
            Engine.SetScene(new GamepadSelectorScene(MatchSetup.Online(session)));
            session = null;
        }
        else if (session.HasFailed)
        {
            session.Dispose();
            session = null;

            ShowError("nobody home.");
        }
    }

    private void Connect()
    {
        if (session is not null) return;

        string text = addressBox.Text.Trim();

        if (!IPAddress.TryParse(text, out _))
        {
            ShowError("dude cmon.");
            return;
        }

        lastAddress = text;
        session = NetSession.Join(text);

        status.Color = HintColor;
        status.Text = "connecting...";
    }

    private void ShowError(string message)
    {
        status.Color = ErrorColor;
        status.Text = message;
        addressBox.Shake(10.0f);
    }

    /// <summary>
    /// Called for every key of the on-screen keyboard, the ones that type have already done so by now.
    /// </summary>
    private void OnKey(string key)
    {
        if (key == KEY_BACK)
        {
            Back();
        }
        else if (key == KEY_CONNECT)
        {
            Connect();
        }
    }

    private void Back()
    {
        session?.Dispose();
        session = null;

        Engine.SetScene(new MainMenuScene());
    }

    private void CompositeImages()
    {
        var spriteBatch = screen.AddEntity<SpriteBatch>();

        if (Engine.ObjectManager.Textures.TryCreateOrGet("gpselbg", new Horizon.OpenGL.Descriptions.TextureDescription { Paths = ["Assets/backgrounds/player_select_bg.png"], Definition = Horizon.OpenGL.Descriptions.TextureDefinition.RgbaUnsignedByteNearest }, out var result_bg))
        {
            var bg = spriteBatch.AddEntity(new Sprite(Engine.WindowManager.WindowSize));
            bg.Transform.SetPositionRelativeToOrigin(new Vector2(-Engine.WindowManager.WindowSize.X / 2, Engine.WindowManager.WindowSize.Y / 2));
            bg.ConfigureSpriteSheet(SpriteSheet.FromTexture(result_bg.Asset, new Vector2(result_bg.Asset.Width, result_bg.Asset.Height)), "bg");

            spriteBatch.Add(bg);
        }
    }

    private void CompositeUi()
    {
        // The screen is laid out in Assets/ui/layouts/join_server.hor
        UILayout layout = MenuLayouts.Load(this, (Camera2D)ActiveCamera, MenuLayouts.JOIN_SERVER);

        // Empty until there is something to say
        status = layout.Get<Label>("status");
        status.Text = string.Empty;

        // A real keyboard can type into the box as well, enter connects
        addressBox = layout.Get<TextBox>("address");
        addressBox.Text = lastAddress;
        addressBox.AllowedCharacters = "0123456789.";
        addressBox.OnChanged = _ => status.Text = string.Empty;
        addressBox.OnSubmitted = _ => Connect();

        // The keys are ours to make, the layout only says where they go
        // The digits type into the box by themselves, the two keys on the last row are ours to handle
        keyboard = layout.Get<StackPanel>("keyboard_slot").Add(new OnScreenKeyboard([.. OnScreenKeyboard.Numeric, [KEY_BACK, KEY_CONNECT]])
        {
            Target = addressBox,
            OnKey = OnKey
        });

        // The skin draws the buttons of the gamepad where the text asks for them
        hint = layout.Get<Label>("hint");

        // The box takes whatever is typed on a real keyboard for as long as the screen is up
        addressBox.Focus();
        keyboard.Select("1");
    }
}

using System.Net;

using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the address of a server is typed in, with an on-screen keyboard that any gamepad can drive (or a real one).
/// Once the server answers we move on to its lobby, see <see cref="GamepadSelectorScene"/>.
/// </summary>
internal class JoinServerScene : MenuScene
{
    private const string KEY_BACK = "BACK";
    private const string KEY_CONNECT = "CONNECT";
    private const string HINT = "[icon:dpad] move    [icon:pad_a] press    [icon:pad_b] delete    [icon:pad_y] connect";

    // The address that was typed in last, so it doesn't have to be typed again after every fight
    private static string lastAddress = "127.0.0.1";

    protected override string LayoutFile => MenuLayouts.JOIN_SERVER;

    private Label _status = null!, _hint = null!;
    private TextBox _addressBox = null!;
    private OnScreenKeyboard _keyboard = null!;

    // The connection we are waiting on, null while the address is still being typed
    private NetSession? _session;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/join_server.hor
        _status = layout.Get<Label>("status");
        _status.Text = string.Empty;
        _hint = layout.Get<Label>("hint");

        // A real keyboard can type into the box as well, enter connects
        _addressBox = layout.Get<TextBox>("address");
        _addressBox.Text = lastAddress;
        _addressBox.AllowedCharacters = "0123456789.";
        _addressBox.OnChanged = _ => _status.Text = string.Empty;
        _addressBox.OnSubmitted = _ => Connect();

        // The layout only says where the keyboard goes, the keys are ours to make.
        // The digits type into the box by themselves, the two keys on the last row are ours to handle
        _keyboard = layout.Get<StackPanel>("keyboard_slot").Add(new OnScreenKeyboard([.. OnScreenKeyboard.Numeric, [KEY_BACK, KEY_CONNECT]])
        {
            Target = _addressBox,
            OnKey = OnKey
        });

        _addressBox.Focus();
        _keyboard.Select("1");
    }

    protected override void UpdateMenu(float dt)
    {
        // The hint shows the buttons of whichever gamepad was touched last
        _hint.Text = ButtonGlyphs.Localize(HINT, GameInput.Manager.LastUsed);

        if (_session is not null)
        {
            UpdateConnecting(_session);
            return;
        }

        if (!InputReady) return;

        // Nobody has picked a gamepad yet, so the keyboard listens to all of them
        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            int right = MenuInput.Horizontal(gamepad), down = MenuInput.Vertical(gamepad);
            if (right != 0 || down != 0) _keyboard.Navigate(right, down);

            if (gamepad.WasPressed(GamepadInput.A)) _keyboard.Press();
            else if (gamepad.WasPressed(GamepadInput.B)) _addressBox.Backspace();
            else if (gamepad.WasPressed(GamepadInput.Y)) Connect();

            // A key can have started connecting or taken us somewhere else
            if (_session is not null) return;
        }
    }

    /// <summary>
    /// Called every update while we wait for the server to answer.
    /// </summary>
    private void UpdateConnecting(NetSession session)
    {
        if (session.IsConnected)
        {
            // We are in, the rest happens in the lobby
            _session = null;
            GoTo(new GamepadSelectorScene(MatchSetup.Online(session)));
        }
        else if (session.HasFailed)
        {
            session.Dispose();
            _session = null;

            ShowError("nobody home.");
        }
    }

    private void Connect()
    {
        if (_session is not null) return;

        string text = _addressBox.Text.Trim();

        if (!IPAddress.TryParse(text, out _))
        {
            ShowError("dude cmon.");
            return;
        }

        lastAddress = text;
        _session = NetSession.Join(text);

        _status.Color = MenuColors.Hint;
        _status.Text = "connecting...";
    }

    private void ShowError(string message)
    {
        _status.Color = MenuColors.Error;
        _status.Text = message;
        _addressBox.Shake(10.0f);
    }

    /// <summary>
    /// Called for every key of the on-screen keyboard, the ones that type have already done so by now.
    /// </summary>
    private void OnKey(string key)
    {
        if (key == KEY_BACK) Back();
        else if (key == KEY_CONNECT) Connect();
    }

    private void Back()
    {
        _session?.Dispose();
        _session = null;

        GoTo(new MainMenuScene());
    }
}

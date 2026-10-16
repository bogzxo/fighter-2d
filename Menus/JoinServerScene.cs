using Horizon.Content;
using System.Net;

using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.Menus;

/// <summary>
/// Scene where the address of a server is typed in, with an on-screen keyboard that any gamepad can drive (or a real one).
/// Once the server answers we move on to its lobby, see <see cref="FightSetupScene"/>.
/// </summary>
internal class JoinServerScene : MenuScene
{
    // The two keys of our own on the keyboard, which are what they say, the same string every time it is asked for
    private static string KeyBack => Localisation.Get("join.key_back");
    private static string KeyConnect => Localisation.Get("join.key_connect");

    // The address that was typed in last, so it doesn't have to be typed again after every fight

    protected override string LayoutFile => MenuLayouts.JOIN_SERVER;

    private Label _status = null!;
    private TextBox _addressBox = null!;
    private OnScreenKeyboard _keyboard = null!;

    // The connection we are waiting on, null while the address is still being typed
    private NetSession? _session;

    // The gamepad that did the connecting, which makes it player one's in the lobby. Null when it was the keyboard
    private int? _connectedBy;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/join_server.hor
        _status = layout.Get<Label>("status");
        _status.Text = string.Empty;

        // A real keyboard can type into the box as well, enter connects
        _addressBox = layout.Get<TextBox>("address");
        // Wherever the last fight that was joined was, kept in options.hor between runs
        _addressBox.Text = GameOptions.JoinAddress;
        _addressBox.AllowedCharacters = "0123456789.";
        _addressBox.OnChanged = _ => _status.Text = string.Empty;
        _addressBox.OnSubmitted = _ => Connect();

        // The button in the box (the layout puts it there) wipes it, for whoever has a mouse and a wrong address
        _addressBox.OnButtonPressed = ClearAddress;

        // The layout only says where the keyboard goes, the keys are ours to make.
        // The digits type into the box by themselves, the two keys on the last row are ours to handle
        _keyboard = layout.Get<StackPanel>("keyboard_slot").Add(new OnScreenKeyboard([.. OnScreenKeyboard.Numeric, [KeyBack, KeyConnect]])
        {
            Target = _addressBox,
            OnKey = OnKey
        });

        _addressBox.Focus();
        _keyboard.Select("1");
    }

    private void ClearAddress()
    {
        _addressBox.Text = string.Empty;
        _status.Text = string.Empty;
        _addressBox.Focus();
    }

    protected override void UpdateMenu(float dt)
    {
        if (_session is not null) UpdateConnecting(_session);

        // Nothing is pressed while a connection is being made
        Actions.Enabled = _session is null;
    }

    protected override void BuildActions()
    {
        // A real keyboard types straight into the box, which has enter to connect and backspace to delete by itself.
        // All it needs from here is a way out, escape, which on a gamepad is the button that deletes
        Actions.Add(GamepadInput.B, "hints.back", _ => Back(), OnKeyboard);

        Actions.Directions("hints.move", (_, right, down) => _keyboard.Navigate(right, down), OnPad);
        Actions.Add(GamepadInput.A, "hints.press", gamepad => Pressing(gamepad, _keyboard.Press), OnPad);
        Actions.Add(GamepadInput.B, "hints.delete", _ => _addressBox.Backspace(), OnPad);
        Actions.Add(GamepadInput.Y, "hints.connect", gamepad => Pressing(gamepad, Connect), OnPad);
    }

    private static bool OnKeyboard(Gamepad gamepad) => gamepad.Kind == GamepadKind.Keyboard;
    private static bool OnPad(Gamepad gamepad) => gamepad.Kind != GamepadKind.Keyboard;

    /// <summary>
    /// Helper method to press something that may start connecting, remembering whose gamepad it was if it did.
    /// </summary>
    private void Pressing(Gamepad gamepad, Action press)
    {
        _connectedBy = gamepad.Slot;
        press();
        if (_session is null) _connectedBy = null;
    }

    /// <summary>
    /// Called every update while we wait for the server to answer.
    /// </summary>
    private void UpdateConnecting(NetSession session)
    {
        if (session.IsConnected)
        {
            // We are in, the rest happens in the lobby. The address is the one to start on next time
            _session = null;
            GameOptions.JoinAddress = _addressBox.Text.Trim();
            GameOptions.Save();

            var setup = MatchSetup.Online(session);
            if (_connectedBy is { } slot) setup.Slots.Add(slot);
            GoTo(new FightSetupScene(setup));
        }
        else if (session.HasFailed)
        {
            session.Dispose();
            _session = null;

            ShowError(Localisation.Get("join.nobody_home"));
        }
    }

    private void Connect()
    {
        if (_session is not null) return;

        string text = _addressBox.Text.Trim();

        if (!IPAddress.TryParse(text, out _))
        {
            ShowError(Localisation.Get("join.bad_address"));
            return;
        }

        _session = NetSession.Join(text);

        _status.Color = MenuColors.Hint;
        _status.Text = Localisation.Get("join.connecting");
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
        if (key == KeyBack) Back();
        else if (key == KeyConnect) Connect();
    }

    private void Back()
    {
        _session?.Dispose();
        _session = null;

        GoTo(new MainMenuScene());
    }
}

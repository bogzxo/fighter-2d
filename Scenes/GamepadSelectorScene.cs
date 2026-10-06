using Fighter2D.Match;

using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes;

/// <summary>
/// Scene where the players pick their gamepads one after the other, by pressing any button on the one they want.
/// A gamepad that has been picked can change its bindings from here as well, see <see cref="BindingsScene"/>.
/// For an online fight this is the lobby too. What happens once the gamepads are picked is up to the <see cref="GamepadSelectFlow"/>.
/// </summary>
internal class GamepadSelectorScene(MatchSetup setup) : MenuScene
{
    protected override string LayoutFile => MenuLayouts.GAMEPAD_SELECT;

    /// <summary>
    /// What the text of a card is coloured with when it wants somebody's attention.
    /// </summary>
    internal Pulse FastPulse { get; } = new(0.3f, 1.0f, 1.05f);
    internal Pulse SlowPulse { get; } = new(0.3f, 1.0f, 2.1f);

    private PlayerCard[] _cards = [];
    private GamepadSelectFlow _flow = null!;

    protected override void BuildUi(UILayout layout)
    {
        // The screen is laid out in Assets/ui/layouts/gamepad_select.hor, the cards in player_card.hor
        layout.Get<Label>("title").Text = setup.Title;

        // One card per player, side by side. Online there are always two of them, one for each corner of the fight
        var items = layout.Populate("cards", setup.IsOnline ? 2 : setup.PlayerCount);
        _cards = new PlayerCard[items.Count];
        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i] = new PlayerCard(items[i], setup.IsOnline ? (i == 0 ? "Blue corner" : "Red corner") : $"Player {i + 1}");
        }

        var hint = layout.Get<Label>("hint");
        hint.Text = string.Empty;

        // For the mouse, a gamepad goes back with B
        layout.Get<Button>("btn_back").OnPressed = Back;

        _flow = setup.Lobby is { } lobby
            ? new LobbySelectFlow(this, setup, lobby, _cards, hint)
            : new LocalSelectFlow(this, setup, _cards, hint);
    }

    protected override void UpdateMenu(float dt)
    {
        FastPulse.Update(dt);
        SlowPulse.Update(dt);

        DropUnplugged();

        if (_flow.Update()) return;
        _flow.Refresh();

        if (!InputReady) return;

        foreach (Gamepad gamepad in GameInput.Manager.Gamepads)
        {
            if (!gamepad.IsConnected) continue;

            // Only one thing happens per update, whoever pressed first wins
            if (setup.Slots.Contains(gamepad.Slot) ? HandlePicked(gamepad) : HandleUnpicked(gamepad)) return;
        }
    }

    /// <summary>
    /// Called for every gamepad that belongs to a player already, returns whether it did anything.
    /// </summary>
    private bool HandlePicked(Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.Y))
        {
            GoTo(new BindingsScene(gamepad.Slot, setup));
            return true;
        }

        return _flow.HandlePicked(gamepad);
    }

    /// <summary>
    /// Called for every gamepad nobody has picked yet, returns whether it did anything.
    /// </summary>
    private bool HandleUnpicked(Gamepad gamepad)
    {
        if (setup.IsReady || gamepad.Pressed is not { } input) return false;

        // Any button picks the gamepad. Apart from B while there is nothing to undo, that one means back like everywhere else
        if (input == GamepadInput.B && setup.Slots.Count == 0)
        {
            Back();
            return true;
        }

        int card = _flow.NextCard;
        setup.Slots.Add(gamepad.Slot);

        // The card of whoever just picked jumps, so there is no doubt it went to them
        _cards[card].Bump();
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

            // Nobody can be ready without a gamepad
            setup.Lobby?.SetReady(false);
            return;
        }
    }

    /// <summary>
    /// Goes back to the main menu, which for an online fight means hanging up on whoever is in the lobby.
    /// </summary>
    internal void Back()
    {
        setup.Close();
        GoTo(new MainMenuScene());
    }
}

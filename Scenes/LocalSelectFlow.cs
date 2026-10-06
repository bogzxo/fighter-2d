using System.Numerics;

using Fighter2D.Match;

using Horizon.Input2;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes;

/// <summary>
/// The gamepad select screen of a match on one machine. The players pick their gamepads one after the other, then it is on to the characters.
/// </summary>
internal sealed class LocalSelectFlow(GamepadSelectorScene scene, MatchSetup setup, PlayerCard[] cards, Label hint)
    : GamepadSelectFlow(scene, setup, cards, hint)
{
    private const string HINT_READY = "[icon:pad_a] continue    [icon:pad_y] bindings    [icon:pad_b] undo";
    private const string HINT_PICKING = "[icon:pad_y] bindings    [icon:pad_b] undo";
    private const string HINT_EMPTY = "[icon:pad_b] back";

    // Whoever picks next gets the card after the last one that was taken
    public override int NextCard => Setup.Slots.Count;

    public override bool HandlePicked(Gamepad gamepad)
    {
        if (gamepad.WasPressed(GamepadInput.B))
        {
            // The last player to pick goes back to picking
            Setup.Slots.RemoveAt(Setup.Slots.Count - 1);
            Cards[Setup.Slots.Count].Box.Shake(8.0f);
            return true;
        }

        if (Setup.IsReady && gamepad.WasPressed(GamepadInput.A))
        {
            // Everybody has a gamepad, next they pick who they play
            Scene.GoTo(new CharacterSelectScene(Setup));
            return true;
        }

        return false;
    }

    public override void Refresh()
    {
        for (int i = 0; i < Cards.Length; i++)
        {
            RefreshCard(Cards[i], i);
        }

        // The hint is for everybody, so it shows the buttons of whichever gamepad was touched last
        Gamepad? lastUsed = GameInput.Manager.LastUsed;

        if (Setup.IsReady) Hint.Text = ButtonGlyphs.Localize(HINT_READY, lastUsed);
        else if (GameInput.Manager.ConnectedCount <= Setup.Slots.Count) Hint.Text = "Plug in a gamepad to continue";
        else Hint.Text = ButtonGlyphs.Localize(Setup.Slots.Count == 0 ? HINT_EMPTY : HINT_PICKING, lastUsed);
    }

    private void RefreshCard(PlayerCard card, int player)
    {
        if (player < Setup.Slots.Count && GameInput.Manager.TryGet(Setup.Slots[player], out Gamepad gamepad))
        {
            // Holding anything turns the name white, which is how you check the gamepad in your hands is the one you picked
            card.ShowStatus(PlayerCard.Shorten(gamepad.Name), gamepad.AnyDown ? Vector4.One : MenuColors.Name);
            card.ShowDetail(ButtonGlyphs.Localize("[icon:pad_y] bindings", gamepad), MenuColors.Hint);
        }
        else if (player == Setup.Slots.Count)
        {
            ShowWaitingForButton(card);
        }
        else
        {
            card.ShowStatus("waiting", MenuColors.Hint);
            card.ShowDetail($"for player {player}", MenuColors.Hint);
        }
    }
}

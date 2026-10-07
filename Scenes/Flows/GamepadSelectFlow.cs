
using Horizon.Input;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes.Flows;

/// <summary>
/// What the gamepad select screen does once the gamepads are picked.
/// A local match and the lobby of an online one look the same but behave nothing alike, so each gets its own flow.
/// See <see cref="LocalSelectFlow"/> and <see cref="LobbySelectFlow"/>.
/// </summary>
internal abstract class GamepadSelectFlow(GamepadSelectorScene scene, MatchSetup setup, PlayerCard[] cards, Label hint)
{
    protected const string TEXT_WAITING = "press any button";

    protected GamepadSelectorScene Scene { get; } = scene;
    protected MatchSetup Setup { get; } = setup;
    protected PlayerCard[] Cards { get; } = cards;
    protected Label Hint { get; } = hint;

    /// <summary>
    /// The card that belongs to whoever picks a gamepad next.
    /// </summary>
    public abstract int NextCard { get; }

    /// <summary>
    /// Called every update before anything else.
    /// </summary>
    /// <returns>True if the flow has sent us off to another scene.</returns>
    public virtual bool Update() => false;

    /// <summary>
    /// Called every update to write what is going on onto the cards and the hint.
    /// </summary>
    public abstract void Refresh();

    /// <summary>
    /// Called for a gamepad that belongs to a player already.
    /// </summary>
    /// <returns>True if the gamepad did anything.</returns>
    public abstract bool HandlePicked(Gamepad gamepad);

    /// <summary>
    /// Helper method to show that a card is waiting for somebody to press a button on a gamepad.
    /// </summary>
    protected void ShowWaitingForButton(PlayerCard card)
    {
        card.ShowStatus(TEXT_WAITING, Scene.FastPulse.Grey);
        card.ShowDetail("on your gamepad", MenuColors.Hint);
    }
}

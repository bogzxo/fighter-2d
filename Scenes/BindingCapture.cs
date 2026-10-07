using Horizon.Input;

namespace Fighter2D.Scenes;

internal enum CaptureState
{
    // Still waiting for the player to press (or let go of) something
    Listening,

    // Another button joined the ones that are held, the row should show them
    Changed,

    // Everything was let go of, <see cref="BindingCapture.Buttons"/> is the answer
    Captured,

    // Nobody pressed anything in time, or they pressed one of the buttons that mean never mind
    Cancelled
}

/// <summary>
/// Listens to a gamepad for the buttons somebody wants to bind an action to.
/// Buttons add up for as long as any of them is held, which is how A + B gets bound. Letting go of everything is the answer.
/// </summary>
internal sealed class BindingCapture
{
    // How long we wait for a button before giving up on it
    private const float LISTEN_TIME = 5.0f;

    // Start and back stay out of the fight, in here they are how you change your mind
    private const uint RESERVED = (1u << (int)GamepadInput.Start) | (1u << (int)GamepadInput.Back);

    private float _timer;

    // Whatever was held when we started listening has to be let go of first, it isn't part of the answer
    private bool _armed;

    /// <summary>
    /// The row that is waiting for buttons, -1 while none is.
    /// </summary>
    public int Row { get; private set; } = -1;

    /// <summary>
    /// Whether the buttons get added to what the action is already on, rather than replacing it.
    /// </summary>
    public bool Adding { get; private set; }

    /// <summary>
    /// Every button that has been held since we started listening.
    /// </summary>
    public uint Buttons { get; private set; }

    public bool IsListening => Row >= 0;

    public void Start(int row, bool adding)
    {
        Row = row;
        Adding = adding;
        Buttons = 0;
        _timer = LISTEN_TIME;
        _armed = false;
    }

    public void Stop() => Row = -1;

    /// <param name="held">Everything the gamepad is holding right now, see Gamepad.HeldMask.</param>
    public CaptureState Update(float dt, uint held)
    {
        _timer -= dt;

        if (!_armed)
        {
            _armed = held == 0;
            return CaptureState.Listening;
        }

        if (held != 0)
        {
            if ((Buttons | held) == Buttons) return CaptureState.Listening;

            Buttons |= held;
            return CaptureState.Changed;
        }

        if (Buttons != 0) return (Buttons & RESERVED) == 0 ? CaptureState.Captured : CaptureState.Cancelled;

        return _timer <= 0 ? CaptureState.Cancelled : CaptureState.Listening;
    }
}

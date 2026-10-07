using System;

using Fighter2D.Logic;

namespace Fighter2D.Character.Controllers.Dummy;

/// <summary>
/// The fingers of the dummy. Presses a button for a few ticks and lets go of it again, the same way a thumb would.
/// The dummy only ever plays through buttons, so it is bound by the same input rules as a real player.
/// </summary>
internal sealed class DummyHands
{
    private float _pressTimer, _pressGap, _jumpTimer, _jumpCooldown;
    private InputFlags _pressed;

    /// <summary>
    /// Whether a button can be pressed right now and count as a fresh press.
    /// </summary>
    public bool CanPress => _pressTimer <= 0 && _pressGap <= 0;

    /// <summary>
    /// The buttons that are down this tick.
    /// </summary>
    public InputFlags Held => (_pressTimer > 0 ? _pressed : InputFlags.None) | (_jumpTimer > 0 ? InputFlags.DPadUp : InputFlags.None);

    public void Tick()
    {
        _pressTimer -= DummyConfig.TICK;
        _jumpTimer -= DummyConfig.TICK;
        _jumpCooldown -= DummyConfig.TICK;

        // The gap only starts counting once the button is up
        if (_pressTimer <= 0) _pressGap -= DummyConfig.TICK;
    }

    public void Press(InputFlags button)
    {
        _pressed = button;
        _pressTimer = DummyConfig.TAP_DURATION;
        _pressGap = DummyConfig.TAP_GAP;
    }

    /// <summary>
    /// Taps jump, unless it only just jumped.
    /// </summary>
    public void Jump()
    {
        if (_jumpCooldown > 0) return;

        _jumpTimer = DummyConfig.JUMP_HOLD;
        _jumpCooldown = DummyConfig.JUMP_COOLDOWN;
    }

    /// <summary>
    /// Taps jump right now no matter what, for getting out of the way of something.
    /// </summary>
    public void JumpNow() => _jumpTimer = DummyConfig.JUMP_HOLD;

    public static InputFlags Kick() => Random.Shared.NextSingle() < 0.5f ? InputFlags.A : InputFlags.B;
    public static InputFlags Punch() => Random.Shared.NextSingle() < 0.5f ? InputFlags.X : InputFlags.Y;
}

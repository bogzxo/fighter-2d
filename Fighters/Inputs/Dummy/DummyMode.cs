namespace Fighter2D.Fighters.Inputs.Dummy;

/// <summary>
/// What the dummy does with itself. It fights back unless training mode tells it to stand there and take it, see <see cref="DummyPlayerInput.Mode"/>.
/// </summary>
internal enum DummyMode
{
    // Plays the game, as well as it can
    Fight,

    // Stands still and eats everything, for trying combos out
    Stand,

    // Crouches and eats everything, for seeing which of your moves hit crouchers
    Crouch,

    // Holds a standing block, for finding out what is minus on block and what goes over a block
    Block,

    // Holds a crouching block, which stops lows and mids and gets hit by overheads
    CrouchBlock,

    // Does whatever the script it was given says, see DummyScript
    Script
}

internal static class DummyModes
{
    // In the order the training menu steps through them
    public static readonly DummyMode[] All = [DummyMode.Fight, DummyMode.Stand, DummyMode.Crouch, DummyMode.Block, DummyMode.CrouchBlock];

    public static string Describe(DummyMode mode) => mode switch
    {
        DummyMode.Stand => "Stand",
        DummyMode.Crouch => "Crouch",
        DummyMode.Block => "Block",
        DummyMode.CrouchBlock => "Crouch block",
        _ => "CPU"
    };
}

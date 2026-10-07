namespace Fighter2D.Match;

/// <summary>
/// The lab. A practice fight against the dummy gets these on top of the fight itself, from the pause menu: what the dummy does with
/// itself (fights, stands there, crouches, blocks high or low), health that comes back once a combo is over so nobody has to restart
/// the round to try again, and a button that puts both fighters back on their marks.
/// </summary>
internal sealed class TrainingMode(DummyPlayerInput dummy, Player playerOne, Player playerTwo)
{
    // How long (in seconds) both fighters have to be out of a scrap before the health comes back
    private const float SETTLE_TIME = 1.2f;

    private float _calm;

    /// <summary>
    /// What the dummy does with itself, see <see cref="DummyMode"/>.
    /// </summary>
    public DummyMode Dummy
    {
        get => dummy.Mode;
        set => dummy.Mode = value;
    }

    /// <summary>
    /// Whether the health of both fighters comes back once a combo is over and the dust has settled.
    /// </summary>
    public bool RefillHealth { get; set; } = true;

    /// <summary>
    /// Puts both fighters back where they started with full health, the way a new round does.
    /// </summary>
    public void ResetPositions()
    {
        playerOne.ResetForRound();
        playerTwo.ResetForRound();
    }

    /// <summary>
    /// Called every update of a fight that isn't held still.
    /// </summary>
    public void Update(float dt)
    {
        if (!RefillHealth) return;

        // The combo counter is what the player is here to see, so nothing is topped up until the hits have stopped landing
        bool scrapping = IsBusy(playerOne) || IsBusy(playerTwo);
        _calm = scrapping ? 0.0f : _calm + dt;

        if (_calm < SETTLE_TIME) return;

        Refill(playerOne);
        Refill(playerTwo);
    }

    /// <summary>
    /// Helper method to say whether a fighter is in the middle of something a refill would get in the way of.
    /// </summary>
    private static bool IsBusy(Player player)
    {
        if (player.Controller?.State is not { } state) return false;

        return state.IsInHitstun || state.IsInBlockstun || state.CurrentStatus == FighterStatus.Attacking;
    }

    private static void Refill(Player player)
    {
        // Somebody who went down stays down, that is the round's business
        if (player.Health is 0 or Player.MAX_HEALTH) return;

        player.Health = Player.MAX_HEALTH;
    }
}

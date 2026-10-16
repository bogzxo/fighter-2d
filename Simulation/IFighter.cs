namespace Fighter2D.Simulation;

/// <summary>
/// A fighter as the simulation sees it. Who they are (the character and its moves), how they are doing (the health, the
/// meter), the fight they are in and their body in the world (<see cref="IFighterBody"/>). <see cref="Player"/> is one,
/// a test's fake is one, and <see cref="PlayerController"/> drives either.
/// </summary>
internal interface IFighter
{
    /// <summary>What kind of fighter this is, out of the content.</summary>
    CharacterDefinition Character { get; }

    /// <summary>Every move the character has.</summary>
    MoveList MoveList { get; }

    /// <summary>The brain, which reads the buttons and plays the moves.</summary>
    PlayerController Controller { get; }

    /// <summary>The fight we are in, the opponent, the effects, the stage and the rounds are in here.</summary>
    FightContext Fight { get; }

    /// <summary>The body in the world, where we are and what we show.</summary>
    IFighterBody Body { get; }

    /// <summary>The health out of <see cref="CombatRules.MAX_HEALTH"/>, 0 being out.</summary>
    byte Health { get; set; }

    /// <summary>The super meter out of <see cref="CombatRules.MAX_METER"/>. See <see cref="GainMeter"/> and <see cref="SpendMeter"/>.</summary>
    byte Meter { get; set; }

    /// <summary>Adds to the meter, up to full.</summary>
    void GainMeter(int amount)
    {
        if (amount <= 0) return;

        Meter = (byte)Math.Min(CombatRules.MAX_METER, Meter + amount);
    }

    /// <summary>Pays for a move. False (and nothing paid) if there isn't enough in the tank.</summary>
    bool SpendMeter(int amount)
    {
        if (amount > Meter) return false;

        Meter = (byte)(Meter - amount);
        return true;
    }
}

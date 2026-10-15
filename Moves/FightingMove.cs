using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fighter2D.Moves;

/// <summary>
/// One move out of a move file. Everything about it is data, the code only knows how to play it (see MovePlayback).
/// Every number of it means exactly what it says. Times are frames of the fight, 60 a second whoever plays the move,
/// and distances are world units (a fighter is about 30 wide and 73 tall, a tile 16).
/// </summary>
public class FightingMove
{
    public string Id { get; init; } = MoveIds.IDLE;
    public int Damage { get; init; } = 0;

    // Where whoever gets hit is sent, X how far away from the attacker and Y how high. They go up Y above where they
    // were hit and are X along by the time they get to the top, on the hitstun gravity. Nothing up is no launch
    public Vector2 Launch { get; init; } = Vector2.Zero;

    // How much further it launches for every 10 health whoever it hits has lost, X away and Y up, less for a heavy character
    // (CharacterMovement.Weight). At full health a launch is exactly Launch. Smash's knockback scaling
    public Vector2 LaunchGrowth { get; init; } = Vector2.Zero;

    // How far a hit that doesn't launch slides whoever it lands on along the ground. Below zero for the usual (CombatRules.PUSH)
    public float Push { get; init; } = -1.0f;

    // How many frames whoever gets hit is in hitstun for, exactly. Below zero means the move didn't say, then it is
    // worked out from its startup (see MoveFrameData), which is a guess and Hex shows what it came to
    public int Hitstun { get; init; } = -1;

    // How many frames whoever blocks it is stuck in their block for. Below zero for a share of the hitstun
    public int Blockstun { get; init; } = -1;

    // How many frames both of them freeze for when it lands. Below zero for the usual, longer for a launcher
    public int Hitstop { get; init; } = -1;

    // How much of their gravity whoever gets hit falls with while the hitstun lasts, null for what their character
    // says (CharacterMovement.HitstunGravity). Less floats them for longer, MUGEN's yaccel
    public float? HitstunGravity { get; init; }

    // Where the hit comes in, which block stops it. See HitLevel
    public HitLevel Level { get; init; } = HitLevel.Mid;

    // Whether whoever gets hit ends up on the floor. In the air they come down first, on the ground they go down on the spot
    public bool Knockdown { get; init; }

    // How much meter the move burns to come out, 0 for a move anybody can throw any time. See Player.Meter
    public int MeterCost { get; init; }

    // The stances the move can be started from
    public Stance Stances { get; init; } = Fighters.Stance.Standing;

    // Any one of these starts the move, None means the move can only be reached through reroutes
    public InputFlags[] InputSignatures { get; init; } = [InputFlags.None];

    // How a signature has to be entered
    public InputTrigger Trigger { get; init; } = InputTrigger.Held;

    // The order the inputs are matched in, lower goes first. Negative for moves no input starts
    public int Priority { get; init; } = -1;

    // How much of the character's walk speed they can walk at during the move on the ground, and how much of their air speed
    // they can drift at in the air, each 0 to 1. A ground attack plants you (walk 0), an aerial in Smash keeps all of the drift
    public float Walk { get; init; } = 1.0f;
    public float Drift { get; init; } = 1.0f;

    // Whether the player can turn around during the move, the same as whether they can walk in it unless it says
    public bool AllowsTurning { get; init; } = true;

    // What the player is for as long as the move lasts (attacking, blocking, invulnerable), null leaves that alone
    public FighterStatus? Status { get; init; }

    // The stance the move puts the player in and the one it leaves them in after, null leaves that to the physics
    public Stance? Stance { get; init; }
    public Stance? StanceAfter { get; init; }

    // Whether the opponent gets a heads up the moment the move starts, which is what gives the dummy a chance to block
    public bool Warns { get; init; }

    // The move starts over once its last phase is done, for as long as this holds
    public MoveCondition Repeat { get; init; }

    // How many frames of the landing move a player who comes down in the middle of this one is stuck in, 0 to carry on
    // with the move on the ground. An aerial that is landed in the middle of costs, the Smash way
    public int LandingLag { get; init; }

    // The moves this one can be cancelled into from the frame after its hit lands, whatever its cancel frames say. Move ids,
    // kinds of move ("special", "super") or HIT_CANCEL_ANY for anything, null for none. A launcher with jump in here is a jump
    // cancel, a normal with special in here is Street Fighter's cancel
    public string[]? HitCancel { get; init; }

    public const string HIT_CANCEL_ANY = "any";

    // What kind of move it is, which is what a hit cancel by kind goes by
    public MoveKind Kind { get; init; } = MoveKind.Normal;

    // How many frames longer the move lasts when its hit came out and didn't land, and its cancel frames don't count then
    // either. Rivals' whifflag, swinging at nothing costs
    public int WhiffLag { get; init; }

    // The frame by frame logic of the move
    public MovePhase[] Phases { get; init; } = [];

    // Does a different move instead depending on the stance the player is in when the input comes (kick in the air -> jump kick)
    public Dictionary<Stance, string>? StanceReroutes { get; init; }

    // Carries on into a different move once this one is finished, depending on the stance the player is in by then
    public Dictionary<Stance, string>? FinishReroutes { get; init; }

    // The hitboxes drawn by hand for the move (see CustomHitboxes), spread over its active frames, null for the limb its art has sticking out
    internal Fighters.Box[]? CustomHitboxes { get; set; }

    // How many frames from the hit frame the hitbox stays out for, the active frames. What the phase with the hit says,
    // or the hand drawn hitboxes, or 1. The move tries to land on every one of them until it does, a hit is a hit once
    public int ActiveFrames { get; internal set; } = 1;

    // Startup, recovery and hitstun in frames, worked out once the character's animations are known
    public MoveFrameData FrameData { get; internal set; }

    /// <summary>
    /// Whether the move sends whoever it hits up in the air.
    /// </summary>
    public bool Launches => Launch.Y > 0.0f;

    /// <summary>
    /// Whether the move uses up one of the jumps a character has in the air.
    /// </summary>
    public bool IsAirJump => Array.Exists(Phases, phase => phase.Jump == JumpKind.Double);

    /// <summary>
    /// Whether a move can be cancelled into another one once its hit has landed.
    /// </summary>
    public bool HitCancelsInto(FightingMove move) =>
        HitCancel is { } into
        && (Array.IndexOf(into, HIT_CANCEL_ANY) >= 0 || Array.IndexOf(into, move.Id) >= 0 || Array.IndexOf(into, KindName(move.Kind)) >= 0);

    private static string KindName(MoveKind kind) => kind switch
    {
        MoveKind.Special => "special",
        MoveKind.Super => "super",
        _ => "normal"
    };
}

/// <summary>
/// The cancel hierarchy of Street Fighter, a normal cancels into a special and a special into a super.
/// </summary>
public enum MoveKind
{
    Normal,
    Special,
    Super
}

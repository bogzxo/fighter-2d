using System;
using System.Numerics;

using Horizon.Rendering.Tiling;

namespace Fighter2D.Fighters.Control;

/// <summary>
/// What the winner of a round does while the loser lies there. They make their way over to the head of whoever they knocked out
/// and play their victory move over it, which for the mannequins is bending down to "pick something up". Taking the piss, basically.
/// The way over is found on the tiles of the map (see <see cref="StageRoute"/>), so a loser who went down on another ledge or
/// with their head against a wall still gets visited. Around, down off an edge or up with a jump, whatever it takes.
/// It only happens after a K.O. (nobody is on the floor after a time out) and only for a character whose move list has a victory move.
/// </summary>
internal sealed class VictoryTaunt(PlayerController controller)
{
    // How long (in ticks) the loser lies there before anybody walks up to them
    private const int WAIT_TICKS = 15;

    // The longest (in ticks) the way over is allowed to take. After that the taunt is done from wherever we got to
    private const int WALK_TICKS = 240;

    // How far past the head of the loser we stand, in world units
    private const float STAND_OFF = 14.0f;

    // How far from the spot we asked for the end of the way may be, sideways in tiles and up or down in world units.
    // Further than that is somewhere else entirely (up on the ledge they are lying against) and no good
    private const float NEAR_TILES = 1.5f;
    private const float LEVEL = 20.0f;

    // How fast the stroll is against the walk speed of the character, nobody runs to a taunt. A jump needs a proper run up though
    private const float STROLL = 0.55f;
    private const float JUMP_PACE = 1.0f;

    private const string JUMP_MOVE = "jump";

    private enum Step
    {
        Waiting,
        Walking,
        Taunting
    }

    private Step _step;
    private int _ticks;

    private readonly StageRoute _route = new(controller);

    /// <summary>
    /// The way the player is walking by themselves right now, -1 for left, 1 for right and 0 while they aren't.
    /// The movement goes by this while nobody has the controls.
    /// </summary>
    public float Steering { get; private set; }

    /// <summary>
    /// How fast they are going against the walk speed of their character.
    /// </summary>
    public float WalkScale { get; private set; } = STROLL;

    public bool IsWalking => _step == Step.Walking && Steering != 0.0f;

    /// <summary>
    /// Whether the player is on their way to a taunt or in the middle of one, which the next round waits for.
    /// A network player is only seen doing it, the move they are shown in is all there is to go by.
    /// </summary>
    public bool IsBusy => controller.CurrentMove.Id == MoveIds.VICTORY || (_step != Step.Taunting && ShouldTaunt());

    /// <summary>
    /// Called between two rounds, nothing of the last one is left.
    /// </summary>
    public void Reset()
    {
        _step = Step.Waiting;
        _ticks = 0;
        _route.Forget();
        Steering = 0.0f;
    }

    /// <summary>
    /// Called on every tick the players are held still, which is when a round has just been called.
    /// </summary>
    public void Update()
    {
        if (_step == Step.Taunting || !ShouldTaunt())
        {
            Steering = 0.0f;
            return;
        }

        _ticks++;

        switch (_step)
        {
            case Step.Waiting:
                // Not before they have finished falling over. Where their head ends up isn't known until they have
                if (!LoserIsDown()) _ticks = 0;
                else if (_ticks >= WAIT_TICKS) BeginWalk();
                break;

            case Step.Walking:
                Walk();
                break;
        }
    }

    /// <summary>
    /// Helper method to test if there is anybody to taunt and we are in a state to do it.
    /// </summary>
    private bool ShouldTaunt()
    {
        // A network player taunts on their own machine, here they are only shown doing it
        if (controller.IsRemote || !controller.MoveList.Moves.ContainsKey(MoveIds.VICTORY)) return false;
        if (Fight.Round is not { Phase: RoundPhase.RoundOver or RoundPhase.MatchOver }) return false;

        return !controller.IsKnockedOut && controller.IsInControl && controller.Opponent.Controller.IsKnockedOut;
    }

    /// <summary>
    /// Helper method to test if the loser is lying still, which is the last phase of their knocked out move.
    /// </summary>
    private bool LoserIsDown()
    {
        PlayerController loser = controller.Opponent.Controller;
        if (!loser.State.IsGrounded || !controller.State.IsGrounded) return false;

        // A character with no way of falling over stays in whatever they were hit in, that will have to do
        return loser.CurrentMove.Id != MoveIds.KNOCKED_OUT || loser.Playback.Phase >= loser.CurrentMove.Phases.Length - 1;
    }

    /// <summary>
    /// Where the head of the loser is, and which way from it is away from the rest of them.
    /// Somebody who fell over backwards has their head where their back was. The box around what is drawn of them is as long as they are.
    /// </summary>
    private (float Head, float Away) FindHead()
    {
        Player loser = controller.Opponent;
        Box body = loser.Boxes.Hurtbox;

        return loser.Flipped ? (body.Max.X, 1.0f) : (body.Min.X, -1.0f);
    }

    private void BeginWalk()
    {
        _step = Step.Walking;
        _ticks = 0;

        FindWay();
    }

    /// <summary>
    /// Helper method to find the way to the loser on the tiles of the map. The spot just past their head if anybody can
    /// stand there, the spot just short of it (on top of them) if not, which is the case when they went down against a wall.
    /// </summary>
    private void FindWay()
    {
        Player player = controller.Player, loser = controller.Opponent;
        var (head, away) = FindHead();

        float floor = loser.FeetPosition.Y;
        float tile = Fight.Stage?.Paths?.Map.TileSize.X ?? 0.0f;

        Span<float> spots = [head + away * STAND_OFF * player.Scale, head - away * STAND_OFF * player.Scale];
        foreach (float spot in spots)
        {
            if (!_route.TryFind(new Vector2(spot, floor), JUMP_PACE)) continue;

            // Somewhere to stand was found, but only somewhere near where we asked. If that is nowhere near the head it is no good
            Vector2 end = _route.End;
            if (MathF.Abs(end.X - spot) <= tile * NEAR_TILES && MathF.Abs(end.Y - floor) <= LEVEL)
            {
                // The last stop is the very spot, not the middle of the tile it is on
                _route.EndAt(spot);
                return;
            }
        }

        // No way anybody could work out (or no map to work it out on). Straight at them it is
        _route.GoStraight(new Vector2(spots[0], floor));
    }

    /// <summary>
    /// Helper method to follow the way to the loser one stop at a time, and start the taunt at the end of it.
    /// </summary>
    private void Walk()
    {
        Player player = controller.Player;
        bool grounded = controller.State.IsGrounded;

        _route.Update();

        // Going straight at them and stuck anyway, this is as close as it gets
        if (_route.Stalled && _route.Direct) _ticks = WALK_TICKS;

        if (_route.Arrived || _ticks >= WALK_TICKS)
        {
            if (grounded) Taunt();
            else Steering = 0.0f;
            return;
        }

        Steering = _route.Steering;
        WalkScale = _route.Next is { Move: TileMapMove.Jump } ? JUMP_PACE : STROLL;
        if (Steering != 0.0f) player.Flipped = Steering < 0.0f;

        if (!grounded) return;

        if (_route.ShouldJump)
        {
            controller.ChangeToMove(JUMP_MOVE, forceRestart: true);
            return;
        }

        // The walk of the character for as long as we are on our way, once whatever got us here (a jump, a landing) is done with
        if (controller.CurrentMove.Id is MoveIds.IDLE or MoveIds.RUN) controller.ChangeToMove(MoveIds.RUN);
    }

    private void Taunt()
    {
        Player player = controller.Player;
        var (head, _) = FindHead();

        // Turn to the head before bending down over it, from whichever side of it we ended up on
        Steering = 0.0f;
        player.Flipped = head < player.Transform.Position.X;

        _step = Step.Taunting;
        controller.ChangeToMove(MoveIds.VICTORY, forceRestart: true);
    }
}

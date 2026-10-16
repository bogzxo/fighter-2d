using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// The way across the stage for a fighter who walks somewhere without a thumb steering them. The dummy going after the player
/// and the winner of a round going over to gloat both use one of these. It finds the way on the tiles of the map (see
/// <see cref="IArena"/>) and then follows it stop by stop, saying which way to walk and when to jump.
/// Finding the way is one thing, actually getting there is another. So it also notices when it has landed somewhere else than
/// it meant to or when it isn't getting anywhere, and works the way out again from wherever it ended up.
/// </summary>
internal sealed class StageRoute(PlayerController controller)
{
    // What of a character there is to get past things, in pixels of its art (it grows with the scale of the character)
    private static readonly Vector2 BODY = new(28.0f, 70.0f);

    // How much of what a jump of the character could clear we count on, up and along. A jump that comes up short is worse
    // than a walk around, so the way is found on the careful side of the numbers (see CharacterMovement)
    private const float JUMP_MARGIN = 0.78f;

    // How close to a stop counts as standing on it, and how far above or below it our feet can be and still count
    private const float ARRIVED = 8.0f;
    private const float LEVEL = 20.0f;

    // How far along the take off tile we jump from, 0 the moment we step on it and 1 at the very edge
    private const float TAKE_OFF_POINT = 0.2f;

    // How close to the landing spot we let go of the direction in the air. The air friction takes what speed is left off
    // over a moment, so letting go a little early is how a jump is aimed
    private const float AIR_AIM = 6.0f;

    // How little we have to move in a tick to count as stuck, and how many ticks of that it takes to look for another way
    private const float STALLED = 0.05f;
    private const int STALL_TICKS = 20;

    private readonly List<RouteStep> _path = [];
    private int _stop;

    private Vector2 _goal;
    private float _pace;

    // Going straight at the goal because no way could be found, nothing clever happens then
    private bool _direct;

    private bool _wasGrounded = true, _jumped;
    private float _lastX = float.NaN;
    private int _stalled;

    private IFighterBody Body => controller.Body;

    /// <summary>
    /// Whether there is a map to find a way on at all.
    /// </summary>
    public bool Available => controller.Fight.Stage is { HasPaths: true };

    /// <summary>
    /// Whether we are going straight at the goal because no way could be found.
    /// </summary>
    public bool Direct => _direct;

    /// <summary>
    /// Whether there is a way with stops left on it.
    /// </summary>
    public bool Found => _stop < _path.Count;

    /// <summary>
    /// Whether we have been to every stop of the way, which is standing at the end of it.
    /// </summary>
    public bool Arrived => _path.Count > 0 && _stop >= _path.Count;

    /// <summary>
    /// The stop we are on our way to, null without one.
    /// </summary>
    public RouteStep? Next => Found ? _path[_stop] : null;

    /// <summary>
    /// Where the way ends, which is as near the goal as anybody can stand.
    /// </summary>
    public Vector2 End => _path.Count > 0 ? _path[^1].Position : _goal;

    /// <summary>
    /// Which way to walk right now, -1 for left, 1 for right and 0 for standing still (or letting go in the air to land on the spot).
    /// </summary>
    public float Steering { get; private set; }

    /// <summary>
    /// Whether this is the tick to jump on. The next stop is one that is jumped to and we are far enough along the take off
    /// tile, or stuck at the foot of whatever it is and jumping is all that is left to try.
    /// </summary>
    public bool ShouldJump { get; private set; }

    /// <summary>
    /// Whether we were stuck for a while on this tick. A way that was found is worked out again by itself, a straight line isn't.
    /// </summary>
    public bool Stalled { get; private set; }

    /// <summary>
    /// Finds the way from where the player stands to a spot of the stage.
    /// </summary>
    /// <param name="to">Where they want to be. They end up on the nearest floor under (or next to) it.</param>
    /// <param name="pace">How fast they walk against the walk speed of their character, somebody slow doesn't jump as far.</param>
    /// <returns>False if there is no way (or no map), nothing is followed then.</returns>
    public bool TryFind(Vector2 to, float pace)
    {
        _goal = to;
        _pace = pace;
        _direct = false;
        Restart();

        if (controller.Fight.Stage is not { HasPaths: true } stage) return false;

        // The numbers of the character say how high a jump goes and for how long, drifting all the way at the pace we walk
        var numbers = controller.Player.Character.Movement;
        float height = numbers.JumpHeight * JUMP_MARGIN;
        float distance = numbers.AirSpeed * pace * numbers.AirTime(numbers.JumpHeight) * JUMP_MARGIN;
        return stage.TryFindPath(Body.FeetPosition, to, BODY * Body.Scale, height, distance, _path);
    }

    /// <summary>
    /// Goes straight at a spot without looking at the map, for when no way could be found. If that gets stuck so be it.
    /// </summary>
    public void GoStraight(Vector2 to)
    {
        _goal = to;
        _direct = true;
        Restart();

        _path.Add(new RouteStep(to, Jump: false));
    }

    /// <summary>
    /// Moves the end of the way to an exact spot, for standing somewhere in a tile rather than in the middle of it.
    /// </summary>
    public void EndAt(float x)
    {
        if (_path.Count == 0) return;

        RouteStep last = _path[^1];
        _path[^1] = last with { Position = new Vector2(x, last.Position.Y) };
    }

    /// <summary>
    /// Forgets the way, for when the player stops caring where they were going.
    /// </summary>
    public void Forget()
    {
        _path.Clear();
        Restart();
        Steering = 0.0f;
    }

    /// <summary>
    /// Called once a tick while the way is being followed. Moves on to the next stop when we get to one, aims the landing
    /// of a jump, and works the way out again when we land somewhere we didn't mean to or stop getting anywhere.
    /// </summary>
    public void Update()
    {
        bool grounded = controller.State.IsGrounded;
        bool landed = grounded && !_wasGrounded;
        if (_wasGrounded && !grounded) _jumped = true;
        _wasGrounded = grounded;

        Stalled = ShouldJump = false;

        if (!Found)
        {
            Steering = 0.0f;
            return;
        }

        // Where we come down after a jump or a drop is where the rest of the way starts from, however it went
        if (landed && !_direct) Replan();

        CheckStall(grounded);

        RouteStep stop = _path[_stop];
        float x = Body.Position.X;
        float distance = stop.Position.X - x;
        bool level = _direct || MathF.Abs(stop.Position.Y - Body.FeetPosition.Y) <= LEVEL;

        if (grounded && level && MathF.Abs(distance) <= ARRIVED)
        {
            _stop++;
            _jumped = false;
            Steering = 0.0f;
            return;
        }

        // In the air we only hold the direction for as long as the landing is still ahead of us
        float reach = grounded ? 1.0f : AIR_AIM;
        Steering = MathF.Abs(distance) > reach ? MathF.Sign(distance) : 0.0f;

        if (!grounded || !stop.Jump || _jumped) return;

        // How far into the take off tile we are, from its edge behind us to the one in front
        float into = AlongTile(x);
        if (distance < 0.0f) into = 1.0f - into;

        ShouldJump = into >= TAKE_OFF_POINT || _stalled >= STALL_TICKS;
    }

    /// <summary>
    /// Helper method to notice when we are stuck against something the tiles didn't know about, or got shoved off the way.
    /// </summary>
    private void CheckStall(bool grounded)
    {
        float x = Body.Position.X;

        if (grounded && MathF.Abs(x - _lastX) < STALLED) _stalled++;
        else _stalled = 0;
        _lastX = x;

        if (_stalled < STALL_TICKS) return;

        Stalled = true;

        // Stuck at the foot of a jump is for the jump to sort out, anything else gets looked at again from here
        if (_direct || _path[_stop].Jump) return;

        _stalled = 0;
        Replan();
    }

    /// <summary>
    /// Helper method to work the way out again from where we are now. If there is no way from here any more it is straight at the goal.
    /// </summary>
    private void Replan()
    {
        if (!TryFind(_goal, _pace)) GoStraight(_goal);
    }

    private void Restart()
    {
        _path.Clear();
        _stop = 0;
        _stalled = 0;
        _lastX = float.NaN;
        _jumped = false;
    }

    /// <summary>
    /// Helper method for how far along its tile a spot of the stage is, from 0 at the left edge of the tile to 1 at its right one.
    /// </summary>
    private float AlongTile(float x)
    {
        if (controller.Fight.Stage is not { HasPaths: true } stage) return 0.5f;

        float tiles = (x - stage.OriginX) / stage.TileWidth;
        return tiles - MathF.Floor(tiles);
    }
}

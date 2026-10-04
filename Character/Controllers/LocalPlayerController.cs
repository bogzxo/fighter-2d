using Fighter2D.Logic;
using Fighter2D.Logic.Moves;
using System.Numerics;
using Egui;
using Egui.Containers;

namespace Fighter2D.Character.Controllers;

/// <summary>
/// Controller for players which are played on this machine, it buffers the buttons of its input and matches them against the move list.
/// Whether those buttons come from a gamepad or the AI makes no difference to it, see <see cref="IPlayerInput"/>.
/// </summary>
internal sealed class LocalPlayerController(IPlayerInput input) : PlayerController
{
    private readonly InputBuffer _input = new(PlayerConfig.INPUT_BUFFER_TICKS, PlayerConfig.DOUBLE_TAP_TICKS);
    private float _tickTimer = 0.0f;

    public override void Initialize()
    {
        base.Initialize();
        input.Attach(this);
    }

    protected override void ReadInputs(float dt)
    {
        // @spd investigate if substepping the input buffer is a good idea
        const float tickTime = 1.0f / PlayerConfig.INPUT_TICK_RATE;

        _tickTimer += dt;
        while (_tickTimer >= tickTime)
        {
            _tickTimer -= tickTime;
            FixedUpdate();
        }
    }

    // @spd this is vestigial
    private void FixedUpdate()
    {
        // save current state of the player input
        _input.Push(input.Read());

        // Face the way we are steering, this happens before any move is matched so any move that has a direction like the roll goes the way it was tapped
        float direction = GetSteeringDirection();
        if (direction != 0)
        {
            Player.Flipped = direction < 0;
        }
    }

    protected override void TryProcessNewInputs(float dt)
    {
        // Find a match as per the order of precedence defined in the movelist
        if (!MoveList.TryMatchInput(_input, StateTracker.CurrentStance, out var move, out var signature)) return;

        // A press only ever starts one move, held inputs (run, crouch, block) keep matching for as long as they are held
        if (move.Trigger != InputTrigger.Held)
        {
            _input.Consume(signature);
        }

        // Check stance reroutes (hitting kick while in the air -> jumpkick)
        if (move.StanceReroutes != null &&
            move.StanceReroutes.TryGetValue(StateTracker.CurrentStance, out MoveId rerouteId))
        {
            ChangeToMove(rerouteId);
            return;
        }

        ChangeToMove(move.Id);
    }

    public override bool IsHeld(InputFlags buttons) => _input.IsHeld(buttons);

    public override void PrepareForHit() => input.OnOpponentAttack();

    /// <summary>
    /// Helper method to get the way the player is trying to walk, 0 if they aren't or the current move doesn't allow it.
    /// </summary>
    private float GetSteeringDirection()
    {
        if (!IsInControl || !CurrentMove.AllowsSteering) return 0;

        return (_input.IsHeld(InputFlags.DPadRight) ? 1 : 0) - (_input.IsHeld(InputFlags.DPadLeft) ? 1 : 0);
    }

    public override void UpdatePhysics(float dt)
    {
        // rudementary temporary move logic
        float direction = GetSteeringDirection();
        if (direction == 0) return;

        var targetVelocity = direction * PlayerConfig.WALK_SPEED * input.WalkSpeedScale;
        var currentVelocityX = Player.PhysicsBody.Velocity.X;
        var velocityDiff = targetVelocity - currentVelocityX;

        Player.PhysicsBody.ApplyForce(new Vector2(velocityDiff * Player.PhysicsBody.Mass * 5f, 0));
    }

    // Here we can use EGUI to show debug info
    public override void RenderUi(Ui root)
    {
        new Window(input.Name)
            .Show(root.Ctx, ui =>
            {
                ui.Heading("Player Information");
                ui.Label($"Current Move: {CurrentMove.Id}");
                ui.Label($"Current Stance: {StateTracker.CurrentStance}");
                ui.Label($"Current Status: {StateTracker.CurrentStatus}");
                ui.Label($"Is Grounded: {StateTracker.IsGrounded}");
                ui.Label($"CanInterrupt: {CanInterrupt}");
                ui.Label($"Held Inputs: {_input.Held}");
            });
    }
}

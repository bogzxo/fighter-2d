using System;
using System.Linq;
using System.Numerics;

using Horizon.Logging;

using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Fighters;

/// <summary>
/// A fighter in the arena. This is the sprite, the physics body and the health bar's worth of health.
/// What it actually does is up to its <see cref="PlayerController"/>.
/// </summary>
internal class Player() : Sprite(SIZE), IFighter, IFighterBody
{
    public const byte MAX_HEALTH = CombatRules.MAX_HEALTH;

    // The tag of the fixture under the feet, the state tracker asks it whether we are on the ground
    public const string FEET_TAG = "feet";

    // The collision group every fighter is in. They don't run into each other (or stand on each other), see Pushboxes for what keeps them apart
    private const int FIGHTER_GROUP = 1;

    // The size a character is drawn at before its own scale
    private static readonly Vector2 SIZE = new(128);

    // The animation a character shows before its controller has said anything
    private const string IDLE_ANIMATION = "idle";

    public PlayerController Controller { get; init; } = null!;

    /// <summary>
    /// The fight we are in, which the scene gives us before we are added to it. Everything that was a static Fight
    /// is in here, the opponent, the effects, the stage, the rounds, see <see cref="FightContext"/>.
    /// </summary>
    public FightContext Fight { get; internal set; } = null!;
    public Vector2 SpawnPosition { get; set; }

    /// <summary>
    /// Which way the player faces at the start of every round, -1 left and 1 right, null for however the art was drawn.
    /// Set by a fight that puts its players in exact places for a test (see MatchSetup.TestPositions).
    /// </summary>
    public float? StartFacing { get; set; }

    /// <summary>
    /// This is the player health out of 100, 0 being dead.
    /// </summary>
    public byte Health { get; set; } = MAX_HEALTH;

    public const byte MAX_METER = CombatRules.MAX_METER;

    /// <summary>
    /// The super meter out of 100. It builds off hits dealt and taken (see CombatRules) and buys the moves that cost it (meter_cost).
    /// It carries over from one round to the next and starts the match empty.
    /// </summary>
    public byte Meter { get; set; }

    /// <summary>
    /// Helper method to add to the meter, up to full.
    /// </summary>
    public void GainMeter(int amount)
    {
        if (amount <= 0) return;

        Meter = (byte)Math.Min(MAX_METER, Meter + amount);
    }

    /// <summary>
    /// Helper method to pay for a move. False (and nothing paid) if there isn't enough in the tank.
    /// </summary>
    public bool SpendMeter(int amount)
    {
        if (amount > Meter) return false;

        Meter = (byte)(Meter - amount);
        return true;
    }

    /// <summary>
    /// What kind of fighter this is, read from the content once the fight is set up.
    /// </summary>
    public CharacterDefinition Character { get; private set; } = null!;

    /// <summary>
    /// Which character of the content this player is, null for the first one there is.
    /// </summary>
    public string? CharacterId { get; init; }

    public MoveList MoveList { get; private set; } = null!;
    public PhysicsBodyComponent2D PhysicsBody { get; internal set; } = null!;

    /// <summary>
    /// What of the player stands on the map and bumps into it, shaped like whatever frame they are showing. See <see cref="PlayerBody"/>.
    /// </summary>
    public PlayerBody Body { get; private set; } = null!;

    /// <summary>
    /// The hurtbox and hitbox of the player, see <see cref="PlayerBoxes"/>.
    /// </summary>
    public PlayerBoxes Boxes { get; private set; } = null!;

    /// <summary>
    /// How big the player is against the art it was drawn at, see <see cref="CharacterDefinition.Scale"/>.
    /// </summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>
    /// Where the player touches the ground, this is where dust comes from.
    /// </summary>
    public Vector2 FeetPosition => Transform.Position + Vector2.UnitY * (-64 * Scale);

    /// <summary>
    /// Where the head of the player is, this is where the haze of a hitstun hangs.
    /// </summary>
    public Vector2 HeadPosition => Transform.Position + Vector2.UnitY * (2 * Scale);

    /// <summary>
    /// The way the player is facing, -1 for left and 1 for right.
    /// Everything that has a direction (the push of a move, the hitbox, which side a block covers) goes by this.
    /// </summary>
    public float Facing => Flipped ? -1.0f : 1.0f;

    /// <summary>
    /// Whether the other player is in front of us rather than behind our back. Somebody standing right on top of us is both.
    /// </summary>
    public bool IsFacing(IFighterBody other)
    {
        float toOther = other.Position.X - Transform.Position.X;
        return toOther == 0.0f || (toOther < 0.0f) == Flipped;
    }

    /// <summary>
    /// Called between two rounds. Puts the player back where they started with full health and nothing of the last round left on them.
    /// </summary>
    public void ResetForRound()
    {
        Health = MAX_HEALTH;
        Tint = Vector4.One;

        if (PhysicsBody is not null)
        {
            PhysicsBody.Position = SpawnPosition;
            PhysicsBody.SetVelocity(Vector2.Zero);
            Transform.Position = SpawnPosition;

            // Put there, not walked there: no frame shows them on their way across the stage
            Transform.Snap();
        }

        if (StartFacing is { } facing) Flipped = facing < 0.0f;

        Controller?.Reset();
    }

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        // The controller used to be a component and ran in the base update, so it still goes before the boxes follow the frame
        Controller?.Update(dt);
        if (Controller is not null && Atlas is not null) Boxes?.Sync();
    }

    public override void UpdatePhysics(float dt)
    {
        base.UpdatePhysics(dt);
        Controller?.UpdatePhysics(dt);
    }

    /* The fighter as the simulation sees it, see IFighter and IFighterBody. The body is the sprite and the physics body in one */

    IFighterBody IFighter.Body => this;

    public Vector2 Position => Transform.Position;
    public Vector2 Velocity => PhysicsBody.Velocity;
    public void SetVelocity(Vector2 velocity) => PhysicsBody.SetVelocity(velocity);
    public void ApplyImpulse(Vector2 impulse) => PhysicsBody.ApplyImpulse(impulse);
    public void Nudge(Vector2 step) => PhysicsBody.Position += step;

    public void Teleport(Vector2 position, Vector2 velocity)
    {
        PhysicsBody.Position = position;
        PhysicsBody.SetVelocity(velocity);
        Transform.Snap();
    }

    public float GravityScale
    {
        get => PhysicsBody.GravityScale;
        set => PhysicsBody.GravityScale = value;
    }

    public float MaxFallSpeed
    {
        get => PhysicsBody.MaxFallSpeed;
        set => PhysicsBody.MaxFallSpeed = value;
    }

    public float WorldGravity => Parent?.GetComponent<PhysicsWorld>()?.Gravity.Length() ?? 0.0f;

    // Looked up once rather than on every update
    private IPhysicsFixture? _feetFixture;

    public bool IsGrounded
    {
        get
        {
            if (PhysicsBody is null) return false;

            _feetFixture ??= PhysicsBody.KinematicFixtures.Find(f => f.Tag == FEET_TAG);
            return _feetFixture?.IsTouching ?? false;
        }
    }

    public void PlayAnimation(string animation) => SetAnimation(animation);
    public string? ShownAnimation => FrameName;
    public void ShowFrame(int frame) => Frame = frame;
    public int FrameCount(string animation) => GetFrameCount(animation);
    public void TintTo(Vector4 tint, float time) => this.TweenTint(tint, time);
    void IFighterBody.Flash(Vector4 colour, float time, float amount) => this.Flash(colour, time, amount);

    public Box Hurtbox => Boxes.Hurtbox;
    public Box Hitbox(uint frame) => Boxes.Hitbox(frame);

    public bool HitboxTouches(uint frame, IFighterBody other) => other is Player them
        ? Boxes.HitboxFixture.TestIntersection(them.Boxes.HurtboxFixture, Transform.Position, them.Transform.Position)
        : Hitbox(frame).Overlaps(other.Hurtbox, out _);

    public override void Initialize()
    {
        base.Initialize();

        // A fighter throws a shadow as sharp as their pixels and bounces the light of the arena, see DeferredRenderer2D.SpriteShadows
        CastsShadows = true;

        // Everything a character is comes out of the content, which online can be the content of whoever hosts the fight
        Character = CharacterDefinition.Load(CharacterId);
        MoveList = Character.LoadMoves();

        // The hitboxes drawn by hand in the hitbox editor, if the character has any, in before the frame data is worked out
        CustomHitboxes.Apply(MoveList, Character.Id);

        CharacterBoxes? boxes = LoadSprites();
        CreateBody();

        Boxes = new PlayerBoxes(this, boxes);

        // The controller is no component, the player ticks it (see UpdateState), so it comes to life here by hand
        Controller.Attach(this);
    }

    /// <summary>
    /// Helper method to load the art of the character and everything that depends on its animations.
    /// </summary>
    private CharacterBoxes? LoadSprites()
    {
        // Some characters are bigger than others, and everything about them is
        Scale = Character.Scale;
        Transform.Size = SIZE * Scale;
        StencilTransform.Size = SIZE * Scale;

        // The controller decides which frame is shown, the sprite doesn't play anything by itself
        Animated = false;

        CharacterArt? art = CharacterArt.Load(Character);
        if (art is null)
        {
            Log.Error("Failed to load player sprite!");
            MoveList.Bake(_ => 1, Character.FrameRate);
            return null;
        }

        // Every animation a move can play is in the atlas before the fight starts, nothing turns up late
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        art.Prepare(MoveList.Animations);
        // Packed now, or waited for if a worker is at it (the fight setup got the character ready ahead), so the
        // first frame of the fight has every frame of the character
        art.Atlas.Pack();
        double preparedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Whatever the character has to stand around in, the controller picks the real one on its first tick
        string first = art.Has(IDLE_ANIMATION) ? IDLE_ANIMATION : MoveList.Animations.FirstOrDefault(art.Has) ?? IDLE_ANIMATION;
        ConfigureAtlas(art.Atlas, art.Sprites, first);

        // Art that isn't drawn at its own size would come out with uneven pixels otherwise
        Smooth = art.FrameSize != SIZE * Scale;

        // The frame data of a move depends on how long the animations it plays are
        MoveList.Bake(animation => (uint)Math.Max(1, art.FrameCount(animation)), Character.FrameRate);
        Log.Info($"[Player] '{Character.Id}' ready in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms, {preparedMs:0} of them putting the frames in the atlas and tracing their boxes.");

        return art.Boxes;
    }

    /// <summary>
    /// Helper method to create the body that stands on the map. What it is shaped like follows the sprite frame by frame (see
    /// <see cref="PlayerBody"/>), and so do the boxes that get hit, which are not part of it.
    /// </summary>
    private void CreateBody()
    {
        var world = Parent.GetComponent<PhysicsWorld>();
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic, SpawnPosition));
        Body = new PlayerBody(this, world);

        PhysicsBody.CollisionGroup = FIGHTER_GROUP;

        // No drag, how a fighter speeds up, slows down and falls is all PlayerMovement, out of the numbers of their character
        PhysicsBody.LinearDrag = 0.0f;
        PhysicsBody.Mass = 1.0f;

        // A fighter lands, they don't bounce. The drag used to hide that it was 0.3, coming down at the fall speed it was a hop
        PhysicsBody.Restitution = 0.0f;
    }
}

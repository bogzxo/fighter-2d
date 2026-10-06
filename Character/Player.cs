using System.Numerics;
using System.Runtime.CompilerServices;
using Bogz.Logging.Loggers;

using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;


using Fighter2D.Character.Controllers;
using Fighter2D.Logic;

using Horizon.Core.Components.Physics2D;
using Horizon.GameEntity.Components.Physics2D;
using Horizon.Physics;
using Horizon.Physics.Fixtures;
using Horizon.Rendering.Particles;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Character;

internal class Player() : Sprite(SIZE)
{
    private static readonly Vector2 SIZE = new(128);
    internal PlayerController Controller { get; init; }
    internal ParticleRenderer2D Particles { get; private set; }
    public Vector2 SpawnPosition { get; set; }
    /// <summary>
    /// This is the player health out of 100, 0 being dead.
    /// </summary>
    public byte Health = 100;
    /// <summary>
    /// What kind of fighter this is and the moves it has, read from the content of the game once the fight is set up.
    /// </summary>
    public CharacterDefinition Character { get; private set; } = null!;

    /// <summary>
    /// Which character of the content this player is, null for the first one there is.
    /// </summary>
    public string? CharacterId { get; init; }
    public MoveList MoveList { get; private set; } = null!;

    private PhysicsWorld world;
    public PhysicsBodyComponent2D PhysicsBody { get; internal set; }

    // How far a blow reaches past what is drawn of it, a hit that only just misses the eye is still a hit
    private const float STRIKE_REACH = 4f;

    // Where a character without boxes can be hit (one whose sheet couldn't be read), as a share of its sprite
    private static readonly Box FallbackBox = new(new Vector2(-0.2f, -0.5f), new Vector2(0.2f, 0.05f));

    // How much bigger than what is drawn of them the box a player is hit in is, all round
    private const float HURT_PADDING = 3f;

    // Where the character can be hit and what it hits with, frame by frame
    private CharacterBoxes? _boxes;

    // What was last handed to the physics, which only needs telling when it changes
    private (string? Animation, uint Frame, bool Flipped) _shown;
    private Vector2[] _outline = [];

    /// <summary>
    /// The box the player can be hit in right now, for the fight: it follows the frame they are showing, a little
    /// bigger than what is drawn of them. Blows are tested against this, see <see cref="StrikeFixture"/>.
    /// </summary>
    public RectanglePhysicsFixture HurtboxFixture { get; private set; } = null!;

    /// <summary>
    /// The box of what the player last hit with, there for the frame the blow is thrown on.
    /// </summary>
    public RectanglePhysicsFixture StrikeFixture { get; private set; } = null!;

    /// <summary>
    /// What is drawn of the player right now, to the pixel. Nothing of the fight goes by it: it is what the rain
    /// runs down and what blood and dust bounce off.
    /// </summary>
    public OutlinePhysicsFixture OutlineFixture { get; private set; } = null!;

    /// <summary>
    /// How big the player is against the art it was drawn at, see <see cref="CharacterDefinition.Scale"/>.
    /// </summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>
    /// Where the player touches the ground, this is where dust comes from.
    /// </summary>
    public Vector2 FeetPosition => Transform.Position + Vector2.UnitY * (-64 * Scale);

    /// <summary>
    /// Where the head of the player is, this is where the haze of a stun hangs.
    /// </summary>
    public Vector2 HeadPosition => Transform.Position + Vector2.UnitY * (2 * Scale);

    /// <summary>
    /// The way the player is facing, -1 for left and 1 for right. Everything that has a direction (the push of a move,
    /// what a blow reaches, which side a guard covers) goes by this.
    /// </summary>
    public float Facing => Flipped ? -1.0f : 1.0f;

    /// <summary>
    /// Whether the other player is in front of us rather than behind our back. Somebody right on top of us is both.
    /// </summary>
    public bool IsFacing(Player other)
    {
        float toOther = other.Transform.Position.X - Transform.Position.X;
        return toOther == 0.0f || (toOther < 0.0f) == Flipped;
    }

    /// <summary>
    /// Where the player can be hit right now: the box around what is drawn of them on the frame they are showing.
    /// </summary>
    public Box HurtBox
    {
        get
        {
            string animation = Controller.ActiveAnimation;
            uint frame = AnimationManager.Animations.TryGetValue(animation, out var playing) ? playing.Index : 0;

            return ToWorld(_boxes is not null && _boxes.TryGet(animation, frame, out var boxes) ? boxes.Hurt : FallbackBox, 0);
        }
    }

    /// <summary>
    /// What the player hits with on a frame of the animation they are playing: the limb that is thrown on it.
    /// </summary>
    public Box StrikeBox(uint frame)
    {
        Box strike = ToWorld(_boxes is not null && _boxes.TryGet(Controller.ActiveAnimation, frame, out var boxes) ? boxes.Strike : FallbackBox, STRIKE_REACH);

        // Where the physics (and whoever is looking at its overlay) can see it
        StrikeFixture.Bounds = new PhysicsRectangle(strike.Min - Transform.Position, strike.Max - strike.Min);
        return strike;
    }

    /// <summary>
    /// Called between two rounds, puts the player back where they started: on their feet, with all of their health and nothing of the last round left on them.
    /// </summary>
    public void ResetForRound()
    {
        Health = 100;
        Tint = Vector4.One;

        if (PhysicsBody is not null)
        {
            PhysicsBody.Position = SpawnPosition;
            PhysicsBody.SetVelocity(Vector2.Zero);
            Transform.Position = SpawnPosition;
        }

        Controller?.Reset();
    }

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);
        if (Controller is null || AnimationManager is null || OutlineFixture is null) return;

        string animation = Controller.ActiveAnimation;
        uint frame = AnimationManager.Animations.TryGetValue(animation, out var playing) ? playing.Index : 0;

        // Only when what is drawn of us has changed
        if (_shown == (animation, frame, Flipped)) return;
        _shown = (animation, frame, Flipped);

        Box hurt = HurtBox;
        HurtboxFixture.Bounds = new PhysicsRectangle(
            hurt.Min - Transform.Position - new Vector2(HURT_PADDING),
            hurt.Max - hurt.Min + new Vector2(HURT_PADDING * 2));

        // A blow is over with the frame it was thrown on
        StrikeFixture.Bounds = new PhysicsRectangle(Vector2.Zero, Vector2.Zero);

        // The silhouette of the frame, at the size and the way round we are drawn
        Vector2[] pieces = _boxes is not null && _boxes.TryGet(animation, frame, out var boxes) ? boxes.Outline : [];
        if (_outline.Length < pieces.Length) _outline = new Vector2[pieces.Length];

        Vector2 size = Vector2.Abs(Transform.Size) * new Vector2(Flipped ? -1 : 1, 1);
        for (int i = 0; i < pieces.Length; i++) _outline[i] = pieces[i] * size;

        OutlineFixture.Set(_outline.AsSpan(0, pieces.Length));
    }

    /// <summary>
    /// Helper method to put a box of the sprite (a share of it, facing right) where it is in the world, the way the player is facing.
    /// </summary>
    private Box ToWorld(Box share, float grow)
    {
        Vector2 size = Vector2.Abs(Transform.Size);
        float minX = share.Min.X, maxX = share.Max.X;
        if (Flipped) (minX, maxX) = (-maxX, -minX);

        return new Box(
            Transform.Position + new Vector2(minX, share.Min.Y) * size - new Vector2(grow),
            Transform.Position + new Vector2(maxX, share.Max.Y) * size + new Vector2(grow));
    }

    public override void Initialize()
    {
        base.Initialize();

        
        // Everything a character is comes out of the content, which can be the one of a game pack or of whoever hosts the fight
        Character = CharacterDefinition.Load(CharacterId);
        MoveList = Character.LoadMoves();

        var (success, sheet, manager) = SpriteSheet.LoadSpriteSheetFromDirectory(Fighter2D.Content.GameContent.PathOf(Character.SpriteDirectory));

        if (!success)
        {
            ConcurrentLogger.Instance.Log(Bogz.Logging.LogLevel.Error, "Failed to load player sprite!");
        }

        this.Spritesheet = sheet;
        this.AnimationManager = manager;

        // Some characters are bigger than others, and everything about them is
        Scale = Character.Scale;
        Transform.Size = SIZE * Scale;
        StencilTransform.Size = SIZE * Scale;

        // A character whose sprites are another size than we draw them at would come out with uneven pixels otherwise
        Smooth = success && sheet.SpriteSize != SIZE * Scale;

        if (success) _boxes = CharacterBoxes.Load(Fighter2D.Content.GameContent.PathOf(Character.SpriteDirectory), manager);

        AnimationManager.AnimateFrames = false;

        // How long a move takes (and with that how long it stuns for) depends on the animations it plays
        MoveList.Bake(
            animation => animation is not null && manager.Animations.TryGetValue(animation, out var found) ? found.Length : 1,
            Character.FrameRate);

        world = Parent.GetComponent<PhysicsWorld>();
        // Create player physics body and feet fixture
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic, SpawnPosition));

        // The body that stands on the map. What can be hit is not part of it, that follows the sprite (see HurtBox)
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * (-48 * Scale), 16.0f * Scale);
        PhysicsBody.CreateCircleFixture(new Vector2(0, -32 * Scale), 16.0f * Scale);
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * (-64 * Scale), 12.0f * Scale, true, "feet");

        // What a fight goes by: two boxes that follow the animation, see UpdateState
        HurtboxFixture = PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.Zero, true, "hurtbox");
        StrikeFixture = PhysicsBody.CreateRectangularFixture(Vector2.Zero, Vector2.Zero, true, "strike");

        // And what particles go by: the silhouette itself
        OutlineFixture = PhysicsBody.CreateOutlineFixture("outline");

        PhysicsBody.LinearDrag = 16.0f;
        PhysicsBody.Mass = 1.0f;
        PhysicsBody.Restitution = 0.3f;

        SetAnimation("idle");

        AddComponent(Controller);

        Particles = AddEntity(new ParticleRenderer2D(32768) { EndColor = new Vector3(0, 0, 0.6f) });
    }
}
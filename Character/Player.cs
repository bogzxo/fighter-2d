using System.Numerics;

using Bogz.Logging;

using Fighter2D.Character.Controllers;
using Fighter2D.Content;
using Fighter2D.Logic;

using Horizon.Physics;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Character;

/// <summary>
/// A fighter in the arena. This is the sprite, the physics body and the health bar's worth of health.
/// What it actually does is up to its <see cref="PlayerController"/>.
/// </summary>
internal class Player() : Sprite(SIZE)
{
    public const byte MAX_HEALTH = 100;

    // The tag of the fixture under the feet, the state tracker asks it whether we are on the ground
    public const string FEET_TAG = "feet";

    // The collision group every fighter is in. They don't run into each other (or stand on each other), see Pushboxes for what keeps them apart
    private const int FIGHTER_GROUP = 1;

    // The size a character is drawn at before its own scale
    private static readonly Vector2 SIZE = new(128);

    internal PlayerController Controller { get; init; } = null!;
    public Vector2 SpawnPosition { get; set; }

    /// <summary>
    /// This is the player health out of 100, 0 being dead.
    /// </summary>
    public byte Health = MAX_HEALTH;

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
    public bool IsFacing(Player other)
    {
        float toOther = other.Transform.Position.X - Transform.Position.X;
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
        }

        Controller?.Reset();
    }

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        if (Controller is not null && AnimationManager is not null) Boxes?.Sync();
    }

    public override void Initialize()
    {
        base.Initialize();

        // Everything a character is comes out of the content, which online can be the content of whoever hosts the fight
        Character = CharacterDefinition.Load(CharacterId);
        MoveList = Character.LoadMoves();

        CharacterBoxes? boxes = LoadSprites();
        CreateBody();

        Boxes = new PlayerBoxes(this, boxes);

        SetAnimation("idle");
        AddComponent(Controller);
    }

    /// <summary>
    /// Helper method to load the sprite sheet of the character and everything that depends on its animations.
    /// </summary>
    private CharacterBoxes? LoadSprites()
    {
        string directory = GameContent.PathOf(Character.SpriteDirectory);
        var (success, sheet, manager) = SpriteSheet.LoadSpriteSheetFromDirectory(directory);

        if (!success)
        {
            Log.Error("Failed to load player sprite!");
        }

        Spritesheet = sheet;
        AnimationManager = manager;

        // The controller decides which frame is shown, not the animation manager
        AnimationManager.AnimateFrames = false;

        // Some characters are bigger than others, and everything about them is
        Scale = Character.Scale;
        Transform.Size = SIZE * Scale;
        StencilTransform.Size = SIZE * Scale;

        // A sheet that isn't drawn at its own size would come out with uneven pixels otherwise
        Smooth = success && sheet.SpriteSize != SIZE * Scale;

        // The frame data of a move depends on how long the animations it plays are
        MoveList.Bake(
            animation => animation is not null && manager.Animations.TryGetValue(animation, out var found) ? found.Length : 1,
            Character.FrameRate);

        return success ? CharacterBoxes.Load(directory, manager) : null;
    }

    /// <summary>
    /// Helper method to create the body that stands on the map. The boxes that get hit are not part of it, those follow the sprite.
    /// </summary>
    private void CreateBody()
    {
        var world = Parent.GetComponent<PhysicsWorld>();
        PhysicsBody = AddComponent(world.CreateBody(PhysicsBodySimulationType.Dynamic, SpawnPosition));

        PhysicsBody.CreateCircleFixture(Vector2.UnitY * (-48 * Scale), 16.0f * Scale);
        PhysicsBody.CreateCircleFixture(new Vector2(0, -32 * Scale), 16.0f * Scale);
        PhysicsBody.CreateCircleFixture(Vector2.UnitY * (-64 * Scale), 12.0f * Scale, true, FEET_TAG);

        PhysicsBody.CollisionGroup = FIGHTER_GROUP;
        PhysicsBody.LinearDrag = 16.0f;
        PhysicsBody.Mass = 1.0f;
        PhysicsBody.Restitution = 0.3f;
    }
}

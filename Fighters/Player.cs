using System;
using System.Linq;
using System.Numerics;

using Horizon.Logging;

using Horizon.Physics;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Fighters;

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

    // The animation a character shows before its controller has said anything
    private const string IDLE_ANIMATION = "idle";

    internal PlayerController Controller { get; init; } = null!;
    public Vector2 SpawnPosition { get; set; }

    /// <summary>
    /// This is the player health out of 100, 0 being dead.
    /// </summary>
    public byte Health = MAX_HEALTH;

    public const byte MAX_METER = 100;

    /// <summary>
    /// The super meter out of 100. It builds off hits dealt and taken (see CombatRules) and buys the moves that cost it (meter_cost).
    /// It carries over from one round to the next and starts the match empty.
    /// </summary>
    public byte Meter;

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

            // Put there, not walked there: no frame shows them on their way across the stage
            Transform.Snap();
        }

        Controller?.Reset();
    }

    public override void UpdateState(float dt)
    {
        base.UpdateState(dt);

        if (Controller is not null && Atlas is not null) Boxes?.Sync();
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

        AddComponent(Controller);
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
        art.Prepare(MoveList.Animations);

        // Whatever the character has to stand around in, the controller picks the real one on its first tick
        string first = art.Has(IDLE_ANIMATION) ? IDLE_ANIMATION : MoveList.Animations.FirstOrDefault(art.Has) ?? IDLE_ANIMATION;
        ConfigureAtlas(art.Atlas, art.Sprites, first);

        // Art that isn't drawn at its own size would come out with uneven pixels otherwise
        Smooth = art.FrameSize != SIZE * Scale;

        // The frame data of a move depends on how long the animations it plays are
        MoveList.Bake(animation => (uint)Math.Max(1, art.FrameCount(animation)), Character.FrameRate);

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
        PhysicsBody.LinearDrag = 16.0f;
        PhysicsBody.Mass = 1.0f;
        PhysicsBody.Restitution = 0.3f;
    }
}

using System.Numerics;

using Fighter2D.Content;
using Fighter2D.Data;
using Fighter2D.Fighters;

using Horizon.Rendering.Spriting;

namespace Fighter2D.Tests.Support;

/// <summary>
/// Nothing flies around in a test.
/// </summary>
internal sealed class NoEffects : IFightEffects
{
    public void Shockwave(Vector2 position, float radius, float strength) { }
    public void Hit(Vector2 position, float direction, bool heavy) { }
    public void Block(Vector2 position, float direction) { }
    public void Dust(Vector2 position, float direction, int count = 6) { }
    public void Land(Vector2 position, float fallDuration) { }
    public void Haze(Vector2 position, float frameRate) { }
}

/// <summary>
/// Two fake fighters and the fight between them, out of the game's own content (characters.hor, the move files, the
/// hitboxes and the frame counts of the art), ticked by hand. Player one is on the left facing right, player two on
/// the right facing left, the distance between them is the test's to pick.
/// </summary>
internal sealed class TestFight
{
    public const float TICK = FightTicks.TICK_TIME;

    public FakeFighter One { get; }
    public FakeFighter Two { get; }
    public ScriptedInput OneInput { get; } = new();
    public ScriptedInput TwoInput { get; } = new();
    public FightContext Fight { get; } = new() { Effects = new NoEffects() };

    /// <summary>The moves player one has started, by the tick they started on, newest last.</summary>
    public List<(int Tick, string Move)> Started { get; } = [];

    private string _lastMove = string.Empty;
    private int _lastPhase = -1;
    private uint _lastFrame;

    public TestFight(string character = "blocky", string opponent = "blocky", float apart = 40.0f)
    {
        UseGameContent();

        One = Make(character, 0.0f, facesLeft: false, OneInput);
        Two = Make(opponent, apart, facesLeft: true, TwoInput);
        Fight.PlayerOne = One;
        Fight.PlayerTwo = Two;

        One.Controller.Attach(One);
        Two.Controller.Attach(Two);
    }

    private FakeFighter Make(string id, float x, bool facesLeft, IPlayerInput input)
    {
        CharacterDefinition character = CharacterDefinition.Load(id);
        MoveList moves = character.LoadMoves();
        CustomHitboxes.Apply(moves, character.Id);

        // The frames of the real art, so a move is as long here as it is in the game
        SpriteSheetDefinition sprites = SpriteSheetDefinition.Open(GameContent.PathOf(character.Sprites));
        int Frames(string animation) => sprites.FrameCount(animation);
        moves.Bake(animation => (uint)Math.Max(1, animation is null ? 1 : Frames(animation)), character.FrameRate);

        var fighter = new FakeFighter(character, moves, Frames, x, facesLeft) { Fight = Fight };
        fighter.Controller = new PlayerController(input);
        return fighter;
    }

    /// <summary>
    /// Plays ticks of the fight, both players, and remembers every move player one starts.
    /// </summary>
    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            One.Controller.Update(TICK);
            Two.Controller.Update(TICK);
            One.Controller.UpdatePhysics(TICK);
            Two.Controller.UpdatePhysics(TICK);
            One.Integrate(TICK);
            Two.Integrate(TICK);

            // A move that starts, or starts over, is on its first frame of its first phase
            var playback = One.Controller.Playback;
            string move = One.Controller.CurrentMove.Id;
            bool begun = playback.Phase == 0 && playback.Frame == 1 && !(_lastPhase == 0 && _lastFrame == 1);
            if (move != _lastMove || begun)
            {
                Started.Add((OneInput.Tick, move));
                _lastMove = move;
            }
            _lastPhase = playback.Phase;
            _lastFrame = playback.Frame;
        }
    }

    /// <summary>The tick player one first started a move on, -1 if they never did.</summary>
    public int StartOf(string move)
    {
        foreach (var (tick, id) in Started)
        {
            if (id == move) return tick;
        }
        return -1;
    }

    /// <summary>
    /// Points the content at the repo's Assets, found by walking up from the test binaries to Fighter2D.csproj.
    /// </summary>
    internal static void UseGameContent()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "Fighter2D.csproj")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        if (folder is null) throw new InvalidOperationException("Couldn't find Fighter2D.csproj above the test binaries, the tests want the game's Assets.");
        GameContent.UseHome(folder);
    }
}

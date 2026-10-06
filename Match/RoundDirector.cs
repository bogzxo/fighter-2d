using System;

using Fighter2D.Character;
using Fighter2D.Networking;

using Horizon.Core;
using Horizon.Core.Components;

namespace Fighter2D.Match;

internal enum RoundPhase
{
    // The two fighters are shown off against each other, once before the first round
    Versus,

    // The round is called out, nobody can move yet
    Ready,

    // The round is on
    Fight,

    // Somebody went down or the time ran out, the round is being called
    RoundOver,

    // The match is decided, whoever won is shown until the players have seen enough
    MatchOver
}

/// <summary>
/// Runs a match round by round: when the players may move, when a round is over and who took it, and when the match is.
/// It decides and keeps the score, showing any of it is up to whoever looks at it (the HUD), and what happens after the match is up to <see cref="Finished"/>.
/// In an online fight only the host decides (see <see cref="IsAuthority"/>), the other machine is told how the match stands and goes by that.
/// </summary>
internal sealed class RoundDirector(MatchRules rules, Player playerOne, Player playerTwo) : IGameComponent
{
    // How long (in seconds) each of the phases lasts that end by themselves
    private const float VERSUS_TIME = 2.8f;
    private const float READY_TIME = 1.5f;
    private const float ROUND_OVER_TIME = 3.0f;

    // How long the result of the match is up at the most, and how soon a button can skip the rest of it
    private const float MATCH_OVER_TIME = 8.0f;
    private const float MATCH_OVER_SKIP = 1.5f;

    public MatchRules Rules { get; } = rules;
    public MatchScore Score { get; } = new(rules);

    /// <summary>
    /// Whether this machine decides how the match goes. The one that doesn't only keeps its clocks running between
    /// what it is told by the one that does, see <see cref="Apply"/>.
    /// </summary>
    public bool IsAuthority { get; init; } = true;

    public RoundPhase Phase { get; private set; } = RoundPhase.Versus;

    /// <summary>
    /// How long the phase has been going, in seconds.
    /// </summary>
    public float PhaseTime { get; private set; }

    /// <summary>
    /// How much of the round is left in seconds, which only counts down while the fight is on. Stays where it is for a round without a time limit.
    /// </summary>
    public float TimeLeft { get; private set; } = rules.RoundSeconds;

    /// <summary>
    /// How the round that was just called ended, null before the first one is.
    /// </summary>
    public RoundOutcome? LastOutcome { get; private set; }

    /// <summary>
    /// Whether the round that was just called ended on the clock rather than with somebody going down.
    /// </summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Whether the players are held where they are: before a round, and once it has been called.
    /// </summary>
    public bool PlayersFrozen => Phase != RoundPhase.Fight;

    /// <summary>
    /// Called whenever the match moves on to another phase, with the phase it is in from then on.
    /// </summary>
    public event Action<RoundPhase>? PhaseChanged;

    /// <summary>
    /// Called once when the match is over and its result has been shown, whoever set the fight up decides where to go from there.
    /// </summary>
    public Action? Finished { get; set; }

    /// <summary>
    /// Asked on every update while the result is up, true ends it early. For whoever knows what the players are pressing.
    /// </summary>
    public Func<bool>? SkipRequested { get; set; }

    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "Round Director";
    public Entity Parent { get; set; } = null!;

    private bool _finished;

    public Player PlayerOf(int index) => index == 0 ? playerOne : playerTwo;

    public void Initialize() { }

    public void UpdateState(float dt)
    {
        PhaseTime += dt;

        if (!IsAuthority)
        {
            Follow(dt);
            return;
        }

        switch (Phase)
        {
            case RoundPhase.Versus:
                if (PhaseTime >= VERSUS_TIME) Enter(RoundPhase.Ready);
                break;

            case RoundPhase.Ready:
                if (PhaseTime >= READY_TIME) Enter(RoundPhase.Fight);
                break;

            case RoundPhase.Fight:
                UpdateFight(dt);
                break;

            case RoundPhase.RoundOver:
                if (PhaseTime < ROUND_OVER_TIME) break;

                if (Score.IsOver)
                {
                    Enter(RoundPhase.MatchOver);
                }
                else
                {
                    StartRound();
                }
                break;

            case RoundPhase.MatchOver:
                UpdateMatchOver();
                break;
        }
    }

    /// <summary>
    /// Helper method for the machine that doesn't decide anything: the clock runs on by itself between two messages of the host,
    /// and leaving once the match is over is everybodys own business.
    /// </summary>
    private void Follow(float dt)
    {
        if (Phase == RoundPhase.Fight && Rules.IsTimed)
        {
            TimeLeft = MathF.Max(0.0f, TimeLeft - dt);
        }
        else if (Phase == RoundPhase.MatchOver)
        {
            UpdateMatchOver();
        }
    }

    private void UpdateMatchOver()
    {
        if (_finished) return;

        if (PhaseTime >= MATCH_OVER_TIME || (PhaseTime >= MATCH_OVER_SKIP && SkipRequested?.Invoke() == true))
        {
            _finished = true;
            Finished?.Invoke();
        }
    }

    /// <summary>
    /// Helper method to take down how the match stands, for telling the other machine.
    /// </summary>
    public RoundSnapshot Capture() => new(Phase, PhaseTime, TimeLeft, [.. Score.Rounds], LastOutcome, TimedOut, Score.ForfeitedTo ?? -1);

    /// <summary>
    /// This method is called on the machine that follows the match for everything the host says about it, already turned round so that player one is ours.
    /// </summary>
    public void Apply(in RoundSnapshot snapshot)
    {
        // Something that was said before what we already know, reminders are not sent in a way that keeps them in order
        if (Progress(snapshot.Phase, snapshot.Rounds.Length) < Progress(Phase, Score.Rounds.Count)) return;

        Score.Restore(snapshot.Rounds, snapshot.Forfeit);
        LastOutcome = snapshot.LastOutcome;
        TimedOut = snapshot.TimedOut;
        TimeLeft = snapshot.TimeLeft;

        if (snapshot.Phase == Phase) return;

        // A round after the first starts with everybody back where they began
        if (snapshot.Phase == RoundPhase.Ready && snapshot.Rounds.Length > 0) ResetPlayers();

        Phase = snapshot.Phase;
        PhaseTime = snapshot.PhaseTime;
        PhaseChanged?.Invoke(Phase);
    }

    /// <summary>
    /// Helper method to say how far into a match a phase is, the later the higher: a round is called (which adds it to the ones that were played),
    /// then the next one is got ready for and fought.
    /// </summary>
    private static int Progress(RoundPhase phase, int roundsPlayed) => phase switch
    {
        RoundPhase.Versus => -1,
        RoundPhase.MatchOver => int.MaxValue,
        RoundPhase.RoundOver => roundsPlayed * 4,
        RoundPhase.Ready => roundsPlayed * 4 + 1,
        _ => roundsPlayed * 4 + 2
    };

    /// <summary>
    /// Called when a player leaves in the middle of the match, it is over there and then and goes to whoever stayed.
    /// </summary>
    public void Forfeit(int winner)
    {
        if (Phase == RoundPhase.MatchOver) return;

        Score.Forfeit(winner);
        Enter(RoundPhase.MatchOver);
    }

    private void UpdateFight(float dt)
    {
        if (Rules.IsTimed)
        {
            TimeLeft = MathF.Max(0.0f, TimeLeft - dt);
        }

        bool oneDown = playerOne.Health == 0, twoDown = playerTwo.Health == 0;

        if (oneDown || twoDown)
        {
            // Both of them on the same blow is nobody's round
            EndRound(oneDown && twoDown ? RoundOutcome.Draw : oneDown ? RoundOutcome.PlayerTwo : RoundOutcome.PlayerOne, timedOut: false);
        }
        else if (Rules.IsTimed && TimeLeft <= 0.0f)
        {
            // On the clock it goes to whoever has more left in them
            EndRound(
                playerOne.Health == playerTwo.Health ? RoundOutcome.Draw
                : playerOne.Health > playerTwo.Health ? RoundOutcome.PlayerOne
                : RoundOutcome.PlayerTwo,
                timedOut: true);
        }
    }

    private void EndRound(RoundOutcome outcome, bool timedOut)
    {
        LastOutcome = outcome;
        TimedOut = timedOut;
        Score.Record(outcome);

        Enter(RoundPhase.RoundOver);
    }

    /// <summary>
    /// Helper method to put both players back where they started, on their feet and in one piece, and call the next round.
    /// </summary>
    private void StartRound()
    {
        ResetPlayers();

        TimeLeft = Rules.RoundSeconds;
        Enter(RoundPhase.Ready);
    }

    private void ResetPlayers()
    {
        playerOne.ResetForRound();
        playerTwo.ResetForRound();
    }

    private void Enter(RoundPhase phase)
    {
        Phase = phase;
        PhaseTime = 0.0f;
        PhaseChanged?.Invoke(phase);
    }

    public void UpdatePhysics(float dt) { }

    public void Render(float dt, object? obj = null) { }
}

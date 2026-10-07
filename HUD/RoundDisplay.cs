using System;

using Fighter2D.Character;
using Fighter2D.Match;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The clock of the round and whatever is called out across the middle of the screen. That is the round, FIGHT!, how it ended and who won the match.
/// </summary>
internal sealed class RoundDisplay : IHudDisplay
{
    // How long (in seconds) FIGHT! stays up once the round is on
    private const float FIGHT_CALL_TIME = 0.9f;

    // From how many seconds left the clock starts thumping
    private const int COUNTDOWN_SECONDS = 10;

    private const string NO_TIME_LIMIT = "--";
    private const string CONTINUE_HINT = "[icon:pad_a] continue";
    private const string REMATCH_HINT = "[icon:pad_a] rematch    [icon:pad_b] main menu";

    private readonly RoundDirector _round;
    private readonly Label _timer, _banner, _detail;

    private int _shownSeconds = -1;
    private string _shownBanner = string.Empty;

    public RoundDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _timer = layout.Get<Label>("timer_label");
        _banner = layout.Get<Label>("banner");
        _detail = layout.Get<Label>("banner_detail");

        _banner.Visible = false;
        _detail.Visible = false;
    }

    public void Update(float dt)
    {
        ShowTime();

        var (banner, detail) = Describe();
        ShowBanner(banner, detail);
    }

    private void ShowTime()
    {
        if (!_round.Rules.IsTimed)
        {
            _timer.Text = NO_TIME_LIMIT;
            return;
        }

        // Only written when the second changes, not on every update
        int seconds = (int)MathF.Ceiling(_round.TimeLeft);
        if (seconds == _shownSeconds) return;

        _shownSeconds = seconds;
        _timer.Text = seconds.ToString();

        // The last ten seconds are counted down with a bit of a thump
        if (seconds <= COUNTDOWN_SECONDS && _round.Phase == RoundPhase.Fight) _timer.Punch(0.2f, 0.25f);
    }

    /// <summary>
    /// Helper method to work out what is called out right now and the line under it, both empty when nothing is.
    /// </summary>
    private (string Banner, string Detail) Describe()
    {
        MatchScore score = _round.Score;

        switch (_round.Phase)
        {
            case RoundPhase.Ready:
                // Whoever takes this one takes the match
                int matchPoint = _round.Rules.RoundsToWin - 1;
                bool decider = score.Wins(0) == matchPoint && score.Wins(1) == matchPoint && _round.Rules.Rounds > 1;
                return (decider ? "FINAL ROUND" : $"ROUND {score.Round}", string.Empty);

            case RoundPhase.Fight:
                return (_round.PhaseTime < FIGHT_CALL_TIME ? "FIGHT!" : string.Empty, string.Empty);

            case RoundPhase.RoundOver:
                return _round.LastOutcome switch
                {
                    RoundOutcome.PlayerOne => (CallFor(0), $"{NameOf(0)} takes the round"),
                    RoundOutcome.PlayerTwo => (CallFor(1), $"{NameOf(1)} takes the round"),
                    _ => (_round.TimedOut ? "DRAW" : "DOUBLE K.O.", "nobody takes the round")
                };

            case RoundPhase.MatchOver:
                string hint = _round.RematchOffered ? REMATCH_HINT : CONTINUE_HINT;
                string result = $"{score.Wins(0)} - {score.Wins(1)}    {ButtonGlyphs.Localize(hint, GameInput.Manager.LastUsed)}";

                return score.Winner is int winner
                    ? ($"{NameOf(winner).ToUpperInvariant()} WINS", result)
                    : ("DRAW", result);

            default:
                return (string.Empty, string.Empty);
        }
    }

    private void ShowBanner(string banner, string detail)
    {
        _detail.Text = detail;
        _detail.Visible = detail.Length > 0;

        if (banner == _shownBanner) return;
        _shownBanner = banner;

        _banner.Visible = banner.Length > 0;
        if (banner.Length == 0) return;

        // Every new call lands with a thump
        _banner.Text = banner;
        _banner.Punch(0.35f, 0.35f);
    }

    /// <summary>
    /// Helper method to pick what a round that somebody won is called out as. Winning without getting touched is a PERFECT.
    /// </summary>
    private string CallFor(int winner)
    {
        if (_round.TimedOut) return "TIME UP";

        // The players aren't put back until the next round starts, so their health is still what the round ended on
        return _round.PlayerOf(winner).Health == Player.MAX_HEALTH ? "PERFECT" : "K.O.";
    }

    /// <summary>
    /// Helper method to get what a player is called on screen, which is the character they play.
    /// </summary>
    private string NameOf(int player) => _round.PlayerOf(player).Character?.PrettyName ?? $"Player {player + 1}";
}

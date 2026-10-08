using System;
using System.Numerics;

using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The clock of the round, which round it is, and whatever is called out across the middle of the screen on its dark stripe.
/// That is the round, FIGHT!, how it ended and who won the match. A K.O. hits the whole screen white for a moment first.
/// </summary>
internal sealed class RoundDisplay : IHudDisplay
{
    // How long (in seconds) FIGHT! stays up once the round is on
    private const float FIGHT_CALL_TIME = 0.9f;

    // From how many seconds left the clock starts thumping, and goes red
    private const int COUNTDOWN_SECONDS = 10;

    // How hard the screen goes white when somebody goes down, and how long it takes to come back
    private const float KO_FLASH = 0.85f;
    private const float KO_FLASH_TIME = 0.5f;
    private const float MATCH_FLASH = 0.5f;

    // How long the stripe behind a call takes to come and go
    private const float STRIPE_TIME = 0.18f;

    private const string NO_TIME_LIMIT = "--";
    private const string CONTINUE_HINT = "[icon:pad_a] continue";
    private const string REMATCH_HINT = "[icon:pad_a] rematch    [icon:pad_b] main menu";

    private static readonly Vector4 TimeColor = Vector4.One;
    private static readonly Vector4 LowTimeColor = new(1.0f, 0.38f, 0.32f, 1.0f);

    private readonly RoundDirector _round;
    private readonly Label _timer, _roundLabel, _banner, _detail;
    private readonly Panel _stripe, _flash;

    private int _shownSeconds = -1;
    private int _shownRound = -1;
    private string _shownBanner = string.Empty;

    public RoundDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _timer = layout.Get<Label>("timer_label");
        _roundLabel = layout.Get<Label>("round_label");
        _banner = layout.Get<Label>("banner");
        _detail = layout.Get<Label>("banner_detail");
        _stripe = layout.Get<Panel>("banner_stripe");
        _flash = layout.Get<Panel>("flash");

        _banner.Visible = false;
        _detail.Visible = false;
        _stripe.Opacity = 0.0f;
        _flash.Opacity = 0.0f;

        round.PhaseChanged += OnPhaseChanged;
    }

    /// <summary>
    /// Helper method for the moments that hit the whole screen. The end of a round goes white, the end of the match a bit less so.
    /// </summary>
    private void OnPhaseChanged(RoundPhase phase)
    {
        switch (phase)
        {
            case RoundPhase.RoundOver:
                Flash(KO_FLASH);
                break;

            case RoundPhase.MatchOver:
                Flash(MATCH_FLASH);
                break;
        }
    }

    private void Flash(float strength)
    {
        _flash.Opacity = strength;
        _flash.FadeOut(KO_FLASH_TIME);
    }

    public void Update(float dt)
    {
        ShowTime();
        ShowRound();

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

        // The last ten seconds are counted down in red with a bit of a thump
        bool low = seconds <= COUNTDOWN_SECONDS && _round.Phase == RoundPhase.Fight;
        _timer.Color = low ? LowTimeColor : TimeColor;
        if (low) _timer.Punch(0.2f, 0.25f);
    }

    /// <summary>
    /// Helper method to write which round this is under the clock. The one that decides the match says so.
    /// </summary>
    private void ShowRound()
    {
        int round = _round.Score.Round;
        if (round == _shownRound) return;

        _shownRound = round;
        _roundLabel.Text = IsDecider() ? "FINAL ROUND" : $"ROUND {round}";
        _roundLabel.Punch(0.2f, 0.3f);
    }

    /// <summary>
    /// Helper method to say whether whoever takes the next round takes the match, with both of them one off.
    /// </summary>
    private bool IsDecider()
    {
        int matchPoint = _round.Rules.RoundsToWin - 1;
        MatchScore score = _round.Score;

        return score.Wins(0) == matchPoint && score.Wins(1) == matchPoint && _round.Rules.Rounds > 1;
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
                return (IsDecider() ? "FINAL ROUND" : $"ROUND {score.Round}", string.Empty);

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

        // The stripe comes with the call and goes with it
        if (banner.Length == 0)
        {
            _stripe.FadeOut(STRIPE_TIME);
            return;
        }

        if (_stripe.Opacity < 1.0f) _stripe.FadeIn(STRIPE_TIME);

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

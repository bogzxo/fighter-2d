using System.Text;

using Horizon.Core.Tweening;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The two bars along the top of the fight. How much health each player has left, and under them who they are playing and how many rounds they have taken.
/// </summary>
internal sealed class HealthDisplay : IHudDisplay
{
    // What a round looks like on the counter once it is won, and before
    private const string ROUND_WON = "[icon:check_on]";
    private const string ROUND_OPEN = "[icon:check_off]";

    // How long (in seconds) a bar takes to drain down to the new health, and the name of the tween that does it
    private const float DRAIN_TIME = 0.25f;
    private const string DRAIN_CHANNEL = "health";

    // How long (in seconds) the meter takes to fill up to what was earned, and the name of the tween that does it
    private const float METER_FILL_TIME = 0.3f;
    private const string METER_CHANNEL = "meter";

    private readonly RoundDirector _round;
    private readonly ProgressBar[] _bars, _meters;
    private readonly Label[] _info;

    // What each bar was last told to show, the bar is only touched again when that changes
    private readonly float[] _shownHealth = [-1, -1];
    private readonly float[] _shownMeter = [-1, -1];
    private readonly int[] _shownWins = [-1, -1];

    public HealthDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _bars = [layout.Get<ProgressBar>("player1_health"), layout.Get<ProgressBar>("player2_health")];
        _meters = [layout.Get<ProgressBar>("player1_meter"), layout.Get<ProgressBar>("player2_meter")];
        _info = [layout.Get<Label>("player1_info"), layout.Get<Label>("player2_info")];

        foreach (ProgressBar meter in _meters) meter.Progress = 0.0f;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            Player player = _round.PlayerOf(i);

            ShowHealth(i, player.Health / (float)Player.MAX_HEALTH);
            ShowMeter(i, player.Meter / (float)Player.MAX_METER);
            ShowInfo(i, player);
        }
    }

    /// <summary>
    /// Helper method to put the meter of a player on their bar. It fills up smoothly and gives a little jump when it is full, which is the moment to spend it.
    /// </summary>
    private void ShowMeter(int index, float meter)
    {
        if (meter == _shownMeter[index]) return;

        ProgressBar bar = _meters[index];
        bool filled = meter >= 1.0f && _shownMeter[index] < 1.0f;

        _shownMeter[index] = meter;
        bar.Tweens.Play(Tween.To(() => bar.Progress, value => bar.Progress = value, meter, METER_FILL_TIME).SetEasing(Easing.OutCubic), METER_CHANNEL);

        if (filled) bar.Punch(0.15f, 0.35f);
    }

    /// <summary>
    /// Helper method to put the health of a player on their bar. The bar drains instead of snapping, and rattles when it goes down.
    /// </summary>
    private void ShowHealth(int index, float health)
    {
        if (health == _shownHealth[index]) return;

        ProgressBar bar = _bars[index];
        if (health < _shownHealth[index]) bar.Shake(9.0f, 0.3f);

        _shownHealth[index] = health;
        bar.Tweens.Play(Tween.To(() => bar.Progress, value => bar.Progress = value, health, DRAIN_TIME).SetEasing(Easing.OutCubic), DRAIN_CHANNEL);
    }

    /// <summary>
    /// Helper method to write the name of a player and the rounds they have won under their bar, with the counter towards the middle of the screen.
    /// </summary>
    private void ShowInfo(int index, Player player)
    {
        int wins = _round.Score.Wins(index);
        if (wins == _shownWins[index] || player.Character is null) return;

        var counter = new StringBuilder();
        for (int i = 0; i < _round.Rules.RoundsToWin; i++)
        {
            // Player two fills theirs up from the other end, so both counters grow towards the middle
            bool won = index == 0 ? i < wins : i >= _round.Rules.RoundsToWin - wins;
            counter.Append(won ? ROUND_WON : ROUND_OPEN);
        }

        string name = player.Character.PrettyName.ToUpperInvariant();
        _info[index].Text = index == 0 ? $"{name}   {counter}" : $"{counter}   {name}";

        // Not the first time, that is just the line being filled in
        if (_shownWins[index] >= 0) _info[index].Punch(0.2f, 0.4f);
        _shownWins[index] = wins;
    }
}

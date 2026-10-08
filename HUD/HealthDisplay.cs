using System.Numerics;
using System.Text;

using Horizon.Core.Tweening;
using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The two bars along the top of the fight and the meters along the bottom. How much health each player has left (with a red
/// ghost of what the last hits took off, catching up a moment later), under the bars who they are playing and how many rounds
/// they have taken, and in the bottom corners the super meter in the four pieces a special costs. A bar on its last legs
/// pulses red, a meter that is full says MAX and glows, so nobody has to look twice.
/// </summary>
internal sealed class HealthDisplay : IHudDisplay
{
    // What a round looks like on the counter once it is won, and before
    private const string ROUND_WON = "[icon:check_on]";
    private const string ROUND_OPEN = "[icon:check_off]";

    // How long (in seconds) a bar takes to drain down to the new health, and the name of the tween that does it
    private const float DRAIN_TIME = 0.2f;
    private const string DRAIN_CHANNEL = "health";

    // How long the ghost hangs about before it catches up with the bar, and how long that takes
    private const float GHOST_DELAY = 0.55f;
    private const float GHOST_TIME = 0.4f;
    private const string GHOST_CHANNEL = "ghost";

    // How long (in seconds) the meter takes to fill up to what was earned, and the name of the tween that does it
    private const float METER_FILL_TIME = 0.3f;
    private const string METER_CHANNEL = "meter";

    // Under this much health the bar starts pulsing
    private const float LOW_HEALTH = 0.25f;

    private const string METER_TEXT = "METER";
    private const string METER_FULL_TEXT = "MAX";

    private static readonly Vector4 MeterColor = new(1.0f, 0.82f, 0.25f, 1.0f);
    private static readonly Vector4 LabelColor = MenuColors.Hint;

    private readonly RoundDirector _round;
    private readonly ProgressBar[] _bars, _meters;
    private readonly Label[] _names, _rounds, _meterLabels;

    // The red of a bar on its last legs and the glow of a full meter swing on these
    private readonly Pulse _lowPulse = new(0.3f, 1.0f, 0.8f);
    private readonly Pulse _fullPulse = new(0.7f, 1.0f, 1.2f);

    // What each bar was last told to show, the bar is only touched again when that changes
    private readonly float[] _shownHealth = [-1, -1];
    private readonly float[] _shownMeter = [-1, -1];
    private readonly int[] _shownWins = [-1, -1];

    public HealthDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _bars = [layout.Get<ProgressBar>("player1_health"), layout.Get<ProgressBar>("player2_health")];
        _meters = [layout.Get<ProgressBar>("player1_meter"), layout.Get<ProgressBar>("player2_meter")];
        _names = [layout.Get<Label>("player1_name"), layout.Get<Label>("player2_name")];
        _rounds = [layout.Get<Label>("player1_rounds"), layout.Get<Label>("player2_rounds")];
        _meterLabels = [layout.Get<Label>("player1_meter_label"), layout.Get<Label>("player2_meter_label")];

        foreach (ProgressBar meter in _meters) meter.Progress = 0.0f;
    }

    public void Update(float dt)
    {
        _lowPulse.Update(dt);
        _fullPulse.Update(dt);

        for (int i = 0; i < _bars.Length; i++)
        {
            Player player = _round.PlayerOf(i);

            ShowHealth(i, player.Health / (float)Player.MAX_HEALTH);
            ShowMeter(i, player.Meter / (float)Player.MAX_METER);
            ShowInfo(i, player);
        }
    }

    /// <summary>
    /// Helper method to put the health of a player on their bar. The bar drains instead of snapping and rattles when it goes down,
    /// the ghost behind it waits a moment and then drains after it. Under a quarter the fill throbs red.
    /// </summary>
    private void ShowHealth(int index, float health)
    {
        ProgressBar bar = _bars[index];

        // The pulse is every update, the bar is on its last legs for as long as it is
        float pulse = _lowPulse.Value;
        bar.FillColor = health <= LOW_HEALTH && health > 0.0f ? new Vector4(1.0f, pulse * 0.6f, pulse * 0.5f, 1.0f) : null;

        if (health == _shownHealth[index]) return;

        bool hurt = health < _shownHealth[index];
        if (hurt) bar.Shake(9.0f, 0.3f);

        // Healed (a round starting over, the lab refilling) the ghost just goes along, there is nothing to show
        if (!hurt) bar.Ghost = health;

        _shownHealth[index] = health;
        bar.Tweens.Play(Tween.To(() => bar.Progress, value => bar.Progress = value, health, DRAIN_TIME).SetEasing(Easing.OutCubic), DRAIN_CHANNEL);
        bar.Tweens.Play(Tween.To(() => bar.Ghost, value => bar.Ghost = value, health, GHOST_TIME).SetEasing(Easing.OutCubic).SetDelay(GHOST_DELAY), GHOST_CHANNEL);
    }

    /// <summary>
    /// Helper method to put the meter of a player on their bar. It fills up smoothly and gives a little jump when it is full,
    /// which is the moment to spend it, and glows for as long as it stays full.
    /// </summary>
    private void ShowMeter(int index, float meter)
    {
        ProgressBar bar = _meters[index];
        Label label = _meterLabels[index];

        bool full = meter >= 1.0f;
        if (full)
        {
            float glow = _fullPulse.Value;
            bar.FillColor = new Vector4(1.0f, 0.82f + (1.0f - 0.82f) * glow, 0.25f + (1.0f - 0.25f) * glow, 1.0f);
            label.Color = MeterColor;
        }
        else
        {
            bar.FillColor = MeterColor;
            label.Color = LabelColor;
        }

        if (meter == _shownMeter[index]) return;

        bool filled = full && _shownMeter[index] < 1.0f;
        bool spent = meter < _shownMeter[index];

        _shownMeter[index] = meter;
        bar.Tweens.Play(Tween.To(() => bar.Progress, value => bar.Progress = value, meter, METER_FILL_TIME).SetEasing(Easing.OutCubic), METER_CHANNEL);

        label.Text = full ? METER_FULL_TEXT : METER_TEXT;

        if (filled)
        {
            bar.Punch(0.15f, 0.35f);
            label.Punch(0.4f, 0.35f);
        }
        else if (spent)
        {
            bar.Shake(5.0f, 0.2f);
        }
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

        _names[index].Text = player.Character.PrettyName.ToUpperInvariant();
        _rounds[index].Text = counter.ToString();

        // Not the first time, that is just the line being filled in
        if (_shownWins[index] >= 0) _rounds[index].Punch(0.3f, 0.4f);
        _shownWins[index] = wins;
    }
}

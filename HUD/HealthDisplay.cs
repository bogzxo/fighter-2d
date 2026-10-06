using System.Text;

using Fighter2D.Character;
using Fighter2D.Match;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The two bars along the top of the fight: how much health each player has left, and under them who they are playing and how many rounds they have taken.
/// </summary>
internal sealed class HealthDisplay : IHudDisplay
{
    // What a round looks like on the counter once it is won, and before
    private const string ROUND_WON = "[icon:check_on]";
    private const string ROUND_OPEN = "[icon:check_off]";

    private readonly RoundDirector _round;
    private readonly ProgressBar[] _bars;
    private readonly Label[] _info;

    // How many rounds each player had won when their line was last written, it is only written again when that changes
    private readonly int[] _shownWins = [-1, -1];

    public HealthDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _bars = [layout.Get<ProgressBar>("player1_health"), layout.Get<ProgressBar>("player2_health")];
        _info = [layout.Get<Label>("player1_info"), layout.Get<Label>("player2_info")];
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            Player player = _round.PlayerOf(i);

            ShowHealth(_bars[i], player.Health / 100.0f);
            ShowInfo(i, player);
        }
    }

    /// <summary>
    /// Helper method to put the health of a player on their bar, which rattles when it goes down.
    /// </summary>
    private static void ShowHealth(ProgressBar bar, float health)
    {
        if (health < bar.Progress - 0.001f)
        {
            bar.Shake(9.0f, 0.3f);
        }

        bar.Progress = health;
    }

    /// <summary>
    /// Helper method to write the name of a player and the rounds they have won under their bar, the counter towards the middle of the screen.
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

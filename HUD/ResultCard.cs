using System;
using System.Text;

using Horizon.Content;
using Horizon.Input;
using Horizon.UI;
using Horizon.UI.Components;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.HUD;

/// <summary>
/// What comes up once the match is decided, on the result layer of the HUD. Who won and by how much, how every round
/// went, what both of them did (<see cref="MatchStats"/>), and the three ways on, the same fight again, back to the
/// fighters with the same setup, or the main menu. Any gamepad picks. It used to be a line of text and two buttons
/// nobody could see the names of.
/// </summary>
internal sealed class ResultCard : IHudDisplay
{
    private const string LAYER = "result";

    private static readonly string[] Stats = ["damage", "combo", "hits", "counters", "punishes", "blocked", "parried", "whiffs"];

    private readonly UILayout _layout;
    private readonly RoundDirector _round;
    private readonly MatchStats _stats;
    private readonly Action? _changeFighters;
    private readonly UINavigator _nav;
    private readonly MenuActions _actions = new();
    private readonly HintBar _hint;
    private readonly Button _rematch, _fighters, _menu;

    private bool _shown;

    /// <param name="changeFighters">Back to the fight setup screen with the same setup, null where there is none (online).</param>
    public ResultCard(UILayout layout, RoundDirector round, MatchStats stats, Action? changeFighters)
    {
        _layout = layout;
        _round = round;
        _stats = stats;
        _changeFighters = changeFighters;
        _nav = layout.Module.Navigation;

        _rematch = layout.Get<Button>("btn_result_rematch");
        _fighters = layout.Get<Button>("btn_result_fighters");
        _menu = layout.Get<Button>("btn_result_menu");

        // Each goes through the rounds, which make sure only one of them ever happens
        _rematch.OnPressed = round.StartRematch;
        _fighters.OnPressed = () => { if (round.TryFinish()) _changeFighters?.Invoke(); };
        _menu.OnPressed = () => round.TryFinish();

        _rematch.Visible = round.RematchOffered;
        _fighters.Visible = changeFighters is not null;

        _hint = new HintBar(layout.Get<Label>("result_hint"), _actions);
        _actions.Directions("hints.choose", (_, right, _) => _nav.Move(right, 0));
        _actions.Add(GamepadInput.A, "hints.pick", _ => _nav.Activate());

        layout.Module.SetLayerVisible(LAYER, false);
    }

    public void Update(float dt)
    {
        if (_round.Phase != RoundPhase.MatchOver) return;

        if (!_shown) Show();

        _hint.Update(dt);

        // Not right away, the button somebody was mashing when the last hit landed shouldn't decide anything
        if (_round.PhaseTime >= RoundDirector.MATCH_OVER_SKIP) _actions.Update();
    }

    private void Show()
    {
        _shown = true;

        MatchScore score = _round.Score;
        _layout.Get<Label>("result_title").Text = score.Winner is int winner
            ? Localisation.Format("fight.wins", ("name", NameOf(winner).ToUpperInvariant()))
            : Localisation.Get("fight.draw");
        _layout.Get<Label>("result_score").Text = Localisation.Format("result.score", ("one", score.Wins(0)), ("two", score.Wins(1)));
        _layout.Get<Label>("result_rounds").Text = DescribeRounds();

        _layout.Get<Label>("result_name_one").Text = NameOf(0);
        _layout.Get<Label>("result_name_two").Text = NameOf(1);

        FighterStats one = _stats.Of(0), two = _stats.Of(1);
        foreach (string stat in Stats)
        {
            _layout.Get<Label>($"stat_{stat}_label").Text = Localisation.Get("result." + stat);
            _layout.Get<Label>($"stat_{stat}_one").Text = ValueOf(one, stat).ToString();
            _layout.Get<Label>($"stat_{stat}_two").Text = ValueOf(two, stat).ToString();
        }

        _layout.Module.SetLayerVisible(LAYER, true);
        _layout.PlayIntros();

        // Onto the first way on there is, a rematch where there is one
        _nav.Select(_rematch.Visible ? _rematch : _fighters.Visible ? _fighters : _menu);
    }

    private static int ValueOf(FighterStats stats, string stat) => stat switch
    {
        "damage" => stats.Damage,
        "combo" => stats.LongestCombo,
        "hits" => stats.Hits,
        "counters" => stats.CounterHits,
        "punishes" => stats.Punishes,
        "blocked" => stats.Blocked,
        "parried" => stats.Parried,
        _ => stats.Whiffs
    };

    /// <summary>
    /// Helper method to write every round out in a line, who took it and how.
    /// </summary>
    private string DescribeRounds()
    {
        var text = new StringBuilder();
        for (int i = 0; i < _stats.Rounds.Count; i++)
        {
            RoundResult round = _stats.Rounds[i];
            if (text.Length > 0) text.Append(Localisation.Get("result.between"));

            string how = Localisation.Get(round.Ending switch
            {
                RoundEnding.Perfect => "fight.perfect",
                RoundEnding.TimeUp => "fight.time_up",
                RoundEnding.DoubleKO => "fight.double_ko",
                RoundEnding.Draw => "fight.draw",
                _ => "fight.ko"
            });

            text.Append(round.Winner is int winner
                ? Localisation.Format("result.round", ("round", i + 1), ("name", NameOf(winner)), ("how", how))
                : Localisation.Format("result.round_nobody", ("round", i + 1), ("how", how)));
        }

        return text.ToString();
    }

    private string NameOf(int player) => _round.PlayerOf(player).Character?.PrettyName ?? Localisation.Format("common.player", ("number", player + 1));
}

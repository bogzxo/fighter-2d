using System.Numerics;

using Fighter2D.Character;
using Fighter2D.Combat;
using Fighter2D.Match;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// Calls out what every attack came to, on the side of whoever threw it. HIT, COUNTER HIT, PUNISH, BLOCKED or WHIFF, plus the combo count once it is a combo.
/// It listens to the <see cref="CombatLog"/> of the fight, so it shows the same thing no matter who is attacking.
/// </summary>
internal sealed class HitCalloutDisplay : IHudDisplay
{
    // How long (in seconds) a callout stays up before it fades, and how long the fade takes
    private const float HOLD_TIME = 0.9f;
    private const float FADE_TIME = 0.3f;

    // From how many hits in a row the combo counter shows up
    private const int MIN_COMBO = 2;

    private static readonly Vector4 HitColor = Vector4.One;
    private static readonly Vector4 CounterColor = new(1.0f, 0.8f, 0.25f, 1.0f);
    private static readonly Vector4 PunishColor = new(1.0f, 0.38f, 0.32f, 1.0f);
    private static readonly Vector4 BlockedColor = new(0.5f, 0.75f, 1.0f, 1.0f);
    private static readonly Vector4 WhiffColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    private readonly RoundDirector _round;
    private readonly Label[] _callouts, _combos;

    // The last thing each player's attack came to that hasn't been put on screen yet
    private readonly (HitResult Result, int Combo)?[] _pending = new (HitResult, int)?[2];

    public HitCalloutDisplay(UILayout layout, RoundDirector round, CombatLog log)
    {
        _round = round;
        _callouts = [layout.Get<Label>("player1_callout"), layout.Get<Label>("player2_callout")];
        _combos = [layout.Get<Label>("player1_combo"), layout.Get<Label>("player2_combo")];

        foreach (Label label in _callouts) label.Opacity = 0.0f;
        foreach (Label label in _combos) label.Opacity = 0.0f;

        log.Reported += OnReported;
    }

    private void OnReported(Player attacker, HitResult result, int combo)
    {
        // Only noted down here, the labels are touched in Update like everything else of the HUD
        int side = attacker == _round.PlayerOf(0) ? 0 : 1;
        _pending[side] = (result, combo);
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i] is not { } report) continue;
            _pending[i] = null;

            var (text, color) = Describe(report.Result);
            Show(_callouts[i], text, color);

            // The counter only shows up once it is actually a combo, and goes away with the next thing that isn't one
            if (report.Combo >= MIN_COMBO) Show(_combos[i], $"{report.Combo} HITS", color);
            else _combos[i].FadeOut(FADE_TIME);
        }
    }

    /// <summary>
    /// Helper method to pop a callout up, hold it for a moment and fade it out again. It is all tweens, a new callout simply takes over from the old.
    /// </summary>
    private static void Show(Label label, string text, Vector4 color)
    {
        label.Text = text;
        label.Color = color;
        label.Opacity = 1.0f;

        label.Punch(0.3f, 0.2f);
        label.FadeOut(FADE_TIME, HOLD_TIME);
    }

    private static (string Text, Vector4 Color) Describe(HitResult result) => result switch
    {
        HitResult.CounterHit => ("COUNTER HIT", CounterColor),
        HitResult.Punish => ("PUNISH", PunishColor),
        HitResult.Blocked => ("BLOCKED", BlockedColor),
        HitResult.Whiff => ("WHIFF", WhiffColor),
        _ => ("HIT", HitColor)
    };
}

using System;
using System.Numerics;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// Calls out what every attack came to, on the side of whoever threw it. HIT, COUNTER HIT, PUNISH, BLOCKED or WHIFF.
/// Once it is a combo the hits are counted under it, along with the damage they have done so far.
/// With frame data switched on in the options there is a third line with the startup of the move and who gets to act first after it.
/// It listens to the <see cref="CombatLog"/> of the fight, so it shows the same thing no matter who is attacking.
/// </summary>
internal sealed class HitCalloutDisplay : IHudDisplay
{
    // How long (in seconds) a callout stays up before it fades, and how long the fade takes
    private const float HOLD_TIME = 0.9f;
    private const float FADE_TIME = 0.3f;

    // The frame data is there to be read, so it gets to stay up for longer
    private const float FRAME_DATA_HOLD_TIME = 2.0f;

    // From how many hits in a row the combo counter shows up
    private const int MIN_COMBO = 2;

    private static readonly Vector4 HitColor = Vector4.One;
    private static readonly Vector4 CounterColor = new(1.0f, 0.8f, 0.25f, 1.0f);
    private static readonly Vector4 PunishColor = new(1.0f, 0.38f, 0.32f, 1.0f);
    private static readonly Vector4 BlockedColor = new(0.5f, 0.75f, 1.0f, 1.0f);
    private static readonly Vector4 WhiffColor = new(0.58f, 0.6f, 0.66f, 1.0f);
    private static readonly Vector4 FrameDataColor = new(0.86f, 0.92f, 1.0f, 1.0f);

    private readonly RoundDirector _round;
    private readonly Label[] _callouts, _combos, _frames;

    // The last thing each player's attack came to that hasn't been put on screen yet
    private readonly AttackReport?[] _pending = new AttackReport?[2];

    public HitCalloutDisplay(UILayout layout, RoundDirector round, CombatLog log)
    {
        _round = round;
        _callouts = [layout.Get<Label>("player1_callout"), layout.Get<Label>("player2_callout")];
        _combos = [layout.Get<Label>("player1_combo"), layout.Get<Label>("player2_combo")];
        _frames = [layout.Get<Label>("player1_frames"), layout.Get<Label>("player2_frames")];

        foreach (Label label in _callouts) label.Opacity = 0.0f;
        foreach (Label label in _combos) label.Opacity = 0.0f;
        foreach (Label label in _frames) label.Opacity = 0.0f;

        log.Reported += OnReported;
    }

    private void OnReported(AttackReport report)
    {
        // Only noted down here, the labels are touched in Update like everything else of the HUD
        int side = report.Attacker == _round.PlayerOf(0) ? 0 : 1;
        _pending[side] = report;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i] is not { } report) continue;
            _pending[i] = null;

            var (text, color) = Describe(report.Result);
            Show(_callouts[i], text, color, HOLD_TIME);

            // The counter only shows up once it is actually a combo, and goes away with the next thing that isn't one
            if (report.ComboCount >= MIN_COMBO) Show(_combos[i], $"{report.ComboCount} HITS    {report.ComboDamage} DMG", color, HOLD_TIME);
            else _combos[i].FadeOut(FADE_TIME);

            if (GameOptions.FrameData) Show(_frames[i], DescribeFrames(report), FrameDataColor, FRAME_DATA_HOLD_TIME);
        }
    }

    /// <summary>
    /// Helper method to pop a callout up, hold it for a moment and fade it out again. It is all tweens, a new callout simply takes over from the old.
    /// </summary>
    private static void Show(Label label, string text, Vector4 color, float holdTime)
    {
        label.Text = text;
        label.Color = color;
        label.Opacity = 1.0f;

        label.Punch(0.3f, 0.2f);
        label.FadeOut(FADE_TIME, holdTime);
    }

    private static (string Text, Vector4 Color) Describe(HitResult result) => result switch
    {
        HitResult.CounterHit => ("COUNTER HIT", CounterColor),
        HitResult.Punish => ("PUNISH", PunishColor),
        HitResult.Blocked => ("BLOCKED", BlockedColor),
        HitResult.Whiff => ("WHIFF", WhiffColor),
        _ => ("HIT", HitColor)
    };

    /// <summary>
    /// Helper method to write the frame data of an attack the way the lab rats read it. Startup first, then who is ahead once it is over.
    /// Plus is the attacker getting to act first, minus is the other one. Everything is in ticks of the fight.
    /// </summary>
    private static string DescribeFrames(in AttackReport report)
    {
        MoveFrameData frames = report.Move.FrameData;

        // A counter hit stuns for longer, which is the whole point of landing one
        int hitstun = report.Result == HitResult.CounterHit
            ? (int)MathF.Round(frames.Hitstun * CombatRules.COUNTER_HITSTUN_SCALE)
            : frames.Hitstun;

        // A hit that didn't land leaves the attacker stuck in the rest of the move with nobody stunned, which is as minus as it gets
        bool landed = report.Result is not (HitResult.Whiff or HitResult.Blocked);
        int advantage = landed ? hitstun - frames.Recovery : -frames.Recovery;

        string outcome = report.Result switch
        {
            HitResult.Whiff => "on whiff",
            HitResult.Blocked => "on block",
            _ => "on hit"
        };

        return $"startup {frames.Startup}    {advantage:+0;-0;0} {outcome}";
    }
}

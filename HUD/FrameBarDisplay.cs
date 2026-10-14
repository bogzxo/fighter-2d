using System;
using System.Collections.Generic;
using System.Numerics;

using Horizon.UI;
using Horizon.UI.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The frame bars at the bottom of the screen, one for each player, player one over player two. Each says what its
/// player is doing right now and has the frame data of it as a bar. The startup, the active frames and the recovery
/// of an attack are three pieces as long as they last, with a line that runs along them as the move plays, and the
/// numbers next to the name (startup, active, recovery, who gets to act first on hit and on block, the damage).
/// Somebody who got hit or is stuck in a block has the stun as a bar running out instead.
/// Once an attack is over its bar stays, fainter, until the next one, frame data is there to be read.
/// Everything is in ticks of the fight (see FightTicks.TICK_RATE) like the rest of the frame data.
/// </summary>
internal sealed class FrameBarDisplay : IHudDisplay
{
    // What the lot of it is called in the layout, for whoever wants it off the screen
    public const string ROOT = "frame_bars";

    // How much of itself a bar is drawn with once what it shows is over, and how long it takes to get there and back
    private const float RESTING_OPACITY = 0.4f;
    private const float FADE_TIME = 0.25f;
    private const float WAKE_TIME = 0.06f;

    // The same three colours every frame meter there is uses, and two for being stunned
    private static readonly Vector4 StartupColor = new(0.36f, 0.85f, 0.52f, 1.0f);
    private static readonly Vector4 ActiveColor = new(1.0f, 0.34f, 0.36f, 1.0f);
    private static readonly Vector4 RecoveryColor = new(0.36f, 0.6f, 1.0f, 1.0f);
    private static readonly Vector4 HitstunColor = new(1.0f, 0.76f, 0.28f, 1.0f);
    private static readonly Vector4 BlockstunColor = new(0.62f, 0.7f, 0.86f, 1.0f);

    private enum Showing { Nothing, Attack, Hitstun, Blockstun }

    /// <summary>
    /// One player's bar, and what was last put on it so that it is only written again when that changes.
    /// </summary>
    private sealed class Row(UILayout layout, string prefix, string tag)
    {
        public readonly Label Name = layout.Get<Label>(prefix + "_move");
        public readonly Readout Data = layout.Get<Readout>(prefix + "_move_data");
        public readonly SegmentBar Bar = layout.Get<SegmentBar>(prefix + "_move_bar");

        // The names of the moves as they are shown, made once each, a string every tick would be garbage
        public readonly Dictionary<string, string> Names = [];
        public readonly string Tag = tag;

        public string? NamedMove;
        public FightingMove? Attack;
        public Showing Showing;
        public bool Live;

        // How long the stun that is showing was when it started, and how much of it was left a tick ago
        public int StunWhole, StunLeft;
    }

    private readonly RoundDirector _round;
    private readonly Row[] _rows;

    public FrameBarDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _rows = [new Row(layout, "player1", "P1"), new Row(layout, "player2", "P2")];

        foreach (Row row in _rows)
        {
            row.Name.Text = row.Tag;
            row.Data.Write(string.Empty);
            row.Bar.Clear();
            row.Bar.Opacity = RESTING_OPACITY;
        }
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            Player player = _round.PlayerOf(i);
            if (player.Controller is not { } controller) continue;

            Row row = _rows[i];
            FightingMove move = controller.CurrentMove;

            // The name is whatever they are doing, attack or not
            if (!ReferenceEquals(row.NamedMove, move.Id))
            {
                row.NamedMove = move.Id;
                row.Name.Text = NameOf(row, move.Id);
            }

            FighterState state = controller.State;

            if (state.IsInHitstun) ShowStun(row, Showing.Hitstun, state.HitstunTicks);
            else if (state.IsInBlockstun) ShowStun(row, Showing.Blockstun, state.BlockstunTicks);
            else if (IsAttack(move) && !controller.Playback.IsFinished) ShowAttack(row, controller, move);
            else Rest(row);
        }
    }

    // A move that hits somebody is the only kind that has frame data worked out for it (see MoveFrameData.Of)
    private static bool IsAttack(FightingMove move) => move.FrameData.Recovery > 0;

    /// <summary>
    /// Helper method to put an attack on a bar, its three pieces and its numbers when it is a new one, and the line
    /// where the move has got to every time.
    /// </summary>
    private static void ShowAttack(Row row, PlayerController controller, FightingMove move)
    {
        MoveFrameData frames = move.FrameData;
        float ticksPerFrame = FightTicks.TICK_RATE / MathF.Max(1.0f, controller.Player.Character.FrameRate);
        int active = Math.Max(1, (int)MathF.Round(Math.Max(1, move.ActiveFrames) * ticksPerFrame));

        if (row.Showing != Showing.Attack || !ReferenceEquals(row.Attack, move))
        {
            row.Showing = Showing.Attack;
            row.Attack = move;

            row.Bar.Clear();
            row.Bar.Add(frames.Startup, StartupColor, 0);
            row.Bar.Add(active, ActiveColor, 1);
            row.Bar.Add(frames.Recovery, RecoveryColor, 2);
            row.Bar.Whole = 0.0f;

            row.Data.Clear()
                .Add("STARTUP ").Add(frames.Startup)
                .Add("  ACTIVE ").Add(active)
                .Add("  RECOVERY ").Add(frames.Recovery)
                .Add("  HIT ").Add(Sign(frames.OnHit)).Add(Math.Abs(frames.OnHit))
                .Add("  BLOCK ").Add(Sign(frames.OnBlock)).Add(Math.Abs(frames.OnBlock))
                .Add("  DMG ").Add(move.Damage);
        }

        // How far into the move the playback is, the phases that are done and the frames of the one that is playing.
        // It stands still through a hitstop like the move does, which is the freeze of a hit seen on the bar
        MovePlayback playback = controller.Playback;
        uint played = playback.Frame;

        for (int phase = 0; phase < playback.Phase && phase < move.Phases.Length; phase++)
        {
            played += move.Phases[phase].LengthFor(controller.GetAnimationLength(move.Phases[phase].Animation));
        }

        row.Bar.Marker = MathF.Min(played * ticksPerFrame, frames.Startup + active + frames.Recovery);
        Wake(row);
    }

    /// <summary>
    /// Helper method to put a stun on a bar, one piece as long as the stun was when it began with the line on how
    /// much of it is gone, and how many ticks are left as a number.
    /// </summary>
    private static void ShowStun(Row row, Showing kind, int left)
    {
        // A new stun, or one that got longer because another hit landed in it
        if (row.Showing != kind || left > row.StunLeft)
        {
            row.Showing = kind;
            row.Attack = null;
            row.StunWhole = Math.Max(1, left);

            row.Bar.Clear();
            row.Bar.Add(row.StunWhole, kind == Showing.Hitstun ? HitstunColor : BlockstunColor, 3);
            row.Bar.Whole = 0.0f;
        }

        row.StunLeft = left;
        row.Bar.Marker = row.StunWhole - left;

        // Every tick, the number changes every tick. A readout takes it without making a string
        row.Data.Clear().Add(kind == Showing.Hitstun ? "HITSTUN " : "BLOCKSTUN ").Add(left);
        Wake(row);
    }

    /// <summary>
    /// Helper method to leave a bar as it is and let it go faint, what was on it is over.
    /// </summary>
    private static void Rest(Row row)
    {
        if (!row.Live) return;

        row.Live = false;
        row.StunLeft = 0;

        // A stun that is over says how long it was, "1" is where the count stopped and tells nobody anything.
        // And whatever comes next is new, another stun of the same kind as well
        if (row.Showing != Showing.Attack)
        {
            row.Data.Clear().Add(row.Showing == Showing.Hitstun ? "HITSTUN " : "BLOCKSTUN ").Add(row.StunWhole).Add(" IN ALL");
            row.Showing = Showing.Nothing;
        }

        row.Bar.Marker = -1.0f;
        row.Bar.TweenOpacity(RESTING_OPACITY, FADE_TIME);
    }

    private static void Wake(Row row)
    {
        if (row.Live) return;

        row.Live = true;
        row.Bar.TweenOpacity(1.0f, WAKE_TIME);
    }

    private static string Sign(int advantage) => advantage < 0 ? "-" : "+";

    /// <summary>
    /// Helper method to write the name of a move the way it is shown, "punch_left" being "P1  PUNCH LEFT".
    /// </summary>
    private static string NameOf(Row row, string id)
    {
        if (row.Names.TryGetValue(id, out string? name)) return name;

        name = id.Length > 0 ? $"{row.Tag}  {id.Replace('_', ' ').ToUpperInvariant()}" : row.Tag;
        row.Names[id] = name;
        return name;
    }
}

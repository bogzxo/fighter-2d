using System;
using System.Numerics;

using Horizon.Core.Components;

namespace Fighter2D.Audio;

/// <summary>
/// What a fight sounds like. Every attack as the combat log reports it (a hit by how much it did, the ding of a counter
/// hit or a punish on top, a block, a parry, a whiff), panned to where it happened, the announcer on the rounds (the
/// round, FIGHT, a perfect, a tie, time, the winner) and the bell of a K.O., footsteps under whoever runs about on the
/// ground and a thump when somebody comes down, the fight's music and the map's rain under the lot. It listens to the fight
/// and never tells it anything, the simulation knows nothing about sound.
/// </summary>
internal sealed class FightSounds(FightContext fight, RoundDirector round, MapDefinition map) : GameComponent
{
    // How far a fighter goes on the ground between two footsteps, and how fast they have to be going for there to be any
    private const float STRIDE = 34.0f;
    private const float WALKING = 40.0f;

    // How fast a fall has to be to be heard landing, and how fast is the loudest landing there is
    private const float QUIET_FALL = 150.0f;
    private const float HARD_FALL = 900.0f;

    // How much damage is the heaviest a hit sounds, and how far from the middle of the two of them is all the way to one side
    private const float HEAVIEST = 18.0f;
    private const float PAN_REACH = 320.0f;

    // A map with this much falling out of the sky a second is a rainy one
    private const float RAINY = 100.0f;

    private readonly float[] _stride = new float[2];
    private readonly bool[] _wasGrounded = [true, true];
    private readonly float[] _falling = new float[2];

    public override void Initialize()
    {
        base.Initialize();

        fight.CombatLog.Reported += OnReported;
        round.PhaseChanged += OnPhase;

        GameSounds.Music(GameSounds.FIGHT_MUSIC);
        GameSounds.Ambience(map.Ambience.Rate >= RAINY ? GameSounds.RAIN : null);
    }

    private float PanOf(float x)
    {
        float middle = (((IFighterBody)round.PlayerOf(0)).Position.X + ((IFighterBody)round.PlayerOf(1)).Position.X) / 2.0f;
        return Math.Clamp((x - middle) / PAN_REACH, -0.7f, 0.7f);
    }

    private void OnReported(AttackReport report)
    {
        IFighter victim = report.Attacker.Fight.OpponentOf(report.Attacker);
        float pan = PanOf(victim.Body.Position.X);

        switch (report.Result)
        {
            case HitResult.Hit:
            case HitResult.CounterHit:
            case HitResult.Punish:
                GameSounds.Hit(Math.Clamp(report.Move.Damage / HEAVIEST, 0.0f, 1.0f), pan);
                if (report.Result != HitResult.Hit) GameSounds.Counter(pan);
                break;

            case HitResult.Blocked:
                GameSounds.Block(pan);
                break;

            case HitResult.Parried:
                GameSounds.Parry(pan);
                break;

            case HitResult.Whiff:
                GameSounds.Whiff(PanOf(report.Attacker.Body.Position.X));
                break;
        }
    }

    private void OnPhase(RoundPhase phase)
    {
        MatchScore score = round.Score;

        switch (phase)
        {
            case RoundPhase.Ready:
                GameSounds.Call(IsDecider(score) ? "final_round" : $"round_{Math.Clamp(score.Round, 1, 5)}");
                break;

            case RoundPhase.Fight:
                GameSounds.Call("fight");
                break;

            case RoundPhase.RoundOver:
                if (round.TimedOut) GameSounds.Call("time");
                else GameSounds.KnockOut();

                if (round.LastOutcome == RoundOutcome.Draw) GameSounds.Call("tie");
                else if (WinnerUntouched()) GameSounds.Call("flawless_victory");
                break;

            case RoundPhase.MatchOver:
                GameSounds.Call("winner");
                break;
        }
    }

    // Both of them a round off the match, whoever takes this one takes it
    private bool IsDecider(MatchScore score)
    {
        int matchPoint = round.Rules.RoundsToWin - 1;
        return round.Rules.Rounds > 1 && score.Wins(0) == matchPoint && score.Wins(1) == matchPoint;
    }

    private bool WinnerUntouched()
    {
        Player? winner = round.LastOutcome switch
        {
            RoundOutcome.PlayerOne => round.PlayerOf(0),
            RoundOutcome.PlayerTwo => round.PlayerOf(1),
            _ => null
        };
        return winner is not null && winner.Health == Player.MAX_HEALTH;
    }

    public override void UpdateState(float dt)
    {
        for (int i = 0; i < 2; i++)
        {
            Player player = round.PlayerOf(i);
            if (player.Controller is null) continue;

            IFighterBody body = player;
            Vector2 velocity = body.Velocity;
            bool grounded = body.IsGrounded;

            // Coming down, as hard as the fall was going
            if (grounded && !_wasGrounded[i] && _falling[i] > QUIET_FALL)
                GameSounds.Land(PanOf(body.Position.X), Math.Clamp(_falling[i] / HARD_FALL, 0.0f, 1.0f));

            _falling[i] = grounded ? 0.0f : MathF.Max(_falling[i], -velocity.Y);
            _wasGrounded[i] = grounded;

            // A step every stride on the ground, none for somebody standing about or sliding off a hit
            if (grounded && MathF.Abs(velocity.X) > WALKING && player.Controller.IsInControl)
            {
                _stride[i] += MathF.Abs(velocity.X) * dt;
                if (_stride[i] >= STRIDE)
                {
                    _stride[i] -= STRIDE;
                    GameSounds.Step(PanOf(body.Position.X));
                }
            }
            else _stride[i] = STRIDE * 0.6f;
        }

        base.UpdateState(dt);
    }
}

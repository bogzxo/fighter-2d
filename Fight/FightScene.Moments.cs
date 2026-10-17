using Horizon.Core.Tweening;

namespace Fighter2D.Fight;

// The moments of a fight that want to be felt. A K.O. holds both fighters still for the hit (the ko_hitstop of the
// rules), then the launch plays out in slow motion and the fight comes back up to speed, the whole scene with it
// (SceneManager.TimeScale). Not online, the other machine would carry on at full speed and the two would part ways, and
// not under a pause, the pause menu would crawl.
internal partial class FightScene
{
    // How slow the launch of a K.O. starts out, and how long (of the fight's own, slowed, time) it takes to get back
    private const float KO_SLOW = 0.3f;
    private const float KO_SLOW_TIME = 0.4f;

    // How long since the K.O. landed, in the fight's own time, negative while there isn't one
    private float _knockOutTime = -1.0f;

    private void OnRoundPhase(RoundPhase phase)
    {
        if (phase == RoundPhase.RoundOver && !_round.TimedOut && Session is null) _knockOutTime = 0.0f;
    }

    private void UpdateKnockOut(float dt)
    {
        if (_knockOutTime < 0.0f) return;

        if (_pause is { HoldsFight: true })
        {
            EndKnockOut();
            return;
        }

        _knockOutTime += dt;

        // Full speed through the hitstop, slow once they fly, and back up to speed with most of it spent slow
        float t = (_knockOutTime - CombatRules.KO_HITSTOP * FightTicks.TICK_TIME) / KO_SLOW_TIME;
        if (t >= 1.0f)
        {
            EndKnockOut();
            return;
        }

        Engine.SceneManager.TimeScale = t < 0.0f ? 1.0f : float.Lerp(KO_SLOW, 1.0f, Ease.Apply(Easing.InCubic, t));
    }

    private void EndKnockOut()
    {
        _knockOutTime = -1.0f;
        Engine.SceneManager.TimeScale = 1.0f;
    }
}

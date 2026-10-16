using Horizon.Content;
using Horizon.Logging;
using System;
using System.Numerics;

using Horizon.Core.Tweening;
using Horizon.Engine;
using Horizon.Engine.Components;
using Horizon.Input;
using Horizon.Physics;
using Horizon.Rendering;
using Horizon.Rendering.PostProcessing;
using Horizon.Rendering.Spriting;

namespace Fighter2D.Fight;

// The pause, and the focus of the arena. The arena is smeared behind the versus screen and behind a pause (ours or
// the other machine's), the options go on top of the paused fight, and whatever changed under them is seen to on the way back
internal partial class FightScene
{
    /// <summary>
    /// Helper method to smear the arena for as long as there is a pause on screen, ours or the other machine's.
    /// </summary>
    private void UpdatePauseBlur()
    {
        bool paused = _pause is { IsShowing: true } || _awaitingPeer;
        if (paused == _pauseBlurred) return;

        _pauseBlurred = paused;

        if (paused) _backdropBlur.BlurTo(PAUSE_BLUR, PAUSE_BLUR_TIME);
        else _backdropBlur.BlurTo(0.0f, PAUSE_CLEAR_TIME);
    }

    private void OnPhaseChanged(RoundPhase phase)
    {
        // The versus screen is gone, the arena comes into focus. It only ever leaves that phase the once
        if (phase != RoundPhase.Versus && _backdropBlur.Radius >= VERSUS_BLUR)
        {
            _backdropBlur.BlurTo(0.0f, VERSUS_CLEAR_TIME, Easing.InOutSine);
        }
    }

    /// <summary>
    /// Opens the options menu in the game! The options go on top of the fight, which is kept underneath exactly as
    /// it is and comes back when they are done with (see <see cref="Scene.Open"/>). It is paused already, the pause
    /// menu is how anybody gets here, and it stays paused under there, for the other machine of an online fight as
    /// well. It still gets its updates so that machine keeps hearing from us, see <see cref="OnResumed"/> for the way back.
    /// </summary>
    private void OpenOptions()
    {
        // Drawn as well as updated, the options have the fight for a background, blurred (see OptionsScene)
        Open(new OptionsScene { OverScene = true }, Screen.BetweenMenus, SceneBelow.Running);
    }

    /// <summary>
    /// Back on screen after the options (or whatever else was put on top of the fight). Whatever was changed in
    /// the meantime is seen to here, the effects, the lighting, the hitboxes, and the size of the window, which the
    /// renderer and the camera were made for. The pause menu is still up, carrying on is the player's to press.
    /// </summary>
    protected override void OnResumed()
    {
        Vector2 viewport = Engine.WindowManager.ViewportSize;
        _renderer.Resize((uint)viewport.X, (uint)viewport.Y);
        _sceneCamera.ViewSize = viewport / 2.0f;
        _camera.Refit(viewport);

        Screen.ApplyOptions(_renderer, null, FightCamera.Zoom);
        _world.RenderDebug = GameOptions.Hitboxes;
    }
}

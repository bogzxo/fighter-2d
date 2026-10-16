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

// Reloading the data in the middle of a fight (the host tools of the pause menu, or Hex saving a file), and a fight
// that takes the place of another one carrying on from where it was. Online the host gets the other machine the
// files first and holds the reloaded fight until they are back in it
internal partial class FightScene
{
    /* Reloading the data in the middle of a fight */

    /// <summary>
    /// Called from the host tools of the pause menu. Everything the fight is made of (moves, characters, maps, art) is read from disk again.
    /// Online the other machine gets whatever changed first and then does the same, so both of them carry on with the same data.
    /// </summary>
    private void ReloadData()
    {
        if (_reloadWait > 0.0f) return;

        if (_network is null || Content is null)
        {
            ReloadNow();
            return;
        }

        // Offering the content again has the other machine fetch what is different, and say so once it has all of it
        Content.Offer();
        _reloadWait = RELOAD_PATIENCE;
        if (_transfer is not null) _transfer.Waiting = Localisation.Get("transfer.reloading");
    }

    /// <summary>
    /// Helper method for the host to wait until the other machine has the data, and to tell it to reload once it does.
    /// </summary>
    private void UpdateReload(float dt)
    {
        if (_reloadWait <= 0.0f || Content is null) return;

        if (Content.PeerVerified)
        {
            _network?.SendReload();
            ReloadNow();
            return;
        }

        // Files that are still going across are no reason to give up, only silence is
        if (!Content.IsTransferring) _reloadWait -= dt;
        if (_reloadWait > 0.0f) return;

        Log.Warning("[Fight] The other machine never said it has the data, nothing was reloaded.");
        if (_transfer is not null) _transfer.Waiting = string.Empty;
    }

    /// <summary>
    /// Helper method to put another character in the dummy's place. The fight is built again around it, from where it was.
    /// </summary>
    private void ChangeOpponent(string characterId)
    {
        if (OpponentChanged is null) return;

        OpponentChanged(characterId);
        ReloadNow();
    }

    /// <summary>
    /// Helper method to swap this fight for the same one built from what is on disk right now, carrying on from where this one is.
    /// </summary>
    private void ReloadNow()
    {
        if (Reload is null || _handedOver) return;

        Log.Info("[Fight] Reading the data again.");

        // What was worked out from the old files is no good for the new ones
        CharacterArt.Forget();

        var resume = new FightResume(_round.TakeSnapshot(), FighterResume.Of(_one), FighterResume.Of(_two));

        // The fight that takes over uses the same connection, so this one lets go of it without hanging up
        _network?.Detach();
        _handedOver = true;

        Engine.SceneManager.ResetTo(Reload(resume), null);
    }

    /// <summary>
    /// Helper method for a fight that takes the place of another one. The match goes back to how it stood,
    /// and the arena is in focus from the start because there is no versus screen to sit behind.
    /// </summary>
    private void ResumeMatch()
    {
        if (Resume is not { } resume) return;

        _round.Restore(resume.Round);
        _resumePending = true;

        if (resume.Round.Phase != RoundPhase.Versus)
        {
            _backdropBlur.Radius = 0.0f;
            _backdropBlur.Enabled = false;
        }

        // The host doesn't get a head start, it holds still until the other machine is back in the fight too
        _awaitingPeer = Session is { IsHost: true };
    }

    /// <summary>
    /// Helper method for the host to wait for the other machine after a reload. It asks every so often,
    /// and gives up waiting if they left or take forever.
    /// </summary>
    private void AwaitPeer(float dt)
    {
        _awaitTime += dt;

        bool done = _network is null || _network.PeerReloaded || !_network.IsConnected || _awaitTime >= RELOAD_PATIENCE;
        if (done)
        {
            if (_network is { PeerReloaded: false }) Log.Warning("[Fight] Never heard that the other machine is done reloading, carrying on anyway.");

            _awaitingPeer = false;
            if (_transfer is not null) _transfer.Waiting = string.Empty;

            // Both of us are back, three two one and off we go
            _pause?.StartCountdown();
            return;
        }

        if ((_askTimer -= dt) <= 0.0f)
        {
            _askTimer = RELOAD_ASK_INTERVAL;
            _network!.AskReloaded();
        }

        if (_transfer is not null)
        {
            _transfer.Waiting = Localisation.Get("transfer.reloaded");
            _transfer.WaitingDetail = Localisation.Get("transfer.other_machine_reloading");
            _transfer.WaitingProgress = 1.0f;
        }
    }

    /// <summary>
    /// Helper method to put the fighters back the way they were, once they have bodies to put anywhere.
    /// </summary>
    private void ResumeFighters()
    {
        if (Resume is not { } resume || _one.PhysicsBody is null || _two.PhysicsBody is null) return;
        _resumePending = false;

        Place(_one, resume.One);
        Place(_two, resume.Two);

        // That is us back in the fight, which the host is waiting to hear. It counts down from there, and so do we.
        // On one machine there is nobody to wait for
        if (Session is { IsHost: false }) _network?.SayReloaded();
        if (!_awaitingPeer) _pause?.StartCountdown();

        static void Place(Player player, FighterResume was)
        {
            player.Health = was.Health;
            player.PhysicsBody.Position = was.Position;
            player.Transform.Position = was.Position;
            player.Transform.Snap();
        }
    }
}

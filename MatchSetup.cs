using System;
using System.Collections.Generic;
using System.Text;

namespace Fighter2D;

internal enum MatchMode
{
    // One player against the dummy
    Practice,

    // Two players on two gamepads
    Pvp
}

/// <summary>
/// What the menus have settled on so far for the next fight, handed from one screen to the next.
/// </summary>
internal class MatchSetup(MatchMode mode)
{
    public MatchMode Mode { get; } = mode;

    public int PlayerCount => Mode == MatchMode.Pvp ? 2 : 1;

    // The slot of the gamepad every player has picked, player one first
    public List<int> Slots { get; } = [];

    public bool IsReady => Slots.Count >= PlayerCount;

    public string Title => Mode == MatchMode.Pvp ? "Player vs Player" : "Practice";
}

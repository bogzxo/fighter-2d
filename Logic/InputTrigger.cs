namespace Fighter2D.Logic;

/// <summary>
/// How the input signature of a move has to be entered for the move to start.
/// </summary>
public enum InputTrigger
{
    Held,      // for as long as it is held down (run, crouch, block)
    Pressed,   // once for every press, holding it down doesn't repeat the move
    DoubleTap  // once it has been pressed twice in a row
}

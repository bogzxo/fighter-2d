namespace Fighter2D.Logic;

/// <summary>
/// How the input of a move has to be entered for the move to come out.
/// </summary>
public enum InputTrigger
{
    Held,      // for as long as it is held down (run, crouch, block)
    Pressed,   // once per press, holding it down doesn't repeat the move
    DoubleTap  // pressed twice in a row (dodge roll)
}

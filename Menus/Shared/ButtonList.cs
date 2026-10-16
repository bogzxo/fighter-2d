using System;
using System.Collections.Generic;

using Button = Horizon.UI.Components.Button;

namespace Fighter2D.Scenes.Menus;

/// <summary>
/// A list of buttons a gamepad can walk up and down, one of them is always the selected one.
/// </summary>
internal sealed class ButtonList
{
    private readonly List<Button> _buttons = [];

    public int Selected { get; private set; }
    public int Count => _buttons.Count;

    public Button this[int index] => _buttons[index];

    public void Add(Button button) => _buttons.Add(button);

    /// <summary>
    /// Selects a button and lights it up. Going past either end of the list stops at the end.
    /// </summary>
    public void Select(int index)
    {
        if (_buttons.Count == 0) return;

        Selected = Math.Clamp(index, 0, _buttons.Count - 1);

        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Selected = i == Selected;
        }
    }

    public void Move(int by)
    {
        if (by != 0) Select(Selected + by);
    }

    /// <summary>
    /// Does whatever clicking the selected button would do.
    /// </summary>
    public void Press()
    {
        if (_buttons.Count > 0) _buttons[Selected].OnPressed?.Invoke();
    }
}

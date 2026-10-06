using System;
using System.Collections.Generic;
using System.Text;

namespace Fighter2D.Logic
{
    [Flags]
    public enum InputFlags
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        X = 1 << 2,
        Y = 1 << 3,
        DPadUp = 1 << 4,
        DPadDown = 1 << 5,
        DPadLeft = 1 << 6,
        DPadRight = 1 << 7,
        RightBumper = 1 << 8,
        LeftBumper = 1 << 9,
        LeftTrigger = 1 << 10,
        RightTrigger = 1 << 11
    }
}

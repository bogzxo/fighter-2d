using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

namespace Fighter2D.Physics;

internal interface IPhysicsFixture
{
    public PhysicsBodyComponent2D Parent { get; init; }
    public PhysicsFixtureShape Shape { get; init; }
    public bool TestIntersection(in IPhysicsFixture other);
}

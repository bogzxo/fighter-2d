using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

using Horizon.Core;
using Horizon.Core.Components;

namespace Fighter2D.Physics;

internal class PhysicsBodyComponent2D : IGameComponent
{
    public PhysicsBodySimulationType SimulationType { get; init; }
    public ConcurrentBag<IPhysicsFixture> Fixtures { get; init; } = [];
    public Vector2 Position { get; set; }

    public bool Enabled { get; set; }
    public string Name { get; set; } = "Physics Body";
    public Entity Parent { get; set; }

    private TransformComponent2D parentTransform;

    public PhysicsBodyComponent2D( Vector2 initialPosition)
    {
        this.Position = initialPosition;
    }
    public PhysicsBodyComponent2D() : this(Vector2.Zero) { }

    public void Initialize()
    {
        parentTransform = Parent.GetComponent<TransformComponent2D>();
    }

    public RectanglePhysicsFixture CreateRectangularFixture(Vector2 position, Vector2 size)
    {
        var rf = new RectanglePhysicsFixture(this, position, size);
        this.Fixtures.Add(rf);
        return rf;
    }

    public CirclePhysicsFixture CreateCircleFixture(Vector2 position, float radius)
    {
        var cf = new CirclePhysicsFixture(this, radius, position);
        this.Fixtures.Add(cf);
        return cf;
    }

    // TODO: implement methods to apply implulses and forces


    public void Render(float dt, object? obj = null)
    {
        parentTransform.Position = Position;
    }

    public void UpdatePhysics(float dt)
    {

    }

    public void UpdateState(float dt)
    {
    }
}
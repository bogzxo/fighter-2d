using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

using Horizon.Core;
using Horizon.Core.Components;

namespace Fighter2D.Physics;

internal class PhysicsBody : IGameComponent
{
    public PhysicsBodySimulationType SimulationType { get; init; }
    public IPhysicsFixture[] Fixtures { get; set; }
    public Vector2 Position { get; init; }

    public bool Enabled { get; set; }
    public string Name { get; set; } = "Physics Body";
    public Entity Parent { get; set; }

    private TransformComponent2D parentTransform;

    public PhysicsBody( Vector2 initialPosition)
    {
        this.Position = initialPosition;
    }
    public PhysicsBody() : this(Vector2.Zero) { }

    public void Initialize()
    {
        parentTransform = Parent.GetComponent<TransformComponent2D>();
    }

    public void Render(float dt, object? obj = null)
    {

    }

    public void UpdatePhysics(float dt)
    {

    }

    public void UpdateState(float dt)
    {
        
    }
}
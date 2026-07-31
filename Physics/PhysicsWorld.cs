using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

using Box2D.NetStandard.Dynamics.Bodies;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Rendering;

namespace Fighter2D.Physics;

internal class PhysicsWorld : IGameComponent
{
    public bool RenderDebug { get; set; } = true;
    public ConcurrentBag<PhysicsBody> StaticBodies { get; init; } = new();
    public ConcurrentBag<PhysicsBody> DynamicBodies { get; init; } = new();

    public bool Enabled { get; set; }
    public string Name { get; set; } = "Physics World";
    public Entity Parent { get; set; }

    private Box2DDebugRendererComponent debugRenderer = new();

    public void AddBody(in PhysicsBody body)
    {
        switch (body.SimulationType)
        {
            case PhysicsBodySimulationType.Static:
                this.StaticBodies.Add(body);
                break;
            case PhysicsBodySimulationType.Dynamic:
                this.DynamicBodies.Add(body);
                break;
        }
    }

    public void Initialize()
    {
        debugRenderer.Initialize();
    }
    public void UpdatePhysics(float dt)
    {

    }
    public void UpdateState(float dt)
    {

    }
    public void Render(float dt, object? obj = null)
    {
        void drawBody(in PhysicsBody body, Vector4 colour)
        {
            foreach (var fixture in body.Fixtures)
            {
                if (fixture is CirclePhysicsFixture c)
                {
                    debugRenderer.DrawCircle(c.Position, c.Radius, colour);
                }
                else if (fixture is RectanglePhysicsFixture r)
                {
                    debugRenderer.DrawPolygon(new Vector2[] {
                            new Vector2(r.Bounds.Left, r.Bounds.Top),
                            new Vector2(r.Bounds.Right, r.Bounds.Top),
                            new Vector2(r.Bounds.Right, r.Bounds.Bottom),
                            new Vector2(r.Bounds.Left, r.Bounds.Bottom),
                        }, colour);
                }
            }
        }

        if (RenderDebug)
        {
            debugRenderer.ClearBuffers();

            foreach (var body in StaticBodies)
            {
                drawBody(body, new System.Numerics.Vector4(1, 0, 0, 1));
            }
            foreach (var body in DynamicBodies)
            {
                drawBody(body, new System.Numerics.Vector4(0, 0, 1, 1));
            }


            debugRenderer.Render(dt);
        }
    }
}

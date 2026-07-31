using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

namespace Fighter2D.Physics;

internal struct PhysicsRectangle
{
    public Vector2 Position { get; init; }
    public Vector2 Size { get; init; }

    public readonly float X => Position.X;
    public readonly float Y => Position.Y;
    public readonly float Width => Size.X;
    public readonly float Height => Size.Y;

    public readonly float Left => Position.X;
    public readonly float Right => Position.X + Size.X;
    public readonly float Top => Position.Y;
    public readonly float Bottom => Position.Y + Size.Y;

    public PhysicsRectangle(float x, float y, float w, float h)
    {
        this.Position = new Vector2(x, y);
        this.Size = new Vector2(w, h);
    }

    public PhysicsRectangle(Vector2 position, Vector2 size)
    {
        this.Position = position;
        this.Size = size;
    }
}

internal enum PhysicsBodySimulationType
{
    Static,
    Dynamic
}

internal enum PhysicsFixtureShape
{
    Rectangle,
    Circle
}

internal interface IPhysicsFixture
{
    public PhysicsFixtureShape Shape { get; init; }
    public bool TestIntersection(in IPhysicsFixture other);
}

internal class CirclePhysicsFixture(float radius, Vector2 position) : IPhysicsFixture
{
    public float Radius { get; init; } = radius;
    public Vector2 Position { get; init; } = position;
    public PhysicsFixtureShape Shape { get; init; } = PhysicsFixtureShape.Circle;

    public bool TestIntersection(in IPhysicsFixture other)
    {
        switch (other.Shape)
        {
            case PhysicsFixtureShape.Circle:
                {
                    var circle = (CirclePhysicsFixture)other;

                    float radiusSum = this.Radius + circle.Radius;
                    float distanceSquared = Vector2.DistanceSquared(this.Position, circle.Position);

                    return distanceSquared <= (radiusSum * radiusSum);
                }
            case PhysicsFixtureShape.Rectangle:
                {
                    var rect = (RectanglePhysicsFixture)other;
                    return IntersectsCircleAndRectangle(this, rect.Bounds);
                }
            default:
                return false;
        }
    }
    // Stack allocation free test
    internal static bool IntersectsCircleAndRectangle(CirclePhysicsFixture circle, PhysicsRectangle rect)
    {
        // Clamp the circle center to the bounds of the rectangle to find the closest point
        float closestX = Math.Clamp(circle.Position.X, rect.Left, rect.Right);
        float closestY = Math.Clamp(circle.Position.Y, rect.Top, rect.Bottom);

        // distanec from circle center to closest point on rectangle
        float deltaX = circle.Position.X - closestX;
        float deltaY = circle.Position.Y - closestY;

        float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        return distanceSquared <= (circle.Radius * circle.Radius);
    }
}

internal class RectanglePhysicsFixture : IPhysicsFixture
{
    public PhysicsFixtureShape Shape { get; init; } = PhysicsFixtureShape.Rectangle;

    public PhysicsRectangle Bounds { get; init; }

    public Vector2 Position => Bounds.Position;
    public Vector2 Size => Bounds.Size;

    public RectanglePhysicsFixture(Vector2 position, Vector2 size)
    {
        this.Bounds = new PhysicsRectangle(position, size);
    }

    public bool TestIntersection(in IPhysicsFixture other)
    {
        switch (other.Shape)
        {
            case PhysicsFixtureShape.Rectangle:
                {
                    var rect = (RectanglePhysicsFixture)other;

                    // AABB vs AABB
                    return this.Bounds.Left < rect.Bounds.Right &&
                           this.Bounds.Right > rect.Bounds.Left &&
                           this.Bounds.Top < rect.Bounds.Bottom &&
                           this.Bounds.Bottom > rect.Bounds.Top;
                }
            case PhysicsFixtureShape.Circle:
                {
                    var circle = (CirclePhysicsFixture)other;
                    return CirclePhysicsFixture.IntersectsCircleAndRectangle(circle, this.Bounds);
                }
            default:
                return false;
        }
    }
}

internal class PhysicsBody
{
    public PhysicsBodySimulationType SimulationType { get; init; }
    public IPhysicsFixture[] Fixtures { get; init; }
}
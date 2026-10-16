using System.Numerics;

namespace Fighter2D.Simulation;

/// <summary>
/// The eye candy the simulation asks for, the dust, the blood, the sparks and the haze. The fight's <see cref="FightEffects"/>
/// is one of these and makes particles, a test's does nothing at all. None of it changes the fight.
/// </summary>
internal interface IFightEffects
{
    void Shockwave(Vector2 position, float radius, float strength);
    void Hit(Vector2 position, float direction, bool heavy);
    void Block(Vector2 position, float direction);
    void Dust(Vector2 position, float direction, int count = 6);
    void Land(Vector2 position, float fallDuration);
    void Haze(Vector2 position, float frameRate);
}

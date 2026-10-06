namespace Fighter2D.HUD;

/// <summary>
/// One part of the HUD of a fight: it finds its components in the layout when it is made and from then on only
/// writes onto them what the fight is doing, once per update. The HUDManager keeps a list of these and knows nothing else about them.
/// </summary>
internal interface IHudDisplay
{
    void Update(float dt);
}

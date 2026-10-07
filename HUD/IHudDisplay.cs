namespace Fighter2D.HUD;

/// <summary>
/// One part of the HUD of a fight. It finds its components in the layout when it is made, and from then on only writes onto them what the fight is doing.
/// The HUDManager keeps a list of these and knows nothing else about them. To add something to the HUD make one of these and add it to that list.
/// </summary>
internal interface IHudDisplay
{
    void Update(float dt);
}

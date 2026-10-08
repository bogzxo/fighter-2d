
namespace Fighter2D.Moves;

/// <summary>
/// The eye candy a phase of a move can ask for by name. None of it changes the fight, it only looks nice.
/// To add one give it a name here, a case in Play and a mention in the header of fighting_moves.hor.
/// </summary>
internal static class MoveEffects
{
    private const string JUMP = "jump";
    private const string ROLL = "roll";
    private const string HAZE = "haze";

    /// <summary>
    /// Whether there is an effect by this name, so the move files can be told off for asking for one that doesn't exist.
    /// </summary>
    public static bool Exists(string effect) => effect is JUMP or ROLL or HAZE;

    public static void Play(string effect, Player player)
    {
        switch (effect)
        {
            case JUMP:
                Fight.Effects.Dust(player.FeetPosition, 0);
                Fight.Effects.Shockwave(player.FeetPosition, 40, 45);
                break;

            case ROLL:
                // A trail of dust behind us. This comes every frame of the roll so each puff has to be a small one
                Fight.Effects.Dust(player.FeetPosition, -player.Facing, 3);
                Fight.Effects.Shockwave(player.FeetPosition, 60, 30);
                break;

            case HAZE:
                // The little cloud around the head of somebody who just got their bell rung
                Fight.Effects.Haze(player.HeadPosition, player.Character.FrameRate);
                break;
        }
    }
}

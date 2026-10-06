using Fighter2D.Character;
using Fighter2D.Match;
using Fighter2D.Scenes;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The two fighters shown off against each other before the first round: who is playing whom, and what the match is played by.
/// It is up for as long as the RoundDirector is in its versus phase and gone for the rest of the fight.
/// </summary>
internal sealed class VersusDisplay : IHudDisplay
{
    private const string PORTRAIT_ANIMATION = "idle";
    private const string LAYER = "versus";

    private readonly RoundDirector _round;
    private readonly UIModule _module;
    private readonly CharacterPortrait[] _portraits;

    public VersusDisplay(UILayout layout, RoundDirector round)
    {
        _round = round;
        _module = layout.Module;

        // Player two looks back at player one, the art of every character faces right
        _portraits =
        [
            new CharacterPortrait(layout.Get<Image>("versus_p1")),
            new CharacterPortrait(layout.Get<Image>("versus_p2")) { Mirrored = true }
        ];

        Label[] names = [layout.Get<Label>("versus_p1_name"), layout.Get<Label>("versus_p2_name")];

        for (int i = 0; i < _portraits.Length; i++)
        {
            // The players are set up before the HUD is, so who they play is known by now
            CharacterDefinition character = round.PlayerOf(i).Character ?? CharacterDefinition.LoadDefault();

            _portraits[i].Show(character, PORTRAIT_ANIMATION);
            names[i].Text = character.PrettyName;
        }

        MatchRules rules = round.Rules;
        layout.Get<Label>("versus_rules").Text = $"{MatchRules.DescribeRounds(rules.RoundsToWin)}\n{MatchRules.DescribeTime(rules.RoundSeconds)}";
    }

    public void Update(float dt)
    {
        // Everything of the display is on a layer of its own in the layout, which is all there is to switch
        bool showing = _round.Phase == RoundPhase.Versus;
        _module.SetLayerVisible(LAYER, showing);

        if (!showing) return;

        foreach (CharacterPortrait portrait in _portraits)
        {
            portrait.Update(dt);
        }
    }
}


using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.Scenes.Widgets;

/// <summary>
/// The big preview on the left of the character select screen. Shows whoever is being looked at along with their name and stat bars.
/// </summary>
internal sealed class CharacterPreview
{
    private const string ANIMATION = "run_loop";

    private readonly CharacterPortrait _portrait;
    private readonly Label _name;
    private readonly ProgressBar _speed, _health, _bulk, _complexity;

    // Who is being shown right now, so we only switch when it actually changes
    private CharacterDefinition? _shown;

    public CharacterPreview(UILayout layout)
    {
        _portrait = new CharacterPortrait(layout.Get<Image>("preview"));
        _name = layout.Get<Label>("preview_name");
        _speed = layout.Get<ProgressBar>("speed");
        _health = layout.Get<ProgressBar>("health");
        _bulk = layout.Get<ProgressBar>("bulk");
        _complexity = layout.Get<ProgressBar>("complexity");
    }

    public void Show(CharacterDefinition character)
    {
        if (ReferenceEquals(character, _shown)) return;
        _shown = character;

        _portrait.Show(character, ANIMATION);
        _portrait.Image.Punch(0.06f, 0.25f);

        _name.Text = character.PrettyName;
        _speed.Progress = character.Speed;
        _health.Progress = character.Health;
        _bulk.Progress = character.Bulk;
        _complexity.Progress = character.Complexity;
    }

    public void Update(float dt) => _portrait.Update(dt);
}

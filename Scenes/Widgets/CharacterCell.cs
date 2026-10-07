using System.Numerics;

using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

using Button = Horizon.Rendering.UIX.Components.Button;

namespace Fighter2D.Scenes.Widgets;

/// <summary>
/// One cell of the character select grid, a button with a character in it (or a question mark if there is nobody for it).
/// The cell itself comes out of Assets/ui/layouts/character_cell.hor.
/// </summary>
internal sealed class CharacterCell
{
    public Button Box { get; }
    public CharacterDefinition? Character { get; }

    private readonly Label _tags;
    private readonly CharacterPortrait? _portrait;

    public CharacterCell(UILayout item, CharacterDefinition? character)
    {
        Character = character;

        // A button so it lights up like one
        Box = item.Get<Button>("box");
        Box.Enabled = character is not null;
        _tags = item.Get<Label>("tags");

        var portrait = item.Get<Image>("portrait");
        var name = item.Get<Label>("name");

        // The cells there is nobody for show their question mark instead
        portrait.Visible = name.Visible = character is not null;
        item.Get<Label>("empty").Visible = character is null;

        if (character is null) return;

        _portrait = new CharacterPortrait(portrait);
        _portrait.Show(character);

        name.Text = character.PrettyName;
    }

    /// <summary>
    /// Shows which players are standing on the cell (as "P1 P2"), lit up if anybody is.
    /// </summary>
    public void ShowPlayers(string tags, Vector4 color)
    {
        Box.Selected = tags.Length > 0;
        _tags.Text = tags;
        _tags.Color = color;
    }

    public void Update(float dt) => _portrait?.Update(dt);
}

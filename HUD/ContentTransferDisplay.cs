using System.Numerics;

using Fighter2D.Networking;
using Fighter2D.Scenes;

using Horizon.Core;
using Horizon.Core.Components;
using Horizon.Core.Threading;
using Horizon.Engine;
using Horizon.Rendering.UIX;
using Horizon.Rendering.UIX.Components;

namespace Fighter2D.HUD;

/// <summary>
/// The little panel with a progress bar that comes up while the game files go from the host to the other machine.
/// That happens in the lobby when somebody joins with different files, and in the middle of a fight when the host reloads its data.
/// It shows itself whenever files are on the move and goes away by itself a moment after the last of them arrived.
/// It is laid out in Assets/ui/layouts/content_transfer.hor and is the same thing on both machines, only the wording differs.
/// </summary>
internal sealed class ContentTransferDisplay(ContentSync content, bool hosting) : GameComponent
{
    // How long (in seconds) the full bar stays up once everything has arrived, so it can be seen to have finished
    private const float LINGER_TIME = 0.7f;

    private const float MEGABYTE = 1024.0f * 1024.0f;

    private static readonly Vector4 ErrorColor = new(1.0f, 0.45f, 0.4f, 1.0f);
    private static readonly Vector4 DetailColor = new(0.58f, 0.6f, 0.66f, 1.0f);

    private UICompositor _compositor = null!;
    private Label _title = null!, _detail = null!;
    private ProgressBar _bar = null!;

    // Whether the panel is up, as of every tick: what the frames drawn alongside the simulation go by
    private readonly Snapshot<bool> _shown = new();

    private float _linger;
    private bool _wasTransferring;

    /// <summary>
    /// What the panel says it is waiting for while nothing is on the move yet, empty for nothing.
    /// For whoever knows that files are about to be (the host that just asked for a reload).
    /// </summary>
    public string Waiting { get; set; } = string.Empty;

    /// <summary>
    /// The line under the bar and how full the bar is while the panel is only waiting.
    /// </summary>
    public string WaitingDetail { get; set; } = "checking what the other machine has";
    public float WaitingProgress { get; set; }

    private bool Failed => content.Phase == ContentPhase.Failed;

    /// <summary>
    /// Whether the panel is on screen right now.
    /// </summary>
    public bool IsShowing => content.IsTransferring || _linger > 0.0f || Waiting.Length > 0 || Failed;

    public override void Initialize()
    {
        var camera = new Camera2D(GameEngine.Instance.WindowManager.ViewportSize);

        (UILayout layout, _compositor) = MenuLayouts.Load(camera, MenuLayouts.CONTENT_TRANSFER);
        _compositor.Initialize();

        _title = layout.Get<Label>("title");
        _detail = layout.Get<Label>("detail");
        _bar = layout.Get<ProgressBar>("bar");
    }

    public override void UpdateState(float dt)
    {
        bool transferring = content.IsTransferring;

        // The last file just landed, the bar stays up full for a moment
        if (_wasTransferring && !transferring) _linger = LINGER_TIME;
        else if (!transferring) _linger = MathF.Max(0.0f, _linger - dt);
        _wasTransferring = transferring;

        if (!IsShowing) return;

        Describe(transferring);
        _compositor.UpdateState(dt);
    }

    /// <summary>
    /// Helper method to write what is going on into the panel.
    /// </summary>
    private void Describe(bool transferring)
    {
        _detail.Color = DetailColor;

        if (Failed)
        {
            _title.Text = "That went wrong";
            _detail.Text = content.Error;
            _detail.Color = ErrorColor;
            _bar.Progress = 0.0f;
        }
        else if (transferring)
        {
            _title.Text = hosting ? "Sending the game files" : "Getting the host's game files";
            _detail.Text = $"{content.DoneBytes / MEGABYTE:0.0} of {content.TotalBytes / MEGABYTE:0.0} MB";
            _bar.Progress = content.Progress;
        }
        else if (_linger > 0.0f)
        {
            _title.Text = hosting ? "Sent the game files" : "Got the host's game files";
            _detail.Text = "all there";
            _bar.Progress = 1.0f;
        }
        else
        {
            _title.Text = Waiting;
            _detail.Text = WaitingDetail;
            _bar.Progress = WaitingProgress;
        }
    }

    public override void Capture()
    {
        _shown.Publish(IsShowing);
        _compositor.Capture();
    }

    public override void Render(float dt)
    {
        RenderFrame frame = RenderFrame.Active;
        bool shown = frame.IsDecoupled ? _shown.TryGet(frame, out bool up) && up : IsShowing;

        if (shown) _compositor.Render(dt);
    }
}

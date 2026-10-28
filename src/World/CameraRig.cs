using Godot;
using ArcPrefabs.Core;

namespace ArcPrefabs.World;

/// <summary>
/// The fixed-yaw, tilted follow camera: about 55° down, smoothed, leading a little in the
/// direction the target is moving. The mouse wheel, +/- or the shoulder buttons zoom between
/// <see cref="MinDistance"/> and <see cref="MaxDistance"/>; the choice is kept across restarts.
/// </summary>
public partial class CameraRig : Node3D
{
    private readonly Camera3D _camera = new() { Fov = 40f, Near = 0.5f, Far = 200f };
    public const float DefaultDistance = 19f;
    public const float MinDistance = 11f;
    public const float MaxDistance = 34f;
    private const float ZoomStep = 1.12f; // per wheel notch or key press

    private static float? _kept; // the player's zoom, kept across restarts in one session

    private Vector3 _lead;
    private Vector3 _lastTarget;
    private float _shown;

    public Node3D? Target { get; set; }
    public float Pitch { get; set; } = 55f;

    /// <summary>The distance the camera eases toward; set from the command line or by zooming.</summary>
    public float Distance { get; set; } = _kept ?? DefaultDistance;
    public float LeadAmount { get; set; } = 0.35f;
    public float Smoothing { get; set; } = 6f;

    public Camera3D Camera => _camera;

    /// <summary>How much more ground the camera shows than at the default distance (never below 1).</summary>
    public float Reach => Mathf.Max(1f, _shown / DefaultDistance);

    public override void _Ready()
    {
        _shown = Distance;
        AddChild(_camera);
        _camera.MakeCurrent();
        if (Target != null)
        {
            _lastTarget = Target.GlobalPosition;
            GlobalPosition = _lastTarget;
        }
        Place();
    }

    public override void _Process(double delta)
    {
        if (Target == null)
            return;

        var dt = (float)delta;
        var target = Target.GlobalPosition;
        var velocity = dt > 0f ? (target - _lastTarget) / dt : Vector3.Zero;
        _lastTarget = target;
        _lead = _lead.Lerp(velocity * LeadAmount, 1f - Mathf.Exp(-3f * dt));
        GlobalPosition = GlobalPosition.Lerp(target + _lead, 1f - Mathf.Exp(-Smoothing * dt));
        _shown = Mathf.Lerp(_shown, Distance, 1f - Mathf.Exp(-10f * dt));
        Place();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        var step = e.IsActionPressed(Controls.ZoomIn, true) ? 1f / ZoomStep : e.IsActionPressed(Controls.ZoomOut, true) ? ZoomStep : 0f;
        if (step == 0f)
            return;
        // Zooming can leave the command line's close-up range, but not go further out.
        Distance = Mathf.Clamp(Distance * step, Mathf.Min(MinDistance, Distance), MaxDistance);
        _kept = Distance;
        GetViewport().SetInputAsHandled();
    }

    private void Place()
    {
        var pitch = Mathf.DegToRad(Pitch);
        _camera.Position = new Vector3(0f, Mathf.Sin(pitch) * _shown, Mathf.Cos(pitch) * _shown);
        _camera.Rotation = new Vector3(-pitch, 0f, 0f);
    }
}

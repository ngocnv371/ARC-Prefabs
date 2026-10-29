using Godot;
using ArcPrefabs.Render;

namespace ArcPrefabs.World;

/// <summary>
/// Small pictures of Synty props and set pieces for the painter's lists. Each is a SubViewport
/// with a world of its own that draws once, through an orthographic camera, with the same ARC
/// materials as the game. Shots are kept by key and redrawn only when their stamp changes.
/// </summary>
public partial class ThumbnailStudio : Node
{
    /// <summary>The size of the picture saved next to each piece's JSON.</summary>
    public static readonly Vector2I PieceImage = new(1024, 768);

    private readonly Dictionary<string, (object? Stamp, SubViewport View)> _shots = [];

    public required WorldMap Map { get; init; }

    /// <summary>One prop at <paramref name="scale"/> (before its pack's unit), seen three-quarters on and filling the frame.</summary>
    public Texture2D Prop(string mesh, float scale, Vector2I size, Color background)
    {
        var key = $"prop:{mesh}";
        if (Cached(key, scale, out var texture))
            return texture;

        var view = Studio(size, background);
        var node = Place(view, mesh, Vector2.Zero, Mathf.DegToRad(-30f), scale);
        var box = node != null ? node.Transform * SyntyModels.Bounds(node) : new Aabb(Vector3.Zero, Vector3.One);
        // Fit the box's height and its widest footprint; ortho size is the frame's height.
        var aspect = (float)size.X / size.Y;
        var extent = Mathf.Max(box.Size.Y, Mathf.Max(box.Size.X, box.Size.Z) / aspect);
        Shoot(view, box.GetCenter(), 30f, extent * 1.15f);
        _shots[key] = (scale, view);
        return view.GetTexture();
    }

    /// <summary>A set piece on its biome's grass from the game camera's angle, framing its clear circle.</summary>
    public Texture2D Piece(SetPiece piece, Biome biome, object stamp, Vector2I size)
    {
        var key = $"piece:{piece.Id}";
        if (Cached(key, stamp, out var texture))
            return texture;

        var view = Stage(piece, biome, size);
        Frame(view, piece);
        _shots[key] = (stamp, view);
        return view.GetTexture();
    }

    /// <summary>
    /// Renders <paramref name="piece"/> as <see cref="Piece"/> does, at <paramref name="size"/>, and
    /// writes it to <paramref name="path"/> as a PNG (next to the piece's JSON, for tools that look
    /// at pieces without Godot). Completes once the file is written.
    /// </summary>
    public async Task SavePiece(SetPiece piece, Biome biome, string path, Vector2I size)
    {
        var view = Stage(piece, biome, size);
        view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Frame(view, piece);
        // A few frames, so the first draw's shader compiles have settled.
        for (var i = 0; i < 3; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = view.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(path));
        if (error != Error.Ok)
            GD.PushWarning($"could not write {path}: {error}");
        view.QueueFree();
    }

    /// <summary>A studio holding the piece's props on a plane of its biome's grass.</summary>
    private SubViewport Stage(SetPiece piece, Biome biome, Vector2I size)
    {
        var view = Studio(size, Rgb(biome.Deep));
        view.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One * piece.Clear * 6f },
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Rgb(biome.Lush) },
        });
        foreach (var prop in piece.Props)
            Place(view, prop.Mesh, prop.Offset, prop.Rotation, prop.Scale);
        return view;
    }

    // Ground circles foreshorten by sin(pitch), so a little over the clear radius frames them;
    // aiming a couple of metres up keeps tree tops in.
    private void Frame(SubViewport view, SetPiece piece) => Shoot(view, new Vector3(0f, 2f, 0f), 55f, piece.Clear * 1.55f);

    /// <summary>Drops a set piece's shot (its file is gone).</summary>
    public void ForgetPiece(string id)
    {
        if (_shots.Remove($"piece:{id}", out var shot))
            shot.View.QueueFree();
    }

    private bool Cached(string key, object? stamp, out Texture2D texture)
    {
        texture = null!;
        if (!_shots.TryGetValue(key, out var shot))
            return false;
        if (Equals(shot.Stamp, stamp))
        {
            texture = shot.View.GetTexture();
            return true;
        }
        shot.View.QueueFree();
        _shots.Remove(key);
        return false;
    }

    private static SubViewport Studio(Vector2I size, Color background)
    {
        var view = new SubViewport { Size = size, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Msaa3D = Viewport.Msaa.Msaa4X };
        view.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = background,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        return view;
    }

    private Node3D? Place(SubViewport view, string mesh, Vector2 offset, float rotation, float scale)
    {
        var (atlas, unit) = Map.Pack(mesh);
        if (SyntyModels.Instance(mesh, atlas, scenery: true) is not { } node)
            return null;
        node.Position = new Vector3(offset.X, 0f, offset.Y);
        node.Rotation = new Vector3(0f, rotation, 0f);
        node.Scale = Vector3.One * scale * unit;
        view.AddChild(node);
        return node;
    }

    /// <summary>Points an orthographic camera at <paramref name="target"/> from <paramref name="pitch"/> degrees up and starts the view drawing.</summary>
    private void Shoot(SubViewport view, Vector3 target, float pitch, float height)
    {
        var p = Mathf.DegToRad(pitch);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = height, Near = 0.5f, Far = 400f };
        camera.Position = target + new Vector3(0f, Mathf.Sin(p), Mathf.Cos(p)) * 100f;
        camera.Rotation = new Vector3(-p, 0f, 0f);
        view.AddChild(camera);
        AddChild(view);
        camera.MakeCurrent();
    }

    private static Color Rgb(Vector3 c) => new(c.X, c.Y, c.Z);
}

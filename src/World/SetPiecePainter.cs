using Godot;
using ArcPrefabs.Core;
using ArcPrefabs.Render;
using ArcPrefabs.UI;

namespace ArcPrefabs.World;

/// <summary>
/// The set piece painter (ARC-Prefabs's <c>run.cmd [name]</c>): the field of one biome without its
/// scatter, a palette of the props data/biomes.json allows (so only meshes that suit the ARC
/// shader), and the game's camera, so a piece looks as it will in a run. Props sit around the
/// region's centre, which becomes the piece's centre; saving writes data/setpieces/name.json.
/// Controls: WASD pans, wheel zooms, Tab switches between painting and selecting, R random turn
/// and scale, G snaps to half a metre, ctrl+Z undoes, ctrl+S saves, B shows or hides the
/// <see cref="SetPieceBrowser"/>. Painting: click places, shift-drag paints, right-click (or drag)
/// erases, alt-click picks up a placed prop's mesh, rotation and scale, Q/E turn and Z/X scale
/// the brush. Selecting: click a prop (its box, not just its centre), shift-click to add or drop
/// one, or drag a box over open ground; drag the selection to move it, Q/E turn it (shift: 1°) and
/// Z/X scale it, a group about its middle; Delete removes it, ctrl+D duplicates it, Esc drops it.
/// The palette is on the left; the piece's name, actions and the browser are on the right.
/// </summary>
public partial class SetPiecePainter : Node3D
{
    private const float PanSpeed = 1.2f; // camera distances per second
    private const float TurnStep = 15f;
    private const float ScaleStep = 1.08f;
    private const float Snap = 0.5f;
    private const float PanelWidth = 320f;
    private const float NewPieceClear = 12f; // the circle a new piece's view fits, about a typical piece's
    private const float FitMargin = 1.08f; // a little ground beyond the clear circle
    private static readonly Vector2I PropThumb = new(168, 168); // drawn at half size, sharp on high-DPI
    private static readonly Color PropBackground = new(0.13f, 0.11f, 0.2f);

    private readonly List<SetPieceProp> _props = [];
    private readonly List<Node3D?> _nodes = [];
    private readonly Stack<SetPieceProp[]> _undo = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly Node3D _cursor = new() { Name = "Cursor" };
    private readonly Node3D _piece = new() { Name = "Piece" };
    private readonly MeshInstance3D _lines = new() { Name = "Lines", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly ImmediateMesh _lineMesh = new();
    private CameraRig _rig = null!;
    private Field? _field;
    private Node3D? _ghost;

    private Biome _biome = null!;
    private BiomeProp[] _palette = [];
    private int _selected;
    private float _rotation;
    private float _scale = 1f;
    private bool _random = true;
    private bool _snap;
    private float _weight = 1f;
    private Vector2 _centre;
    private Vector2? _pointer; // the mouse in the viewport, from input events (so injected ones count)
    private Vector2? _mouse; // ground point under the mouse, relative to the centre
    private Vector2? _lastStroke;

    // Select mode: the chosen props by index, a move in progress (where it started and the
    // props before it), a box being dragged out, and each mesh's box for picking by ray.
    private readonly HashSet<int> _picked = [];
    private readonly Dictionary<string, Aabb> _meshBounds = [];
    private bool _selecting;
    private Vector2? _moveFrom;
    private SetPieceProp[]? _moveStart;
    private bool _moved;
    private Vector2? _boxFrom;
    private bool _boxAdds;
    private bool _dirty;
    private string _message = "";
    private string? _confirmOpen;

    private PanelContainer _panel = null!; // the palette, on the left
    private PanelContainer _piecePanel = null!; // the piece's name, actions and the browser, on the right
    private SetPieceBrowser _browser = null!;
    private ThumbnailStudio _studio = null!;
    private LineEdit _name = null!;
    private OptionButton _biomes = null!;
    private CheckBox _all = null!;
    private CheckBox _turn = null!;
    private ItemList _list = null!;
    private Button _selectMode = null!;
    private Label _status = null!;

    public required WorldMap Map { get; init; }

    /// <summary>A piece to open (a file name under data/setpieces without .json).</summary>
    public string? Open { get; init; }

    /// <summary>The biome to start a new piece in.</summary>
    public string? Biome { get; init; }

    public override void _Ready()
    {
        _rng.Randomize();
        AddChild(_cursor);
        AddChild(_piece);
        _lines.Mesh = _lineMesh;
        _lines.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };
        AddChild(_lines);
        _rig = new CameraRig { Name = "CameraRig", Target = _cursor, LeadAmount = 0f, Distance = 26f, ZoomLimit = 80f };
        AddChild(_rig);
        _studio = new ThumbnailStudio { Name = "Thumbnails", Map = Map };
        AddChild(_studio);
        BuildUi();

        _biome = Map.Biomes.FirstOrDefault(b => b.Id == Biome) ?? Map.Biomes[0];
        var path = SetPieces.Folder + Open + ".json";
        if (Open != null && Godot.FileAccess.FileExists(path))
            Load(SetPiece.Read(path));
        else
        {
            _name.Text = Open ?? "";
            ShowBiome(_biome);
        }
        FitCamera();
        GetViewport().SizeChanged += FitCamera;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= FitCamera;

    /// <summary>
    /// Zooms so the piece's clear circle (a typical one for an empty piece) just fits the paint
    /// area between the panels, seen from the rig's pitch with its vertical field of view.
    /// </summary>
    private void FitCamera()
    {
        var radius = (_props.Count > 0 ? SetPiece.Footprint(_props) : NewPieceClear) * FitMargin;
        var size = GetViewport().GetVisibleRect().Size;
        var halfWidth = Mathf.Max(size.X - 2f * PanelWidth, 1f) / size.X; // the paint area's share of the screen
        var tanV = Mathf.Tan(Mathf.DegToRad(_rig.Camera.Fov / 2f));
        var tanH = tanV * size.X / size.Y;
        var pitch = Mathf.DegToRad(_rig.Pitch);
        var distance = 0f;
        for (var i = 0; i < 64; i++)
        {
            // A ground point (x, z) about the centre sits at depth d - z·cos(pitch) from a camera d away.
            var p = Vector2.FromAngle(i * Mathf.Tau / 64f) * radius;
            // It shows when that depth times the half-view's tangent (scaled to the paint area) covers it.
            var nearer = p.Y * Mathf.Cos(pitch);
            distance = Mathf.Max(distance, nearer + Mathf.Abs(p.X) / (tanH * halfWidth));
            distance = Mathf.Max(distance, nearer + Mathf.Abs(p.Y) * Mathf.Sin(pitch) / tanV);
        }
        _rig.Distance = Mathf.Clamp(distance, CameraRig.MinDistance, _rig.ZoomLimit);
    }

    private void Load(SetPiece piece)
    {
        _name.Text = piece.Id;
        _browser.Current = piece.Id;
        _weight = piece.Weight;
        _turn.ButtonPressed = piece.Turn;
        ShowBiome(Map.Biomes.FirstOrDefault(b => piece.Biomes.Contains(b.Id)) ?? _biome);
        _props.Clear();
        _props.AddRange(piece.Props);
        _undo.Clear();
        _picked.Clear();
        Rebuild();
        FitCamera();
        _dirty = false;
        _message = $"opened {piece.Id}";
    }

    /// <summary>Moves to a region of <paramref name="biome"/>: its ground, its palette, its centre.</summary>
    private void ShowBiome(Biome biome)
    {
        _biome = biome;
        _biomes.Select(Map.Biomes.ToList().IndexOf(biome));
        _centre = Map.FindRegion(biome.Id, Vector2.Zero) ?? Vector2.Zero;
        _piece.Position = new Vector3(_centre.X, 0f, _centre.Y);
        _cursor.Position = _piece.Position;
        _rig.GlobalPosition = _piece.Position;

        _field?.QueueFree();
        _field = new Field { Name = "Field", Map = Map, Start = _piece.Position, Focus = _cursor, Scatter = false, GrassRadius = -1 }; // its props leave no marks, so blades would grow through them
        AddChild(_field);
        FillPalette();
    }

    private void FillPalette()
    {
        var source = _all.ButtonPressed ? Map.Biomes.SelectMany(b => b.Props) : _biome.Props;
        _palette = source.DistinctBy(p => p.Mesh).OrderBy(p => p.Mesh).ToArray();
        _list.Clear();
        foreach (var prop in _palette)
        {
            var (min, max) = Range(prop);
            var item = _list.AddIconItem(_studio.Prop(prop.Mesh, (min + max) / 2f, PropThumb, PropBackground));
            _list.SetItemTooltip(item, Short(prop.Mesh));
        }
        Select(Mathf.Clamp(_selected, 0, _palette.Length - 1));
    }

    private void Select(int index)
    {
        if (_palette.Length == 0)
            return;
        _selected = index;
        _list.Select(index);
        _list.EnsureCurrentIsVisible();
        var (min, max) = Range(_palette[index]);
        _scale = _random ? _rng.RandfRange(min, max) : (min + max) / 2f;
        RebuildGhost();
    }

    private void RebuildGhost()
    {
        _ghost?.QueueFree();
        _ghost = null;
        if (_palette.Length == 0)
            return;
        var mesh = _palette[_selected].Mesh;
        _ghost = SyntyModels.Instance(mesh, Map.Pack(mesh).Atlas, scenery: true);
        if (_ghost != null)
            _piece.AddChild(_ghost);
    }

    /// <summary>A palette prop's scale range before its pack's unit.</summary>
    private (float Min, float Max) Range(BiomeProp prop)
    {
        var unit = Map.Pack(prop.Mesh).Unit;
        return (prop.MinScale / unit, prop.MaxScale / unit);
    }

    private float BlockOf(BiomeProp prop) => prop.Block * Map.Pack(prop.Mesh).Unit;

    public override void _Process(double delta)
    {
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        if (!typing && !Input.IsKeyPressed(Godot.Key.Ctrl))
        {
            var move = Controls.Move();
            _cursor.Position += new Vector3(move.X, 0f, move.Y) * _rig.Distance * PanSpeed * (float)delta;
        }

        UpdateMouse();
        if (_ghost != null)
        {
            _ghost.Visible = _mouse != null && !_selecting;
            if (_mouse is { } m)
                Pose(_ghost, new SetPieceProp(_palette[_selected].Mesh, m, _rotation, _scale, 0f));
        }

        DrawLines();
        UpdateStatus();
    }

    /// <summary>The ground point under the pointer (none over the panels), snapped when snap is on.</summary>
    private void UpdateMouse()
    {
        _mouse = null;
        if (_pointer is not { } screen || _panel.GetGlobalRect().HasPoint(screen) || _piecePanel.GetGlobalRect().HasPoint(screen))
            return;
        if (Ground(screen) is { } at)
            _mouse = _snap ? at.Snapped(Vector2.One * Snap) : at;
    }

    /// <summary>Where the ray through <paramref name="screen"/> meets the ground, relative to the centre.</summary>
    private Vector2? Ground(Vector2 screen)
    {
        var camera = _rig.Camera;
        var origin = camera.ProjectRayOrigin(screen);
        var normal = camera.ProjectRayNormal(screen);
        if (normal.Y >= -1e-3f)
            return null;
        var hit = origin + normal * (-origin.Y / normal.Y);
        return new Vector2(hit.X, hit.Z) - _centre;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouse pointer)
        {
            _pointer = pointer.Position;
            UpdateMouse();
        }
        if (_selecting && e is InputEventMouse && SelectMouse(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click when _mouse != null:
                if (click.AltPressed)
                    PickUp(_mouse.Value);
                else
                {
                    Remember();
                    _lastStroke = _mouse;
                    Place(_mouse.Value);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _mouse != null:
                Remember();
                Erase(_mouse.Value);
                break;
            case InputEventMouseMotion motion when _mouse != null:
                if ((motion.ButtonMask & MouseButtonMask.Left) != 0 && motion.ShiftPressed && _lastStroke is { } last)
                {
                    var spacing = Mathf.Max(1.2f, 2.2f * BlockOf(_palette[_selected]) * _scale);
                    if (last.DistanceTo(_mouse.Value) >= spacing)
                    {
                        _lastStroke = _mouse;
                        Place(_mouse.Value);
                    }
                }
                else if ((motion.ButtonMask & MouseButtonMask.Right) != 0)
                    Erase(_mouse.Value);
                break;
            case InputEventKey { Pressed: true } key:
                if (!HandleKey(key))
                    return;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private bool HandleKey(InputEventKey key)
    {
        switch (key.PhysicalKeycode)
        {
            case Godot.Key.Z when key.CtrlPressed:
                if (!key.Echo)
                    Undo();
                return true;
            case Godot.Key.S when key.CtrlPressed:
                if (!key.Echo)
                    Save();
                return true;
            case Godot.Key.Tab when !key.Echo:
                _selectMode.ButtonPressed = !_selecting; // fires Toggled, which switches the mode
                return true;
            case Godot.Key.Escape when _selecting:
                _picked.Clear();
                return true;
            case Godot.Key.Delete or Godot.Key.Backspace when _selecting && _picked.Count > 0:
                if (!key.Echo)
                    DeletePicked();
                return true;
            case Godot.Key.D when key.CtrlPressed && _selecting && _picked.Count > 0:
                if (!key.Echo)
                    DuplicatePicked();
                return true;
            case Godot.Key.Q or Godot.Key.E when _selecting:
                var step = key.ShiftPressed ? 1f : TurnStep;
                TurnPicked(Mathf.DegToRad(key.PhysicalKeycode == Godot.Key.Q ? step : -step));
                return true;
            case Godot.Key.Z or Godot.Key.X when _selecting:
                ScalePicked(key.PhysicalKeycode == Godot.Key.X ? ScaleStep : 1f / ScaleStep);
                return true;
            case Godot.Key.Q:
                _rotation = Mathf.DegToRad(Mathf.Snapped(Mathf.RadToDeg(_rotation) + TurnStep, TurnStep));
                return true;
            case Godot.Key.E:
                _rotation = Mathf.DegToRad(Mathf.Snapped(Mathf.RadToDeg(_rotation) - TurnStep, TurnStep));
                return true;
            case Godot.Key.Z:
                _scale /= ScaleStep;
                return true;
            case Godot.Key.X:
                _scale *= ScaleStep;
                return true;
            case Godot.Key.R when !key.Echo:
                _random = !_random;
                return true;
            case Godot.Key.G when !key.Echo:
                _snap = !_snap;
                return true;
            default:
                return false;
        }
    }

    private void Place(Vector2 at)
    {
        if (_palette.Length == 0)
            return;
        var prop = _palette[_selected];
        _props.Add(new SetPieceProp(prop.Mesh, at, _rotation, _scale, BlockOf(prop)));
        _nodes.Add(Instance(_props[^1]));
        _dirty = true;
        if (!_random)
            return;
        var (min, max) = Range(prop);
        _rotation = _rng.Randf() * Mathf.Tau;
        _scale = _rng.RandfRange(min, max);
    }

    private void Erase(Vector2 at)
    {
        if (Nearest(at) is not { } i)
            return;
        _nodes[i]?.QueueFree();
        _nodes.RemoveAt(i);
        _props.RemoveAt(i);
        _picked.Clear(); // indices past i moved down
        _dirty = true;
    }

    /// <summary>Select mode's mouse: picking, moving the selection and box selecting. False when not its event.</summary>
    private bool SelectMouse(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click when _mouse != null:
                var hit = PropAt(click.Position);
                if (click.ShiftPressed)
                {
                    if (hit is { } i)
                    {
                        if (!_picked.Remove(i))
                            _picked.Add(i);
                    }
                    else
                        (_boxFrom, _boxAdds) = (_mouse, true);
                }
                else if (hit is { } i)
                {
                    if (!_picked.Contains(i))
                    {
                        _picked.Clear();
                        _picked.Add(i);
                    }
                    (_moveFrom, _moveStart, _moved) = (Ground(click.Position), _props.ToArray(), false);
                }
                else
                {
                    _picked.Clear();
                    (_boxFrom, _boxAdds) = (_mouse, false);
                }
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_boxFrom is { } from && _mouse is { } to)
                {
                    var box = new Rect2(from, Vector2.Zero).Expand(to);
                    if (!_boxAdds)
                        _picked.Clear();
                    for (var i = 0; i < _props.Count; i++)
                    {
                        if (box.HasPoint(_props[i].Offset))
                            _picked.Add(i);
                    }
                }
                (_boxFrom, _moveFrom, _moveStart) = (null, null, null);
                return true;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Left) != 0:
                if (_moveFrom is { } start && _moveStart != null && Ground(motion.Position) is { } now)
                    MovePicked(start, now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the selection by the drag from <paramref name="start"/> to <paramref name="now"/>. With
    /// snap on, one prop lands on the half-metre grid and a group moves in half-metre steps.
    /// </summary>
    private void MovePicked(Vector2 start, Vector2 now)
    {
        var delta = now - start;
        if (_snap && _picked.Count > 1)
            delta = delta.Snapped(Vector2.One * Snap);
        if (!_moved)
        {
            if (delta.Length() < 0.05f)
                return;
            _undo.Push(_moveStart!);
            _moved = true;
        }
        foreach (var i in _picked)
        {
            var at = _moveStart![i].Offset + delta;
            if (_snap && _picked.Count == 1)
                at = at.Snapped(Vector2.One * Snap);
            Update(i, _moveStart[i] with { Offset = at });
        }
    }

    /// <summary>Turns the selection by <paramref name="angle"/>: one prop in place, a group about its middle.</summary>
    private void TurnPicked(float angle)
    {
        if (_picked.Count == 0)
            return;
        Remember();
        var middle = Middle();
        foreach (var i in _picked)
        {
            var p = _props[i];
            // A turn of the node by +angle about Y moves ground points by Rotated(-angle).
            var offset = _picked.Count > 1 ? middle + (p.Offset - middle).Rotated(-angle) : p.Offset;
            Update(i, p with { Offset = offset, Rotation = p.Rotation + angle });
        }
    }

    /// <summary>Scales the selection: one prop in place, a group about its middle (spacing too).</summary>
    private void ScalePicked(float factor)
    {
        if (_picked.Count == 0)
            return;
        Remember();
        var middle = Middle();
        foreach (var i in _picked)
        {
            var p = _props[i];
            var offset = _picked.Count > 1 ? middle + (p.Offset - middle) * factor : p.Offset;
            Update(i, p with { Offset = offset, Scale = p.Scale * factor });
        }
    }

    private void DeletePicked()
    {
        Remember();
        foreach (var i in _picked.OrderDescending())
        {
            _nodes[i]?.QueueFree();
            _nodes.RemoveAt(i);
            _props.RemoveAt(i);
        }
        _message = $"deleted {_picked.Count}";
        _picked.Clear();
        _dirty = true;
    }

    /// <summary>Copies the selection a metre over and selects the copies, ready to drag into place.</summary>
    private void DuplicatePicked()
    {
        Remember();
        var copies = _picked.Order().Select(i => _props[i] with { Offset = _props[i].Offset + Vector2.One }).ToArray();
        _picked.Clear();
        foreach (var copy in copies)
        {
            _picked.Add(_props.Count);
            _props.Add(copy);
            _nodes.Add(Instance(copy));
        }
        _message = $"duplicated {copies.Length}";
        _dirty = true;
    }

    private Vector2 Middle() => _picked.Aggregate(Vector2.Zero, (sum, i) => sum + _props[i].Offset) / _picked.Count;

    private void Update(int i, SetPieceProp prop)
    {
        _props[i] = prop;
        if (_nodes[i] is { } node)
            Pose(node, prop);
        _dirty = true;
    }

    /// <summary>
    /// The prop whose box the ray through <paramref name="screen"/> meets first (each box padded a
    /// little so small props can be hit), else the one nearest the ground point.
    /// </summary>
    private int? PropAt(Vector2 screen)
    {
        var camera = _rig.Camera;
        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        int? best = null;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is not { } node || BoundsOf(_props[i].Mesh, node) is not { } box)
                continue;
            var toLocal = node.GlobalTransform.AffineInverse();
            var pad = 0.2f / Mathf.Max(node.Scale.X, 1e-3f);
            var from = toLocal * origin;
            var along = toLocal.Basis * direction;
            if (RayEnters(box.Grow(pad), from, along) is not { } t)
                continue;
            var distance = (node.GlobalTransform * (from + along * t)).DistanceTo(origin);
            if (distance < bestDistance)
                (best, bestDistance) = (i, distance);
        }
        return best ?? (Ground(screen) is { } at ? Nearest(at) : null);
    }

    /// <summary>How far along <paramref name="along"/> from <paramref name="from"/> the ray enters <paramref name="box"/> (0 when it starts inside), or none.</summary>
    private static float? RayEnters(Aabb box, Vector3 from, Vector3 along)
    {
        var (near, far) = (0f, float.MaxValue);
        for (var axis = 0; axis < 3; axis++)
        {
            var (o, d, lo, hi) = (from[axis], along[axis], box.Position[axis], box.End[axis]);
            if (Mathf.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }
            var (t0, t1) = ((lo - o) / d, (hi - o) / d);
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            (near, far) = (Mathf.Max(near, t0), Mathf.Min(far, t1));
            if (near > far)
                return null;
        }
        return near;
    }

    /// <summary>A mesh's box in its node's own space (none for an empty one), measured once.</summary>
    private Aabb? BoundsOf(string mesh, Node3D node)
    {
        if (!_meshBounds.TryGetValue(mesh, out var box))
            _meshBounds[mesh] = box = SyntyModels.Bounds(node);
        return box.Size == Vector3.Zero ? null : box;
    }

    /// <summary>Takes the mesh, turn and scale of the prop under the mouse (random goes off).</summary>
    private void PickUp(Vector2 at)
    {
        if (Nearest(at) is not { } i)
            return;
        var prop = _props[i];
        if (!_all.ButtonPressed && _palette.All(p => p.Mesh != prop.Mesh))
            _all.ButtonPressed = true; // fires Toggled, which refills the palette
        _random = false;
        Select(Array.FindIndex(_palette, p => p.Mesh == prop.Mesh) is var index and >= 0 ? index : _selected);
        _rotation = prop.Rotation;
        _scale = prop.Scale;
    }

    private int? Nearest(Vector2 at)
    {
        int? best = null;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < _props.Count; i++)
        {
            var d = _props[i].Offset.DistanceTo(at);
            var reach = Mathf.Max(1.2f, _props[i].Block * _props[i].Scale + 0.4f);
            if (d < reach && d < bestDistance)
                (best, bestDistance) = (i, d);
        }
        return best;
    }

    private void Remember() => _undo.Push(_props.ToArray());

    private void Undo()
    {
        if (!_undo.TryPop(out var previous))
        {
            _message = "nothing to undo";
            return;
        }
        _props.Clear();
        _props.AddRange(previous);
        Rebuild();
        _dirty = true;
    }

    private void Rebuild()
    {
        foreach (var node in _nodes)
            node?.QueueFree();
        _nodes.Clear();
        foreach (var prop in _props)
            _nodes.Add(Instance(prop));
        _picked.RemoveWhere(i => i >= _props.Count); // an undo keeps the selection while it still fits
    }

    private Node3D? Instance(SetPieceProp prop)
    {
        var node = SyntyModels.Instance(prop.Mesh, Map.Pack(prop.Mesh).Atlas, scenery: true);
        if (node == null)
            return null;
        Pose(node, prop);
        _piece.AddChild(node);
        return node;
    }

    private void Pose(Node3D node, SetPieceProp prop)
    {
        node.Position = new Vector3(prop.Offset.X, 0f, prop.Offset.Y);
        node.Rotation = new Vector3(0f, prop.Rotation, 0f);
        node.Scale = Vector3.One * prop.Scale * Map.Pack(prop.Mesh).Unit;
    }

    private void Save()
    {
        var name = new string(_name.Text.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (name.Length == 0)
        {
            _message = "name the piece first";
            _name.GrabFocus();
            return;
        }
        if (_props.Count == 0)
        {
            _message = "nothing to save";
            return;
        }
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SetPieces.Folder));
        var props = _props.ToArray();
        var piece = new SetPiece(name, [_biome.Id], _weight, _turn.ButtonPressed, SetPiece.Footprint(props), props);
        piece.Write(SetPieces.Folder + name + ".json");
        _ = _studio.SavePiece(piece, _biome, SetPieces.Folder + name + ".png", ThumbnailStudio.PieceImage);
        _name.Text = name;
        _dirty = false;
        _message = $"saved data/setpieces/{name}.json and .png";
        _browser.Current = name;
        _browser.Refresh();
    }

    /// <summary>The piece's clear circle and centre, and the selected prop's blocking circle.</summary>
    private void DrawLines()
    {
        _lineMesh.ClearSurfaces();
        _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var lift = 0.06f;
        void Circle(Vector2 c, float r, Color colour)
        {
            const int segments = 48;
            for (var i = 0; i < segments; i++)
            {
                var a = c + Vector2.FromAngle(i * Mathf.Tau / segments) * r;
                var b = c + Vector2.FromAngle((i + 1) * Mathf.Tau / segments) * r;
                _lineMesh.SurfaceSetColor(colour);
                _lineMesh.SurfaceAddVertex(new Vector3(a.X, lift, a.Y));
                _lineMesh.SurfaceSetColor(colour);
                _lineMesh.SurfaceAddVertex(new Vector3(b.X, lift, b.Y));
            }
        }
        void Line(Vector2 a, Vector2 b, Color colour)
        {
            _lineMesh.SurfaceSetColor(colour);
            _lineMesh.SurfaceAddVertex(new Vector3(a.X, lift, a.Y));
            _lineMesh.SurfaceSetColor(colour);
            _lineMesh.SurfaceAddVertex(new Vector3(b.X, lift, b.Y));
        }

        var centre = _centre;
        Line(centre - new Vector2(1f, 0f), centre + new Vector2(1f, 0f), UiStyle.Accent);
        Line(centre - new Vector2(0f, 1f), centre + new Vector2(0f, 1f), UiStyle.Accent);
        if (_props.Count > 0)
            Circle(centre, SetPiece.Footprint(_props), UiStyle.Accent with { A = 0.8f });
        if (_mouse is { } m && _palette.Length > 0 && !_selecting)
            Circle(centre + m, Mathf.Max(0.15f, BlockOf(_palette[_selected]) * _scale), UiStyle.Xp);

        // The selection: each prop's box outlined on the ground, and the box being dragged out.
        foreach (var i in _picked)
        {
            if (_nodes[i] is not { } node || BoundsOf(_props[i].Mesh, node) is not { } box)
                continue;
            var corners = new[]
            {
                new Vector3(box.Position.X, 0f, box.Position.Z), new Vector3(box.End.X, 0f, box.Position.Z),
                new Vector3(box.End.X, 0f, box.End.Z), new Vector3(box.Position.X, 0f, box.End.Z),
            }.Select(c => node.GlobalTransform * c).Select(c => new Vector2(c.X, c.Z)).ToArray();
            for (var k = 0; k < 4; k++)
                Line(corners[k], corners[(k + 1) % 4], UiStyle.Xp);
        }
        if (_boxFrom is { } from && _mouse is { } to)
        {
            Line(centre + from, centre + new Vector2(to.X, from.Y), UiStyle.Xp);
            Line(centre + new Vector2(to.X, from.Y), centre + to, UiStyle.Xp);
            Line(centre + to, centre + new Vector2(from.X, to.Y), UiStyle.Xp);
            Line(centre + new Vector2(from.X, to.Y), centre + from, UiStyle.Xp);
        }
        _lineMesh.SurfaceEnd();
    }

    private void UpdateStatus()
    {
        var clear = _props.Count > 0 ? $" · clear {SetPiece.Footprint(_props):0.#} m" : "";
        var tool = !_selecting
            ? $"paint · turn {Mathf.PosMod(Mathf.RadToDeg(_rotation), 360f):0}° · scale {_scale:0.00}"
            : _picked.Count == 1 && _picked.First() is var one
                ? $"select · {Short(_props[one].Mesh)} · turn {Mathf.PosMod(Mathf.RadToDeg(_props[one].Rotation), 360f):0}° · scale {_props[one].Scale:0.00}"
                : $"select · {_picked.Count} props";
        _status.Text = $"{_props.Count} props{clear}{(_dirty ? " · unsaved" : "")}\n" +
            $"{tool}\n" +
            $"random {(_random ? "on" : "off")} (R) · snap {(_snap ? "on" : "off")} (G)" +
            (_message.Length > 0 ? $"\n{_message}" : "");
    }

    /// <summary>Opens a piece picked in the browser; with unsaved changes it asks for a second click.</summary>
    private void OpenFromBrowser(SetPiece piece)
    {
        if (_dirty && _confirmOpen != piece.Id)
        {
            _confirmOpen = piece.Id;
            _message = $"unsaved changes: click {piece.Id} again to drop them";
            return;
        }
        _confirmOpen = null;
        Load(piece);
    }

    private static string Short(string mesh) => mesh[(mesh.IndexOf('/') + 1)..].Replace("SM_", "").Replace(".fbx", "");

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);

        // Left: the biome and its palette.
        _panel = Panel("Palette", Control.LayoutPreset.LeftWide);
        layer.AddChild(_panel);
        var palette = Column(_panel);
        palette.AddChild(UiStyle.Label("PALETTE", 16, UiStyle.Accent));
        _biomes = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var biome in Map.Biomes)
            _biomes.AddItem(biome.Name);
        _biomes.ItemSelected += i =>
        {
            if (Map.Biomes[(int)i] != _biome)
                ShowBiome(Map.Biomes[(int)i]);
        };
        palette.AddChild(_biomes);

        _all = new CheckBox { Text = "Props of every biome", FocusMode = Control.FocusModeEnum.None };
        _all.Toggled += _ => FillPalette();
        palette.AddChild(_all);

        // A grid of pictures; hovering one names it.
        _list = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MaxColumns = 0,
            SameColumnWidth = true,
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = PropThumb / 2,
        };
        _list.ItemSelected += i => Select((int)i);
        palette.AddChild(_list);

        // Right: the piece being painted, then every saved piece.
        _piecePanel = Panel("Piece", Control.LayoutPreset.RightWide);
        layer.AddChild(_piecePanel);
        var piece = Column(_piecePanel);
        piece.AddChild(UiStyle.Label("SET PIECE PAINTER", 16, UiStyle.Accent));
        _name = new LineEdit { PlaceholderText = "name (file under data/setpieces)" };
        _name.TextSubmitted += _ => _name.ReleaseFocus();
        piece.AddChild(_name);

        _turn = new CheckBox { Text = "Turn randomly when placed in a run", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        piece.AddChild(_turn);

        var actions = new HBoxContainer();
        actions.AddChild(Button("New", () =>
        {
            Remember();
            _props.Clear();
            _picked.Clear();
            Rebuild();
            _name.Text = "";
            _weight = 1f;
            _dirty = false;
            _message = "new piece";
            FitCamera();
        }));
        actions.AddChild(Button("Undo", Undo));
        actions.AddChild(Button("Save", Save));
        _selectMode = Button("Select", () => { });
        _selectMode.ToggleMode = true;
        _selectMode.TooltipText = "Select props to move, turn or scale them (Tab)";
        _selectMode.Toggled += on =>
        {
            _selecting = on;
            (_boxFrom, _moveFrom, _moveStart) = (null, null, null);
            _message = on ? "select: click, shift-click or drag a box" : "paint";
        };
        actions.AddChild(_selectMode);
        piece.AddChild(actions);

        _status = UiStyle.Label("", 13);
        piece.AddChild(_status);
        piece.AddChild(UiStyle.Label(
            "WASD pan · wheel zoom · Tab paint/select\n" +
            "paint: click place · shift-drag paint\n   right-click/drag erase · alt-click pick up\n   Q/E turn · Z/X scale the brush\n" +
            "select: click · shift-click · drag a box\n   drag move · Q/E turn (shift 1°) · Z/X scale\n   Del delete · ctrl+D duplicate · Esc drop\n" +
            "ctrl+Z undo · ctrl+S save · B set pieces",
            12, UiStyle.Dim));
        piece.AddChild(new HSeparator());

        _browser = new SetPieceBrowser { Name = "Browser", Map = Map, Studio = _studio };
        _browser.Opened += OpenFromBrowser;
        piece.AddChild(_browser);
    }

    /// <summary>A full-height side panel docked to the window's edge; the paint area is between the two.</summary>
    private static PanelContainer Panel(string name, Control.LayoutPreset side)
    {
        var panel = new PanelContainer { Name = name };
        panel.SetAnchorsPreset(side);
        var left = side == Control.LayoutPreset.LeftWide;
        panel.OffsetLeft = left ? 0f : -PanelWidth;
        panel.OffsetRight = left ? PanelWidth : 0f;
        panel.OffsetTop = 0f;
        panel.OffsetBottom = 0f;
        if (!left)
            panel.GrowHorizontal = Control.GrowDirection.Begin; // widen inward, never off screen
        panel.AddThemeStyleboxOverride("panel", UiStyle.Dock(left));
        return panel;
    }

    private static VBoxContainer Column(Control parent)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        parent.AddChild(column);
        return column;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.Pressed += action;
        return button;
    }
}

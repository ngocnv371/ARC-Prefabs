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
/// Controls: WASD pans, wheel zooms, click places, shift-drag paints, right-click (or drag)
/// erases, alt-click picks up a placed prop's mesh, rotation and scale, Q/E turn, Z/X scale,
/// R random turn and scale, G snaps to half a metre, ctrl+Z undoes, ctrl+S saves, B shows or
/// hides the <see cref="SetPieceBrowser"/>. The palette is on the left; the piece's name, actions
/// and the browser are on the right.
/// </summary>
public partial class SetPiecePainter : Node3D
{
    private const float PanSpeed = 1.2f; // camera distances per second
    private const float TurnStep = 15f;
    private const float ScaleStep = 1.08f;
    private const float Snap = 0.5f;
    private const float PanelWidth = 320f;
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
    private Vector2? _mouse; // ground point under the mouse, relative to the centre
    private Vector2? _lastStroke;
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
        _rig = new CameraRig { Name = "CameraRig", Target = _cursor, LeadAmount = 0f, Distance = 26f };
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
        Rebuild();
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

        _mouse = null;
        var screen = GetViewport().GetMousePosition();
        if (!_panel.GetGlobalRect().HasPoint(screen) && !_piecePanel.GetGlobalRect().HasPoint(screen))
        {
            var camera = _rig.Camera;
            var origin = camera.ProjectRayOrigin(screen);
            var normal = camera.ProjectRayNormal(screen);
            if (normal.Y < -1e-3f)
            {
                var hit = origin + normal * (-origin.Y / normal.Y);
                var at = new Vector2(hit.X, hit.Z) - _centre;
                _mouse = _snap ? at.Snapped(Vector2.One * Snap) : at;
            }
        }

        if (_ghost != null)
        {
            _ghost.Visible = _mouse != null;
            if (_mouse is { } m)
                Pose(_ghost, new SetPieceProp(_palette[_selected].Mesh, m, _rotation, _scale, 0f));
        }

        DrawLines();
        UpdateStatus();
    }

    public override void _UnhandledInput(InputEvent e)
    {
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
        _dirty = true;
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
        new SetPiece(name, [_biome.Id], _weight, _turn.ButtonPressed, SetPiece.Footprint(props), props).Write(SetPieces.Folder + name + ".json");
        _name.Text = name;
        _dirty = false;
        _message = $"saved data/setpieces/{name}.json";
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
        if (_mouse is { } m && _palette.Length > 0)
            Circle(centre + m, Mathf.Max(0.15f, BlockOf(_palette[_selected]) * _scale), UiStyle.Xp);
        _lineMesh.SurfaceEnd();
    }

    private void UpdateStatus()
    {
        var clear = _props.Count > 0 ? $" · clear {SetPiece.Footprint(_props):0.#} m" : "";
        _status.Text = $"{_props.Count} props{clear}{(_dirty ? " · unsaved" : "")}\n" +
            $"turn {Mathf.PosMod(Mathf.RadToDeg(_rotation), 360f):0}° · scale {_scale:0.00}\n" +
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
            Rebuild();
            _name.Text = "";
            _weight = 1f;
            _dirty = false;
            _message = "new piece";
        }));
        actions.AddChild(Button("Undo", Undo));
        actions.AddChild(Button("Save", Save));
        piece.AddChild(actions);

        _status = UiStyle.Label("", 13);
        piece.AddChild(_status);
        piece.AddChild(UiStyle.Label(
            "WASD pan · wheel zoom\nclick place · shift-drag paint\nright-click/drag erase · alt-click pick up\nQ/E turn · Z/X scale\nctrl+Z undo · ctrl+S save\nB shows or hides the set pieces",
            12, UiStyle.Dim));
        piece.AddChild(new HSeparator());

        _browser = new SetPieceBrowser { Name = "Browser", Map = Map, Studio = _studio };
        _browser.Opened += OpenFromBrowser;
        // With the list hidden the column shrinks to its controls instead of running to the bottom.
        _browser.VisibilityChanged += () =>
        {
            _piecePanel.AnchorBottom = _browser.Visible ? 1f : 0f;
            _piecePanel.OffsetBottom = _browser.Visible ? -12f : 12f;
        };
        piece.AddChild(_browser);
    }

    /// <summary>A full-height side panel, 12 px in from the window's edges.</summary>
    private static PanelContainer Panel(string name, Control.LayoutPreset side)
    {
        var panel = new PanelContainer { Name = name };
        panel.SetAnchorsPreset(side);
        var left = side == Control.LayoutPreset.LeftWide;
        panel.OffsetLeft = left ? 12f : -12f - PanelWidth;
        panel.OffsetRight = left ? 12f + PanelWidth : -12f;
        panel.OffsetTop = 12f;
        panel.OffsetBottom = -12f;
        if (!left)
            panel.GrowHorizontal = Control.GrowDirection.Begin; // widen inward, never off screen
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Panel));
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

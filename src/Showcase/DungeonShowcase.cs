using Godot;
using ArcPrefabs.Render;
using ArcPrefabs.UI;
using ArcPrefabs.World;

namespace ArcPrefabs.Showcase;

/// <summary>
/// The set pieces on show: an endless dungeon of walled rooms, one piece in the middle of each
/// with open floor round it, joined by torch-lit corridors, with a camera gliding through it
/// just over the wall tops, rising over the bigger rooms, so it looks down into each room
/// without running into the props. Each room has a second exit the camera passes up, down a
/// short corridor to a shut door (see <see cref="DungeonLayout"/>). Rooms come in runs of a theme
/// (data/themes.json), which may recolour their floors, tint their light and strew its own clutter;
/// a card names the theme as its run begins. Each room's theme, biome, name and lore show at the
/// foot of the screen. Rooms are built a few ahead of the camera and dropped behind it.
/// Space pauses, Up and Down change speed, Right skips to the next room, H hides the text and
/// Esc quits. With <see cref="Capture"/> set it instead photographs that many rooms and quits.
/// </summary>
public partial class DungeonShowcase : Node3D
{
    private const float CorridorHeight = 7f; // the camera's height over corridors (walls are 5.24 m)
    private const int Ahead = 4; // rooms built ahead of the one the camera is in
    private const int Behind = 1; // and kept behind it

    private static readonly string WallMesh = "dungeon/SM_Env_Wall_01.fbx";
    private static readonly string DoorMesh = "dungeon/SM_Env_Wall_Archway_01.fbx";
    private static readonly string SealedMesh = "dungeon/SM_Env_Wall_DoorFrame_Double_Round_01.fbx";
    private static readonly string CornerMesh = "dungeon/SM_Env_Wall_Pillar_Large_01.fbx";
    private static readonly string TorchMesh = "dungeon/SM_Prop_Torch_Ornate_01.fbx";

    private readonly Dictionary<int, (Node3D Root, List<DungeonLight> Lights)> _built = [];
    private readonly List<Vector3> _path = []; // the camera's control points
    private readonly List<float> _along = []; // distance along the path at each point
    private readonly List<(int Room, int Point)> _centres = []; // which point is each room's centre
    private DungeonLayout _layout = null!;
    private DungeonLights _lights = null!;
    private Camera3D _camera = null!;
    private float _s; // the camera's distance along the path
    private float _speed = 4.5f;
    private bool _paused;
    private int _room = -1;
    private bool _relight = true;
    private Label _biomeLabel = null!, _nameLabel = null!, _loreLabel = null!;
    private Label _themeName = null!, _themeLore = null!;
    private Control _hud = null!, _themeCard = null!;
    private float _cardTime; // seconds the theme card has been up

    public required WorldMap Map { get; init; }
    public ulong Seed { get; init; } = 1;

    /// <summary>Biomes to draw rooms from (ids); empty means the dungeon ones.</summary>
    public string[] Biomes { get; init; } = [];

    /// <summary>Themes to draw runs from (ids in data/themes.json); empty means all.</summary>
    public string[] Themes { get; init; } = [];

    /// <summary>When above 0, photograph this many rooms into <see cref="CaptureDir"/> and quit.</summary>
    public int Capture { get; init; }
    public string CaptureDir { get; init; } = "res://captures";

    private static readonly string[] DungeonBiomes = ["crypt", "warrens", "caverns", "forge", "halls"];

    public override void _Ready()
    {
        try
        {
            Start();
        }
        catch (Exception e) when (Capture > 0)
        {
            // Unattended (run.cmd --capture, minimised): report and quit rather than sit there.
            GD.PushError($"showcase: {e}");
            GetTree().Quit(1);
        }
    }

    private void Start()
    {
        _lights = DungeonLights.Load();
        _lights.Apply();
        var ids = Biomes.Length > 0 ? Biomes : DungeonBiomes;
        var biomes = Map.Biomes.Where(b => ids.Contains(b.Id)).ToArray();
        _layout = new DungeonLayout(SetPieces.ReadAll(), biomes, SetPieceThemes.Load(Map), Seed, Themes);

        _camera = new Camera3D { Fov = 62f, Near = 0.1f, Far = 220f };
        AddChild(_camera);
        _camera.MakeCurrent();
        BuildHud();

        Extend();
        _s = _along.Count > 1 ? _along[1] * 0.2f : 0f;
        Follow(1f);

        if (Capture > 0)
            _ = Photograph();
    }

    public override void _Process(double delta)
    {
        if (Capture > 0)
            return;
        if (!_paused)
            _s += _speed * (float)delta;
        Follow((float)delta);
        // The theme card fades out a few seconds after a run begins.
        _cardTime += (float)delta;
        _themeCard.Modulate = Colors.White with { A = Mathf.Clamp((7f - _cardTime) / 1.5f, 0f, 1f) };
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        switch (key.Keycode)
        {
            case Key.Space: _paused = !_paused; break;
            case Key.Up: _speed = Mathf.Min(_speed * 1.25f, 30f); break;
            case Key.Down: _speed = Mathf.Max(_speed / 1.25f, 1f); break;
            case Key.Right: _s = CentreAlong(_room + 1) - 6f; break;
            case Key.H: _hud.Visible = !_hud.Visible; break;
            case Key.Escape: GetTree().Quit(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Moves the camera to <see cref="_s"/>, builds and drops rooms around it, and relights.</summary>
    private void Follow(float delta)
    {
        Extend();
        var at = PathAt(_s);
        var ahead = PathAt(_s + 13f);
        // Look ahead and down to the floor.
        var target = new Vector3(ahead.X, 0.5f, ahead.Z);
        var want = Transform3D.Identity.LookingAt(target - at, Vector3.Up).Basis;
        var basis = delta >= 1f ? want : _camera.Basis.Orthonormalized().Slerp(want, 1f - Mathf.Exp(-3.5f * delta));
        _camera.Transform = new Transform3D(basis, at);

        var room = RoomAt(_s);
        if (room != _room)
        {
            _room = room;
            ShowRoom(_layout.Rooms[room]);
            Keep(room);
        }
        var ground = new Vector2(at.X, at.Z) + new Vector2(-basis.Z.X, -basis.Z.Z).Normalized() * 20f;
        if (_relight || _lights.Drifted(ground))
        {
            _lights.Bake(ground, _built.Values.SelectMany(b => b.Lights));
            _relight = false;
        }
    }

    /// <summary>Plans rooms and lays the path far enough ahead of the camera.</summary>
    private void Extend()
    {
        var need = Math.Max(_room, 0) + Ahead + 3;
        _layout.PlanTo(need + 1);
        // Path points go in once a room's way out is known (the room after it is planned).
        var done = _centres.Count;
        for (var i = done; i < _layout.Rooms.Count - 1; i++)
        {
            var r = _layout.Rooms[i];
            if (i > 0)
                AddPoint(Flat(r.Door(r.In), CorridorHeight));
            _centres.Add((i, _path.Count));
            AddPoint(Flat(r.Centre, RoomHeight(r)));
            AddPoint(Flat(r.Door(r.Out), CorridorHeight));
            if (r.Corridor >= 3)
                AddPoint(Flat((r.Door(r.Out) + _layout.Rooms[i + 1].Door(_layout.Rooms[i + 1].In)) / 2f, CorridorHeight));
        }
    }

    private static Vector3 Flat(Vector2 p, float y) => new(p.X, y, p.Y);

    /// <summary>How high the camera flies over a room's middle: higher over bigger rooms, to take them in.</summary>
    private static float RoomHeight(DungeonRoom r) => CorridorHeight + 1f + r.Span * 0.45f;

    private void AddPoint(Vector3 p)
    {
        _along.Add(_path.Count == 0 ? 0f : _along[^1] + _path[^1].DistanceTo(p));
        _path.Add(p);
    }

    /// <summary>A point on the Catmull-Rom curve through the control points, <paramref name="s"/> metres along.</summary>
    private Vector3 PathAt(float s)
    {
        s = Mathf.Clamp(s, 0f, _along[^1]);
        var k = _along.BinarySearch(s);
        if (k < 0)
            k = ~k - 1;
        k = Mathf.Clamp(k, 0, _path.Count - 2);
        var t = (s - _along[k]) / Mathf.Max(_along[k + 1] - _along[k], 1e-3f);
        Vector3 P(int i) => _path[Mathf.Clamp(i, 0, _path.Count - 1)];
        return P(k).CubicInterpolate(P(k + 1), P(k - 1), P(k + 2), t);
    }

    private float CentreAlong(int room)
    {
        var entry = _centres.FirstOrDefault(c => c.Room == room, _centres[^1]);
        return _along[entry.Point];
    }

    /// <summary>The room whose centre the camera has most recently passed, or is about to reach.</summary>
    private int RoomAt(float s)
    {
        var room = 0;
        foreach (var (r, point) in _centres)
        {
            // A room counts from the corridor that leads into it (two points before its centre).
            if (s >= _along[Math.Max(point - 2, 0)])
                room = r;
        }
        return room;
    }

    /// <summary>Builds the rooms near <paramref name="room"/> and drops those well behind.</summary>
    private void Keep(int room)
    {
        for (var i = Math.Max(0, room - Behind); i <= room + Ahead && i < _layout.Rooms.Count - 1; i++)
        {
            if (!_built.ContainsKey(i))
            {
                _built[i] = Build(_layout.Rooms[i], _layout.Rooms[i + 1]);
                _relight = true;
            }
        }
        foreach (var old in _built.Keys.Where(k => k < room - Behind).ToArray())
        {
            _built[old].Root.QueueFree();
            _built.Remove(old);
            _relight = true;
        }
    }

    // ---- building ---------------------------------------------------------------------------------

    private (Node3D, List<DungeonLight>) Build(DungeonRoom room, DungeonRoom next)
    {
        var root = new Node3D { Name = $"Room{room.Index}_{room.Piece.Id}" };
        AddChild(root);
        var lights = new List<DungeonLight>();
        var floor = FloorFor(room);

        // The floor and the walls: an archway in the middle of each side with a way through, and a
        // shut door where the second exit has no room for a corridor.
        Floor(root, room.Centre, new Vector2(room.Half * 2f, room.Half * 2f), floor);
        foreach (var dir in DungeonLayout.Directions)
        {
            var side = new Vector2I(dir.Y, -dir.X);
            var a = room.Door(dir) + (Vector2)side * room.Half;
            var b = room.Door(dir) - (Vector2)side * room.Half;
            var open = dir == room.Out || (room.Index > 0 && dir == room.In) || (dir == room.Branch && room.BranchLength > 0);
            var shut = dir == room.Branch && room.BranchLength == 0;
            WallRun(root, a, b, room.Span, open || shut ? room.Span / 2 : -1, shut ? SealedMesh : DoorMesh);
            if (shut)
                ShutDoor(root, room.Door(dir), b - a);
            Add(root, CornerMesh, a, 0f, 1f, 0f);
        }

        // The corridor on to the next room, and the one down the other exit to a shut door.
        var nextFloor = FloorFor(next);
        Corridor(root, lights, room.Door(room.Out), room.Out, room.Corridor, nextFloor);
        if (room.BranchLength > 0)
        {
            var end = room.Door(room.Branch) + (Vector2)room.Branch * room.BranchLength * DungeonRoom.Wall;
            Corridor(root, lights, room.Door(room.Branch), room.Branch, room.BranchLength, floor);
            var across = new Vector2(room.Branch.Y, -room.Branch.X) * DungeonRoom.Wall / 2f;
            WallRun(root, end + across, end - across, 1, 0, SealedMesh);
            ShutDoor(root, end, -across);
        }

        // The biome's loose clutter over the open floor round the piece.
        Clutter(root, room);

        // The set piece, turned as planned, and the lights it gives.
        foreach (var prop in room.Piece.Props)
        {
            var at = room.Centre + prop.Offset.Rotated(-room.Turn);
            Add(root, prop.Mesh, at, prop.Rotation + room.Turn, prop.Scale, 0f);
            Emit(root, lights, prop.Mesh, new Vector3(at.X, 0f, at.Y), prop.Scale);
        }
        foreach (var light in room.Piece.Lights)
        {
            var at = room.Centre + light.Offset.Rotated(-room.Turn);
            lights.Add(new DungeonLight(new Vector3(at.X, light.Height, at.Y), light.Color, light.Range, light.Energy));
        }
        // A soft fill over the middle and each quarter, so the open floor is never black: the
        // theme's tint if it has one, else the biome's.
        if (room.Theme?.Fill is { } tint || _lights.Fills.TryGetValue(room.Biome.Id, out tint))
        {
            var fill = (Fill: tint.Item1, Energy: tint.Item2);
            lights.Add(new DungeonLight(Flat(room.Centre, 3f), fill.Fill, room.Half * 1.2f, fill.Energy));
            foreach (var (qx, qz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                lights.Add(new DungeonLight(Flat(room.Centre + new Vector2(qx, qz) * room.Half * 0.5f, 3f), fill.Fill, room.Half * 0.8f, fill.Energy * 0.7f));
        }
        return (root, lights);
    }

    /// <summary>A corridor of <paramref name="segments"/> wall lengths from <paramref name="from"/> toward <paramref name="dir"/>: floor, walls both sides, torches.</summary>
    private void Corridor(Node3D root, List<DungeonLight> lights, Vector2 from, Vector2I dir, int segments, Material floor)
    {
        var along = (Vector2)dir;
        var across = new Vector2(along.Y, -along.X);
        var length = segments * DungeonRoom.Wall;
        var to = from + along * length;
        Floor(root, (from + to) / 2f, new Vector2(Mathf.Abs(along.X) * length + Mathf.Abs(across.X) * DungeonRoom.Wall, Mathf.Abs(along.Y) * length + Mathf.Abs(across.Y) * DungeonRoom.Wall), floor);
        foreach (var s in new[] { -1f, 1f })
        {
            var off = across * s * DungeonRoom.Wall / 2f;
            WallRun(root, from + off, to + off, segments, -1, DoorMesh);
            for (var i = 0; i < segments; i++)
            {
                if ((i + (s > 0 ? 0 : 1)) % 2 != 0)
                    continue;
                var spot = from + along * (i + 0.5f) * DungeonRoom.Wall + across * s * (DungeonRoom.Wall / 2f - 0.45f);
                var face = Mathf.Atan2(-across.X * s, -across.Y * s);
                Add(root, TorchMesh, spot, face, 1f, 2.6f);
                Emit(root, lights, TorchMesh, new Vector3(spot.X, 2.6f, spot.Y), 1f);
            }
        }
    }

    /// <summary>A closed double door in the door frame centred on <paramref name="centre"/>, its wall running along <paramref name="wall"/>.</summary>
    private void ShutDoor(Node3D root, Vector2 centre, Vector2 wall)
    {
        var u = wall.Normalized();
        var rot = Mathf.Atan2(-u.Y, u.X);
        Add(root, "dungeon/SM_Env_DoorDouble_Round_01.fbx", centre - u * 1.32f, rot, 1f, 0f);
        Add(root, "dungeon/SM_Env_DoorDouble_Round_02.fbx", centre + u * 1.32f, rot, 1f, 0f);
    }

    /// <summary>The room's floor: its theme's ground colours if it has some, else its biome's.</summary>
    private static Material FloorFor(DungeonRoom room) => room.Theme?.Ground is { } g
        ? ArcMaterials.FloorLit(g.Lush, g.Deep, g.Dry)
        : ArcMaterials.FloorLit(room.Biome.Lush, room.Biome.Deep, room.Biome.Dry);

    /// <summary>
    /// Scatters loose props over the room's open floor, clear of the piece, the walls and the
    /// doorways: the biome's (its scatter in data/biomes.json: rubble, bones, pebbles,
    /// stalagmites...), with its theme's clutter in place of a share of them.
    /// </summary>
    private void Clutter(Node3D root, DungeonRoom room)
    {
        var theme = room.Theme is { Clutter.Length: > 0 } t ? t : null;
        var biome = room.Biome.Props.Any(p => p.Weight > 0f);
        if (theme == null && !biome)
            return;
        var rng = new RandomNumberGenerator { Seed = Seed * 7919 + (ulong)room.Index };
        var inner = room.Half - 2.5f;
        var count = Math.Min(140, (int)(inner * inner * 4f / 26f));
        var doors = DungeonLayout.Directions.Select(room.Door).ToArray();
        for (var i = 0; i < count; i++)
        {
            var at = room.Centre + new Vector2(rng.RandfRange(-inner, inner), rng.RandfRange(-inner, inner));
            if (at.DistanceTo(room.Centre) < room.Piece.Clear || doors.Any(d => d.DistanceTo(at) < 5f))
                continue;
            var prop = theme != null && (!biome || rng.Randf() < theme.Share) ? theme.PickClutter(rng) : room.Biome.Pick(rng);
            // A biome prop's scale already has its pack's unit in it; Add puts the unit on again.
            var unit = Map.Pack(prop.Mesh).Unit;
            Add(root, prop.Mesh, at, rng.Randf() * Mathf.Tau, rng.RandfRange(prop.MinScale, prop.MaxScale) / unit, 0f);
        }
    }

    /// <summary>Lights a prop that burns or glows (data/lights.json), with a marker where the light is.</summary>
    private void Emit(Node3D root, List<DungeonLight> lights, string mesh, Vector3 foot, float scale)
    {
        if (_lights.EmitterFor(mesh) is not { } e)
            return;
        var at = foot + Vector3.Up * e.Y * scale;
        lights.Add(new DungeonLight(at, e.Color, e.Range * Mathf.Sqrt(scale), e.Energy));
        if (e.Glow <= 0f)
            return;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = ArcMaterials.Glow(e.Color),
            Position = at,
            Scale = Vector3.One * e.Glow * scale,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>A straight wall of <paramref name="count"/> wall lengths from a to b, each twinned so both faces show, with an archway at segment <paramref name="door"/>.</summary>
    private void WallRun(Node3D root, Vector2 a, Vector2 b, int count, int door, string doorMesh)
    {
        var step = (b - a) / count;
        var rot = Mathf.Atan2(-step.Y, step.X);
        for (var i = 0; i < count; i++)
        {
            var mesh = i == door ? doorMesh : WallMesh;
            Add(root, mesh, a + step * i, rot, 1f, 0f);
            Add(root, mesh, a + step * (i + 1), rot + Mathf.Pi, 1f, 0f);
        }
    }

    private void Add(Node3D root, string mesh, Vector2 at, float rotation, float scale, float height)
    {
        var (atlas, unit) = Map.Pack(mesh);
        if (SyntyModels.Instance(mesh, atlas, lit: true) is not { } node)
            return;
        node.Position = new Vector3(at.X, height, at.Y);
        node.Rotation = new Vector3(0f, rotation, 0f);
        node.Scale = Vector3.One * scale * unit;
        root.AddChild(node);
    }

    private static void Floor(Node3D root, Vector2 centre, Vector2 size, Material material)
    {
        root.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = size },
            MaterialOverride = material,
            Position = new Vector3(centre.X, 0f, centre.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ---- text ------------------------------------------------------------------------------------

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_hud);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Panel with { A = 0.62f }, UiStyle.Border with { A = 0.35f }, 8, 1));
        panel.CustomMinimumSize = new Vector2(560f, 0f);
        _hud.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.Minsize, 24);
        panel.GrowVertical = Control.GrowDirection.Begin;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        panel.AddChild(column);
        _biomeLabel = UiStyle.Label("", 13, UiStyle.Accent);
        _nameLabel = UiStyle.Label("", 24, UiStyle.Text);
        _loreLabel = UiStyle.Label("", 15, UiStyle.Dim, wrap: true);
        _loreLabel.CustomMinimumSize = new Vector2(560f, 0f);
        column.AddChild(_biomeLabel);
        column.AddChild(_nameLabel);
        column.AddChild(_loreLabel);

        // The theme card, at the top as a run of a theme begins.
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Panel with { A = 0.55f }, UiStyle.Border with { A = 0.3f }, 8, 1));
        card.CustomMinimumSize = new Vector2(640f, 0f);
        _hud.AddChild(card);
        card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop, Control.LayoutPresetMode.Minsize, 28);
        card.GrowHorizontal = Control.GrowDirection.Both;
        var cardColumn = new VBoxContainer();
        cardColumn.AddThemeConstantOverride("separation", 4);
        card.AddChild(cardColumn);
        _themeName = UiStyle.Label("", 28, UiStyle.Text, HorizontalAlignment.Center);
        _themeLore = UiStyle.Label("", 15, UiStyle.Dim, HorizontalAlignment.Center, wrap: true);
        _themeLore.CustomMinimumSize = new Vector2(640f, 0f);
        cardColumn.AddChild(_themeName);
        cardColumn.AddChild(_themeLore);
        _themeCard = card;
        _themeCard.Modulate = Colors.Transparent;

        if (Capture > 0)
            return;
        var keys = UiStyle.Label("Space pause · Up/Down speed · Right next room · H hide text · Esc quit", 12, UiStyle.Dim, HorizontalAlignment.Right);
        keys.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        keys.GrowHorizontal = Control.GrowDirection.Begin;
        keys.OffsetRight = -18f;
        keys.OffsetTop = 14f;
        _hud.AddChild(keys);
    }

    private void ShowRoom(DungeonRoom room)
    {
        _biomeLabel.Text = (room.Theme != null ? $"{room.Theme.Name} · {room.Biome.Name}" : room.Biome.Name).ToUpperInvariant();
        if (room.FirstOfRun && room.Theme != null)
        {
            _themeName.Text = room.Theme.Name;
            _themeLore.Text = room.Theme.Lore;
            _themeLore.Visible = room.Theme.Lore.Length > 0;
            _cardTime = 0f;
            _themeCard.Modulate = Colors.White;
        }
        else if (Capture > 0)
        {
            _themeCard.Modulate = Colors.Transparent;
        }
        _nameLabel.Text = room.Piece.Id.Replace('_', ' ');
        _loreLabel.Text = room.Piece.Lore;
        _loreLabel.Visible = room.Piece.Lore.Length > 0;
    }

    // ---- capture ---------------------------------------------------------------------------------

    /// <summary>Photographs the first <see cref="Capture"/> rooms as the camera enters each, then quits.</summary>
    private async Task Photograph()
    {
        var dir = ProjectSettings.GlobalizePath(CaptureDir);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var size = new Vector2I(1600, 900);
        // The window may be minimised, so the shots come from a view of their own onto the same world.
        var view = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X, World3D = GetViewport().FindWorld3D() };
        var eye = new Camera3D { Fov = _camera.Fov, Near = _camera.Near, Far = _camera.Far };
        view.AddChild(eye);
        var hud = (CanvasLayer)_hud.GetParent();
        hud.GetParent().RemoveChild(hud);
        view.AddChild(hud);
        AddChild(view);
        eye.MakeCurrent();

        for (var i = 0; i < Capture; i++)
        {
            _s = CentreAlong(i + 1) - _layout.Rooms[i + 1].Half * 0.8f;
            Follow(1f);
            eye.GlobalTransform = _camera.GlobalTransform;
            // A minimised window (run.cmd --capture) only rarely draws, so draw a few frames here;
            // the first ones also settle the shader compiles.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var f = 0; f < 4; f++)
                RenderingServer.ForceDraw(false);
            var path = $"{dir}/showcase_{i + 1:00}.png";
            view.GetTexture().GetImage().SavePng(path);
            var shot = _layout.Rooms[_room];
            GD.Print($"captured {path} ({shot.Theme?.Id ?? "-"}{(shot.FirstOfRun ? "*" : "")} {shot.Biome.Id}/{shot.Piece.Id}, {shot.Span}x{shot.Span} walls, in {shot.In}, on {shot.Out}, branch {shot.Branch} x{shot.BranchLength})");
        }
        GetTree().Quit();
    }
}

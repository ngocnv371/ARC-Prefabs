using Godot;
using ArcPrefabs.Render;

namespace ArcPrefabs.World;

/// <summary>
/// The endless field over the <see cref="WorldMap"/>: one ground plane that follows the focus
/// and a ring of scatter chunks around it. The ground's colours are baked from the map's
/// biomes into small textures whenever the plane moves, so region borders blend on the grass.
/// Each chunk places its props deterministically from a hash of its coordinates, choosing
/// each from the biome under it, so walking away and back finds the same trees. Set pieces
/// (<see cref="SetPieces"/>) place their props wherever they reach into a chunk and keep the
/// random scatter out of their clear circle. Every prop leaves a <see cref="GroundMark"/>, baked
/// into a finer map that darkens the ground where props meet it and keeps grass from under
/// them; chunks near the focus also get <see cref="Grass"/> blades, which share the ground's maps.
/// </summary>
public partial class Field : Node3D
{
    public const float ChunkSize = 32f;
    private const float ClearRadius = 9f; // nothing blocking spawns this close to the start
    private const float Dither = 5f; // props near a border may come from either side
    private const float GroundTexel = 4f; // metres per pixel of the baked ground colours
    private const float MarkTexel = 0.5f; // metres per pixel of the contact shade and grass mask
    private const int MaxMarkPixels = 768; // bird view's wide plane gets coarser marks
    public const int MaxPushers = 24; // see arc_grass.gdshader

    // Bounds of each mesh at scale 1, for its ground mark.
    private static readonly Dictionary<string, Aabb> MeshBounds = [];

    /// <summary>A blocking circle on the ground (tree trunk, rock).</summary>
    public readonly record struct Obstacle(Vector2 Centre, float Radius);

    private readonly Dictionary<Vector2I, Node3D> _chunks = [];
    private readonly Dictionary<Vector2I, List<Obstacle>> _obstacles = [];
    private readonly Dictionary<Vector2I, List<GroundMark>> _marks = [];
    private readonly Dictionary<Vector2I, MultiMeshInstance3D> _grass = [];
    private MeshInstance3D _ground = null!;
    private ShaderMaterial _groundMaterial = null!;
    private ShaderMaterial _grassMaterial = null!;
    private ArrayMesh _blade = null!;
    private byte[] _lush = [], _deep = [], _dry = [], _grassData = [], _markData = [];
    private int _mapPixels, _markPixels;
    private ImageTexture? _lushMap, _deepMap, _dryMap, _grassMap, _markMap;
    private Vector3 _bakedAt = new(float.NaN, 0f, 0f);
    private bool _marksDirty;

    public int Seed { get; set; } = 1;

    /// <summary>Chunks kept built on each side of the focus: 2 is 5×5 (a run); bird view uses more.</summary>
    public int Radius { get; init; } = 2;

    /// <summary>Most chunks built in one frame; a run builds all it needs at once, bird view spreads them out.</summary>
    public int BuildBudget { get; init; } = int.MaxValue;

    public required WorldMap Map { get; init; }

    /// <summary>The hand-made layouts dropped into regions, if any.</summary>
    public SetPieces? Pieces { get; init; }

    /// <summary>Whether chunks get the biomes' random scatter (the set piece painter turns it off).</summary>
    public bool Scatter { get; init; } = true;

    /// <summary>Chunks on each side of the focus that get grass blades; below 0 there is none.</summary>
    public int GrassRadius { get; init; } = 2;

    /// <summary>Where the hero starts; it is kept clear of blocking props.</summary>
    public Vector3 Start { get; set; }

    /// <summary>The point the field keeps itself built around (the hero).</summary>
    public Node3D? Focus { get; set; }

    public override void _Ready()
    {
        var size = ChunkSize * (Radius * 2 + 2);
        _groundMaterial = ArcMaterials.Ground();
        _ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = _groundMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ground);
        _grassMaterial = ArcMaterials.Grass();
        _blade = Grass.Blade();
        _mapPixels = Mathf.CeilToInt(size / GroundTexel);
        _lush = new byte[_mapPixels * _mapPixels * 3];
        _deep = new byte[_lush.Length];
        _dry = new byte[_lush.Length];
        _grassData = new byte[_mapPixels * _mapPixels * 4];
        _markPixels = Mathf.Min(MaxMarkPixels, Mathf.CeilToInt(size / MarkTexel));
        _markData = new byte[_markPixels * _markPixels * 2];
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        var focus = Focus?.GlobalPosition ?? Start;
        _ground.Position = new Vector3(Mathf.Snapped(focus.X, ChunkSize), 0f, Mathf.Snapped(focus.Z, ChunkSize));
        if (_ground.Position != _bakedAt)
            BakeGround(); // about 3 ms, once per chunk crossed

        var centre = new Vector2I(Mathf.FloorToInt(focus.X / ChunkSize), Mathf.FloorToInt(focus.Z / ChunkSize));
        foreach (var key in _chunks.Keys.ToArray())
        {
            if (Mathf.Abs(key.X - centre.X) > Radius + 1 || Mathf.Abs(key.Y - centre.Y) > Radius + 1)
            {
                _chunks[key].QueueFree();
                _chunks.Remove(key);
                _obstacles.Remove(key);
                _marks.Remove(key);
            }
        }

        // Nearest first, so with a budget the middle of the view fills in before its edges.
        var built = 0;
        for (var ring = 0; ring <= Radius && built < BuildBudget; ring++)
        for (var x = -ring; x <= ring && built < BuildBudget; x++)
        for (var z = -ring; z <= ring && built < BuildBudget; z++)
        {
            var key = centre + new Vector2I(x, z);
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring || _chunks.ContainsKey(key))
                continue;
            _chunks[key] = BuildChunk(key);
            _marksDirty = true;
            built++;
        }

        RefreshGrass(centre);
        if (_marksDirty)
            BakeMarks();
    }

    /// <summary>Blades for built chunks within <see cref="GrassRadius"/>; farther ones drop theirs.</summary>
    private void RefreshGrass(Vector2I centre)
    {
        foreach (var key in _grass.Keys.ToArray())
        {
            if (Mathf.Abs(key.X - centre.X) > GrassRadius || Mathf.Abs(key.Y - centre.Y) > GrassRadius)
            {
                _grass[key].QueueFree();
                _grass.Remove(key);
            }
        }
        for (var x = -GrassRadius; x <= GrassRadius; x++)
        for (var z = -GrassRadius; z <= GrassRadius; z++)
        {
            var key = centre + new Vector2I(x, z);
            if (_chunks.ContainsKey(key) && !_grass.ContainsKey(key))
            {
                _grass[key] = Grass.Build(key, Seed, ChunkSize, _blade, _grassMaterial);
                AddChild(_grass[key]);
            }
        }
        var focus = Focus?.GlobalPosition ?? Start;
        _grassMaterial.SetShaderParameter("uFocus", new Vector2(focus.X, focus.Z));
        _grassMaterial.SetShaderParameter("uReach", (GrassRadius + 0.5f) * ChunkSize);
    }

    /// <summary>
    /// What bends the grass this frame: xz position and radius in metres (the hero, monsters near
    /// it). Up to <see cref="MaxPushers"/> are used.
    /// </summary>
    public void SetPushers(IReadOnlyList<Vector4> pushers)
    {
        var slots = new Vector4[MaxPushers];
        for (var i = 0; i < Mathf.Min(pushers.Count, MaxPushers); i++)
            slots[i] = pushers[i];
        _grassMaterial.SetShaderParameter("uPush", slots);
    }

    /// <summary>Draws every chunk's ground marks into the mark map under the plane.</summary>
    private void BakeMarks()
    {
        _marksDirty = false;
        var size = ((PlaneMesh)_ground.Mesh).Size.X;
        var rect = new Rect2(_bakedAt.X - size / 2f, _bakedAt.Z - size / 2f, size, size);
        GroundMark.Bake(_markData, _markPixels, rect, _marks.Values.SelectMany(m => m));
        _markMap = Upload(_markMap, _markData, _markPixels, Image.Format.Rg8, "uMarkMap");
        SetMaps("uMarkRect", new Vector4(rect.Position.X, rect.Position.Y, size, size));
    }

    private void SetMaps(string uniform, Variant value)
    {
        _groundMaterial.SetShaderParameter(uniform, value);
        _grassMaterial.SetShaderParameter(uniform, value);
    }

    /// <summary>Fills the ground's colour maps from the biomes under the plane.</summary>
    private void BakeGround()
    {
        _bakedAt = _ground.Position;
        var size = ((PlaneMesh)_ground.Mesh).Size.X;
        var pixels = _mapPixels;
        var texel = size / pixels;
        var corner = new Vector2(_bakedAt.X - size / 2f, _bakedAt.Z - size / 2f);
        for (var y = 0; y < pixels; y++)
        for (var x = 0; x < pixels; x++)
        {
            var (first, second, w) = Map.Sample(corner + new Vector2(x + 0.5f, y + 0.5f) * texel);
            var i = (y * pixels + x) * 3;
            Put(_lush, i, second.Lush.Lerp(first.Lush, w));
            Put(_deep, i, second.Deep.Lerp(first.Deep, w));
            Put(_dry, i, second.Dry.Lerp(first.Dry, w));
            var density = Mathf.Lerp(second.GrassDensity, first.GrassDensity, w);
            var g = (y * pixels + x) * 4;
            Put(_grassData, g, new Vector3(
                density,
                Mathf.Lerp(second.GrassHeight, first.GrassHeight, w),
                Mathf.Lerp(second.GrassVariation, first.GrassVariation, w)));
            _grassData[g + 3] = (byte)Mathf.RoundToInt(Mathf.Clamp(Mathf.Lerp(second.DryGrass, first.DryGrass, w), 0f, 1f) * 255f);
        }

        _lushMap = Upload(_lushMap, _lush, _mapPixels, Image.Format.Rgb8, "uLushMap");
        _deepMap = Upload(_deepMap, _deep, _mapPixels, Image.Format.Rgb8, "uDeepMap");
        _dryMap = Upload(_dryMap, _dry, _mapPixels, Image.Format.Rgb8, "uDryMap");
        _grassMap = Upload(_grassMap, _grassData, _mapPixels, Image.Format.Rgba8, "uGrassMap");
        SetMaps("uMapRect", new Vector4(corner.X, corner.Y, size, size));
        _marksDirty = true; // the mark map follows the plane
    }

    private static void Put(byte[] data, int i, Vector3 c)
    {
        data[i] = (byte)Mathf.RoundToInt(Mathf.Clamp(c.X, 0f, 1f) * 255f);
        data[i + 1] = (byte)Mathf.RoundToInt(Mathf.Clamp(c.Y, 0f, 1f) * 255f);
        data[i + 2] = (byte)Mathf.RoundToInt(Mathf.Clamp(c.Z, 0f, 1f) * 255f);
    }

    private ImageTexture Upload(ImageTexture? texture, byte[] data, int pixels, Image.Format format, string uniform)
    {
        var image = Image.CreateFromData(pixels, pixels, false, format, data);
        if (texture != null)
        {
            texture.Update(image);
            return texture;
        }
        texture = ImageTexture.CreateFromImage(image);
        SetMaps(uniform, texture);
        return texture;
    }

    private Node3D BuildChunk(Vector2I key)
    {
        var chunk = new Node3D { Name = $"Chunk_{key.X}_{key.Y}" };
        AddChild(chunk);
        var rng = new RandomNumberGenerator { Seed = (ulong)(HashCode.Combine(Seed, key.X, key.Y) & 0x7fffffff) };
        var origin = new Vector3(key.X * ChunkSize, 0f, key.Y * ChunkSize);
        var obstacles = new List<Obstacle>();
        _obstacles[key] = obstacles;
        var marks = new List<GroundMark>();
        _marks[key] = marks;

        var area = new Rect2(origin.X, origin.Z, ChunkSize, ChunkSize);
        var pieces = Pieces?.Overlapping(area).ToArray() ?? [];
        foreach (var placement in pieces)
        {
            foreach (var prop in placement.Piece.Props)
            {
                var at = placement.Place(prop);
                if (!area.HasPoint(at))
                    continue; // the chunk that holds it places it
                var (atlas, unit) = Map.Pack(prop.Mesh);
                if (SyntyModels.Instance(prop.Mesh, atlas, scenery: true) is not { } node)
                    continue;
                Place(chunk, node, prop.Mesh, at, prop.Rotation + placement.Turn, prop.Scale * unit, prop.Block * prop.Scale, marks);
                if (prop.Block > 0f)
                    obstacles.Add(new Obstacle(at, prop.Block * prop.Scale));
            }
        }
        if (!Scatter)
            return chunk;

        // Clumps read more natural than uniform noise: pick a few centres, scatter around them.
        // The biome at the chunk's middle sets how dense it is.
        var home = Map.At(new Vector2(origin.X, origin.Z) + Vector2.One * ChunkSize / 2f);
        var clumps = rng.RandiRange(home.Clumps.X, home.Clumps.Y);
        for (var c = 0; c < clumps; c++)
        {
            var clumpCentre = new Vector3(rng.Randf() * ChunkSize, 0f, rng.Randf() * ChunkSize);
            var count = rng.RandiRange(home.ClumpSize.X, home.ClumpSize.Y);
            for (var i = 0; i < count; i++)
            {
                var offset = new Vector3(rng.Randfn(0f, 3.5f), 0f, rng.Randfn(0f, 3.5f));
                var position = origin + clumpCentre + offset;
                var at = new Vector2(position.X + rng.Randfn(0f, Dither), position.Z + rng.Randfn(0f, Dither));
                var prop = Map.At(at).Pick(rng);
                if (prop.Block > 0f && position.DistanceTo(Start) < ClearRadius)
                    continue;
                if (pieces.Any(p => p.Covers(new Vector2(position.X, position.Z))))
                    continue;
                if (SyntyModels.Instance(prop.Mesh, prop.Atlas, scenery: true) is not { } node)
                    continue;
                var scale = rng.RandfRange(prop.MinScale, prop.MaxScale);
                Place(chunk, node, prop.Mesh, new Vector2(position.X, position.Z), rng.Randf() * Mathf.Tau, scale, prop.Block * scale, marks);
                if (prop.Block > 0f)
                    obstacles.Add(new Obstacle(new Vector2(position.X, position.Z), prop.Block * scale));
            }
        }

        return chunk;
    }

    /// <summary>Puts a prop on the ground and notes where it meets it (<paramref name="block"/> in metres).</summary>
    private static void Place(Node3D chunk, Node3D node, string mesh, Vector2 at, float angle, float scale, float block, List<GroundMark> marks)
    {
        node.Position = new Vector3(at.X, 0f, at.Y);
        node.Rotation = new Vector3(0f, angle, 0f);
        node.Scale = Vector3.One * scale;
        chunk.AddChild(node);
        if (!MeshBounds.TryGetValue(mesh, out var bounds))
            MeshBounds[mesh] = bounds = SyntyModels.Bounds(node);
        if (GroundMark.For(at, angle, block, new Aabb(bounds.Position * scale, bounds.Size * scale)) is { } mark)
            marks.Add(mark);
    }

    /// <summary>
    /// Moves a circle of <paramref name="radius"/> at <paramref name="position"/> out of any
    /// obstacle it overlaps, so walkers slide around trunks and rocks.
    /// </summary>
    public Vector3 PushOut(Vector3 position, float radius)
    {
        var cx = Mathf.FloorToInt(position.X / ChunkSize);
        var cz = Mathf.FloorToInt(position.Z / ChunkSize);
        var p = new Vector2(position.X, position.Z);
        for (var x = cx - 1; x <= cx + 1; x++)
        for (var z = cz - 1; z <= cz + 1; z++)
        {
            if (!_obstacles.TryGetValue(new Vector2I(x, z), out var list))
                continue;
            foreach (var o in list)
            {
                var d = p - o.Centre;
                var min = o.Radius + radius;
                var lengthSq = d.LengthSquared();
                if (lengthSq >= min * min)
                    continue;
                var length = Mathf.Sqrt(lengthSq);
                p = length > 1e-4f ? o.Centre + d / length * min : o.Centre + new Vector2(min, 0f);
            }
        }
        return new Vector3(p.X, position.Y, p.Y);
    }
}

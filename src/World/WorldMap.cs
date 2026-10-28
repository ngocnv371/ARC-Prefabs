using Godot;
using GDict = Godot.Collections.Dictionary;

namespace ArcPrefabs.World;

/// <summary>A scatter prop a biome places: mesh under assets/synty, its atlas, and how it sits.</summary>
public sealed record BiomeProp(string Mesh, string Atlas, float Weight, float MinScale, float MaxScale, float Block);

/// <summary>
/// One kind of land (data/biomes.json): its ground palette, its scatter, its grass (blades per
/// square metre as a share of the most, their full height in metres, how far back the growth
/// waves go, 0 all grown to 1 down to sprouts, and the share that grows on the dry patches) and which anomaly kinds
/// live there.
/// </summary>
public sealed record Biome(
    string Id,
    string Name,
    IReadOnlyDictionary<string, float> Mobs,
    Vector3 Lush,
    Vector3 Deep,
    Vector3 Dry,
    Vector2I Clumps,
    Vector2I ClumpSize,
    BiomeProp[] Props,
    float GrassDensity = 1f,
    float GrassHeight = 0.4f,
    float GrassVariation = 0.5f,
    float DryGrass = 0.4f
)
{
    private readonly float _totalWeight = Props.Sum(p => p.Weight);

    /// <summary>A scatter prop by weight; props of weight 0 are for set pieces only and never come up.</summary>
    public BiomeProp Pick(RandomNumberGenerator rng)
    {
        var roll = rng.Randf() * _totalWeight;
        foreach (var prop in Props)
        {
            if (prop.Weight <= 0f)
                continue;
            roll -= prop.Weight;
            if (roll <= 0f)
                return prop;
        }
        return Props.Last(p => p.Weight > 0f);
    }
}

/// <summary>
/// The endless world as regions: a jittered grid of sites about <see cref="RegionSize"/> apart,
/// each owning the land nearest it (Voronoi cells). A site's biome comes from (x + 2y) mod
/// the biome count, so with five biomes no two neighbouring regions match; the seed shuffles
/// which biome is which, and the region around the origin is always the home biome. Borders
/// are warped by noise so they wander, and blend over <see cref="Border"/> metres.
/// </summary>
public sealed class WorldMap
{
    private const float Jitter = 0.3f; // of the region size

    private readonly Biome[] _order; // biome per (x + 2y) mod count
    private readonly int _seed;
    private readonly float _warp;

    public IReadOnlyList<Biome> Biomes { get; }
    public float RegionSize { get; }
    public float Border { get; }

    /// <summary>Share of spawns drawn from a kind the biome does not list.</summary>
    public float StrayShare { get; }

    /// <summary>How many set pieces a region tries to place (see <see cref="SetPieces"/>).</summary>
    public Vector2I SetPiecesPerRegion { get; private init; }

    /// <summary>Each mesh folder's palette atlas and unit scale (the Dungeon pack is in centimetres).</summary>
    public IReadOnlyDictionary<string, (string Atlas, float Unit)> Packs { get; private init; } = new Dictionary<string, (string, float)>();

    private WorldMap(Biome[] biomes, string home, int seed, float regionSize, float border, float warp, float strayShare)
    {
        Biomes = biomes;
        _seed = seed;
        RegionSize = regionSize;
        Border = border;
        _warp = warp;
        StrayShare = strayShare;

        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var rest = biomes.Where(b => b.Id != home).OrderBy(_ => rng.Randi()).ToList();
        rest.Insert(0, biomes.FirstOrDefault(b => b.Id == home) ?? biomes[0]);
        _order = rest.ToArray();
    }

    /// <summary>The biome that owns <paramref name="p"/> (world XZ).</summary>
    public Biome At(Vector2 p) => Sample(p).First;

    /// <summary>
    /// The nearest region's biome at <paramref name="p"/>, the next nearest, and how much of the
    /// first to show (1 deep inside a region, 0.5 on the border).
    /// </summary>
    public (Biome First, Biome Second, float Weight) Sample(Vector2 p)
    {
        p = Warp(p);
        var cell = new Vector2I(Mathf.RoundToInt(p.X / RegionSize), Mathf.RoundToInt(p.Y / RegionSize));
        Vector2I c1 = default, c2 = default;
        Vector2 s1 = default, s2 = default;
        float d1 = float.MaxValue, d2 = float.MaxValue;
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            var c = cell + new Vector2I(x, y);
            var s = Site(c);
            var d = p.DistanceSquaredTo(s);
            if (d < d1)
            {
                (c2, s2, d2) = (c1, s1, d1);
                (c1, s1, d1) = (c, s, d);
            }
            else if (d < d2)
            {
                (c2, s2, d2) = (c, s, d);
            }
        }

        // Distance from p to the bisector between the two sites.
        var edge = (d2 - d1) / (2f * Mathf.Max(s1.DistanceTo(s2), 1e-3f));
        var weight = 0.5f + 0.5f * Mathf.SmoothStep(0f, Border, edge);
        return (BiomeOf(c1), BiomeOf(c2), weight);
    }

    /// <summary>The centre of the region nearest <paramref name="from"/> whose biome is <paramref name="id"/>, if any is close.</summary>
    public Vector2? FindRegion(string id, Vector2 from)
    {
        var origin = new Vector2I(Mathf.RoundToInt(from.X / RegionSize), Mathf.RoundToInt(from.Y / RegionSize));
        for (var ring = 0; ring <= 4; ring++)
        {
            Vector2? best = null;
            for (var x = -ring; x <= ring; x++)
            for (var y = -ring; y <= ring; y++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != ring)
                    continue;
                var c = origin + new Vector2I(x, y);
                // Check the warped site really is in its own biome (it nearly always is).
                if (BiomeOf(c).Id != id || At(Site(c)).Id != id)
                    continue;
                var s = Site(c);
                if (best == null || from.DistanceSquaredTo(s) < from.DistanceSquaredTo(best.Value))
                    best = s;
            }
            if (best != null)
                return best;
        }
        return null;
    }

    /// <summary>The region cell nearest <paramref name="p"/>, before warping (its region may be a neighbour's).</summary>
    public Vector2I CellOf(Vector2 p) => new(Mathf.RoundToInt(p.X / RegionSize), Mathf.RoundToInt(p.Y / RegionSize));

    /// <summary>A region's centre and biome.</summary>
    public (Vector2 Site, Biome Biome) Region(Vector2I cell) => (Site(cell), BiomeOf(cell));

    /// <summary>The atlas and unit scale of <paramref name="mesh"/>'s pack (its folder under assets/synty).</summary>
    public (string Atlas, float Unit) Pack(string mesh) =>
        Packs.TryGetValue(mesh[..Mathf.Max(mesh.IndexOf('/'), 0)], out var pack) ? pack : ("", 1f);

    private Biome BiomeOf(Vector2I cell) => _order[PosMod(cell.X + 2 * cell.Y, _order.Length)];

    private Vector2 Site(Vector2I cell)
    {
        if (cell == Vector2I.Zero)
            return Vector2.Zero; // the run starts in the middle of the home region
        var h = Hash(cell.X, cell.Y, _seed);
        var jx = ((h & 0xffff) / 65535f * 2f - 1f) * Jitter;
        var jy = ((h >> 16 & 0xffff) / 65535f * 2f - 1f) * Jitter;
        return new Vector2(cell.X + jx, cell.Y + jy) * RegionSize;
    }

    private Vector2 Warp(Vector2 p)
    {
        if (_warp <= 0f)
            return p;
        const float frequency = 1f / 70f;
        var q = p * frequency;
        return p + new Vector2(Noise(q, _seed) - 0.5f, Noise(q + new Vector2(31.7f, 11.3f), _seed) - 0.5f) * 2f * _warp;
    }

    // Two octaves of value noise in [0, 1]; plain C# so baking the ground stays off the engine API.
    private static float Noise(Vector2 p, int seed) => ValueNoise(p, seed) * 0.7f + ValueNoise(p * 2.3f, seed + 1) * 0.3f;

    private static float ValueNoise(Vector2 p, int seed)
    {
        var i = new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y));
        var f = p - i;
        f = f * f * (Vector2.One * 3f - 2f * f);
        float H(int x, int y) => (Hash(i.X + x, i.Y + y, seed) & 0xffffff) / (float)0xffffff;
        return Mathf.Lerp(Mathf.Lerp(H(0, 0), H(1, 0), f.X), Mathf.Lerp(H(0, 1), H(1, 1), f.X), f.Y);
    }

    private static uint Hash(int x, int y, int seed)
    {
        var h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)seed * 0xcb1ab31fu;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        h *= 0x297a2d39u;
        return h ^ h >> 15;
    }

    private static int PosMod(int a, int n) => (a % n + n) % n;

    public static WorldMap Load(int seed, string path = "res://data/biomes.json")
    {
        var root = Json.ParseString(Godot.FileAccess.GetFileAsString(path)).AsGodotDictionary();
        var packs = root["packs"].AsGodotDictionary();
        var biomes = new List<Biome>();
        foreach (var item in root["biomes"].AsGodotArray())
        {
            var b = item.AsGodotDictionary();
            var ground = b["ground"].AsGodotDictionary();
            var grass = b.TryGetValue("grass", out var g) ? g.AsGodotDictionary() : new GDict();
            var props = new List<BiomeProp>();
            foreach (var p in b["props"].AsGodotArray())
            {
                var prop = p.AsGodotDictionary();
                var mesh = prop["mesh"].AsString();
                var pack = packs[mesh[..mesh.IndexOf('/')]].AsGodotDictionary();
                var unit = Get(pack, "unit", 1f);
                var scale = prop["scale"].AsGodotArray();
                props.Add(new BiomeProp(
                    mesh,
                    pack["atlas"].AsString(),
                    Get(prop, "w", 1f),
                    scale[0].AsSingle() * unit,
                    scale[1].AsSingle() * unit,
                    Get(prop, "block", 0f) / unit
                ));
            }
            biomes.Add(new Biome(
                b["id"].AsString(),
                b["name"].AsString(),
                b["mobs"].AsGodotDictionary().ToDictionary(kv => kv.Key.AsString(), kv => kv.Value.AsSingle()),
                Rgb(ground["lush"]),
                Rgb(ground["deep"]),
                Rgb(ground["dry"]),
                Range(b, "clumps", new Vector2I(3, 6)),
                Range(b, "clumpSize", new Vector2I(4, 12)),
                props.ToArray(),
                Get(grass, "density", 1f),
                Get(grass, "height", 0.4f),
                Get(grass, "heightVariation", 0.5f),
                Get(grass, "dryGrass", 0.4f)
            ));
        }

        return new WorldMap(
            biomes.ToArray(),
            root.TryGetValue("home", out var home) ? home.AsString() : biomes[0].Id,
            seed,
            Get(root, "regionSize", 140f),
            Get(root, "border", 14f),
            Get(root, "warp", 22f),
            Get(root, "strayShare", 0.12f)
        )
        {
            SetPiecesPerRegion = Range(root, "setPiecesPerRegion", new Vector2I(1, 2)),
            Packs = packs.ToDictionary(kv => kv.Key.AsString(), kv =>
            {
                var pack = kv.Value.AsGodotDictionary();
                return (pack["atlas"].AsString(), Get(pack, "unit", 1f));
            }),
        };
    }

    private static Vector3 Rgb(Variant v)
    {
        var a = v.AsGodotArray();
        return new Vector3(a[0].AsSingle(), a[1].AsSingle(), a[2].AsSingle());
    }

    private static Vector2I Range(GDict d, string key, Vector2I fallback)
    {
        if (!d.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array)
            return fallback;
        var a = v.AsGodotArray();
        return new Vector2I(a[0].AsInt32(), a[1].AsInt32());
    }

    private static float Get(GDict d, string key, float fallback) =>
        d.TryGetValue(key, out var v) && v.VariantType is Variant.Type.Float or Variant.Type.Int ? v.AsSingle() : fallback;
}

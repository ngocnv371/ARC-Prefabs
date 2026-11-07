using Godot;

namespace ArcPrefabs.World;

/// <summary>
/// A theme (data/themes.json): a story that runs over several rooms, which pieces join through
/// their <see cref="SetPiece.Themes"/>. It may dress its rooms: <see cref="Ground"/> recolours
/// the floor, <see cref="Fill"/> tints the soft light over them, and <see cref="Clutter"/> takes
/// <see cref="Share"/> of the loose props on the open floor from the biome's.
/// </summary>
public sealed record SetPieceTheme(string Id, string Name, string Lore)
{
    public (Vector3 Lush, Vector3 Deep, Vector3 Dry)? Ground { get; init; }
    public (Color Color, float Energy)? Fill { get; init; }
    public BiomeProp[] Clutter { get; init; } = [];
    public float Share { get; init; } = 1f;

    /// <summary>A clutter prop by weight (only when <see cref="Clutter"/> has any).</summary>
    public BiomeProp PickClutter(RandomNumberGenerator rng)
    {
        var roll = rng.Randf() * Clutter.Sum(p => p.Weight);
        return Clutter.FirstOrDefault(p => (roll -= p.Weight) <= 0f) ?? Clutter[^1];
    }
}

/// <summary>The themes in data/themes.json and how many rooms in a row each lasts.</summary>
public sealed class SetPieceThemes
{
    public const string Path = "res://data/themes.json";

    public IReadOnlyList<SetPieceTheme> All { get; private init; } = [];
    public Vector2I Run { get; private init; } = new(3, 5);

    public SetPieceTheme? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    public static SetPieceThemes Load(WorldMap map, string path = Path)
    {
        if (!Godot.FileAccess.FileExists(path))
            return new SetPieceThemes();
        var root = Json.ParseString(Godot.FileAccess.GetFileAsString(path)).AsGodotDictionary();
        static Vector3 V(Variant v)
        {
            var c = v.AsGodotArray();
            return new Vector3(c[0].AsSingle(), c[1].AsSingle(), c[2].AsSingle());
        }
        var run = root.TryGetValue("run", out var r) ? r.AsGodotArray() : null;
        var themes = root["themes"].AsGodotArray().Select(v =>
        {
            var t = v.AsGodotDictionary();
            var theme = new SetPieceTheme(t["id"].AsString(), t["name"].AsString(), t.TryGetValue("lore", out var lore) ? lore.AsString() : "");
            if (t.TryGetValue("ground", out var g))
            {
                var ground = g.AsGodotDictionary();
                theme = theme with { Ground = (V(ground["lush"]), V(ground["deep"]), V(ground["dry"])) };
            }
            if (t.TryGetValue("fill", out var f))
            {
                var fill = f.AsGodotDictionary();
                var c = V(fill["color"]);
                theme = theme with { Fill = (new Color(c.X, c.Y, c.Z), fill["energy"].AsSingle()) };
            }
            if (t.TryGetValue("clutter", out var clutter))
            {
                theme = theme with
                {
                    Share = t.TryGetValue("share", out var share) ? share.AsSingle() : 1f,
                    Clutter = clutter.AsGodotArray().Select(p =>
                    {
                        var prop = p.AsGodotDictionary();
                        var mesh = prop["mesh"].AsString();
                        var (atlas, unit) = map.Pack(mesh);
                        var scale = prop.TryGetValue("scale", out var s) ? s.AsGodotArray() : [1f, 1f];
                        // As in biomes.json: scales include the pack's unit, blocking does not.
                        return new BiomeProp(mesh, atlas, prop.TryGetValue("w", out var w) ? w.AsSingle() : 1f,
                            scale[0].AsSingle() * unit, scale[1].AsSingle() * unit,
                            (prop.TryGetValue("block", out var b) ? b.AsSingle() : 0f) / unit);
                    }).ToArray(),
                };
            }
            return theme;
        }).ToArray();
        return new SetPieceThemes
        {
            All = themes,
            Run = run != null ? new Vector2I(run[0].AsInt32(), run[1].AsInt32()) : new Vector2I(3, 5),
        };
    }
}

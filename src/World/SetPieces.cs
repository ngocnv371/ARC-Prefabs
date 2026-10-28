using System.Globalization;
using System.Text;
using Godot;

namespace ArcPrefabs.World;

/// <summary>
/// One prop of a set piece, around the piece's centre. <see cref="Scale"/> is before the pack's
/// unit and <see cref="Block"/> is the blocking radius in metres at scale 1, as in biomes.json.
/// </summary>
public sealed record SetPieceProp(string Mesh, Vector2 Offset, float Rotation, float Scale, float Block);

/// <summary>
/// A hand-made layout of props (data/setpieces/*.json, painted with ARC-Prefabs) that
/// regions of its biomes drop into the field. <see cref="Clear"/> is the radius kept free of the
/// random scatter; <see cref="Turn"/> lets each placement face a random way.
/// </summary>
public sealed record SetPiece(string Id, string[] Biomes, float Weight, bool Turn, float Clear, SetPieceProp[] Props)
{
    private const float Margin = 2f; // scatter stays this far outside the outermost prop

    /// <summary>The radius that holds every prop, plus a margin.</summary>
    public static float Footprint(IEnumerable<SetPieceProp> props) =>
        props.Select(p => p.Offset.Length() + p.Block * p.Scale).DefaultIfEmpty(0f).Max() + Margin;

    public static SetPiece Read(string path)
    {
        var root = Json.ParseString(Godot.FileAccess.GetFileAsString(path)).AsGodotDictionary();
        var props = root["props"].AsGodotArray().Select(v =>
        {
            var p = v.AsGodotDictionary();
            return new SetPieceProp(
                p["mesh"].AsString(),
                new Vector2(p["x"].AsSingle(), p["z"].AsSingle()),
                Mathf.DegToRad(p.TryGetValue("rot", out var rot) ? rot.AsSingle() : 0f),
                p.TryGetValue("scale", out var scale) ? scale.AsSingle() : 1f,
                p.TryGetValue("block", out var block) ? block.AsSingle() : 0f
            );
        }).ToArray();
        return new SetPiece(
            path.GetFile().GetBaseName(),
            root["biomes"].AsGodotArray().Select(b => b.AsString()).ToArray(),
            root.TryGetValue("weight", out var weight) ? weight.AsSingle() : 1f,
            !root.TryGetValue("turn", out var turn) || turn.AsBool(),
            root.TryGetValue("clear", out var clear) ? clear.AsSingle() : Footprint(props),
            props
        );
    }

    /// <summary>Writes the piece as JSON, one prop per line so diffs stay readable.</summary>
    public void Write(string path)
    {
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append($"  \"biomes\": [{string.Join(", ", Biomes.Select(b => $"\"{b}\""))}],\n");
        json.Append($"  \"weight\": {F(Weight)},\n");
        json.Append($"  \"turn\": {(Turn ? "true" : "false")},\n");
        json.Append("  \"props\": [\n");
        for (var i = 0; i < Props.Length; i++)
        {
            var p = Props[i];
            json.Append($"    {{ \"mesh\": \"{p.Mesh}\", \"x\": {F(p.Offset.X)}, \"z\": {F(p.Offset.Y)}, " +
                $"\"rot\": {F(Mathf.PosMod(Mathf.RadToDeg(p.Rotation), 360f))}, \"scale\": {F(p.Scale)}, \"block\": {F(p.Block)} }}");
            json.Append(i < Props.Length - 1 ? ",\n" : "\n");
        }
        json.Append("  ]\n}\n");
        File.WriteAllText(ProjectSettings.GlobalizePath(path), json.ToString());
    }
}

/// <summary>Where a set piece landed: its centre on the ground and how far it is turned.</summary>
public readonly record struct SetPiecePlacement(SetPiece Piece, Vector2 Centre, float Turn)
{
    /// <summary>A prop's ground position (a turn about Y, matching the node's rotation).</summary>
    public Vector2 Place(SetPieceProp prop) => Centre + prop.Offset.Rotated(-Turn);

    public bool Covers(Vector2 p) => p.DistanceSquaredTo(Centre) < Piece.Clear * Piece.Clear;
}

/// <summary>
/// Scatters the set pieces over the <see cref="WorldMap"/>: each region tries to place
/// <see cref="WorldMap.SetPiecesPerRegion"/> pieces of its biome near its centre, wholly inside
/// the region, apart from each other and clear of the start. The layout comes from a hash of
/// the region, so it is the same each time the field rebuilds there.
/// </summary>
public sealed class SetPieces
{
    public const string Folder = "res://data/setpieces/";
    private const float Spread = 0.3f; // of the region size, how far from the centre a piece may sit
    private const float Gap = 4f; // metres between neighbouring pieces' clear circles

    private readonly Dictionary<Vector2I, SetPiecePlacement[]> _regions = [];

    public required WorldMap Map { get; init; }
    public required IReadOnlyList<SetPiece> All { get; init; }
    public int Seed { get; init; } = 1;

    /// <summary>Where the hero starts, and how far beyond a piece's clear radius it must stay.</summary>
    public Vector2 Start { get; init; }
    public float StartClear { get; init; } = 9f;

    /// <summary>A placement fixed in place (the main menu's stage); random pieces keep away from it.</summary>
    public SetPiecePlacement? Pinned { get; init; }

    public static IReadOnlyList<SetPiece> ReadAll()
    {
        var pieces = new List<SetPiece>();
        foreach (var file in DirAccess.GetFilesAt(Folder).Where(f => f.EndsWith(".json")).Order())
        {
            try
            {
                pieces.Add(SetPiece.Read(Folder + file));
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidCastException)
            {
                GD.PushWarning($"set piece {file}: {e.Message}");
            }
        }
        return pieces;
    }

    /// <summary>The placements whose clear circle reaches into <paramref name="area"/> (world XZ).</summary>
    public IEnumerable<SetPiecePlacement> Overlapping(Rect2 area)
    {
        if (Pinned is { } pinned && pinned.Covers(pinned.Centre.Clamp(area.Position, area.End)))
            yield return pinned;
        var cell = Map.CellOf(area.GetCenter());
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            foreach (var placement in InRegion(cell + new Vector2I(x, y)))
            {
                var nearest = placement.Centre.Clamp(area.Position, area.End);
                if (placement.Covers(nearest))
                    yield return placement;
            }
        }
    }

    private SetPiecePlacement[] InRegion(Vector2I cell)
    {
        if (_regions.TryGetValue(cell, out var placed))
            return placed;

        var (site, biome) = Map.Region(cell);
        var candidates = All.Where(p => p.Biomes.Contains(biome.Id) && p.Props.Length > 0).ToArray();
        var list = new List<SetPiecePlacement>();
        if (candidates.Length > 0)
        {
            var rng = new RandomNumberGenerator { Seed = (ulong)(HashCode.Combine(Seed, cell.X, cell.Y, 0x5e7) & 0x7fffffff) };
            var want = rng.RandiRange(Map.SetPiecesPerRegion.X, Map.SetPiecesPerRegion.Y);
            var total = candidates.Sum(p => p.Weight);
            for (var attempt = 0; attempt < want * 8 && list.Count < want; attempt++)
            {
                var roll = rng.Randf() * total;
                var piece = candidates.FirstOrDefault(p => (roll -= p.Weight) <= 0f) ?? candidates[^1];
                var centre = site + Vector2.FromAngle(rng.Randf() * Mathf.Tau) * Mathf.Sqrt(rng.Randf()) * Map.RegionSize * Spread;
                var turn = piece.Turn ? rng.Randf() * Mathf.Tau : 0f;
                if (centre.DistanceTo(Start) < piece.Clear + StartClear)
                    continue;
                if (Pinned is { } pinned && pinned.Centre.DistanceTo(centre) < pinned.Piece.Clear + piece.Clear + Gap)
                    continue;
                if (list.Any(o => o.Centre.DistanceTo(centre) < o.Piece.Clear + piece.Clear + Gap))
                    continue;
                if (!Inside(centre, piece.Clear, biome.Id))
                    continue;
                list.Add(new SetPiecePlacement(piece, centre, turn));
            }
        }

        placed = list.ToArray();
        _regions[cell] = placed;
        return placed;
    }

    /// <summary>Whether the circle lies in <paramref name="biome"/> (its centre and eight points on the rim).</summary>
    private bool Inside(Vector2 centre, float radius, string biome)
    {
        if (Map.At(centre).Id != biome)
            return false;
        for (var i = 0; i < 8; i++)
        {
            if (Map.At(centre + Vector2.FromAngle(i * Mathf.Tau / 8f) * radius).Id != biome)
                return false;
        }
        return true;
    }
}

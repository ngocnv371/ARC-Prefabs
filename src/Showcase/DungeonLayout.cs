using Godot;
using ArcPrefabs.World;

namespace ArcPrefabs.Showcase;

/// <summary>
/// One room of the showcase's dungeon: a square of <see cref="Span"/> wall lengths a side around
/// a set piece, entered from <see cref="In"/> and left through <see cref="Out"/> (grid directions,
/// x and z), with a corridor of <see cref="Corridor"/> wall lengths to the next room.
/// </summary>
public sealed record DungeonRoom(int Index, Vector2 Centre, int Span, SetPiece Piece, Biome Biome, float Turn)
{
    /// <summary>The length of one wall mesh (SM_Env_Wall_01), the dungeon's grid.</summary>
    public const float Wall = 5.24f;

    public Vector2I In { get; init; }
    public Vector2I Out { get; set; }
    public int Corridor { get; set; }

    public float Half => Span * Wall / 2f;

    public Rect2 Bounds => new(Centre - Vector2.One * Half, Vector2.One * Half * 2f);

    /// <summary>The middle of the doorway on the side facing <paramref name="dir"/>.</summary>
    public Vector2 Door(Vector2I dir) => Centre + (Vector2)dir * Half;
}

/// <summary>
/// Plans an endless chain of rooms: each holds one set piece of the current biome, rooms are
/// joined by straight corridors, the path turns left or right now and then without crossing
/// itself, and the biome changes every few rooms. The same seed gives the same dungeon.
/// </summary>
public sealed class DungeonLayout
{
    private static readonly Vector2I[] Dirs = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private readonly RandomNumberGenerator _rng;
    private readonly Dictionary<string, SetPiece[]> _byBiome;
    private readonly Biome[] _biomes;
    private readonly List<string> _recent = [];
    private readonly List<Rect2> _taken = []; // rooms and corridors already laid, to keep clear of
    private Biome _biome;
    private int _zoneLeft;

    public List<DungeonRoom> Rooms { get; } = [];

    public DungeonLayout(IReadOnlyList<SetPiece> pieces, IEnumerable<Biome> biomes, ulong seed)
    {
        _rng = new RandomNumberGenerator { Seed = seed };
        _byBiome = biomes.ToDictionary(b => b.Id, b => pieces.Where(p => p.Biomes.Contains(b.Id) && p.Props.Length > 0).ToArray());
        _biomes = biomes.Where(b => _byBiome[b.Id].Length > 0).ToArray();
        if (_biomes.Length == 0)
            throw new InvalidOperationException("no set pieces in the showcase's biomes");
        _biome = _biomes[_rng.RandiRange(0, _biomes.Length - 1)];
        _zoneLeft = _rng.RandiRange(4, 7);
    }

    /// <summary>Plans rooms until there are at least <paramref name="count"/>.</summary>
    public void PlanTo(int count)
    {
        while (Rooms.Count < count)
            PlanNext();
    }

    private void PlanNext()
    {
        if (_zoneLeft-- <= 0 && _biomes.Length > 1)
        {
            var others = _biomes.Where(b => b != _biome).ToArray();
            _biome = others[_rng.RandiRange(0, others.Length - 1)];
            _zoneLeft = _rng.RandiRange(4, 7) - 1;
        }
        var piece = PickPiece(_biome);
        // Rooms are an odd number of wall lengths across so a doorway sits in the middle of a side.
        var span = Mathf.Clamp((int)Mathf.Ceil((2f * (piece.Clear - 1f)) / DungeonRoom.Wall), 3, 7);
        if (span % 2 == 0)
            span++;
        var turn = piece.Turn ? _rng.RandiRange(0, 3) * Mathf.Pi / 2f : 0f;

        if (Rooms.Count == 0)
        {
            var first = new DungeonRoom(0, Vector2.Zero, span, piece, _biome, turn) { In = new Vector2I(0, 1) };
            Rooms.Add(first);
            _taken.Add(first.Bounds.Grow(-0.1f));
            return;
        }

        var last = Rooms[^1];
        var forward = -last.In;
        var half = span * DungeonRoom.Wall / 2f;
        // Straight on is likelier than a turn; a turn back the way we came is never taken.
        var choices = new List<Vector2I> { forward, forward, TurnLeft(forward), TurnRight(forward) };
        Shuffle(choices);
        foreach (var length in new[] { 1, 2, 3, 4 })
        {
            foreach (var dir in choices)
            {
                if (dir == last.In)
                    continue;
                var corridorLength = (length + (_rng.Randf() < 0.4f ? 1 : 0)) * DungeonRoom.Wall;
                var door = last.Door(dir);
                var centre = door + (Vector2)dir * (corridorLength + half);
                var room = new Rect2(centre - Vector2.One * half, Vector2.One * half * 2f);
                var hall = new Rect2(door, Vector2.Zero).Expand(door + (Vector2)dir * corridorLength).Grow(DungeonRoom.Wall / 2f);
                // Keep a little gap round the new room; the corridor may touch its own two rooms.
                var recent = _taken.Count > 40 ? _taken.GetRange(_taken.Count - 40, 40) : _taken;
                if (recent.Any(r => r.Intersects(room.Grow(2f))) || recent.Take(recent.Count - 1).Any(r => r.Intersects(hall.Grow(-0.5f))))
                    continue;
                last.Out = dir;
                last.Corridor = (int)Mathf.Round(corridorLength / DungeonRoom.Wall);
                var next = new DungeonRoom(Rooms.Count, centre, span, piece, _biome, turn) { In = -dir };
                Rooms.Add(next);
                _taken.Add(hall.Grow(-0.5f));
                _taken.Add(room.Grow(-0.1f));
                return;
            }
        }
        // Boxed in (rare): carry straight on anyway with a long corridor.
        var straight = forward == last.In ? TurnLeft(forward) : forward;
        last.Out = straight;
        last.Corridor = 6;
        var far = last.Door(straight) + (Vector2)straight * (6 * DungeonRoom.Wall + half);
        var placed = new DungeonRoom(Rooms.Count, far, span, piece, _biome, turn) { In = -straight };
        Rooms.Add(placed);
        _taken.Add(placed.Bounds.Grow(-0.1f));
    }

    /// <summary>A piece of <paramref name="biome"/> not seen lately, by weight.</summary>
    private SetPiece PickPiece(Biome biome)
    {
        var all = _byBiome[biome.Id];
        var fresh = all.Where(p => !_recent.Contains(p.Id)).ToArray();
        var pool = fresh.Length > 0 ? fresh : all;
        var roll = _rng.Randf() * pool.Sum(p => p.Weight);
        var piece = pool.FirstOrDefault(p => (roll -= p.Weight) <= 0f) ?? pool[^1];
        _recent.Add(piece.Id);
        if (_recent.Count > Math.Max(4, all.Length * 2 / 3))
            _recent.RemoveAt(0);
        return piece;
    }

    private static Vector2I TurnLeft(Vector2I d) => new(d.Y, -d.X);

    private static Vector2I TurnRight(Vector2I d) => new(-d.Y, d.X);

    private void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.RandiRange(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>All four grid directions.</summary>
    public static IReadOnlyList<Vector2I> Directions => Dirs;
}

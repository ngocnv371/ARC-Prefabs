using Godot;
using ArcPrefabs.World;

namespace ArcPrefabs.Showcase;

/// <summary>
/// One room of the showcase's dungeon: a square of <see cref="Span"/> wall lengths a side with a
/// set piece in the middle and room around it (for a game, the floor monsters spawn on). It is
/// entered from <see cref="In"/> (grid directions, x and z) and has two ways on: <see cref="Out"/>,
/// the one the camera takes, with a corridor of <see cref="Corridor"/> wall lengths to the next
/// room, and <see cref="Branch"/>, a corridor of <see cref="BranchLength"/> lengths that ends in a
/// shut door (0: the door is in the room's wall), so every room offers a choice.
/// </summary>
public sealed record DungeonRoom(int Index, Vector2 Centre, int Span, SetPiece Piece, Biome Biome, float Turn)
{
    /// <summary>The length of one wall mesh (SM_Env_Wall_01), the dungeon's grid.</summary>
    public const float Wall = 5.24f;

    public Vector2I In { get; init; }
    public Vector2I Out { get; set; }
    public int Corridor { get; set; }
    public Vector2I Branch { get; set; }
    public int BranchLength { get; set; }

    public float Half => Span * Wall / 2f;

    public Rect2 Bounds => new(Centre - Vector2.One * Half, Vector2.One * Half * 2f);

    /// <summary>The middle of the doorway on the side facing <paramref name="dir"/>.</summary>
    public Vector2 Door(Vector2I dir) => Centre + (Vector2)dir * Half;
}

/// <summary>
/// Plans an endless chain of rooms: each holds one set piece of the current biome and has two
/// exits besides the way in; one leads on down a straight corridor to the next room, picked at
/// random, and the other down a short one to a shut door. Nothing crosses anything already laid,
/// and the biome changes every few rooms. The same seed gives the same dungeon.
/// </summary>
public sealed class DungeonLayout
{
    private static readonly Vector2I[] Dirs = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
    private const int Keep = 60; // how many laid rooms and corridors new ones keep clear of

    private readonly RandomNumberGenerator _rng;
    private readonly Dictionary<string, SetPiece[]> _byBiome;
    private readonly Biome[] _biomes;
    private readonly List<string> _recent = [];
    private readonly List<Rect2> _taken = []; // rooms and corridors already laid
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
        // Twice the piece's own span (rounded up to an odd number of wall lengths, so a doorway
        // sits in the middle of each side): room round the piece for a game to spawn monsters in.
        var fit = Mathf.Clamp((int)Mathf.Ceil(2f * (piece.Clear - 1f) / DungeonRoom.Wall), 3, 7);
        var span = 2 * fit + 1;
        var turn = piece.Turn ? _rng.RandiRange(0, 3) * Mathf.Pi / 2f : 0f;
        var half = span * DungeonRoom.Wall / 2f;

        if (Rooms.Count == 0)
        {
            var first = new DungeonRoom(0, Vector2.Zero, span, piece, _biome, turn) { In = new Vector2I(0, 1) };
            Rooms.Add(first);
            _taken.Add(first.Bounds.Grow(-0.1f));
            return;
        }

        var last = Rooms[^1];
        var forward = -last.In;
        // The two exits are two of the three sides other than the way in; which one the camera
        // takes is a coin toss, so straight on is no likelier than a turn.
        var sides = new List<Vector2I> { forward, TurnLeft(forward), TurnRight(forward) };
        Shuffle(sides);
        foreach (var length in new[] { 1, 2, 3, 4 })
        {
            foreach (var dir in sides)
            {
                var corridor = (length + (_rng.Randf() < 0.4f ? 1 : 0)) * DungeonRoom.Wall;
                var door = last.Door(dir);
                var centre = door + (Vector2)dir * (corridor + half);
                var room = new Rect2(centre - Vector2.One * half, Vector2.One * half * 2f);
                var hall = Hall(door, dir, corridor);
                if (Blocked(room.Grow(2f), skip: 0) || Blocked(hall, skip: 1))
                    continue;
                last.Out = dir;
                last.Corridor = (int)Mathf.Round(corridor / DungeonRoom.Wall);
                _taken.Add(hall);
                _taken.Add(room.Grow(-0.1f));
                PlanBranch(last, sides.Where(s => s != dir));
                Rooms.Add(new DungeonRoom(Rooms.Count, centre, span, piece, _biome, turn) { In = -dir });
                return;
            }
        }
        // Boxed in (rare): carry straight on anyway with a long corridor.
        last.Out = forward;
        last.Corridor = 8;
        var far = last.Door(forward) + (Vector2)forward * (8 * DungeonRoom.Wall + half);
        var placed = new DungeonRoom(Rooms.Count, far, span, piece, _biome, turn) { In = -forward };
        _taken.Add(placed.Bounds.Grow(-0.1f));
        PlanBranch(last, sides.Where(s => s != forward));
        Rooms.Add(placed);
    }

    /// <summary>The room's second exit: the longest dead-end corridor (up to four lengths) that fits on one of <paramref name="sides"/>.</summary>
    private void PlanBranch(DungeonRoom room, IEnumerable<Vector2I> sides)
    {
        var options = sides.ToList();
        foreach (var length in new[] { 4, 3, 2, 1 })
        {
            foreach (var dir in options)
            {
                var hall = Hall(room.Door(dir), dir, length * DungeonRoom.Wall);
                if (Blocked(hall, skip: 1))
                    continue;
                room.Branch = dir;
                room.BranchLength = length;
                _taken.Add(hall);
                return;
            }
        }
        // No room for a corridor: the shut door sits in the room's own wall.
        room.Branch = options[0];
        room.BranchLength = 0;
    }

    /// <summary>The ground a corridor of <paramref name="length"/> metres from <paramref name="door"/> takes, walls and all.</summary>
    private static Rect2 Hall(Vector2 door, Vector2I dir, float length) =>
        new Rect2(door, Vector2.Zero).Expand(door + (Vector2)dir * length).Grow(DungeonRoom.Wall / 2f).Grow(-0.5f);

    /// <summary>Whether <paramref name="area"/> overlaps anything laid lately, leaving out the last <paramref name="skip"/> (the room it leaves from).</summary>
    private bool Blocked(Rect2 area, int skip)
    {
        var from = Math.Max(0, _taken.Count - Keep);
        var to = _taken.Count;
        // The room being left is the newest room in the list; a corridor starts at its wall.
        var own = Rooms.Count > 0 ? Rooms[^1].Bounds.Grow(-0.1f) : (Rect2?)null;
        for (var i = from; i < to; i++)
        {
            if (skip > 0 && own is { } r && _taken[i] == r)
                continue;
            if (_taken[i].Intersects(area))
                return true;
        }
        return false;
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

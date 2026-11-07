using Godot;
using ArcPrefabs.World;

namespace ArcPrefabs.Showcase;

/// <summary>
/// One room of the showcase's dungeon: a square of <see cref="Span"/> wall lengths a side with a
/// set piece in the middle and room around it (for a game, the floor monsters spawn on). It is
/// entered from <see cref="In"/> (grid directions, x and z) and has two ways on: <see cref="Out"/>,
/// the one the camera takes, with a corridor of <see cref="Corridor"/> wall lengths to the next
/// room, and <see cref="Branch"/>, a corridor of <see cref="BranchLength"/> lengths that ends in a
/// shut door (0: the door is in the room's wall), so every room offers a choice. <see cref="Theme"/>
/// is the theme of the run of rooms it belongs to, if any, and <see cref="FirstOfRun"/> marks a run's first room.
/// </summary>
public sealed record DungeonRoom(int Index, Vector2 Centre, int Span, SetPiece Piece, Biome Biome, float Turn)
{
    public SetPieceTheme? Theme { get; init; }
    public bool FirstOfRun { get; init; }

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
/// Plans an endless chain of rooms: each holds one set piece and has two exits besides the way in;
/// one leads on down a straight corridor to the next room, picked at random, and the other down a
/// short one to a shut door. Nothing crosses anything already laid. Rooms come in runs of one theme
/// (data/themes.json: a few in a row, each a different piece of it, and the room's biome is its
/// piece's); without themes they come in runs of one biome. The same seed gives the same dungeon.
/// </summary>
public sealed class DungeonLayout
{
    private static readonly Vector2I[] Dirs = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
    private const int Keep = 60; // how many laid rooms and corridors new ones keep clear of

    /// <summary>A run of rooms: a theme's pieces, or (without themes) a biome's.</summary>
    private sealed record Section(SetPieceTheme? Theme, Biome? Biome, SetPiece[] Pool);

    private readonly RandomNumberGenerator _rng;
    private readonly Biome[] _biomes;
    private readonly Section[] _sections;
    private readonly Vector2I _run;
    private readonly List<string> _recent = [];
    private readonly List<Section> _recentSections = [];
    private readonly List<Rect2> _taken = []; // rooms and corridors already laid
    private Section? _section;
    private int _left; // rooms left in the current run
    private bool _fresh; // the next room starts a run

    public List<DungeonRoom> Rooms { get; } = [];

    /// <param name="only">Theme ids to draw from; empty means all.</param>
    public DungeonLayout(IReadOnlyList<SetPiece> pieces, IEnumerable<Biome> biomes, SetPieceThemes themes, ulong seed, string[]? only = null)
    {
        _rng = new RandomNumberGenerator { Seed = seed };
        _biomes = biomes.ToArray();
        var ids = _biomes.Select(b => b.Id).ToHashSet();
        var usable = pieces.Where(p => p.Props.Length > 0 && p.Biomes.Any(ids.Contains)).ToArray();
        // A theme needs two pieces at least to make a run.
        _sections = themes.All
            .Where(t => only is not { Length: > 0 } || only.Contains(t.Id))
            .Select(t => new Section(t, null, usable.Where(p => p.Themes.Contains(t.Id)).ToArray()))
            .Where(s => s.Pool.Length >= 2)
            .ToArray();
        _run = themes.Run;
        if (_sections.Length == 0)
        {
            _sections = _biomes.Select(b => new Section(null, b, usable.Where(p => p.Biomes.Contains(b.Id)).ToArray())).Where(s => s.Pool.Length > 0).ToArray();
            _run = new Vector2I(4, 7);
        }
        if (_sections.Length == 0)
            throw new InvalidOperationException("no set pieces in the showcase's biomes");
    }

    /// <summary>Moves on to a section not seen in the last few, for a run of a few rooms.</summary>
    private void NextSection()
    {
        var fresh = _sections.Where(s => !_recentSections.Contains(s)).ToArray();
        var pool = fresh.Length > 0 ? fresh : _sections.Where(s => s != _section).DefaultIfEmpty(_sections[0]).ToArray();
        _section = pool[_rng.RandiRange(0, pool.Length - 1)];
        _recentSections.Add(_section);
        if (_recentSections.Count > Math.Min(4, _sections.Length - 1))
            _recentSections.RemoveAt(0);
        // A theme's run never repeats a piece, so it is no longer than the theme has pieces.
        var length = _rng.RandiRange(_run.X, _run.Y);
        _left = _section.Theme != null ? Math.Min(length, _section.Pool.Length) : length;
        _fresh = true;
    }

    /// <summary>Plans rooms until there are at least <paramref name="count"/>.</summary>
    public void PlanTo(int count)
    {
        while (Rooms.Count < count)
            PlanNext();
    }

    private void PlanNext()
    {
        if (_section == null || _left <= 0)
            NextSection();
        _left--;
        var section = _section!;
        var piece = PickPiece(section);
        var biome = section.Biome ?? _biomes.First(b => piece.Biomes.Contains(b.Id));
        var theme = section.Theme;
        var opens = _fresh;
        _fresh = false;
        // Twice the piece's own span (rounded up to an odd number of wall lengths, so a doorway
        // sits in the middle of each side): room round the piece for a game to spawn monsters in.
        var fit = Mathf.Clamp((int)Mathf.Ceil(2f * (piece.Clear - 1f) / DungeonRoom.Wall), 3, 7);
        var span = 2 * fit + 1;
        var turn = piece.Turn ? _rng.RandiRange(0, 3) * Mathf.Pi / 2f : 0f;
        var half = span * DungeonRoom.Wall / 2f;

        if (Rooms.Count == 0)
        {
            var first = new DungeonRoom(0, Vector2.Zero, span, piece, biome, turn) { In = new Vector2I(0, 1), Theme = theme, FirstOfRun = opens };
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
                Rooms.Add(new DungeonRoom(Rooms.Count, centre, span, piece, biome, turn) { In = -dir, Theme = theme, FirstOfRun = opens });
                return;
            }
        }
        // Boxed in (rare): carry straight on anyway with a long corridor.
        last.Out = forward;
        last.Corridor = 8;
        var far = last.Door(forward) + (Vector2)forward * (8 * DungeonRoom.Wall + half);
        var placed = new DungeonRoom(Rooms.Count, far, span, piece, biome, turn) { In = -forward, Theme = theme, FirstOfRun = opens };
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

    /// <summary>A piece of <paramref name="section"/> not seen lately, by weight (in a theme's run, never one already in it).</summary>
    private SetPiece PickPiece(Section section)
    {
        var all = section.Pool;
        if (section.Theme != null)
        {
            var inRun = Rooms.TakeLast(_run.Y).Where(r => r.Theme == section.Theme).Select(r => r.Piece.Id).ToHashSet();
            all = all.Where(p => !inRun.Contains(p.Id)).DefaultIfEmpty(all[0]).ToArray();
        }
        var fresh = all.Where(p => !_recent.Contains(p.Id)).ToArray();
        var pool = fresh.Length > 0 ? fresh : all;
        var roll = _rng.Randf() * pool.Sum(p => p.Weight);
        var piece = pool.FirstOrDefault(p => (roll -= p.Weight) <= 0f) ?? pool[^1];
        _recent.Add(piece.Id);
        if (_recent.Count > 16) // about the last four runs
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

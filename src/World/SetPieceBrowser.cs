using Godot;
using ArcPrefabs.UI;

namespace ArcPrefabs.World;

/// <summary>
/// The painter's Browse tab: every piece in data/setpieces as a card in a grid filling the
/// window (its picture from the game's camera, biome, prop count, clear radius, weight, tags and lore),
/// with a search box and biome, theme and tag filters at the side. Biomes add up (a piece in any chosen
/// biome shows), and so do themes; tags narrow (a piece needs every chosen tag); the search matches a name, a tag or
/// the lore. Each tag shows how many pieces the other filters leave it. Clicking a card asks
/// <see cref="Open"/> to open it in the painter.
/// </summary>
public partial class SetPieceBrowser : PanelContainer
{
    private static readonly Vector2I Thumb = new(288, 216);
    private const int Gap = 12;

    private readonly GridContainer _grid = new();
    private readonly ScrollContainer _scroll = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly HFlowContainer _biomeChips = new();
    private readonly HFlowContainer _themeChips = new();
    private readonly HFlowContainer _tagChips = new();
    private readonly LineEdit _search = new() { PlaceholderText = "search names, tags and lore", ClearButtonEnabled = true };
    private readonly Label _count = UiStyle.Label("", 13, UiStyle.Dim);
    private readonly Label _notice = UiStyle.Label("", 13, UiStyle.Accent, wrap: true);
    private readonly HashSet<string> _biomes = [];
    private readonly HashSet<string> _themes = [];
    private readonly HashSet<string> _tags = [];
    private IReadOnlyList<SetPieceTheme> _themeList = [];
    private readonly Dictionary<string, (DateTime Stamp, Texture2D Texture)> _pictures = [];
    private IReadOnlyList<SetPiece> _pieces = [];
    private string? _current;

    public required WorldMap Map { get; init; }
    public required ThumbnailStudio Studio { get; init; }

    /// <summary>Opens a piece in the painter: null when it did, otherwise why not (shown above the grid).</summary>
    public required Func<SetPiece, string?> Open { get; init; }

    /// <summary>The piece open in the painter, outlined in the grid.</summary>
    public string? Current
    {
        get => _current;
        set
        {
            _current = value;
            if (IsInsideTree())
                Fill();
        }
    }

    public override void _Ready()
    {
        var panel = UiStyle.Box(UiStyle.Panel with { A = 1f }, radius: 0, borderWidth: 0);
        panel.ContentMarginTop = 56; // under the Paint | Browse tabs
        panel.ContentMarginLeft = panel.ContentMarginRight = 20;
        AddThemeStyleboxOverride("panel", panel);
        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 18);
        AddChild(split);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(260f, 0f) };
        side.AddThemeConstantOverride("separation", 8);
        split.AddChild(side);
        var header = new HBoxContainer();
        var title = UiStyle.Label("SET PIECES", 16, UiStyle.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(_count);
        side.AddChild(header);
        _search.TextChanged += _ => Fill();
        _search.TextSubmitted += _ => _search.ReleaseFocus();
        side.AddChild(_search);
        side.AddChild(UiStyle.Label("BIOMES", 13, UiStyle.Dim));
        side.AddChild(_biomeChips);
        side.AddChild(UiStyle.Label("THEMES", 13, UiStyle.Dim));
        side.AddChild(_themeChips);
        side.AddChild(UiStyle.Label("TAGS", 13, UiStyle.Dim));
        side.AddChild(_tagChips);
        var clear = new Button { Text = "Clear filters", FocusMode = FocusModeEnum.None };
        clear.Pressed += () =>
        {
            _biomes.Clear();
            _themes.Clear();
            _tags.Clear();
            _search.Text = "";
            Fill();
        };
        side.AddChild(clear);
        side.AddChild(UiStyle.Label("Click a piece to open it in the painter.\nB or Esc goes back to painting.", 12, UiStyle.Dim, wrap: true));

        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(main);
        main.AddChild(_notice);
        _grid.AddThemeConstantOverride("h_separation", Gap);
        _grid.AddThemeConstantOverride("v_separation", Gap);
        _scroll.AddChild(_grid);
        _scroll.Resized += FitColumns;
        main.AddChild(_scroll);
        Refresh();
    }

    /// <summary>Rereads the folder (after a save) and forgets pictures of pieces that are gone.</summary>
    public void Refresh()
    {
        var before = _pieces;
        _pieces = DirAccess.DirExistsAbsolute(SetPieces.Folder) ? SetPieces.ReadAll() : [];
        _themeList = SetPieceThemes.Load(Map).All;
        foreach (var gone in before.Where(old => _pieces.All(p => p.Id != old.Id)))
        {
            Studio.ForgetPiece(gone.Id);
            _pictures.Remove(gone.Id);
        }
        _notice.Text = "";
        Fill();
    }

    private bool Matches(SetPiece piece, bool tags = true, bool biomes = true, bool themes = true)
    {
        if (biomes && _biomes.Count > 0 && !piece.Biomes.Any(_biomes.Contains))
            return false;
        if (themes && _themes.Count > 0 && !piece.Themes.Any(_themes.Contains))
            return false;
        if (tags && !_tags.All(piece.Tags.Contains))
            return false;
        var search = _search.Text.Trim().ToLowerInvariant();
        return search.Length == 0 || piece.Id.Contains(search) || piece.Tags.Any(t => t.Contains(search)) || piece.Lore.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Fill()
    {
        foreach (var child in _grid.GetChildren())
            child.QueueFree();
        foreach (var child in _biomeChips.GetChildren().Concat(_themeChips.GetChildren()).Concat(_tagChips.GetChildren()))
            child.QueueFree();

        foreach (var biome in Map.Biomes)
        {
            var count = _pieces.Count(p => p.Biomes.Contains(biome.Id) && Matches(p, biomes: false));
            _biomeChips.AddChild(Chip($"{biome.Name} {count}", _biomes.Contains(biome.Id), count > 0, () => Toggle(_biomes, biome.Id)));
        }
        // Themes the file names, then any a piece names that it does not.
        var named = _themeList.Select(t => (t.Id, t.Name)).ToList();
        named.AddRange(_pieces.SelectMany(p => p.Themes).Distinct().Where(id => named.All(n => n.Id != id)).Order().Select(id => (id, id)));
        foreach (var (id, name) in named)
        {
            var count = _pieces.Count(p => p.Themes.Contains(id) && Matches(p, themes: false));
            _themeChips.AddChild(Chip($"{name} {count}", _themes.Contains(id), count > 0, () => Toggle(_themes, id)));
        }
        foreach (var tag in _pieces.SelectMany(p => p.Tags).Distinct().Order())
        {
            // How many pieces would show with this tag added to the others.
            var count = _pieces.Count(p => p.Tags.Contains(tag) && Matches(p));
            _tagChips.AddChild(Chip($"{tag} {count}", _tags.Contains(tag), count > 0, () => Toggle(_tags, tag)));
        }

        int Order(SetPiece p) => Map.Biomes.ToList().FindIndex(b => p.Biomes.Contains(b.Id)) is var i and >= 0 ? i : int.MaxValue;
        var shown = _pieces.Where(p => Matches(p)).OrderBy(Order).ThenBy(p => p.Id).ToArray();
        foreach (var piece in shown)
            _grid.AddChild(Card(piece));
        if (shown.Length == 0)
            _grid.AddChild(UiStyle.Label(_pieces.Count == 0 ? "No set pieces yet. Paint one and press ctrl+S." : "Nothing matches these filters.", 14, UiStyle.Dim));
        _count.Text = $"{shown.Length} of {_pieces.Count}";
        FitColumns();
    }

    private void Toggle(HashSet<string> set, string value)
    {
        if (!set.Remove(value))
            set.Add(value);
        Fill();
    }

    private void FitColumns() => _grid.Columns = Mathf.Max(1, (int)((_scroll.Size.X - 16f + Gap) / (Thumb.X + 16 + Gap)));

    private static Button Chip(string text, bool on, bool any, Action toggle)
    {
        static StyleBoxFlat Box(Color fill, Color border)
        {
            var box = UiStyle.Box(fill, border, 10, 1);
            box.ContentMarginLeft = box.ContentMarginRight = 9;
            box.ContentMarginTop = box.ContentMarginBottom = 3;
            return box;
        }
        var chip = new Button { Text = text, ToggleMode = true, ButtonPressed = on, FocusMode = FocusModeEnum.None, Disabled = !any && !on };
        chip.AddThemeFontSizeOverride("font_size", 13);
        chip.AddThemeStyleboxOverride("normal", Box(UiStyle.Panel.Lightened(0.06f), UiStyle.Border with { A = 0.35f }));
        chip.AddThemeStyleboxOverride("hover", Box(UiStyle.Panel.Lightened(0.14f), UiStyle.Accent));
        chip.AddThemeStyleboxOverride("pressed", Box(UiStyle.Accent with { A = 0.3f }, UiStyle.Accent));
        chip.AddThemeStyleboxOverride("hover_pressed", Box(UiStyle.Accent with { A = 0.4f }, UiStyle.Accent));
        chip.AddThemeStyleboxOverride("disabled", Box(UiStyle.Panel, UiStyle.Border with { A = 0.15f }));
        chip.Toggled += _ => toggle();
        return chip;
    }

    /// <summary>A card: a button filling it, under the picture and text, which let the mouse through to it.</summary>
    private Control Card(SetPiece piece)
    {
        var current = piece.Id == Current;
        var biome = Map.Biomes.FirstOrDefault(b => piece.Biomes.Contains(b.Id)) ?? Map.Biomes[0];
        var holder = new PanelContainer();
        holder.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var card = new Button { FocusMode = FocusModeEnum.None, TooltipText = $"Open {piece.Id} in the painter" };
        holder.AddChild(card);
        card.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.Panel.Lightened(0.05f), current ? UiStyle.Accent : UiStyle.Border with { A = 0.3f }, 6, current ? 2 : 1));
        card.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.Panel.Lightened(0.12f), UiStyle.Accent, 6, 1));
        card.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.Panel.Lightened(0.2f), UiStyle.Accent, 6, 2));
        card.Pressed += () => _notice.Text = Open(piece) ?? "";

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(new TextureRect
        {
            Texture = Picture(piece, biome),
            CustomMinimumSize = Thumb,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var biomes = string.Join(", ", Map.Biomes.Where(b => piece.Biomes.Contains(b.Id)).Select(b => b.Name));
        column.AddChild(Ignore(UiStyle.Label(piece.Id, 15, current ? UiStyle.Accent : UiStyle.Text)));
        column.AddChild(Ignore(UiStyle.Label($"{biomes} · {piece.Props.Length} props · {piece.Clear:0.#} m", 12, UiStyle.Dim)));
        column.AddChild(Ignore(UiStyle.Label($"weight {piece.Weight:0.##} · {(piece.Turn ? "turns" : "fixed")}", 12, UiStyle.Dim)));
        if (piece.Themes.Length > 0)
        {
            var names = piece.Themes.Select(id => _themeList.FirstOrDefault(t => t.Id == id)?.Name ?? id);
            var themes = Ignore(UiStyle.Label(string.Join(" · ", names), 12, UiStyle.Accent, wrap: true));
            themes.CustomMinimumSize = new Vector2(Thumb.X, 0f);
            column.AddChild(themes);
        }
        var tags = Ignore(UiStyle.Label(piece.Tags.Length > 0 ? string.Join("  ", piece.Tags.Select(t => "#" + t)) : "no tags", 12, piece.Tags.Length > 0 ? UiStyle.Xp : UiStyle.Dim, wrap: true));
        tags.CustomMinimumSize = new Vector2(Thumb.X, 0f);
        column.AddChild(tags);
        if (piece.Lore.Length > 0)
        {
            var lore = Ignore(UiStyle.Label(piece.Lore, 12, UiStyle.Dim, wrap: true));
            lore.CustomMinimumSize = new Vector2(Thumb.X, 0f);
            column.AddChild(lore);
        }

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 8);
        margin.AddChild(column);
        holder.AddChild(margin);
        return holder;
    }

    private static Control Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        return c;
    }

    /// <summary>
    /// The picture saved next to the piece's JSON when it is up to date, so the grid needs no
    /// live studio for each card; otherwise (a piece saved a moment ago) the studio draws it.
    /// </summary>
    private Texture2D Picture(SetPiece piece, Biome biome)
    {
        var json = ProjectSettings.GlobalizePath(SetPieces.Folder + piece.Id + ".json");
        var png = Path.ChangeExtension(json, ".png");
        var written = File.GetLastWriteTimeUtc(json);
        if (File.Exists(png) && File.GetLastWriteTimeUtc(png) is var drawn && drawn >= written)
        {
            if (_pictures.TryGetValue(piece.Id, out var known) && known.Stamp == drawn)
                return known.Texture;
            var image = Image.LoadFromFile(png);
            if (image != null && !image.IsEmpty())
            {
                image.Resize(Thumb.X * 2, Thumb.Y * 2, Image.Interpolation.Lanczos);
                var texture = ImageTexture.CreateFromImage(image);
                _pictures[piece.Id] = (drawn, texture);
                return texture;
            }
        }
        return Studio.Piece(piece, biome, written, Thumb * 2);
    }
}

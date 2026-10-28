using Godot;
using ArcPrefabs.UI;

namespace ArcPrefabs.World;

/// <summary>
/// The painter's catalogue of data/setpieces, under the piece controls in its right column
/// (B collapses it): every piece grouped by biome, each
/// with a thumbnail rendered from the game's camera angle on its biome's grass, its prop count,
/// clear radius, weight and whether it turns (a piece for several biomes is listed under each).
/// A biome filter narrows the list; clicking a piece opens it in the painter.
/// </summary>
public partial class SetPieceBrowser : VBoxContainer
{
    private static readonly Vector2I Thumb = new(136, 100);

    private readonly VBoxContainer _rows = new();
    private readonly OptionButton _filter = new() { FocusMode = FocusModeEnum.None };
    private readonly Label _count = UiStyle.Label("", 13, UiStyle.Dim);
    private IReadOnlyList<SetPiece> _pieces = [];

    public required WorldMap Map { get; init; }
    public required ThumbnailStudio Studio { get; init; }

    /// <summary>The piece open in the painter, highlighted in the list.</summary>
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

    private string? _current;

    public event Action<SetPiece>? Opened;

    public override void _Ready()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
        var column = this;
        var header = new HBoxContainer();
        var title = UiStyle.Label("SET PIECES  (B)", 16, UiStyle.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(_count);
        column.AddChild(header);

        _filter.AddItem("Every biome");
        foreach (var biome in Map.Biomes)
            _filter.AddItem(biome.Name);
        _filter.ItemSelected += _ => Fill();
        column.AddChild(_filter);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        column.AddChild(scroll);
        Refresh();
    }

    /// <summary>Rereads the folder (after a save) and redraws thumbnails of pieces that changed.</summary>
    public void Refresh()
    {
        var before = _pieces;
        _pieces = DirAccess.DirExistsAbsolute(SetPieces.Folder) ? SetPieces.ReadAll() : [];
        foreach (var gone in before.Where(old => _pieces.All(p => p.Id != old.Id)))
            Studio.ForgetPiece(gone.Id);
        Fill();
    }

    private void Fill()
    {
        foreach (var row in _rows.GetChildren())
            row.QueueFree();

        var only = _filter.Selected > 0 ? Map.Biomes[_filter.Selected - 1] : null;
        var shown = new HashSet<string>(); // a piece for several biomes counts once
        foreach (var biome in Map.Biomes)
        {
            if (only != null && biome != only)
                continue;
            var pieces = _pieces.Where(p => p.Biomes.Contains(biome.Id)).ToArray();
            if (pieces.Length == 0)
                continue;
            _rows.AddChild(UiStyle.Label(biome.Name.ToUpperInvariant(), 13, UiStyle.Dim));
            foreach (var piece in pieces)
            {
                _rows.AddChild(Row(piece, biome));
                shown.Add(piece.Id);
            }
        }
        if (shown.Count == 0)
            _rows.AddChild(UiStyle.Label("No set pieces here yet. Paint one and press ctrl+S.", 13, UiStyle.Dim, wrap: true));
        _count.Text = $"{shown.Count} of {_pieces.Count}";
    }

    private Button Row(SetPiece piece, Biome biome)
    {
        var current = piece.Id == Current;
        var button = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0f, Thumb.Y + 12f), TooltipText = $"Open {piece.Id}" };
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.Panel.Lightened(0.05f), current ? UiStyle.Accent : UiStyle.Border with { A = 0.3f }, 6, current ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.Panel.Lightened(0.12f), UiStyle.Accent, 6, 1));
        button.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.Panel.Lightened(0.2f), UiStyle.Accent, 6, 2));
        button.Pressed += () => Opened?.Invoke(piece);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = row.OffsetTop = 6f;
        row.OffsetRight = row.OffsetBottom = -6f;
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(new TextureRect
        {
            Texture = Thumbnail(piece, biome),
            CustomMinimumSize = Thumb,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddChild(Ignore(UiStyle.Label(piece.Id, 15, current ? UiStyle.Accent : UiStyle.Text)));
        text.AddChild(Ignore(UiStyle.Label($"{piece.Props.Length} props · {piece.Clear:0.#} m", 12, UiStyle.Dim)));
        text.AddChild(Ignore(UiStyle.Label($"weight {piece.Weight:0.##} · {(piece.Turn ? "turns" : "fixed")}", 12, UiStyle.Dim)));
        if (piece.Biomes.Length > 1)
            text.AddChild(Ignore(UiStyle.Label(string.Join(", ", piece.Biomes), 12, UiStyle.Dim)));
        row.AddChild(text);
        button.AddChild(row);
        return button;
    }

    private static Control Ignore(Control c)
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        return c;
    }

    /// <summary>The piece's picture, redrawn when its file is written again.</summary>
    private Texture2D Thumbnail(SetPiece piece, Biome biome)
    {
        var written = File.GetLastWriteTimeUtc(ProjectSettings.GlobalizePath(SetPieces.Folder + piece.Id + ".json"));
        return Studio.Piece(piece, biome, written, Thumb * 2);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.B } && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            Visible = !Visible;
            GetViewport().SetInputAsHandled();
        }
    }
}

using Godot;

namespace ArcPrefabs.UI;

/// <summary>The panels' look: ARC's dark violet panels with a soft accent border.</summary>
public static class UiStyle
{
    public static readonly Color Panel = new(0.06f, 0.045f, 0.11f, 0.88f);
    public static readonly Color Border = new(0.55f, 0.45f, 0.95f, 0.7f);
    public static readonly Color Text = new(0.94f, 0.92f, 1f);
    public static readonly Color Dim = new(0.66f, 0.62f, 0.78f);
    public static readonly Color Accent = new(1f, 0.8f, 0.35f);
    public static readonly Color Xp = new(0.4f, 0.75f, 1f);

    public static StyleBoxFlat Box(Color fill, Color? border = null, int radius = 8, int borderWidth = 2) => new()
    {
        BgColor = fill,
        BorderColor = border ?? Border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 10,
        ContentMarginBottom = 10,
    };

    /// <summary>A panel docked to a window edge: opaque, square, with the border only on its inner side.</summary>
    public static StyleBoxFlat Dock(bool left)
    {
        var box = Box(Panel with { A = 1f }, radius: 0);
        box.BorderWidthLeft = left ? 0 : 2;
        box.BorderWidthRight = left ? 2 : 0;
        box.BorderWidthTop = 0;
        box.BorderWidthBottom = 0;
        box.ContentMarginTop = 14;
        box.ContentMarginBottom = 14;
        return box;
    }

    public static Label Label(string text, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left, bool wrap = false)
    {
        var label = new Label { Text = text, HorizontalAlignment = align, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Text);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
        label.AddThemeConstantOverride("outline_size", size >= 20 ? 6 : 4);
        return label;
    }
}

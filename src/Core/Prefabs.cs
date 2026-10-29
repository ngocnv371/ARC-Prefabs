using Godot;
using ArcPrefabs.Render;
using ArcPrefabs.World;

namespace ArcPrefabs.Core;

/// <summary>
/// Opens the <see cref="SetPiecePainter"/>. Options (after <c>--</c>): <c>--paint=name</c>
/// (open data/setpieces/name.json), <c>--biome=id</c> (start a new piece in that biome),
/// <c>--seed=N</c> (which map to find regions on), <c>--render[=name]</c> (write the pictures
/// next to the pieces' JSON, or one piece's, and quit).
/// </summary>
public partial class Prefabs : Node3D
{
    public override void _Ready()
    {
        var values = new Dictionary<string, string>();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--"))
                continue;
            var eq = arg.IndexOf('=');
            values[eq < 0 ? arg[2..] : arg[2..eq]] = eq < 0 ? "" : arg[(eq + 1)..];
        }

        Controls.Register();
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.02f, 0.015f, 0.04f),
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        var map = WorldMap.Load(int.TryParse(values.GetValueOrDefault("seed"), out var seed) ? seed : 1);
        if (values.TryGetValue("render", out var render))
        {
            _ = Render(map, render);
            return;
        }
        var paint = values.GetValueOrDefault("paint");
        AddChild(new SetPiecePainter
        {
            Name = "Painter",
            Map = map,
            Open = paint is { Length: > 0 } ? paint : null,
            Biome = values.GetValueOrDefault("biome"),
        });
    }

    /// <summary>Writes the picture next to every piece's JSON (or only <paramref name="only"/>'s), then quits.</summary>
    private async Task Render(WorldMap map, string only)
    {
        var studio = new ThumbnailStudio { Name = "Thumbnails", Map = map };
        AddChild(studio);
        foreach (var piece in SetPieces.ReadAll().Where(p => only.Length == 0 || p.Id == only))
        {
            var biome = map.Biomes.FirstOrDefault(b => piece.Biomes.Contains(b.Id)) ?? map.Biomes[0];
            var path = SetPieces.Folder + piece.Id + ".png";
            await studio.SavePiece(piece, biome, path, ThumbnailStudio.PieceImage);
            GD.Print($"rendered {path}");
        }
        GetTree().Quit();
    }

    // The model prototypes and shared materials live outside the tree; without this they
    // outlive the engine and Godot reports them all as leaked at exit.
    public override void _ExitTree()
    {
        SyntyModels.Release();
        ArcMaterials.Release();
    }
}

using Godot;
using ArcPrefabs.Render;
using ArcPrefabs.World;

namespace ArcPrefabs.Core;

/// <summary>
/// Opens the <see cref="SetPiecePainter"/>. Options (after <c>--</c>): <c>--paint=name</c>
/// (open data/setpieces/name.json), <c>--biome=id</c> (start a new piece in that biome),
/// <c>--seed=N</c> (which map to find regions on).
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
        var paint = values.GetValueOrDefault("paint");
        AddChild(new SetPiecePainter
        {
            Name = "Painter",
            Map = WorldMap.Load(int.TryParse(values.GetValueOrDefault("seed"), out var seed) ? seed : 1),
            Open = paint is { Length: > 0 } ? paint : null,
            Biome = values.GetValueOrDefault("biome"),
        });
    }

    // The model prototypes and shared materials live outside the tree; without this they
    // outlive the engine and Godot reports them all as leaked at exit.
    public override void _ExitTree()
    {
        SyntyModels.Release();
        ArcMaterials.Release();
    }
}

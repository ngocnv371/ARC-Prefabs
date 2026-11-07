using Godot;

namespace ArcPrefabs.Showcase;

/// <summary>A point of light in the dungeon: where, what colour, how far and how bright.</summary>
public readonly record struct DungeonLight(Vector3 Position, Color Color, float Range, float Energy);

/// <summary>Which props give light and how (data/lights.json): the first emitter whose match is in the mesh's name.</summary>
public sealed record LightEmitter(string Match, float Y, Color Color, float Range, float Energy, float Glow);

/// <summary>
/// The showcase's lighting (data/lights.json). ARC's shaders are unlit, so every light near the
/// camera is baked into a map over the ground that the lit shaders (arc_lit.gdshaderinc) read
/// through shader globals; the map follows the camera and is redrawn when the lights change.
/// </summary>
public sealed class DungeonLights
{
    private const float Texel = 0.5f; // metres per texel of the light map
    private const int Size = 384; // texels across, so the map covers 192 m around the camera

    private readonly float[] _sum = new float[Size * Size * 3];
    private readonly byte[] _bytes = new byte[Size * Size * 3 * sizeof(float)];
    private ImageTexture? _texture;
    private Vector2 _corner;

    public float Ambient { get; private init; } = 0.13f;
    public float FadeStart { get; private init; } = 24f;
    public float FadeEnd { get; private init; } = 58f;
    public float Flicker { get; private init; } = 0.18f;
    public IReadOnlyList<LightEmitter> Emitters { get; private init; } = [];
    public IReadOnlyDictionary<string, (Color Fill, float Energy)> Fills { get; private init; } = new Dictionary<string, (Color, float)>();

    public static DungeonLights Load(string path = "res://data/lights.json")
    {
        var root = Json.ParseString(Godot.FileAccess.GetFileAsString(path)).AsGodotDictionary();
        static Color Rgb(Variant v)
        {
            var c = v.AsGodotArray();
            return new Color(c[0].AsSingle(), c[1].AsSingle(), c[2].AsSingle());
        }
        var fills = new Dictionary<string, (Color, float)>();
        foreach (var (id, value) in root["biomes"].AsGodotDictionary())
        {
            var b = value.AsGodotDictionary();
            fills[id.AsString()] = (Rgb(b["fill"]), b["energy"].AsSingle());
        }
        return new DungeonLights
        {
            Ambient = root["ambient"].AsSingle(),
            FadeStart = root["fadeStart"].AsSingle(),
            FadeEnd = root["fadeEnd"].AsSingle(),
            Flicker = root["flicker"].AsSingle(),
            Fills = fills,
            Emitters = root["emitters"].AsGodotArray().Select(v =>
            {
                var e = v.AsGodotDictionary();
                return new LightEmitter(e["match"].AsString(), e["y"].AsSingle(), Rgb(e["color"]), e["range"].AsSingle(), e["energy"].AsSingle(), e["glow"].AsSingle());
            }).ToArray(),
        };
    }

    /// <summary>The emitter for <paramref name="mesh"/>, if it gives light.</summary>
    public LightEmitter? EmitterFor(string mesh) => Emitters.FirstOrDefault(e => mesh.Contains(e.Match));

    /// <summary>Sets the shader globals that do not change while the dungeon runs.</summary>
    public void Apply()
    {
        RenderingServer.GlobalShaderParameterSet("arc_light_mood", new Vector4(Ambient, FadeStart, FadeEnd, Flicker));
    }

    /// <summary>Whether <paramref name="centre"/> has moved far enough from the map's middle that it should be redrawn there.</summary>
    public bool Drifted(Vector2 centre) => _texture == null || centre.DistanceTo(_corner + Vector2.One * Size * Texel / 2f) > Size * Texel / 5f;

    /// <summary>Redraws the light map around <paramref name="centre"/> (world XZ) from <paramref name="lights"/>.</summary>
    public void Bake(Vector2 centre, IEnumerable<DungeonLight> lights)
    {
        _corner = new Vector2(Mathf.Floor(centre.X / Texel), Mathf.Floor(centre.Y / Texel)) * Texel - Vector2.One * Size * Texel / 2f;
        Array.Clear(_sum);
        foreach (var light in lights)
        {
            var at = (new Vector2(light.Position.X, light.Position.Z) - _corner) / Texel;
            var r = light.Range / Texel;
            int x0 = Math.Max(0, (int)(at.X - r)), x1 = Math.Min(Size - 1, (int)(at.X + r) + 1);
            int y0 = Math.Max(0, (int)(at.Y - r)), y1 = Math.Min(Size - 1, (int)(at.Y + r) + 1);
            float cr = light.Color.R * light.Energy, cg = light.Color.G * light.Energy, cb = light.Color.B * light.Energy;
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var dx = x + 0.5f - at.X;
                    var dy = y + 0.5f - at.Y;
                    var d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    if (d >= 1f)
                        continue;
                    var f = (1f - d) * (1f - d);
                    var i = (y * Size + x) * 3;
                    _sum[i] += cr * f;
                    _sum[i + 1] += cg * f;
                    _sum[i + 2] += cb * f;
                }
            }
        }

        Buffer.BlockCopy(_sum, 0, _bytes, 0, _bytes.Length);
        var image = Image.CreateFromData(Size, Size, false, Image.Format.Rgbf, _bytes);
        if (_texture == null)
        {
            _texture = ImageTexture.CreateFromImage(image);
            RenderingServer.GlobalShaderParameterSet("arc_light_map", _texture);
        }
        else
        {
            _texture.Update(image);
        }
        RenderingServer.GlobalShaderParameterSet("arc_light_rect", new Vector4(_corner.X, _corner.Y, Size * Texel, Size * Texel));
    }
}

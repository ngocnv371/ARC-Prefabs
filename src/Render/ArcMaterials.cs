using Godot;

namespace ArcPrefabs.Render;

/// <summary>
/// Builds and shares the ARC-look materials. Colours are display values, as ARC's GL renderer
/// used them; the shaders decode the result for Godot. One material per distinct parameter
/// set, so parts that look the same batch together.
/// </summary>
public static class ArcMaterials
{
    // Loaded on first use and dropped by Release(), so nothing outlives the engine at exit.
    private static readonly Dictionary<string, Shader> Shaders = [];
    private static readonly Dictionary<string, ShaderMaterial> Cache = [];

    private static Shader ModelShader => Load("arc_model");
    private static Shader ModelStaticShader => Load("arc_model_static");
    private static Shader ModelBlendShader => Load("arc_model_blend");
    private static Shader AnomalyShader => Load("arc_anomaly");
    private static Shader CrystalShader => Load("arc_crystal");
    private static Shader FlatShader => Load("arc_flat");
    private static Shader GroundShader => Load("arc_ground");
    private static Shader GrassShader => Load("arc_grass");

    /// <summary>Drops every cached shader and material. Call when the game shuts down.</summary>
    public static void Release()
    {
        Cache.Clear();
        Shaders.Clear();
    }

    private static Shader Load(string name)
    {
        if (!Shaders.TryGetValue(name, out var shader))
            Shaders[name] = shader = GD.Load<Shader>($"res://shaders/{name}.gdshader");
        return shader;
    }

    /// <summary>ARC's authored-model material. <paramref name="flipV"/> is for the anomaly GLBs.</summary>
    public static ShaderMaterial Model(
        Color baseColor,
        Color accent,
        float rim,
        Texture2D? texture = null,
        bool blend = false,
        float alpha = 1f,
        bool flipV = false,
        bool scenery = false
    )
    {
        // Blended parts draw both sides (as ARC did); opaque parts cull back faces.
        // Scenery skips the per-instance feedback uniforms (hit flash, tint, fade).
        var key = $"model|{baseColor}|{accent}|{rim}|{texture?.GetRid()}|{blend}|{alpha}|{flipV}|{scenery}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var material = new ShaderMaterial { Shader = blend ? ModelBlendShader : scenery ? ModelStaticShader : ModelShader };
        material.SetShaderParameter("uBase", Rgb(baseColor));
        material.SetShaderParameter("uAccent", Rgb(accent));
        material.SetShaderParameter("uRim", rim);
        material.SetShaderParameter("uAlpha", alpha);
        material.SetShaderParameter("uUseTex", texture != null);
        material.SetShaderParameter("uFlipV", flipV);
        if (texture != null)
            material.SetShaderParameter("uTex", texture);
        Cache[key] = material;
        return material;
    }

    /// <summary>ARC's spectral `Anomaly` material for elites, bosses and effects.</summary>
    public static ShaderMaterial Anomaly(Color baseColor, Color accent, float rim = 0.8f, float glow = 0f, float alpha = 1f)
    {
        var material = new ShaderMaterial { Shader = AnomalyShader };
        material.SetShaderParameter("uBase", Rgb(baseColor));
        material.SetShaderParameter("uAccent", Rgb(accent));
        material.SetShaderParameter("uRim", rim);
        material.SetShaderParameter("uGlow", glow);
        material.SetShaderParameter("uAlpha", alpha);
        return material;
    }

    /// <summary>The faceted, glinting material of the experience crystals.</summary>
    public static ShaderMaterial Crystal(Color baseColor, Color accent, float rim = 0.6f, float glow = 0.1f)
    {
        var key = $"crystal|{baseColor}|{accent}|{rim}|{glow}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var material = new ShaderMaterial { Shader = CrystalShader };
        material.SetShaderParameter("uBase", Rgb(baseColor));
        material.SetShaderParameter("uAccent", Rgb(accent));
        material.SetShaderParameter("uRim", rim);
        material.SetShaderParameter("uGlow", glow);
        Cache[key] = material;
        return material;
    }

    /// <summary>ARC's `Flat` material;<paramref name="soft"/> above 0 makes a quad a soft disc.</summary>
    public static ShaderMaterial Flat(Color baseColor, float alpha, float soft = 0f, Color? accent = null, float glow = 0f)
    {
        var key = $"flat|{baseColor}|{alpha}|{soft}|{accent}|{glow}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var material = new ShaderMaterial { Shader = FlatShader };
        material.SetShaderParameter("uBase", Rgb(baseColor));
        material.SetShaderParameter("uAccent", Rgb(accent ?? Colors.Black));
        material.SetShaderParameter("uGlow", glow);
        material.SetShaderParameter("uAlpha", alpha);
        material.SetShaderParameter("uSoft", soft);
        Cache[key] = material;
        return material;
    }

    public static ShaderMaterial Ground() => new() { Shader = GroundShader };

    /// <summary>The grass blades' material; the field gives it the same maps as the ground.</summary>
    public static ShaderMaterial Grass() => new() { Shader = GrassShader };

    private static Vector3 Rgb(Color c) => new(c.R, c.G, c.B);
}

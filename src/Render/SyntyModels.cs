using Godot;

namespace ArcPrefabs.Render;

/// <summary>
/// Loads Synty POLYGON meshes (assets/synty, see tools/fetch_synty.py) and dresses them in
/// ARC's Model material over the pack's palette atlas, with a soft neutral rim so they sit in
/// the same style as the anomalies. Prototypes are built once and duplicated per placement.
/// </summary>
public static class SyntyModels
{
    public const string Root = "res://assets/synty/";

    public static readonly Color NeutralRim = new(0.85f, 0.9f, 1f);

    private static readonly Dictionary<string, Node3D?> Prototypes = [];

    // The scenes the prototypes were instanced from. Duplicate() re-instances a prototype from
    // its scene; if the collector has taken the scene's C# wrapper by then, swapping its handle
    // fails ("Handle is not initialized"), which bird view's many prototypes hit.
    private static readonly List<PackedScene> Held = [];

    /// <summary>A fresh copy of <paramref name="path"/> (relative to assets/synty) with ARC materials.</summary>
    /// <remarks><paramref name="scenery"/> props cannot flash or fade, which keeps them out of
    /// the shader instance-uniform buffer.</remarks>
    public static Node3D? Instance(string path, string atlas, float rim = 0.3f, Color? accent = null, bool scenery = false)
    {
        var key = $"{path}|{atlas}|{rim}|{accent}|{scenery}";
        if (!Prototypes.TryGetValue(key, out var prototype))
        {
            prototype = Load(path, atlas, rim, accent ?? NeutralRim, scenery);
            Prototypes[key] = prototype;
        }

        return prototype?.Duplicate() as Node3D;
    }

    /// <summary>Frees the prototypes (they are never in the tree). Call when the game shuts down.</summary>
    public static void Release()
    {
        foreach (var prototype in Prototypes.Values)
            prototype?.Free();
        Prototypes.Clear();
        Held.Clear();
    }

    /// <summary>Combined bounds of every mesh under <paramref name="root"/>, in its local space.</summary>
    public static Aabb Bounds(Node3D root)
    {
        var box = new Aabb();
        var first = true;
        foreach (var mesh in root.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh == null)
                continue;
            var t = Transform3D.Identity;
            for (Node? n = mesh; n != null && n != root; n = n.GetParent())
            {
                if (n is Node3D n3)
                    t = n3.Transform * t;
            }
            var b = t * mesh.Mesh.GetAabb();
            box = first ? b : box.Merge(b);
            first = false;
        }
        return box;
    }

    private static Node3D? Load(string path, string atlas, float rim, Color accent, bool scenery)
    {
        var full = Root + path;
        if (!ResourceLoader.Exists(full))
        {
            GD.PushWarning($"Synty asset missing: {full} (run python tools/fetch_synty.py)");
            return null;
        }

        var packed = GD.Load<PackedScene>(full);
        Held.Add(packed); // Duplicate() of an instanced scene re-instances it from this
        if (packed.Instantiate() is not Node3D scene)
            return null;

        var texture = GD.Load<Texture2D>(Root + atlas);
        var material = ArcMaterials.Model(Colors.White, accent, rim, texture, scenery: scenery);
        foreach (var mesh in scene.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (mesh.Mesh == null)
                continue;
            for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                mesh.SetSurfaceOverrideMaterial(i, material);
        }

        return scene;
    }
}

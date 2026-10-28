using Godot;

namespace ArcPrefabs.World;

/// <summary>
/// Grass for one chunk: a MultiMesh of a five-blade tuft, scattered evenly at the most tufts
/// the field ever shows. Each blade carries its index in UV.x, so the shader can show a tuft
/// with anywhere from one blade to all five. shaders/arc_grass.gdshader does the rest on the GPU: it
/// drops blades by the biome's density and the props' marks, sets each one's height, colours
/// it from the ground, and sways and pushes it. The layout comes from a hash of the chunk, so
/// it is the same each time the chunk is built.
/// </summary>
public static class Grass
{
    public const float BladesPerSquareMetre = 6f; // tufts of one to five blades
    private const int Stride = 16; // a 3×4 transform, then the custom data

    /// <summary>
    /// A tuft a metre tall (the shader scales it): five blades fanned around the root, each
    /// three tapering segments leaning outward, so a tuft reads at the game's camera distance.
    /// The first blade stands nearly upright, so a one-blade sprout looks like a shoot.
    /// </summary>
    public static ArrayMesh Blade()
    {
        float[] heights = [0f, 0.4f, 0.75f];
        float[] halfWidths = [0.075f, 0.062f, 0.038f];
        (float Turn, float Out, float Tall)[] blades =
            [(0.3f, 0.08f, 1f), (2.2f, 0.24f, 0.8f), (4.1f, 0.2f, 0.9f), (1.2f, 0.28f, 0.7f), (5.2f, 0.26f, 0.75f)];
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        for (var b = 0; b < blades.Length; b++)
        {
            var (turn, lean, tall) = blades[b];
            var first = vertices.Count;
            var basis = new Basis(Vector3.Up, turn);
            var foot = basis * new Vector3(0f, 0f, 0.05f);
            for (var i = 0; i < heights.Length; i++)
            {
                var h = heights[i];
                var bend = new Vector3(0f, h * tall, lean * h * h);
                vertices.Add(foot + basis * (bend + new Vector3(-halfWidths[i], 0f, 0f)));
                vertices.Add(foot + basis * (bend + new Vector3(halfWidths[i], 0f, 0f)));
            }
            vertices.Add(foot + basis * new Vector3(0f, tall, lean));
            foreach (var i in (int[])[0, 1, 2, 1, 3, 2, 2, 3, 4, 3, 5, 4, 4, 5, 6])
                indices.Add(first + i);
            while (uvs.Count < vertices.Count)
                uvs.Add(new Vector2(b, 0f));
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>The blades of chunk <paramref name="key"/>, positioned in the chunk's own space.</summary>
    public static MultiMeshInstance3D Build(Vector2I key, int seed, float size, Mesh blade, Material material)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(HashCode.Combine(seed, key.X, key.Y, 0x6a55) & 0x7fffffff) };
        var count = Mathf.RoundToInt(size * size * BladesPerSquareMetre);
        var buffer = new float[count * Stride];
        for (var i = 0; i < count; i++)
        {
            var (sin, cos) = Mathf.SinCos(rng.Randf() * Mathf.Tau);
            var x = rng.Randf() * size;
            var z = rng.Randf() * size;
            var o = i * Stride;
            // Rows of the basis with the origin last: a turn about Y.
            buffer[o + 0] = cos; buffer[o + 1] = 0f; buffer[o + 2] = sin; buffer[o + 3] = x;
            buffer[o + 4] = 0f; buffer[o + 5] = 1f; buffer[o + 6] = 0f; buffer[o + 7] = 0f;
            buffer[o + 8] = -sin; buffer[o + 9] = 0f; buffer[o + 10] = cos; buffer[o + 11] = z;
            buffer[o + 12] = rng.Randf(); // survives where below the density; sets the height
            buffer[o + 13] = rng.Randf(); // flutter phase
        }

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = blade,
            InstanceCount = count,
        };
        multimesh.Buffer = buffer;
        return new MultiMeshInstance3D
        {
            Name = $"Grass_{key.X}_{key.Y}",
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(key.X * size, 0f, key.Y * size),
            // Blades bend and lean, so give the culler a little room around the chunk.
            CustomAabb = new Aabb(new Vector3(-2f, -0.5f, -2f), new Vector3(size + 4f, 2.5f, size + 4f)),
        };
    }
}

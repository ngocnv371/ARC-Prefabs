using Godot;

namespace ArcPrefabs.World;

/// <summary>
/// Where a prop meets the ground: a circle (trunks, rocks) or a turned box (slabs, rubble, pads)
/// in metres. Inside <see cref="Half"/> no grass grows when <see cref="Clear"/> is set; the
/// ground darkens to <see cref="Shade"/> at the middle and back to full light by
/// <see cref="Reach"/> times the size.
/// </summary>
public readonly record struct GroundMark(Vector2 Centre, Vector2 Half, float Angle, bool Box, float Shade, float Reach, bool Clear)
{
    /// <summary>The mark for a placed prop, from its blocking radius and its bounds (both in metres).</summary>
    public static GroundMark? For(Vector2 at, float angle, float block, Aabb bounds)
    {
        // Flat and wide (floors, rubble, pads, snow): a box that grass stays out from under.
        if (bounds.Size.Y < 0.6f && Mathf.Max(bounds.Size.X, bounds.Size.Z) > 1.6f)
        {
            var middle = bounds.GetCenter();
            var offset = new Vector2(middle.X, middle.Z).Rotated(-angle);
            return new GroundMark(at + offset, new Vector2(bounds.Size.X, bounds.Size.Z) / 2f, angle, true, 0.86f, 1.15f, true);
        }
        // Blocking (trunks, rocks): shade around the base and no grass through it.
        if (block > 0f)
            return new GroundMark(at, Vector2.One * (block * 1.1f + 0.1f), 0f, false, 0.62f, 1.9f, true);
        // Anything else that stands up (bushes, ferns): a light contact shade, grass grows through.
        var reach = Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.3f;
        return reach > 0.15f ? new GroundMark(at, Vector2.One * reach, 0f, false, 0.86f, 1.4f, false) : null;
    }

    /// <summary>
    /// Draws <paramref name="marks"/> into an RG8 map covering <paramref name="rect"/> (world XZ)
    /// at <paramref name="pixels"/> square: R the light left (255 is full), G grass allowed.
    /// </summary>
    public static void Bake(byte[] data, int pixels, Rect2 rect, IEnumerable<GroundMark> marks)
    {
        Array.Fill(data, (byte)255);
        var texel = rect.Size.X / pixels;
        foreach (var m in marks)
        {
            var extent = Mathf.Max(m.Half.X, m.Half.Y) * Mathf.Max(m.Reach, 1f) * (m.Box ? 1.42f : 1f);
            var x0 = Mathf.Max(0, Mathf.FloorToInt((m.Centre.X - extent - rect.Position.X) / texel));
            var x1 = Mathf.Min(pixels - 1, Mathf.CeilToInt((m.Centre.X + extent - rect.Position.X) / texel));
            var y0 = Mathf.Max(0, Mathf.FloorToInt((m.Centre.Y - extent - rect.Position.Y) / texel));
            var y1 = Mathf.Min(pixels - 1, Mathf.CeilToInt((m.Centre.Y + extent - rect.Position.Y) / texel));
            var (sin, cos) = Mathf.SinCos(m.Angle);
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var d = rect.Position + new Vector2(x + 0.5f, y + 0.5f) * texel - m.Centre;
                float t;
                if (m.Box)
                {
                    // Into the box's frame (a turn of Angle about Y), then the larger of the two ratios.
                    var lx = d.X * cos - d.Y * sin;
                    var lz = d.X * sin + d.Y * cos;
                    t = Mathf.Max(Mathf.Abs(lx) / m.Half.X, Mathf.Abs(lz) / m.Half.Y);
                }
                else
                {
                    t = d.Length() / m.Half.X;
                }
                if (t >= m.Reach && (t >= 1f || !m.Clear))
                    continue;
                var i = (y * pixels + x) * 2;
                var light = Mathf.Lerp(m.Shade, 1f, Mathf.SmoothStep(0.25f, m.Reach, t));
                data[i] = Math.Min(data[i], (byte)(light * 255f));
                if (m.Clear)
                    data[i + 1] = Math.Min(data[i + 1], (byte)(Mathf.SmoothStep(0.8f, 1.05f, t) * 255f));
            }
        }
    }
}

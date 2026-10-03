using System.Numerics;

namespace StlGen.Core;

/// <summary>An indexed triangle mesh with counter-clockwise (outward-facing) winding.</summary>
public sealed class Mesh
{
    public List<Vector3> Vertices { get; } = new();
    public List<int> Indices { get; } = new();

    public int TriangleCount => Indices.Count / 3;

    public (Vector3 A, Vector3 B, Vector3 C) Triangle(int t) =>
        (Vertices[Indices[3 * t]], Vertices[Indices[3 * t + 1]], Vertices[Indices[3 * t + 2]]);

    /// <summary>Enclosed volume (positive for a closed mesh with outward-facing triangles).</summary>
    public double Volume()
    {
        double sum = 0;
        for (int t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Triangle(t);
            sum += Vector3.Dot(a, Vector3.Cross(b, c));
        }
        return sum / 6.0;
    }

    public (Vector3 Min, Vector3 Max) Bounds()
    {
        if (Vertices.Count == 0) return (Vector3.Zero, Vector3.Zero);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (min, max);
    }

    /// <summary>True if every edge is shared by exactly two triangles with opposite orientation.</summary>
    public bool IsClosedManifold()
    {
        var directed = new HashSet<(int, int)>();
        for (int t = 0; t < TriangleCount; t++)
        {
            int a = Indices[3 * t], b = Indices[3 * t + 1], c = Indices[3 * t + 2];
            if (!directed.Add((a, b)) || !directed.Add((b, c)) || !directed.Add((c, a))) return false;
        }
        foreach (var (a, b) in directed)
            if (!directed.Contains((b, a))) return false;
        return true;
    }
}

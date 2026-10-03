using System.Numerics;

namespace StlGen.Core;

/// <summary>
/// Extracts the zero iso-surface of a scalar grid (negative = inside). Each cube is split into six
/// tetrahedra sharing the main diagonal, which gives a watertight, manifold mesh without the
/// ambiguous cases of classic marching cubes.
/// </summary>
internal static class MarchingTetrahedra
{
    private static readonly int[][] Tetrahedra =
    {
        new[] { 0, 1, 3, 7 }, new[] { 0, 1, 5, 7 },
        new[] { 0, 2, 3, 7 }, new[] { 0, 2, 6, 7 },
        new[] { 0, 4, 5, 7 }, new[] { 0, 4, 6, 7 },
    };

    public static Mesh Polygonize(
        float[] field, int nx, int ny, int nz,
        Func<int, int, int, Vector3> position,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mesh = new Mesh();
        var edgeVertices = new Dictionary<long, int>();
        var cornerIndex = new int[8];
        var cornerValue = new float[8];
        var cornerPos = new Vector3[8];
        int sliceSize = nx * ny;

        for (int k = 0; k < nz - 1; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((double)k / (nz - 1));

            for (int j = 0; j < ny - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                bool anyIn = false, anyOut = false;
                for (int c = 0; c < 8; c++)
                {
                    int ci = i + (c & 1), cj = j + ((c >> 1) & 1), ck = k + ((c >> 2) & 1);
                    int g = ck * sliceSize + cj * nx + ci;
                    cornerIndex[c] = g;
                    cornerValue[c] = field[g];
                    if (field[g] < 0) anyIn = true; else anyOut = true;
                }
                if (!anyIn || !anyOut) continue;

                for (int c = 0; c < 8; c++)
                    cornerPos[c] = position(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1));

                foreach (var tet in Tetrahedra)
                    PolygonizeTetrahedron(tet, cornerIndex, cornerValue, cornerPos, mesh, edgeVertices);
            }
        }
        progress?.Report(1);
        return mesh;
    }

    private static void PolygonizeTetrahedron(
        int[] tet, int[] gi, float[] val, Vector3[] pos, Mesh mesh, Dictionary<long, int> cache)
    {
        Span<int> inside = stackalloc int[4];
        Span<int> outside = stackalloc int[4];
        int nIn = 0, nOut = 0;
        foreach (int c in tet)
        {
            if (val[c] < 0) inside[nIn++] = c;
            else outside[nOut++] = c;
        }
        if (nIn == 0 || nOut == 0) return;

        int V(int a, int b) => EdgeVertex(a, b, gi, val, pos, mesh, cache);

        if (nIn == 1)
        {
            int a = inside[0];
            Emit(V(a, outside[0]), V(a, outside[1]), V(a, outside[2]), inside[..1], outside[..3], pos, mesh);
        }
        else if (nIn == 3)
        {
            int a = outside[0];
            Emit(V(inside[0], a), V(inside[1], a), V(inside[2], a), inside[..3], outside[..1], pos, mesh);
        }
        else
        {
            int a = inside[0], b = inside[1], c = outside[0], d = outside[1];
            int ac = V(a, c), ad = V(a, d), bd = V(b, d), bc = V(b, c);
            Emit(ac, ad, bd, inside[..2], outside[..2], pos, mesh);
            Emit(ac, bd, bc, inside[..2], outside[..2], pos, mesh);
        }
    }

    private static int EdgeVertex(int a, int b, int[] gi, float[] val, Vector3[] pos, Mesh mesh, Dictionary<long, int> cache)
    {
        // Tetrahedron edges always join a cube corner to one whose offset bits are a superset of its
        // own, so (lower grid index, direction bits) identifies the edge uniquely and hashes well.
        long key = (long)Math.Min(gi[a], gi[b]) * 8 + (a ^ b);
        if (cache.TryGetValue(key, out int index)) return index;

        float t = val[a] / (val[a] - val[b]);
        t = Math.Clamp(t, 1e-3f, 1 - 1e-3f);
        index = mesh.Vertices.Count;
        mesh.Vertices.Add(pos[a] + (pos[b] - pos[a]) * t);
        cache[key] = index;
        return index;
    }

    /// <summary>Adds a triangle, oriented so its normal points from the inside corners to the outside ones.</summary>
    private static void Emit(int i0, int i1, int i2, ReadOnlySpan<int> inside, ReadOnlySpan<int> outside, Vector3[] pos, Mesh mesh)
    {
        var p0 = mesh.Vertices[i0];
        var normal = Vector3.Cross(mesh.Vertices[i1] - p0, mesh.Vertices[i2] - p0);

        var inC = Vector3.Zero;
        foreach (int c in inside) inC += pos[c];
        var outC = Vector3.Zero;
        foreach (int c in outside) outC += pos[c];
        var dir = outC / outside.Length - inC / inside.Length;

        if (Vector3.Dot(normal, dir) < 0) (i1, i2) = (i2, i1);
        mesh.Indices.Add(i0);
        mesh.Indices.Add(i1);
        mesh.Indices.Add(i2);
    }
}

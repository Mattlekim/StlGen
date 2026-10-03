using System.Globalization;
using System.Numerics;
using System.Text;

namespace StlGen.Core;

public static class StlWriter
{
    public static void WriteBinary(Mesh mesh, string path)
    {
        using var stream = File.Create(path);
        WriteBinary(mesh, stream);
    }

    public static void WriteBinary(Mesh mesh, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var header = new byte[80];
        Encoding.ASCII.GetBytes("StlGen visual hull (units: mm)").CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)mesh.TriangleCount);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Triangle(t);
            WriteVector(w, Normal(a, b, c));
            WriteVector(w, a);
            WriteVector(w, b);
            WriteVector(w, c);
            w.Write((ushort)0);
        }
    }

    public static void WriteAscii(Mesh mesh, TextWriter w, string name = "stlgen")
    {
        static string F(float v) => v.ToString("e6", CultureInfo.InvariantCulture);
        w.WriteLine($"solid {name}");
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var (a, b, c) = mesh.Triangle(t);
            var n = Normal(a, b, c);
            w.WriteLine($"  facet normal {F(n.X)} {F(n.Y)} {F(n.Z)}");
            w.WriteLine("    outer loop");
            foreach (var v in new[] { a, b, c })
                w.WriteLine($"      vertex {F(v.X)} {F(v.Y)} {F(v.Z)}");
            w.WriteLine("    endloop");
            w.WriteLine("  endfacet");
        }
        w.WriteLine($"endsolid {name}");
    }

    private static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        return len > 0 ? n / len : Vector3.Zero;
    }

    private static void WriteVector(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }
}

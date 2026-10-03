using StlGen.Core;

namespace StlGen.Core.Tests;

public class ImagingTests
{
    private static RawImage Image(int w, int h, Func<int, int, (byte B, byte G, byte R, byte A)> pixel)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (b, g, r, a) = pixel(x, y);
            int o = (y * w + x) * 4;
            px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = a;
        }
        return new RawImage(w, h, px);
    }

    [Fact]
    public void DarkObjectOnWhiteBackground()
    {
        var img = Image(50, 40, (x, y) => x is >= 10 and < 30 && y is >= 5 and < 25 ? ((byte)30, (byte)30, (byte)30, (byte)255) : ((byte)250, (byte)250, (byte)250, (byte)255));
        var mask = SilhouetteExtractor.Extract(img, new SilhouetteOptions());
        Assert.Equal(new PixelBounds(10, 5, 29, 24), mask.Bounds());
        Assert.Equal(400, mask.Count);
    }

    [Fact]
    public void TransparentBackgroundUsesAlpha()
    {
        var img = Image(30, 30, (x, y) => x is >= 5 and < 15 && y is >= 5 and < 15 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)0));
        var mask = SilhouetteExtractor.Extract(img, new SilhouetteOptions());
        Assert.Equal(100, mask.Count);
    }

    [Fact]
    public void HolesAreFilledAndSpecksRemoved()
    {
        var img = Image(40, 40, (x, y) =>
        {
            bool ring = x is >= 5 and < 25 && y is >= 5 and < 25 && !(x is >= 10 and < 20 && y is >= 10 and < 20);
            bool speck = x == 35 && y == 35;
            return ring || speck ? ((byte)0, (byte)0, (byte)200, (byte)255) : ((byte)240, (byte)240, (byte)240, (byte)255);
        });
        var mask = SilhouetteExtractor.Extract(img, new SilhouetteOptions());
        Assert.Equal(400, mask.Count);
        Assert.False(mask[35, 35]);

        var raw = SilhouetteExtractor.Extract(img, new SilhouetteOptions { FillHoles = false, KeepLargestComponent = false });
        Assert.Equal(400 - 100 + 1, raw.Count);
    }

    [Fact]
    public void RotateAndMirror()
    {
        // 3x2 image; mark pixel (0,0) red.
        var img = Image(3, 2, (x, y) => x == 0 && y == 0 ? ((byte)0, (byte)0, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)255));
        var rot = img.Rotate90Clockwise();
        Assert.Equal(2, rot.Width);
        Assert.Equal(3, rot.Height);
        Assert.Equal(255, rot.Pixels[(0 * 2 + 1) * 4 + 2]); // top-left moves to top-right
        var mir = img.MirrorHorizontal();
        Assert.Equal(255, mir.Pixels[2 * 4 + 2]);
        var back = img.Transformed(4, false);
        Assert.Equal(img.Pixels, back.Pixels);
    }

    [Fact]
    public void BinaryStlHasExpectedLayout()
    {
        var mesh = new Mesh();
        mesh.Vertices.AddRange(new[] { new System.Numerics.Vector3(0, 0, 0), new(1, 0, 0), new(0, 1, 0) });
        mesh.Indices.AddRange(new[] { 0, 1, 2 });
        using var ms = new MemoryStream();
        StlWriter.WriteBinary(mesh, ms);
        Assert.Equal(84 + 50, ms.Length);
        var bytes = ms.ToArray();
        Assert.Equal(1u, BitConverter.ToUInt32(bytes, 80));
        Assert.Equal(1f, BitConverter.ToSingle(bytes, 84 + 8)); // normal = +Z
    }
}

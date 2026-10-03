using System.Numerics;
using StlGen.Core;

namespace StlGen.Core.Tests;

public class HullBuilderTests
{
    private static Mask Rect(int w, int h, int margin = 10) =>
        Mask.FromFunction(w + 2 * margin, h + 2 * margin,
            (x, y) => x >= margin && y >= margin && x < w + margin && y < h + margin);

    private static Mask Disc(int radius, int margin = 10)
    {
        int size = 2 * (radius + margin);
        double c = size / 2.0;
        return Mask.FromFunction(size, size, (x, y) =>
        {
            double dx = x + 0.5 - c, dy = y + 0.5 - c;
            return dx * dx + dy * dy <= radius * radius;
        });
    }

    [Fact]
    public void ThreeRectanglesMakeABoxWithCorrectSize()
    {
        // 100 x 60 x 40 mm box drawn at 2 px/mm (front, side) and 1 px/mm (top).
        var views = new[]
        {
            new ViewInput(ViewDirection.Front, Rect(200, 80)),
            new ViewInput(ViewDirection.Right, Rect(120, 80)),
            new ViewInput(ViewDirection.Top, Rect(100, 60)),
        };
        var result = HullBuilder.Build(views, new HullOptions { Resolution = 64, ScaleAxis = Axis.X, ScaleSize = 100 });

        Assert.Equal(100, result.Size.X, 1);
        Assert.Equal(60, result.Size.Y, 1);
        Assert.Equal(40, result.Size.Z, 1);
        Assert.Empty(result.Warnings);
        Assert.True(result.Mesh.IsClosedManifold());

        var (min, max) = result.Mesh.Bounds();
        Assert.Equal(100, max.X - min.X, 1.5);
        Assert.Equal(60, max.Y - min.Y, 1.5);
        Assert.Equal(40, max.Z - min.Z, 1.5);
        Assert.InRange(min.Z, -0.5, 0.5);

        Assert.InRange(result.Mesh.Volume(), 100 * 60 * 40 * 0.95, 100 * 60 * 40 * 1.02);
    }

    [Fact]
    public void ThreeCirclesMakeASteinmetzSolid()
    {
        const double r = 25;
        var views = new[]
        {
            new ViewInput(ViewDirection.Front, Disc(100)),
            new ViewInput(ViewDirection.Left, Disc(100)),
            new ViewInput(ViewDirection.Top, Disc(100)),
        };
        var result = HullBuilder.Build(views, new HullOptions { Resolution = 96, ScaleAxis = Axis.Z, ScaleSize = 2 * r });

        Assert.True(result.Mesh.IsClosedManifold());
        double expected = 8 * (2 - Math.Sqrt(2)) * r * r * r;
        Assert.InRange(result.Mesh.Volume(), expected * 0.97, expected * 1.03);
    }

    [Fact]
    public void SingleViewIsExtrudedAndWarns()
    {
        var result = HullBuilder.Build(new[] { new ViewInput(ViewDirection.Front, Disc(50)) },
            new HullOptions { Resolution = 64, ScaleAxis = Axis.X, ScaleSize = 20 });

        Assert.Contains(result.Warnings, w => w.Contains("depth"));
        Assert.Equal(20, result.Size.Y, 1);
        double expected = Math.PI * 10 * 10 * 20;
        Assert.InRange(result.Mesh.Volume(), expected * 0.95, expected * 1.05);
        Assert.True(result.Mesh.IsClosedManifold());
    }

    [Fact]
    public void InconsistentProportionsProduceWarning()
    {
        var views = new[]
        {
            new ViewInput(ViewDirection.Front, Rect(100, 100)),
            new ViewInput(ViewDirection.Right, Rect(100, 100)),
            new ViewInput(ViewDirection.Top, Rect(100, 300)),
        };
        var result = HullBuilder.Build(views, new HullOptions { Resolution = 32 });
        Assert.Contains(result.Warnings, w => w.Contains("disagree"));
    }

    // An L-shape seen from one side: the object is tall at one end and short at the other.
    // Checks every view maps image left/right/up/down to the documented world directions.
    [Theory]
    [InlineData(ViewDirection.Front, 0, false)]  // image right = +X, tall part on the right -> tall at +X
    [InlineData(ViewDirection.Back, 0, true)]    // image right = -X
    [InlineData(ViewDirection.Right, 1, false)]  // image right = +Y
    [InlineData(ViewDirection.Left, 1, true)]    // image right = -Y
    public void SideViewsMapImageRightToCorrectAxis(ViewDirection view, int axis, bool tallAtNegative)
    {
        // 100 x 100 px; right half is full height, left half only the bottom quarter.
        var mask = Mask.FromFunction(120, 120, (x, y) =>
            x >= 10 && x < 110 && y >= 10 && y < 110 && (x >= 60 || y >= 85));
        var result = HullBuilder.Build(new[] { new ViewInput(view, mask) },
            new HullOptions { Resolution = 40, ScaleAxis = Axis.Z, ScaleSize = 100 });

        // Height of the mesh near the low and high ends of the axis.
        float HeightNear(float coord) => result.Mesh.Vertices
            .Where(v => Math.Abs(Component(v, axis) - coord) < 10).Max(v => v.Z);

        float low = HeightNear(15), high = HeightNear(85);
        if (tallAtNegative) Assert.True(low > 90 && high < 35, $"low={low} high={high}");
        else Assert.True(high > 90 && low < 35, $"low={low} high={high}");
    }

    [Theory]
    [InlineData(ViewDirection.Top, false)]    // image up = +Y (back)
    [InlineData(ViewDirection.Bottom, true)]  // image up = -Y (front)
    public void TopAndBottomMapImageUpToCorrectDirection(ViewDirection view, bool wideAtFront)
    {
        // Wide at the top of the image, narrow at the bottom.
        var mask = Mask.FromFunction(120, 120, (x, y) =>
            x >= 10 && x < 110 && y >= 10 && y < 110 && (y < 60 || (x >= 50 && x < 70)));
        var result = HullBuilder.Build(new[] { new ViewInput(view, mask) },
            new HullOptions { Resolution = 40, ScaleAxis = Axis.X, ScaleSize = 100 });

        float WidthNear(float y)
        {
            var xs = result.Mesh.Vertices.Where(v => Math.Abs(v.Y - y) < 10).Select(v => v.X).ToList();
            return xs.Max() - xs.Min();
        }
        float front = WidthNear(15), back = WidthNear(85);
        if (wideAtFront) Assert.True(front > 90 && back < 35, $"front={front} back={back}");
        else Assert.True(back > 90 && front < 35, $"front={front} back={back}");
    }

    private static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}

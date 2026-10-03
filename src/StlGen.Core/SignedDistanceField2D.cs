namespace StlGen.Core;

/// <summary>
/// Signed distance (in millimetres, negative inside) to a silhouette outline, sampled in the
/// silhouette's normalised bounding-box coordinates. Uses an exact anisotropic Euclidean distance
/// transform (Felzenszwalb &amp; Huttenlocher) so non-square pixel scales are handled correctly.
/// </summary>
internal sealed class SignedDistanceField2D
{
    private const double Far = 1e20;

    private readonly float[] _values;
    private readonly int _width, _height, _margin;
    private readonly PixelBounds _bounds;

    public SignedDistanceField2D(Mask mask, double mmPerPixelX, double mmPerPixelY)
    {
        _bounds = mask.Bounds() ?? throw new ArgumentException("Mask is empty.", nameof(mask));
        _margin = Math.Max(_bounds.Width, _bounds.Height) / 20 + 3;
        _width = _bounds.Width + 2 * _margin;
        _height = _bounds.Height + 2 * _margin;

        int n = _width * _height;
        var inside = new bool[n];
        for (int j = 0; j < _height; j++)
        for (int i = 0; i < _width; i++)
        {
            int sx = _bounds.MinX - _margin + i, sy = _bounds.MinY - _margin + j;
            inside[j * _width + i] = sx >= 0 && sy >= 0 && sx < mask.Width && sy < mask.Height && mask[sx, sy];
        }

        var toInside = new double[n];
        var toOutside = new double[n];
        for (int k = 0; k < n; k++)
        {
            toInside[k] = inside[k] ? 0 : Far;
            toOutside[k] = inside[k] ? Far : 0;
        }
        DistanceTransform(toInside, _width, _height, mmPerPixelX, mmPerPixelY);
        DistanceTransform(toOutside, _width, _height, mmPerPixelX, mmPerPixelY);

        // The outline lies half a pixel from the pixel centres on either side of it.
        double half = 0.25 * (mmPerPixelX + mmPerPixelY);
        _values = new float[n];
        for (int k = 0; k < n; k++)
            _values[k] = inside[k]
                ? (float)-(Math.Sqrt(toOutside[k]) - half)
                : (float)(Math.Sqrt(toInside[k]) - half);
    }

    /// <summary>Samples the field at normalised bounding-box coordinates (u, v), bilinearly.</summary>
    public float Sample(double u, double v)
    {
        double fx = u * _bounds.Width + _margin - 0.5;
        double fy = v * _bounds.Height + _margin - 0.5;
        fx = Math.Clamp(fx, 0, _width - 1);
        fy = Math.Clamp(fy, 0, _height - 1);
        int x0 = Math.Min((int)fx, _width - 2), y0 = Math.Min((int)fy, _height - 2);
        double tx = fx - x0, ty = fy - y0;
        int i = y0 * _width + x0;
        double top = _values[i] + (_values[i + 1] - _values[i]) * tx;
        double bottom = _values[i + _width] + (_values[i + _width + 1] - _values[i + _width]) * tx;
        return (float)(top + (bottom - top) * ty);
    }

    /// <summary>In-place squared Euclidean distance transform. Input: 0 at features, Far elsewhere.</summary>
    internal static void DistanceTransform(double[] grid, int width, int height, double spacingX, double spacingY)
    {
        int max = Math.Max(width, height);
        var f = new double[max];
        var d = new double[max];
        var v = new int[max];
        var z = new double[max + 1];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) f[y] = grid[y * width + x];
            Transform1D(f, height, spacingY, d, v, z);
            for (int y = 0; y < height; y++) grid[y * width + x] = d[y];
        }
        for (int y = 0; y < height; y++)
        {
            Array.Copy(grid, y * width, f, 0, width);
            Transform1D(f, width, spacingX, d, v, z);
            Array.Copy(d, 0, grid, y * width, width);
        }
    }

    private static void Transform1D(double[] f, int n, double s, double[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double pq = q * s;
            double sep;
            while (true)
            {
                double pv = v[k] * s;
                sep = ((f[q] + pq * pq) - (f[v[k]] + pv * pv)) / (2 * (pq - pv));
                if (sep > z[k]) break;
                k--;
            }
            k++;
            v[k] = q;
            z[k] = sep;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            double pq = q * s;
            while (z[k + 1] < pq) k++;
            double diff = pq - v[k] * s;
            d[q] = diff * diff + f[v[k]];
        }
    }
}

namespace StlGen.Core;

public sealed class SilhouetteOptions
{
    /// <summary>Colour distance (0-441) from the background colour above which a pixel counts as object.</summary>
    public double Tolerance { get; set; } = 40;

    /// <summary>Swap object and background after classification.</summary>
    public bool Invert { get; set; }

    /// <summary>Fill enclosed background regions so only the outer outline is used.</summary>
    public bool FillHoles { get; set; } = true;

    /// <summary>Discard specks and keep only the biggest connected blob.</summary>
    public bool KeepLargestComponent { get; set; } = true;
}

/// <summary>
/// Separates an object from its background. Images with a transparent background use the alpha
/// channel; otherwise the background colour is estimated from the image border, so photos should
/// be taken against a plain backdrop that contrasts with the object.
/// </summary>
public static class SilhouetteExtractor
{
    public static Mask Extract(RawImage image, SilhouetteOptions options)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var border = BorderPixelIndices(w, h).ToArray();

        int transparentBorder = border.Count(i => px[i * 4 + 3] < 128);
        bool useAlpha = transparentBorder > border.Length / 2;

        var mask = new Mask(w, h);
        if (useAlpha)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[x, y] = px[(y * w + x) * 4 + 3] >= 128;
        }
        else
        {
            var (bb, bg, br) = MedianColor(px, border);
            double tolSq = options.Tolerance * options.Tolerance;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                if (px[o + 3] < 128) continue;
                double db = px[o] - bb, dg = px[o + 1] - bg, dr = px[o + 2] - br;
                mask[x, y] = db * db + dg * dg + dr * dr > tolSq;
            }
        }

        if (options.Invert)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[x, y] = !mask[x, y];
        }

        if (options.KeepLargestComponent) mask.KeepLargestComponent();
        if (options.FillHoles) mask.FillHoles();
        return mask;
    }

    private static IEnumerable<int> BorderPixelIndices(int w, int h)
    {
        for (int x = 0; x < w; x++)
        {
            yield return x;
            if (h > 1) yield return (h - 1) * w + x;
        }
        for (int y = 1; y < h - 1; y++)
        {
            yield return y * w;
            if (w > 1) yield return y * w + w - 1;
        }
    }

    private static (byte B, byte G, byte R) MedianColor(byte[] px, int[] indices)
    {
        var hist = new int[3, 256];
        foreach (int i in indices)
            for (int c = 0; c < 3; c++)
                hist[c, px[i * 4 + c]]++;

        byte Median(int c)
        {
            int half = indices.Length / 2, acc = 0;
            for (int v = 0; v < 256; v++)
            {
                acc += hist[c, v];
                if (acc > half) return (byte)v;
            }
            return 255;
        }
        return (Median(0), Median(1), Median(2));
    }
}

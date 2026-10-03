using System.Windows.Media;
using System.Windows.Media.Imaging;
using StlGen.Core;

namespace StlGen.App;

internal static class ImageIO
{
    /// <summary>Largest image side kept after loading; big photos are scaled down for speed.</summary>
    private const int MaxDimension = 1200;

    public static RawImage Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();

        BitmapSource source = bitmap;
        double scale = Math.Min(1.0, MaxDimension / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1.0)
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new RawImage(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    /// <summary>Shows the image with the background tinted so the detected silhouette stands out.</summary>
    public static BitmapSource RenderPreview(RawImage image, Mask mask, bool highlightMask)
    {
        var px = (byte[])image.Pixels.Clone();
        if (highlightMask)
        {
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                int o = (y * image.Width + x) * 4;
                if (mask[x, y])
                {
                    px[o + 3] = 255;
                    continue;
                }
                // Background: blend 75% towards a magenta tint.
                px[o] = (byte)(px[o] / 4 + 0.75 * 200);
                px[o + 1] = (byte)(px[o + 1] / 4 + 0.75 * 40);
                px[o + 2] = (byte)(px[o + 2] / 4 + 0.75 * 200);
                px[o + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, px, image.Width * 4);
        bmp.Freeze();
        return bmp;
    }
}

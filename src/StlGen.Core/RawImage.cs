namespace StlGen.Core;

/// <summary>
/// A simple 32-bit BGRA image (stride = Width * 4). Independent of any UI framework.
/// </summary>
public sealed class RawImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public RawImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (pixels.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public RawImage Rotate90Clockwise()
    {
        int w = Height, h = Width;
        var dst = new byte[w * h * 4];
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            int sx = dy, sy = Height - 1 - dx;
            Buffer.BlockCopy(Pixels, (sy * Width + sx) * 4, dst, (dy * w + dx) * 4, 4);
        }
        return new RawImage(w, h, dst);
    }

    public RawImage MirrorHorizontal()
    {
        var dst = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            Buffer.BlockCopy(Pixels, (y * Width + (Width - 1 - x)) * 4, dst, (y * Width + x) * 4, 4);
        return new RawImage(Width, Height, dst);
    }

    /// <summary>Applies <paramref name="quarterTurns"/> clockwise rotations, then an optional mirror.</summary>
    public RawImage Transformed(int quarterTurns, bool mirror)
    {
        var img = this;
        for (int i = 0; i < ((quarterTurns % 4) + 4) % 4; i++) img = img.Rotate90Clockwise();
        return mirror ? img.MirrorHorizontal() : img;
    }
}

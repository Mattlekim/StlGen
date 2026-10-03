namespace StlGen.Core;

/// <summary>
/// A binary silhouette: true = object, false = background.
/// </summary>
public sealed class Mask
{
    private readonly bool[] _data;

    public int Width { get; }
    public int Height { get; }

    public Mask(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _data = new bool[width * height];
    }

    public static Mask FromFunction(int width, int height, Func<int, int, bool> isObject)
    {
        var m = new Mask(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            m[x, y] = isObject(x, y);
        return m;
    }

    public bool this[int x, int y]
    {
        get => _data[y * Width + x];
        set => _data[y * Width + x] = value;
    }

    public int Count => _data.Count(b => b);

    public bool IsEmpty => !Array.Exists(_data, b => b);

    public Mask Clone()
    {
        var m = new Mask(Width, Height);
        Array.Copy(_data, m._data, _data.Length);
        return m;
    }

    /// <summary>Inclusive pixel bounds of the object, or null if the mask is empty.</summary>
    public PixelBounds? Bounds()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            if (!_data[y * Width + x]) continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return maxX < 0 ? null : new PixelBounds(minX, minY, maxX, maxY);
    }

    /// <summary>Fills any background region that is not connected to the image border.</summary>
    public void FillHoles()
    {
        var reached = new bool[_data.Length];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            int i = y * Width + x;
            if (_data[i] || reached[i]) return;
            reached[i] = true;
            queue.Enqueue(i);
        }
        for (int x = 0; x < Width; x++) { Seed(x, 0); Seed(x, Height - 1); }
        for (int y = 0; y < Height; y++) { Seed(0, y); Seed(Width - 1, y); }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % Width, y = i / Width;
            if (x > 0) Seed(x - 1, y);
            if (x < Width - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < Height - 1) Seed(x, y + 1);
        }

        for (int i = 0; i < _data.Length; i++)
            if (!reached[i]) _data[i] = true;
    }

    /// <summary>Removes everything except the largest 8-connected object region.</summary>
    public void KeepLargestComponent()
    {
        var label = new int[_data.Length];
        var queue = new Queue<int>();
        int current = 0, bestLabel = 0, bestSize = 0;

        for (int start = 0; start < _data.Length; start++)
        {
            if (!_data[start] || label[start] != 0) continue;
            current++;
            int size = 0;
            label[start] = current;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                size++;
                int x = i % Width, y = i / Width;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) continue;
                    int n = ny * Width + nx;
                    if (!_data[n] || label[n] != 0) continue;
                    label[n] = current;
                    queue.Enqueue(n);
                }
            }
            if (size > bestSize) { bestSize = size; bestLabel = current; }
        }

        for (int i = 0; i < _data.Length; i++)
            _data[i] = label[i] == bestLabel && bestLabel != 0;
    }
}

public readonly record struct PixelBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
}

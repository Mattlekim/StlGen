using System.Numerics;

namespace StlGen.Core;

public sealed record ViewInput(ViewDirection View, Mask Mask);

public sealed class HullOptions
{
    /// <summary>Number of grid cells along the object's longest dimension (higher = finer detail).</summary>
    public int Resolution { get; set; } = 128;

    /// <summary>The axis whose real-world size is known.</summary>
    public Axis ScaleAxis { get; set; } = Axis.X;

    /// <summary>Real-world size, in millimetres, of <see cref="ScaleAxis"/>.</summary>
    public double ScaleSize { get; set; } = 100;
}

public sealed class HullResult
{
    public required Mesh Mesh { get; init; }

    /// <summary>Object size in millimetres along X (width), Y (depth) and Z (height).</summary>
    public required Vector3 Size { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Builds the "visual hull" of an object from its silhouettes: every silhouette is extruded along
/// its viewing direction and the resulting prisms are intersected. The result is the largest shape
/// consistent with all the pictures, which captures the outer outline (but not concavities that no
/// silhouette can see, such as the inside of a bowl).
/// </summary>
public static class HullBuilder
{
    private const int Padding = 2;

    public static HullResult Build(
        IReadOnlyList<ViewInput> views,
        HullOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (views.Count == 0) throw new ArgumentException("At least one view is required.", nameof(views));
        if (views.Select(v => v.View).Distinct().Count() != views.Count)
            throw new ArgumentException("Each view direction may only be used once.", nameof(views));
        if (options.Resolution < 8 || options.Resolution > 512)
            throw new ArgumentOutOfRangeException(nameof(options), "Resolution must be between 8 and 512.");
        if (!(options.ScaleSize > 0)) throw new ArgumentOutOfRangeException(nameof(options), "Scale size must be positive.");

        var warnings = new List<string>();
        var bounds = new List<PixelBounds>();
        foreach (var v in views)
            bounds.Add(v.Mask.Bounds() ?? throw new ArgumentException($"The {v.View} silhouette is empty — no object was found in the picture."));

        var size = SolveSize(views.Select(v => v.View).ToList(), bounds, options, warnings);

        var fields = new SignedDistanceField2D[views.Count];
        for (int n = 0; n < views.Count; n++)
        {
            var (uAxis, vAxis) = ViewGeometry.ImageAxes(views[n].View);
            fields[n] = new SignedDistanceField2D(views[n].Mask, size[(int)uAxis] / bounds[n].Width, size[(int)vAxis] / bounds[n].Height);
        }

        // Sample grid covering the bounding box plus a little empty space so the surface closes.
        double cell = size.Max() / options.Resolution;
        int[] counts = new int[3];
        double[] origin = new double[3];
        for (int a = 0; a < 3; a++)
        {
            int inner = (int)Math.Ceiling(size[a] / cell);
            counts[a] = inner + 1 + 2 * Padding;
            origin[a] = -Padding * cell - (inner * cell - size[a]) / 2;
        }
        int nx = counts[0], ny = counts[1], nz = counts[2];

        var field = new float[nx * ny * nz];
        int done = 0;
        Parallel.For(0, nz, new ParallelOptions { CancellationToken = cancellationToken }, k =>
        {
            double pz = (origin[2] + k * cell) / size[2];
            for (int j = 0; j < ny; j++)
            {
                double py = (origin[1] + j * cell) / size[1];
                for (int i = 0; i < nx; i++)
                {
                    double px = (origin[0] + i * cell) / size[0];
                    // Start with the bounding box, which caps axes that no picture constrains.
                    float value = (float)Math.Max(Math.Max(
                        BoxDistance(origin[0] + i * cell, size[0]),
                        BoxDistance(origin[1] + j * cell, size[1])),
                        BoxDistance(origin[2] + k * cell, size[2]));
                    for (int n = 0; n < views.Count; n++)
                    {
                        var (u, v) = ViewGeometry.Project(views[n].View, px, py, pz);
                        value = Math.Max(value, fields[n].Sample(u, v));
                    }
                    // Keep the surface off grid points to avoid degenerate triangles.
                    field[(k * ny + j) * nx + i] = value == 0 ? 1e-6f : value;
                }
            }
            progress?.Report(0.7 * Interlocked.Increment(ref done) / nz);
        });

        var meshProgress = progress == null ? null : new InlineProgress(p => progress.Report(0.7 + 0.3 * p));
        var mesh = MarchingTetrahedra.Polygonize(
            field, nx, ny, nz,
            (i, j, k) => new Vector3(
                (float)(origin[0] + i * cell),
                (float)(origin[1] + j * cell),
                (float)(origin[2] + k * cell)),
            meshProgress, cancellationToken);

        return new HullResult
        {
            Mesh = mesh,
            Size = new Vector3((float)size[0], (float)size[1], (float)size[2]),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Works out the object's size on each axis. Each view gives the ratio between the two axes it
    /// shows (from the silhouette's bounding box); these ratios are reconciled by least squares in
    /// log space and the result is scaled so that <see cref="HullOptions.ScaleAxis"/> matches
    /// <see cref="HullOptions.ScaleSize"/>.
    /// </summary>
    internal static double[] SolveSize(IReadOnlyList<ViewDirection> views, IReadOnlyList<PixelBounds> bounds, HullOptions options, List<string> warnings)
    {
        // Each edge says: log(size[b]) - log(size[a]) = r
        var edges = new List<(int A, int B, double R)>();
        for (int n = 0; n < views.Count; n++)
        {
            var (u, v) = ViewGeometry.ImageAxes(views[n]);
            edges.Add(((int)u, (int)v, Math.Log((double)bounds[n].Height / bounds[n].Width)));
        }

        var log = new double[3];
        var known = new bool[3];
        int reference = edges[0].A;
        known[reference] = true;
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var (a, b, r) in edges)
            {
                if (known[a] && !known[b]) { log[b] = log[a] + r; known[b] = changed = true; }
                else if (known[b] && !known[a]) { log[a] = log[b] - r; known[a] = changed = true; }
            }
        }

        for (int iteration = 0; iteration < 200; iteration++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == reference || !known[axis]) continue;
                double sum = 0;
                int count = 0;
                foreach (var (a, b, r) in edges)
                {
                    if (a == axis) { sum += log[b] - r; count++; }
                    else if (b == axis) { sum += log[a] + r; count++; }
                }
                if (count > 0) log[axis] = sum / count;
            }
        }

        double worst = edges.Max(e => Math.Abs(log[e.B] - log[e.A] - e.R));
        if (worst > Math.Log(1.1))
            warnings.Add($"The pictures disagree about the object's proportions by about {(Math.Exp(worst) - 1) * 100:0}%. " +
                         "Silhouettes were stretched to fit; check that photos are taken straight-on and correctly rotated.");

        double knownMean = Enumerable.Range(0, 3).Where(a => known[a]).Average(a => log[a]);
        for (int axis = 0; axis < 3; axis++)
        {
            if (known[axis]) continue;
            log[axis] = knownMean;
            warnings.Add($"No picture shows the {AxisName((Axis)axis)}; it was guessed. Add another view for an accurate shape.");
        }

        var size = log.Select(Math.Exp).ToArray();
        double scale = options.ScaleSize / size[(int)options.ScaleAxis];
        return size.Select(s => s * scale).ToArray();
    }

    private static double BoxDistance(double coordinate, double extent) => Math.Max(-coordinate, coordinate - extent);

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    public static string AxisName(Axis axis) => axis switch
    {
        Axis.X => "width (X)",
        Axis.Y => "depth (Y)",
        _ => "height (Z)",
    };
}

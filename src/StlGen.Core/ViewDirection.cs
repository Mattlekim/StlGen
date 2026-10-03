namespace StlGen.Core;

public enum Axis { X = 0, Y = 1, Z = 2 }

/// <summary>
/// Which side of the object a picture shows. World axes: X = width (left to right as seen from the
/// front), Y = depth (front to back), Z = height (up).
/// </summary>
public enum ViewDirection
{
    /// <summary>Looking at the front. Image right = +X, image up = +Z.</summary>
    Front,
    /// <summary>Looking at the back. Image right = -X, image up = +Z.</summary>
    Back,
    /// <summary>Looking at the object's left side. Image right = front (-Y), image up = +Z.</summary>
    Left,
    /// <summary>Looking at the object's right side. Image right = back (+Y), image up = +Z.</summary>
    Right,
    /// <summary>Looking down from above. Image right = +X, image up = back (+Y).</summary>
    Top,
    /// <summary>Looking up from below. Image right = +X, image up = front (-Y).</summary>
    Bottom,
}

public static class ViewGeometry
{
    /// <summary>World axes that the image's horizontal (U) and vertical (V) directions span.</summary>
    public static (Axis U, Axis V) ImageAxes(ViewDirection view) => view switch
    {
        ViewDirection.Front or ViewDirection.Back => (Axis.X, Axis.Z),
        ViewDirection.Left or ViewDirection.Right => (Axis.Y, Axis.Z),
        _ => (Axis.X, Axis.Y),
    };

    /// <summary>
    /// Projects a point given in normalised object coordinates (0..1 across the bounding box on each
    /// axis) into normalised image coordinates (u: 0 = left, 1 = right; v: 0 = top, 1 = bottom).
    /// </summary>
    public static (double U, double V) Project(ViewDirection view, double px, double py, double pz) => view switch
    {
        ViewDirection.Front => (px, 1 - pz),
        ViewDirection.Back => (1 - px, 1 - pz),
        ViewDirection.Left => (1 - py, 1 - pz),
        ViewDirection.Right => (py, 1 - pz),
        ViewDirection.Top => (px, 1 - py),
        ViewDirection.Bottom => (px, py),
        _ => throw new ArgumentOutOfRangeException(nameof(view)),
    };
}

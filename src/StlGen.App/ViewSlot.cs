using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using StlGen.Core;

namespace StlGen.App;

/// <summary>One picture slot (e.g. "Front") with its image, orientation fix-ups and extracted silhouette.</summary>
public sealed class ViewSlot : INotifyPropertyChanged
{
    private RawImage? _source;
    private int _quarterTurns;
    private bool _mirror;
    private ImageSource? _preview;
    private string? _fileName;
    private string _status = "Drop an image here or click Load";

    public ViewSlot(ViewDirection view, string title, string hint)
    {
        View = view;
        Title = title;
        Hint = hint;
    }

    public ViewDirection View { get; }
    public string Title { get; }
    public string Hint { get; }

    public Mask? Mask { get; private set; }

    public bool HasImage => _source != null;

    public string? FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public ImageSource? Preview { get => _preview; private set => Set(ref _preview, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public void Load(RawImage image, string fileName)
    {
        _source = image;
        _quarterTurns = 0;
        _mirror = false;
        FileName = fileName;
        OnPropertyChanged(nameof(HasImage));
    }

    public void Clear()
    {
        _source = null;
        Mask = null;
        Preview = null;
        FileName = null;
        Status = "Drop an image here or click Load";
        OnPropertyChanged(nameof(HasImage));
    }

    public void Rotate() => _quarterTurns = (_quarterTurns + 1) % 4;

    public void ToggleMirror() => _mirror = !_mirror;

    /// <summary>Re-extracts the silhouette and refreshes the preview.</summary>
    public void Update(SilhouetteOptions options, bool showMask)
    {
        if (_source == null) return;
        var image = _source.Transformed(_quarterTurns, _mirror);
        Mask = SilhouetteExtractor.Extract(image, options);
        Preview = ImageIO.RenderPreview(image, Mask, showMask);

        var bounds = Mask.Bounds();
        Status = bounds is { } b
            ? $"{image.Width}×{image.Height} px · object {b.Width}×{b.Height} px"
            : "No object found — adjust the tolerance or try Invert";
        if (bounds is { } bb && (bb.MinX == 0 || bb.MinY == 0 || bb.MaxX == image.Width - 1 || bb.MaxY == image.Height - 1))
            Status += " · touches the image edge!";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}

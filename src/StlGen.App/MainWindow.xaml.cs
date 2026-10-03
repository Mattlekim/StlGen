using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using StlGen.Core;

namespace StlGen.App;

public partial class MainWindow : Window
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";

    private readonly List<ViewSlot> _slots = new()
    {
        new(ViewDirection.Front, "Front", "Right of image = object's right, up = up"),
        new(ViewDirection.Back, "Back", "Seen from behind, up = up"),
        new(ViewDirection.Left, "Left side", "Object's left side; right of image = front"),
        new(ViewDirection.Right, "Right side", "Object's right side; left of image = front"),
        new(ViewDirection.Top, "Top", "Looking down; top of image = back"),
        new(ViewDirection.Bottom, "Bottom", "Looking up; top of image = front, right = right"),
    };

    private HullResult? _result;
    private CancellationTokenSource? _generation;

    // Orbit camera state (angles in degrees).
    private double _yaw = -60, _pitch = 25, _distance = 300;
    private Point3D _target;
    private Point? _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        SlotsControl.ItemsSource = _slots;
        UpdateCamera();
    }

    private SilhouetteOptions SilhouetteOptions => new()
    {
        Tolerance = ToleranceSlider.Value,
        Invert = InvertCheck.IsChecked == true,
        FillHoles = FillHolesCheck.IsChecked == true,
        KeepLargestComponent = LargestCheck.IsChecked == true,
    };

    private void RefreshSlot(ViewSlot slot)
    {
        try
        {
            slot.Update(SilhouetteOptions, ShowMaskCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "StlGen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshAllSlots()
    {
        if (SlotsControl == null) return; // still initialising
        foreach (var slot in _slots.Where(s => s.HasImage)) RefreshSlot(slot);
    }

    private void LoadInto(ViewSlot slot, string path)
    {
        try
        {
            slot.Load(ImageIO.Load(path), Path.GetFileName(path));
            RefreshSlot(slot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load {path}:\n{ex.Message}", "StlGen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static ViewSlot SlotOf(object sender) => (ViewSlot)((FrameworkElement)sender).Tag;

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var slot = SlotOf(sender);
        var dialog = new OpenFileDialog { Filter = ImageFilter, Title = $"Picture of the {slot.Title.ToLowerInvariant()}" };
        if (dialog.ShowDialog(this) == true) LoadInto(slot, dialog.FileName);
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        var slot = SlotOf(sender);
        slot.Rotate();
        RefreshSlot(slot);
    }

    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        var slot = SlotOf(sender);
        slot.ToggleMirror();
        RefreshSlot(slot);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => SlotOf(sender).Clear();

    private void Slot_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Slot_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            LoadInto((ViewSlot)((FrameworkElement)sender).DataContext, files[0]);
    }

    private void Settings_Changed(object sender, RoutedEventArgs e) => RefreshAllSlots();

    private void Tolerance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => RefreshAllSlots();

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_generation != null)
        {
            _generation.Cancel();
            return;
        }

        var views = _slots.Where(s => s.Mask != null && !s.Mask.IsEmpty).Select(s => new ViewInput(s.View, s.Mask!)).ToList();
        if (views.Count == 0)
        {
            MessageBox.Show(this, "Load at least one picture in which an object was detected.", "StlGen");
            return;
        }
        if (!double.TryParse(ScaleSizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double scaleSize) &&
            !double.TryParse(ScaleSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out scaleSize) ||
            scaleSize <= 0)
        {
            MessageBox.Show(this, "Enter a positive size in millimetres.", "StlGen");
            return;
        }

        var options = new HullOptions
        {
            Resolution = (int)ResolutionSlider.Value,
            ScaleAxis = (Axis)ScaleAxisCombo.SelectedIndex,
            ScaleSize = scaleSize,
        };

        _generation = new CancellationTokenSource();
        GenerateButton.Content = "Cancel";
        SaveButton.IsEnabled = false;
        ResultText.Text = "Generating…";
        var progress = new Progress<double>(p => Progress.Value = p);
        try
        {
            var token = _generation.Token;
            _result = await Task.Run(() => HullBuilder.Build(views, options, progress, token), token);
            ShowMesh(_result.Mesh);
            SaveButton.IsEnabled = _result.Mesh.TriangleCount > 0;

            var s = _result.Size;
            var text = $"Size: {s.X:0.#} × {s.Y:0.#} × {s.Z:0.#} mm (W × D × H) · {_result.Mesh.TriangleCount:N0} triangles · " +
                       $"volume {_result.Mesh.Volume() / 1000:0.##} cm³ · from {views.Count} view(s)";
            if (views.Count < 2)
                text += "\nTip: with a single picture the shape is just an extrusion. Add more views.";
            foreach (var warning in _result.Warnings) text += "\n⚠ " + warning;
            ResultText.Text = text;
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            ResultText.Text = "Error: " + ex.Message;
        }
        finally
        {
            _generation.Dispose();
            _generation = null;
            GenerateButton.Content = "Generate 3D shape";
            Progress.Value = 0;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;
        var dialog = new SaveFileDialog { Filter = "STL file|*.stl", FileName = "object.stl", DefaultExt = ".stl" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StlWriter.WriteBinary(_result.Mesh, dialog.FileName);
            ResultText.Text += $"\nSaved to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- 3D preview ----

    private void ShowMesh(Core.Mesh mesh)
    {
        var geometry = new MeshGeometry3D();
        var positions = new Point3DCollection(mesh.Vertices.Count);
        foreach (var v in mesh.Vertices) positions.Add(new Point3D(v.X, v.Y, v.Z));
        geometry.Positions = positions;
        geometry.TriangleIndices = new Int32Collection(mesh.Indices);
        geometry.Freeze();

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x4F, 0x9D, 0xE8))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), 30));
        var model = new GeometryModel3D(geometry, material)
        {
            BackMaterial = new DiffuseMaterial(Brushes.IndianRed),
        };
        model.Freeze();
        ModelHost.Content = model;
        EmptyText.Visibility = Visibility.Collapsed;

        var (min, max) = mesh.Bounds();
        _target = new Point3D((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        _distance = Math.Max(1, (max - min).Length() * 1.8);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(
            Math.Cos(pitch) * Math.Cos(yaw),
            Math.Cos(pitch) * Math.Sin(yaw),
            Math.Sin(pitch)) * _distance;
        Camera.Position = _target + offset;
        Camera.LookDirection = -offset;
        Camera.NearPlaneDistance = _distance / 100;
        Camera.FarPlaneDistance = _distance * 10;
    }

    private void View_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition((IInputElement)sender);
        ((UIElement)sender).CaptureMouse();
    }

    private void View_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void View_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition((IInputElement)sender);
        _yaw -= (p.X - start.X) * 0.5;
        _pitch = Math.Clamp(_pitch + (p.Y - start.Y) * 0.5, -89, 89);
        _dragStart = p;
        UpdateCamera();
    }

    private void View_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance *= e.Delta > 0 ? 0.9 : 1.1;
        UpdateCamera();
    }
}

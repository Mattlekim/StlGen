# StlGen

A Windows desktop app (C# / WPF, .NET 8) that turns pictures of an object (front, side, top, …)
into a 3D-printable **STL** of the object's outer shape.

## How it works

1. **Silhouettes.** The object is separated from the background in each picture. Images with a
   transparent background use the alpha channel. Otherwise the background colour is estimated from
   the image border, and every pixel that differs from it by more than the *colour tolerance* counts
   as object.
2. **Visual hull.** Each silhouette is extruded along its viewing direction and the extrusions are
   intersected. The result is the largest solid that matches every picture.
3. **Size.** The proportions come from the silhouettes' bounding boxes (each picture is cropped to
   the object, so the photos don't need the same zoom). You enter one real dimension in mm and the
   rest is scaled to match.
4. **Mesh.** The intersection is sampled as a signed distance field and meshed with marching
   tetrahedra, giving a smooth, closed (watertight) mesh that is written as binary STL in
   millimetres, sitting on Z = 0.

## Taking good pictures

- Shoot **straight-on** from each side, as far away as you can and zoomed in, to keep perspective
  distortion low.
- Use a **plain background that contrasts** with the object (e.g. a dark object on white paper).
- Keep the whole object in frame. It must not touch the image edge.
- Fill as many of the six slots as you can. You need two different directions at minimum; front,
  side and top usually give a good result.
- Match each picture to the hint on its slot, using ⟳ (rotate) and ⇋ (mirror) if needed:

| Slot   | Image right is…        | Image up is…   |
|--------|------------------------|----------------|
| Front  | object's right (+X)    | up (+Z)        |
| Back   | object's left (−X)     | up (+Z)        |
| Left   | front (−Y)             | up (+Z)        |
| Right  | back (+Y)              | up (+Z)        |
| Top    | object's right (+X)    | back (+Y)      |
| Bottom | object's right (+X)    | front (−Y)     |

**Limitation:** a visual hull can't capture concave areas that no silhouette shows, such as the
inside of a bowl or a dent in a flat face. You get the outer envelope only.

## Building and running

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run --project src/StlGen.App
```

Or open `StlGen.sln` in Visual Studio 2022 and press F5.

To make a self-contained `.exe`:

```
dotnet publish src/StlGen.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Project layout

- `src/StlGen.Core`: cross-platform library with background removal, the visual hull, meshing
  and the STL writer. It has no UI dependencies.
- `src/StlGen.App`: the WPF desktop app, with view slots, settings and a 3D preview.
- `tests/StlGen.Core.Tests`: xUnit tests. Run them with `dotnet test`; they also run on Linux and macOS.

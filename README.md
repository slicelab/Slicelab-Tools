# Slicelab Tools

A Grasshopper plugin for Rhino 8 — mesh processing, tetrahedral meshing and lattice generation, texture mapping, visualization, PDF layout, and file export.

**📖 Documentation: [slicelab-gh-tools-docs.vercel.app](https://slicelab-gh-tools-docs.vercel.app/)**

Cross-platform: macOS (Apple Silicon) and Windows 11 · .NET 7 · AGPL-3.0

## Panels

| Panel | What's in it |
|---|---|
| **Geometry** | Mesh booleans (Manifold), Adaptive TriRemesh (mmg), decimate, refine, repair, bounding box, curve utilities |
| **Geometry Viz** | Attractor field rendering, texture mapping, gradient legends |
| **Tet Tools** | Tetrahedralize (TetGen + MMG3D) and 10 lattice generators |
| **PDF** | Composable multi-page PDF layout + quick export |
| **Export** | STL, 3MF, GLB (Draco), TXT, viewport/render capture |
| **Utilities** | Data flow, text, layers, unit conversion, math |

See the [documentation site](https://slicelab-gh-tools-docs.vercel.app/) for every component, inputs/outputs, and usage.

## Install

1. Copy `SlicelabTools.gha` and the native libraries (`TetgenWrapper`, `MmgsWrapper`, `ManifoldWrapper` — `.dylib` on macOS / `.dll` on Windows) into your Grasshopper Libraries folder
2. **Windows only:** Rhino must use the NETCore runtime — run `SetDotNetRuntime`, select **NETCore**, restart Rhino
3. Restart Rhino

## Build from source

Build the three native libraries first (CMake), then the plugin:

```bash
# each of: native/tetgen, native/mmg, native/manifold
cd native/<lib> && mkdir -p build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release && make -j        # macOS
# Windows: cmake .. -G "Visual Studio 17 2022" -A x64 && cmake --build . --config Release

# then the plugin
dotnet build src/Slicelab/Slicelab.csproj --configuration Release
```

Output: `src/Slicelab/bin/Release/net7.0/SlicelabTools.gha` (native libraries are copied alongside automatically when present).

## License

[AGPL-3.0](LICENSE). Slicelab Tools links [TetGen](https://wias-berlin.de/software/tetgen/), which is dual-licensed AGPL v3 / commercial; this project uses TetGen under AGPL v3, so the plugin is AGPL-3.0 as well. All bundled libraries: [THIRD_PARTY_LICENSES.txt](THIRD_PARTY_LICENSES.txt).

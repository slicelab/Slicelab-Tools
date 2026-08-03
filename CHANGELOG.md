# Changelog

## 0.28.0

### Fixed
- **PDF Viewport Capture / PDF Image** — images failed with `Method not found: SixLabors.ImageSharp.Image.Load` when another installed Grasshopper plugin bundled a newer ImageSharp. Grasshopper loads all plugin dependencies into a single context, so only one version of a library can ever be used, and whichever plugin loads first wins. PDF image decoding now uses System.Drawing instead of ImageSharp, so it no longer depends on which other plugins are installed.
- Image failures now report which stage failed (decode / jpeg encode / bmp encode) instead of a raw runtime error.

### Added
- **PDF Quick Export** and **PDF Flat Geometry** now accept **open curves**, which are stroked rather than filled. Previously any curve that was not closed was discarded without notice, so line work could not be exported at all. Closed curves are unchanged — they are still sealed and can be filled.

### Changed
- **PDF Quick Export** and **PDF Flat Geometry** — `Fill Color` is no longer required. Supply a fill, a stroke, or both:
  - Stroke Color on its own → that color at 1pt
  - Stroke Weight on its own → black stroke at that weight
  - Nothing supplied → 1pt black line, with a note on the component
  - Existing definitions are unaffected — a supplied fill behaves exactly as before, and an explicit stroke weight of 0 still means "no stroke"
- Curves that cannot be drawn (non-planar, or too few points) now raise a warning saying how many were skipped, instead of disappearing silently.
- **PDF Flat Geometry** accepts a single horizontal line as an element (a rule) — its height comes from the stroke weight, since the page size is set by PDF Page Settings rather than the geometry.
- Clearer errors when geometry has no extent in one axis. `"Curves have zero or invalid bounding box extent"` now names the axis and what to do about it. **PDF Quick Export** still rejects these, because it sizes the page from the geometry and a zero-height page is not a usable file.
- The "default: 1pt black line" note now appears as soon as the style inputs are empty, instead of only after a successful export.

## 0.27.2 — Initial public release

Slicelab Tools is now open source under AGPL-3.0.

Includes:
- **Geometry** — Manifold-based mesh booleans (Union / Difference / Intersection), Mesh Decimate, Mesh Refine, Adaptive TriRemesh (mmgs), Fix Invalid Mesh, Min Bounding Box, Edge Chain, Orient Up, Voronoi Relaxation
- **Geometry Viz** — Attractor Field Renderer, Box/Planar Texture Mapping, Curve Renderer, Gradient Legend, Image Info
- **Tet Tools** — Tetrahedralize (TetGen), 10 lattice generators, TetData custom type
- **PDF** — composable PDF document system (PdfSharpCore) + PDF Quick Export
- **Export** — STL, 3MF, GLB (Draco), TXT, Viewport/Render Capture
- **Utilities** — data flow, Rhino layers, unit conversion, math helpers

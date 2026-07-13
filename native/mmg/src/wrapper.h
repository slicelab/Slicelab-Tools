// MmgsWrapper — Thin C wrapper around mmg's surface remesher (mmgs) for P/Invoke.
// Part of the Slicelab Grasshopper plugin.

#ifndef MMGS_WRAPPER_H
#define MMGS_WRAPPER_H

#ifdef _WIN32
    #define MMGS_EXPORT __declspec(dllexport)
#else
    #define MMGS_EXPORT __attribute__((visibility("default")))
#endif

// Input mesh for remeshing (0-based indices)
struct RemeshInput
{
    double *vertices;        // numVertices * 3 (x, y, z)
    int *triangles;          // numTriangles * 3 (vertex indices, 0-based)
    int numVertices;
    int numTriangles;
    double targetEdgeLength; // uniform target edge length
    double hausd;            // Hausdorff distance (surface deviation tolerance)
    double hgrad;            // gradation parameter (controls size transition, e.g. 1.1)
    int noRidge;             // 1 = disable automatic ridge detection (for closed meshes from Breps)
};

// Per-vertex sizing field (optional, for adaptive remeshing)
struct RemeshMetric
{
    double *sizes;           // numVertices values — target edge length at each vertex
    int numValues;
};

// Required edges and vertices for constrained remeshing
struct RemeshConstraints
{
    int *edges;          // numEdges * 2 (vertex index pairs, 0-based)
    int numEdges;
    int *vertices;       // required vertex indices (0-based)
    int numVertices;
};

// Output remeshed surface (0-based indices)
struct RemeshOutput
{
    double *vertices;        // numVertices * 3
    int *triangles;          // numTriangles * 3 (0-based)
    int numVertices;
    int numTriangles;
};

#ifdef __cplusplus
extern "C" {
#endif

// Single-pass uniform remesh
MMGS_EXPORT RemeshOutput* mmgs_remesh_uniform(RemeshInput* input);

// Single-pass adaptive remesh with per-vertex sizing field
MMGS_EXPORT RemeshOutput* mmgs_remesh_adaptive(RemeshInput* input, RemeshMetric* metric);

// Single-pass uniform remesh with required edges/vertices
MMGS_EXPORT RemeshOutput* mmgs_remesh_uniform_constrained(RemeshInput* input, RemeshConstraints* constraints);

// Single-pass adaptive remesh with required edges/vertices
MMGS_EXPORT RemeshOutput* mmgs_remesh_adaptive_constrained(RemeshInput* input, RemeshMetric* metric, RemeshConstraints* constraints);

// Free output memory
MMGS_EXPORT void mmgs_free_output(RemeshOutput* output);

// ---------------------------------------------------------------------------
// mmg3d — Volume meshing (tetrahedralization)
// ---------------------------------------------------------------------------

// Input volume mesh for remeshing (0-based indices)
// Requires initial tetrahedra (e.g. from TetGen) — MMG3D is a remesher, not a generator.
struct VolMeshInput
{
    double *vertices;        // numVertices * 3 (x, y, z)
    int *triangles;          // numTriangles * 3 (vertex indices, 0-based)
    int *tetrahedra;         // numTetrahedra * 4 (vertex indices, 0-based)
    int numVertices;
    int numTriangles;
    int numTetrahedra;
    double hsiz;             // target (constant) edge length
    double hmin;             // minimum edge length
    double hmax;             // maximum edge length
    double hausd;            // Hausdorff distance (surface deviation tolerance)
    double hgrad;            // gradation parameter (controls size transition)
};

// Output volume mesh (0-based indices)
struct VolMeshOutput
{
    double *vertices;        // numVertices * 3
    int *triangles;          // numTriangles * 3 (0-based)
    int *tetrahedra;         // numTetrahedra * 4 (0-based)
    int numVertices;
    int numTriangles;
    int numTetrahedra;
    int returnCode;          // MMG3D return code: 0=strong failure, -1=low failure, 1=success
    char *errorLog;          // captured stdout/stderr from mmg3d (null-terminated)
    int errorLogLength;
};

// Per-vertex sizing field for adaptive volume remeshing
struct VolMeshMetric
{
    double *sizes;           // numVertices values — target edge length at each vertex
    int numValues;
};

// Tetrahedralize a closed surface mesh
MMGS_EXPORT VolMeshOutput* mmg3d_tetrahedralize(VolMeshInput* input);

// Adaptive tetrahedralize with per-vertex sizing field
MMGS_EXPORT VolMeshOutput* mmg3d_tetrahedralize_adaptive(VolMeshInput* input, VolMeshMetric* metric);

// Required vertex indices for guided refinement (0-based)
struct VolMeshConstraints
{
    int *requiredVertices;   // 0-based vertex indices to pin
    int numRequired;
};

// Tetrahedralize with required (pinned) vertices
MMGS_EXPORT VolMeshOutput* mmg3d_tetrahedralize_with_required(
    VolMeshInput* input,
    VolMeshConstraints* constraints);

// Free volume mesh output memory
MMGS_EXPORT void mmg3d_free_output(VolMeshOutput* output);

#ifdef __cplusplus
}
#endif

#endif

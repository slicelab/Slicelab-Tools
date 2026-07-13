// ManifoldWrapper — Thin C wrapper around Manifold for P/Invoke.
// Part of the Slicelab Grasshopper plugin.

#ifndef MANIFOLD_WRAPPER_H
#define MANIFOLD_WRAPPER_H

#ifdef _WIN32
    #define MANIFOLD_W_EXPORT __declspec(dllexport)
#else
    #define MANIFOLD_W_EXPORT __attribute__((visibility("default")))
#endif

// Input mesh (0-based indices, double precision)
struct MeshInput
{
    double *vertices;    // numVerts * 3 (x, y, z)
    int *triangles;      // numTris * 3 (vertex indices, 0-based)
    int numVerts;
    int numTris;
};

// Output mesh (0-based indices, double precision)
// errorCode: 0 = success, negative = error
struct MeshOutput
{
    double *vertices;    // numVerts * 3
    int *triangles;      // numTris * 3 (0-based)
    int numVerts;
    int numTris;
    int errorCode;       // 0=ok, negative=error
};

#ifdef __cplusplus
extern "C" {
#endif

// Boolean: operation 0=Union, 1=Difference, 2=Intersection
MANIFOLD_W_EXPORT MeshOutput* manifold_boolean_op(
    MeshInput* meshA, MeshInput* meshB, int operation);

// Simplify: reduce triangle count while staying within tolerance
MANIFOLD_W_EXPORT MeshOutput* manifold_simplify(
    MeshInput* mesh, double tolerance);

// Smooth + Refine: subdivision smoothing then refine to target edge length
MANIFOLD_W_EXPORT MeshOutput* manifold_smooth(
    MeshInput* mesh, double minSharpAngle, double targetEdgeLength);

MANIFOLD_W_EXPORT void manifold_free_output(MeshOutput* output);

#ifdef __cplusplus
}
#endif

#endif

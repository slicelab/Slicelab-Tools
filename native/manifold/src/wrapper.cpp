// ManifoldWrapper — Thin C++ wrapper around Manifold for P/Invoke.
// Part of the Slicelab Grasshopper plugin.

#include "wrapper.h"
#include "manifold/manifold.h"
#include <cstdlib>

using namespace manifold;

// ---------------------------------------------------------------------------
// Helper: create error output with code
// ---------------------------------------------------------------------------
static MeshOutput* make_error(int code)
{
    MeshOutput* output = new MeshOutput();
    output->vertices = nullptr;
    output->triangles = nullptr;
    output->numVerts = 0;
    output->numTris = 0;
    output->errorCode = code;
    return output;
}

// ---------------------------------------------------------------------------
// Helper: build MeshGL64 from flat arrays and auto-merge coincident vertices
// ---------------------------------------------------------------------------
static MeshGL64 build_meshgl(MeshInput* input)
{
    MeshGL64 gl;
    gl.numProp = 3;
    gl.vertProperties.resize(input->numVerts * 3);
    for (int i = 0; i < input->numVerts * 3; i++)
        gl.vertProperties[i] = input->vertices[i];
    gl.triVerts.resize(input->numTris * 3);
    for (int i = 0; i < input->numTris * 3; i++)
        gl.triVerts[i] = (uint64_t)input->triangles[i];

    // Auto-merge coincident vertices — essential for RhinoCommon meshes
    // which often have duplicate vertices at seams (e.g. sphere poles)
    gl.Merge();

    return gl;
}

// ---------------------------------------------------------------------------
// Helper: extract MeshOutput from a Manifold
// ---------------------------------------------------------------------------
static MeshOutput* extract_output(const Manifold& m)
{
    if (m.IsEmpty())
        return make_error(-4);

    MeshGL64 outGL = m.GetMeshGL64();
    int numVerts = (int)outGL.NumVert();
    int numTris = (int)outGL.NumTri();

    if (numVerts == 0 || numTris == 0)
        return make_error(-4);

    MeshOutput* output = new MeshOutput();
    output->numVerts = numVerts;
    output->numTris = numTris;
    output->errorCode = 0;
    output->vertices = new double[numVerts * 3];
    output->triangles = new int[numTris * 3];

    for (int i = 0; i < numVerts * 3; i++)
        output->vertices[i] = outGL.vertProperties[i];
    for (int i = 0; i < numTris * 3; i++)
        output->triangles[i] = (int)outGL.triVerts[i];

    return output;
}

// ---------------------------------------------------------------------------
// manifold_boolean_op: Perform a boolean operation on two triangle meshes
// ---------------------------------------------------------------------------
MeshOutput* manifold_boolean_op(MeshInput* meshA, MeshInput* meshB, int operation)
{
    if (!meshA || !meshB)
        return make_error(-1);
    if (meshA->numVerts == 0 || meshA->numTris == 0)
        return make_error(-1);
    if (meshB->numVerts == 0 || meshB->numTris == 0)
        return make_error(-2);

    MeshGL64 glA = build_meshgl(meshA);
    MeshGL64 glB = build_meshgl(meshB);

    Manifold a(glA);
    if (a.Status() != Manifold::Error::NoError)
        return make_error(-1);

    Manifold b(glB);
    if (b.Status() != Manifold::Error::NoError)
        return make_error(-2);

    OpType op;
    switch (operation)
    {
        case 0: op = OpType::Add; break;
        case 1: op = OpType::Subtract; break;
        case 2: op = OpType::Intersect; break;
        default: return make_error(-3);
    }

    Manifold result = a.Boolean(b, op);
    if (result.Status() != Manifold::Error::NoError)
        return make_error(-3);

    return extract_output(result);
}

// ---------------------------------------------------------------------------
// manifold_simplify: Reduce triangle count while preserving manifoldness
// ---------------------------------------------------------------------------
MeshOutput* manifold_simplify(MeshInput* mesh, double tolerance)
{
    if (!mesh || mesh->numVerts == 0 || mesh->numTris == 0)
        return make_error(-1);

    MeshGL64 gl = build_meshgl(mesh);

    Manifold m(gl);
    if (m.Status() != Manifold::Error::NoError)
        return make_error(-1);

    Manifold simplified = m.Simplify(tolerance);
    if (simplified.Status() != Manifold::Error::NoError)
        return make_error(-3);

    return extract_output(simplified);
}

// ---------------------------------------------------------------------------
// manifold_smooth: Smooth a mesh then refine to target edge length
// ---------------------------------------------------------------------------
MeshOutput* manifold_smooth(MeshInput* mesh, double minSharpAngle, double targetEdgeLength)
{
    if (!mesh || mesh->numVerts == 0 || mesh->numTris == 0)
        return make_error(-1);

    MeshGL64 gl = build_meshgl(mesh);

    Manifold m(gl);
    if (m.Status() != Manifold::Error::NoError)
        return make_error(-1);

    // SmoothOut calculates tangent vectors for smooth interpolation.
    // minSharpAngle: edges sharper than this angle (degrees) are kept sharp.
    Manifold smoothed = m.SmoothOut(minSharpAngle);
    if (smoothed.Status() != Manifold::Error::NoError)
        return make_error(-3);

    // RefineToLength subdivides to reach target edge length using the
    // smooth tangent vectors computed above.
    Manifold refined = smoothed.RefineToLength(targetEdgeLength);
    if (refined.Status() != Manifold::Error::NoError)
        return make_error(-3);

    return extract_output(refined);
}

// ---------------------------------------------------------------------------
// manifold_free_output: Free output arrays and struct
// ---------------------------------------------------------------------------
void manifold_free_output(MeshOutput* output)
{
    if (output == nullptr) return;

    delete[] output->vertices;
    delete[] output->triangles;
    delete output;
}

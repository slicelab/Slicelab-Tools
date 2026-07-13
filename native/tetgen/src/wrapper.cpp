// Cross-platform TetGen wrapper for Slicelab Grasshopper plugin.
// Based on Tetrino by Tom Svilans (GPL v3), adapted for cross-platform use.

#include "wrapper.h"
#include <cstring>

// ---------------------------------------------------------------------------
// io_to_tetgenio: Convert InteropMesh input into tetgenio for TetGen.
// ---------------------------------------------------------------------------
tetgenio* io_to_tetgenio(InteropMesh* mesh)
{
    tetgenio* io = new tetgenio();
    tetgenio::facet *f;
    tetgenio::polygon *p;

    int nverts = mesh->numVertices;
    int nfaces = mesh->numFaces;

    io->numberofpoints = nverts;
    io->firstnumber = 0;
    io->mesh_dim = 3;

    // Allocate and copy vertex data using memcpy (original had a bug:
    // *(io->pointlist) = *(mesh->vertices) only copies 1 double).
    int nverts3 = nverts * 3;
    io->pointlist = new REAL[nverts3];
    std::memcpy(io->pointlist, mesh->vertices, nverts3 * sizeof(double));

    // Build facet list
    io->numberoffacets = nfaces;
    io->facetlist = new tetgenio::facet[nfaces];

    int fi = 0; // running index into faceIndices
    for (int i = 0; i < nfaces; i++) {
        f = &io->facetlist[i];
        io->init(f);
        f->numberofpolygons = 1;
        f->polygonlist = new tetgenio::polygon[1];
        p = &f->polygonlist[0];
        io->init(p);

        p->numberofvertices = mesh->faceSizes[i];
        p->vertexlist = new int[p->numberofvertices];
        for (int j = 0; j < p->numberofvertices; ++j) {
            p->vertexlist[j] = mesh->faceIndices[fi];
            fi++;
        }
    }

    return io;
}

// ---------------------------------------------------------------------------
// tetgenio_to_io: Convert tetgenio output into a new InteropMesh.
// ---------------------------------------------------------------------------
InteropMesh* tetgenio_to_io(tetgenio* io)
{
    InteropMesh* mesh = new InteropMesh();

    // Tetrahedra
    mesh->numTetra = io->numberoftetrahedra;
    mesh->tetra = new int[mesh->numTetra * 4];
    std::memcpy(mesh->tetra, io->tetrahedronlist, mesh->numTetra * 4 * sizeof(int));

    // Faces (output trifaces)
    mesh->numFaces = io->numberoftrifaces;
    mesh->numFaceIndices = mesh->numFaces * 3;
    mesh->faceIndices = new int[mesh->numFaceIndices];
    mesh->faceSizes = new int[mesh->numFaces];

    std::memcpy(mesh->faceIndices, io->trifacelist, mesh->numFaceIndices * sizeof(int));
    for (int i = 0; i < mesh->numFaces; ++i) {
        mesh->faceSizes[i] = 3;
    }

    // Vertices
    mesh->numVertices = io->numberofpoints;
    mesh->vertices = new double[mesh->numVertices * 3];
    std::memcpy(mesh->vertices, io->pointlist, mesh->numVertices * 3 * sizeof(double));

    // Edges
    mesh->numEdges = io->numberofedges;
    mesh->edges = new int[mesh->numEdges * 2];
    std::memcpy(mesh->edges, io->edgelist, mesh->numEdges * 2 * sizeof(int));

    return mesh;
}

// ---------------------------------------------------------------------------
// performTetgen: Main entry point called from C# via P/Invoke.
// Returns a new InteropMesh* on success, or nullptr on error.
// ---------------------------------------------------------------------------
InteropMesh* performTetgen(InteropMesh* mesh, TetgenBehaviour* tb)
{
    tetgenbehavior beh;

    // Copy behaviour settings
    beh.plc              = tb->plc;
    beh.refine           = tb->refine;
    beh.quality          = tb->quality;
    beh.coarsen          = tb->coarsen;
    beh.insertaddpoints  = tb->insertaddpoints;
    beh.supsteiner_level = tb->supsteiner_level;
    beh.optlevel         = tb->optlevel;
    beh.maxvolume        = tb->maxvolume;
    beh.minratio         = tb->minratio;
    beh.mindihedral      = tb->mindihedral;
    beh.optmaxdihedral   = tb->optmaxdihedral;
    beh.optminslidihed   = tb->optminslidihed;
    beh.optminsmtdihed   = tb->optminsmtdihed;

    // Suppress all terminal output (this runs inside a GUI app)
    beh.quiet   = 1;
    beh.verbose = 0;

    // Enable edge output
    beh.edgesout = 1;

    // Volume constraints — only use fixedvolume (uniform constraint).
    // varvolume requires per-region attributes which we don't provide.
    if (tb->maxvolume > 0.0) {
        beh.fixedvolume = 1;
    }

    // ------------------------------------------------------------------
    // Switch validation logic (from TetGen's parse_commandline)
    // ------------------------------------------------------------------
    if (beh.nobisect && (!beh.plc && !beh.refine)) {
        beh.plc = 1;
    }
    if (beh.quality && (!beh.plc && !beh.refine)) {
        beh.plc = 1;
    }
    if (beh.diagnose && !beh.plc) {
        beh.plc = 1;
    }

    if ((beh.refine || beh.plc) && beh.weighted) {
        // Incompatible switches — return null instead of crashing
        return nullptr;
    }

    if (beh.convex) {
        if (beh.plc && !beh.regionattrib) {
            beh.regionattrib = 1;
        }
    }

    if (beh.refine || !beh.plc) {
        beh.regionattrib = 0;
    }

    if (!beh.refine && !beh.plc) {
        beh.varvolume = 0;
    }

    if (beh.fixedvolume || beh.varvolume) {
        if (beh.quality == 0) {
            beh.quality = 1;
            if (!beh.plc && !beh.refine) {
                beh.plc = 1;
            }
        }
    }

    if (!beh.quality) {
        if (beh.optmaxdihedral < 179.0) {
            if (beh.nobisect) {
                beh.optmaxdihedral = 179.0;
            } else {
                beh.optmaxdihedral = 179.999;
            }
        }
    }

    // ------------------------------------------------------------------
    // Run TetGen
    // ------------------------------------------------------------------
    tetgenio* in  = io_to_tetgenio(mesh);
    tetgenio* out = new tetgenio();

    try {
        tetrahedralize(&beh, in, out);
    } catch (int errcode) {
        // TetGen throws int on error (e.g. terminatetetgen)
        delete in;
        delete out;
        return nullptr;
    } catch (...) {
        delete in;
        delete out;
        return nullptr;
    }

    InteropMesh* result = tetgenio_to_io(out);

    // Clean up tetgenio objects (their destructors free internal arrays)
    delete in;
    delete out;

    return result;
}

// ---------------------------------------------------------------------------
// performTetgenWithAddin: Insert additional points into an existing tet mesh.
// Uses PLC mode: takes the surface faces from the existing mesh, appends the
// additional points to the vertex list, and re-tetrahedralizes from scratch.
// This avoids TetGen's refine+insertaddpoints code path which has a memory
// corruption bug in freememory() (crashes Rhino with SIGABRT).
// ---------------------------------------------------------------------------
InteropMesh* performTetgenWithAddin(
    InteropMesh* mesh,
    TetgenBehaviour* tb,
    double* addPoints,
    int numAddPoints)
{
    if (!mesh || !addPoints || numAddPoints <= 0)
        return nullptr;

    if (mesh->numFaces <= 0 || mesh->faceIndices == nullptr)
        return nullptr;  // need surface faces for PLC mode

    tetgenbehavior beh;

    // PLC mode: constrained Delaunay tetrahedralization of the surface
    // with interior guide points included in the vertex list.
    beh.plc              = 1;
    beh.refine           = 0;
    beh.insertaddpoints  = 0;
    beh.quality          = 0;  // no quality constraints — MMG3D refines next
    beh.coarsen          = 0;
    beh.supsteiner_level = 0;
    beh.optlevel         = 0;
    beh.maxvolume        = 0;
    beh.minratio         = 0;
    beh.mindihedral      = 0;
    beh.optmaxdihedral   = 165.0;
    beh.optminslidihed   = 179.0;
    beh.optminsmtdihed   = 179.0;

    // Suppress all terminal output
    beh.quiet   = 1;
    beh.verbose = 0;

    // Enable edge output
    beh.edgesout = 1;

    // Build input: existing vertices + guide points, surface faces as facets
    int totalVerts = mesh->numVertices + numAddPoints;

    tetgenio* in = new tetgenio();
    in->firstnumber = 0;
    in->mesh_dim = 3;

    // Combined vertex list: existing mesh vertices followed by guide points
    in->numberofpoints = totalVerts;
    in->pointlist = new REAL[totalVerts * 3];
    std::memcpy(in->pointlist, mesh->vertices,
                mesh->numVertices * 3 * sizeof(double));
    std::memcpy(in->pointlist + mesh->numVertices * 3, addPoints,
                numAddPoints * 3 * sizeof(double));

    // Surface faces as PLC facets (each face becomes a single-polygon facet)
    in->numberoffacets = mesh->numFaces;
    in->facetlist = new tetgenio::facet[mesh->numFaces];

    int fi = 0;
    for (int i = 0; i < mesh->numFaces; i++) {
        tetgenio::facet* f = &in->facetlist[i];
        in->init(f);
        f->numberofpolygons = 1;
        f->polygonlist = new tetgenio::polygon[1];
        tetgenio::polygon* p = &f->polygonlist[0];
        in->init(p);

        p->numberofvertices = mesh->faceSizes[i];
        p->vertexlist = new int[p->numberofvertices];
        for (int j = 0; j < p->numberofvertices; ++j) {
            p->vertexlist[j] = mesh->faceIndices[fi++];
        }
    }

    tetgenio* out = new tetgenio();

    try {
        tetrahedralize(&beh, in, out);
    } catch (int errcode) {
        delete in;
        delete out;
        return nullptr;
    } catch (...) {
        delete in;
        delete out;
        return nullptr;
    }

    InteropMesh* result = tetgenio_to_io(out);

    delete in;
    delete out;

    return result;
}

// ---------------------------------------------------------------------------
// freeMesh: Free all arrays in the InteropMesh AND the struct itself.
// Called from C# after marshalling the data.
// ---------------------------------------------------------------------------
void freeMesh(InteropMesh* mesh)
{
    if (mesh == nullptr) return;

    delete[] mesh->vertices;
    delete[] mesh->faceIndices;
    delete[] mesh->faceSizes;
    delete[] mesh->tetra;
    delete[] mesh->edges;

    delete mesh;
}

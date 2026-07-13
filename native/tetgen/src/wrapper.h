// Cross-platform TetGen wrapper for Slicelab Grasshopper plugin.
// Based on Tetrino by Tom Svilans (GPL v3), adapted for cross-platform use.

#ifndef WRAPPER_H
#define WRAPPER_H

#include <cstdlib>
#include "tetgen.h"

// Cross-platform export macro
#ifdef _WIN32
    #define TETGEN_EXPORT __declspec(dllexport)
#else
    #define TETGEN_EXPORT __attribute__((visibility("default")))
#endif

struct InteropMesh
{
    double *vertices;    // numVertices * 3
    int *faceIndices;
    int *faceSizes;
    int *tetra;          // numTetra * 4
    int *edges;          // numEdges * 2
    int numVertices;
    int numFaces;
    int numFaceIndices;
    int numTetra;
    int numEdges;
};

struct TetgenBehaviour
{
    int plc;
    int refine;
    int quality;
    int coarsen;
    int insertaddpoints;

    int supsteiner_level;
    int optlevel;

    double maxvolume;
    double minratio;
    double mindihedral;

    double optmaxdihedral;
    double optminslidihed;
    double optminsmtdihed;
};

extern "C" {

TETGEN_EXPORT InteropMesh* performTetgen(InteropMesh* mesh, TetgenBehaviour* behaviour);

TETGEN_EXPORT InteropMesh* performTetgenWithAddin(
    InteropMesh* mesh,
    TetgenBehaviour* behaviour,
    double* addPoints,
    int numAddPoints);

TETGEN_EXPORT void freeMesh(InteropMesh* mesh);

}

// Internal helpers (not exported)
tetgenio* io_to_tetgenio(InteropMesh* mesh);
InteropMesh* tetgenio_to_io(tetgenio* io);

#endif

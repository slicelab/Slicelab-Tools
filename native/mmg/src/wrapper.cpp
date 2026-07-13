// MmgsWrapper — Thin C wrapper around mmg's surface remesher (mmgs)
// and volume mesher (mmg3d) for P/Invoke.
// Part of the Slicelab Grasshopper plugin.

#include "wrapper.h"
#include "libmmgs.h"
#include "libmmg3d.h"
#include <cstdlib>
#include <cstring>
#include <cstdio>
#include <string>

#ifndef _WIN32
#include <unistd.h>
#include <fcntl.h>
#endif

// ---------------------------------------------------------------------------
// Helper: capture stdout+stderr into a string during a block of code (POSIX)
// ---------------------------------------------------------------------------
struct OutputCapture
{
    int saved_stdout;
    int saved_stderr;
    int pipe_read;
    int pipe_write;
    bool active;

    OutputCapture() : saved_stdout(-1), saved_stderr(-1), pipe_read(-1), pipe_write(-1), active(false) {}

    void start()
    {
#ifndef _WIN32
        int pipefd[2];
        if (pipe(pipefd) != 0) return;
        pipe_read = pipefd[0];
        pipe_write = pipefd[1];

        // Make read end non-blocking
        fcntl(pipe_read, F_SETFL, O_NONBLOCK);

        fflush(stdout);
        fflush(stderr);
        saved_stdout = dup(fileno(stdout));
        saved_stderr = dup(fileno(stderr));
        dup2(pipe_write, fileno(stdout));
        dup2(pipe_write, fileno(stderr));
        active = true;
#endif
    }

    std::string stop()
    {
        std::string captured;
#ifndef _WIN32
        if (!active) return captured;

        fflush(stdout);
        fflush(stderr);
        close(pipe_write);
        pipe_write = -1;

        dup2(saved_stdout, fileno(stdout));
        dup2(saved_stderr, fileno(stderr));
        close(saved_stdout);
        close(saved_stderr);
        saved_stdout = saved_stderr = -1;
        active = false;

        char buf[4096];
        ssize_t n;
        while ((n = read(pipe_read, buf, sizeof(buf) - 1)) > 0)
        {
            buf[n] = '\0';
            captured += buf;
        }
        close(pipe_read);
        pipe_read = -1;
#endif
        return captured;
    }

    ~OutputCapture()
    {
        if (active) stop();
    }
};

// ---------------------------------------------------------------------------
// Helper: set up mmgs mesh from RemeshInput (handles 0→1 index conversion)
// ---------------------------------------------------------------------------
static int setup_mesh(MMG5_pMesh mesh, MMG5_pSol met, RemeshInput* input,
                      RemeshConstraints* constraints = nullptr)
{
    int numEdges = (constraints && constraints->numEdges > 0) ? constraints->numEdges : 0;

    if (MMGS_Set_meshSize(mesh, input->numVertices, input->numTriangles, numEdges) != 1)
        return 0;

    for (int i = 0; i < input->numVertices; i++)
    {
        if (MMGS_Set_vertex(mesh,
            input->vertices[i * 3],
            input->vertices[i * 3 + 1],
            input->vertices[i * 3 + 2],
            0, i + 1) != 1)
            return 0;
    }

    for (int i = 0; i < input->numTriangles; i++)
    {
        if (MMGS_Set_triangle(mesh,
            input->triangles[i * 3] + 1,
            input->triangles[i * 3 + 1] + 1,
            input->triangles[i * 3 + 2] + 1,
            0, i + 1) != 1)
            return 0;
    }

    if (constraints)
    {
        for (int k = 0; k < constraints->numEdges; k++)
        {
            int v0 = constraints->edges[k * 2];
            int v1 = constraints->edges[k * 2 + 1];
            if (MMGS_Set_edge(mesh, v0 + 1, v1 + 1, 0, k + 1) != 1)
                return 0;
            if (MMGS_Set_requiredEdge(mesh, k + 1) != 1)
                return 0;
        }

        for (int k = 0; k < constraints->numVertices; k++)
        {
            if (MMGS_Set_requiredVertex(mesh, constraints->vertices[k] + 1) != 1)
                return 0;
        }
    }

    return 1;
}

// ---------------------------------------------------------------------------
// Helper: extract output mesh from mmgs (handles 1→0 index conversion)
// ---------------------------------------------------------------------------
static RemeshOutput* extract_output(MMG5_pMesh mesh)
{
    MMG5_int np, nt, na;
    if (MMGS_Get_meshSize(mesh, &np, &nt, &na) != 1)
        return nullptr;

    if (np == 0 || nt == 0)
        return nullptr;

    RemeshOutput* output = new RemeshOutput();
    output->numVertices = (int)np;
    output->numTriangles = (int)nt;
    output->vertices = new double[np * 3];
    output->triangles = new int[nt * 3];

    for (MMG5_int i = 0; i < np; i++)
    {
        double x, y, z;
        MMG5_int ref;
        int isCorner, isRequired;
        MMGS_Get_vertex(mesh, &x, &y, &z, &ref, &isCorner, &isRequired);
        output->vertices[i * 3] = x;
        output->vertices[i * 3 + 1] = y;
        output->vertices[i * 3 + 2] = z;
    }

    for (MMG5_int i = 0; i < nt; i++)
    {
        MMG5_int v0, v1, v2, ref;
        int isRequired;
        MMGS_Get_triangle(mesh, &v0, &v1, &v2, &ref, &isRequired);
        output->triangles[i * 3] = (int)(v0 - 1);
        output->triangles[i * 3 + 1] = (int)(v1 - 1);
        output->triangles[i * 3 + 2] = (int)(v2 - 1);
    }

    return output;
}

// ---------------------------------------------------------------------------
// mmgs_remesh_uniform: Single-pass uniform remesh
// ---------------------------------------------------------------------------
RemeshOutput* mmgs_remesh_uniform(RemeshInput* input)
{
    if (!input || input->numVertices == 0 || input->numTriangles == 0)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMGS_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_verbose, -1);
    if (input->noRidge)
        MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_angle, 0);

    if (!setup_mesh(mesh, met, input))
    {
        MMGS_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    double h = input->targetEdgeLength;
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hsiz, h);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hausd, input->hausd);
    if (input->hgrad > 0)
        MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hgrad, input->hgrad);

    int ret = MMGS_mmgslib(mesh, met);

    RemeshOutput* output = nullptr;
    if (ret != MMG5_STRONGFAILURE)
        output = extract_output(mesh);

    MMGS_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmgs_remesh_adaptive: Single-pass adaptive remesh with per-vertex sizing
// ---------------------------------------------------------------------------
RemeshOutput* mmgs_remesh_adaptive(RemeshInput* input, RemeshMetric* metric)
{
    if (!input || !metric || input->numVertices == 0 || input->numTriangles == 0)
        return nullptr;

    if (metric->numValues != input->numVertices)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMGS_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_verbose, -1);
    if (input->noRidge)
        MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_angle, 0);

    if (!setup_mesh(mesh, met, input))
    {
        MMGS_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    // Per-vertex scalar metric (sizing field)
    MMGS_Set_solSize(mesh, met, MMG5_Vertex, input->numVertices, MMG5_Scalar);
    for (int i = 0; i < metric->numValues; i++)
    {
        MMGS_Set_scalarSol(met, metric->sizes[i], i + 1);
    }

    // Global bounds from sizing field
    double globalMin = metric->sizes[0];
    double globalMax = metric->sizes[0];
    for (int i = 1; i < metric->numValues; i++)
    {
        if (metric->sizes[i] < globalMin) globalMin = metric->sizes[i];
        if (metric->sizes[i] > globalMax) globalMax = metric->sizes[i];
    }
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hmin, globalMin);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hmax, globalMax);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hausd, input->hausd);
    if (input->hgrad > 0)
        MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hgrad, input->hgrad);

    int ret = MMGS_mmgslib(mesh, met);

    RemeshOutput* output = nullptr;
    if (ret != MMG5_STRONGFAILURE)
        output = extract_output(mesh);

    MMGS_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmgs_remesh_uniform_constrained: Uniform remesh with required edges/vertices
// ---------------------------------------------------------------------------
RemeshOutput* mmgs_remesh_uniform_constrained(RemeshInput* input, RemeshConstraints* constraints)
{
    if (!input || input->numVertices == 0 || input->numTriangles == 0)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMGS_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_verbose, -1);
    if (input->noRidge)
        MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_angle, 0);

    if (!setup_mesh(mesh, met, input, constraints))
    {
        MMGS_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    double h = input->targetEdgeLength;
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hsiz, h);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hausd, input->hausd);
    if (input->hgrad > 0)
        MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hgrad, input->hgrad);

    int ret = MMGS_mmgslib(mesh, met);

    RemeshOutput* output = nullptr;
    if (ret != MMG5_STRONGFAILURE)
        output = extract_output(mesh);

    MMGS_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmgs_remesh_adaptive_constrained: Adaptive remesh with required edges/vertices
// ---------------------------------------------------------------------------
RemeshOutput* mmgs_remesh_adaptive_constrained(RemeshInput* input, RemeshMetric* metric,
                                                RemeshConstraints* constraints)
{
    if (!input || !metric || input->numVertices == 0 || input->numTriangles == 0)
        return nullptr;

    if (metric->numValues != input->numVertices)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMGS_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_verbose, -1);
    if (input->noRidge)
        MMGS_Set_iparameter(mesh, met, MMGS_IPARAM_angle, 0);

    if (!setup_mesh(mesh, met, input, constraints))
    {
        MMGS_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    // Per-vertex scalar metric (sizing field)
    MMGS_Set_solSize(mesh, met, MMG5_Vertex, input->numVertices, MMG5_Scalar);
    for (int i = 0; i < metric->numValues; i++)
    {
        MMGS_Set_scalarSol(met, metric->sizes[i], i + 1);
    }

    // Global bounds from sizing field
    double globalMin = metric->sizes[0];
    double globalMax = metric->sizes[0];
    for (int i = 1; i < metric->numValues; i++)
    {
        if (metric->sizes[i] < globalMin) globalMin = metric->sizes[i];
        if (metric->sizes[i] > globalMax) globalMax = metric->sizes[i];
    }
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hmin, globalMin);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hmax, globalMax);
    MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hausd, input->hausd);
    if (input->hgrad > 0)
        MMGS_Set_dparameter(mesh, met, MMGS_DPARAM_hgrad, input->hgrad);

    int ret = MMGS_mmgslib(mesh, met);

    RemeshOutput* output = nullptr;
    if (ret != MMG5_STRONGFAILURE)
        output = extract_output(mesh);

    MMGS_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmgs_free_output: Free output arrays and struct
// ---------------------------------------------------------------------------
void mmgs_free_output(RemeshOutput* output)
{
    if (output == nullptr) return;

    delete[] output->vertices;
    delete[] output->triangles;
    delete output;
}

// ===========================================================================
// mmg3d — Volume meshing (tetrahedralization)
// ===========================================================================

// ---------------------------------------------------------------------------
// mmg3d_tetrahedralize: Generate volume mesh from a closed surface
// ---------------------------------------------------------------------------
VolMeshOutput* mmg3d_tetrahedralize(VolMeshInput* input)
{
    if (!input || input->numVertices == 0 || input->numTetrahedra == 0)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMG3D_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMG3D_Set_iparameter(mesh, met, MMG3D_IPARAM_verbose, 5);

    // Set mesh size: np vertices, ne tetrahedra, 0 prisms, nt triangles, 0 quads, 0 edges
    if (MMG3D_Set_meshSize(mesh, input->numVertices, input->numTetrahedra, 0,
                            input->numTriangles, 0, 0) != 1)
    {
        MMG3D_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    // Set vertices (0-based → 1-based)
    for (int i = 0; i < input->numVertices; i++)
    {
        if (MMG3D_Set_vertex(mesh,
            input->vertices[i * 3],
            input->vertices[i * 3 + 1],
            input->vertices[i * 3 + 2],
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set tetrahedra (0-based → 1-based)
    for (int i = 0; i < input->numTetrahedra; i++)
    {
        if (MMG3D_Set_tetrahedron(mesh,
            input->tetrahedra[i * 4] + 1,
            input->tetrahedra[i * 4 + 1] + 1,
            input->tetrahedra[i * 4 + 2] + 1,
            input->tetrahedra[i * 4 + 3] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set triangles (surface boundary, 0-based → 1-based)
    for (int i = 0; i < input->numTriangles; i++)
    {
        if (MMG3D_Set_triangle(mesh,
            input->triangles[i * 3] + 1,
            input->triangles[i * 3 + 1] + 1,
            input->triangles[i * 3 + 2] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set meshing parameters
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hsiz, input->hsiz);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmin, input->hmin);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmax, input->hmax);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hausd, input->hausd);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hgrad, input->hgrad);

    // Capture stdout/stderr from mmg3d for diagnostics
    OutputCapture capture;
    capture.start();

    // Run mmg3d volume mesher
    int ret = MMG3D_mmg3dlib(mesh, met);

    std::string log = capture.stop();

    // Helper to populate errorLog on output struct
    auto setLog = [&](VolMeshOutput* out) {
        if (!log.empty()) {
            out->errorLogLength = (int)log.size();
            out->errorLog = new char[log.size() + 1];
            memcpy(out->errorLog, log.c_str(), log.size() + 1);
        } else {
            out->errorLog = nullptr;
            out->errorLogLength = 0;
        }
    };

    VolMeshOutput* output = nullptr;
    if (ret == MMG5_STRONGFAILURE)
    {
        // Return an output struct with just the error code + log
        output = new VolMeshOutput();
        output->vertices = nullptr;
        output->triangles = nullptr;
        output->tetrahedra = nullptr;
        output->numVertices = 0;
        output->numTriangles = 0;
        output->numTetrahedra = 0;
        output->returnCode = 0; // strong failure
        setLog(output);
    }
    else
    {
        // Extract output mesh sizes
        MMG5_int np, ne, nprism, nt, nquad, na;
        if (MMG3D_Get_meshSize(mesh, &np, &ne, &nprism, &nt, &nquad, &na) == 1
            && np > 0 && ne > 0)
        {
            output = new VolMeshOutput();
            output->numVertices = (int)np;
            output->numTriangles = (int)nt;
            output->numTetrahedra = (int)ne;
            output->returnCode = (ret == MMG5_LOWFAILURE) ? -1 : 1;
            setLog(output);
            output->vertices = new double[np * 3];
            output->triangles = (nt > 0) ? new int[nt * 3] : nullptr;
            output->tetrahedra = new int[ne * 4];

            // Extract vertices (1-based → 0-based implicit via sequential Get)
            for (MMG5_int i = 0; i < np; i++)
            {
                double x, y, z;
                MMG5_int ref;
                int isCorner, isRequired;
                MMG3D_Get_vertex(mesh, &x, &y, &z, &ref, &isCorner, &isRequired);
                output->vertices[i * 3] = x;
                output->vertices[i * 3 + 1] = y;
                output->vertices[i * 3 + 2] = z;
            }

            // Extract triangles (1-based → 0-based)
            for (MMG5_int i = 0; i < nt; i++)
            {
                MMG5_int v0, v1, v2, ref;
                int isRequired;
                MMG3D_Get_triangle(mesh, &v0, &v1, &v2, &ref, &isRequired);
                output->triangles[i * 3] = (int)(v0 - 1);
                output->triangles[i * 3 + 1] = (int)(v1 - 1);
                output->triangles[i * 3 + 2] = (int)(v2 - 1);
            }

            // Extract tetrahedra (1-based → 0-based)
            for (MMG5_int i = 0; i < ne; i++)
            {
                MMG5_int v0, v1, v2, v3, ref;
                int isRequired;
                MMG3D_Get_tetrahedron(mesh, &v0, &v1, &v2, &v3, &ref, &isRequired);
                output->tetrahedra[i * 4] = (int)(v0 - 1);
                output->tetrahedra[i * 4 + 1] = (int)(v1 - 1);
                output->tetrahedra[i * 4 + 2] = (int)(v2 - 1);
                output->tetrahedra[i * 4 + 3] = (int)(v3 - 1);
            }
        }
    }

    MMG3D_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmg3d_tetrahedralize_adaptive: Volume mesh with per-vertex sizing field
// ---------------------------------------------------------------------------
VolMeshOutput* mmg3d_tetrahedralize_adaptive(VolMeshInput* input, VolMeshMetric* metric)
{
    if (!input || !metric || input->numVertices == 0 || input->numTetrahedra == 0)
        return nullptr;

    if (metric->numValues != input->numVertices || metric->sizes == nullptr)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMG3D_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMG3D_Set_iparameter(mesh, met, MMG3D_IPARAM_verbose, 5);

    // Set mesh size: np vertices, ne tetrahedra, 0 prisms, nt triangles, 0 quads, 0 edges
    if (MMG3D_Set_meshSize(mesh, input->numVertices, input->numTetrahedra, 0,
                            input->numTriangles, 0, 0) != 1)
    {
        MMG3D_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    // Set vertices (0-based → 1-based)
    for (int i = 0; i < input->numVertices; i++)
    {
        if (MMG3D_Set_vertex(mesh,
            input->vertices[i * 3],
            input->vertices[i * 3 + 1],
            input->vertices[i * 3 + 2],
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set tetrahedra (0-based → 1-based)
    for (int i = 0; i < input->numTetrahedra; i++)
    {
        if (MMG3D_Set_tetrahedron(mesh,
            input->tetrahedra[i * 4] + 1,
            input->tetrahedra[i * 4 + 1] + 1,
            input->tetrahedra[i * 4 + 2] + 1,
            input->tetrahedra[i * 4 + 3] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set triangles (surface boundary, 0-based → 1-based)
    for (int i = 0; i < input->numTriangles; i++)
    {
        if (MMG3D_Set_triangle(mesh,
            input->triangles[i * 3] + 1,
            input->triangles[i * 3 + 1] + 1,
            input->triangles[i * 3 + 2] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Per-vertex scalar metric (sizing field) — replaces constant hsiz
    MMG3D_Set_solSize(mesh, met, MMG5_Vertex, input->numVertices, MMG5_Scalar);
    for (int i = 0; i < metric->numValues; i++)
    {
        MMG3D_Set_scalarSol(met, metric->sizes[i], i + 1);
    }

    // Derive hmin/hmax from sizing field with slack
    double globalMin = metric->sizes[0];
    double globalMax = metric->sizes[0];
    for (int i = 1; i < metric->numValues; i++)
    {
        if (metric->sizes[i] < globalMin) globalMin = metric->sizes[i];
        if (metric->sizes[i] > globalMax) globalMax = metric->sizes[i];
    }
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmin, globalMin * 0.5);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmax, globalMax * 1.5);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hausd, input->hausd);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hgrad, input->hgrad);

    // Capture stdout/stderr from mmg3d for diagnostics
    OutputCapture capture;
    capture.start();

    // Run mmg3d volume mesher
    int ret = MMG3D_mmg3dlib(mesh, met);

    std::string log = capture.stop();

    // Helper to populate errorLog on output struct
    auto setLog = [&](VolMeshOutput* out) {
        if (!log.empty()) {
            out->errorLogLength = (int)log.size();
            out->errorLog = new char[log.size() + 1];
            memcpy(out->errorLog, log.c_str(), log.size() + 1);
        } else {
            out->errorLog = nullptr;
            out->errorLogLength = 0;
        }
    };

    VolMeshOutput* output = nullptr;
    if (ret == MMG5_STRONGFAILURE)
    {
        // Return an output struct with just the error code + log
        output = new VolMeshOutput();
        output->vertices = nullptr;
        output->triangles = nullptr;
        output->tetrahedra = nullptr;
        output->numVertices = 0;
        output->numTriangles = 0;
        output->numTetrahedra = 0;
        output->returnCode = 0; // strong failure
        setLog(output);
    }
    else
    {
        // Extract output mesh sizes
        MMG5_int np, ne, nprism, nt, nquad, na;
        if (MMG3D_Get_meshSize(mesh, &np, &ne, &nprism, &nt, &nquad, &na) == 1
            && np > 0 && ne > 0)
        {
            output = new VolMeshOutput();
            output->numVertices = (int)np;
            output->numTriangles = (int)nt;
            output->numTetrahedra = (int)ne;
            output->returnCode = (ret == MMG5_LOWFAILURE) ? -1 : 1;
            setLog(output);
            output->vertices = new double[np * 3];
            output->triangles = (nt > 0) ? new int[nt * 3] : nullptr;
            output->tetrahedra = new int[ne * 4];

            // Extract vertices (1-based → 0-based implicit via sequential Get)
            for (MMG5_int i = 0; i < np; i++)
            {
                double x, y, z;
                MMG5_int ref;
                int isCorner, isRequired;
                MMG3D_Get_vertex(mesh, &x, &y, &z, &ref, &isCorner, &isRequired);
                output->vertices[i * 3] = x;
                output->vertices[i * 3 + 1] = y;
                output->vertices[i * 3 + 2] = z;
            }

            // Extract triangles (1-based → 0-based)
            for (MMG5_int i = 0; i < nt; i++)
            {
                MMG5_int v0, v1, v2, ref;
                int isRequired;
                MMG3D_Get_triangle(mesh, &v0, &v1, &v2, &ref, &isRequired);
                output->triangles[i * 3] = (int)(v0 - 1);
                output->triangles[i * 3 + 1] = (int)(v1 - 1);
                output->triangles[i * 3 + 2] = (int)(v2 - 1);
            }

            // Extract tetrahedra (1-based → 0-based)
            for (MMG5_int i = 0; i < ne; i++)
            {
                MMG5_int v0, v1, v2, v3, ref;
                int isRequired;
                MMG3D_Get_tetrahedron(mesh, &v0, &v1, &v2, &v3, &ref, &isRequired);
                output->tetrahedra[i * 4] = (int)(v0 - 1);
                output->tetrahedra[i * 4 + 1] = (int)(v1 - 1);
                output->tetrahedra[i * 4 + 2] = (int)(v2 - 1);
                output->tetrahedra[i * 4 + 3] = (int)(v3 - 1);
            }
        }
    }

    MMG3D_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmg3d_tetrahedralize_with_required: Volume mesh with pinned vertices
// Same as mmg3d_tetrahedralize but marks specific vertices as required
// (MMG3D won't move them during remeshing).
// ---------------------------------------------------------------------------
VolMeshOutput* mmg3d_tetrahedralize_with_required(VolMeshInput* input, VolMeshConstraints* constraints)
{
    if (!input || input->numVertices == 0 || input->numTetrahedra == 0)
        return nullptr;

    if (!constraints || constraints->numRequired <= 0)
        return nullptr;

    MMG5_pMesh mesh = nullptr;
    MMG5_pSol met = nullptr;

    MMG3D_Init_mesh(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh,
        MMG5_ARG_ppMet, &met,
        MMG5_ARG_end);

    MMG3D_Set_iparameter(mesh, met, MMG3D_IPARAM_verbose, 5);

    if (MMG3D_Set_meshSize(mesh, input->numVertices, input->numTetrahedra, 0,
                            input->numTriangles, 0, 0) != 1)
    {
        MMG3D_Free_all(MMG5_ARG_start,
            MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
        return nullptr;
    }

    // Set vertices (0-based → 1-based)
    for (int i = 0; i < input->numVertices; i++)
    {
        if (MMG3D_Set_vertex(mesh,
            input->vertices[i * 3],
            input->vertices[i * 3 + 1],
            input->vertices[i * 3 + 2],
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Mark required vertices (0-based → 1-based)
    for (int k = 0; k < constraints->numRequired; k++)
    {
        MMG3D_Set_requiredVertex(mesh, constraints->requiredVertices[k] + 1);
    }

    // Set tetrahedra (0-based → 1-based)
    for (int i = 0; i < input->numTetrahedra; i++)
    {
        if (MMG3D_Set_tetrahedron(mesh,
            input->tetrahedra[i * 4] + 1,
            input->tetrahedra[i * 4 + 1] + 1,
            input->tetrahedra[i * 4 + 2] + 1,
            input->tetrahedra[i * 4 + 3] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set triangles (surface boundary, 0-based → 1-based)
    for (int i = 0; i < input->numTriangles; i++)
    {
        if (MMG3D_Set_triangle(mesh,
            input->triangles[i * 3] + 1,
            input->triangles[i * 3 + 1] + 1,
            input->triangles[i * 3 + 2] + 1,
            0, i + 1) != 1)
        {
            MMG3D_Free_all(MMG5_ARG_start,
                MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);
            return nullptr;
        }
    }

    // Set meshing parameters
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hsiz, input->hsiz);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmin, input->hmin);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hmax, input->hmax);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hausd, input->hausd);
    MMG3D_Set_dparameter(mesh, met, MMG3D_DPARAM_hgrad, input->hgrad);

    // Capture stdout/stderr
    OutputCapture capture;
    capture.start();

    int ret = MMG3D_mmg3dlib(mesh, met);

    std::string log = capture.stop();

    auto setLog = [&](VolMeshOutput* out) {
        if (!log.empty()) {
            out->errorLogLength = (int)log.size();
            out->errorLog = new char[log.size() + 1];
            memcpy(out->errorLog, log.c_str(), log.size() + 1);
        } else {
            out->errorLog = nullptr;
            out->errorLogLength = 0;
        }
    };

    VolMeshOutput* output = nullptr;
    if (ret == MMG5_STRONGFAILURE)
    {
        output = new VolMeshOutput();
        output->vertices = nullptr;
        output->triangles = nullptr;
        output->tetrahedra = nullptr;
        output->numVertices = 0;
        output->numTriangles = 0;
        output->numTetrahedra = 0;
        output->returnCode = 0;
        setLog(output);
    }
    else
    {
        MMG5_int np, ne, nprism, nt, nquad, na;
        if (MMG3D_Get_meshSize(mesh, &np, &ne, &nprism, &nt, &nquad, &na) == 1
            && np > 0 && ne > 0)
        {
            output = new VolMeshOutput();
            output->numVertices = (int)np;
            output->numTriangles = (int)nt;
            output->numTetrahedra = (int)ne;
            output->returnCode = (ret == MMG5_LOWFAILURE) ? -1 : 1;
            setLog(output);
            output->vertices = new double[np * 3];
            output->triangles = (nt > 0) ? new int[nt * 3] : nullptr;
            output->tetrahedra = new int[ne * 4];

            for (MMG5_int i = 0; i < np; i++)
            {
                double x, y, z;
                MMG5_int ref;
                int isCorner, isRequired;
                MMG3D_Get_vertex(mesh, &x, &y, &z, &ref, &isCorner, &isRequired);
                output->vertices[i * 3] = x;
                output->vertices[i * 3 + 1] = y;
                output->vertices[i * 3 + 2] = z;
            }

            for (MMG5_int i = 0; i < nt; i++)
            {
                MMG5_int v0, v1, v2, ref;
                int isRequired;
                MMG3D_Get_triangle(mesh, &v0, &v1, &v2, &ref, &isRequired);
                output->triangles[i * 3] = (int)(v0 - 1);
                output->triangles[i * 3 + 1] = (int)(v1 - 1);
                output->triangles[i * 3 + 2] = (int)(v2 - 1);
            }

            for (MMG5_int i = 0; i < ne; i++)
            {
                MMG5_int v0, v1, v2, v3, ref;
                int isRequired;
                MMG3D_Get_tetrahedron(mesh, &v0, &v1, &v2, &v3, &ref, &isRequired);
                output->tetrahedra[i * 4] = (int)(v0 - 1);
                output->tetrahedra[i * 4 + 1] = (int)(v1 - 1);
                output->tetrahedra[i * 4 + 2] = (int)(v2 - 1);
                output->tetrahedra[i * 4 + 3] = (int)(v3 - 1);
            }
        }
    }

    MMG3D_Free_all(MMG5_ARG_start,
        MMG5_ARG_ppMesh, &mesh, MMG5_ARG_ppMet, &met, MMG5_ARG_end);

    return output;
}

// ---------------------------------------------------------------------------
// mmg3d_free_output: Free volume mesh output arrays and struct
// ---------------------------------------------------------------------------
void mmg3d_free_output(VolMeshOutput* output)
{
    if (output == nullptr) return;

    delete[] output->vertices;
    delete[] output->triangles;
    delete[] output->tetrahedra;
    delete[] output->errorLog;
    delete output;
}

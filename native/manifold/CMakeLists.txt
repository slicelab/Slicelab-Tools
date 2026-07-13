cmake_minimum_required(VERSION 3.18)
project(ManifoldWrapper LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Build Manifold from source — only the core library, no bindings/tests
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(MANIFOLD_TEST OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PYBIND OFF CACHE BOOL "" FORCE)
set(MANIFOLD_CBIND OFF CACHE BOOL "" FORCE)
set(MANIFOLD_CROSS_SECTION OFF CACHE BOOL "" FORCE)
set(MANIFOLD_PAR OFF CACHE BOOL "" FORCE)
add_subdirectory(manifold-src)

# Our wrapper shared library
add_library(ManifoldWrapper SHARED src/wrapper.cpp)
target_include_directories(ManifoldWrapper PRIVATE
    manifold-src/include
)
target_link_libraries(ManifoldWrapper PRIVATE manifold)

if(APPLE)
    set_target_properties(ManifoldWrapper PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN ON
    )
endif()

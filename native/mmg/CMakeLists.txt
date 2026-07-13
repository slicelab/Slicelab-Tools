cmake_minimum_required(VERSION 3.15)
project(MmgsWrapper LANGUAGES C CXX)

set(CMAKE_C_STANDARD 99)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Build mmg as static library (mmgs surface remesher + mmg3d volume mesher)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)

# Build all mmg libraries (mmgs + mmg3d + mmg2d)
set(BUILD "MMG" CACHE STRING "" FORCE)

# Build static libraries only
set(LIBMMGS_STATIC ON CACHE BOOL "" FORCE)
set(LIBMMGS_SHARED OFF CACHE BOOL "" FORCE)
set(LIBMMG3D_STATIC ON CACHE BOOL "" FORCE)
set(LIBMMG3D_SHARED OFF CACHE BOOL "" FORCE)
set(LIBMMG2D_STATIC ON CACHE BOOL "" FORCE)
set(LIBMMG2D_SHARED OFF CACHE BOOL "" FORCE)
set(LIBMMG_STATIC ON CACHE BOOL "" FORCE)
set(LIBMMG_SHARED OFF CACHE BOOL "" FORCE)

add_subdirectory(mmg-src)

# Our wrapper shared library
add_library(MmgsWrapper SHARED src/wrapper.cpp)

target_include_directories(MmgsWrapper PRIVATE
    mmg-src/src/mmgs
    mmg-src/src/mmg3d
    mmg-src/src/common
    ${CMAKE_BINARY_DIR}/mmg-src/include
    ${CMAKE_BINARY_DIR}/mmg-src/include/mmg/mmgs
    ${CMAKE_BINARY_DIR}/mmg-src/include/mmg/mmg3d
    ${CMAKE_BINARY_DIR}/mmg-src/src/common
)

target_link_libraries(MmgsWrapper PRIVATE libmmgs_a libmmg3d_a)

if(APPLE)
    set_target_properties(MmgsWrapper PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN ON
    )
endif()

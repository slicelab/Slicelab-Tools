cmake_minimum_required(VERSION 3.15)
project(TetgenWrapper LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# TetGen source (compiled as a static library)
add_library(tetgen STATIC
    tetgen1.5.1/tetgen.cxx
    tetgen1.5.1/predicates.cxx
)
target_include_directories(tetgen PUBLIC tetgen1.5.1)
# TETLIBRARY tells TetGen to compile as a library (not standalone app)
target_compile_definitions(tetgen PRIVATE TETLIBRARY)

# Our wrapper shared library
add_library(TetgenWrapper SHARED
    src/wrapper.cpp
)
target_include_directories(TetgenWrapper PRIVATE tetgen1.5.1)
target_link_libraries(TetgenWrapper PRIVATE tetgen)
target_compile_definitions(TetgenWrapper PRIVATE TETLIBRARY)

# On macOS, set default visibility to hidden (only TETGEN_EXPORT symbols exported)
if(APPLE)
    set_target_properties(TetgenWrapper PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN ON
    )
endif()

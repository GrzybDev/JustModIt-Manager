cmake_minimum_required(VERSION 3.22)
project(avifshim C)

set(LIBAVIF_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../libavif" CACHE PATH "libavif sources (git submodule)")
set(DAV1D_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../dav1d" CACHE PATH "dav1d sources (git submodule)")
set(DAV1D_BUILD_DIR "" CACHE PATH "dav1d's meson build folder")

if(NOT EXISTS "${LIBAVIF_SOURCE_DIR}/CMakeLists.txt" OR NOT EXISTS "${DAV1D_BUILD_DIR}/src/libdav1d.a")
  message(FATAL_ERROR "libavif sources or the dav1d build not found, run: native/build.sh")
endif()

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# dav1d builds with meson, so build.sh builds it first and libavif takes this target as the system's dav1d; it is global,
# since libavif adds link libraries to it from its own folder.
add_library(dav1d::dav1d STATIC IMPORTED GLOBAL)
set_target_properties(dav1d::dav1d PROPERTIES IMPORTED_LOCATION "${DAV1D_BUILD_DIR}/src/libdav1d.a")
target_include_directories(dav1d::dav1d INTERFACE "${DAV1D_SOURCE_DIR}/include")

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(AVIF_CODEC_DAV1D SYSTEM CACHE STRING "" FORCE)
# libavif scales with its own copy of libyuv's scaler, which is enough for stills; a libyuv of the build machine's would
# be linked instead.
set(AVIF_LIBYUV OFF CACHE STRING "" FORCE)
add_subdirectory("${LIBAVIF_SOURCE_DIR}" libavif EXCLUDE_FROM_ALL)

add_library(avifshim SHARED avifshim.c)
target_link_libraries(avifshim PRIVATE avif)
set_target_properties(avifshim PROPERTIES C_STANDARD 99 C_VISIBILITY_PRESET hidden)
# The static libavif and dav1d would export their symbols too, so a system libavif in the process could take their place.
target_link_options(avifshim PRIVATE "LINKER:--exclude-libs,ALL")

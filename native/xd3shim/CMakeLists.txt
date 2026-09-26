cmake_minimum_required(VERSION 3.20)
project(xd3shim C CXX)

set(XDELTA3_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../xdelta/xdelta3" CACHE PATH "xdelta3 sources (git submodule)")
set(XDELTA3_VERSION "3.2.0" CACHE STRING "xdelta3 version of XDELTA3_SOURCE_DIR")
# Build liblzma from the pinned submodule instead of letting FetchContent clone it.
set(FETCHCONTENT_SOURCE_DIR_LIBLZMA "${CMAKE_CURRENT_SOURCE_DIR}/../xz" CACHE PATH "xz sources (git submodule)")

if(NOT EXISTS "${XDELTA3_SOURCE_DIR}/xdelta3.h")
  message(FATAL_ERROR "xdelta3 sources not found, run: git submodule update --init")
endif()

set(CMAKE_POSITION_INDEPENDENT_CODE ON)
if(MSVC)
  # Static CRT: the shim must not depend on the VC++ redistributable.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

set(XD3_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(XD3_BUILD_LIB ON CACHE BOOL "" FORCE)
set(XD3_ARMOR OFF CACHE BOOL "" FORCE)
set(XD3_LZMA_FETCH ON CACHE BOOL "" FORCE)
set(XD3_LIB_LZMA ON CACHE BOOL "" FORCE)
# Off upstream, but older community patches were made with "-S fgk".
set(XD3_SECONDARY_FGK ON CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
add_subdirectory("${XDELTA3_SOURCE_DIR}" xdelta3 EXCLUDE_FROM_ALL)

add_library(xd3shim SHARED xd3shim.c)
target_link_libraries(xd3shim PRIVATE xdelta3::xdelta3)
target_compile_definitions(xd3shim PRIVATE XDS_XD3_VERSION="${XDELTA3_VERSION}")
set_target_properties(xd3shim PROPERTIES C_STANDARD 99 C_VISIBILITY_PRESET hidden)
if(APPLE)
  set_target_properties(xd3shim PROPERTIES INSTALL_NAME_DIR "@rpath")
endif()

cmake_minimum_required(VERSION 3.24)
project(trt_roi_backend LANGUAGES CXX CUDA)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CUDA_STANDARD 17)

# ---- CUDA Toolkit ----
find_package(CUDAToolkit REQUIRED)

# ---- TensorRT (set TensorRT_ROOT or TENSORRT_DIR in environment / cache) ----
# Typical: cmake -DTensorRT_ROOT="C:/TensorRT-10.x" ..
if(NOT TensorRT_ROOT AND DEFINED ENV{TENSORRT_DIR})
    set(TensorRT_ROOT $ENV{TENSORRT_DIR})
endif()

if(TensorRT_ROOT)
    set(TRT_INCLUDE_DIR "${TensorRT_ROOT}/include")
else()
    message(FATAL_ERROR
        "TensorRT_ROOT is not set. Pass -DTensorRT_ROOT=<path> or set TENSORRT_DIR env var.")
endif()

# Windows TensorRT layouts vary: lib, lib/x64, lib/Release.
set(TRT_LIB_SEARCH_PATHS
    "${TensorRT_ROOT}/lib"
    "${TensorRT_ROOT}/lib/x64"
    "${TensorRT_ROOT}/lib/Release"
    "${TensorRT_ROOT}/lib/x64/Release"
)

find_library(
    NVINFER_LIB
    NAMES nvinfer_10 nvinfer
    PATHS ${TRT_LIB_SEARCH_PATHS}
    REQUIRED
)

# GPU arch list: override with -DTRT_CUDA_ARCHITECTURES="86;89" if your CUDA toolkit is too old for sm_120.
if(NOT TRT_CUDA_ARCHITECTURES)
    set(TRT_CUDA_ARCHITECTURES "86;89;100;120")
endif()

# MSVC: single output dir for all single-config and multi-config generators.
set(TRT_PLUGIN_OUT_DIR "${CMAKE_SOURCE_DIR}/../../AGXUnity_Excavator_Assets/Plugins/x86_64")
file(MAKE_DIRECTORY "${TRT_PLUGIN_OUT_DIR}")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${TRT_PLUGIN_OUT_DIR}")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${TRT_PLUGIN_OUT_DIR}")
foreach(_cfg DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${_cfg} "${TRT_PLUGIN_OUT_DIR}")
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${_cfg} "${TRT_PLUGIN_OUT_DIR}")
endforeach()

# ---- Plugin shared library ----
add_library(trt_roi_backend SHARED
    src/trt_roi_api.cpp
    src/trt_roi_engine.cpp
    src/trt_roi_preprocess.cu
)

target_include_directories(trt_roi_backend
    PUBLIC  include
    PRIVATE src
    PRIVATE "${TRT_INCLUDE_DIR}"
)

target_link_libraries(trt_roi_backend
    PRIVATE CUDA::cudart
    PRIVATE CUDA::cuda_driver
    PRIVATE "${NVINFER_LIB}"
    PRIVATE d3d11
)

# Build for the GPU architecture of RTX 5060 (Blackwell sm_120) and common
# earlier architectures so the same DLL works on various dev machines.
set_target_properties(trt_roi_backend PROPERTIES
    CUDA_ARCHITECTURES "${TRT_CUDA_ARCHITECTURES}"
)

set(TRT_RUNTIME_DLLS
    "${TensorRT_ROOT}/bin/nvinfer_10.dll"
    "${CUDAToolkit_BIN_DIR}/cudart64_12.dll"
)

foreach(_runtime_dll IN LISTS TRT_RUNTIME_DLLS)
    if(EXISTS "${_runtime_dll}")
        add_custom_command(
            TARGET trt_roi_backend POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_runtime_dll}" "${TRT_PLUGIN_OUT_DIR}"
        )
    endif()
endforeach()

# ---- Install (optional) ----
install(TARGETS trt_roi_backend RUNTIME DESTINATION "${TRT_PLUGIN_OUT_DIR}")

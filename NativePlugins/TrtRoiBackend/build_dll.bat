@echo off
setlocal

call "C:\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64
set "PATH=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.8\bin;%PATH%"
set "CUDAToolkit_ROOT=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.8"

set "PLUGIN_DIR=%~dp0..\..\AGXUnity_Excavator_Assets\Plugins\x86_64"
set "PLUGIN_DLL=%PLUGIN_DIR%\trt_roi_backend.dll"

if exist build rmdir /s /q build

"C:\Program Files\CMake\bin\cmake.exe" -B build -G "NMake Makefiles" ^
  -DTensorRT_ROOT="C:\TensorRT-10.16.1.11" ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DTRT_CUDA_ARCHITECTURES="86;89" ^
  -DCUDAToolkit_ROOT="C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.8"

if errorlevel 1 (
    echo [ERROR] CMake configure failed
    exit /b 1
)

"C:\Program Files\CMake\bin\cmake.exe" --build build --config Release

if errorlevel 1 (
    echo [ERROR] Build failed
    exit /b 1
)

if exist "%PLUGIN_DLL%" (
    echo [OK] trt_roi_backend.dll deployed to %PLUGIN_DIR%
) else (
    echo [ERROR] Build finished but %PLUGIN_DLL% was not produced.
    exit /b 1
)

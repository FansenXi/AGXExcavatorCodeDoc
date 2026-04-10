#include "trt_roi_api.h"

// C API implementations use C++ (mutex, STL); declarations are extern "C" in header.
#include "trt_roi_engine.h"
#include "trt_roi_preprocess.h"

#include <d3d11.h>
#include <cuda_d3d11_interop.h>
#include <cuda_runtime_api.h>

#include <mutex>
#include <string>
#include <unordered_map>
#include <atomic>

// ---------------------------------------------------------------------------
// Global state
// ---------------------------------------------------------------------------
static std::mutex g_mutex;
static std::unordered_map<int, TrtRoiEngine*> g_engines;
static std::atomic<int> g_next_handle{1};
static thread_local std::string g_last_error;

// Per-handle GPU buffers allocated once on first infer.
struct InferBuffers {
    float* d_input  = nullptr;
    float* d_output = nullptr;
    int    input_elements  = 0;
    int    output_elements = 0;
};
static std::unordered_map<int, InferBuffers> g_buffers;

static void set_error(const std::string& msg) { g_last_error = msg; }

static TrtRoiEngine* get_engine(int handle) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_engines.find(handle);
    return it != g_engines.end() ? it->second : nullptr;
}

// ---------------------------------------------------------------------------
// API Implementation
// ---------------------------------------------------------------------------

int trt_roi_create(const char* engine_path, int* out_handle) {
    if (!engine_path || !out_handle) {
        set_error("null argument");
        return -1;
    }

    auto* eng = new TrtRoiEngine();
    std::string err;
    if (!eng->load(engine_path, err)) {
        set_error(err);
        delete eng;
        return -1;
    }

    int handle = g_next_handle.fetch_add(1);
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_engines[handle] = eng;
    }
    *out_handle = handle;
    return 0;
}

void trt_roi_destroy(int handle) {
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_engines.find(handle);
    if (it != g_engines.end()) {
        delete it->second;
        g_engines.erase(it);
    }
    auto bit = g_buffers.find(handle);
    if (bit != g_buffers.end()) {
        if (bit->second.d_input)  cudaFree(bit->second.d_input);
        if (bit->second.d_output) cudaFree(bit->second.d_output);
        g_buffers.erase(bit);
    }
}

static InferBuffers& ensure_buffers(int handle, TrtRoiEngine* eng) {
    auto& buf = g_buffers[handle];
    int needed_in = eng->input_channels() * eng->input_height() * eng->input_width();
    int needed_out = eng->output_element_count();
    if (buf.input_elements < needed_in) {
        if (buf.d_input) cudaFree(buf.d_input);
        cudaMalloc(reinterpret_cast<void**>(&buf.d_input), needed_in * sizeof(float));
        buf.input_elements = needed_in;
    }
    if (buf.output_elements < needed_out) {
        if (buf.d_output) cudaFree(buf.d_output);
        cudaMalloc(reinterpret_cast<void**>(&buf.d_output), needed_out * sizeof(float));
        buf.output_elements = needed_out;
    }
    return buf;
}

int trt_roi_infer(int handle,
                  void* d3d11_texture_ptr,
                  int tex_width, int tex_height,
                  float* output_buffer,
                  int output_buffer_capacity,
                  int* out_element_count,
                  float* out_infer_ms) {
    std::lock_guard<std::mutex> lock(g_mutex);

    auto eng_it = g_engines.find(handle);
    if (eng_it == g_engines.end()) {
        set_error("invalid handle");
        return -1;
    }
    TrtRoiEngine* eng = eng_it->second;

    if (!d3d11_texture_ptr) {
        set_error("null texture pointer");
        return -1;
    }

    if (eng->output_element_count() > output_buffer_capacity) {
        set_error("output buffer too small");
        return -1;
    }

    // D3D11-CUDA interop: register the texture, map, get cudaArray.
    ID3D11Resource* tex = static_cast<ID3D11Resource*>(d3d11_texture_ptr);
    cudaGraphicsResource_t cuda_res = nullptr;
    cudaError_t err = cudaGraphicsD3D11RegisterResource(
            &cuda_res, tex, cudaGraphicsRegisterFlagsNone);
    if (err != cudaSuccess) {
        set_error(std::string("cudaGraphicsD3D11RegisterResource: ") + cudaGetErrorString(err));
        return -1;
    }

    err = cudaGraphicsMapResources(1, &cuda_res, nullptr);
    if (err != cudaSuccess) {
        set_error(std::string("cudaGraphicsMapResources: ") + cudaGetErrorString(err));
        cudaGraphicsUnregisterResource(cuda_res);
        return -1;
    }

    cudaArray_t mapped_array = nullptr;
    err = cudaGraphicsSubResourceGetMappedArray(&mapped_array, cuda_res, 0, 0);
    if (err != cudaSuccess) {
        set_error(std::string("GetMappedArray: ") + cudaGetErrorString(err));
        cudaGraphicsUnmapResources(1, &cuda_res, nullptr);
        cudaGraphicsUnregisterResource(cuda_res);
        return -1;
    }

    InferBuffers& buf = ensure_buffers(handle, eng);

    // Preprocess: BGRA8 texture -> CHW float32 RGB normalized.
    launch_preprocess_kernel(mapped_array, tex_width, tex_height,
                             buf.d_input,
                             eng->input_width(), eng->input_height(),
                             nullptr);

    // Kernel is async on the default stream; must finish before unmapping the D3D resource.
    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) {
        set_error(std::string("cudaDeviceSynchronize after preprocess: ") + cudaGetErrorString(err));
        cudaGraphicsUnmapResources(1, &cuda_res, nullptr);
        cudaGraphicsUnregisterResource(cuda_res);
        return -1;
    }

    cudaGraphicsUnmapResources(1, &cuda_res, nullptr);
    cudaGraphicsUnregisterResource(cuda_res);

    // Run TensorRT inference.
    float infer_ms = 0.0f;
    std::string infer_err;
    if (!eng->infer(buf.d_input, buf.d_output, infer_ms, infer_err)) {
        set_error(infer_err);
        return -1;
    }

    // Copy output device -> host.
    int count = eng->output_element_count();
    cudaMemcpy(output_buffer, buf.d_output, count * sizeof(float), cudaMemcpyDeviceToHost);

    if (out_element_count) *out_element_count = count;
    if (out_infer_ms) *out_infer_ms = infer_ms;

    return 0;
}

int trt_roi_get_output_size(int handle) {
    auto* eng = get_engine(handle);
    return eng ? eng->output_element_count() : 0;
}

int trt_roi_get_input_width(int handle) {
    auto* eng = get_engine(handle);
    return eng ? eng->input_width() : 0;
}

int trt_roi_get_input_height(int handle) {
    auto* eng = get_engine(handle);
    return eng ? eng->input_height() : 0;
}

const char* trt_roi_get_last_error() {
    return g_last_error.c_str();
}

int trt_roi_warmup(int handle, int iterations) {
    std::lock_guard<std::mutex> lock(g_mutex);

    auto eng_it = g_engines.find(handle);
    if (eng_it == g_engines.end()) {
        set_error("invalid handle");
        return -1;
    }
    TrtRoiEngine* eng = eng_it->second;

    InferBuffers& buf = ensure_buffers(handle, eng);
    cudaMemset(buf.d_input, 0, buf.input_elements * sizeof(float));

    if (iterations < 1)
        iterations = 1;

    for (int i = 0; i < iterations; ++i) {
        float ms = 0.0f;
        std::string err;
        if (!eng->infer(buf.d_input, buf.d_output, ms, err)) {
            set_error(err);
            return -1;
        }
    }
    return 0;
}

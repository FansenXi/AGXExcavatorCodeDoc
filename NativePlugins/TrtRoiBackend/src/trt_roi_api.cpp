#include "trt_roi_api.h"
#include "trt_roi_engine.h"
#include "trt_roi_preprocess.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <d3d11.h>
#include <dxgi.h>
#include <cuda.h>
#include <cudaD3D11.h>
#include <cuda_runtime_api.h>

#include <mutex>
#include <string>
#include <unordered_map>
#include <atomic>
#include <cstring>
#include <cstdio>
#include <cstdarg>
#include <algorithm>
#include <vector>
#include <chrono>
#include <cmath>

#ifdef _WIN32
#include <debugapi.h>
#endif

// ============================================================================
// Minimal Unity Plugin API types (from Unity PluginAPI, MIT license)
// ============================================================================
#ifdef _WIN64
  #define UNITY_INTERFACE_API
#else
  #define UNITY_INTERFACE_API __stdcall
#endif
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)

struct UnityInterfaceGUID {
    unsigned long long m_GUIDHigh;
    unsigned long long m_GUIDLow;
};

struct IUnityInterfaces {
    void* (UNITY_INTERFACE_API* GetInterface)(UnityInterfaceGUID guid);
    void  (UNITY_INTERFACE_API* RegisterInterface)(UnityInterfaceGUID guid, void* ptr);
};

struct IUnityGraphicsD3D11 {
    ID3D11Device* (UNITY_INTERFACE_API* GetDevice)();
};

static const UnityInterfaceGUID kD3D11GUID = {
    0xAAB37EF87A87D748ULL, 0xBF76967F07EFB177ULL
};

// ============================================================================
// File-based logging
// ============================================================================
static FILE* g_log_file = nullptr;
static std::mutex g_log_mutex;

static void log_open()
{
    if (g_log_file) return;
    g_log_file = fopen("trt_roi_debug.log", "w");
    if (g_log_file) {
        fprintf(g_log_file, "=== TRT ROI Backend Debug Log ===\n");
        fflush(g_log_file);
    }
}

static void log_close()
{
    if (g_log_file) { fclose(g_log_file); g_log_file = nullptr; }
}

static void dbg(const char* fmt, ...)
{
    std::lock_guard<std::mutex> lk(g_log_mutex);
    if (!g_log_file) log_open();
    if (!g_log_file) return;
    va_list ap;
    va_start(ap, fmt);
    vfprintf(g_log_file, fmt, ap);
    va_end(ap);
    fflush(g_log_file);
#ifdef _WIN32
    char buf[512];
    va_start(ap, fmt);
    vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    OutputDebugStringA(buf);
#endif
}

// ============================================================================
// Per-handle state
// ============================================================================
struct InferBuffers {
    float*         d_input  = nullptr;
    float*         d_output = nullptr;
    int            input_elements  = 0;
    int            output_elements = 0;
    unsigned char* d_rgba          = nullptr;
    int            d_rgba_capacity = 0;
};

struct PostprocessConfig {
    float confidence_threshold = 0.35f;
    float nms_iou_threshold = 0.5f;
    int   max_class_count = 4;
    int   max_detections = 128;
};

struct InferStats {
    int raw_candidates = 0;
    int threshold_kept = 0;
    int nms_kept = 0;
    float postprocess_ms = 0.0f;
};

struct HandleState {
    TrtRoiEngine*  engine = nullptr;
    InferBuffers   buffers;
    PostprocessConfig config;
    InferStats     stats;
    std::vector<float> host_output;
};

// ============================================================================
// Globals
// ============================================================================
static CUcontext  g_cu_ctx    = nullptr;
static CUdevice   g_cu_device = -1;

static std::mutex                              g_mutex;
static std::unordered_map<int, HandleState*>   g_handles;
static std::atomic<int>                        g_next_handle{1};
static thread_local std::string                g_last_error;

static void set_error(const std::string& msg) { g_last_error = msg; }

static const char* cu_err_str(CUresult r)
{
    const char* str = nullptr;
    cuGetErrorString(r, &str);
    return str ? str : "unknown";
}

static bool push_ctx()
{
    if (!g_cu_ctx) return false;
    CUresult r = cuCtxPushCurrent(g_cu_ctx);
    return r == CUDA_SUCCESS;
}

static void pop_ctx()
{
    CUcontext dummy = nullptr;
    cuCtxPopCurrent(&dummy);
}

// ============================================================================
// Unity Plugin Lifecycle
// ============================================================================
extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces* interfaces)
{
    log_open();
    dbg("[TRT] UnityPluginLoad called\n");

    if (!interfaces) { dbg("[TRT] FAIL: interfaces=null\n"); return; }

    auto* d3d11 = static_cast<IUnityGraphicsD3D11*>(
        interfaces->GetInterface(kD3D11GUID));
    if (!d3d11) { dbg("[TRT] FAIL: GetInterface(D3D11)=null\n"); return; }

    auto* device = d3d11->GetDevice();
    if (!device) { dbg("[TRT] FAIL: GetDevice()=null\n"); return; }
    dbg("[TRT] D3D11 device=%p\n", static_cast<void*>(device));

    IDXGIDevice* dxgiDev = nullptr;
    HRESULT hr = device->QueryInterface(
        __uuidof(IDXGIDevice), reinterpret_cast<void**>(&dxgiDev));
    if (FAILED(hr) || !dxgiDev) { dbg("[TRT] FAIL: IDXGIDevice QI hr=0x%08lx\n", (unsigned long)hr); return; }

    IDXGIAdapter* adapter = nullptr;
    hr = dxgiDev->GetAdapter(&adapter);
    dxgiDev->Release();
    if (FAILED(hr) || !adapter) { dbg("[TRT] FAIL: GetAdapter hr=0x%08lx\n", (unsigned long)hr); return; }

    DXGI_ADAPTER_DESC adapterDesc;
    if (SUCCEEDED(adapter->GetDesc(&adapterDesc))) {
        char adapterName[256];
        wcstombs(adapterName, adapterDesc.Description, sizeof(adapterName));
        dbg("[TRT] Adapter: %s\n", adapterName);
    }

    CUresult cr = cuInit(0);
    dbg("[TRT] cuInit = %d (%s)\n", static_cast<int>(cr), cu_err_str(cr));
    if (cr != CUDA_SUCCESS) { adapter->Release(); return; }

    cr = cuD3D11GetDevice(&g_cu_device, adapter);
    adapter->Release();
    dbg("[TRT] cuD3D11GetDevice = %d, device=%d\n",
        static_cast<int>(cr), static_cast<int>(g_cu_device));
    if (cr != CUDA_SUCCESS) return;

    char devName[256] = {};
    cuDeviceGetName(devName, sizeof(devName), g_cu_device);
    dbg("[TRT] CUDA device: %s\n", devName);

    cr = cuCtxCreate(&g_cu_ctx, CU_CTX_SCHED_AUTO, g_cu_device);
    dbg("[TRT] cuCtxCreate = %d, ctx=%p\n",
        static_cast<int>(cr), static_cast<void*>(g_cu_ctx));
    if (cr != CUDA_SUCCESS || !g_cu_ctx) return;

    pop_ctx();
    dbg("[TRT] Init complete\n");
}

extern "C" UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API
UnityPluginUnload()
{
    dbg("[TRT] UnityPluginUnload called\n");
    std::lock_guard<std::mutex> lock(g_mutex);

    if (g_cu_ctx) {
        cuCtxPushCurrent(g_cu_ctx);

        for (auto& kv : g_handles) {
            auto* s = kv.second;
            if (!s) continue;
            if (s->buffers.d_input)  cudaFree(s->buffers.d_input);
            if (s->buffers.d_output) cudaFree(s->buffers.d_output);
            if (s->buffers.d_rgba)   cudaFree(s->buffers.d_rgba);
            delete s->engine;
            delete s;
        }
        g_handles.clear();

        CUcontext dummy;
        cuCtxPopCurrent(&dummy);
        cuCtxDestroy(g_cu_ctx);
        g_cu_ctx = nullptr;
    }

    log_close();
}

// ============================================================================
// Internal helpers
// ============================================================================
static void ensure_buffers(HandleState* s)
{
    auto* eng = s->engine;
    auto& buf = s->buffers;
    int needed_in  = eng->input_channels() * eng->input_height() * eng->input_width();
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
}

struct DetectionCandidate {
    float x1 = 0.0f;
    float y1 = 0.0f;
    float x2 = 0.0f;
    float y2 = 0.0f;
    float score = 0.0f;
    int class_id = -1;
};

static float read_output_value(const std::vector<float>& data,
                               int candidate_count,
                               int feature_size,
                               int candidate_index,
                               int feature_index,
                               bool feature_major)
{
    return feature_major ?
        data[feature_index * candidate_count + candidate_index] :
        data[candidate_index * feature_size + feature_index];
}

static bool decode_candidate(const std::vector<float>& raw_output,
                             const TrtRoiEngine* engine,
                             const PostprocessConfig& config,
                             int candidate_index,
                             DetectionCandidate& out_candidate)
{
    const int feature_size = engine->output_feature_size();
    const int candidate_count = engine->output_candidate_count();
    const bool feature_major = engine->output_feature_major();

    if (feature_size < 5 || candidate_count <= 0)
        return false;

    const float cx = read_output_value(raw_output, candidate_count, feature_size, candidate_index, 0, feature_major);
    const float cy = read_output_value(raw_output, candidate_count, feature_size, candidate_index, 1, feature_major);
    const float w = read_output_value(raw_output, candidate_count, feature_size, candidate_index, 2, feature_major);
    const float h = read_output_value(raw_output, candidate_count, feature_size, candidate_index, 3, feature_major);
    if (w <= 0.0f || h <= 0.0f)
        return false;

    const int class_count = std::max(0, feature_size - 4);
    if (class_count <= 0)
        return false;

    float best_class_score = 0.0f;
    int best_class_index = -1;
    for (int class_index = 0; class_index < class_count; ++class_index) {
        const float class_score = std::max(
            0.0f,
            read_output_value(raw_output, candidate_count, feature_size, candidate_index, 4 + class_index, feature_major));
        if (class_score > best_class_score) {
            best_class_score = class_score;
            best_class_index = class_index;
        }
    }

    if (best_class_index < 0 ||
        best_class_score < config.confidence_threshold ||
        best_class_index >= config.max_class_count) {
        return false;
    }

    const float input_w = static_cast<float>(std::max(1, engine->input_width()));
    const float input_h = static_cast<float>(std::max(1, engine->input_height()));
    const bool looks_like_pixels = std::max({std::fabs(cx), std::fabs(cy), std::fabs(w), std::fabs(h)}) > 1.5f;

    const float norm_cx = looks_like_pixels ? (cx / input_w) : cx;
    const float norm_cy = looks_like_pixels ? (cy / input_h) : cy;
    const float norm_w = looks_like_pixels ? (w / input_w) : w;
    const float norm_h = looks_like_pixels ? (h / input_h) : h;

    const float x1 = std::clamp(norm_cx - 0.5f * norm_w, 0.0f, 1.0f);
    const float y1 = std::clamp(norm_cy - 0.5f * norm_h, 0.0f, 1.0f);
    const float x2 = std::clamp(norm_cx + 0.5f * norm_w, 0.0f, 1.0f);
    const float y2 = std::clamp(norm_cy + 0.5f * norm_h, 0.0f, 1.0f);

    if (x2 <= x1 || y2 <= y1)
        return false;

    out_candidate.x1 = x1;
    out_candidate.y1 = y1;
    out_candidate.x2 = x2;
    out_candidate.y2 = y2;
    out_candidate.score = best_class_score;
    out_candidate.class_id = best_class_index;
    return true;
}

static float compute_iou(const DetectionCandidate& left, const DetectionCandidate& right)
{
    const float inter_x1 = std::max(left.x1, right.x1);
    const float inter_y1 = std::max(left.y1, right.y1);
    const float inter_x2 = std::min(left.x2, right.x2);
    const float inter_y2 = std::min(left.y2, right.y2);

    const float inter_w = std::max(0.0f, inter_x2 - inter_x1);
    const float inter_h = std::max(0.0f, inter_y2 - inter_y1);
    const float inter_area = inter_w * inter_h;
    if (inter_area <= 0.0f)
        return 0.0f;

    const float left_area = std::max(0.0f, left.x2 - left.x1) * std::max(0.0f, left.y2 - left.y1);
    const float right_area = std::max(0.0f, right.x2 - right.x1) * std::max(0.0f, right.y2 - right.y1);
    const float denom = left_area + right_area - inter_area;
    return denom > 0.0f ? inter_area / denom : 0.0f;
}

static int postprocess_detections(HandleState* state, float* output_buffer, int output_buffer_capacity)
{
    auto* engine = state->engine;
    auto& config = state->config;
    auto& stats = state->stats;
    stats = {};

    const int candidate_count = engine->output_candidate_count();
    stats.raw_candidates = candidate_count;

    std::vector<DetectionCandidate> threshold_kept;
    threshold_kept.reserve(candidate_count);

    DetectionCandidate candidate;
    for (int candidate_index = 0; candidate_index < candidate_count; ++candidate_index) {
        if (decode_candidate(state->host_output, engine, config, candidate_index, candidate))
            threshold_kept.push_back(candidate);
    }
    stats.threshold_kept = static_cast<int>(threshold_kept.size());

    std::sort(threshold_kept.begin(), threshold_kept.end(),
        [](const DetectionCandidate& left, const DetectionCandidate& right) {
            return left.score > right.score;
        });

    std::vector<DetectionCandidate> nms_kept;
    nms_kept.reserve(std::min<int>(config.max_detections, static_cast<int>(threshold_kept.size())));
    for (const auto& current : threshold_kept) {
        bool keep = true;
        for (const auto& accepted : nms_kept) {
            if (accepted.class_id != current.class_id)
                continue;
            if (compute_iou(accepted, current) >= config.nms_iou_threshold) {
                keep = false;
                break;
            }
        }

        if (!keep)
            continue;

        nms_kept.push_back(current);
        if (static_cast<int>(nms_kept.size()) >= config.max_detections)
            break;
    }

    stats.nms_kept = static_cast<int>(nms_kept.size());

    const int max_output_detections = std::max(0, output_buffer_capacity / 6);
    const int write_count = std::min(max_output_detections, static_cast<int>(nms_kept.size()));
    for (int index = 0; index < write_count; ++index) {
        const auto& detection = nms_kept[index];
        const int offset = index * 6;
        output_buffer[offset + 0] = detection.x1;
        output_buffer[offset + 1] = detection.y1;
        output_buffer[offset + 2] = detection.x2;
        output_buffer[offset + 3] = detection.y2;
        output_buffer[offset + 4] = detection.score;
        output_buffer[offset + 5] = static_cast<float>(detection.class_id);
    }

    return write_count;
}

// ============================================================================
// API: Create / Destroy
// ============================================================================
int trt_roi_create(const char* engine_path,
                   float confidence_threshold,
                   float nms_iou_threshold,
                   int max_class_count,
                   int max_detections,
                   int* out_handle)
{
    if (!engine_path || !out_handle) {
        set_error("null argument");
        return -1;
    }

    if (!push_ctx()) {
        set_error("cuda_context_not_available");
        return -1;
    }

    auto* eng = new TrtRoiEngine();
    std::string err;
    if (!eng->load(engine_path, err)) {
        set_error(err);
        delete eng;
        pop_ctx();
        return -1;
    }

    dbg("[TRT] Engine loaded: input=%dx%d, output_elements=%d, feature_size=%d, candidates=%d, feature_major=%d\n",
        eng->input_width(), eng->input_height(), eng->output_element_count(),
        eng->output_feature_size(), eng->output_candidate_count(), eng->output_feature_major() ? 1 : 0);

    auto* state   = new HandleState();
    state->engine = eng;
    state->config.confidence_threshold = confidence_threshold;
    state->config.nms_iou_threshold = nms_iou_threshold;
    state->config.max_class_count = std::max(1, max_class_count);
    state->config.max_detections = std::max(1, max_detections);
    state->host_output.resize(static_cast<size_t>(eng->output_element_count()));

    int handle = g_next_handle.fetch_add(1);
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        g_handles[handle] = state;
    }
    *out_handle = handle;
    pop_ctx();
    return 0;
}

void trt_roi_destroy(int handle)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_handles.find(handle);
    if (it == g_handles.end()) return;

    auto* s = it->second;
    if (s) {
        if (g_cu_ctx) cuCtxPushCurrent(g_cu_ctx);

        if (s->buffers.d_input)  cudaFree(s->buffers.d_input);
        if (s->buffers.d_output) cudaFree(s->buffers.d_output);
        if (s->buffers.d_rgba)   cudaFree(s->buffers.d_rgba);
        delete s->engine;
        delete s;

        if (g_cu_ctx) { CUcontext d; cuCtxPopCurrent(&d); }
    }
    g_handles.erase(it);
}

// ============================================================================
// API: Query helpers
// ============================================================================
int trt_roi_get_output_size(int handle)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_handles.find(handle);
    if (it == g_handles.end()) return 0;
    return it->second->engine ? it->second->config.max_detections * 6 : 0;
}

int trt_roi_get_input_width(int handle)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_handles.find(handle);
    if (it == g_handles.end()) return 0;
    return it->second->engine ? it->second->engine->input_width() : 0;
}

int trt_roi_get_input_height(int handle)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_handles.find(handle);
    if (it == g_handles.end()) return 0;
    return it->second->engine ? it->second->engine->input_height() : 0;
}

const char* trt_roi_get_last_error(void)
{
    return g_last_error.c_str();
}

int trt_roi_get_last_stats(int handle,
                           int* out_raw_candidates,
                           int* out_threshold_kept,
                           int* out_nms_kept,
                           float* out_postprocess_ms)
{
    std::lock_guard<std::mutex> lock(g_mutex);
    auto it = g_handles.find(handle);
    if (it == g_handles.end())
        return -1;

    const auto& stats = it->second->stats;
    if (out_raw_candidates) *out_raw_candidates = stats.raw_candidates;
    if (out_threshold_kept) *out_threshold_kept = stats.threshold_kept;
    if (out_nms_kept) *out_nms_kept = stats.nms_kept;
    if (out_postprocess_ms) *out_postprocess_ms = stats.postprocess_ms;
    return 0;
}

// ============================================================================
// API: Warmup
// ============================================================================
int trt_roi_warmup(int handle, int iterations)
{
    HandleState* state = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_handles.find(handle);
        if (it == g_handles.end()) {
            set_error("invalid handle");
            return -1;
        }
        state = it->second;
    }

    if (!state->engine) {
        set_error("engine not loaded");
        return -1;
    }

    if (!push_ctx()) {
        set_error("cuda_context_not_available");
        return -1;
    }

    ensure_buffers(state);
    cudaMemset(state->buffers.d_input, 0,
               state->buffers.input_elements * sizeof(float));

    if (iterations < 1) iterations = 1;

    for (int i = 0; i < iterations; ++i) {
        float       ms = 0.0f;
        std::string err;
        if (!state->engine->infer(state->buffers.d_input,
                                  state->buffers.d_output, ms, err)) {
            set_error(err);
            pop_ctx();
            return -1;
        }
    }

    dbg("[TRT] Warmup done, %d iterations\n", iterations);
    pop_ctx();
    return 0;
}

// ============================================================================
// API: Synchronous inference from CPU RGBA pixels
// ============================================================================
int trt_roi_infer_cpu(int handle,
                      const void* rgba_pixels,
                      int width, int height,
                      float* output_buffer,
                      int output_buffer_capacity,
                      int* out_element_count,
                      float* out_infer_ms)
{
    if (!rgba_pixels || width <= 0 || height <= 0) {
        set_error("invalid pixel data or dimensions");
        return -1;
    }

    HandleState* state = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        auto it = g_handles.find(handle);
        if (it == g_handles.end()) {
            set_error("invalid handle");
            return -1;
        }
        state = it->second;
    }

    if (!state->engine) {
        set_error("engine not loaded");
        return -1;
    }

    if (!push_ctx()) {
        set_error("cuda_context_not_available");
        return -1;
    }

    ensure_buffers(state);

    int pixel_bytes = width * height * 4;
    if (state->buffers.d_rgba_capacity < pixel_bytes) {
        if (state->buffers.d_rgba) cudaFree(state->buffers.d_rgba);
        cudaMalloc(reinterpret_cast<void**>(&state->buffers.d_rgba), pixel_bytes);
        state->buffers.d_rgba_capacity = pixel_bytes;
    }

    cudaError_t err = cudaMemcpyAsync(state->buffers.d_rgba, rgba_pixels,
                                       pixel_bytes, cudaMemcpyHostToDevice, nullptr);
    if (err != cudaSuccess) {
        set_error(std::string("cudaMemcpyAsync H2D: ") + cudaGetErrorString(err));
        pop_ctx();
        return -1;
    }

    auto* eng = state->engine;
    launch_preprocess_rgba_linear(state->buffers.d_rgba,
                                  width, height,
                                  state->buffers.d_input,
                                  eng->input_width(), eng->input_height(),
                                  nullptr);

    err = cudaDeviceSynchronize();
    if (err != cudaSuccess) {
        set_error(std::string("preprocess sync: ") + cudaGetErrorString(err));
        pop_ctx();
        return -1;
    }

    float infer_ms = 0.0f;
    std::string infer_err;
    if (!eng->infer(state->buffers.d_input, state->buffers.d_output, infer_ms, infer_err)) {
        set_error(infer_err);
        pop_ctx();
        return -1;
    }

    const int raw_element_count = eng->output_element_count();
    if (static_cast<int>(state->host_output.size()) < raw_element_count)
        state->host_output.resize(static_cast<size_t>(raw_element_count));

    if (raw_element_count > 0) {
        err = cudaMemcpy(state->host_output.data(), state->buffers.d_output,
                         raw_element_count * sizeof(float), cudaMemcpyDeviceToHost);
        if (err != cudaSuccess) {
            set_error(std::string("cudaMemcpy D2H: ") + cudaGetErrorString(err));
            pop_ctx();
            return -1;
        }
    }

    const auto postprocess_start = std::chrono::high_resolution_clock::now();
    const int detection_count = output_buffer && output_buffer_capacity >= 6 ?
        postprocess_detections(state, output_buffer, output_buffer_capacity) :
        0;
    const auto postprocess_end = std::chrono::high_resolution_clock::now();
    state->stats.postprocess_ms = static_cast<float>(
        std::chrono::duration<double, std::milli>(postprocess_end - postprocess_start).count());

    dbg("[TRT] infer_cpu: raw=%d threshold=%d nms=%d final=%d infer_ms=%.3f post_ms=%.3f\n",
        state->stats.raw_candidates,
        state->stats.threshold_kept,
        state->stats.nms_kept,
        detection_count,
        infer_ms,
        state->stats.postprocess_ms);

    if (out_element_count) *out_element_count = detection_count * 6;
    if (out_infer_ms)      *out_infer_ms      = infer_ms;

    pop_ctx();
    return 0;
}

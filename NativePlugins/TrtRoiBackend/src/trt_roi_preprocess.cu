#include "trt_roi_preprocess.h"

// CUDA texture object for bilinear sampling the source BGRA8 array.
// We create / destroy the texture per-call for simplicity; caching can be
// added later if profiling shows measurable overhead.

__global__ void preprocess_bgra_to_chw_rgb(
        cudaTextureObject_t tex,
        int src_w, int src_h,
        float* __restrict__ dst,
        int dst_w, int dst_h) {
    const int x = blockIdx.x * blockDim.x + threadIdx.x;
    const int y = blockIdx.y * blockDim.y + threadIdx.y;
    if (x >= dst_w || y >= dst_h) return;

    // Bilinear sample coordinates in source space (0.5-pixel center offset).
    const float u = (static_cast<float>(x) + 0.5f) * src_w / dst_w;
    const float v = (static_cast<float>(y) + 0.5f) * src_h / dst_h;

    // tex2D on a uchar4 texture returns float4 in [0,255].
    float4 px = tex2D<float4>(tex, u, v);

    // Unity ARGB32 is BGRA in GPU memory: px = {B, G, R, A}.
    const float r = px.z / 255.0f;
    const float g = px.y / 255.0f;
    const float b = px.x / 255.0f;

    const int plane_size = dst_w * dst_h;
    const int idx = y * dst_w + x;
    dst[0 * plane_size + idx] = r;
    dst[1 * plane_size + idx] = g;
    dst[2 * plane_size + idx] = b;
}

void launch_preprocess_kernel(cudaArray_t src_array,
                              int src_w, int src_h,
                              float* d_output,
                              int dst_w, int dst_h,
                              cudaStream_t stream) {
    // Create a CUDA texture object bound to the source array.
    cudaResourceDesc res_desc = {};
    res_desc.resType = cudaResourceTypeArray;
    res_desc.res.array.array = src_array;

    cudaTextureDesc tex_desc = {};
    tex_desc.addressMode[0] = cudaAddressModeClamp;
    tex_desc.addressMode[1] = cudaAddressModeClamp;
    tex_desc.filterMode = cudaFilterModeLinear;
    tex_desc.readMode = cudaReadModeElementType;
    tex_desc.normalizedCoords = 0;

    cudaTextureObject_t tex_obj = 0;
    cudaCreateTextureObject(&tex_obj, &res_desc, &tex_desc, nullptr);

    dim3 block(16, 16);
    dim3 grid((dst_w + block.x - 1) / block.x,
              (dst_h + block.y - 1) / block.y);

    preprocess_bgra_to_chw_rgb<<<grid, block, 0, stream>>>(
            tex_obj, src_w, src_h, d_output, dst_w, dst_h);

    cudaDestroyTextureObject(tex_obj);
}

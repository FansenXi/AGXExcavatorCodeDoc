#ifndef TRT_ROI_PREPROCESS_H
#define TRT_ROI_PREPROCESS_H

#include <cuda_runtime_api.h>

// Resize a BGRA8 texture (via cudaArray) to CHW float32 RGB, normalized to [0,1].
// d_output must have capacity >= 3 * dst_h * dst_w floats.
void launch_preprocess_kernel(cudaArray_t src_array,
                              int src_w, int src_h,
                              float* d_output,
                              int dst_w, int dst_h,
                              cudaStream_t stream);

#endif

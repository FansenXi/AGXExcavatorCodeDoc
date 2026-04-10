#ifndef TRT_ROI_API_H
#define TRT_ROI_API_H

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
  #define TRT_ROI_API __declspec(dllexport)
#else
  #define TRT_ROI_API __attribute__((visibility("default")))
#endif

TRT_ROI_API int  trt_roi_create(const char* engine_path, int* out_handle);
TRT_ROI_API void trt_roi_destroy(int handle);

TRT_ROI_API int  trt_roi_infer(int handle,
                               void* d3d11_texture_ptr,
                               int tex_width, int tex_height,
                               float* output_buffer,
                               int output_buffer_capacity,
                               int* out_element_count,
                               float* out_infer_ms);

TRT_ROI_API int  trt_roi_get_output_size(int handle);
TRT_ROI_API int  trt_roi_get_input_width(int handle);
TRT_ROI_API int  trt_roi_get_input_height(int handle);
TRT_ROI_API const char* trt_roi_get_last_error();
TRT_ROI_API int  trt_roi_warmup(int handle, int iterations);

#ifdef __cplusplus
}
#endif

#endif

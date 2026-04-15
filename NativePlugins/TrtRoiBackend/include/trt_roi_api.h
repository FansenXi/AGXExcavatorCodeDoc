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

/* Engine lifecycle */
TRT_ROI_API int  trt_roi_create(const char* engine_path,
                                float confidence_threshold,
                                float nms_iou_threshold,
                                int max_class_count,
                                int max_detections,
                                int* out_handle);
TRT_ROI_API void trt_roi_destroy(int handle);

/* Synchronous inference from CPU RGBA pixels.
   rgba_pixels: pointer to RGBA8 pixel data (width * height * 4 bytes).
   Returns 0 on success, -1 on error (check trt_roi_get_last_error). */
TRT_ROI_API int  trt_roi_infer_cpu(int handle,
                                   const void* rgba_pixels,
                                   int width, int height,
                                   float* output_buffer,
                                   int output_buffer_capacity,
                                   int* out_element_count,
                                   float* out_infer_ms);

/* Query helpers */
TRT_ROI_API int  trt_roi_get_output_size(int handle);
TRT_ROI_API int  trt_roi_get_input_width(int handle);
TRT_ROI_API int  trt_roi_get_input_height(int handle);
TRT_ROI_API const char* trt_roi_get_last_error(void);
TRT_ROI_API int  trt_roi_get_last_stats(int handle,
                                        int* out_raw_candidates,
                                        int* out_threshold_kept,
                                        int* out_nms_kept,
                                        float* out_postprocess_ms);

/* Warmup: runs inference N times with zeroed input */
TRT_ROI_API int  trt_roi_warmup(int handle, int iterations);

#ifdef __cplusplus
}
#endif

#endif

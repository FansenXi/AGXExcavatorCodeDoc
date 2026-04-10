#ifndef TRT_ROI_ENGINE_H
#define TRT_ROI_ENGINE_H

#include <string>
#include <vector>
#include <memory>

#include <NvInfer.h>
#include <cuda_runtime_api.h>

class TrtRoiEngine {
public:
    TrtRoiEngine();
    ~TrtRoiEngine();

    TrtRoiEngine(const TrtRoiEngine&) = delete;
    TrtRoiEngine& operator=(const TrtRoiEngine&) = delete;

    bool load(const std::string& engine_path, std::string& error);
    void release();

    bool infer(float* d_input, float* d_output, float& infer_ms, std::string& error);

    int input_width() const { return input_w_; }
    int input_height() const { return input_h_; }
    int input_channels() const { return input_c_; }
    int output_element_count() const { return output_elements_; }

private:
    class Logger : public nvinfer1::ILogger {
    public:
        void log(Severity severity, const char* msg) noexcept override;
    };

    Logger logger_;
    std::unique_ptr<nvinfer1::IRuntime> runtime_;
    std::unique_ptr<nvinfer1::ICudaEngine> engine_;
    std::unique_ptr<nvinfer1::IExecutionContext> context_;

    cudaStream_t stream_ = nullptr;
    cudaEvent_t evt_start_ = nullptr;
    cudaEvent_t evt_end_ = nullptr;

    int input_c_ = 3;
    int input_h_ = 0;
    int input_w_ = 0;
    int output_elements_ = 0;

    int input_binding_ = -1;
    int output_binding_ = -1;
};

#endif

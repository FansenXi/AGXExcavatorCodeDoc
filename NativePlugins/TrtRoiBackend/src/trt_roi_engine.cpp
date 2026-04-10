#include "trt_roi_engine.h"

#include <fstream>
#include <cstring>

void TrtRoiEngine::Logger::log(Severity severity, const char* msg) noexcept {
    if (severity <= Severity::kWARNING) {
        // Could forward to Unity Debug.Log via a callback; for now ignore.
    }
}

TrtRoiEngine::TrtRoiEngine() = default;

TrtRoiEngine::~TrtRoiEngine() { release(); }

bool TrtRoiEngine::load(const std::string& engine_path, std::string& error) {
    release();

    std::ifstream file(engine_path, std::ios::binary | std::ios::ate);
    if (!file.is_open()) {
        error = "cannot open engine file: " + engine_path;
        return false;
    }

    const auto size = file.tellg();
    file.seekg(0, std::ios::beg);
    std::vector<char> blob(static_cast<size_t>(size));
    if (!file.read(blob.data(), size)) {
        error = "failed to read engine file";
        return false;
    }

    runtime_.reset(nvinfer1::createInferRuntime(logger_));
    if (!runtime_) {
        error = "createInferRuntime failed";
        return false;
    }

    engine_.reset(runtime_->deserializeCudaEngine(blob.data(), blob.size()));
    if (!engine_) {
        error = "deserializeCudaEngine failed";
        return false;
    }

    context_.reset(engine_->createExecutionContext());
    if (!context_) {
        error = "createExecutionContext failed";
        return false;
    }

    // Resolve input / output bindings (TensorRT 10.x uses name-based API).
    const int nb = engine_->getNbIOTensors();
    for (int i = 0; i < nb; ++i) {
        const char* name = engine_->getIOTensorName(i);
        if (engine_->getTensorIOMode(name) == nvinfer1::TensorIOMode::kINPUT) {
            input_binding_ = i;
            auto dims = engine_->getTensorShape(name);
            // Expect NCHW.
            input_c_ = dims.d[1];
            input_h_ = dims.d[2];
            input_w_ = dims.d[3];
        } else {
            output_binding_ = i;
            auto dims = engine_->getTensorShape(name);
            output_elements_ = 1;
            for (int d = 0; d < dims.nbDims; ++d)
                output_elements_ *= dims.d[d];
        }
    }

    if (input_binding_ < 0 || output_binding_ < 0 || input_w_ <= 0 || input_h_ <= 0) {
        error = "engine has unexpected IO layout";
        release();
        return false;
    }

    cudaStreamCreate(&stream_);
    cudaEventCreate(&evt_start_);
    cudaEventCreate(&evt_end_);

    return true;
}

void TrtRoiEngine::release() {
    context_.reset();
    engine_.reset();
    runtime_.reset();

    if (stream_) { cudaStreamDestroy(stream_); stream_ = nullptr; }
    if (evt_start_) { cudaEventDestroy(evt_start_); evt_start_ = nullptr; }
    if (evt_end_) { cudaEventDestroy(evt_end_); evt_end_ = nullptr; }

    input_binding_ = -1;
    output_binding_ = -1;
    input_w_ = 0;
    input_h_ = 0;
    output_elements_ = 0;
}

bool TrtRoiEngine::infer(float* d_input, float* d_output, float& infer_ms, std::string& error) {
    if (!context_) {
        error = "engine not loaded";
        return false;
    }

    const char* input_name = engine_->getIOTensorName(input_binding_);
    const char* output_name = engine_->getIOTensorName(output_binding_);
    context_->setTensorAddress(input_name, d_input);
    context_->setTensorAddress(output_name, d_output);

    cudaEventRecord(evt_start_, stream_);
    if (!context_->enqueueV3(stream_)) {
        error = "enqueueV3 failed";
        return false;
    }
    cudaEventRecord(evt_end_, stream_);
    cudaStreamSynchronize(stream_);

    cudaEventElapsedTime(&infer_ms, evt_start_, evt_end_);
    return true;
}

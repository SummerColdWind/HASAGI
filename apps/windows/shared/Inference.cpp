#include "vendor/ort/onnxruntime_cxx_api.h"
#include <algorithm>
#include <cstdio>
#include <memory>
#include <stdexcept>

namespace {
struct Model {
    Ort::Env env{ORT_LOGGING_LEVEL_WARNING, "HASAGI"};
    Ort::Session session{nullptr};
    Ort::MemoryInfo memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);

    explicit Model(const wchar_t* path) {
        Ort::SessionOptions options;
        options.SetIntraOpNumThreads(4);
        options.SetInterOpNumThreads(1);
        session = Ort::Session(env, path, options);
    }
};
}

extern "C" __declspec(dllexport) Model* OpenModel(const wchar_t* path, char* error, size_t size) {
    try { return new Model(path); }
    catch (const std::exception& ex) { snprintf(error, size, "%s", ex.what()); return nullptr; }
}

extern "C" __declspec(dllexport) int Predict(Model* model, float* pixels, float* logits, char* error, size_t size) {
    try {
        const int64_t shape[] = {1, 3, 384, 384};
        auto input = Ort::Value::CreateTensor<float>(model->memory, pixels, 3 * 384 * 384, shape, 4);
        const char* inputs[] = {"images"};
        const char* outputs[] = {"logits"};
        auto result = model->session.Run(Ort::RunOptions{nullptr}, inputs, &input, 1, outputs, 1);
        if (result[0].GetTensorTypeAndShapeInfo().GetElementCount() != 4)
            throw std::runtime_error("Expected four logits.");
        std::copy_n(result[0].GetTensorData<float>(), 4, logits);
        return 0;
    } catch (const std::exception& ex) { snprintf(error, size, "%s", ex.what()); return -1; }
}

extern "C" __declspec(dllexport) void CloseModel(Model* model) { delete model; }

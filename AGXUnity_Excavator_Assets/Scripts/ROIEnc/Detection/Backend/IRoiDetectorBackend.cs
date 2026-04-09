using System;
using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend
{
  [Serializable]
  public sealed class RoiBackendExecutionRequest
  {
    public bool FlipY = true;
    public Vector4 Scale = Vector4.one;
    public Vector4 Bias = Vector4.zero;
    public int Channels = 3;
  }

  [Serializable]
  public sealed class RoiBackendTensor
  {
    public string Name = string.Empty;
    public int[] Shape = Array.Empty<int>();
    public float[] Data = Array.Empty<float>();
  }

  public interface IRoiDetectorBackend : IDisposable
  {
    string BackendName { get; }
    bool IsReady { get; }
    bool TryInitialize( UnityEngine.Object modelAsset, RoiEncConfiguration.DetectionOptions options, out string error );
    bool TryExecute( Texture inputTexture, RoiBackendExecutionRequest request, List<RoiBackendTensor> outputs, out string error );
  }
}

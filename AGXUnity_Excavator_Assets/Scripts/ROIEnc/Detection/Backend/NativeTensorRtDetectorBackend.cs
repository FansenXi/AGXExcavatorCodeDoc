using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend
{
  public sealed class NativeTensorRtDetectorBackend : IRoiDetectorBackend
  {
    public string BackendName => "ExternalOverlaySidecar";
    public bool IsReady => false;

    public bool TryInitialize( UnityEngine.Object modelAsset,
                               RoiEncConfiguration.DetectionOptions options,
                               out string error )
    {
      error = "runtime_inference_has_moved_to_external_roi_overlay_sidecar";
      return false;
    }

    public bool TryExecute( Texture inputTexture,
                            RoiBackendExecutionRequest request,
                            List<RoiBackendTensor> outputs,
                            out string error )
    {
      if ( outputs != null )
        outputs.Clear();

      error = "runtime_inference_has_moved_to_external_roi_overlay_sidecar";
      return false;
    }

    public void Dispose()
    {
    }
  }
}

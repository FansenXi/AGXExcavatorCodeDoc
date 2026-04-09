using System;
using AGXUnity_Excavator.Scripts.Presentation;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  [Serializable]
  public sealed class RoiFrameSample
  {
    public long frameId = -1;
    public long stepId = -1;
    public long captureTimeNs = -1;
    public int width = 0;
    public int height = 0;
    public RenderTexture sourceTexture = null;
    public byte[] rgb24 = null;
    public Camera sourceCamera = null;
    public TrackedCameraWindow sourceWindow = null;
  }
}

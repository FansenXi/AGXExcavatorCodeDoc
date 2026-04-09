using System;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection
{
  public interface IRoiDetector : IRoiProvider, IDisposable
  {
    string DetectorName { get; }
    bool IsReady { get; }
  }
}

using System.Collections.Generic;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  public interface IRoiProvider
  {
    bool TryGetRois( RoiFrameSample frameSample, List<RoiDescriptor> results, out string error );
  }
}

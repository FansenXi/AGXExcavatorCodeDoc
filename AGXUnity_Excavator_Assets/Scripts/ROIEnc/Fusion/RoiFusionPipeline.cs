using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Fusion
{
  public sealed class RoiFusionPipeline
  {
    private readonly KinematicBucketProjector m_bucketProjector = new KinematicBucketProjector();
    private int m_bucketMissCount = 0;

    public bool LastFrameUsedKinematicFallback { get; private set; } = false;

    public void Fuse( UnityEngine.Camera sourceCamera,
                      ExcavatorMachineController machineController,
                      long frameId,
                      List<RoiDescriptor> sourceDetections,
                      RoiEncConfiguration.FusionOptions options,
                      List<RoiDescriptor> fusedResults )
    {
      fusedResults.Clear();
      LastFrameUsedKinematicFallback = false;
      options = options ?? new RoiEncConfiguration.FusionOptions();

      var hasBucket = false;
      if ( sourceDetections != null ) {
        foreach ( var detection in sourceDetections ) {
          if ( detection == null )
            continue;

          var clone = detection.Clone();
          clone.Source = clone.Source == RoiSource.Unknown ? RoiSource.Fusion : clone.Source;
          fusedResults.Add( clone );
          hasBucket |= clone.Category == RoiCategory.Bucket;
        }
      }

      m_bucketMissCount = hasBucket ? 0 : m_bucketMissCount + 1;
      if ( hasBucket ||
           !options.EnableKinematicBucketFallback ||
           m_bucketMissCount < UnityEngine.Mathf.Max( 1, options.ConsecutiveBucketMissesBeforeFallback ) )
        return;

      if ( m_bucketProjector.TryProject( sourceCamera,
                                         machineController,
                                         options.KinematicFallbackPadding,
                                         options.KinematicFallbackConfidence,
                                         frameId,
                                         out var fallbackDescriptor ) ) {
        fusedResults.Add( fallbackDescriptor );
        LastFrameUsedKinematicFallback = true;
      }
    }
  }
}

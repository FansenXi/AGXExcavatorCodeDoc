using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary
{
  public sealed class KinematicBucketProjector
  {
    private static readonly Vector3[] s_corners = new Vector3[8];

    public bool TryProject( Camera sourceCamera,
                            ExcavatorMachineController machineController,
                            float padding,
                            float confidence,
                            long frameId,
                            out RoiDescriptor descriptor )
    {
      descriptor = null;

      if ( sourceCamera == null || machineController == null || machineController.BucketReference == null )
        return false;

      var bucketReference = machineController.BucketReference;
      if ( !HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( bucketReference, out var localBounds ) )
        return false;

      var rect = ProjectLocalBounds( sourceCamera, bucketReference, localBounds );
      if ( rect.width <= 0.0f || rect.height <= 0.0f )
        return false;

      rect.xMin -= padding;
      rect.yMin -= padding;
      rect.xMax += padding;
      rect.yMax += padding;
      rect = RoiMathUtility.ClampNormalizedRectTopLeft( rect );

      descriptor = new RoiDescriptor
      {
        Category = RoiCategory.Bucket,
        Source = RoiSource.KinematicFallback,
        Confidence = Mathf.Clamp01( confidence ),
        FrameId = frameId,
        Priority = 150,
        NormalizedRect = rect,
        DebugText = "kinematic_bucket_fallback"
      };
      return descriptor.IsValid;
    }

    private static Rect ProjectLocalBounds( Camera camera, Transform reference, Bounds localBounds )
    {
      var validCornerCount = 0;
      var minX = float.PositiveInfinity;
      var minY = float.PositiveInfinity;
      var maxX = float.NegativeInfinity;
      var maxY = float.NegativeInfinity;

      FillLocalBoundsCorners( localBounds, s_corners );
      for ( var cornerIndex = 0; cornerIndex < s_corners.Length; ++cornerIndex ) {
        var worldCorner = reference.TransformPoint( s_corners[ cornerIndex ] );
        var viewport = camera.WorldToViewportPoint( worldCorner );
        if ( viewport.z <= 0.0f )
          continue;

        validCornerCount += 1;
        minX = Mathf.Min( minX, viewport.x );
        minY = Mathf.Min( minY, viewport.y );
        maxX = Mathf.Max( maxX, viewport.x );
        maxY = Mathf.Max( maxY, viewport.y );
      }

      if ( validCornerCount == 0 )
        return default;

      return RoiMathUtility.ClampNormalizedRectTopLeft( new Rect(
        minX,
        1.0f - maxY,
        maxX - minX,
        maxY - minY ) );
    }

    private static void FillLocalBoundsCorners( Bounds bounds, Vector3[] corners )
    {
      var min = bounds.min;
      var max = bounds.max;

      corners[ 0 ] = new Vector3( min.x, min.y, min.z );
      corners[ 1 ] = new Vector3( min.x, min.y, max.z );
      corners[ 2 ] = new Vector3( min.x, max.y, min.z );
      corners[ 3 ] = new Vector3( min.x, max.y, max.z );
      corners[ 4 ] = new Vector3( max.x, min.y, min.z );
      corners[ 5 ] = new Vector3( max.x, min.y, max.z );
      corners[ 6 ] = new Vector3( max.x, max.y, min.z );
      corners[ 7 ] = new Vector3( max.x, max.y, max.z );
    }
  }
}

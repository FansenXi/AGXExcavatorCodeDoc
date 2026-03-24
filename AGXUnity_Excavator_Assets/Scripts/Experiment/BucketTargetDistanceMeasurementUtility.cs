using AGXUnity.Collide;
using UnityEngine;

internal struct OrientedMeasurementBox
{
  public Transform Frame;
  public Vector3 CenterLocal;
  public Vector3 HalfExtents;

  public bool IsValid =>
    Frame != null &&
    HalfExtents.x > 0.0f &&
    HalfExtents.y > 0.0f &&
    HalfExtents.z > 0.0f;

  public Vector3 ClosestPointWorld( Vector3 worldPoint )
  {
    if ( Frame == null )
      return worldPoint;

    var pointLocal = Frame.InverseTransformPoint( worldPoint ) - CenterLocal;
    var clampedLocal = new Vector3(
      Mathf.Clamp( pointLocal.x, -HalfExtents.x, HalfExtents.x ),
      Mathf.Clamp( pointLocal.y, -HalfExtents.y, HalfExtents.y ),
      Mathf.Clamp( pointLocal.z, -HalfExtents.z, HalfExtents.z ) );
    return Frame.TransformPoint( CenterLocal + clampedLocal );
  }

  public Vector3 SamplePointWorld( float normalizedX, float normalizedY, float normalizedZ )
  {
    var localOffset = new Vector3(
      normalizedX * HalfExtents.x,
      normalizedY * HalfExtents.y,
      normalizedZ * HalfExtents.z );
    return Frame.TransformPoint( CenterLocal + localOffset );
  }
}

internal static class BucketTargetDistanceMeasurementUtility
{
  private static readonly float[] SampleCoordinates = { -1.0f, 0.0f, 1.0f };

  public static bool TryMeasureDistance( Transform bucketReference,
                                         TargetMassSensorBase targetSensor,
                                         out float minDistanceMeters )
  {
    minDistanceMeters = -1.0f;
    if ( bucketReference == null || targetSensor == null )
      return false;

    if ( !TryGetMeasurementBox( bucketReference, out var bucketBox ) )
      return false;

    if ( !targetSensor.TryGetMeasurementVolume( out var targetFrame, out var targetCenterLocal, out var targetHalfExtents ) )
      return false;

    var targetBox = new OrientedMeasurementBox
    {
      Frame = targetFrame,
      CenterLocal = targetCenterLocal,
      HalfExtents = targetHalfExtents
    };

    if ( !targetBox.IsValid )
      return false;

    minDistanceMeters = MeasureApproximateDistance( bucketBox, targetBox );
    return true;
  }

  private static bool TryGetMeasurementBox( Transform reference, out OrientedMeasurementBox measurementBox )
  {
    measurementBox = default;
    if ( reference == null )
      return false;

    if ( !TryCalculateLocalCompositeBounds( reference, out var localBounds ) )
      return false;

    var halfExtents = 0.5f * localBounds.size;
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return false;

    measurementBox = new OrientedMeasurementBox
    {
      Frame = reference,
      CenterLocal = localBounds.center,
      HalfExtents = halfExtents
    };
    return true;
  }

  private static float MeasureApproximateDistance( OrientedMeasurementBox left, OrientedMeasurementBox right )
  {
    var minDistanceSq = float.PositiveInfinity;
    SampleBoxAgainstOther( left, right, ref minDistanceSq );
    SampleBoxAgainstOther( right, left, ref minDistanceSq );

    return float.IsPositiveInfinity( minDistanceSq ) ? -1.0f : Mathf.Sqrt( Mathf.Max( 0.0f, minDistanceSq ) );
  }

  private static void SampleBoxAgainstOther( OrientedMeasurementBox source,
                                             OrientedMeasurementBox target,
                                             ref float minDistanceSq )
  {
    for ( var xIndex = 0; xIndex < SampleCoordinates.Length; ++xIndex ) {
      for ( var yIndex = 0; yIndex < SampleCoordinates.Length; ++yIndex ) {
        for ( var zIndex = 0; zIndex < SampleCoordinates.Length; ++zIndex ) {
          var samplePointWorld = source.SamplePointWorld( SampleCoordinates[xIndex],
                                                          SampleCoordinates[yIndex],
                                                          SampleCoordinates[zIndex] );
          var closestPointWorld = target.ClosestPointWorld( samplePointWorld );
          var distanceSq = ( samplePointWorld - closestPointWorld ).sqrMagnitude;
          if ( distanceSq < minDistanceSq )
            minDistanceSq = distanceSq;
        }
      }
    }
  }

  private static bool TryCalculateLocalCompositeBounds( Transform reference, out Bounds localBounds )
  {
    localBounds = default;
    var hasBounds = false;

    if ( TryCalculateLocalBoxBounds( reference, out var boxBounds ) ) {
      localBounds = boxBounds;
      hasBounds = true;
    }

    if ( HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( reference, out var rendererBounds ) ) {
      if ( !hasBounds ) {
        localBounds = rendererBounds;
        hasBounds = true;
      }
      else {
        localBounds.Encapsulate( rendererBounds.min );
        localBounds.Encapsulate( rendererBounds.max );
      }
    }

    return hasBounds;
  }

  private static bool TryCalculateLocalBoxBounds( Transform reference, out Bounds localBounds )
  {
    localBounds = default;
    if ( reference == null )
      return false;

    var boxes = reference.GetComponentsInChildren<Box>( true );
    var hasBounds = false;

    foreach ( var box in boxes ) {
      if ( box == null )
        continue;

      var halfExtents = box.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      var localMin = Vector3.positiveInfinity;
      var localMax = Vector3.negativeInfinity;
      for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
        for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
          for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
            var localCorner = new Vector3( xSign * halfExtents.x, ySign * halfExtents.y, zSign * halfExtents.z );
            var worldCorner = box.transform.TransformPoint( localCorner );
            var referenceCorner = reference.InverseTransformPoint( worldCorner );
            localMin = Vector3.Min( localMin, referenceCorner );
            localMax = Vector3.Max( localMax, referenceCorner );
          }
        }
      }

      if ( !hasBounds ) {
        localBounds = new Bounds( 0.5f * ( localMin + localMax ), localMax - localMin );
        hasBounds = true;
      }
      else {
        localBounds.Encapsulate( localMin );
        localBounds.Encapsulate( localMax );
      }
    }

    return hasBounds;
  }
}

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

  public Vector3 CornerWorld( int xSign, int ySign, int zSign )
  {
    return Frame.TransformPoint(
      CenterLocal + new Vector3(
        xSign * HalfExtents.x,
        ySign * HalfExtents.y,
        zSign * HalfExtents.z ) );
  }

  public float GetWorldMinY()
  {
    var minWorldY = float.PositiveInfinity;
    for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
      for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
        for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
          var worldCorner = CornerWorld( xSign, ySign, zSign );
          if ( worldCorner.y < minWorldY )
            minWorldY = worldCorner.y;
        }
      }
    }

    return float.IsPositiveInfinity( minWorldY ) ? 0.0f : minWorldY;
  }

  public float GetWorldMaxY()
  {
    var maxWorldY = float.NegativeInfinity;
    for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
      for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
        for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
          var worldCorner = CornerWorld( xSign, ySign, zSign );
          if ( worldCorner.y > maxWorldY )
            maxWorldY = worldCorner.y;
        }
      }
    }

    return float.IsNegativeInfinity( maxWorldY ) ? 0.0f : maxWorldY;
  }
}

internal struct MeasurementBoxClosestSample
{
  public bool IsValid;
  public bool SourceIsBucket;
  public Vector3 NormalizedSourcePoint;
  public Vector3 SourcePointWorld;
  public Vector3 ClosestPointWorld;
  public float DistanceMeters;
}

internal enum BucketTargetDistanceBoxSource
{
  None = 0,
  ConfiguredProxy = 1,
  BucketMeasurement = 2,
  LegacyCompositeBounds = 3
}

internal enum TargetDistanceGeometrySource
{
  None = 0,
  TargetShapeBoxes = 1,
  TargetDistanceVolume = 2
}

internal struct TargetDistanceDiagnostic
{
  public OrientedMeasurementBox BucketBox;
  public OrientedMeasurementBox TargetBox;
  public MeasurementBoxClosestSample ClosestSample;
  public float ApproximateDistanceMeters;
  public BucketTargetDistanceBoxSource BucketBoxSource;
  public TargetDistanceGeometrySource TargetGeometrySource;
}

public struct TargetGeometryMetrics
{
  public bool IsValid;
  public float TargetHorizontalDistanceMeters;
  public float BucketHeightAboveTargetRimMeters;
  public float BucketOverTargetFootprintMask;
  public float DumpClearanceOkMask;
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

    if ( !TryGetTargetDistanceBucketBox( bucketReference, out var bucketBox, out _ ) )
      return false;

    if ( !TryGetTargetDistanceGeometry( targetSensor,
                                        bucketBox,
                                        out var targetBox,
                                        out _,
                                        out var minDistanceMetersCandidate ) )
      return false;

    minDistanceMeters = minDistanceMetersCandidate;
    return true;
  }

  public static bool TryMeasureTargetGeometry( Transform bucketReference,
                                               TargetMassSensorBase targetSensor,
                                               out TargetGeometryMetrics metrics )
  {
    metrics = new TargetGeometryMetrics
    {
      IsValid = false,
      TargetHorizontalDistanceMeters = -1.0f,
      BucketHeightAboveTargetRimMeters = 0.0f,
      BucketOverTargetFootprintMask = 0.0f,
      DumpClearanceOkMask = 0.0f
    };

    if ( bucketReference == null || targetSensor == null )
      return false;

    if ( !TryGetTargetDistanceBucketBox( bucketReference, out var bucketBox, out _ ) )
      return false;

    if ( !TryGetTargetClearanceGeometry( targetSensor, out var targetBox ) )
      return false;

    var horizontalDistance = MeasureHorizontalFootprintDistance( bucketBox,
                                                                 targetBox,
                                                                 out var overFootprint );
    var heightAboveTargetRim = bucketBox.GetWorldMinY() - targetBox.GetWorldMaxY();
    var horizontalTolerance = Mathf.Max( 0.0f, targetSensor.TargetDumpClearanceHorizontalToleranceMeters );
    var clearanceOk = horizontalDistance <= horizontalTolerance &&
                      heightAboveTargetRim >= 0.0f;

    metrics = new TargetGeometryMetrics
    {
      IsValid = true,
      TargetHorizontalDistanceMeters = horizontalDistance,
      BucketHeightAboveTargetRimMeters = heightAboveTargetRim,
      BucketOverTargetFootprintMask = overFootprint ? 1.0f : 0.0f,
      DumpClearanceOkMask = clearanceOk ? 1.0f : 0.0f
    };
    return true;
  }

  private static float MeasureHorizontalFootprintDistance( OrientedMeasurementBox bucketBox,
                                                           OrientedMeasurementBox targetBox,
                                                           out bool footprintsOverlap )
  {
    footprintsOverlap = false;

    if ( bucketBox.Frame == null || targetBox.Frame == null )
      return -1.0f;

    var bucketMinX = float.PositiveInfinity;
    var bucketMaxX = float.NegativeInfinity;
    var bucketMinZ = float.PositiveInfinity;
    var bucketMaxZ = float.NegativeInfinity;

    for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
      for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
        for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
          var bucketCornerWorld = bucketBox.CornerWorld( xSign, ySign, zSign );
          var bucketCornerInTarget = targetBox.Frame.InverseTransformPoint( bucketCornerWorld ) - targetBox.CenterLocal;
          bucketMinX = Mathf.Min( bucketMinX, bucketCornerInTarget.x );
          bucketMaxX = Mathf.Max( bucketMaxX, bucketCornerInTarget.x );
          bucketMinZ = Mathf.Min( bucketMinZ, bucketCornerInTarget.z );
          bucketMaxZ = Mathf.Max( bucketMaxZ, bucketCornerInTarget.z );
        }
      }
    }

    if ( float.IsPositiveInfinity( bucketMinX ) || float.IsNegativeInfinity( bucketMaxX ) ||
         float.IsPositiveInfinity( bucketMinZ ) || float.IsNegativeInfinity( bucketMaxZ ) )
      return -1.0f;

    var targetMinX = -targetBox.HalfExtents.x;
    var targetMaxX = targetBox.HalfExtents.x;
    var targetMinZ = -targetBox.HalfExtents.z;
    var targetMaxZ = targetBox.HalfExtents.z;

    var outsideX = Mathf.Max( Mathf.Max( targetMinX - bucketMaxX, bucketMinX - targetMaxX ), 0.0f );
    var outsideZ = Mathf.Max( Mathf.Max( targetMinZ - bucketMaxZ, bucketMinZ - targetMaxZ ), 0.0f );
    footprintsOverlap = outsideX <= 1.0e-4f && outsideZ <= 1.0e-4f;
    return Mathf.Sqrt( outsideX * outsideX + outsideZ * outsideZ );
  }

  internal static bool TryDiagnoseDistance( Transform bucketReference,
                                            TargetMassSensorBase targetSensor,
                                            out TargetDistanceDiagnostic diagnostic )
  {
    diagnostic = default;
    if ( bucketReference == null || targetSensor == null )
      return false;

    if ( !TryGetTargetDistanceBucketBox( bucketReference, out var bucketBox, out var bucketBoxSource ) )
      return false;

    if ( !TryGetTargetDistanceGeometry( targetSensor,
                                        bucketBox,
                                        out var targetBox,
                                        out var targetGeometrySource,
                                        out var approximateDistanceMeters,
                                        out var closestSample ) )
      return false;

    diagnostic = new TargetDistanceDiagnostic
    {
      BucketBox = bucketBox,
      TargetBox = targetBox,
      ApproximateDistanceMeters = approximateDistanceMeters,
      ClosestSample = closestSample,
      BucketBoxSource = bucketBoxSource,
      TargetGeometrySource = targetGeometrySource
    };

    return diagnostic.ApproximateDistanceMeters >= 0.0f;
  }

  internal static bool TryGetMeasurementBox( Transform reference, out OrientedMeasurementBox measurementBox )
  {
    measurementBox = default;
    if ( reference == null )
      return false;

    if ( ExcavationMassTracker.TryGetBucketMeasurementVolumeForFrame( reference,
                                                                      out var trackerMeasurementCenter,
                                                                      out var trackerMeasurementHalfExtents ) ) {
      measurementBox = new OrientedMeasurementBox
      {
        Frame = reference,
        CenterLocal = trackerMeasurementCenter,
        HalfExtents = trackerMeasurementHalfExtents
      };
      return measurementBox.IsValid;
    }

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

  internal static bool TryGetDigAreaMeasurementBox( Transform reference,
                                                    out OrientedMeasurementBox measurementBox )
  {
    measurementBox = default;
    if ( reference == null )
      return false;

    // DigArea start qualification should use the same tighter proxy geometry
    // that operators tune for close-proximity spatial reasoning.
    if ( TryGetTargetDistanceBucketBox( reference, out measurementBox, out _ ) )
      return measurementBox.IsValid;

    return false;
  }

  internal static float MeasureApproximateDistance( OrientedMeasurementBox left, OrientedMeasurementBox right )
  {
    return MeasureApproximateDistance( left, right, out _ );
  }

  internal static float MeasureApproximateDistance( OrientedMeasurementBox left,
                                                    OrientedMeasurementBox right,
                                                    out MeasurementBoxClosestSample closestSample )
  {
    var minDistanceSq = float.PositiveInfinity;
    closestSample = default;
    SampleBoxAgainstOther( left, right, true, ref minDistanceSq, ref closestSample );
    SampleBoxAgainstOther( right, left, false, ref minDistanceSq, ref closestSample );

    if ( float.IsPositiveInfinity( minDistanceSq ) )
      return -1.0f;

    closestSample.DistanceMeters = Mathf.Sqrt( Mathf.Max( 0.0f, minDistanceSq ) );
    return closestSample.DistanceMeters;
  }

  private static bool TryGetTargetDistanceBucketBox( Transform reference,
                                                     out OrientedMeasurementBox measurementBox,
                                                     out BucketTargetDistanceBoxSource measurementSource )
  {
    measurementSource = BucketTargetDistanceBoxSource.None;
    measurementBox = default;
    if ( reference == null )
      return false;

    if ( TryCreateMeasurementBox( reference,
                                  ExcavationMassTracker.TryGetTargetDistanceProxyVolumeForFrame,
                                  out measurementBox ) ) {
      measurementSource = BucketTargetDistanceBoxSource.ConfiguredProxy;
      return true;
    }

    if ( TryCreateMeasurementBox( reference,
                                  ExcavationMassTracker.TryGetBucketMeasurementVolumeForFrame,
                                  out measurementBox ) ) {
      measurementSource = BucketTargetDistanceBoxSource.BucketMeasurement;
      return true;
    }

    if ( TryGetLegacyMeasurementBox( reference, out measurementBox ) ) {
      measurementSource = BucketTargetDistanceBoxSource.LegacyCompositeBounds;
      return true;
    }

    return false;
  }

  private static bool TryGetTargetDistanceGeometry( TargetMassSensorBase targetSensor,
                                                    OrientedMeasurementBox bucketBox,
                                                    out OrientedMeasurementBox targetBox,
                                                    out TargetDistanceGeometrySource targetGeometrySource,
                                                    out float minDistanceMeters )
  {
    return TryGetTargetDistanceGeometry( targetSensor,
                                         bucketBox,
                                         out targetBox,
                                         out targetGeometrySource,
                                         out minDistanceMeters,
                                         out _ );
  }

  private static bool TryGetTargetDistanceGeometry( TargetMassSensorBase targetSensor,
                                                    OrientedMeasurementBox bucketBox,
                                                    out OrientedMeasurementBox targetBox,
                                                    out TargetDistanceGeometrySource targetGeometrySource,
                                                    out float minDistanceMeters,
                                                    out MeasurementBoxClosestSample closestSample )
  {
    targetBox = default;
    targetGeometrySource = TargetDistanceGeometrySource.None;
    minDistanceMeters = -1.0f;
    closestSample = default;

    if ( targetSensor == null )
      return false;

    if ( TryGetTargetShapeBoxes( targetSensor, out var targetBoxes ) &&
         TryMeasureDistanceToTargetBoxes( bucketBox,
                                          targetBoxes,
                                          out targetBox,
                                          out minDistanceMeters,
                                          out closestSample ) ) {
      targetGeometrySource = TargetDistanceGeometrySource.TargetShapeBoxes;
      return true;
    }

    if ( !targetSensor.TryGetTargetDistanceVolume( out var targetFrame, out var targetCenterLocal, out var targetHalfExtents ) )
      return false;

    targetBox = new OrientedMeasurementBox
    {
      Frame = targetFrame,
      CenterLocal = targetCenterLocal,
      HalfExtents = targetHalfExtents
    };

    if ( !targetBox.IsValid )
      return false;

    minDistanceMeters = MeasureApproximateDistance( bucketBox, targetBox, out closestSample );
    if ( minDistanceMeters < 0.0f )
      return false;

    targetGeometrySource = TargetDistanceGeometrySource.TargetDistanceVolume;
    return true;
  }

  private static bool TryGetTargetClearanceGeometry( TargetMassSensorBase targetSensor,
                                                     out OrientedMeasurementBox targetBox )
  {
    targetBox = default;
    if ( targetSensor == null )
      return false;

    if ( !targetSensor.TryGetTargetClearanceVolume( out var targetFrame,
                                                    out var targetCenterLocal,
                                                    out var targetHalfExtents ) )
      return false;

    targetBox = new OrientedMeasurementBox
    {
      Frame = targetFrame,
      CenterLocal = targetCenterLocal,
      HalfExtents = targetHalfExtents
    };
    return targetBox.IsValid;
  }

  private static bool TryGetTargetShapeBoxes( TargetMassSensorBase targetSensor,
                                              out OrientedMeasurementBox[] targetBoxes )
  {
    targetBoxes = null;
    if ( targetSensor == null )
      return false;

    var collisionShapes = targetSensor.GetCollisionShapes();
    if ( collisionShapes == null || collisionShapes.Length == 0 )
      return false;

    var collectedBoxes = new System.Collections.Generic.List<OrientedMeasurementBox>( collisionShapes.Length );
    foreach ( var collisionShape in collisionShapes ) {
      if ( collisionShape is not Box targetBoxShape )
        continue;

      var halfExtents = targetBoxShape.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      var targetBox = new OrientedMeasurementBox
      {
        Frame = targetBoxShape.transform,
        CenterLocal = Vector3.zero,
        HalfExtents = halfExtents
      };

      if ( targetBox.IsValid )
        collectedBoxes.Add( targetBox );
    }

    if ( collectedBoxes.Count == 0 )
      return false;

    targetBoxes = collectedBoxes.ToArray();
    return true;
  }

  private static bool TryMeasureDistanceToTargetBoxes( OrientedMeasurementBox bucketBox,
                                                       OrientedMeasurementBox[] targetBoxes,
                                                       out OrientedMeasurementBox closestTargetBox,
                                                       out float minDistanceMeters,
                                                       out MeasurementBoxClosestSample closestSample )
  {
    closestTargetBox = default;
    minDistanceMeters = -1.0f;
    closestSample = default;
    if ( targetBoxes == null || targetBoxes.Length == 0 )
      return false;

    var found = false;
    var bestDistanceMeters = float.PositiveInfinity;

    foreach ( var targetBox in targetBoxes ) {
      if ( !targetBox.IsValid )
        continue;

      var candidateDistanceMeters = MeasureApproximateDistance( bucketBox, targetBox, out var candidateClosestSample );
      if ( candidateDistanceMeters < 0.0f )
        continue;

      if ( !found || candidateDistanceMeters < bestDistanceMeters ) {
        found = true;
        bestDistanceMeters = candidateDistanceMeters;
        closestTargetBox = targetBox;
        closestSample = candidateClosestSample;
      }
    }

    if ( !found )
      return false;

    minDistanceMeters = bestDistanceMeters;
    return true;
  }

  private delegate bool FrameMeasurementVolumeProvider( Transform measurementFrame,
                                                        out Vector3 measurementCenter,
                                                        out Vector3 measurementHalfExtents );

  private static bool TryCreateMeasurementBox( Transform reference,
                                               FrameMeasurementVolumeProvider measurementProvider,
                                               out OrientedMeasurementBox measurementBox )
  {
    measurementBox = default;
    if ( reference == null || measurementProvider == null )
      return false;

    if ( !measurementProvider( reference, out var measurementCenter, out var measurementHalfExtents ) )
      return false;

    measurementBox = new OrientedMeasurementBox
    {
      Frame = reference,
      CenterLocal = measurementCenter,
      HalfExtents = measurementHalfExtents
    };
    return measurementBox.IsValid;
  }

  private static bool TryGetLegacyMeasurementBox( Transform reference, out OrientedMeasurementBox measurementBox )
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

  private static void SampleBoxAgainstOther( OrientedMeasurementBox source,
                                             OrientedMeasurementBox target,
                                             bool sourceIsBucket,
                                             ref float minDistanceSq,
                                             ref MeasurementBoxClosestSample closestSample )
  {
    for ( var xIndex = 0; xIndex < SampleCoordinates.Length; ++xIndex ) {
      for ( var yIndex = 0; yIndex < SampleCoordinates.Length; ++yIndex ) {
        for ( var zIndex = 0; zIndex < SampleCoordinates.Length; ++zIndex ) {
          var normalizedSourcePoint = new Vector3( SampleCoordinates[xIndex],
                                                   SampleCoordinates[yIndex],
                                                   SampleCoordinates[zIndex] );
          var samplePointWorld = source.SamplePointWorld( normalizedSourcePoint.x,
                                                          normalizedSourcePoint.y,
                                                          normalizedSourcePoint.z );
          var closestPointWorld = target.ClosestPointWorld( samplePointWorld );
          var distanceSq = ( samplePointWorld - closestPointWorld ).sqrMagnitude;
          if ( distanceSq < minDistanceSq ) {
            minDistanceSq = distanceSq;
            closestSample = new MeasurementBoxClosestSample
            {
              IsValid = true,
              SourceIsBucket = sourceIsBucket,
              NormalizedSourcePoint = normalizedSourcePoint,
              SourcePointWorld = samplePointWorld,
              ClosestPointWorld = closestPointWorld
            };
          }
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

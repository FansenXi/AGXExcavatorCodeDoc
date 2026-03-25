using AGXUnity.Collide;
using UnityEngine;

internal static class TargetDistanceVolumeUtility
{
  private static readonly Vector3[] BoxLocalCorners = new Vector3[8];

  public static bool TryCalculateLocalBoxBounds( Transform reference,
                                                 Transform excludedRoot,
                                                 out Bounds localBounds )
  {
    localBounds = default;
    if ( reference == null )
      return false;

    var boxes = reference.GetComponentsInChildren<Box>( true );
    var hasBounds = false;

    foreach ( var box in boxes ) {
      if ( box == null )
        continue;

      if ( excludedRoot != null && box.transform.IsChildOf( excludedRoot ) )
        continue;

      var halfExtents = box.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      GetLocalBoxCorners( halfExtents, BoxLocalCorners );
      var localMin = Vector3.positiveInfinity;
      var localMax = Vector3.negativeInfinity;

      for ( var i = 0; i < BoxLocalCorners.Length; ++i ) {
        var worldCorner = box.transform.TransformPoint( BoxLocalCorners[i] );
        var localCorner = reference.InverseTransformPoint( worldCorner );
        localMin = Vector3.Min( localMin, localCorner );
        localMax = Vector3.Max( localMax, localCorner );
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

  public static bool TryCalculateLocalBoxBounds( Transform reference,
                                                 Shape[] sourceShapes,
                                                 out Bounds localBounds )
  {
    localBounds = default;
    if ( reference == null || sourceShapes == null || sourceShapes.Length == 0 )
      return false;

    var hasBounds = false;
    foreach ( var sourceShape in sourceShapes ) {
      if ( sourceShape is not Box box )
        continue;

      var halfExtents = box.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      GetLocalBoxCorners( halfExtents, BoxLocalCorners );
      var localMin = Vector3.positiveInfinity;
      var localMax = Vector3.negativeInfinity;

      for ( var i = 0; i < BoxLocalCorners.Length; ++i ) {
        var worldCorner = box.transform.TransformPoint( BoxLocalCorners[i] );
        var localCorner = reference.InverseTransformPoint( worldCorner );
        localMin = Vector3.Min( localMin, localCorner );
        localMax = Vector3.Max( localMax, localCorner );
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

  private static void GetLocalBoxCorners( Vector3 halfExtents, Vector3[] corners )
  {
    var min = -halfExtents;
    var max = halfExtents;

    corners[0] = new Vector3( min.x, min.y, min.z );
    corners[1] = new Vector3( min.x, min.y, max.z );
    corners[2] = new Vector3( min.x, max.y, min.z );
    corners[3] = new Vector3( min.x, max.y, max.z );
    corners[4] = new Vector3( max.x, min.y, min.z );
    corners[5] = new Vector3( max.x, min.y, max.z );
    corners[6] = new Vector3( max.x, max.y, min.z );
    corners[7] = new Vector3( max.x, max.y, max.z );
  }
}

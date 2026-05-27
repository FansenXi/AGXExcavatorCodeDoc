using System;
using System.Collections.Generic;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.GraphPerception
{
  [Serializable]
  public class BucketSamplePoint
  {
    [Tooltip( "Transform whose world position is emitted as a tool node." )]
    public Transform transform;

    [Tooltip( "Optional human-readable label (e.g. cutting_edge_left). Currently informational only; not serialized into the export." )]
    public string label;
  }

  /// <summary>
  /// Inspector-driven set of bucket-anchored sample points consumed by
  /// <see cref="TerrainGraphObservationProvider"/>. Drag a handful of
  /// child Transforms of the bucket prefab (cutting edge ends, back wall,
  /// side walls, base) into <c>m_points</c>; the provider will emit one
  /// <c>kind = tool</c> node per entry.
  ///
  /// Falls back to <see cref="m_originFallback"/> (or the provider's
  /// bucket reference) when no points are configured, so the export never
  /// loses the bucket entirely.
  /// </summary>
  [AddComponentMenu( "AGXUnity Excavator/Bucket Sample Point Provider" )]
  public class BucketSamplePointProvider : MonoBehaviour
  {
    [SerializeField]
    private List<BucketSamplePoint> m_points = new List<BucketSamplePoint>();

    [SerializeField]
    [Tooltip( "When no sample points are configured, emit a single fallback node at m_originFallback (or the provider's bucket reference)." )]
    private bool m_includeOriginFallback = true;

    [SerializeField]
    [Tooltip( "Transform used for the single fallback tool node when m_points is empty. If null, the provider's bucket reference is used." )]
    private Transform m_originFallback = null;

    public int ConfiguredPointCount
    {
      get
      {
        if ( m_points == null )
          return 0;
        var count = 0;
        for ( var i = 0; i < m_points.Count; ++i ) {
          var p = m_points[ i ];
          if ( p != null && p.transform != null )
            count++;
        }
        return count;
      }
    }

    /// <summary>
    /// Appends world-space sample positions (and best-effort labels) into the
    /// supplied lists. Returns the number of points emitted.
    /// </summary>
    public int EmitSamplePoints( List<Vector3> outPositions, List<string> outLabels, Transform defaultOrigin )
    {
      if ( outPositions == null )
        throw new ArgumentNullException( nameof( outPositions ) );

      var emitted = 0;
      if ( m_points != null ) {
        for ( var i = 0; i < m_points.Count; ++i ) {
          var p = m_points[ i ];
          if ( p == null || p.transform == null )
            continue;

          outPositions.Add( p.transform.position );
          if ( outLabels != null )
            outLabels.Add( string.IsNullOrEmpty( p.label ) ? p.transform.name : p.label );
          emitted++;
        }
      }

      if ( emitted == 0 && m_includeOriginFallback ) {
        var origin = m_originFallback != null ? m_originFallback : defaultOrigin;
        if ( origin != null ) {
          outPositions.Add( origin.position );
          if ( outLabels != null )
            outLabels.Add( "bucket_origin" );
          emitted = 1;
        }
      }

      return emitted;
    }
  }
}

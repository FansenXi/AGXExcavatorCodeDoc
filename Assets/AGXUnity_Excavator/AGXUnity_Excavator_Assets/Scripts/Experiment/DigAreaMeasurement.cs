using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using UnityEngine;
using UnityEngine.Rendering;

public class DigAreaMeasurement : MonoBehaviour
{
  private const string DefaultDigAreaRootName = "AGXUnity.RigidBody.DigArea";
  private const int CellGridLongCount = 3;
  private const int CellGridShortCount = 2;
  private const int LongAxisX = 0;
  private const int LongAxisZ = 2;

	  public struct CellMetrics
	  {
    public bool GeometryAvailable;
    public int LongAxis;
    public int GridLongCount;
    public int GridShortCount;
    public Vector3 BucketDigAreaLocalMeters;
    public float LongNorm;
    public float ShortNorm;
    public int LongIndex;
    public int ShortIndex;
	    public int CellId;
	  }

	  public struct SurfaceGridMetrics
	  {
	    public bool GeometryAvailable;
	    public float TargetDepthMeters;
	    public float[] SurfaceDepthMeters;
	    public float[] RemovedDepthMeters;
	    public float[] TargetDepthMetersByCell;
	    public float[] ValidMask;
	  }

  [SerializeField]
  private Transform m_digAreaRoot = null;

  [SerializeField]
  private Box m_digAreaBox = null;

  [SerializeField]
  private string m_digAreaRootName = DefaultDigAreaRootName;

  [SerializeField]
  private bool m_autoAlignToDigTerrain = false;

  [SerializeField]
  private string m_digTerrainName = "DigTerrain";

  [SerializeField]
  [Min( 0.0f )]
  private float m_digAreaPlaneYOffset = 0.0f;

  [SerializeField]
  [Min( 0.001f )]
  private float m_digAreaHalfHeight = 0.0375f;

  [SerializeField]
  private bool m_enableRuntimeVisuals = false;

  [SerializeField]
  private MeshRenderer m_fillRenderer = null;

  [SerializeField]
  private LineRenderer m_contourRenderer = null;

  [SerializeField]
  private Color m_fillColor = new Color( 1.0f, 0.55f, 0.20f, 0.12f );

  [SerializeField]
  private Color m_contourColor = new Color( 1.0f, 0.45f, 0.05f, 0.98f );

  [SerializeField]
  [Min( 0.001f )]
  private float m_contourWidth = 0.08f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_contourHeightOffset = 0.015f;

  [SerializeField]
  private bool m_enableCellGridVisuals = true;

  [SerializeField]
  private Color m_cellGridColor = new Color( 1.0f, 0.55f, 0.0f, 0.95f );

  [SerializeField]
  [Min( 0.001f )]
  private float m_cellGridWidth = 0.035f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_cellGridHeightOffset = 0.025f;

	  [SerializeField]
	  [Min( 0.0f )]
	  private float m_depthHorizontalBlendDistance = 0.25f;

	  [SerializeField]
	  [Min( 0.0f )]
	  private float m_targetDepthMeters = 0.08f;

  [SerializeField]
  [Range( 3, 9 )]
  private int m_depthSamplingResolution = 5;

  private Material m_runtimeFillMaterial = null;
  private Material m_runtimeContourMaterial = null;
	  private Material m_runtimeCellGridMaterial = null;
	  private LineRenderer[] m_cellGridRenderers = new LineRenderer[ 0 ];
	  private MeshRenderer m_cachedFillRenderer = null;
	  private Material m_originalFillMaterial = null;
	  private bool m_originalFillRendererEnabled = false;
	  private bool m_hasOriginalFillRendererState = false;
	  private bool m_hasAutoAlignedDigArea = false;
	  private readonly float[] m_surfaceBaselineDepthMeters = new float[ CellGridLongCount * CellGridShortCount ];
	  private bool m_surfaceBaselineValid = false;

	  public float TargetDepthMeters => m_targetDepthMeters;

  private void OnEnable()
  {
    ResolveReferences();
    ApplyVisuals();
  }

  private void LateUpdate()
  {
    ResolveReferences();
    RefreshContourGeometry();
    RefreshCellGridGeometry();
  }

  private void OnDestroy()
  {
    RestoreFillRendererState();
    DestroyRuntimeMaterials();
  }

  public static DigAreaMeasurement FindOrCreateInScene()
  {
    var existingMeasurements = Object.FindObjectsByType<DigAreaMeasurement>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( existingMeasurements != null ) {
      foreach ( var existingMeasurement in existingMeasurements ) {
        if ( existingMeasurement == null )
          continue;

        existingMeasurement.ResolveReferences();
        return existingMeasurement;
      }
    }

    var digAreaRoot = FindDigAreaRoot( DefaultDigAreaRootName );
    if ( digAreaRoot == null )
      return null;

    var measurement = digAreaRoot.GetComponent<DigAreaMeasurement>();
    if ( measurement == null )
      measurement = digAreaRoot.gameObject.AddComponent<DigAreaMeasurement>();

    measurement.ResolveReferences();
    return measurement;
  }

  public void ResolveReferences()
  {
    if ( m_digAreaRoot == null )
      m_digAreaRoot = FindDigAreaRoot( m_digAreaRootName );

    if ( m_digAreaRoot == null ) {
      m_digAreaBox = null;
      m_fillRenderer = null;
      m_contourRenderer = null;
      m_cellGridRenderers = new LineRenderer[ 0 ];
      m_hasAutoAlignedDigArea = false;
      return;
    }

    if ( m_digAreaBox == null || !m_digAreaBox.transform.IsChildOf( m_digAreaRoot ) )
      m_digAreaBox = ResolveDigAreaBox( m_digAreaRoot );

    AlignToDigTerrainOnceIfAvailable();

    if ( m_fillRenderer == null || !m_fillRenderer.transform.IsChildOf( m_digAreaRoot ) )
      m_fillRenderer = ResolveFillRenderer( m_digAreaRoot );
    CacheFillRendererState();

    if ( m_contourRenderer == null || !m_contourRenderer.transform.IsChildOf( m_digAreaRoot ) )
      m_contourRenderer = ResolveContourRenderer( m_digAreaRoot );

    var cellGridParent = m_digAreaBox != null ? m_digAreaBox.transform : m_digAreaRoot;
    var legacyCellGridRoot = m_digAreaRoot.Find( "DigAreaCellGridRuntime" );
    if ( legacyCellGridRoot != null && legacyCellGridRoot.parent != cellGridParent )
      legacyCellGridRoot.SetParent( cellGridParent, false );
    if ( legacyCellGridRoot != null )
      legacyCellGridRoot.gameObject.SetActive( true );
    if ( m_cellGridRenderers == null ||
         m_cellGridRenderers.Length != 3 ||
         !CellGridRenderersBelongTo( m_cellGridRenderers, cellGridParent ) )
      m_cellGridRenderers = ResolveCellGridRenderers( cellGridParent );

    ApplyVisuals();
  }

  public bool TryMeasureBucketDigAreaMetrics( Transform bucketReference,
                                              out float minDistanceMeters,
                                              out float bucketDepthBelowPlaneMeters )
  {
    minDistanceMeters = -1.0f;
    bucketDepthBelowPlaneMeters = 0.0f;

    ResolveReferences();
    if ( bucketReference == null || m_digAreaBox == null )
      return false;

    var digAreaBox = new OrientedMeasurementBox
    {
      Frame = m_digAreaBox.transform,
      CenterLocal = Vector3.zero,
      HalfExtents = m_digAreaBox.HalfExtents
    };
    if ( !digAreaBox.IsValid )
      return false;

    var hasBoxMetrics = BucketTargetDistanceMeasurementUtility.TryGetDigAreaMeasurementBox( bucketReference, out var bucketBox );
    if ( hasBoxMetrics ) {
      minDistanceMeters = BucketTargetDistanceMeasurementUtility.MeasureApproximateDistance( bucketBox, digAreaBox );
      bucketDepthBelowPlaneMeters = MeasureEffectiveBucketDepthBelowPlane( bucketBox );
    }

    if ( TryMeasureShovelDigAreaMetrics( bucketReference,
                                         digAreaBox,
                                         out var shovelDistanceMeters,
                                         out var shovelDepthBelowPlaneMeters ) ) {
      minDistanceMeters = minDistanceMeters >= 0.0f ?
                          Mathf.Min( minDistanceMeters, shovelDistanceMeters ) :
                          shovelDistanceMeters;
      bucketDepthBelowPlaneMeters = Mathf.Max( bucketDepthBelowPlaneMeters,
                                               shovelDepthBelowPlaneMeters );
      return true;
    }

    return hasBoxMetrics && minDistanceMeters >= 0.0f;
  }

	  public bool TryMeasureBucketCellMetrics( Transform bucketReference, out CellMetrics metrics )
	  {
    metrics = new CellMetrics
    {
      GeometryAvailable = false,
      LongAxis = LongAxisZ,
      GridLongCount = CellGridLongCount,
      GridShortCount = CellGridShortCount,
      BucketDigAreaLocalMeters = Vector3.zero,
      LongNorm = 0.0f,
      ShortNorm = 0.0f,
      LongIndex = -1,
      ShortIndex = -1,
      CellId = -1
    };

    ResolveReferences();
    if ( bucketReference == null || m_digAreaBox == null )
      return false;

    if ( !BucketTargetDistanceMeasurementUtility.TryGetDigAreaMeasurementBox( bucketReference, out var bucketBox ) )
      return false;
    if ( !bucketBox.IsValid )
      return false;

    var halfExtents = m_digAreaBox.HalfExtents;
    if ( halfExtents.x <= 0.0f || halfExtents.z <= 0.0f )
      return false;

	    var bucketReferenceWorld = TryGetBucketReferencePointWorld( bucketReference, out var referenceWorld ) ?
	                               referenceWorld :
	                               bucketBox.Frame.TransformPoint( bucketBox.CenterLocal );
    var local = m_digAreaBox.transform.InverseTransformPoint( bucketReferenceWorld );
    var longAxis = halfExtents.x >= halfExtents.z ? LongAxisX : LongAxisZ;
    var longHalf = longAxis == LongAxisX ? halfExtents.x : halfExtents.z;
    var shortHalf = longAxis == LongAxisX ? halfExtents.z : halfExtents.x;
    var longValue = longAxis == LongAxisX ? local.x : local.z;
    var shortValue = longAxis == LongAxisX ? local.z : local.x;
    var longNorm = longValue / longHalf;
    var shortNorm = shortValue / shortHalf;
    var inBounds =
      longNorm >= -1.0f &&
      longNorm <= 1.0f &&
      shortNorm >= -1.0f &&
      shortNorm <= 1.0f;
    var longIndex = inBounds ? CellIndexFromNorm( longNorm, CellGridLongCount ) : -1;
    var shortIndex = inBounds ? CellIndexFromNorm( shortNorm, CellGridShortCount ) : -1;
    var cellId = inBounds ? longIndex * CellGridShortCount + shortIndex : -1;

    metrics = new CellMetrics
    {
      GeometryAvailable = inBounds,
      LongAxis = longAxis,
      GridLongCount = CellGridLongCount,
      GridShortCount = CellGridShortCount,
      BucketDigAreaLocalMeters = local,
      LongNorm = longNorm,
      ShortNorm = shortNorm,
      LongIndex = longIndex,
      ShortIndex = shortIndex,
      CellId = cellId
	    };
	    return true;
	  }

	  public void ResetSurfaceBaseline()
	  {
	    m_surfaceBaselineValid = false;
	    for ( var index = 0; index < m_surfaceBaselineDepthMeters.Length; ++index )
	      m_surfaceBaselineDepthMeters[ index ] = 0.0f;
	  }

	  public bool TryMeasureBucketTipDigAreaLocal( Transform bucketReference,
	                                              out Vector3 bucketTipDigAreaLocalMeters )
	  {
	    bucketTipDigAreaLocalMeters = Vector3.zero;
	    ResolveReferences();
	    if ( bucketReference == null || m_digAreaBox == null )
	      return false;

	    if ( TryGetBucketReferencePointWorld( bucketReference, out var referenceWorld ) ) {
	      bucketTipDigAreaLocalMeters = m_digAreaBox.transform.InverseTransformPoint( referenceWorld );
	      return true;
	    }

	    return false;
	  }

	  public bool TryMeasureSurfaceGridMetrics( out SurfaceGridMetrics metrics )
	  {
	    metrics = CreateEmptySurfaceGridMetrics();
	    ResolveReferences();
	    if ( m_digAreaBox == null )
	      return false;

	    var surfaceDepth = new float[ CellGridLongCount * CellGridShortCount ];
	    var removedDepth = new float[ CellGridLongCount * CellGridShortCount ];
	    var targetDepth = new float[ CellGridLongCount * CellGridShortCount ];
	    var validMask = new float[ CellGridLongCount * CellGridShortCount ];
	    var anyValid = false;

	    for ( var longIndex = 0; longIndex < CellGridLongCount; ++longIndex ) {
	      for ( var shortIndex = 0; shortIndex < CellGridShortCount; ++shortIndex ) {
	        var cellId = longIndex * CellGridShortCount + shortIndex;
	        var localPoint = CellCenterLocal( longIndex, shortIndex );
	        if ( TryMeasureSurfaceDepthAtLocal( localPoint.x,
	                                            localPoint.z,
	                                            out var currentSurfaceDepthMeters,
	                                            out _ ) ) {
	          surfaceDepth[ cellId ] = currentSurfaceDepthMeters;
	          validMask[ cellId ] = 1.0f;
	          targetDepth[ cellId ] = m_targetDepthMeters;
	          anyValid = true;
	        }
	        else {
	          surfaceDepth[ cellId ] = 0.0f;
	          validMask[ cellId ] = 0.0f;
	          targetDepth[ cellId ] = m_targetDepthMeters;
	        }
	      }
	    }

	    if ( anyValid && !m_surfaceBaselineValid ) {
	      for ( var index = 0; index < surfaceDepth.Length; ++index )
	        m_surfaceBaselineDepthMeters[ index ] = surfaceDepth[ index ];
	      m_surfaceBaselineValid = true;
	    }

	    for ( var index = 0; index < surfaceDepth.Length; ++index ) {
	      removedDepth[ index ] = validMask[ index ] > 0.5f && m_surfaceBaselineValid ?
	                              Mathf.Max( 0.0f, surfaceDepth[ index ] - m_surfaceBaselineDepthMeters[ index ] ) :
	                              0.0f;
	    }

	    metrics = new SurfaceGridMetrics
	    {
	      GeometryAvailable = anyValid,
	      TargetDepthMeters = m_targetDepthMeters,
	      SurfaceDepthMeters = surfaceDepth,
	      RemovedDepthMeters = removedDepth,
	      TargetDepthMetersByCell = targetDepth,
	      ValidMask = validMask
	    };
	    return anyValid;
	  }

	  public bool TryMeasureBucketDepthBelowSurface( Vector3 bucketTipDigAreaLocalMeters,
	                                                out float depthBelowLocalSurfaceMeters,
	                                                out float depthBelowTargetSurfaceMeters )
	  {
	    depthBelowLocalSurfaceMeters = 0.0f;
	    depthBelowTargetSurfaceMeters = 0.0f;
	    if ( m_digAreaBox == null )
	      return false;

	    if ( TryMeasureSurfaceDepthAtLocal( bucketTipDigAreaLocalMeters.x,
	                                        bucketTipDigAreaLocalMeters.z,
	                                        out _,
	                                        out var surfaceLocalYMeters ) ) {
	      depthBelowLocalSurfaceMeters = Mathf.Max( 0.0f, surfaceLocalYMeters - bucketTipDigAreaLocalMeters.y );
	    }
	    depthBelowTargetSurfaceMeters = Mathf.Max( 0.0f, -m_targetDepthMeters - bucketTipDigAreaLocalMeters.y );
	    return true;
	  }

	  public bool TryDigAreaLocalPlanePointWorld( float localXMeters,
	                                             float localZMeters,
	                                             float heightOffsetMeters,
	                                             out Vector3 worldPoint )
	  {
	    worldPoint = Vector3.zero;
	    ResolveReferences();
	    if ( m_digAreaBox == null )
	      return false;

	    worldPoint = m_digAreaBox.transform.TransformPoint(
	      new Vector3( localXMeters, 0.0f, localZMeters ) );
	    worldPoint += Vector3.up * Mathf.Max( 0.0f, heightOffsetMeters );
	    return true;
	  }

	  private SurfaceGridMetrics CreateEmptySurfaceGridMetrics()
	  {
	    var cellCount = CellGridLongCount * CellGridShortCount;
	    var targetDepth = new float[ cellCount ];
	    for ( var index = 0; index < targetDepth.Length; ++index )
	      targetDepth[ index ] = m_targetDepthMeters;
	    return new SurfaceGridMetrics
	    {
	      GeometryAvailable = false,
	      TargetDepthMeters = m_targetDepthMeters,
	      SurfaceDepthMeters = new float[ cellCount ],
	      RemovedDepthMeters = new float[ cellCount ],
	      TargetDepthMetersByCell = targetDepth,
	      ValidMask = new float[ cellCount ]
	    };
	  }

	  private Vector3 CellCenterLocal( int longIndex, int shortIndex )
	  {
	    var halfExtents = m_digAreaBox != null ? m_digAreaBox.HalfExtents : Vector3.zero;
	    var longAxis = halfExtents.x >= halfExtents.z ? LongAxisX : LongAxisZ;
	    var halfLong = longAxis == LongAxisX ? halfExtents.x : halfExtents.z;
	    var halfShort = longAxis == LongAxisX ? halfExtents.z : halfExtents.x;
	    var longNorm = -1.0f + ( longIndex + 0.5f ) * 2.0f / CellGridLongCount;
	    var shortNorm = -1.0f + ( shortIndex + 0.5f ) * 2.0f / CellGridShortCount;
	    var longValue = longNorm * halfLong;
	    var shortValue = shortNorm * halfShort;
	    return longAxis == LongAxisX ?
	           new Vector3( longValue, 0.0f, shortValue ) :
	           new Vector3( shortValue, 0.0f, longValue );
	  }

	  private bool TryMeasureSurfaceDepthAtLocal( float localXMeters,
	                                             float localZMeters,
	                                             out float surfaceDepthMeters,
	                                             out float surfaceLocalYMeters )
	  {
	    surfaceDepthMeters = 0.0f;
	    surfaceLocalYMeters = 0.0f;
	    if ( m_digAreaBox == null )
	      return false;

	    var terrain = FindDigTerrain( m_digTerrainName );
	    if ( terrain == null || terrain.terrainData == null )
	      return false;

	    var planeWorld = m_digAreaBox.transform.TransformPoint(
	      new Vector3( localXMeters, 0.0f, localZMeters ) );
	    var terrainLocal = terrain.transform.InverseTransformPoint( planeWorld );
	    var terrainSize = terrain.terrainData.size;
	    if ( terrainSize.x <= 0.0f || terrainSize.z <= 0.0f )
	      return false;

	    var normX = terrainLocal.x / terrainSize.x;
	    var normZ = terrainLocal.z / terrainSize.z;
	    if ( normX < 0.0f || normX > 1.0f || normZ < 0.0f || normZ > 1.0f )
	      return false;

	    var surfaceWorldY = terrain.transform.position.y +
	                        terrain.terrainData.GetInterpolatedHeight( normX, normZ );
	    var surfaceWorld = new Vector3( planeWorld.x, surfaceWorldY, planeWorld.z );
	    surfaceLocalYMeters = m_digAreaBox.transform.InverseTransformPoint( surfaceWorld ).y;
	    surfaceDepthMeters = Mathf.Max( 0.0f, -surfaceLocalYMeters );
	    return true;
	  }

  private float MeasureEffectiveBucketDepthBelowPlane( OrientedMeasurementBox bucketBox )
  {
    if ( m_digAreaBox == null )
      return 0.0f;

    var digAreaTransform = m_digAreaBox.transform;
    var minBucketDigAreaLocalY = float.PositiveInfinity;
    for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
      for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
        for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
          var bucketCornerWorld = bucketBox.CornerWorld( xSign, ySign, zSign );
          var bucketCornerDigAreaLocal = digAreaTransform.InverseTransformPoint( bucketCornerWorld );
          minBucketDigAreaLocalY = Mathf.Min( minBucketDigAreaLocalY, bucketCornerDigAreaLocal.y );
        }
      }
    }

    return float.IsPositiveInfinity( minBucketDigAreaLocalY ) ?
           0.0f :
           Mathf.Max( 0.0f, -minBucketDigAreaLocalY );
  }

  private bool TryMeasureShovelDigAreaMetrics( Transform bucketReference,
                                               OrientedMeasurementBox digAreaBox,
                                               out float minDistanceMeters,
                                               out float bucketDepthBelowPlaneMeters )
  {
    minDistanceMeters = -1.0f;
    bucketDepthBelowPlaneMeters = 0.0f;

    var shovel = ResolveShovel( bucketReference );
    if ( shovel == null || digAreaBox.Frame == null )
      return false;

    var hasSample = false;
    SampleShovelLine( shovel.CuttingEdge, digAreaBox, ref hasSample, ref minDistanceMeters, ref bucketDepthBelowPlaneMeters );
    SampleShovelLine( shovel.ToothDirection, digAreaBox, ref hasSample, ref minDistanceMeters, ref bucketDepthBelowPlaneMeters );
    SampleShovelLine( shovel.TopEdge, digAreaBox, ref hasSample, ref minDistanceMeters, ref bucketDepthBelowPlaneMeters );
    return hasSample && minDistanceMeters >= 0.0f;
  }

  private void SampleShovelLine( Line line,
                                 OrientedMeasurementBox digAreaBox,
                                 ref bool hasSample,
                                 ref float minDistanceMeters,
                                 ref float bucketDepthBelowPlaneMeters )
  {
    if ( line == null || !line.Valid )
      return;

    var sampleCount = Mathf.Max( 2, m_depthSamplingResolution );
    for ( var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++ ) {
      var t = sampleCount <= 1 ? 0.0f : sampleIndex / (float)( sampleCount - 1 );
      var sampleWorld = Vector3.Lerp( line.Start.Position, line.End.Position, t );
      MeasureShovelPoint( sampleWorld,
                          digAreaBox,
                          ref hasSample,
                          ref minDistanceMeters,
                          ref bucketDepthBelowPlaneMeters );
    }
  }

  private void MeasureShovelPoint( Vector3 sampleWorld,
                                   OrientedMeasurementBox digAreaBox,
                                   ref bool hasSample,
                                   ref float minDistanceMeters,
                                   ref float bucketDepthBelowPlaneMeters )
  {
    if ( digAreaBox.Frame == null )
      return;

    var local = digAreaBox.Frame.InverseTransformPoint( sampleWorld ) - digAreaBox.CenterLocal;
    var outsideX = Mathf.Max( Mathf.Abs( local.x ) - digAreaBox.HalfExtents.x, 0.0f );
    var outsideZ = Mathf.Max( Mathf.Abs( local.z ) - digAreaBox.HalfExtents.z, 0.0f );
    var outsideHorizontal = Mathf.Sqrt( outsideX * outsideX + outsideZ * outsideZ );
    var verticalGapAbovePlane = Mathf.Max( local.y - digAreaBox.HalfExtents.y, 0.0f );
    var distanceMeters = Mathf.Sqrt(
      outsideHorizontal * outsideHorizontal +
      verticalGapAbovePlane * verticalGapAbovePlane );

    if ( !hasSample || distanceMeters < minDistanceMeters )
      minDistanceMeters = distanceMeters;

    var horizontalWeight = 1.0f;
    if ( m_depthHorizontalBlendDistance <= 0.0f )
      horizontalWeight = outsideHorizontal <= 0.0f ? 1.0f : 0.0f;
    else
      horizontalWeight = 1.0f - Mathf.Clamp01( outsideHorizontal / m_depthHorizontalBlendDistance );

    var depthMeters = Mathf.Max( 0.0f, -local.y ) * horizontalWeight;
    bucketDepthBelowPlaneMeters = Mathf.Max( bucketDepthBelowPlaneMeters, depthMeters );
    hasSample = true;
  }

	  private bool TryGetBucketReferencePointWorld( Transform bucketReference, out Vector3 referenceWorld )
	  {
	    referenceWorld = Vector3.zero;
	    var shovel = ResolveShovel( bucketReference );
	    if ( shovel == null || m_digAreaBox == null ) {
	      if ( BucketTargetDistanceMeasurementUtility.TryGetDigAreaMeasurementBox( bucketReference, out var bucketBox ) &&
	           bucketBox.IsValid ) {
	        referenceWorld = bucketBox.Frame.TransformPoint( bucketBox.CenterLocal );
	        return true;
	      }
	      return false;
	    }

    var hasPoint = false;
    var bestLocalY = float.PositiveInfinity;
    TrySelectLowestShovelLinePoint( shovel.CuttingEdge, ref hasPoint, ref bestLocalY, ref referenceWorld );
    TrySelectLowestShovelLinePoint( shovel.ToothDirection, ref hasPoint, ref bestLocalY, ref referenceWorld );
    TrySelectLowestShovelLinePoint( shovel.TopEdge, ref hasPoint, ref bestLocalY, ref referenceWorld );
    return hasPoint;
  }

  private void TrySelectLowestShovelLinePoint( Line line,
                                               ref bool hasPoint,
                                               ref float bestLocalY,
                                               ref Vector3 referenceWorld )
  {
    if ( line == null || !line.Valid || m_digAreaBox == null )
      return;

    var sampleCount = Mathf.Max( 2, m_depthSamplingResolution );
    for ( var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++ ) {
      var t = sampleCount <= 1 ? 0.0f : sampleIndex / (float)( sampleCount - 1 );
      var sampleWorld = Vector3.Lerp( line.Start.Position, line.End.Position, t );
      var sampleLocal = m_digAreaBox.transform.InverseTransformPoint( sampleWorld );
      if ( hasPoint && sampleLocal.y >= bestLocalY )
        continue;

      hasPoint = true;
      bestLocalY = sampleLocal.y;
      referenceWorld = sampleWorld;
    }
  }

  private static DeformableTerrainShovel ResolveShovel( Transform bucketReference )
  {
    if ( bucketReference == null )
      return null;

    var shovel = bucketReference.GetComponent<DeformableTerrainShovel>();
    if ( shovel != null )
      return shovel;

    shovel = bucketReference.GetComponentInChildren<DeformableTerrainShovel>( true );
    if ( shovel != null )
      return shovel;

    return bucketReference.GetComponentInParent<DeformableTerrainShovel>( true );
  }

  private static Transform FindDigAreaRoot( string digAreaRootName )
  {
    if ( string.IsNullOrWhiteSpace( digAreaRootName ) )
      return null;

    var allTransforms = Object.FindObjectsByType<Transform>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( allTransforms == null )
      return null;

    foreach ( var candidate in allTransforms ) {
      if ( candidate != null && candidate.name == digAreaRootName )
        return candidate;
    }

    return null;
  }

  private void AlignToDigTerrainOnceIfAvailable()
  {
    if ( m_hasAutoAlignedDigArea || !m_autoAlignToDigTerrain || m_digAreaRoot == null || m_digAreaBox == null )
      return;

    if ( HasCalibratedSceneBox() ) {
      m_hasAutoAlignedDigArea = true;
      return;
    }

    var terrain = FindDigTerrain( m_digTerrainName );
    if ( terrain == null || terrain.terrainData == null )
      return;

    var terrainSize = terrain.terrainData.size;
    if ( terrainSize.x <= 0.0f || terrainSize.z <= 0.0f )
      return;

    var centerHeight = terrain.terrainData.GetInterpolatedHeight( 0.5f, 0.5f ) +
                       m_digAreaPlaneYOffset;
    if ( m_digAreaRoot.parent != terrain.transform )
      m_digAreaRoot.SetParent( terrain.transform, false );

    m_digAreaRoot.localPosition = new Vector3(
      0.5f * terrainSize.x,
      centerHeight,
      0.5f * terrainSize.z );
    m_digAreaRoot.localRotation = Quaternion.identity;
    m_digAreaRoot.localScale = Vector3.one;

    var boxTransform = m_digAreaBox.transform;
    if ( boxTransform.parent != m_digAreaRoot )
      boxTransform.SetParent( m_digAreaRoot, false );

    boxTransform.localPosition = Vector3.zero;
    boxTransform.localRotation = Quaternion.identity;
    boxTransform.localScale = Vector3.one;

    m_digAreaBox.HalfExtents = new Vector3(
      0.5f * terrainSize.x,
      Mathf.Max( 0.001f, m_digAreaHalfHeight ),
      0.5f * terrainSize.z );
    m_hasAutoAlignedDigArea = true;
  }

  private bool HasCalibratedSceneBox()
  {
    if ( m_digAreaRoot == null || m_digAreaBox == null )
      return false;

    var rootTransform = m_digAreaRoot;
    var boxTransform = m_digAreaBox.transform;
    return rootTransform.localPosition.sqrMagnitude > 1.0e-6f ||
           Quaternion.Angle( rootTransform.localRotation, Quaternion.identity ) > 0.001f ||
           ( rootTransform.localScale - Vector3.one ).sqrMagnitude > 1.0e-6f ||
           boxTransform.localPosition.sqrMagnitude > 1.0e-6f ||
           Quaternion.Angle( boxTransform.localRotation, Quaternion.identity ) > 0.001f ||
           ( boxTransform.localScale - Vector3.one ).sqrMagnitude > 1.0e-6f;
  }

  private static Terrain FindDigTerrain( string digTerrainName )
  {
    if ( string.IsNullOrWhiteSpace( digTerrainName ) )
      return null;

    var allTerrains = Object.FindObjectsByType<Terrain>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( allTerrains == null )
      return null;

    foreach ( var terrain in allTerrains ) {
      if ( terrain != null && terrain.name == digTerrainName )
        return terrain;
    }

    return null;
  }

  private static Box ResolveDigAreaBox( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var boxes = digAreaRoot.GetComponentsInChildren<Box>( true );
    if ( boxes == null || boxes.Length == 0 )
      return null;

    Box bestBox = null;
    var bestHalfHeight = float.PositiveInfinity;
    foreach ( var candidate in boxes ) {
      if ( candidate == null )
        continue;

      var halfExtents = candidate.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      if ( halfExtents.y < bestHalfHeight ) {
        bestBox = candidate;
        bestHalfHeight = halfExtents.y;
      }
    }

    return bestBox;
  }

  private void ApplyVisuals()
  {
    if ( !m_enableRuntimeVisuals ) {
      RestoreFillRendererState();
      SetRendererEnabled( m_fillRenderer, false );
      if ( m_contourRenderer != null )
        m_contourRenderer.enabled = false;
      if ( m_enableCellGridVisuals ) {
        ApplyCellGridVisual();
        RefreshCellGridGeometry();
      }
      else {
        SetCellGridRenderersEnabled( false );
      }
      return;
    }

    ApplyFillVisual();
    ApplyContourVisual();
    ApplyCellGridVisual();
    RefreshContourGeometry();
    RefreshCellGridGeometry();
  }

  private void ApplyFillVisual()
  {
    if ( m_fillRenderer == null )
      return;

    if ( m_runtimeFillMaterial == null ) {
      var fillShader = FindFirstAvailableShader(
        "Legacy Shaders/Transparent/Diffuse",
        "Sprites/Default",
        "Unlit/Transparent",
        "Standard" );
      if ( fillShader == null )
        return;

      m_runtimeFillMaterial = new Material( fillShader )
      {
        name = "DigAreaFillRuntime",
        hideFlags = HideFlags.DontSave
      };
      ConfigureStandardTransparentMaterial( m_runtimeFillMaterial );
    }

    if ( m_runtimeFillMaterial.HasProperty( "_Color" ) )
      m_runtimeFillMaterial.color = m_fillColor;

    m_runtimeFillMaterial.renderQueue = 3000;
    m_fillRenderer.sharedMaterial = m_runtimeFillMaterial;
    m_fillRenderer.enabled = true;
    m_fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
    m_fillRenderer.receiveShadows = false;
  }

  private void ApplyContourVisual()
  {
    if ( m_contourRenderer == null )
      return;

    if ( m_runtimeContourMaterial == null ) {
      var contourShader = FindFirstAvailableShader(
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended Premultiply",
        "Unlit/Color" );
      if ( contourShader == null )
        return;

      m_runtimeContourMaterial = new Material( contourShader )
      {
        name = "DigAreaContourRuntime",
        hideFlags = HideFlags.DontSave
      };
      if ( m_runtimeContourMaterial.HasProperty( "_Color" ) )
        m_runtimeContourMaterial.color = m_contourColor;
      m_runtimeContourMaterial.renderQueue = 3100;
    }

    m_contourRenderer.sharedMaterial = m_runtimeContourMaterial;
    m_contourRenderer.loop = true;
    m_contourRenderer.useWorldSpace = true;
    m_contourRenderer.positionCount = 4;
    m_contourRenderer.startWidth = m_contourWidth;
    m_contourRenderer.endWidth = m_contourWidth;
    m_contourRenderer.startColor = m_contourColor;
    m_contourRenderer.endColor = m_contourColor;
    m_contourRenderer.shadowCastingMode = ShadowCastingMode.Off;
    m_contourRenderer.receiveShadows = false;
    m_contourRenderer.textureMode = LineTextureMode.Stretch;
    m_contourRenderer.numCornerVertices = 2;
    m_contourRenderer.numCapVertices = 2;
    m_contourRenderer.sortingOrder = 10;
    m_contourRenderer.enabled = true;
  }

  private void RefreshContourGeometry()
  {
    if ( m_digAreaBox == null || m_contourRenderer == null )
      return;

    if ( !m_enableRuntimeVisuals )
      return;

    var halfExtents = m_digAreaBox.HalfExtents;
    if ( halfExtents.x <= 0.0f || halfExtents.z <= 0.0f )
      return;

    m_contourRenderer.SetPosition( 0, DigAreaPlaneCornerWorld( -halfExtents.x, -halfExtents.z ) );
    m_contourRenderer.SetPosition( 1, DigAreaPlaneCornerWorld( -halfExtents.x,  halfExtents.z ) );
    m_contourRenderer.SetPosition( 2, DigAreaPlaneCornerWorld(  halfExtents.x,  halfExtents.z ) );
    m_contourRenderer.SetPosition( 3, DigAreaPlaneCornerWorld(  halfExtents.x, -halfExtents.z ) );
  }

  private void ApplyCellGridVisual()
  {
    if ( !m_enableCellGridVisuals || m_cellGridRenderers == null || m_cellGridRenderers.Length == 0 ) {
      SetCellGridRenderersEnabled( false );
      return;
    }

    if ( m_runtimeCellGridMaterial == null ) {
      var gridShader = FindFirstAvailableShader(
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended Premultiply",
        "Unlit/Color" );
      if ( gridShader == null ) {
        SetCellGridRenderersEnabled( false );
        return;
      }

      m_runtimeCellGridMaterial = new Material( gridShader )
      {
        name = "DigAreaCellGridRuntimeMaterial",
        hideFlags = HideFlags.DontSave
      };
      if ( m_runtimeCellGridMaterial.HasProperty( "_Color" ) )
        m_runtimeCellGridMaterial.color = m_cellGridColor;
      m_runtimeCellGridMaterial.renderQueue = 3110;
    }

    foreach ( var renderer in m_cellGridRenderers ) {
      if ( renderer == null )
        continue;

      renderer.sharedMaterial = m_runtimeCellGridMaterial;
      renderer.loop = false;
      renderer.useWorldSpace = true;
      renderer.positionCount = 2;
      renderer.startWidth = m_cellGridWidth;
      renderer.endWidth = m_cellGridWidth;
      renderer.startColor = m_cellGridColor;
      renderer.endColor = m_cellGridColor;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      renderer.textureMode = LineTextureMode.Stretch;
      renderer.numCornerVertices = 1;
      renderer.numCapVertices = 1;
      renderer.sortingOrder = 11;
      renderer.enabled = true;
    }
  }

  private void RefreshCellGridGeometry()
  {
    if ( m_digAreaBox == null || m_cellGridRenderers == null || m_cellGridRenderers.Length < 3 )
      return;

    if ( !m_enableCellGridVisuals )
      return;

    var halfExtents = m_digAreaBox.HalfExtents;
    if ( halfExtents.x <= 0.0f || halfExtents.z <= 0.0f )
      return;

    var longAxis = halfExtents.x >= halfExtents.z ? LongAxisX : LongAxisZ;
    var halfLong = longAxis == LongAxisX ? halfExtents.x : halfExtents.z;
    var halfShort = longAxis == LongAxisX ? halfExtents.z : halfExtents.x;

    SetGridLine(
      0,
      DigAreaPlaneGridPointWorld( longAxis, -halfLong / 3.0f, -halfShort ),
      DigAreaPlaneGridPointWorld( longAxis, -halfLong / 3.0f,  halfShort ) );
    SetGridLine(
      1,
      DigAreaPlaneGridPointWorld( longAxis,  halfLong / 3.0f, -halfShort ),
      DigAreaPlaneGridPointWorld( longAxis,  halfLong / 3.0f,  halfShort ) );
    SetGridLine(
      2,
      DigAreaPlaneGridPointWorld( longAxis, -halfLong, 0.0f ),
      DigAreaPlaneGridPointWorld( longAxis,  halfLong, 0.0f ) );
  }

  private Vector3 DigAreaPlaneCornerWorld( float localX, float localZ )
  {
    var worldPoint = m_digAreaBox.transform.TransformPoint( new Vector3( localX, 0.0f, localZ ) );
    worldPoint += Vector3.up * m_contourHeightOffset;
    return worldPoint;
  }

  private Vector3 DigAreaPlaneGridPointWorld( int longAxis, float longValue, float shortValue )
  {
    var localPoint = longAxis == LongAxisX ?
                     new Vector3( longValue, 0.0f, shortValue ) :
                     new Vector3( shortValue, 0.0f, longValue );
    var worldPoint = m_digAreaBox.transform.TransformPoint( localPoint );
    worldPoint += Vector3.up * m_cellGridHeightOffset;
    return worldPoint;
  }

  private static MeshRenderer ResolveFillRenderer( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var meshRenderers = digAreaRoot.GetComponentsInChildren<MeshRenderer>( true );
    if ( meshRenderers == null || meshRenderers.Length == 0 )
      return null;

    foreach ( var meshRenderer in meshRenderers ) {
      if ( meshRenderer != null )
        return meshRenderer;
    }

    return null;
  }

  private static void SetRendererEnabled( Renderer renderer, bool enabled )
  {
    if ( renderer != null )
      renderer.enabled = enabled;
  }

  private void CacheFillRendererState()
  {
    if ( m_fillRenderer == null ) {
      m_cachedFillRenderer = null;
      m_originalFillMaterial = null;
      m_hasOriginalFillRendererState = false;
      return;
    }

    if ( m_cachedFillRenderer == m_fillRenderer && m_hasOriginalFillRendererState )
      return;

    m_cachedFillRenderer = m_fillRenderer;
    m_originalFillMaterial = m_fillRenderer.sharedMaterial;
    m_originalFillRendererEnabled = m_fillRenderer.enabled;
    m_hasOriginalFillRendererState = true;
  }

  private void RestoreFillRendererState()
  {
    if ( !m_hasOriginalFillRendererState || m_cachedFillRenderer == null )
      return;

    if ( m_cachedFillRenderer.sharedMaterial == m_runtimeFillMaterial )
      m_cachedFillRenderer.sharedMaterial = m_originalFillMaterial;
    m_cachedFillRenderer.enabled = m_originalFillRendererEnabled;

    m_cachedFillRenderer = null;
    m_originalFillMaterial = null;
    m_originalFillRendererEnabled = false;
    m_hasOriginalFillRendererState = false;
  }

  private static LineRenderer ResolveContourRenderer( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var existingContour = digAreaRoot.Find( "DigAreaContour" );
    if ( existingContour != null ) {
      var existingLineRenderer = existingContour.GetComponent<LineRenderer>();
      if ( existingLineRenderer != null )
        return existingLineRenderer;
    }

    var contourObject = new GameObject( "DigAreaContour" );
    contourObject.transform.SetParent( digAreaRoot, false );
    return contourObject.AddComponent<LineRenderer>();
  }

  private static LineRenderer[] ResolveCellGridRenderers( Transform cellGridParent )
  {
    if ( cellGridParent == null )
      return new LineRenderer[ 0 ];

    var gridRoot = cellGridParent.Find( "DigAreaCellGridRuntime" );
    if ( gridRoot == null ) {
      var gridObject = new GameObject( "DigAreaCellGridRuntime" );
      gridObject.transform.SetParent( cellGridParent, false );
      gridRoot = gridObject.transform;
    }
    gridRoot.gameObject.SetActive( true );

    var renderers = new LineRenderer[ 3 ];
    for ( var index = 0; index < renderers.Length; ++index ) {
      var childName = $"Line{index}";
      var child = gridRoot.Find( childName );
      if ( child == null ) {
        var lineObject = new GameObject( childName );
        lineObject.transform.SetParent( gridRoot, false );
        child = lineObject.transform;
      }
      child.gameObject.SetActive( true );

      var lineRenderer = child.GetComponent<LineRenderer>();
      if ( lineRenderer == null )
        lineRenderer = child.gameObject.AddComponent<LineRenderer>();
      renderers[ index ] = lineRenderer;
    }

    return renderers;
  }

  private static bool CellGridRenderersBelongTo( LineRenderer[] renderers, Transform cellGridParent )
  {
    if ( renderers == null || cellGridParent == null )
      return false;

    foreach ( var renderer in renderers ) {
      if ( renderer == null || !renderer.transform.IsChildOf( cellGridParent ) )
        return false;
    }

    return true;
  }

  private void SetCellGridRenderersEnabled( bool enabled )
  {
    if ( m_cellGridRenderers == null )
      return;

    foreach ( var renderer in m_cellGridRenderers ) {
      if ( renderer != null )
        renderer.enabled = enabled;
    }
  }

  private void SetGridLine( int index, Vector3 start, Vector3 end )
  {
    if ( m_cellGridRenderers == null || index < 0 || index >= m_cellGridRenderers.Length )
      return;

    var renderer = m_cellGridRenderers[ index ];
    if ( renderer == null )
      return;

    renderer.positionCount = 2;
    renderer.SetPosition( 0, start );
    renderer.SetPosition( 1, end );
  }

  private static int CellIndexFromNorm( float norm, int count )
  {
    if ( count <= 0 || norm < -1.0f || norm > 1.0f )
      return -1;

    var index = Mathf.FloorToInt( ( norm + 1.0f ) * 0.5f * count );
    return Mathf.Clamp( index, 0, count - 1 );
  }

  private static Shader FindFirstAvailableShader( params string[] shaderNames )
  {
    if ( shaderNames == null || shaderNames.Length == 0 )
      return null;

    foreach ( var shaderName in shaderNames ) {
      if ( string.IsNullOrWhiteSpace( shaderName ) )
        continue;

      var shader = Shader.Find( shaderName );
      if ( shader != null )
        return shader;
    }

    return null;
  }

  private static void ConfigureStandardTransparentMaterial( Material material )
  {
    if ( material == null || !material.HasProperty( "_Mode" ) )
      return;

    material.SetFloat( "_Mode", 3.0f );
    material.SetInt( "_SrcBlend", (int)BlendMode.SrcAlpha );
    material.SetInt( "_DstBlend", (int)BlendMode.OneMinusSrcAlpha );
    material.SetInt( "_ZWrite", 0 );
    material.DisableKeyword( "_ALPHATEST_ON" );
    material.EnableKeyword( "_ALPHABLEND_ON" );
    material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
  }

  private void DestroyRuntimeMaterials()
  {
    DestroyRuntimeMaterial( ref m_runtimeFillMaterial );
    DestroyRuntimeMaterial( ref m_runtimeContourMaterial );
    DestroyRuntimeMaterial( ref m_runtimeCellGridMaterial );
  }

  private static void DestroyRuntimeMaterial( ref Material material )
  {
    if ( material == null )
      return;

    if ( Application.isPlaying )
      Object.Destroy( material );
    else
      Object.DestroyImmediate( material );

    material = null;
  }
}

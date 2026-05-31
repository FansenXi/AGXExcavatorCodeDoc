using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using AGXUnity.Utils;
using UnityEngine;

public class DigAreaMeasurement : MonoBehaviour
{
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
  private string m_digTerrainName = "DigTerrain";

  [SerializeField]
  private DeformableTerrainBase m_digDeformableTerrain = null;

  [SerializeField]
  private bool m_preferLiveDeformableTerrainSurface = true;

  [SerializeField]
  private bool m_preferPhysicsTerrainSurface = true;

  [SerializeField]
  [Min( 0.01f )]
  private float m_surfaceRaycastAbovePlaneMeters = 2.0f;

  [SerializeField]
  [Min( 0.01f )]
  private float m_surfaceRaycastBelowPlaneMeters = 2.0f;

	  [SerializeField]
	  [Min( 0.0f )]
	  private float m_targetDepthMeters = 0.08f;

  [SerializeField]
  [Range( 1, 7 )]
  private int m_surfaceGridSamplesPerCellAxis = 3;

  [SerializeField]
  private bool m_enableMassAttributedRemovedDepthFallback = false;

  [SerializeField]
  [Min( 1.0f )]
  private float m_massAttributionSoilBulkDensityKgPerM3 = 1600.0f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_massAttributionMinBucketGainKg = 0.25f;

	  private readonly float[] m_surfaceBaselineDepthMeters = new float[ CellGridLongCount * CellGridShortCount ];
	  private readonly float[] m_massAttributedRemovedDepthMeters = new float[ CellGridLongCount * CellGridShortCount ];
	  private bool m_surfaceBaselineValid = false;

	  public float TargetDepthMeters => m_targetDepthMeters;
  public Transform DigAreaRoot => m_digAreaRoot;
  public Box DigAreaBox => m_digAreaBox;

  public RigidBody DigAreaRigidBody =>
    m_digAreaRoot != null ? m_digAreaRoot.GetComponent<RigidBody>() : GetComponent<RigidBody>();

  public bool TryGetDigAreaNativeBoxPosition( out Vector3 nativePosition )
  {
    nativePosition = Vector3.zero;
    if ( m_digAreaBox == null || m_digAreaBox.NativeGeometry == null )
      return false;

    nativePosition = m_digAreaBox.NativeGeometry.getPosition().ToHandedVector3();
    return true;
  }

  public bool TryGetDigAreaNativeRigidBodyPosition( out Vector3 nativePosition )
  {
    nativePosition = Vector3.zero;
    var rigidBody = DigAreaRigidBody;
    if ( rigidBody == null || rigidBody.Native == null )
      return false;

    nativePosition = rigidBody.Native.getPosition().ToHandedVector3();
    return true;
  }

  private void OnEnable()
  {
    ResolveReferences();
  }

  private void LateUpdate()
  {
    ResolveReferences();
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

    return null;
  }

  public void ResolveReferences()
  {
    if ( m_digAreaRoot == null )
      m_digAreaRoot = transform;

    if ( m_digAreaRoot == null )
      return;

    DetachManualDigAreaFromAgxRigidBodySync();
    RemoveLegacyDigAreaRuntimeVisuals();
  }

  private void DetachManualDigAreaFromAgxRigidBodySync()
  {
    var rigidBody = m_digAreaRoot != null ?
                    m_digAreaRoot.GetComponent<RigidBody>() :
                    GetComponent<RigidBody>();
    if ( rigidBody != null ) {
      rigidBody.MotionControl = agx.RigidBody.MotionControl.STATIC;
      rigidBody.LinearVelocity = Vector3.zero;
      rigidBody.AngularVelocity = Vector3.zero;
      if ( rigidBody.enabled )
        rigidBody.enabled = false;
    }

    if ( m_digAreaBox != null ) {
      m_digAreaBox.CollisionsEnabled = false;
      m_digAreaBox.EnableMassProperties = false;
      if ( m_digAreaBox.enabled )
        m_digAreaBox.enabled = false;
    }
  }

  private void RemoveLegacyDigAreaRuntimeVisuals()
  {
    RemoveChildIfPresent( m_digAreaRoot, "DigAreaContour" );
    RemoveChildIfPresent( m_digAreaRoot, "DigAreaContourRuntime" );
    RemoveChildIfPresent( m_digAreaRoot, "DigAreaCellGridRuntime" );

    if ( m_digAreaBox != null ) {
      RemoveChildIfPresent( m_digAreaBox.transform, "AGXUnity.Collide.Box_Visual" );
      RemoveChildIfPresent( m_digAreaBox.transform, "DigAreaCellGridRuntime" );
    }
  }

  private static void RemoveChildIfPresent( Transform parent, string childName )
  {
    if ( parent == null || string.IsNullOrWhiteSpace( childName ) )
      return;

    var child = parent.Find( childName );
    if ( child == null )
      return;

    if ( Application.isPlaying )
      Object.Destroy( child.gameObject );
    else
      Object.DestroyImmediate( child.gameObject );
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

    if ( !BucketTargetDistanceMeasurementUtility.TryGetMeasurementBox( bucketReference, out var bucketBox ) ||
         !bucketBox.IsValid )
      return false;

    minDistanceMeters = MeasureBoxDistanceToReferencePlanePatch( bucketBox, digAreaBox );
    bucketDepthBelowPlaneMeters = MeasureEffectiveBucketDepthBelowPlane( bucketBox );
    return minDistanceMeters >= 0.0f;
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

    if ( !BucketTargetDistanceMeasurementUtility.TryGetMeasurementBox( bucketReference, out var bucketBox ) ||
         !bucketBox.IsValid )
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
	    for ( var index = 0; index < m_surfaceBaselineDepthMeters.Length; ++index ) {
	      m_surfaceBaselineDepthMeters[ index ] = 0.0f;
	      m_massAttributedRemovedDepthMeters[ index ] = 0.0f;
	    }
	  }

	  public void AccumulateMassAttributedRemoval( CellMetrics metrics, float bucketMassDeltaKg )
	  {
	    if ( !m_enableMassAttributedRemovedDepthFallback ||
	         !metrics.GeometryAvailable ||
	         metrics.CellId < 0 ||
	         metrics.CellId >= m_massAttributedRemovedDepthMeters.Length ||
	         bucketMassDeltaKg < m_massAttributionMinBucketGainKg )
	      return;

	    var cellArea = MeasureCellAreaMetersSquared();
	    var density = Mathf.Max( 1.0f, m_massAttributionSoilBulkDensityKgPerM3 );
	    if ( cellArea <= 1.0e-5f )
	      return;

	    var removedDepthDelta = bucketMassDeltaKg / ( density * cellArea );
	    if ( float.IsNaN( removedDepthDelta ) ||
	         float.IsInfinity( removedDepthDelta ) ||
	         removedDepthDelta <= 0.0f )
	      return;

	    m_massAttributedRemovedDepthMeters[ metrics.CellId ] += removedDepthDelta;
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
	        if ( TryMeasureSurfaceDepthForCell( longIndex,
	                                            shortIndex,
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
	      if ( m_enableMassAttributedRemovedDepthFallback && validMask[ index ] > 0.5f ) {
	        removedDepth[ index ] = Mathf.Max( removedDepth[ index ],
	                                           m_massAttributedRemovedDepthMeters[ index ] );
	        surfaceDepth[ index ] = Mathf.Max( surfaceDepth[ index ],
	                                          m_surfaceBaselineDepthMeters[ index ] + removedDepth[ index ] );
	      }
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
	    return TryMeasureBucketDepthBelowSurface( bucketTipDigAreaLocalMeters,
	                                             out depthBelowLocalSurfaceMeters,
	                                             out depthBelowTargetSurfaceMeters,
	                                             out _ );
	  }

	  public bool TryMeasureBucketDepthBelowSurface( Vector3 bucketTipDigAreaLocalMeters,
	                                                out float depthBelowLocalSurfaceMeters,
	                                                out float depthBelowTargetSurfaceMeters,
	                                                out bool localSurfaceAvailable )
	  {
	    depthBelowLocalSurfaceMeters = 0.0f;
	    depthBelowTargetSurfaceMeters = 0.0f;
	    localSurfaceAvailable = false;
	    if ( m_digAreaBox == null )
	      return false;

	    if ( TryMeasureSurfaceDepthAtLocal( bucketTipDigAreaLocalMeters.x,
	                                        bucketTipDigAreaLocalMeters.z,
	                                        out _,
	                                        out var surfaceLocalYMeters ) ) {
	      depthBelowLocalSurfaceMeters = Mathf.Max( 0.0f, surfaceLocalYMeters - bucketTipDigAreaLocalMeters.y );
	      localSurfaceAvailable = true;
	    }
	    depthBelowTargetSurfaceMeters = Mathf.Max(
	      0.0f,
	      TargetSurfaceLocalY() - bucketTipDigAreaLocalMeters.y );
	    return true;
	  }

	  public bool TryMeasureBucketDepthBelowSurface( Transform bucketReference,
	                                                out float depthBelowLocalSurfaceMeters,
	                                                out float depthBelowTargetSurfaceMeters )
	  {
	    return TryMeasureBucketDepthBelowSurface( bucketReference,
	                                             out depthBelowLocalSurfaceMeters,
	                                             out depthBelowTargetSurfaceMeters,
	                                             out _ );
	  }

	  public bool TryMeasureBucketDepthBelowSurface( Transform bucketReference,
	                                                out float depthBelowLocalSurfaceMeters,
	                                                out float depthBelowTargetSurfaceMeters,
	                                                out bool localSurfaceAvailable )
	  {
	    depthBelowLocalSurfaceMeters = 0.0f;
	    depthBelowTargetSurfaceMeters = 0.0f;
	    localSurfaceAvailable = false;
	    ResolveReferences();
	    if ( bucketReference == null || m_digAreaBox == null )
	      return false;

	    if ( !BucketTargetDistanceMeasurementUtility.TryGetMeasurementBox( bucketReference,
	                                                                       out var bucketBox ) )
	      return TryMeasureBucketTipDigAreaLocal( bucketReference, out var bucketTipLocal ) &&
	             TryMeasureBucketDepthBelowSurface( bucketTipLocal,
	                                                out depthBelowLocalSurfaceMeters,
	                                                out depthBelowTargetSurfaceMeters,
	                                                out localSurfaceAvailable );

	    var hasAnyCorner = false;
	    for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
	      for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
	        for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
	          var bucketCornerWorld = bucketBox.CornerWorld( xSign, ySign, zSign );
	          var bucketCornerDigAreaLocal = m_digAreaBox.transform.InverseTransformPoint(
	            bucketCornerWorld );
	          hasAnyCorner = true;

	          if ( TryMeasureSurfaceDepthAtLocal( bucketCornerDigAreaLocal.x,
	                                              bucketCornerDigAreaLocal.z,
	                                              out _,
	                                              out var surfaceLocalYMeters ) ) {
	            depthBelowLocalSurfaceMeters = Mathf.Max(
	              depthBelowLocalSurfaceMeters,
	              surfaceLocalYMeters - bucketCornerDigAreaLocal.y );
	            localSurfaceAvailable = true;
	          }

	          depthBelowTargetSurfaceMeters = Mathf.Max(
	            depthBelowTargetSurfaceMeters,
	            TargetSurfaceLocalY() - bucketCornerDigAreaLocal.y );
	        }
	      }
	    }

	    depthBelowLocalSurfaceMeters = Mathf.Max( 0.0f, depthBelowLocalSurfaceMeters );
	    depthBelowTargetSurfaceMeters = Mathf.Max( 0.0f, depthBelowTargetSurfaceMeters );
	    return hasAnyCorner || localSurfaceAvailable;
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

	  private float MeasureCellAreaMetersSquared()
	  {
	    if ( m_digAreaBox == null )
	      return 0.0f;

	    var halfExtents = m_digAreaBox.HalfExtents;
	    var longAxis = halfExtents.x >= halfExtents.z ? LongAxisX : LongAxisZ;
	    var halfLong = longAxis == LongAxisX ? halfExtents.x : halfExtents.z;
	    var halfShort = longAxis == LongAxisX ? halfExtents.z : halfExtents.x;
	    var cellLong = 2.0f * halfLong / CellGridLongCount;
	    var cellShort = 2.0f * halfShort / CellGridShortCount;
	    return Mathf.Max( 0.0f, cellLong * cellShort );
	  }

	  private Vector3 CellSampleLocal( int longIndex,
	                                   int shortIndex,
	                                   int longSampleIndex,
	                                   int shortSampleIndex,
	                                   int sampleCount )
	  {
	    var halfExtents = m_digAreaBox != null ? m_digAreaBox.HalfExtents : Vector3.zero;
	    var longAxis = halfExtents.x >= halfExtents.z ? LongAxisX : LongAxisZ;
	    var halfLong = longAxis == LongAxisX ? halfExtents.x : halfExtents.z;
	    var halfShort = longAxis == LongAxisX ? halfExtents.z : halfExtents.x;
	    var longCellMin = -1.0f + longIndex * 2.0f / CellGridLongCount;
	    var shortCellMin = -1.0f + shortIndex * 2.0f / CellGridShortCount;
	    var longNorm = longCellMin +
	                   ( longSampleIndex + 0.5f ) * 2.0f /
	                   ( CellGridLongCount * sampleCount );
	    var shortNorm = shortCellMin +
	                    ( shortSampleIndex + 0.5f ) * 2.0f /
	                    ( CellGridShortCount * sampleCount );
	    var longValue = longNorm * halfLong;
	    var shortValue = shortNorm * halfShort;
	    return longAxis == LongAxisX ?
	           new Vector3( longValue, 0.0f, shortValue ) :
	           new Vector3( shortValue, 0.0f, longValue );
	  }

	  private bool TrySampleLiveTerrainSurfaceWorldY( Vector3 planeWorld,
	                                                 out float surfaceWorldY )
	  {
	    surfaceWorldY = 0.0f;
	    var preferredTerrain = ResolveDigDeformableTerrain();
	    if ( preferredTerrain != null &&
	         TrySampleDeformableTerrainSurfaceWorldY( preferredTerrain,
	                                                  planeWorld,
	                                                  out surfaceWorldY ) )
	      return true;

	    var terrains = Object.FindObjectsByType<DeformableTerrainBase>(
	      FindObjectsInactive.Exclude,
	      FindObjectsSortMode.None );
	    if ( terrains == null )
	      return false;

	    foreach ( var terrain in terrains ) {
	      if ( terrain == null || terrain == preferredTerrain || !terrain.isActiveAndEnabled )
	        continue;

	      if ( TrySampleDeformableTerrainSurfaceWorldY( terrain,
	                                                    planeWorld,
	                                                    out surfaceWorldY ) )
	        return true;
	    }

	    return false;
	  }

	  private DeformableTerrainBase ResolveDigDeformableTerrain()
	  {
	    if ( m_digDeformableTerrain != null && m_digDeformableTerrain.isActiveAndEnabled )
	      return m_digDeformableTerrain;

	    if ( string.IsNullOrWhiteSpace( m_digTerrainName ) )
	      return null;

	    var terrains = Object.FindObjectsByType<DeformableTerrainBase>(
	      FindObjectsInactive.Include,
	      FindObjectsSortMode.None );
	    if ( terrains == null )
	      return null;

	    foreach ( var terrain in terrains ) {
	      if ( terrain == null )
	        continue;

	      if ( terrain.name == m_digTerrainName || terrain.gameObject.name == m_digTerrainName ) {
	        m_digDeformableTerrain = terrain;
	        return m_digDeformableTerrain;
	      }
	    }

	    return null;
	  }

	  private static bool TrySampleDeformableTerrainSurfaceWorldY( DeformableTerrainBase terrain,
	                                                              Vector3 planeWorld,
	                                                              out float surfaceWorldY )
	  {
	    surfaceWorldY = 0.0f;
	    if ( terrain == null || !terrain.isActiveAndEnabled )
	      return false;

	    if ( terrain is DeformableTerrain deformableTerrain )
	      return TrySampleUnityBackedTerrainSurfaceWorldY(
	        deformableTerrain,
	        deformableTerrain.Terrain,
	        deformableTerrain.TerrainDataResolution,
	        planeWorld,
	        out surfaceWorldY );

	    if ( terrain is DeformableTerrainPager deformableTerrainPager )
	      return TrySampleUnityBackedTerrainSurfaceWorldY(
	        deformableTerrainPager,
	        deformableTerrainPager.Terrain,
	        deformableTerrainPager.TerrainDataResolution,
	        planeWorld,
	        out surfaceWorldY );

	    if ( terrain is MovableTerrain movableTerrain )
	      return TrySampleMovableTerrainSurfaceWorldY( movableTerrain,
	                                                  planeWorld,
	                                                  out surfaceWorldY );

	    return false;
	  }

	  private static bool TrySampleUnityBackedTerrainSurfaceWorldY( DeformableTerrainBase terrain,
	                                                               Terrain unityTerrain,
	                                                               int resolution,
	                                                               Vector3 planeWorld,
	                                                               out float surfaceWorldY )
	  {
	    surfaceWorldY = 0.0f;
	    if ( terrain == null ||
	         unityTerrain == null ||
	         unityTerrain.terrainData == null ||
	         resolution <= 1 )
	      return false;

	    var terrainLocal = unityTerrain.transform.InverseTransformPoint( planeWorld );
	    var terrainSize = unityTerrain.terrainData.size;
	    if ( terrainSize.x <= 0.0f || terrainSize.z <= 0.0f )
	      return false;

	    var normX = terrainLocal.x / terrainSize.x;
	    var normZ = terrainLocal.z / terrainSize.z;
	    if ( normX < 0.0f || normX > 1.0f || normZ < 0.0f || normZ > 1.0f )
	      return false;

	    var indexX = normX * ( resolution - 1 );
	    var indexZ = normZ * ( resolution - 1 );
	    if ( !TrySampleTerrainHeightMeters( terrain,
	                                        indexX,
	                                        indexZ,
	                                        resolution,
	                                        resolution,
	                                        out var nativeHeightMeters ) )
	      return false;

	    var surfaceWorld = unityTerrain.transform.TransformPoint(
	      new Vector3( terrainLocal.x, nativeHeightMeters, terrainLocal.z ) );
	    surfaceWorldY = surfaceWorld.y;
	    return true;
	  }

	  private static bool TrySampleMovableTerrainSurfaceWorldY( MovableTerrain terrain,
	                                                           Vector3 planeWorld,
	                                                           out float surfaceWorldY )
	  {
	    surfaceWorldY = 0.0f;
	    if ( terrain == null || terrain.SizeCells.x <= 1 || terrain.SizeCells.y <= 1 || terrain.ElementSize <= 0.0f )
	      return false;

	    var terrainLocal = terrain.transform.InverseTransformPoint( planeWorld );
	    var indexX = terrainLocal.x / terrain.ElementSize + 0.5f * terrain.SizeCells.x;
	    var indexZ = terrainLocal.z / terrain.ElementSize + 0.5f * terrain.SizeCells.y;
	    if ( !TrySampleTerrainHeightMeters( terrain,
	                                        indexX,
	                                        indexZ,
	                                        terrain.SizeCells.x,
	                                        terrain.SizeCells.y,
	                                        out var nativeHeightMeters ) )
	      return false;

	    var surfaceWorld = terrain.transform.TransformPoint(
	      new Vector3( terrainLocal.x, nativeHeightMeters, terrainLocal.z ) );
	    surfaceWorldY = surfaceWorld.y;
	    return true;
	  }

	  private static bool TrySampleTerrainHeightMeters( DeformableTerrainBase terrain,
	                                                   float indexX,
	                                                   float indexZ,
	                                                   int resolutionX,
	                                                   int resolutionZ,
	                                                   out float nativeHeightMeters )
	  {
	    nativeHeightMeters = 0.0f;
	    if ( terrain == null ||
	         resolutionX <= 1 ||
	         resolutionZ <= 1 ||
	         indexX < 0.0f ||
	         indexZ < 0.0f ||
	         indexX > resolutionX - 1 ||
	         indexZ > resolutionZ - 1 )
	      return false;

	    var x0 = Mathf.Clamp( Mathf.FloorToInt( indexX ), 0, resolutionX - 1 );
	    var z0 = Mathf.Clamp( Mathf.FloorToInt( indexZ ), 0, resolutionZ - 1 );
	    var x1 = Mathf.Clamp( x0 + 1, 0, resolutionX - 1 );
	    var z1 = Mathf.Clamp( z0 + 1, 0, resolutionZ - 1 );
	    var tx = Mathf.Clamp01( indexX - x0 );
	    var tz = Mathf.Clamp01( indexZ - z0 );

	    try {
	      var h00 = terrain.GetHeight( x0, z0 ) + terrain.MaximumDepth;
	      var h10 = terrain.GetHeight( x1, z0 ) + terrain.MaximumDepth;
	      var h01 = terrain.GetHeight( x0, z1 ) + terrain.MaximumDepth;
	      var h11 = terrain.GetHeight( x1, z1 ) + terrain.MaximumDepth;
	      nativeHeightMeters = Mathf.Lerp( Mathf.Lerp( h00, h10, tx ),
	                                       Mathf.Lerp( h01, h11, tx ),
	                                       tz );
	      return true;
	    }
	    catch {
	      nativeHeightMeters = 0.0f;
	      return false;
	    }
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

	    if ( m_preferPhysicsTerrainSurface &&
	         TryMeasurePhysicsTerrainSurfaceDepthAtLocal( localXMeters,
	                                                       localZMeters,
	                                                       out surfaceDepthMeters,
	                                                       out surfaceLocalYMeters ) )
	      return true;

	    if ( m_preferLiveDeformableTerrainSurface &&
	         TryMeasureLiveTerrainSurfaceDepthAtLocal( localXMeters,
	                                                   localZMeters,
	                                                   out surfaceDepthMeters,
	                                                   out surfaceLocalYMeters ) )
	      return true;

	    return TryMeasureUnityTerrainSurfaceDepthAtLocal( localXMeters,
	                                                     localZMeters,
	                                                     out surfaceDepthMeters,
	                                                     out surfaceLocalYMeters );
	  }

	  private bool TryMeasurePhysicsTerrainSurfaceDepthAtLocal( float localXMeters,
	                                                           float localZMeters,
	                                                           out float surfaceDepthMeters,
	                                                           out float surfaceLocalYMeters )
	  {
	    surfaceDepthMeters = 0.0f;
	    surfaceLocalYMeters = 0.0f;
	    if ( m_digAreaBox == null )
	      return false;

	    var digAreaTransform = m_digAreaBox.transform;
	    var planeWorld = digAreaTransform.TransformPoint(
	      new Vector3( localXMeters, 0.0f, localZMeters ) );
	    var up = digAreaTransform.up;
	    if ( up.sqrMagnitude <= 1.0e-6f )
	      return false;
	    up.Normalize();

	    var raycastAbove = Mathf.Max( 0.01f, m_surfaceRaycastAbovePlaneMeters );
	    var raycastBelow = Mathf.Max( 0.01f, m_surfaceRaycastBelowPlaneMeters );
	    var origin = planeWorld + up * raycastAbove;
	    var hits = Physics.RaycastAll(
	      origin,
	      -up,
	      raycastAbove + raycastBelow,
	      Physics.DefaultRaycastLayers,
	      QueryTriggerInteraction.Ignore );
	    if ( hits == null || hits.Length == 0 )
	      return false;

	    var hasHit = false;
	    var bestLocalY = float.NegativeInfinity;
	    for ( var hitIndex = 0; hitIndex < hits.Length; ++hitIndex ) {
	      var hit = hits[ hitIndex ];
	      if ( hit.collider == null || !IsDigTerrainSurfaceCollider( hit.collider ) )
	        continue;

	      var hitLocal = digAreaTransform.InverseTransformPoint( hit.point );
	      if ( hasHit && hitLocal.y <= bestLocalY )
	        continue;

	      bestLocalY = hitLocal.y;
	      hasHit = true;
	    }

	    if ( !hasHit )
	      return false;

	    surfaceLocalYMeters = bestLocalY;
	    surfaceDepthMeters = DepthBelowReferencePlane( surfaceLocalYMeters );
	    return true;
	  }

	  private bool IsDigTerrainSurfaceCollider( Collider candidate )
	  {
	    if ( candidate == null )
	      return false;

	    if ( !string.IsNullOrWhiteSpace( m_digTerrainName ) ) {
	      var transformToCheck = candidate.transform;
	      while ( transformToCheck != null ) {
	        if ( transformToCheck.name == m_digTerrainName ||
	             transformToCheck.gameObject.name == m_digTerrainName )
	          return true;
	        transformToCheck = transformToCheck.parent;
	      }
	    }

	    if ( candidate.GetComponentInParent<DeformableTerrainBase>() != null )
	      return true;

	    return candidate is TerrainCollider && string.IsNullOrWhiteSpace( m_digTerrainName );
	  }

	  private bool TryMeasureSurfaceDepthForCell( int longIndex,
	                                             int shortIndex,
	                                             out float surfaceDepthMeters,
	                                             out float surfaceLocalYMeters )
	  {
	    surfaceDepthMeters = 0.0f;
	    surfaceLocalYMeters = 0.0f;
	    var sampleCount = Mathf.Max( 1, m_surfaceGridSamplesPerCellAxis );
	    if ( sampleCount <= 1 ) {
	      var localPoint = CellCenterLocal( longIndex, shortIndex );
	      return TryMeasureSurfaceDepthAtLocal( localPoint.x,
	                                           localPoint.z,
	                                           out surfaceDepthMeters,
	                                           out surfaceLocalYMeters );
	    }

	    var depthSum = 0.0f;
	    var localYSum = 0.0f;
	    var validCount = 0;
	    for ( var longSample = 0; longSample < sampleCount; ++longSample ) {
	      for ( var shortSample = 0; shortSample < sampleCount; ++shortSample ) {
	        var localPoint = CellSampleLocal( longIndex,
	                                          shortIndex,
	                                          longSample,
	                                          shortSample,
	                                          sampleCount );
	        if ( !TryMeasureSurfaceDepthAtLocal( localPoint.x,
	                                             localPoint.z,
	                                             out var sampleDepthMeters,
	                                             out var sampleLocalYMeters ) )
	          continue;

	        depthSum += sampleDepthMeters;
	        localYSum += sampleLocalYMeters;
	        validCount++;
	      }
	    }

	    if ( validCount <= 0 )
	      return false;

	    surfaceDepthMeters = depthSum / validCount;
	    surfaceLocalYMeters = localYSum / validCount;
	    return true;
	  }

	  private bool TryMeasureLiveTerrainSurfaceDepthAtLocal( float localXMeters,
	                                                        float localZMeters,
	                                                        out float surfaceDepthMeters,
	                                                        out float surfaceLocalYMeters )
	  {
	    surfaceDepthMeters = 0.0f;
	    surfaceLocalYMeters = 0.0f;
	    if ( m_digAreaBox == null )
	      return false;

	    var planeWorld = m_digAreaBox.transform.TransformPoint(
	      new Vector3( localXMeters, 0.0f, localZMeters ) );
	    if ( !TrySampleLiveTerrainSurfaceWorldY( planeWorld, out var surfaceWorldY ) )
	      return false;

	    var surfaceWorld = new Vector3( planeWorld.x, surfaceWorldY, planeWorld.z );
	    surfaceLocalYMeters = m_digAreaBox.transform.InverseTransformPoint( surfaceWorld ).y;
	    surfaceDepthMeters = DepthBelowReferencePlane( surfaceLocalYMeters );
	    return true;
	  }

	  private bool TryMeasureUnityTerrainSurfaceDepthAtLocal( float localXMeters,
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
	    surfaceDepthMeters = DepthBelowReferencePlane( surfaceLocalYMeters );
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

    if ( float.IsPositiveInfinity( minBucketDigAreaLocalY ) )
      return 0.0f;

    return DepthBelowReferencePlane( minBucketDigAreaLocalY );
  }

  private float ReferencePlaneLocalY()
  {
    return m_digAreaBox != null ? -m_digAreaBox.HalfExtents.y : 0.0f;
  }

  private float TargetSurfaceLocalY()
  {
    return ReferencePlaneLocalY() - m_targetDepthMeters;
  }

  private float DepthBelowReferencePlane( float digAreaLocalY )
  {
    return Mathf.Max( 0.0f, ReferencePlaneLocalY() - digAreaLocalY );
  }

  private float MeasureBoxDistanceToReferencePlanePatch( OrientedMeasurementBox bucketBox,
                                                         OrientedMeasurementBox digAreaBox )
  {
    if ( !bucketBox.IsValid || !digAreaBox.IsValid )
      return -1.0f;

    var bucketCorners = BuildBoxCornersWorld( bucketBox );
    var referenceCorners = BuildReferencePlaneCornersWorld( digAreaBox );
    var minDistanceSq = float.PositiveInfinity;

    foreach ( var bucketCorner in bucketCorners )
      minDistanceSq = Mathf.Min(
        minDistanceSq,
        MeasurePointDistanceSqToReferencePlanePatch( bucketCorner, digAreaBox ) );

    foreach ( var referenceCorner in referenceCorners )
      minDistanceSq = Mathf.Min(
        minDistanceSq,
        MeasurePointDistanceSqToBox( referenceCorner, bucketBox ) );

    AccumulateBoxEdgeToReferencePlaneEdgeDistance( bucketCorners,
                                                  referenceCorners,
                                                  bucketBox,
                                                  digAreaBox,
                                                  ref minDistanceSq );

    return float.IsPositiveInfinity( minDistanceSq ) ?
      -1.0f :
      Mathf.Sqrt( Mathf.Max( 0.0f, minDistanceSq ) );
  }

  private float MeasurePointDistanceSqToReferencePlanePatch( Vector3 pointWorld,
                                                             OrientedMeasurementBox digAreaBox )
  {
    if ( digAreaBox.Frame == null )
      return float.PositiveInfinity;

    var local = digAreaBox.Frame.InverseTransformPoint( pointWorld ) - digAreaBox.CenterLocal;
    var outsideX = Mathf.Max( Mathf.Abs( local.x ) - digAreaBox.HalfExtents.x, 0.0f );
    var outsideZ = Mathf.Max( Mathf.Abs( local.z ) - digAreaBox.HalfExtents.z, 0.0f );
    var deltaY = local.y - ReferencePlaneLocalY();
    return outsideX * outsideX + deltaY * deltaY + outsideZ * outsideZ;
  }

  private static float MeasurePointDistanceSqToBox( Vector3 pointWorld,
                                                    OrientedMeasurementBox box )
  {
    if ( !box.IsValid )
      return float.PositiveInfinity;

    var closestWorld = box.ClosestPointWorld( pointWorld );
    return ( pointWorld - closestWorld ).sqrMagnitude;
  }

  private Vector3[] BuildReferencePlaneCornersWorld( OrientedMeasurementBox digAreaBox )
  {
    var localY = ReferencePlaneLocalY();
    return new[]
    {
      digAreaBox.Frame.TransformPoint(
        digAreaBox.CenterLocal + new Vector3( -digAreaBox.HalfExtents.x, localY, -digAreaBox.HalfExtents.z ) ),
      digAreaBox.Frame.TransformPoint(
        digAreaBox.CenterLocal + new Vector3( -digAreaBox.HalfExtents.x, localY,  digAreaBox.HalfExtents.z ) ),
      digAreaBox.Frame.TransformPoint(
        digAreaBox.CenterLocal + new Vector3(  digAreaBox.HalfExtents.x, localY,  digAreaBox.HalfExtents.z ) ),
      digAreaBox.Frame.TransformPoint(
        digAreaBox.CenterLocal + new Vector3(  digAreaBox.HalfExtents.x, localY, -digAreaBox.HalfExtents.z ) )
    };
  }

  private static Vector3[] BuildBoxCornersWorld( OrientedMeasurementBox box )
  {
    return new[]
    {
      box.CornerWorld( -1, -1, -1 ),
      box.CornerWorld( -1, -1,  1 ),
      box.CornerWorld( -1,  1, -1 ),
      box.CornerWorld( -1,  1,  1 ),
      box.CornerWorld(  1, -1, -1 ),
      box.CornerWorld(  1, -1,  1 ),
      box.CornerWorld(  1,  1, -1 ),
      box.CornerWorld(  1,  1,  1 )
    };
  }

  private static void AccumulateBoxEdgeToReferencePlaneEdgeDistance( Vector3[] boxCorners,
                                                                     Vector3[] referenceCorners,
                                                                     OrientedMeasurementBox bucketBox,
                                                                     OrientedMeasurementBox digAreaBox,
                                                                     ref float minDistanceSq )
  {
    var boxEdges = new[,]
    {
      { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 },
      { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 }
    };
    var referenceEdges = new[,]
    {
      { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }
    };

    for ( var referenceEdgeIndex = 0; referenceEdgeIndex < referenceEdges.GetLength( 0 ); ++referenceEdgeIndex ) {
      var referenceStart = referenceCorners[ referenceEdges[ referenceEdgeIndex, 0 ] ];
      var referenceEnd = referenceCorners[ referenceEdges[ referenceEdgeIndex, 1 ] ];
      if ( TrySegmentBoxIntersection( referenceStart, referenceEnd, bucketBox ) ) {
        minDistanceSq = 0.0f;
        return;
      }
    }

    for ( var boxEdgeIndex = 0; boxEdgeIndex < boxEdges.GetLength( 0 ); ++boxEdgeIndex ) {
      var boxStart = boxCorners[ boxEdges[ boxEdgeIndex, 0 ] ];
      var boxEnd = boxCorners[ boxEdges[ boxEdgeIndex, 1 ] ];
      if ( TrySegmentReferencePlanePatchIntersection( boxStart, boxEnd, digAreaBox ) ) {
        minDistanceSq = 0.0f;
        return;
      }

      for ( var referenceEdgeIndex = 0; referenceEdgeIndex < referenceEdges.GetLength( 0 ); ++referenceEdgeIndex ) {
        var referenceStart = referenceCorners[ referenceEdges[ referenceEdgeIndex, 0 ] ];
        var referenceEnd = referenceCorners[ referenceEdges[ referenceEdgeIndex, 1 ] ];
        minDistanceSq = Mathf.Min(
          minDistanceSq,
          MeasureSegmentSegmentDistanceSq( boxStart, boxEnd, referenceStart, referenceEnd ) );
      }
    }
  }

  private static bool TrySegmentBoxIntersection( Vector3 segmentStart,
                                                 Vector3 segmentEnd,
                                                 OrientedMeasurementBox box )
  {
    if ( !box.IsValid )
      return false;

    var startLocal = box.Frame.InverseTransformPoint( segmentStart ) - box.CenterLocal;
    var endLocal = box.Frame.InverseTransformPoint( segmentEnd ) - box.CenterLocal;
    var delta = endLocal - startLocal;
    var tMin = 0.0f;
    var tMax = 1.0f;

    return UpdateSegmentBoxSlab( startLocal.x, delta.x, box.HalfExtents.x, ref tMin, ref tMax ) &&
           UpdateSegmentBoxSlab( startLocal.y, delta.y, box.HalfExtents.y, ref tMin, ref tMax ) &&
           UpdateSegmentBoxSlab( startLocal.z, delta.z, box.HalfExtents.z, ref tMin, ref tMax );
  }

  private static bool UpdateSegmentBoxSlab( float start,
                                            float delta,
                                            float halfExtent,
                                            ref float tMin,
                                            ref float tMax )
  {
    if ( Mathf.Abs( delta ) <= Mathf.Epsilon )
      return start >= -halfExtent && start <= halfExtent;

    var inverseDelta = 1.0f / delta;
    var t1 = ( -halfExtent - start ) * inverseDelta;
    var t2 = ( halfExtent - start ) * inverseDelta;
    if ( t1 > t2 ) {
      var swap = t1;
      t1 = t2;
      t2 = swap;
    }

    tMin = Mathf.Max( tMin, t1 );
    tMax = Mathf.Min( tMax, t2 );
    return tMin <= tMax;
  }

  private static bool TrySegmentReferencePlanePatchIntersection( Vector3 segmentStart,
                                                                 Vector3 segmentEnd,
                                                                 OrientedMeasurementBox digAreaBox )
  {
    if ( !digAreaBox.IsValid )
      return false;

    var startLocal = digAreaBox.Frame.InverseTransformPoint( segmentStart ) - digAreaBox.CenterLocal;
    var endLocal = digAreaBox.Frame.InverseTransformPoint( segmentEnd ) - digAreaBox.CenterLocal;
    var planeLocalY = -digAreaBox.HalfExtents.y;
    var startDistance = startLocal.y - planeLocalY;
    var endDistance = endLocal.y - planeLocalY;
    if ( Mathf.Abs( startDistance ) <= Mathf.Epsilon &&
         IsInsideReferencePlanePatch( startLocal, digAreaBox ) )
      return true;
    if ( Mathf.Abs( endDistance ) <= Mathf.Epsilon &&
         IsInsideReferencePlanePatch( endLocal, digAreaBox ) )
      return true;
    if ( startDistance * endDistance > 0.0f )
      return false;

    var denominator = startLocal.y - endLocal.y;
    if ( Mathf.Abs( denominator ) <= Mathf.Epsilon )
      return false;

    var t = ( startLocal.y - planeLocalY ) / denominator;
    if ( t < 0.0f || t > 1.0f )
      return false;

    var intersectionLocal = Vector3.Lerp( startLocal, endLocal, t );
    return IsInsideReferencePlanePatch( intersectionLocal, digAreaBox );
  }

  private static bool IsInsideReferencePlanePatch( Vector3 digAreaLocalPoint,
                                                   OrientedMeasurementBox digAreaBox )
  {
    return Mathf.Abs( digAreaLocalPoint.x ) <= digAreaBox.HalfExtents.x &&
           Mathf.Abs( digAreaLocalPoint.z ) <= digAreaBox.HalfExtents.z;
  }

  private static float MeasureSegmentSegmentDistanceSq( Vector3 firstStart,
                                                        Vector3 firstEnd,
                                                        Vector3 secondStart,
                                                        Vector3 secondEnd )
  {
    var firstDelta = firstEnd - firstStart;
    var secondDelta = secondEnd - secondStart;
    var startDelta = firstStart - secondStart;
    var firstLengthSq = Vector3.Dot( firstDelta, firstDelta );
    var secondLengthSq = Vector3.Dot( secondDelta, secondDelta );
    var secondStartProjection = Vector3.Dot( secondDelta, startDelta );
    float firstT;
    float secondT;

    if ( firstLengthSq <= Mathf.Epsilon && secondLengthSq <= Mathf.Epsilon )
      return ( firstStart - secondStart ).sqrMagnitude;

    if ( firstLengthSq <= Mathf.Epsilon ) {
      firstT = 0.0f;
      secondT = Mathf.Clamp01( secondStartProjection / secondLengthSq );
    }
    else {
      var firstStartProjection = Vector3.Dot( firstDelta, startDelta );
      if ( secondLengthSq <= Mathf.Epsilon ) {
        secondT = 0.0f;
        firstT = Mathf.Clamp01( -firstStartProjection / firstLengthSq );
      }
      else {
        var crossProjection = Vector3.Dot( firstDelta, secondDelta );
        var denominator = firstLengthSq * secondLengthSq - crossProjection * crossProjection;
        firstT = denominator != 0.0f ?
                 Mathf.Clamp01( ( crossProjection * secondStartProjection - firstStartProjection * secondLengthSq ) / denominator ) :
                 0.0f;
        secondT = ( crossProjection * firstT + secondStartProjection ) / secondLengthSq;
        if ( secondT < 0.0f ) {
          secondT = 0.0f;
          firstT = Mathf.Clamp01( -firstStartProjection / firstLengthSq );
        }
        else if ( secondT > 1.0f ) {
          secondT = 1.0f;
          firstT = Mathf.Clamp01( ( crossProjection - firstStartProjection ) / firstLengthSq );
        }
      }
    }

    var firstClosest = firstStart + firstDelta * firstT;
    var secondClosest = secondStart + secondDelta * secondT;
    return ( firstClosest - secondClosest ).sqrMagnitude;
  }

	  private bool TryGetBucketReferencePointWorld( Transform bucketReference, out Vector3 referenceWorld )
	  {
	    referenceWorld = Vector3.zero;
    return TrySelectLowestMeasurementBoxPoint( bucketReference, out referenceWorld );
  }

  private bool TrySelectLowestMeasurementBoxPoint( Transform bucketReference,
                                                   out Vector3 referenceWorld )
  {
    referenceWorld = Vector3.zero;
    if ( bucketReference == null || m_digAreaBox == null )
      return false;

    if ( BucketTargetDistanceMeasurementUtility.TryGetMeasurementBox( bucketReference,
	                                                                      out var bucketBox ) &&
	         bucketBox.IsValid )
	      return TrySelectLowestMeasurementBoxPoint( bucketBox, out referenceWorld );
	
    return false;
  }

  private bool TrySelectLowestMeasurementBoxPoint( OrientedMeasurementBox bucketBox,
                                                   out Vector3 referenceWorld )
  {
    referenceWorld = Vector3.zero;
    if ( !bucketBox.IsValid || m_digAreaBox == null )
      return false;

    var hasPoint = false;
    var bestLocalY = float.PositiveInfinity;
    for ( var xIndex = -1; xIndex <= 1; xIndex++ ) {
      for ( var yIndex = -1; yIndex <= 1; yIndex++ ) {
        for ( var zIndex = -1; zIndex <= 1; zIndex++ ) {
          var sampleWorld = bucketBox.SamplePointWorld( xIndex,
                                                        yIndex,
                                                        zIndex );
          var sampleLocal = m_digAreaBox.transform.InverseTransformPoint( sampleWorld );
          if ( hasPoint && sampleLocal.y >= bestLocalY )
            continue;

          hasPoint = true;
          bestLocalY = sampleLocal.y;
          referenceWorld = sampleWorld;
        }
      }
    }

    return hasPoint;
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

  private static int CellIndexFromNorm( float norm, int count )
  {
    if ( count <= 0 || norm < -1.0f || norm > 1.0f )
      return -1;

    var index = Mathf.FloorToInt( ( norm + 1.0f ) * 0.5f * count );
    return Mathf.Clamp( index, 0, count - 1 );
  }

}

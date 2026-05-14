using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity.Utils;
using System.Collections.Generic;
using UnityEngine;

public class TerrainParticleBoxMassSensor : TargetMassSensorBase
{
  [SerializeField]
  private string m_targetName = "DumpArea";

  [SerializeField]
  public DeformableTerrain m_terrain = null;

  [SerializeField]
  public Box m_sensorFootprint = null;

  [SerializeField]
  [Min( 0.05f )]
  private float m_measurementHeight = 1.5f;

  [SerializeField]
  private Vector3 m_localCenterOffset = Vector3.zero;

  [SerializeField]
  private Vector3 m_additionalHalfExtents = Vector3.zero;

  [SerializeField]
  [Min( 0.01f )]
  private float m_updateIntervalSeconds = 0.05f;

  [SerializeField]
  private bool m_includeHandledAsParticleRigidBodies = true;

  [SerializeField]
  [Min( 0.0f )]
  private float m_handledAsParticleRigidBodyPadding = 0.1f;

  [SerializeField]
  private bool m_drawSensorBoundsGizmo = true;

  [SerializeField]
  private bool m_useSettledCompactorMassForDepositedMass = true;

  [SerializeField]
  private DumpParticleStaticTerrainCompactor[] m_settledMassCompactors = null;

  [SerializeField]
  private bool m_autoDiscoverSettledMassCompactors = true;

  [SerializeField]
  private bool m_accumulateBucketUnloadNearTarget = false;

  [SerializeField]
  private ExcavationMassTracker m_bucketMassTracker = null;

  [SerializeField]
  [Min( 0.0f )]
  private float m_bucketUnloadTargetDistanceTolerance = 0.75f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_dumpClearanceHorizontalToleranceMeters = 0.0f;

  [SerializeField]
  private bool m_accumulateEnteredParticleMass = true;

  [SerializeField]
  private bool m_useStaticTerrainHeightForRetainedMass = false;

  [SerializeField]
  private DeformableTerrainBase[] m_staticMassTerrains = null;

  [SerializeField]
  private bool m_autoDiscoverStaticMassTerrains = true;

  [SerializeField]
  [Min( 0.0f )]
  private float m_staticTerrainBulkDensity = 1600.0f;

  private float m_massInBox = 0.0f;
  private float m_depositedMass = 0.0f;
  private float m_enteredParticleMass = 0.0f;
  private float m_bucketUnloadNearTargetMass = 0.0f;
  private float m_previousBucketMass = 0.0f;
  private bool m_hasPreviousBucketMass = false;
  private float m_resetBaselineLiveTerrainMassInBox = 0.0f;
  private float m_resetBaselineHandledAsParticleMassInBox = 0.0f;
  private float m_resetBaselineMassInBox = 0.0f;
  private float m_resetBaselineSettledCompactorMass = 0.0f;
  private float m_nextSampleTime = 0.0f;
  private readonly HashSet<uint> m_enteredParticleHashes = new HashSet<uint>();
  private readonly HashSet<uint> m_activeParticleHashes = new HashSet<uint>();
  private readonly List<StaticTerrainBaseline> m_staticTerrainBaselines = new List<StaticTerrainBaseline>();
  private Transform m_cachedTargetDistanceBoundsTransform = null;
  private Bounds m_cachedTargetDistanceLocalBounds = default;
  private bool m_hasCachedTargetDistanceLocalBounds = false;

  private sealed class StaticTerrainBaseline
  {
    public DeformableTerrainBase Terrain = null;
    public Terrain UnityTerrain = null;
    public TerrainData TerrainData = null;
    public float[,] Heights = null;
    public int Resolution = 0;
    public Vector3 Size = Vector3.zero;
  }

  public override string TargetName => string.IsNullOrWhiteSpace( m_targetName ) ? gameObject.name : m_targetName;
  public override float MassInBox => m_massInBox;
  public override float DepositedMass => m_depositedMass;
  public override float TargetDumpClearanceHorizontalToleranceMeters => Mathf.Max( 0.0f, m_dumpClearanceHorizontalToleranceMeters );
  public override Shape[] GetCollisionShapes()
  {
    var shapes = GetComponentsInChildren<Shape>( true );
    if ( shapes == null || shapes.Length == 0 )
      return System.Array.Empty<Shape>();

    var filteredShapes = new System.Collections.Generic.List<Shape>( shapes.Length );
    foreach ( var shape in shapes ) {
      if ( shape == null || !shape.CollisionsEnabled )
        continue;

      if ( !filteredShapes.Contains( shape ) )
        filteredShapes.Add( shape );
    }

    return filteredShapes.ToArray();
  }

  public override bool TryGetMeasurementVolume( out Transform measurementFrame,
                                                out Vector3 measurementCenterLocal,
                                                out Vector3 measurementHalfExtents )
  {
    ResolveReferences();

    measurementFrame = transform;
    measurementCenterLocal = GetMeasurementCenterLocal();
    measurementHalfExtents = GetMeasurementHalfExtents();

    return measurementFrame != null &&
           measurementHalfExtents.x > 0.0f &&
           measurementHalfExtents.y > 0.0f &&
           measurementHalfExtents.z > 0.0f;
  }

  public override bool TryGetTargetDistanceVolume( out Transform measurementFrame,
                                                   out Vector3 measurementCenterLocal,
                                                   out Vector3 measurementHalfExtents )
  {
    ResolveReferences();

    measurementFrame = transform;
    measurementCenterLocal = Vector3.zero;
    measurementHalfExtents = Vector3.zero;
    if ( measurementFrame == null )
      return false;

    if ( m_cachedTargetDistanceBoundsTransform != measurementFrame ) {
      m_cachedTargetDistanceBoundsTransform = measurementFrame;
      m_hasCachedTargetDistanceLocalBounds =
        TargetDistanceVolumeUtility.TryCalculateLocalBoxBounds( measurementFrame, (Transform)null, out m_cachedTargetDistanceLocalBounds );
    }

    if ( !m_hasCachedTargetDistanceLocalBounds )
      return TryGetMeasurementVolume( out measurementFrame, out measurementCenterLocal, out measurementHalfExtents );

    measurementCenterLocal = m_cachedTargetDistanceLocalBounds.center;
    measurementHalfExtents = new Vector3(
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.x ),
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.y ),
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.z ) );
    return true;
  }

  public override bool TryGetTargetClearanceVolume( out Transform measurementFrame,
                                                    out Vector3 measurementCenterLocal,
                                                    out Vector3 measurementHalfExtents )
  {
    ResolveReferences();

    if ( m_sensorFootprint != null ) {
      measurementFrame = m_sensorFootprint.transform;
      measurementCenterLocal = Vector3.zero;
      measurementHalfExtents = m_sensorFootprint.HalfExtents;
      return measurementFrame != null &&
             measurementHalfExtents.x > 0.0f &&
             measurementHalfExtents.y > 0.0f &&
             measurementHalfExtents.z > 0.0f;
    }

    return TryGetMeasurementVolume( out measurementFrame,
                                    out measurementCenterLocal,
                                    out measurementHalfExtents );
  }

  protected override bool Initialize()
  {
    ResolveReferences();
    ResetMeasurements();

    return base.Initialize();
  }

  public override void ResetMeasurements()
  {
    ResolveReferences();

    m_enteredParticleHashes.Clear();
    m_activeParticleHashes.Clear();
    m_enteredParticleMass = 0.0f;
    m_bucketUnloadNearTargetMass = 0.0f;
    m_previousBucketMass = ReadBucketMass();
    m_hasPreviousBucketMass = true;
    var liveTerrainMassInBox = ReadLiveTerrainParticleMassInBox();
    var liveHandledAsParticleMassInBox = ReadLiveHandledAsParticleMassInBox();
    m_resetBaselineLiveTerrainMassInBox = liveTerrainMassInBox;
    m_resetBaselineHandledAsParticleMassInBox = liveHandledAsParticleMassInBox;
    m_resetBaselineMassInBox = liveTerrainMassInBox + liveHandledAsParticleMassInBox;
    m_resetBaselineSettledCompactorMass = ReadSettledCompactorMass();
    CaptureStaticTerrainBaselines();
    PrimeEnteredParticleHashes();
    m_massInBox = 0.0f;
    m_depositedMass = 0.0f;
    m_nextSampleTime = Time.time + Mathf.Max( 0.01f, m_updateIntervalSeconds );
  }

  private void Update()
  {
    if ( !Application.isPlaying )
      return;

    AccumulateEnteredParticleMass();

    if ( Time.time + 1.0e-5f < m_nextSampleTime )
      return;

    SampleMeasurements();
    m_nextSampleTime = Time.time + Mathf.Max( 0.01f, m_updateIntervalSeconds );
  }

  private void OnDrawGizmosSelected()
  {
    if ( !m_drawSensorBoundsGizmo )
      return;

    ResolveReferences();

    if ( !TryGetMeasurementVolume( out var measurementFrame, out var measurementCenterLocal, out var measurementHalfExtents ) )
      return;

    var previousColor = Gizmos.color;
    var previousMatrix = Gizmos.matrix;

    Gizmos.color = new Color( 0.95f, 0.78f, 0.15f, 1.0f );
    Gizmos.matrix = measurementFrame.localToWorldMatrix;
    Gizmos.DrawWireCube( measurementCenterLocal, 2.0f * measurementHalfExtents );

    Gizmos.matrix = previousMatrix;
    Gizmos.color = previousColor;
  }

  private void SampleMeasurements()
  {
    AccumulateEnteredParticleMass();
    AccumulateBucketUnloadNearTargetMass();

    var liveHandledAsParticleMassInBox = ReadLiveHandledAsParticleMassInBox();
    var liveHandledAsParticleDepositedMass = Mathf.Max( 0.0f, liveHandledAsParticleMassInBox - m_resetBaselineHandledAsParticleMassInBox );
    var targetTerrainMass = m_accumulateEnteredParticleMass ?
                              Mathf.Max( 0.0f, m_enteredParticleMass ) :
                              ReadRetainedTerrainMassFallback();
    var deliveredMass = targetTerrainMass + liveHandledAsParticleDepositedMass;

    m_depositedMass = deliveredMass;
    m_massInBox = deliveredMass;
  }

  private float ReadRetainedTerrainMassFallback()
  {
    var liveTerrainMassInBox = ReadLiveTerrainParticleMassInBox();
    var liveTerrainDepositedMass = Mathf.Max( 0.0f, liveTerrainMassInBox - m_resetBaselineLiveTerrainMassInBox );
    var fallbackStaticTerrainMass = ReadStaticTerrainMassFromBaseline();
    var settledCompactorMass = 0.0f;
    var hasSettledCompactorMass = TryReadNormalizedSettledCompactorMass( out settledCompactorMass );
    var staticTerrainMass = hasSettledCompactorMass ?
                              settledCompactorMass :
                              fallbackStaticTerrainMass;
    return Mathf.Max( liveTerrainDepositedMass, staticTerrainMass );
  }

  private bool TryReadNormalizedSettledCompactorMass( out float settledCompactorMass )
  {
    settledCompactorMass = 0.0f;
    if ( !m_useSettledCompactorMassForDepositedMass )
      return false;

    if ( !HasSettledMassCompactors() )
      return false;

    settledCompactorMass = Mathf.Max( 0.0f, ReadSettledCompactorMass() - m_resetBaselineSettledCompactorMass );
    return true;
  }

  private float ReadSettledCompactorMass()
  {
    ResolveSettledMassCompactors();
    if ( !HasSettledMassCompactors() )
      return 0.0f;

    var totalSettledMass = 0.0f;
    foreach ( var compactor in m_settledMassCompactors ) {
      if ( compactor == null || !compactor.isActiveAndEnabled )
        continue;

      totalSettledMass += Mathf.Max( 0.0f, compactor.TotalSettledMass );
    }

    return totalSettledMass;
  }

  private float ReadLiveMassInBox()
  {
    return ReadLiveTerrainParticleMassInBox() + ReadLiveHandledAsParticleMassInBox();
  }

  private float ReadLiveTerrainParticleMassInBox()
  {
    if ( !TryGetMeasurementBox( out var measurementCenterLocal, out var halfExtents ) )
      return 0.0f;

    return DeformableTerrainParticleMassUtility.SumMassInOrientedBox( transform,
                                                                      measurementCenterLocal,
                                                                      halfExtents,
                                                                      m_terrain );
  }

  private float ReadLiveHandledAsParticleMassInBox()
  {
    if ( !m_includeHandledAsParticleRigidBodies )
      return 0.0f;

    if ( !TryGetMeasurementBox( out var measurementCenterLocal, out var halfExtents ) )
      return 0.0f;

    return ReadHandledAsParticleRigidBodyMassInBox( measurementCenterLocal, halfExtents );
  }

  private bool TryGetMeasurementBox( out Vector3 measurementCenterLocal,
                                     out Vector3 halfExtents )
  {
    ResolveReferences();

    measurementCenterLocal = GetMeasurementCenterLocal();
    halfExtents = GetMeasurementHalfExtents();
    return halfExtents.x > 0.0f && halfExtents.y > 0.0f && halfExtents.z > 0.0f;
  }

  private void AccumulateEnteredParticleMass()
  {
    if ( !m_accumulateEnteredParticleMass )
      return;

    var halfExtents = GetMeasurementHalfExtents();
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return;

    DeformableTerrainParticleMassUtility.CollectActiveParticleHashes( m_terrain, m_activeParticleHashes );
    m_enteredParticleHashes.IntersectWith( m_activeParticleHashes );

    m_enteredParticleMass += DeformableTerrainParticleMassUtility.SumNewParticleMassInOrientedBox(
      transform,
      GetMeasurementCenterLocal(),
      halfExtents,
      m_terrain,
      m_enteredParticleHashes );
  }

  private void AccumulateBucketUnloadNearTargetMass()
  {
    if ( !m_accumulateBucketUnloadNearTarget )
      return;

    var currentBucketMass = ReadBucketMass();
    if ( !m_hasPreviousBucketMass ) {
      m_previousBucketMass = currentBucketMass;
      m_hasPreviousBucketMass = true;
      return;
    }

    var unloadedMass = Mathf.Max( 0.0f, m_previousBucketMass - currentBucketMass );
    if ( unloadedMass > 0.0f && IsBucketNearTarget() )
      m_bucketUnloadNearTargetMass += unloadedMass;

    m_previousBucketMass = currentBucketMass;
  }

  private float ReadBucketMass()
  {
    ResolveBucketMassTracker();
    return m_bucketMassTracker != null ? Mathf.Max( 0.0f, m_bucketMassTracker.MassInBucket ) : 0.0f;
  }

  private void ResolveBucketMassTracker()
  {
    if ( ExcavatorRigLocator.IsSelectable( m_bucketMassTracker ) )
      return;

    m_bucketMassTracker = ExcavatorRigLocator.ResolveComponent( this, m_bucketMassTracker );
  }

  private bool IsBucketNearTarget()
  {
    ResolveBucketMassTracker();
    if ( m_bucketMassTracker == null )
      return false;

    if ( !BucketTargetDistanceMeasurementUtility.TryMeasureTargetGeometry(
           m_bucketMassTracker.BucketMeasurementFrame,
           this,
           out var metrics ) ||
         !metrics.IsValid )
      return false;

    if ( metrics.DumpClearanceOkMask >= 0.5f ||
         metrics.BucketOverTargetFootprintMask >= 0.5f )
      return true;

    return metrics.BucketHeightAboveTargetRimMeters >= 0.0f &&
           metrics.BucketDumpAreaFootprintOutsideDistanceMeters >= 0.0f &&
           metrics.BucketDumpAreaFootprintOutsideDistanceMeters <= m_bucketUnloadTargetDistanceTolerance;
  }

  private void PrimeEnteredParticleHashes()
  {
    if ( !m_accumulateEnteredParticleMass )
      return;

    var halfExtents = GetMeasurementHalfExtents();
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return;

    DeformableTerrainParticleMassUtility.CollectActiveParticleHashes( m_terrain, m_activeParticleHashes );

    DeformableTerrainParticleMassUtility.SumNewParticleMassInOrientedBox(
      transform,
      GetMeasurementCenterLocal(),
      halfExtents,
      m_terrain,
      m_enteredParticleHashes );
  }

  private float ReadHandledAsParticleRigidBodyMassInBox( Vector3 measurementCenterLocal, Vector3 halfExtents )
  {
    return HandledAsParticleRigidBodyMassUtility.SumMassInOrientedBox( transform,
                                                                      measurementCenterLocal,
                                                                      halfExtents,
                                                                      null,
                                                                      m_handledAsParticleRigidBodyPadding );
  }

  private Vector3 GetMeasurementCenterLocal()
  {
    var footprintHalfHeight = m_sensorFootprint != null ? m_sensorFootprint.HalfExtents.y : 0.0f;
    return m_localCenterOffset + Vector3.up * ( footprintHalfHeight + 0.5f * m_measurementHeight );
  }

  private Vector3 GetMeasurementHalfExtents()
  {
    var footprintHalfExtents = m_sensorFootprint != null ? m_sensorFootprint.HalfExtents : new Vector3( 0.5f, 0.01f, 0.5f );
    return new Vector3(
      Mathf.Max( 0.01f, footprintHalfExtents.x + m_additionalHalfExtents.x ),
      Mathf.Max( 0.01f, 0.5f * m_measurementHeight + m_additionalHalfExtents.y ),
      Mathf.Max( 0.01f, footprintHalfExtents.z + m_additionalHalfExtents.z ) );
  }

  private void CaptureStaticTerrainBaselines()
  {
    m_staticTerrainBaselines.Clear();
    if ( !m_useStaticTerrainHeightForRetainedMass )
      return;

    ResolveStaticMassTerrains();
    if ( !HasStaticMassTerrains() )
      return;

    foreach ( var terrain in m_staticMassTerrains ) {
      if ( terrain == null )
        continue;

      var unityTerrain = terrain.GetComponent<Terrain>();
      var terrainData = unityTerrain != null ? unityTerrain.terrainData : null;
      if ( terrainData == null || terrainData.heightmapResolution < 2 )
        continue;

      var resolution = terrainData.heightmapResolution;
      m_staticTerrainBaselines.Add( new StaticTerrainBaseline {
        Terrain = terrain,
        UnityTerrain = unityTerrain,
        TerrainData = terrainData,
        Heights = terrainData.GetHeights( 0, 0, resolution, resolution ),
        Resolution = resolution,
        Size = terrainData.size
      } );
    }
  }

  private float ReadStaticTerrainMassFromBaseline()
  {
    if ( !m_useStaticTerrainHeightForRetainedMass )
      return 0.0f;

    if ( m_staticTerrainBaselines.Count == 0 )
      return 0.0f;

    var totalMass = 0.0f;
    foreach ( var baseline in m_staticTerrainBaselines )
      totalMass += ReadStaticTerrainMassFromBaseline( baseline );

    return totalMass;
  }

  private float ReadStaticTerrainMassFromBaseline( StaticTerrainBaseline baseline )
  {
    if ( baseline == null ||
         baseline.UnityTerrain == null ||
         baseline.TerrainData == null ||
         baseline.Heights == null ||
         baseline.Resolution < 2 )
      return 0.0f;

    var terrainData = baseline.UnityTerrain.terrainData;
    if ( terrainData == null || terrainData.heightmapResolution != baseline.Resolution )
      return 0.0f;

    var currentHeights = terrainData.GetHeights( 0, 0, baseline.Resolution, baseline.Resolution );
    var size = terrainData.size;
    var cellSizeX = size.x / ( baseline.Resolution - 1 );
    var cellSizeZ = size.z / ( baseline.Resolution - 1 );
    var cellArea = Mathf.Max( 1.0e-5f, cellSizeX * cellSizeZ );
    var density = Mathf.Max( 0.0f, m_staticTerrainBulkDensity );
    if ( density <= 0.0f )
      return 0.0f;

    if ( !TryGetMeasurementVolume( out var measurementFrame, out var measurementCenterLocal, out var measurementHalfExtents ) )
      return 0.0f;

    var totalVolume = 0.0f;
    for ( var z = 0; z < baseline.Resolution - 1; ++z ) {
      for ( var x = 0; x < baseline.Resolution - 1; ++x ) {
        var deltaHeight =
          ( ( currentHeights[ z, x ] - baseline.Heights[ z, x ] ) +
            ( currentHeights[ z + 1, x ] - baseline.Heights[ z + 1, x ] ) +
            ( currentHeights[ z, x + 1 ] - baseline.Heights[ z, x + 1 ] ) +
            ( currentHeights[ z + 1, x + 1 ] - baseline.Heights[ z + 1, x + 1 ] ) ) *
          0.25f *
          size.y;
        if ( deltaHeight <= 0.0f )
          continue;

        var currentHeight =
          ( currentHeights[ z, x ] +
            currentHeights[ z + 1, x ] +
            currentHeights[ z, x + 1 ] +
            currentHeights[ z + 1, x + 1 ] ) *
          0.25f *
          size.y;
        var localPosition = new Vector3( ( x + 0.5f ) * cellSizeX,
                                         currentHeight,
                                         ( z + 0.5f ) * cellSizeZ );
        var worldPosition = baseline.UnityTerrain.transform.TransformPoint( localPosition );
        var targetLocalPosition = measurementFrame.InverseTransformPoint( worldPosition ) - measurementCenterLocal;
        if ( Mathf.Abs( targetLocalPosition.x ) > measurementHalfExtents.x ||
             Mathf.Abs( targetLocalPosition.z ) > measurementHalfExtents.z )
          continue;

        totalVolume += deltaHeight * cellArea;
      }
    }

    return Mathf.Max( 0.0f, totalVolume * density );
  }

  private void ResolveReferences()
  {
    if ( m_sensorFootprint == null )
      m_sensorFootprint = GetComponent<Box>();

    if ( m_terrain == null )
      m_terrain = FindObjectOfType<DeformableTerrain>();

    ResolveSettledMassCompactors();
    ResolveStaticMassTerrains();
    ResolveBucketMassTracker();
  }

  private void ResolveSettledMassCompactors()
  {
    if ( HasSettledMassCompactors() || !m_autoDiscoverSettledMassCompactors )
      return;

    m_settledMassCompactors = FindObjectsByType<DumpParticleStaticTerrainCompactor>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
  }

  private bool HasSettledMassCompactors()
  {
    if ( m_settledMassCompactors == null || m_settledMassCompactors.Length == 0 )
      return false;

    foreach ( var compactor in m_settledMassCompactors ) {
      if ( compactor != null )
        return true;
    }

    return false;
  }

  private void ResolveStaticMassTerrains()
  {
    if ( HasStaticMassTerrains() || !m_autoDiscoverStaticMassTerrains )
      return;

    var terrains = new List<DeformableTerrainBase>();
    if ( HasSettledMassCompactors() ) {
      foreach ( var compactor in m_settledMassCompactors ) {
        var terrain = compactor != null ? compactor.GetComponent<DeformableTerrainBase>() : null;
        if ( terrain != null && !terrains.Contains( terrain ) )
          terrains.Add( terrain );
      }
    }

    if ( terrains.Count == 0 ) {
      var allTerrains = FindObjectsByType<DeformableTerrainBase>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );
      foreach ( var terrain in allTerrains ) {
        if ( terrain != null &&
             terrain.name.IndexOf( "DumpTerrainReceiver", System.StringComparison.OrdinalIgnoreCase ) >= 0 &&
             !terrains.Contains( terrain ) )
          terrains.Add( terrain );
      }
    }

    if ( terrains.Count > 0 )
      m_staticMassTerrains = terrains.ToArray();
  }

  private bool HasStaticMassTerrains()
  {
    if ( m_staticMassTerrains == null || m_staticMassTerrains.Length == 0 )
      return false;

    foreach ( var terrain in m_staticMassTerrains ) {
      if ( terrain != null )
        return true;
    }

    return false;
  }

}

internal static class HandledAsParticleRigidBodyMassUtility
{
  private static readonly Vector3[] BoxCorners = new Vector3[8];
  private static RigidBody[] s_cachedBodies = new RigidBody[0];
  private static float s_nextRefreshTime = -1.0f;

  public static float SumMassInOrientedBox( Transform measurementFrame,
                                            Vector3 measurementCenterLocal,
                                            Vector3 halfExtents,
                                            RigidBody excludedBody,
                                            float padding )
  {
    if ( measurementFrame == null )
      return 0.0f;

    var expandedPadding = Mathf.Max( 0.0f, padding );
    var totalMass = 0.0f;
    var candidateBodies = GetCandidateBodies();

    foreach ( var body in candidateBodies ) {
      if ( !ShouldInclude( body, excludedBody ) )
        continue;

      var localPosition = measurementFrame.InverseTransformPoint( GetWorldCenter( body ) ) - measurementCenterLocal;
      if ( Mathf.Abs( localPosition.x ) > halfExtents.x + expandedPadding ||
           Mathf.Abs( localPosition.y ) > halfExtents.y + expandedPadding ||
           Mathf.Abs( localPosition.z ) > halfExtents.z + expandedPadding )
        continue;

      totalMass += GetMass( body );
    }

    return totalMass;
  }

  public static bool TryCalculateLocalRendererBounds( Transform reference, out Bounds localBounds )
  {
    localBounds = default;
    if ( reference == null )
      return false;

    var renderers = reference.GetComponentsInChildren<Renderer>( true );
    var hasBounds = false;

    foreach ( var renderer in renderers ) {
      if ( renderer == null )
        continue;

      var worldBounds = renderer.bounds;
      var localMin = Vector3.positiveInfinity;
      var localMax = Vector3.negativeInfinity;

      GetBoundsCorners( worldBounds, BoxCorners );
      for ( var i = 0; i < BoxCorners.Length; ++i ) {
        var localCorner = reference.InverseTransformPoint( BoxCorners[i] );
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

  private static RigidBody[] GetCandidateBodies()
  {
    if ( Time.unscaledTime >= s_nextRefreshTime || s_cachedBodies == null || s_cachedBodies.Length == 0 ) {
      s_cachedBodies = Object.FindObjectsOfType<RigidBody>( true );
      s_nextRefreshTime = Time.unscaledTime + 0.5f;
    }

    return s_cachedBodies;
  }

  private static bool ShouldInclude( RigidBody body, RigidBody excludedBody )
  {
    return body != null &&
           body != excludedBody &&
           body.isActiveAndEnabled &&
           body.HandleAsParticle &&
           body.MotionControl == agx.RigidBody.MotionControl.DYNAMICS &&
           body.Native != null;
  }

  private static Vector3 GetWorldCenter( RigidBody body )
  {
    return body != null && body.Native != null ?
             body.Native.getCmPosition().ToHandedVector3() :
             body.transform.position;
  }

  private static float GetMass( RigidBody body )
  {
    return body != null && body.Native != null ?
             (float)body.Native.getMassProperties().getMass() :
             0.0f;
  }

  private static void GetBoundsCorners( Bounds bounds, Vector3[] corners )
  {
    var min = bounds.min;
    var max = bounds.max;

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

internal static class DeformableTerrainParticleMassUtility
{
  private static DeformableTerrainBase[] s_cachedTerrains = new DeformableTerrainBase[0];
  private static float s_nextRefreshTime = -1.0f;

  public static float SumMassInOrientedBox( Transform measurementFrame,
                                            Vector3 measurementCenterLocal,
                                            Vector3 halfExtents,
                                            DeformableTerrainBase preferredTerrain )
  {
    if ( measurementFrame == null ||
         halfExtents.x <= 0.0f ||
         halfExtents.y <= 0.0f ||
         halfExtents.z <= 0.0f )
      return 0.0f;

    var totalMass = 0.0f;
    var candidateTerrains = GetCandidateTerrains( preferredTerrain );
    foreach ( var terrain in candidateTerrains ) {
      if ( terrain == null || !terrain.isActiveAndEnabled )
        continue;

      var particles = terrain.GetParticles();
      if ( particles == null )
        continue;

      var numParticles = particles.size();
      for ( uint particleIndex = 0; particleIndex < numParticles; ++particleIndex ) {
        var particle = particles.at( particleIndex );
        if ( particle == null )
          continue;

        try {
          if ( TryGetParticleMassInOrientedBox( measurementFrame,
                                                measurementCenterLocal,
                                                halfExtents,
                                                particle,
                                                out var particleMass ) )
            totalMass += particleMass;
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }

    return totalMass;
  }

  public static float SumNewParticleMassInOrientedBox( Transform measurementFrame,
                                                       Vector3 measurementCenterLocal,
                                                       Vector3 halfExtents,
                                                       DeformableTerrainBase preferredTerrain,
                                                       ISet<uint> knownParticleHashes )
  {
    if ( measurementFrame == null ||
         knownParticleHashes == null ||
         halfExtents.x <= 0.0f ||
         halfExtents.y <= 0.0f ||
         halfExtents.z <= 0.0f )
      return 0.0f;

    var totalMass = 0.0f;
    var candidateTerrains = GetCandidateTerrains( preferredTerrain );
    foreach ( var terrain in candidateTerrains ) {
      if ( terrain == null || !terrain.isActiveAndEnabled )
        continue;

      var particles = terrain.GetParticles();
      if ( particles == null )
        continue;

      var numParticles = particles.size();
      for ( uint particleIndex = 0; particleIndex < numParticles; ++particleIndex ) {
        var particle = particles.at( particleIndex );
        if ( particle == null )
          continue;

        try {
          var particleHash = particle.hash();
          if ( knownParticleHashes.Contains( particleHash ) )
            continue;

          if ( !TryGetParticleMassInOrientedBox( measurementFrame,
                                                 measurementCenterLocal,
                                                 halfExtents,
                                                 particle,
                                                 out var particleMass ) )
            continue;

          knownParticleHashes.Add( particleHash );
          totalMass += particleMass;
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }

    return totalMass;
  }

  public static void CollectActiveParticleHashes( DeformableTerrainBase preferredTerrain,
                                                  ISet<uint> activeParticleHashes )
  {
    if ( activeParticleHashes == null )
      return;

    activeParticleHashes.Clear();
    var candidateTerrains = GetCandidateTerrains( preferredTerrain );
    foreach ( var terrain in candidateTerrains ) {
      if ( terrain == null || !terrain.isActiveAndEnabled )
        continue;

      var particles = terrain.GetParticles();
      if ( particles == null )
        continue;

      var numParticles = particles.size();
      for ( uint particleIndex = 0; particleIndex < numParticles; ++particleIndex ) {
        var particle = particles.at( particleIndex );
        if ( particle == null )
          continue;

        try {
          activeParticleHashes.Add( particle.hash() );
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }
  }

  private static bool TryGetParticleMassInOrientedBox( Transform measurementFrame,
                                                       Vector3 measurementCenterLocal,
                                                       Vector3 halfExtents,
                                                       agx.GranularBodyPtr particle,
                                                       out float particleMass )
  {
    particleMass = 0.0f;
    if ( particle == null )
      return false;

    var particlePositionLocal = measurementFrame.InverseTransformPoint( particle.getPosition().ToHandedVector3() ) - measurementCenterLocal;
    var particleRadius = Mathf.Max( 0.0f, (float)particle.getRadius() );
    if ( Mathf.Abs( particlePositionLocal.x ) > halfExtents.x + particleRadius ||
         Mathf.Abs( particlePositionLocal.y ) > halfExtents.y + particleRadius ||
         Mathf.Abs( particlePositionLocal.z ) > halfExtents.z + particleRadius )
      return false;

    particleMass = Mathf.Max( 0.0f, (float)particle.getMass() );
    return particleMass > 0.0f;
  }

  private static DeformableTerrainBase[] GetCandidateTerrains( DeformableTerrainBase preferredTerrain )
  {
    RefreshTerrainCache();
    if ( preferredTerrain == null )
      return s_cachedTerrains;

    if ( s_cachedTerrains == null || s_cachedTerrains.Length == 0 )
      return new[] { preferredTerrain };

    for ( var terrainIndex = 0; terrainIndex < s_cachedTerrains.Length; ++terrainIndex ) {
      if ( s_cachedTerrains[ terrainIndex ] == preferredTerrain )
        return s_cachedTerrains;
    }

    var terrains = new DeformableTerrainBase[s_cachedTerrains.Length + 1];
    terrains[0] = preferredTerrain;
    for ( var terrainIndex = 0; terrainIndex < s_cachedTerrains.Length; ++terrainIndex )
      terrains[terrainIndex + 1] = s_cachedTerrains[terrainIndex];

    return terrains;
  }

  private static void RefreshTerrainCache()
  {
    if ( Time.unscaledTime < s_nextRefreshTime && s_cachedTerrains != null && s_cachedTerrains.Length > 0 )
      return;

    s_cachedTerrains = Object.FindObjectsOfType<DeformableTerrainBase>( true );
    s_nextRefreshTime = Time.unscaledTime + 0.5f;
  }
}

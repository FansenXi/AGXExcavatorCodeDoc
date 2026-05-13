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
  private string m_targetName = "ContainerBox";

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
  private bool m_accumulateBucketUnloadNearTarget = true;

  [SerializeField]
  private ExcavationMassTracker m_bucketMassTracker = null;

  [SerializeField]
  [Min( 0.0f )]
  private float m_bucketUnloadTargetDistanceTolerance = 0.75f;

  [SerializeField]
  private bool m_accumulateEnteredParticleMass = true;

  [SerializeField]
  private bool m_useEnteredParticleMassForDepositedMass = true;

  [SerializeField]
  private bool m_useEnteredParticleMassForMassInBox = true;

  private float m_massInBox = 0.0f;
  private float m_depositedMass = 0.0f;
  private float m_enteredParticleMass = 0.0f;
  private float m_bucketUnloadNearTargetMass = 0.0f;
  private float m_previousBucketMass = 0.0f;
  private bool m_hasPreviousBucketMass = false;
  private float m_resetBaselineMassInBox = 0.0f;
  private float m_resetBaselineSettledCompactorMass = 0.0f;
  private float m_nextSampleTime = 0.0f;
  private readonly HashSet<uint> m_enteredParticleHashes = new HashSet<uint>();
  private Transform m_cachedTargetDistanceBoundsTransform = null;
  private Bounds m_cachedTargetDistanceLocalBounds = default;
  private bool m_hasCachedTargetDistanceLocalBounds = false;

  public override string TargetName => string.IsNullOrWhiteSpace( m_targetName ) ? gameObject.name : m_targetName;
  public override float MassInBox => m_massInBox;
  public override float DepositedMass => m_depositedMass;
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
    m_enteredParticleMass = 0.0f;
    m_bucketUnloadNearTargetMass = 0.0f;
    m_previousBucketMass = ReadBucketMass();
    m_hasPreviousBucketMass = true;
    var liveMassInBox = ReadLiveMassInBox();
    m_resetBaselineMassInBox = liveMassInBox;
    m_resetBaselineSettledCompactorMass = ReadSettledCompactorMass();
    PrimeEnteredParticleHashes();
    m_massInBox = liveMassInBox;
    m_depositedMass = 0.0f;
    m_nextSampleTime = Time.time + Mathf.Max( 0.01f, m_updateIntervalSeconds );
  }

  private void Update()
  {
    if ( !Application.isPlaying )
      return;

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

    var liveMassInBox = ReadLiveMassInBox();
    var liveDepositedMass = NormalizeMeasuredMass( liveMassInBox );
    var particleDepositedMass = m_useEnteredParticleMassForDepositedMass ?
                                Mathf.Max( liveDepositedMass, m_enteredParticleMass ) :
                                liveDepositedMass;
    var physicalTargetMass = TryReadNormalizedSettledCompactorMass( out var settledCompactorMass ) ?
                               Mathf.Max( particleDepositedMass, settledCompactorMass ) :
                               particleDepositedMass;
    m_depositedMass = Mathf.Max( physicalTargetMass, m_bucketUnloadNearTargetMass );
    m_massInBox = m_useEnteredParticleMassForMassInBox ?
                    Mathf.Max( liveMassInBox, physicalTargetMass ) :
                    liveMassInBox;
  }

  private float NormalizeMeasuredMass( float currentMassInBox )
  {
    return Mathf.Max( 0.0f, currentMassInBox - m_resetBaselineMassInBox );
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
    ResolveReferences();
    var halfExtents = GetMeasurementHalfExtents();
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return 0.0f;

    var measurementCenterLocal = GetMeasurementCenterLocal();
    var totalMass = DeformableTerrainParticleMassUtility.SumMassInOrientedBox( transform,
                                                                               measurementCenterLocal,
                                                                               halfExtents,
                                                                               m_terrain );

    if ( m_includeHandledAsParticleRigidBodies )
      totalMass += ReadHandledAsParticleRigidBodyMassInBox( measurementCenterLocal, halfExtents );

    return totalMass;
  }

  private void AccumulateEnteredParticleMass()
  {
    if ( !m_accumulateEnteredParticleMass )
      return;

    var halfExtents = GetMeasurementHalfExtents();
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return;

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

    return BucketTargetDistanceMeasurementUtility.TryMeasureDistance( m_bucketMassTracker.BucketMeasurementFrame,
                                                                     this,
                                                                     out var minDistanceMeters ) &&
           minDistanceMeters >= 0.0f &&
           minDistanceMeters <= m_bucketUnloadTargetDistanceTolerance;
  }

  private void PrimeEnteredParticleHashes()
  {
    if ( !m_accumulateEnteredParticleMass )
      return;

    var halfExtents = GetMeasurementHalfExtents();
    if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
      return;

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

  private void ResolveReferences()
  {
    if ( m_sensorFootprint == null )
      m_sensorFootprint = GetComponent<Box>();

    if ( m_terrain == null )
      m_terrain = FindObjectOfType<DeformableTerrain>();

    ResolveSettledMassCompactors();
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

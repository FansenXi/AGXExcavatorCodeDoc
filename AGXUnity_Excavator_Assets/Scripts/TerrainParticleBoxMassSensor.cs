using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity.Utils;
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

  private float m_massInBox = 0.0f;
  private float m_depositedMass = 0.0f;
  private float m_resetBaselineMassInBox = 0.0f;
  private float m_nextSampleTime = 0.0f;
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

    m_massInBox = ReadMassInBox();
    m_resetBaselineMassInBox = m_massInBox;
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
    m_massInBox = ReadMassInBox();
    m_depositedMass = NormalizeMeasuredMass( m_massInBox );
  }

  private float NormalizeMeasuredMass( float currentMassInBox )
  {
    return Mathf.Max( 0.0f, currentMassInBox - m_resetBaselineMassInBox );
  }

  private float ReadMassInBox()
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

        var particlePositionLocal = measurementFrame.InverseTransformPoint( particle.getPosition().ToHandedVector3() ) - measurementCenterLocal;
        var particleRadius = (float)particle.getRadius();
        if ( Mathf.Abs( particlePositionLocal.x ) <= halfExtents.x + particleRadius &&
             Mathf.Abs( particlePositionLocal.y ) <= halfExtents.y + particleRadius &&
             Mathf.Abs( particlePositionLocal.z ) <= halfExtents.z + particleRadius )
          totalMass += (float)particle.getMass();

        particle.ReturnToPool();
      }
    }

    return totalMass;
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

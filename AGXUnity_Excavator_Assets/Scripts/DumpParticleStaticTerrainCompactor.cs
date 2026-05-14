using System.Collections.Generic;
using AGXUnity.Model;
using AGXUnity.Utils;
using UnityEngine;

[DisallowMultipleComponent]
public class DumpParticleStaticTerrainCompactor : MonoBehaviour
{
  [SerializeField]
  private DeformableTerrainBase m_targetTerrain = null;

  [SerializeField]
  private DeformableTerrainBase[] m_sourceTerrains = null;

  [SerializeField]
  private Transform m_measurementFrame = null;

  [SerializeField]
  private Vector3 m_measurementCenterLocal = Vector3.zero;

  [SerializeField]
  private Vector3 m_halfExtents = new Vector3( 1.25f, 0.35f, 1.5f );

  [SerializeField]
  private float m_settleSpeedThreshold = 0.35f;

  [SerializeField]
  private float m_maxSettleWorldHeight = 0.85f;

  [SerializeField]
  private float m_staticHeightLimit = 0.7f;

  [SerializeField]
  private float m_soilBulkDensity = 1600.0f;

  [SerializeField]
  private float m_minFootprintRadius = 0.08f;

  [SerializeField]
  private float m_updateInterval = 0.1f;

  [SerializeField]
  private bool m_includeTargetTerrainParticles = true;

  [SerializeField]
  private bool m_logDiagnostics = false;

  public int LastSettledParticleCount { get; private set; }
  public float LastSettledMass { get; private set; }
  public float LastHeightRejectedMass { get; private set; }
  public float LastSpeedRejectedMass { get; private set; }
  public float LastTerrainRejectedMass { get; private set; }
  public float TotalSettledMass { get; private set; }
  public int TotalSettledParticleCount { get; private set; }

  private float m_nextUpdateTime = 0.0f;
  private readonly Dictionary<Vector2Int, float> m_heightDeltas = new Dictionary<Vector2Int, float>();
  private readonly List<DeformableTerrainBase> m_candidateTerrains = new List<DeformableTerrainBase>();

  public void Configure( DeformableTerrainBase targetTerrain,
                         DeformableTerrainBase[] sourceTerrains,
                         Transform measurementFrame,
                         Vector3 measurementCenterLocal,
                         Vector3 halfExtents,
                         float settleSpeedThreshold,
                         float maxSettleWorldHeight,
                         float staticHeightLimit,
                         float soilBulkDensity )
  {
    m_targetTerrain = targetTerrain;
    m_sourceTerrains = sourceTerrains;
    m_measurementFrame = measurementFrame;
    m_measurementCenterLocal = measurementCenterLocal;
    m_halfExtents = halfExtents;
    m_settleSpeedThreshold = settleSpeedThreshold;
    m_maxSettleWorldHeight = maxSettleWorldHeight;
    m_staticHeightLimit = staticHeightLimit;
    m_soilBulkDensity = soilBulkDensity;
  }

  private void Reset()
  {
    m_targetTerrain = GetComponent<DeformableTerrainBase>();
    m_measurementFrame = transform;
  }

  private void Awake()
  {
    if ( m_measurementFrame == null )
      m_measurementFrame = transform;
  }

  private void LateUpdate()
  {
    if ( !Application.isPlaying )
      return;

    if ( Time.time < m_nextUpdateTime )
      return;

    m_nextUpdateTime = Time.time + Mathf.Max( 0.02f, m_updateInterval );
    ConvertSettledParticlesNow();
  }

  [ContextMenu( "Convert Settled Dump Particles Now" )]
  public void ConvertSettledParticlesNow()
  {
    LastSettledParticleCount = 0;
    LastSettledMass = 0.0f;
    LastHeightRejectedMass = 0.0f;
    LastSpeedRejectedMass = 0.0f;
    LastTerrainRejectedMass = 0.0f;
    m_heightDeltas.Clear();

    var targetUnityTerrain = m_targetTerrain != null ? m_targetTerrain.GetComponent<Terrain>() : null;
    if ( m_targetTerrain == null ||
         targetUnityTerrain == null ||
         targetUnityTerrain.terrainData == null ||
         m_measurementFrame == null ||
         m_halfExtents.x <= 0.0f ||
         m_halfExtents.y <= 0.0f ||
         m_halfExtents.z <= 0.0f )
      return;

    RefreshCandidateTerrains();

    foreach ( var sourceTerrain in m_candidateTerrains ) {
      if ( sourceTerrain == null || !sourceTerrain.isActiveAndEnabled )
        continue;

      var particles = sourceTerrain.GetParticles();
      var soilInterface = sourceTerrain.GetSoilSimulationInterface();
      if ( particles == null || soilInterface == null )
        continue;

      var particleCount = particles.size();
      for ( var particleIndex = (long)particleCount - 1L; particleIndex >= 0L; --particleIndex ) {
        var particle = particles.at( (uint)particleIndex );
        if ( particle == null )
          continue;

        try {
          var particlePosition = particle.getPosition().ToHandedVector3();
          var particleRadius = (float)particle.getRadius();
          var mass = Mathf.Max( 0.0f, (float)particle.getMass() );

          if ( !IsInsideMeasurementVolume( particlePosition, particleRadius ) )
            continue;

          if ( particlePosition.y > GetEffectiveMaxSettleWorldHeight() + particleRadius ) {
            LastHeightRejectedMass += mass;
            continue;
          }

          var speed = particle.getVelocity().ToHandedVector3().magnitude;
          if ( speed > m_settleSpeedThreshold ) {
            LastSpeedRejectedMass += mass;
            continue;
          }

          if ( !AccumulateParticleMass( targetUnityTerrain, particlePosition, particleRadius, mass ) ) {
            LastTerrainRejectedMass += mass;
            continue;
          }

          soilInterface.removeSoilParticle( particle );

          LastSettledParticleCount++;
          LastSettledMass += mass;
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }

    if ( m_heightDeltas.Count > 0 )
      ApplyHeightDeltas();

    TotalSettledParticleCount += LastSettledParticleCount;
    TotalSettledMass += LastSettledMass;

    if ( m_logDiagnostics && ( LastSettledParticleCount > 0 ||
                               LastHeightRejectedMass > 0.0f ||
                               LastSpeedRejectedMass > 0.0f ||
                               LastTerrainRejectedMass > 0.0f ) )
      Debug.Log( $"DumpParticleStaticTerrainCompactor: converted {LastSettledParticleCount} particles ({LastSettledMass:0.###} kg) to static dump terrain; skipped height={LastHeightRejectedMass:0.###} kg, speed={LastSpeedRejectedMass:0.###} kg, terrain={LastTerrainRejectedMass:0.###} kg.", this );
  }

  public bool ContainsMeasurementParticle( Vector3 worldPosition, float radius )
  {
    return m_measurementFrame != null && IsInsideMeasurementVolume( worldPosition, radius );
  }

  public void ResetCompactionState()
  {
    LastSettledParticleCount = 0;
    LastSettledMass = 0.0f;
    LastHeightRejectedMass = 0.0f;
    LastSpeedRejectedMass = 0.0f;
    LastTerrainRejectedMass = 0.0f;
    TotalSettledMass = 0.0f;
    TotalSettledParticleCount = 0;
    m_heightDeltas.Clear();
    m_nextUpdateTime = 0.0f;
  }

  private bool IsInsideMeasurementVolume( Vector3 worldPosition, float radius )
  {
    var local = m_measurementFrame.InverseTransformPoint( worldPosition ) - m_measurementCenterLocal;
    return Mathf.Abs( local.x ) <= m_halfExtents.x + radius &&
           Mathf.Abs( local.y ) <= m_halfExtents.y + radius &&
           Mathf.Abs( local.z ) <= m_halfExtents.z + radius;
  }

  private float GetEffectiveMaxSettleWorldHeight()
  {
    if ( m_measurementFrame == null )
      return m_maxSettleWorldHeight;

    var volumeTopWorldHeight = m_measurementFrame.TransformPoint(
      m_measurementCenterLocal + Vector3.up * m_halfExtents.y ).y;
    return Mathf.Max( m_maxSettleWorldHeight, volumeTopWorldHeight );
  }

  private bool AccumulateParticleMass( Terrain targetUnityTerrain,
                                       Vector3 particleWorldPosition,
                                       float particleRadius,
                                       float particleMass )
  {
    var terrainData = targetUnityTerrain.terrainData;
    var terrainSize = terrainData.size;
    var localPosition = targetUnityTerrain.transform.InverseTransformPoint( particleWorldPosition );

    if ( localPosition.x < 0.0f || localPosition.z < 0.0f ||
         localPosition.x > terrainSize.x || localPosition.z > terrainSize.z )
      return false;

    var resolution = terrainData.heightmapResolution;
    if ( resolution < 2 )
      return false;

    var centerX = Mathf.Clamp( Mathf.RoundToInt( localPosition.x / terrainSize.x * ( resolution - 1 ) ), 0, resolution - 1 );
    var centerZ = Mathf.Clamp( Mathf.RoundToInt( localPosition.z / terrainSize.z * ( resolution - 1 ) ), 0, resolution - 1 );
    var cellSizeX = terrainSize.x / ( resolution - 1 );
    var cellSizeZ = terrainSize.z / ( resolution - 1 );
    var cellArea = Mathf.Max( 1.0e-5f, cellSizeX * cellSizeZ );
    var footprintRadius = Mathf.Max( m_minFootprintRadius, particleRadius );
    var radiusCellsX = Mathf.Max( 0, Mathf.CeilToInt( footprintRadius / cellSizeX ) + 1 );
    var radiusCellsZ = Mathf.Max( 0, Mathf.CeilToInt( footprintRadius / cellSizeZ ) + 1 );

    var particleVolume = particleMass > 0.0f && m_soilBulkDensity > 1.0f ?
                         particleMass / m_soilBulkDensity :
                         4.0f * Mathf.PI * particleRadius * particleRadius * particleRadius / 3.0f;
    if ( particleVolume <= 0.0f )
      return false;

    var totalWeight = 0.0f;
    for ( var z = centerZ - radiusCellsZ; z <= centerZ + radiusCellsZ; ++z ) {
      if ( z < 0 || z >= resolution )
        continue;

      for ( var x = centerX - radiusCellsX; x <= centerX + radiusCellsX; ++x ) {
        if ( x < 0 || x >= resolution )
          continue;

        totalWeight += CalculateFootprintWeight( x, z, centerX, centerZ, cellSizeX, cellSizeZ, footprintRadius );
      }
    }

    if ( totalWeight <= 0.0f )
      return false;

    for ( var z = centerZ - radiusCellsZ; z <= centerZ + radiusCellsZ; ++z ) {
      if ( z < 0 || z >= resolution )
        continue;

      for ( var x = centerX - radiusCellsX; x <= centerX + radiusCellsX; ++x ) {
        if ( x < 0 || x >= resolution )
          continue;

        var weight = CalculateFootprintWeight( x, z, centerX, centerZ, cellSizeX, cellSizeZ, footprintRadius );
        if ( weight <= 0.0f )
          continue;

        var key = new Vector2Int( x, z );
        var deltaHeight = particleVolume * weight / totalWeight / cellArea;
        if ( m_heightDeltas.TryGetValue( key, out var previousDelta ) )
          m_heightDeltas[ key ] = previousDelta + deltaHeight;
        else
          m_heightDeltas.Add( key, deltaHeight );
      }
    }

    return true;
  }

  private static float CalculateFootprintWeight( int x,
                                                 int z,
                                                 int centerX,
                                                 int centerZ,
                                                 float cellSizeX,
                                                 float cellSizeZ,
                                                 float footprintRadius )
  {
    var dx = ( x - centerX ) * cellSizeX;
    var dz = ( z - centerZ ) * cellSizeZ;
    var distance = Mathf.Sqrt( dx * dx + dz * dz );
    var radius = footprintRadius + Mathf.Max( cellSizeX, cellSizeZ ) * 0.5f;
    if ( distance > radius )
      return 0.0f;

    return Mathf.Max( 0.05f, 1.0f - distance / Mathf.Max( radius, 1.0e-5f ) );
  }

  private void ApplyHeightDeltas()
  {
    foreach ( var pair in m_heightDeltas ) {
      var currentHeight = Mathf.Max( 0.0f, m_targetTerrain.GetHeight( pair.Key.x, pair.Key.y ) );
      var targetHeight = Mathf.Min( m_staticHeightLimit, currentHeight + pair.Value );
      m_targetTerrain.SetHeight( pair.Key.x, pair.Key.y, targetHeight );
    }

    m_targetTerrain.TriggerModifyAllCells();
  }

  private void RefreshCandidateTerrains()
  {
    m_candidateTerrains.Clear();

    if ( m_sourceTerrains != null ) {
      for ( var index = 0; index < m_sourceTerrains.Length; ++index )
        AddCandidateTerrain( m_sourceTerrains[ index ] );
    }

    if ( m_candidateTerrains.Count == 0 ) {
      var allTerrains = FindObjectsOfType<DeformableTerrainBase>();
      for ( var index = 0; index < allTerrains.Length; ++index )
        AddCandidateTerrain( allTerrains[ index ] );
    }
  }

  private void AddCandidateTerrain( DeformableTerrainBase terrain )
  {
    if ( terrain == null )
      return;

    if ( terrain == m_targetTerrain && !m_includeTargetTerrainParticles )
      return;

    if ( !m_candidateTerrains.Contains( terrain ) )
      m_candidateTerrains.Add( terrain );
  }
}

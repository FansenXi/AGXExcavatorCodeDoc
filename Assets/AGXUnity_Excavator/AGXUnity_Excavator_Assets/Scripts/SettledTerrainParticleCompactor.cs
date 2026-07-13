using System;
using System.Collections.Generic;
using AGXUnity.Model;
using AGXUnity.Utils;
using UnityEngine;

[DisallowMultipleComponent]
public class SettledTerrainParticleCompactor : MonoBehaviour
{
  [SerializeField]
  private DeformableTerrainBase[] m_receiverTerrains = null;

  [SerializeField]
  private DeformableTerrainBase[] m_sourceTerrains = null;

  [SerializeField]
  private DumpParticleStaticTerrainCompactor[] m_reservedVolumeCompactors = null;

  [SerializeField]
  private float m_settleSpeedThreshold = 0.35f;

  [SerializeField]
  private float m_maxSettleHeightAboveTerrain = 0.35f;

  [SerializeField]
  private float m_staticHeightLimit = 0.7f;

  [SerializeField]
  private float m_soilBulkDensity = 1600.0f;

  [SerializeField]
  private float m_minFootprintRadius = 0.08f;

  [SerializeField]
  private float m_updateInterval = 0.1f;

  [SerializeField]
  private bool m_logDiagnostics = false;

  public int LastSettledParticleCount { get; private set; }
  public float LastSettledMass { get; private set; }
  public float TotalSettledMass { get; private set; }
  public int TotalSettledParticleCount { get; private set; }

  private sealed class Receiver
  {
    public DeformableTerrainBase Terrain = null;
    public Terrain UnityTerrain = null;
    public readonly Dictionary<Vector2Int, float> HeightDeltas = new Dictionary<Vector2Int, float>();
  }

  private float m_nextUpdateTime = 0.0f;
  private readonly List<DeformableTerrainBase> m_candidateSources = new List<DeformableTerrainBase>();
  private readonly List<Receiver> m_receivers = new List<Receiver>();

  private void Reset()
  {
    m_receiverTerrains = GetComponents<DeformableTerrainBase>();
    m_sourceTerrains = null;
    m_reservedVolumeCompactors = FindObjectsOfType<DumpParticleStaticTerrainCompactor>();
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

  [ContextMenu( "Convert Settled Terrain Particles Now" )]
  public void ConvertSettledParticlesNow()
  {
    LastSettledParticleCount = 0;
    LastSettledMass = 0.0f;
    RefreshReceivers();
    RefreshCandidateSources();

    if ( m_receivers.Count == 0 || m_candidateSources.Count == 0 )
      return;

    foreach ( var receiver in m_receivers )
      receiver.HeightDeltas.Clear();

    foreach ( var sourceTerrain in m_candidateSources ) {
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

          if ( IsInsideReservedVolume( particlePosition, particleRadius ) )
            continue;

          var speed = particle.getVelocity().ToHandedVector3().magnitude;
          if ( speed > m_settleSpeedThreshold )
            continue;

          if ( !TryFindReceiver( particlePosition, particleRadius, out var receiver ) )
            continue;

          var mass = Mathf.Max( 0.0f, (float)particle.getMass() );
          if ( !AccumulateParticleMass( receiver, particlePosition, particleRadius, mass ) )
            continue;

          soilInterface.removeSoilParticle( particle );

          LastSettledParticleCount++;
          LastSettledMass += mass;
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }

    foreach ( var receiver in m_receivers )
      ApplyHeightDeltas( receiver );

    TotalSettledParticleCount += LastSettledParticleCount;
    TotalSettledMass += LastSettledMass;

    if ( m_logDiagnostics && LastSettledParticleCount > 0 )
      Debug.Log( $"SettledTerrainParticleCompactor: converted {LastSettledParticleCount} particles ({LastSettledMass:0.###} kg) to terrain height.", this );
  }

  public void ResetCompactionState()
  {
    LastSettledParticleCount = 0;
    LastSettledMass = 0.0f;
    TotalSettledMass = 0.0f;
    TotalSettledParticleCount = 0;
    m_nextUpdateTime = 0.0f;

    foreach ( var receiver in m_receivers )
      receiver.HeightDeltas.Clear();
  }

  private bool IsInsideReservedVolume( Vector3 worldPosition, float radius )
  {
    if ( m_reservedVolumeCompactors == null )
      return false;

    foreach ( var compactor in m_reservedVolumeCompactors ) {
      if ( compactor != null && compactor.isActiveAndEnabled && compactor.ContainsMeasurementParticle( worldPosition, radius ) )
        return true;
    }

    return false;
  }

  private bool TryFindReceiver( Vector3 particleWorldPosition, float particleRadius, out Receiver bestReceiver )
  {
    bestReceiver = null;
    var bestScore = float.PositiveInfinity;

    foreach ( var receiver in m_receivers ) {
      if ( receiver?.UnityTerrain == null || receiver.Terrain == null || !receiver.Terrain.isActiveAndEnabled )
        continue;

      var terrainData = receiver.UnityTerrain.terrainData;
      if ( terrainData == null )
        continue;

      var localPosition = receiver.UnityTerrain.transform.InverseTransformPoint( particleWorldPosition );
      var terrainSize = terrainData.size;
      if ( localPosition.x < 0.0f || localPosition.z < 0.0f ||
           localPosition.x > terrainSize.x || localPosition.z > terrainSize.z )
        continue;

      var terrainHeight = receiver.UnityTerrain.SampleHeight( particleWorldPosition ) +
                          receiver.UnityTerrain.transform.position.y;
      var verticalOffset = particleWorldPosition.y - terrainHeight;
      var penetrationTolerance = Mathf.Max( 0.05f, particleRadius * 2.0f );
      if ( verticalOffset < -penetrationTolerance ||
           verticalOffset > m_maxSettleHeightAboveTerrain + particleRadius )
        continue;

      var score = Mathf.Abs( verticalOffset );
      if ( score < bestScore ) {
        bestReceiver = receiver;
        bestScore = score;
      }
    }

    return bestReceiver != null;
  }

  private bool AccumulateParticleMass( Receiver receiver,
                                       Vector3 particleWorldPosition,
                                       float particleRadius,
                                       float particleMass )
  {
    var targetUnityTerrain = receiver.UnityTerrain;
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
        if ( receiver.HeightDeltas.TryGetValue( key, out var previousDelta ) )
          receiver.HeightDeltas[ key ] = previousDelta + deltaHeight;
        else
          receiver.HeightDeltas.Add( key, deltaHeight );
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

  private void ApplyHeightDeltas( Receiver receiver )
  {
    if ( receiver?.Terrain == null || receiver.HeightDeltas.Count == 0 )
      return;

    foreach ( var pair in receiver.HeightDeltas ) {
      var currentHeight = Mathf.Max( 0.0f, receiver.Terrain.GetHeight( pair.Key.x, pair.Key.y ) );
      var targetHeight = Mathf.Min( m_staticHeightLimit, currentHeight + pair.Value );
      receiver.Terrain.SetHeight( pair.Key.x, pair.Key.y, targetHeight );
    }

    receiver.Terrain.TriggerModifyAllCells();
  }

  private void RefreshReceivers()
  {
    m_receivers.Clear();

    if ( m_receiverTerrains != null ) {
      foreach ( var receiverTerrain in m_receiverTerrains )
        AddReceiver( receiverTerrain );
    }

    if ( m_receivers.Count > 0 )
      return;

    foreach ( var terrain in FindObjectsOfType<DeformableTerrainBase>() )
      AddReceiver( terrain );
  }

  private void RefreshCandidateSources()
  {
    m_candidateSources.Clear();

    if ( m_sourceTerrains != null ) {
      foreach ( var sourceTerrain in m_sourceTerrains )
        AddCandidateSource( sourceTerrain );
    }

    if ( m_candidateSources.Count > 0 )
      return;

    foreach ( var terrain in FindObjectsOfType<DeformableTerrainBase>() )
      AddCandidateSource( terrain );
  }

  private void AddReceiver( DeformableTerrainBase terrain )
  {
    if ( terrain == null )
      return;

    foreach ( var receiver in m_receivers ) {
      if ( receiver.Terrain == terrain )
        return;
    }

    var unityTerrain = terrain.GetComponent<Terrain>();
    if ( unityTerrain == null || unityTerrain.terrainData == null )
      return;

    m_receivers.Add( new Receiver
    {
      Terrain = terrain,
      UnityTerrain = unityTerrain
    } );
  }

  private void AddCandidateSource( DeformableTerrainBase terrain )
  {
    if ( terrain != null && !m_candidateSources.Contains( terrain ) )
      m_candidateSources.Add( terrain );
  }
}

public static class DeformableTerrainParticleResetUtility
{
  public static int RemoveAllParticlesInScene()
  {
    return RemoveAllParticles( UnityEngine.Object.FindObjectsOfType<DeformableTerrainBase>( true ) );
  }

  public static int RemoveAllParticles( IEnumerable<DeformableTerrainBase> terrains )
  {
    if ( terrains == null )
      return 0;

    var removedCount = 0;
    var visitedTerrains = new HashSet<DeformableTerrainBase>();
    foreach ( var terrain in terrains ) {
      if ( terrain == null || !visitedTerrains.Add( terrain ) )
        continue;

      removedCount += RemoveAllParticles( terrain );
    }

    return removedCount;
  }

  public static int RemoveAllParticles( DeformableTerrainBase terrain )
  {
    if ( terrain == null )
      return 0;

    if ( terrain is DeformableTerrain deformableTerrain && deformableTerrain.Native != null ) {
      var nativeParticleCount = deformableTerrain.Native.getNumSoilParticles();
      deformableTerrain.Native.clearAllSoilParticles();
      return nativeParticleCount > int.MaxValue ? int.MaxValue : Convert.ToInt32( nativeParticleCount );
    }

    var particles = terrain.GetParticles();
    var soilInterface = terrain.GetSoilSimulationInterface();
    if ( particles == null || soilInterface == null )
      return 0;

    var removedCount = 0;
    var particleCount = particles.size();
    for ( var particleIndex = (long)particleCount - 1L; particleIndex >= 0L; --particleIndex ) {
      var particle = particles.at( (uint)particleIndex );
      if ( particle == null )
        continue;

      try {
        soilInterface.removeSoilParticle( particle );
        ++removedCount;
      }
      finally {
        particle.ReturnToPool();
      }
    }

    return removedCount;
  }
}

using System;
using System.Collections.Generic;
using AGXUnity.Model;
using AGXUnity.Utils;
using UnityEngine;

[DisallowMultipleComponent]
public class SettledFloorParticleCleaner : MonoBehaviour
{
  [Serializable]
  private struct BoxVolume
  {
    [SerializeField]
    private Transform m_frame;

    [SerializeField]
    private Vector3 m_centerLocal;

    [SerializeField]
    private Vector3 m_halfExtents;

    public bool IsValid => m_frame != null &&
                           m_halfExtents.x > 0.0f &&
                           m_halfExtents.y > 0.0f &&
                           m_halfExtents.z > 0.0f;

    public bool Contains( Vector3 worldPosition, float radius )
    {
      if ( !IsValid )
        return false;

      var worldCenter = m_frame.position + m_frame.rotation * m_centerLocal;
      var local = Quaternion.Inverse( m_frame.rotation ) * ( worldPosition - worldCenter );
      return Mathf.Abs( local.x ) <= m_halfExtents.x + radius &&
             Mathf.Abs( local.y ) <= m_halfExtents.y + radius &&
             Mathf.Abs( local.z ) <= m_halfExtents.z + radius;
    }
  }

  [SerializeField]
  private DeformableTerrainBase[] m_sourceTerrains = null;

  [SerializeField]
  private BoxVolume[] m_cleanupVolumes = null;

  [SerializeField]
  private BoxVolume[] m_excludedVolumes = null;

  [SerializeField]
  private float m_settleSpeedThreshold = 0.35f;

  [SerializeField]
  private float m_deleteDelay = 10.0f;

  [SerializeField]
  private float m_updateInterval = 1.0f;

  [SerializeField]
  private bool m_logDiagnostics = false;

  public int LastDeletedParticleCount { get; private set; }
  public float LastDeletedMass { get; private set; }
  public int TotalDeletedParticleCount { get; private set; }
  public float TotalDeletedMass { get; private set; }
  public int TrackedCandidateCount => m_settleStartTimes.Count;

  private float m_nextUpdateTime = 0.0f;
  private readonly List<DeformableTerrainBase> m_candidateTerrains = new List<DeformableTerrainBase>();
  private readonly Dictionary<uint, float> m_settleStartTimes = new Dictionary<uint, float>();
  private readonly HashSet<uint> m_seenParticleHashes = new HashSet<uint>();
  private readonly List<uint> m_hashesToRemove = new List<uint>();

  private void Reset()
  {
    m_sourceTerrains = FindObjectsOfType<DeformableTerrainBase>();
    m_cleanupVolumes = null;
    m_excludedVolumes = null;
    m_settleSpeedThreshold = 0.35f;
    m_deleteDelay = 10.0f;
    m_updateInterval = 1.0f;
  }

  private void OnDisable()
  {
    ClearTracking();
  }

  private void LateUpdate()
  {
    if ( !Application.isPlaying )
      return;

    if ( Time.time < m_nextUpdateTime )
      return;

    m_nextUpdateTime = Time.time + Mathf.Max( 0.1f, m_updateInterval );
    DeleteSettledParticlesNow();
  }

  [ContextMenu( "Delete Settled Floor Particles Now" )]
  public void DeleteSettledParticlesNow()
  {
    LastDeletedParticleCount = 0;
    LastDeletedMass = 0.0f;
    m_seenParticleHashes.Clear();

    if ( !HasValidCleanupVolume() )
      return;

    RefreshCandidateTerrains();
    var speedThresholdSquared = Mathf.Max( 0.0f, m_settleSpeedThreshold * m_settleSpeedThreshold );
    var deleteDelay = Mathf.Max( 0.0f, m_deleteDelay );

    foreach ( var terrain in m_candidateTerrains ) {
      if ( terrain == null || !terrain.isActiveAndEnabled )
        continue;

      var particles = terrain.GetParticles();
      var soilInterface = terrain.GetSoilSimulationInterface();
      if ( particles == null || soilInterface == null )
        continue;

      var particleCount = particles.size();
      for ( var particleIndex = (long)particleCount - 1L; particleIndex >= 0L; --particleIndex ) {
        var particle = particles.at( (uint)particleIndex );
        if ( particle == null )
          continue;

        try {
          var particleHash = particle.hash();
          m_seenParticleHashes.Add( particleHash );

          var particlePosition = particle.getPosition().ToHandedVector3();
          var particleRadius = Mathf.Max( 0.0f, (float)particle.getRadius() );
          if ( !IsInsideAny( m_cleanupVolumes, particlePosition, particleRadius ) ||
               IsInsideAny( m_excludedVolumes, particlePosition, particleRadius ) ) {
            m_settleStartTimes.Remove( particleHash );
            continue;
          }

          var velocity = particle.getVelocity().ToHandedVector3();
          if ( velocity.sqrMagnitude > speedThresholdSquared ) {
            m_settleStartTimes.Remove( particleHash );
            continue;
          }

          if ( !m_settleStartTimes.TryGetValue( particleHash, out var settleStartTime ) ) {
            m_settleStartTimes.Add( particleHash, Time.time );
            continue;
          }

          if ( Time.time - settleStartTime < deleteDelay )
            continue;

          var mass = Mathf.Max( 0.0f, (float)particle.getMass() );
          soilInterface.removeSoilParticle( particle );
          m_settleStartTimes.Remove( particleHash );

          LastDeletedParticleCount++;
          LastDeletedMass += mass;
        }
        finally {
          particle.ReturnToPool();
        }
      }
    }

    RemoveMissingTrackedParticles();

    TotalDeletedParticleCount += LastDeletedParticleCount;
    TotalDeletedMass += LastDeletedMass;

    if ( m_logDiagnostics && LastDeletedParticleCount > 0 )
      Debug.Log( $"SettledFloorParticleCleaner: deleted {LastDeletedParticleCount} particles ({LastDeletedMass:0.###} kg).", this );
  }

  [ContextMenu( "Clear Floor Particle Tracking" )]
  public void ClearTracking()
  {
    LastDeletedParticleCount = 0;
    LastDeletedMass = 0.0f;
    m_nextUpdateTime = 0.0f;
    m_settleStartTimes.Clear();
    m_seenParticleHashes.Clear();
    m_hashesToRemove.Clear();
  }

  private bool HasValidCleanupVolume()
  {
    if ( m_cleanupVolumes == null )
      return false;

    for ( var volumeIndex = 0; volumeIndex < m_cleanupVolumes.Length; ++volumeIndex ) {
      if ( m_cleanupVolumes[ volumeIndex ].IsValid )
        return true;
    }

    return false;
  }

  private static bool IsInsideAny( BoxVolume[] volumes, Vector3 worldPosition, float radius )
  {
    if ( volumes == null )
      return false;

    for ( var volumeIndex = 0; volumeIndex < volumes.Length; ++volumeIndex ) {
      if ( volumes[ volumeIndex ].Contains( worldPosition, radius ) )
        return true;
    }

    return false;
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
    if ( terrain == null || m_candidateTerrains.Contains( terrain ) )
      return;

    m_candidateTerrains.Add( terrain );
  }

  private void RemoveMissingTrackedParticles()
  {
    m_hashesToRemove.Clear();

    foreach ( var pair in m_settleStartTimes ) {
      if ( !m_seenParticleHashes.Contains( pair.Key ) )
        m_hashesToRemove.Add( pair.Key );
    }

    for ( var index = 0; index < m_hashesToRemove.Count; ++index )
      m_settleStartTimes.Remove( m_hashesToRemove[ index ] );
  }
}

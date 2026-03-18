using System.Collections.Generic;
using System.Linq;
using AGXUnity;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class SceneResetService : MonoBehaviour
  {
    private sealed class RigidBodySnapshot
    {
      public RigidBody Body = null;
      public Vector3 Position = Vector3.zero;
      public Quaternion Rotation = Quaternion.identity;
      public agx.RigidBody.MotionControl MotionControl = agx.RigidBody.MotionControl.DYNAMICS;
      public int HierarchyDepth = 0;
    }

    [SerializeField]
    private bool m_captureSnapshotOnAwake = true;

    [SerializeField]
    private Transform[] m_resetRoots = null;

    [SerializeField]
    private RigidBody[] m_explicitRigidBodies = null;

    [SerializeField]
    private Constraint[] m_constraints = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private global::MassVolumeCounter[] m_massVolumeCounters = null;

    [SerializeField]
    private global::ResetTerrain[] m_resetTerrains = null;

    [SerializeField]
    private AGXUnity.Model.DeformableTerrain[] m_fallbackTerrains = null;

    private readonly List<RigidBodySnapshot> m_rigidBodySnapshots = new List<RigidBodySnapshot>();
    private bool m_hasSnapshot = false;
    private bool m_isResetInProgress = false;

    private void Awake()
    {
      ResolveReferences();
      if ( m_captureSnapshotOnAwake )
        CaptureResetSnapshot();
    }

    [ContextMenu( "Capture Reset Snapshot" )]
    public void CaptureResetSnapshot()
    {
      ResolveReferences();

      m_rigidBodySnapshots.Clear();
      foreach ( var body in EnumerateRigidBodiesToReset() ) {
        if ( body == null )
          continue;

        m_rigidBodySnapshots.Add( new RigidBodySnapshot
        {
          Body = body,
          Position = body.transform.position,
          Rotation = body.transform.rotation,
          MotionControl = body.MotionControl,
          HierarchyDepth = GetHierarchyDepth( body.transform )
        } );
      }

      m_rigidBodySnapshots.Sort( ( left, right ) => left.HierarchyDepth.CompareTo( right.HierarchyDepth ) );
      m_hasSnapshot = m_rigidBodySnapshots.Count > 0;
    }

    [ContextMenu( "Hard Reset Scene" )]
    public void ResetScene()
    {
      if ( m_isResetInProgress )
        return;

      if ( !Application.isPlaying ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): hard reset is only supported in Play Mode.", this );
        return;
      }

      ResolveReferences();
      if ( !m_hasSnapshot )
        CaptureResetSnapshot();

      if ( !m_hasSnapshot ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): no rigid body snapshot available.", this );
        return;
      }

      m_isResetInProgress = true;
      var previousAutoStepping = Simulation.HasInstance ?
                                 Simulation.Instance.AutoSteppingMode :
                                 Simulation.AutoSteppingModes.FixedUpdate;

      try {
        m_episodeManager?.StopEpisode( "scene_reset" );
        m_machineController?.StopMotion();

        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = Simulation.AutoSteppingModes.Disabled;

        NeutralizeConstraintControllers( snapLockControllersToCurrentState: false );
        ResetTerrainAndMeasurements();
        RestoreRigidBodiesFromSnapshot();
        NeutralizeConstraintControllers( snapLockControllersToCurrentState: true );
        m_machineController?.StopMotion();
      }
      finally {
        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = previousAutoStepping;

        m_isResetInProgress = false;
      }
    }

    private void ResetTerrainAndMeasurements()
    {
      var countersHandledReset = false;
      if ( m_massVolumeCounters != null ) {
        foreach ( var counter in m_massVolumeCounters ) {
          if ( counter == null )
            continue;

          counter.ResetMeasurements();
          countersHandledReset |= counter.m_terrain != null;
        }
      }

      if ( countersHandledReset )
        return;

      var terrainResetHandled = false;
      if ( m_resetTerrains != null ) {
        foreach ( var resetTerrain in m_resetTerrains ) {
          if ( resetTerrain == null )
            continue;

          resetTerrain.ResetTerrainHeights();
          terrainResetHandled = true;
        }
      }

      if ( terrainResetHandled || m_fallbackTerrains == null )
        return;

      foreach ( var terrain in m_fallbackTerrains ) {
        if ( terrain != null )
          terrain.ResetHeights();
      }
    }

    private void RestoreRigidBodiesFromSnapshot()
    {
      foreach ( var snapshot in m_rigidBodySnapshots ) {
        var body = snapshot.Body;
        if ( body == null )
          continue;

        body.MotionControl = agx.RigidBody.MotionControl.KINEMATICS;
        body.transform.SetPositionAndRotation( snapshot.Position, snapshot.Rotation );
        body.SyncNativeTransform();
        body.LinearVelocity = Vector3.zero;
        body.AngularVelocity = Vector3.zero;
        body.MotionControl = snapshot.MotionControl;
      }
    }

    private void NeutralizeConstraintControllers( bool snapLockControllersToCurrentState )
    {
      foreach ( var constraint in EnumerateConstraints() ) {
        if ( constraint == null )
          continue;

        var speedController = constraint.GetController<TargetSpeedController>();
        if ( speedController != null ) {
          speedController.Speed = 0.0f;
          speedController.Enable = false;
        }

        var lockController = constraint.GetController<LockController>();
        if ( lockController != null && snapLockControllersToCurrentState )
          lockController.Position = constraint.GetCurrentAngle();
      }
    }

    private IEnumerable<RigidBody> EnumerateRigidBodiesToReset()
    {
      var bodies = new HashSet<RigidBody>();
      if ( m_explicitRigidBodies != null ) {
        foreach ( var body in m_explicitRigidBodies ) {
          if ( body != null )
            bodies.Add( body );
        }
      }

      if ( m_resetRoots != null && m_resetRoots.Length > 0 ) {
        foreach ( var root in m_resetRoots ) {
          if ( root == null )
            continue;

          foreach ( var body in root.GetComponentsInChildren<RigidBody>( true ) )
            bodies.Add( body );
        }
      }
      else {
        foreach ( var body in FindObjectsByType<RigidBody>( FindObjectsInactive.Include, FindObjectsSortMode.None ) )
          bodies.Add( body );
      }

      return bodies;
    }

    private IEnumerable<Constraint> EnumerateConstraints()
    {
      if ( m_constraints != null && m_constraints.Length > 0 )
        return m_constraints.Where( constraint => constraint != null );

      if ( m_resetRoots != null && m_resetRoots.Length > 0 ) {
        var constraints = new HashSet<Constraint>();
        foreach ( var root in m_resetRoots ) {
          if ( root == null )
            continue;

          foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) )
            constraints.Add( constraint );
        }

        return constraints;
      }

      return FindObjectsByType<Constraint>( FindObjectsInactive.Include, FindObjectsSortMode.None );
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );

      if ( m_massVolumeCounters == null || m_massVolumeCounters.Length == 0 )
        m_massVolumeCounters = FindObjectsOfType<global::MassVolumeCounter>();

      if ( m_resetTerrains == null || m_resetTerrains.Length == 0 )
        m_resetTerrains = FindObjectsOfType<global::ResetTerrain>();

      if ( m_fallbackTerrains == null || m_fallbackTerrains.Length == 0 )
        m_fallbackTerrains = FindObjectsOfType<AGXUnity.Model.DeformableTerrain>();
    }

    private static int GetHierarchyDepth( Transform transform )
    {
      var depth = 0;
      while ( transform != null ) {
        ++depth;
        transform = transform.parent;
      }

      return depth;
    }
  }
}

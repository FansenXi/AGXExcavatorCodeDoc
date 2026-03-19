using System.Collections.Generic;
using System.Linq;
using AGXUnity;
using AGXUnity.Utils;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;
using UnityEngine.Serialization;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class SceneResetService : MonoBehaviour
  {
    private sealed class RigidBodySnapshot
    {
      public RigidBody Body = null;
      public Vector3 Position = Vector3.zero;
      public Quaternion Rotation = Quaternion.identity;
      public Vector3 LinearVelocity = Vector3.zero;
      public Vector3 AngularVelocity = Vector3.zero;
      public agx.RigidBody.MotionControl MotionControl = agx.RigidBody.MotionControl.DYNAMICS;
      public int HierarchyDepth = 0;
    }

    private sealed class ConstraintSnapshot
    {
      public Constraint Constraint = null;
      public bool LockControllerEnabled = false;
      public float LockControllerPosition = 0.0f;
      public bool TargetSpeedControllerEnabled = false;
      public float TargetSpeed = 0.0f;
      public bool TargetSpeedLockAtZeroSpeed = false;
    }

    private sealed class DriveTrainSnapshot
    {
      public float Throttle = 0.0f;
      public float CentralGearRatio = 0.0f;
      public Vector2 ClutchEfficiency = Vector2.zero;
      public Vector2 BrakeEfficiency = Vector2.zero;
      public Vector2 GearRatio = Vector2.zero;
    }

    [SerializeField]
    private bool m_captureSnapshotOnAwake = true;

    [SerializeField]
    private bool m_captureSnapshotOnFirstFixedUpdate = true;

    [SerializeField]
    private Transform[] m_resetRoots = null;

    [SerializeField]
    private RigidBody[] m_explicitRigidBodies = null;

    [SerializeField]
    private Constraint[] m_constraints = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Excavator m_excavator = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [FormerlySerializedAs( "m_massVolumeCounters" )]
    [SerializeField]
    private global::ExcavationMassTracker[] m_massTrackers = null;

    [SerializeField]
    private global::ResetTerrain[] m_resetTerrains = null;

    [SerializeField]
    private AGXUnity.Model.DeformableTerrain[] m_fallbackTerrains = null;

    private readonly List<RigidBodySnapshot> m_rigidBodySnapshots = new List<RigidBodySnapshot>();
    private readonly List<ConstraintSnapshot> m_constraintSnapshots = new List<ConstraintSnapshot>();
    private DriveTrainSnapshot m_driveTrainSnapshot = null;
    private bool m_hasSnapshot = false;
    private bool m_isResetInProgress = false;
    private bool m_pendingInitialSnapshotCapture = false;

    private void Awake()
    {
      ResolveReferences();

      m_pendingInitialSnapshotCapture = Application.isPlaying && m_captureSnapshotOnFirstFixedUpdate;
      if ( m_captureSnapshotOnAwake && !m_pendingInitialSnapshotCapture )
        CaptureResetSnapshot();
    }

    private void FixedUpdate()
    {
      if ( !m_pendingInitialSnapshotCapture || m_isResetInProgress )
        return;

      CaptureResetSnapshot();
    }

    [ContextMenu( "Capture Reset Snapshot" )]
    public void CaptureResetSnapshot()
    {
      ResolveReferences();
      m_pendingInitialSnapshotCapture = false;

      m_rigidBodySnapshots.Clear();
      m_constraintSnapshots.Clear();
      m_driveTrainSnapshot = null;

      foreach ( var body in EnumerateRigidBodiesToReset() ) {
        if ( body == null )
          continue;

        m_rigidBodySnapshots.Add( new RigidBodySnapshot
        {
          Body = body,
          Position = body.transform.position,
          Rotation = body.transform.rotation,
          LinearVelocity = body.LinearVelocity,
          AngularVelocity = body.AngularVelocity,
          MotionControl = body.MotionControl,
          HierarchyDepth = GetHierarchyDepth( body.transform )
        } );
      }

      foreach ( var constraint in EnumerateConstraintsToReset() ) {
        if ( constraint == null )
          continue;

        var lockController = constraint.GetController<LockController>();
        var speedController = constraint.GetController<TargetSpeedController>();
        m_constraintSnapshots.Add( new ConstraintSnapshot
        {
          Constraint = constraint,
          LockControllerEnabled = lockController != null && lockController.Enable,
          LockControllerPosition = lockController != null ? lockController.Position : 0.0f,
          TargetSpeedControllerEnabled = speedController != null && speedController.Enable,
          TargetSpeed = speedController != null ? speedController.Speed : 0.0f,
          TargetSpeedLockAtZeroSpeed = speedController != null && speedController.LockAtZeroSpeed
        } );
      }

      if ( m_excavator != null ) {
        m_driveTrainSnapshot = new DriveTrainSnapshot
        {
          Throttle = m_excavator.Throttle,
          CentralGearRatio = m_excavator.CentralGearRatio,
          ClutchEfficiency = m_excavator.ClutchEfficiency,
          BrakeEfficiency = m_excavator.BrakeEfficiency,
          GearRatio = m_excavator.GearRatio
        };
      }

      m_rigidBodySnapshots.Sort( ( left, right ) => left.HierarchyDepth.CompareTo( right.HierarchyDepth ) );
      m_hasSnapshot = m_rigidBodySnapshots.Count > 0 || m_constraintSnapshots.Count > 0;
    }

    [ContextMenu( "Hard Reset Scene" )]
    public void ResetScene()
    {
      ResetScene( resetTerrain: true, resetPose: true );
    }

    public void ResetScene( bool resetTerrain, bool resetPose )
    {
      if ( m_isResetInProgress )
        return;

      if ( !Application.isPlaying ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): hard reset is only supported in Play Mode.", this );
        return;
      }

      ResolveReferences();
      if ( resetPose && !m_hasSnapshot )
        CaptureResetSnapshot();

      if ( resetPose && !m_hasSnapshot ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): no rigid body snapshot available.", this );
        return;
      }

      m_isResetInProgress = true;
      var previousAutoStepping = Simulation.HasInstance ?
                                 Simulation.Instance.AutoSteppingMode :
                                 Simulation.AutoSteppingModes.FixedUpdate;

      try {
        m_episodeManager?.StopEpisode( "scene_reset" );
        m_machineController?.StopEngine();

        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = Simulation.AutoSteppingModes.Disabled;

        if ( resetPose ) {
          DisableConstraintControllers();
          SetRigidBodiesMotionControlForRestore();
        }

        ResetTerrains( resetTerrain );

        if ( resetPose ) {
          RestoreRigidBodiesFromSnapshot();
          RestoreConstraintControllersFromSnapshot();
          RestoreDriveTrainFromSnapshot();
          ReinitializeTracksFromSnapshot();
        }

        if ( Simulation.HasInstance && ( resetTerrain || resetPose ) )
          Simulation.Instance.DoStep();

        if ( resetPose ) {
          RestoreRigidBodiesFromSnapshot();
          RestoreConstraintControllersFromSnapshot();
          RestoreDriveTrainFromSnapshot();
          ReinitializeTracksFromSnapshot();
          RestoreRigidBodyMotionControls();
        }
        ResetMeasurementTrackers();

      }
      finally {
        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = previousAutoStepping;

        m_isResetInProgress = false;
      }
    }

    [ContextMenu( "Reset Episode To Initial Snapshot" )]
    public void ResetEpisodeToInitialSnapshot()
    {
      ResolveReferences();

      if ( m_episodeManager != null )
        m_episodeManager.ResetEpisode( restartEpisode: true );
      else
        ResetScene();
    }

    private void ResetTerrains( bool resetTerrain )
    {
      if ( !resetTerrain )
        return;

      var terrainResetHandled = false;
      if ( m_resetTerrains != null ) {
        foreach ( var terrainResetter in m_resetTerrains ) {
          if ( terrainResetter == null )
            continue;

          terrainResetter.ResetTerrainHeights();
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

    private void ResetMeasurementTrackers()
    {
      if ( m_massTrackers == null )
        return;

      foreach ( var tracker in m_massTrackers ) {
        if ( tracker != null )
          tracker.ResetMeasurements();
      }
    }

    private void RestoreRigidBodiesFromSnapshot()
    {
      foreach ( var snapshot in m_rigidBodySnapshots ) {
        var body = snapshot.Body;
        if ( body == null )
          continue;

        body.transform.SetPositionAndRotation( snapshot.Position, snapshot.Rotation );
        body.SyncNativeTransform();
        body.LinearVelocity = snapshot.LinearVelocity;
        body.AngularVelocity = snapshot.AngularVelocity;
        ClearNativeForceAndTorque( body );
      }
    }

    private void SetRigidBodiesMotionControlForRestore()
    {
      foreach ( var snapshot in m_rigidBodySnapshots ) {
        var body = snapshot.Body;
        if ( body == null )
          continue;

        body.MotionControl = agx.RigidBody.MotionControl.KINEMATICS;
      }
    }

    private void RestoreRigidBodyMotionControls()
    {
      foreach ( var snapshot in m_rigidBodySnapshots ) {
        var body = snapshot.Body;
        if ( body == null )
          continue;

        body.MotionControl = snapshot.MotionControl;
      }
    }

    private void DisableConstraintControllers()
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
        if ( lockController != null )
          lockController.Enable = false;
      }
    }

    private void RestoreConstraintControllersFromSnapshot()
    {
      foreach ( var snapshot in m_constraintSnapshots ) {
        var constraint = snapshot.Constraint;
        if ( constraint == null )
          continue;

        var speedController = constraint.GetController<TargetSpeedController>();
        if ( speedController != null ) {
          speedController.Speed = snapshot.TargetSpeed;
          speedController.LockAtZeroSpeed = snapshot.TargetSpeedLockAtZeroSpeed;
          speedController.Enable = snapshot.TargetSpeedControllerEnabled;
        }

        var lockController = constraint.GetController<LockController>();
        if ( lockController != null ) {
          lockController.Position = snapshot.LockControllerPosition;
          lockController.Enable = snapshot.LockControllerEnabled;
        }
      }
    }

    private void RestoreDriveTrainFromSnapshot()
    {
      if ( m_excavator == null || m_driveTrainSnapshot == null )
        return;

      m_excavator.CentralGearRatio = m_driveTrainSnapshot.CentralGearRatio;
      m_excavator.Throttle = m_driveTrainSnapshot.Throttle;
      m_excavator.ClutchEfficiency = m_driveTrainSnapshot.ClutchEfficiency;
      m_excavator.BrakeEfficiency = m_driveTrainSnapshot.BrakeEfficiency;
      m_excavator.GearRatio = m_driveTrainSnapshot.GearRatio;
    }

    private void ReinitializeTracksFromSnapshot()
    {
      foreach ( var track in EnumerateTracksToReset() ) {
        if ( track == null )
          continue;

        var initializedTrack = track.GetInitialized<AGXUnity.Model.Track>();
        if ( initializedTrack?.Native == null )
          continue;

        initializedTrack.Native.reset();
        initializedTrack.Native.setVelocity( Vector3.zero.ToHandedVec3() );
        initializedTrack.Native.setAngularVelocity( Vector3.zero.ToHandedVec3() );
        initializedTrack.Native.reinitialize( (ulong)Mathf.Max( initializedTrack.NumberOfNodes, 1 ),
                                              initializedTrack.Width,
                                              initializedTrack.Thickness,
                                              initializedTrack.InitialTensionDistance );
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

    private IEnumerable<Constraint> EnumerateConstraintsToReset()
    {
      var constraints = new HashSet<Constraint>();
      if ( m_excavator != null ) {
        foreach ( var sprocketHinge in m_excavator.SprocketHinges ) {
          if ( sprocketHinge != null )
            constraints.Add( sprocketHinge );
        }

        if ( m_excavator.SwingHinge != null )
          constraints.Add( m_excavator.SwingHinge );

        if ( m_excavator.BucketPrismatic != null )
          constraints.Add( m_excavator.BucketPrismatic );

        if ( m_excavator.StickPrismatic != null )
          constraints.Add( m_excavator.StickPrismatic );

        if ( m_excavator.BoomPrismatics != null ) {
          foreach ( var constraint in m_excavator.BoomPrismatics ) {
            if ( constraint != null )
              constraints.Add( constraint );
          }
        }
      }

      if ( constraints.Count > 0 )
        return constraints;

      return EnumerateConstraints();
    }

    private IEnumerable<AGXUnity.Model.Track> EnumerateTracksToReset()
    {
      var tracks = new HashSet<AGXUnity.Model.Track>();
      if ( m_excavator != null ) {
        foreach ( var track in m_excavator.GetComponentsInChildren<AGXUnity.Model.Track>( true ) ) {
          if ( track != null )
            tracks.Add( track );
        }
      }

      if ( tracks.Count > 0 )
        return tracks;

      if ( m_resetRoots != null && m_resetRoots.Length > 0 ) {
        foreach ( var root in m_resetRoots ) {
          if ( root == null )
            continue;

          foreach ( var track in root.GetComponentsInChildren<AGXUnity.Model.Track>( true ) )
            tracks.Add( track );
        }

        return tracks;
      }

      return FindObjectsByType<AGXUnity.Model.Track>( FindObjectsInactive.Include, FindObjectsSortMode.None );
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );

      if ( !HasAssignedEntries( m_massTrackers ) )
        m_massTrackers = FindObjectsOfType<global::ExcavationMassTracker>();

      if ( !HasAssignedEntries( m_resetTerrains ) )
        m_resetTerrains = FindObjectsOfType<global::ResetTerrain>();

      if ( !HasAssignedEntries( m_fallbackTerrains ) )
        m_fallbackTerrains = FindObjectsOfType<AGXUnity.Model.DeformableTerrain>();
    }

    private static bool HasAssignedEntries<T>( T[] values ) where T : UnityEngine.Object
    {
      if ( values == null || values.Length == 0 )
        return false;

      foreach ( var value in values ) {
        if ( value != null )
          return true;
      }

      return false;
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

    private static void ClearNativeForceAndTorque( RigidBody body )
    {
      if ( body?.Native == null )
        return;

      // AGX keeps controller and solver forces on the rigid body between
      // steps. For reset we want a clean restart state rather than replaying
      // those residual impulses after the teleport.
      body.Native.setForce( Vector3.zero.ToHandedVec3() );
      body.Native.setTorque( Vector3.zero.ToHandedVec3() );
    }
  }
}

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
    public sealed class SceneResetReport
    {
      public int RequestedSeed = 0;
      public bool UnityRandomSeedApplied = false;
      public string SoilSeedStatus = "not_requested";
      public bool ResetTerrain = false;
      public bool ResetPose = false;
      public int TerrainResetCount = 0;
      public bool NativeTerrainRecreateRequested = false;
      public int DynamicSoilParticlesClearedBeforeTerrainReset = 0;
      public int DynamicSoilParticlesClearedAfterTerrainReset = 0;
      public int DynamicSoilParticlesCleared = 0;
      public int SoilCompactorResetCount = 0;
      public string Status = "not_run";
    }

    public sealed class ScenePoseRealignReport
    {
      public string Status = "not_run";
      public int RequestedBurnInSteps = 0;
      public int AppliedBurnInSteps = 0;
      public int LockedConstraintCount = 0;
      public bool ClearedRigidBodyVelocities = false;
      public bool ClearedRigidBodyVelocitiesAfterBurnIn = false;
      public bool QvelApplied = false;
      public string Reason = string.Empty;
    }

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
    private global::ExcavatorE85 m_e85Excavator = null;

    [SerializeField]
    private ExcavatorYuLong m_yuLongExcavator = null;

    [SerializeField]
    private Transform m_machineRoot = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [FormerlySerializedAs( "m_massVolumeCounters" )]
    [SerializeField]
    private global::ExcavationMassTracker[] m_massTrackers = null;

    [FormerlySerializedAs( "m_boxMassSensors" )]
    [SerializeField]
    private global::SwitchableTargetMassSensor[] m_targetMassSensors = null;

    [SerializeField]
    private global::ResetTerrain[] m_resetTerrains = null;

    [SerializeField]
    private AGXUnity.Model.DeformableTerrain[] m_fallbackTerrains = null;

    [SerializeField]
    private global::DumpParticleStaticTerrainCompactor[] m_dumpParticleCompactors = null;

    [SerializeField]
    private global::SettledTerrainParticleCompactor[] m_settledTerrainParticleCompactors = null;

    [SerializeField]
    private bool m_clearSoilParticlesOnReset = true;

    private readonly List<RigidBodySnapshot> m_rigidBodySnapshots = new List<RigidBodySnapshot>();
    private readonly List<ConstraintSnapshot> m_constraintSnapshots = new List<ConstraintSnapshot>();
    private DriveTrainSnapshot m_driveTrainSnapshot = null;
    private bool m_hasSnapshot = false;
    private bool m_isResetInProgress = false;
    private bool m_pendingInitialSnapshotCapture = false;

    public SceneResetReport LastResetReport { get; private set; } = new SceneResetReport();

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
      m_machineController?.ReleaseParkingBrake();
      CaptureMachineMechanicalResetStates();
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
      ResetSceneWithReport( resetTerrain, resetPose, seed: 0 );
    }

    public SceneResetReport ResetSceneWithReport( bool resetTerrain, bool resetPose, int seed )
    {
      if ( m_isResetInProgress )
        return SetLastResetReport( CreateSkippedResetReport( resetTerrain, resetPose, seed, "already_in_progress" ) );

      if ( !Application.isPlaying ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): hard reset is only supported in Play Mode.", this );
        return SetLastResetReport( CreateSkippedResetReport( resetTerrain, resetPose, seed, "not_in_play_mode" ) );
      }

      ResolveReferences();
      if ( resetPose && !m_hasSnapshot )
        CaptureResetSnapshot();

      if ( resetPose && !m_hasSnapshot ) {
        Debug.LogWarning( "SceneResetService.ResetScene(): no rigid body snapshot available.", this );
        return SetLastResetReport( CreateSkippedResetReport( resetTerrain, resetPose, seed, "missing_rigid_body_snapshot" ) );
      }

      var report = new SceneResetReport
      {
        RequestedSeed = seed,
        UnityRandomSeedApplied = true,
        SoilSeedStatus = resetTerrain ? "not_supported" : "not_requested",
        ResetTerrain = resetTerrain,
        ResetPose = resetPose,
        Status = "started"
      };

      m_isResetInProgress = true;
      var previousAutoStepping = Simulation.HasInstance ?
                                 Simulation.Instance.AutoSteppingMode :
                                 Simulation.AutoSteppingModes.FixedUpdate;

      try {
        UnityEngine.Random.InitState( seed );

        var machineMechanicalResets = resetPose ?
                                      ResolveMachineMechanicalResets() :
                                      new List<IMachineMechanicalReset>();

        m_episodeManager?.StopEpisode( "scene_reset" );
        m_machineController?.StopEngine();
        PrepareMachineMechanicalResets( machineMechanicalResets );

        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = Simulation.AutoSteppingModes.Disabled;

        if ( resetPose ) {
          DisableConstraintControllers();
          SetRigidBodiesMotionControlForRestore();
        }

        if ( resetTerrain ) {
          report.DynamicSoilParticlesClearedBeforeTerrainReset = ClearDynamicSoilParticles();
        }

        report.TerrainResetCount = ResetTerrains( resetTerrain );
        report.NativeTerrainRecreateRequested = resetTerrain && report.TerrainResetCount > 0;

        if ( resetTerrain ) {
          report.DynamicSoilParticlesClearedAfterTerrainReset = ClearDynamicSoilParticles();
          report.DynamicSoilParticlesCleared =
            report.DynamicSoilParticlesClearedBeforeTerrainReset +
            report.DynamicSoilParticlesClearedAfterTerrainReset;
          report.SoilCompactorResetCount = ResetSoilCompactors();
        }

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
          RestoreMachineMechanicalResets( machineMechanicalResets );
          FinalizeMachineMechanicalResets( machineMechanicalResets );
        }
        ResetMeasurementTrackers();
        report.Status = "applied";

      }
      finally {
        if ( Simulation.HasInstance )
          Simulation.Instance.AutoSteppingMode = previousAutoStepping;

        m_isResetInProgress = false;
      }

      return SetLastResetReport( report );
    }

    public ScenePoseRealignReport RealignActuatorPose( float[] rawQpos,
                                                       float[] qvel,
                                                       int burnInSteps,
                                                       string reason )
    {
      var report = new ScenePoseRealignReport
      {
        RequestedBurnInSteps = burnInSteps,
        Reason = reason ?? string.Empty,
        Status = "started"
      };

      if ( m_isResetInProgress ) {
        report.Status = "already_in_progress";
        return report;
      }

      if ( !Application.isPlaying ) {
        Debug.LogWarning( "SceneResetService.RealignActuatorPose(): realign is only supported in Play Mode.", this );
        report.Status = "not_in_play_mode";
        return report;
      }

      if ( rawQpos == null || rawQpos.Length < 4 ) {
        report.Status = "qpos_dim_must_be_4";
        return report;
      }

      ResolveReferences();
      m_machineController?.StopMotion();
      report.ClearedRigidBodyVelocities = ClearRigidBodyVelocitiesAndForces();

      report.LockedConstraintCount += ApplyLockTarget( m_machineController != null ? m_machineController.SwingConstraint : null,
                                                       rawQpos[ 0 ] );

      var boomConstraints = m_machineController != null ? m_machineController.BoomConstraints : null;
      if ( boomConstraints != null ) {
        foreach ( var boomConstraint in boomConstraints )
          report.LockedConstraintCount += ApplyLockTarget( boomConstraint, rawQpos[ 1 ] );
      }

      report.LockedConstraintCount += ApplyLockTarget( m_machineController != null ? m_machineController.StickConstraint : null,
                                                       rawQpos[ 2 ] );
      report.LockedConstraintCount += ApplyLockTarget( m_machineController != null ? m_machineController.BucketConstraint : null,
                                                       rawQpos[ 3 ] );

      report.AppliedBurnInSteps = RunManualSimulationSteps( burnInSteps );
      report.ClearedRigidBodyVelocitiesAfterBurnIn = ClearRigidBodyVelocitiesAndForces();
      report.QvelApplied = false;
      report.Status = report.LockedConstraintCount > 0 ? "applied" : "no_lock_controllers";
      return report;
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

    private int ResetTerrains( bool resetTerrain )
    {
      if ( !resetTerrain )
        return 0;

      var terrainResetHandled = false;
      var resetCount = 0;
      if ( m_resetTerrains != null ) {
        foreach ( var terrainResetter in m_resetTerrains ) {
          if ( terrainResetter == null )
            continue;

          terrainResetter.ResetTerrainHeights();
          terrainResetHandled = true;
          ++resetCount;
        }
      }

      if ( terrainResetHandled || m_fallbackTerrains == null )
        return resetCount;

      foreach ( var terrain in m_fallbackTerrains ) {
        if ( terrain == null )
          continue;

        terrain.ResetHeightsAndRecreateNative();
        ++resetCount;
      }

      return resetCount;
    }

    private int ClearDynamicSoilParticles()
    {
      if ( !m_clearSoilParticlesOnReset )
        return 0;

      return global::DeformableTerrainParticleResetUtility.RemoveAllParticlesInScene();
    }

    private int ResetSoilCompactors()
    {
      var resetCount = 0;
      if ( m_dumpParticleCompactors != null ) {
        foreach ( var compactor in m_dumpParticleCompactors ) {
          if ( compactor == null )
            continue;

          compactor.ResetCompactionState();
          ++resetCount;
        }
      }

      if ( m_settledTerrainParticleCompactors == null )
        return resetCount;

      foreach ( var compactor in m_settledTerrainParticleCompactors ) {
        if ( compactor == null )
          continue;

        compactor.ResetCompactionState();
        ++resetCount;
      }

      return resetCount;
    }

    private int ApplyLockTarget( Constraint constraint, float targetPosition )
    {
      if ( constraint == null )
        return 0;

      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController != null ) {
        speedController.Speed = 0.0f;
        speedController.LockAtZeroSpeed = false;
        speedController.Enable = false;
      }

      var lockController = constraint.GetController<LockController>();
      if ( lockController == null )
        return 0;

      lockController.Position = targetPosition;
      lockController.Enable = true;
      return 1;
    }

    private bool ClearRigidBodyVelocitiesAndForces()
    {
      var clearedAny = false;
      foreach ( var body in EnumerateRigidBodiesToReset() ) {
        if ( body == null )
          continue;

        body.LinearVelocity = Vector3.zero;
        body.AngularVelocity = Vector3.zero;
        ClearNativeForceAndTorque( body );
        clearedAny = true;
      }

      return clearedAny;
    }

    private int RunManualSimulationSteps( int requestedSteps )
    {
      var steps = Mathf.Max( 0, requestedSteps );
      if ( steps <= 0 || !Simulation.HasInstance )
        return 0;

      var previousAutoStepping = Simulation.Instance.AutoSteppingMode;
      Simulation.Instance.AutoSteppingMode = Simulation.AutoSteppingModes.Disabled;
      try {
        for ( var stepIndex = 0; stepIndex < steps; ++stepIndex )
          Simulation.Instance.DoStep();
      }
      finally {
        Simulation.Instance.AutoSteppingMode = previousAutoStepping;
      }

      return steps;
    }

    private SceneResetReport CreateSkippedResetReport( bool resetTerrain, bool resetPose, int seed, string status )
    {
      return new SceneResetReport
      {
        RequestedSeed = seed,
        UnityRandomSeedApplied = false,
        SoilSeedStatus = resetTerrain ? "not_applied" : "not_requested",
        ResetTerrain = resetTerrain,
        ResetPose = resetPose,
        Status = status
      };
    }

    private SceneResetReport SetLastResetReport( SceneResetReport report )
    {
      LastResetReport = report ?? new SceneResetReport();
      return LastResetReport;
    }

    private void ResetMeasurementTrackers()
    {
      if ( m_massTrackers != null ) {
        foreach ( var tracker in m_massTrackers ) {
          if ( tracker != null )
            tracker.ResetMeasurements();
        }
      }

      if ( m_targetMassSensors == null )
        return;

      foreach ( var sensor in m_targetMassSensors ) {
        if ( sensor != null )
          sensor.ResetMeasurements();
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

    private void CaptureMachineMechanicalResetStates()
    {
      foreach ( var reset in ResolveMachineMechanicalResets() )
        InvokeMachineMechanicalReset( reset, reset.CaptureInitialState, "capture" );
    }

    private static void PrepareMachineMechanicalResets( IReadOnlyList<IMachineMechanicalReset> resets )
    {
      foreach ( var reset in resets )
        InvokeMachineMechanicalReset( reset, reset.PrepareForReset, "prepare" );
    }

    private static void RestoreMachineMechanicalResets( IReadOnlyList<IMachineMechanicalReset> resets )
    {
      foreach ( var reset in resets )
        InvokeMachineMechanicalReset( reset, reset.RestoreInitialState, "restore" );
    }

    private static void FinalizeMachineMechanicalResets( IReadOnlyList<IMachineMechanicalReset> resets )
    {
      foreach ( var reset in resets )
        InvokeMachineMechanicalReset( reset, reset.FinalizeAfterReset, "finalize" );
    }

    private static List<IMachineMechanicalReset> ResolveMachineMechanicalResets()
    {
      var resets = new List<IMachineMechanicalReset>();
      foreach ( var behaviour in FindObjectsByType<MonoBehaviour>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( behaviour is IMachineMechanicalReset reset && behaviour.isActiveAndEnabled )
          resets.Add( reset );
      }
      return resets;
    }

    private static void InvokeMachineMechanicalReset( IMachineMechanicalReset reset, System.Action action, string phase )
    {
      if ( reset == null || action == null )
        return;
      try {
        action();
      }
      catch ( System.Exception exception ) {
        Debug.LogError( $"Machine mechanical reset '{reset.ResetDisplayName}' failed during {phase}: {exception}" );
      }
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
      foreach ( var constraint in EnumerateConstraints() ) {
        if ( constraint != null )
          constraints.Add( constraint );
      }

      if ( m_machineController != null ) {
        if ( m_machineController.SwingConstraint != null )
          constraints.Add( m_machineController.SwingConstraint );

        foreach ( var boomConstraint in m_machineController.BoomConstraints ) {
          if ( boomConstraint != null )
            constraints.Add( boomConstraint );
        }

        if ( m_machineController.StickConstraint != null )
          constraints.Add( m_machineController.StickConstraint );

        if ( m_machineController.BucketConstraint != null )
          constraints.Add( m_machineController.BucketConstraint );

        foreach ( var trackConstraint in m_machineController.TrackConstraints ) {
          if ( trackConstraint != null )
            constraints.Add( trackConstraint );
        }
      }

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

      if ( m_yuLongExcavator != null ) {
        if ( m_yuLongExcavator.SwingHinge != null )
          constraints.Add( m_yuLongExcavator.SwingHinge );

        if ( m_yuLongExcavator.BoomConstraint != null )
          constraints.Add( m_yuLongExcavator.BoomConstraint );

        if ( m_yuLongExcavator.BoomCylinderPrismatic != null )
          constraints.Add( m_yuLongExcavator.BoomCylinderPrismatic );

        if ( m_yuLongExcavator.StickConstraint != null )
          constraints.Add( m_yuLongExcavator.StickConstraint );

        if ( m_yuLongExcavator.StickCylinderPrismatic != null )
          constraints.Add( m_yuLongExcavator.StickCylinderPrismatic );

        if ( m_yuLongExcavator.BucketConstraint != null )
          constraints.Add( m_yuLongExcavator.BucketConstraint );

        if ( m_yuLongExcavator.BucketCylinderPrismatic != null )
          constraints.Add( m_yuLongExcavator.BucketCylinderPrismatic );
      }

      return constraints;
    }

    private IEnumerable<AGXUnity.Model.Track> EnumerateTracksToReset()
    {
      var tracks = new HashSet<AGXUnity.Model.Track>();
      var machineRoot = ResolveMachineRoot();
      if ( machineRoot != null ) {
        foreach ( var track in machineRoot.GetComponentsInChildren<AGXUnity.Model.Track>( true ) ) {
          if ( track != null )
            tracks.Add( track );
        }
      }

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
      var previousExcavator = m_excavator;
      var previousE85Excavator = m_e85Excavator;
      var previousYuLongExcavator = m_yuLongExcavator;
      var previousMachineRoot = m_machineRoot;
      if ( !ExcavatorRigLocator.IsSelectable( m_machineRoot ) && m_machineController != null )
        m_machineRoot = m_machineController.MachineRoot;

      if ( ExcavatorRigLocator.IsSelectable( m_machineRoot ) ) {
        m_excavator = ExcavatorRigLocator.ResolveActiveComponentInRoot( m_machineRoot, m_excavator );
        m_e85Excavator = ExcavatorRigLocator.ResolveActiveComponentInRoot( m_machineRoot, m_e85Excavator );
        m_yuLongExcavator = ExcavatorRigLocator.ResolveActiveComponentInRoot( m_machineRoot, m_yuLongExcavator );
      }
      else {
        m_excavator = ExcavatorRigLocator.ResolveActiveComponent( this, m_excavator );
        m_e85Excavator = ExcavatorRigLocator.ResolveActiveComponent( this, m_e85Excavator );
        m_yuLongExcavator = ExcavatorRigLocator.ResolveActiveComponent( this, m_yuLongExcavator );
      }
      if ( previousExcavator != null && previousExcavator != m_excavator )
        ClearResetSnapshot();
      if ( previousE85Excavator != null && previousE85Excavator != m_e85Excavator )
        ClearResetSnapshot();
      if ( previousYuLongExcavator != null && previousYuLongExcavator != m_yuLongExcavator )
        ClearResetSnapshot();
      if ( previousMachineRoot != null && previousMachineRoot != m_machineRoot )
        ClearResetSnapshot();

      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );

      if ( !HasAssignedEntries( m_massTrackers ) )
        m_massTrackers = FindObjectsOfType<global::ExcavationMassTracker>();

      if ( !HasAssignedEntries( m_targetMassSensors ) )
        m_targetMassSensors = FindObjectsOfType<global::SwitchableTargetMassSensor>();

      if ( !HasAssignedEntries( m_resetTerrains ) )
        m_resetTerrains = FindObjectsOfType<global::ResetTerrain>();

      if ( !HasAssignedEntries( m_fallbackTerrains ) )
        m_fallbackTerrains = FindObjectsOfType<AGXUnity.Model.DeformableTerrain>();

      if ( !HasAssignedEntries( m_dumpParticleCompactors ) )
        m_dumpParticleCompactors = FindObjectsOfType<global::DumpParticleStaticTerrainCompactor>();

      if ( !HasAssignedEntries( m_settledTerrainParticleCompactors ) )
        m_settledTerrainParticleCompactors = FindObjectsOfType<global::SettledTerrainParticleCompactor>();
    }

    private Transform ResolveMachineRoot()
    {
      if ( m_machineController != null )
        return m_machineController.MachineRoot;

      if ( m_machineRoot != null )
        return m_machineRoot;

      if ( m_e85Excavator != null )
        return m_e85Excavator.transform;

      if ( m_yuLongExcavator != null )
        return m_yuLongExcavator.transform;

      return m_excavator != null ? m_excavator.transform : null;
    }

    private void ClearResetSnapshot()
    {
      m_rigidBodySnapshots.Clear();
      m_constraintSnapshots.Clear();
      m_driveTrainSnapshot = null;
      m_hasSnapshot = false;
      m_pendingInitialSnapshotCapture = Application.isPlaying && m_captureSnapshotOnFirstFixedUpdate;
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

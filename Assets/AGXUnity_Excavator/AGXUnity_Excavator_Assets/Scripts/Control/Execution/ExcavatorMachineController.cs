using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity;
using AGXUnity.Utils;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public enum ExcavatorMachineRigKind
  {
    None,
    Cat365,
    BobcatE85,
    YuLong
  }

  public class ExcavatorMachineController : ScriptComponent
  {
    private const string YuLongDefaultNormalizationSoftLimitProfilePath =
      "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/YuLong_norm.json";
    private const float SoftLimitCommandThreshold = 1.0e-4f;

    [SerializeField]
    private Excavator m_excavator = null;

    [SerializeField]
    private global::ExcavatorE85 m_e85Excavator = null;

    [SerializeField]
    private ExcavatorYuLong m_yuLongExcavator = null;

    [SerializeField]
    private Transform m_machineRoot = null;

    [SerializeField]
    private Transform m_bucketReference = null;

    [SerializeField]
    private ExcavatorHydraulicSystem m_hydraulicSystem = null;

    [SerializeField]
    private ExcavatorAxisActuatorBackend m_swingBackend = ExcavatorAxisActuatorBackend.TargetSpeed;

    [SerializeField]
    private ExcavatorActuationLimits m_limits = new ExcavatorActuationLimits();

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_trackSpeedScale = 0.2f;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_trackCommandDeadZone = 0.05f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_directTrackSpeedScale = 1.5f;

    [SerializeField]
    private bool m_e85TrackParkingBrake = true;

    [SerializeField]
    private bool m_e85ChassisParkingBrake = true;

    [SerializeField]
    private AGXUnity.RigidBody m_e85ParkingBrakeBody = null;

    [SerializeField]
    private string m_e85ParkingBrakeBodyPath = "UnderCarriageBody";

    [SerializeField]
    private float m_e85SwingDirection = -1.0f;

    [SerializeField]
    private float m_e85BoomDirection = 1.0f;

    [SerializeField]
    private float m_e85StickDirection = 1.0f;

    [SerializeField]
    private float m_e85BucketDirection = 1.0f;

    [SerializeField]
    private float m_yuLongSwingDirection = 1.0f;

    [SerializeField]
    private float m_yuLongBoomDirection = -1.0f;

    [SerializeField]
    private float m_yuLongStickDirection = 1.0f;

    [SerializeField]
    private float m_yuLongBucketDirection = 1.0f;

    [Header( "YuLong Swing Stabilization" )]
    [SerializeField]
    private bool m_yuLongLockSwingAtNeutral = true;

    [SerializeField]
    [Range( 0.0f, 0.2f )]
    private float m_yuLongSwingNeutralLockDeadZone = 0.03f;

    [Header( "Normalization Soft Limits" )]
    [SerializeField]
    private bool m_enableNormalizationSoftLimits = true;

    [SerializeField]
    private bool m_normalizationSoftLimitsYuLongOnly = true;

    [SerializeField]
    private string m_normalizationSoftLimitProfilePath = YuLongDefaultNormalizationSoftLimitProfilePath;

    [SerializeField]
    [Min( 0.0f )]
    private float m_normalizationSoftLimitTolerance = 0.0f;

    [SerializeField]
    private bool m_lockAtNormalizationSoftLimit = true;

    [SerializeField]
    private bool m_startWithEngineRunning = true;

    private readonly ActuatorNormalizationRange m_softLimitSwingRange = new ActuatorNormalizationRange();
    private readonly ActuatorNormalizationRange m_softLimitBoomRange = new ActuatorNormalizationRange();
    private readonly ActuatorNormalizationRange m_softLimitStickRange = new ActuatorNormalizationRange();
    private readonly ActuatorNormalizationRange m_softLimitBucketRange = new ActuatorNormalizationRange();

    private Component m_machineComponent = null;
    private ExcavatorMachineRigKind m_machineKind = ExcavatorMachineRigKind.None;
    private Component m_axisActuatorRig = null;
    private bool m_axisActuatorsInitialized = false;
    private Constraint m_swingConstraint = null;
    private Constraint[] m_boomConstraints = null;
    private Constraint m_stickConstraint = null;
    private Constraint m_bucketConstraint = null;
    private Constraint m_leftTrackConstraint = null;
    private Constraint m_rightTrackConstraint = null;
    private IExcavatorAxisActuator m_swingActuator = null;
    private IExcavatorAxisActuator m_boomActuator = null;
    private IExcavatorAxisActuator m_stickActuator = null;
    private IExcavatorAxisActuator m_bucketActuator = null;
    private IExcavatorAxisActuator m_leftTrackActuator = null;
    private IExcavatorAxisActuator m_rightTrackActuator = null;
    private bool m_e85ParkingBrakeEngaged = false;
    private bool m_e85ParkingBrakeHasOriginalMotionControl = false;
    private agx.RigidBody.MotionControl m_e85ParkingBrakeOriginalMotionControl = agx.RigidBody.MotionControl.DYNAMICS;
    private bool m_pendingHydraulicSwingActuatorRetry = false;
    private bool m_hydraulicSwingFallbackWarningLogged = false;
    private bool m_normalizationSoftLimitProfileLoaded = false;
    private bool m_normalizationSoftLimitLoadAttempted = false;
    private bool m_normalizationSoftLimitLoadWarningLogged = false;

    public ExcavatorActuationCommand LastActuationCommand { get; private set; }
    public bool IsEngineRunning { get; private set; } = true;
    public float SwingMaxTargetSpeed
    {
      get => Limits.Swing.MaxSpeed;
      set => Limits.Swing.MaxSpeed = value;
    }

    public float BoomMaxTargetSpeed
    {
      get => Limits.Boom.MaxSpeed;
      set => Limits.Boom.MaxSpeed = value;
    }

    public float StickMaxTargetSpeed
    {
      get => Limits.Stick.MaxSpeed;
      set => Limits.Stick.MaxSpeed = value;
    }

    public float BucketMaxTargetSpeed
    {
      get => Limits.Bucket.MaxSpeed;
      set => Limits.Bucket.MaxSpeed = value;
    }

    public float SwingMaxAcceleration
    {
      get => GetSwingMaxAcceleration();
      set => Limits.Swing.MaxAccelerationOverride = value;
    }

    public float BoomMaxAcceleration
    {
      get => GetBoomMaxAcceleration();
      set => Limits.Boom.MaxAccelerationOverride = value;
    }

    public float StickMaxAcceleration
    {
      get => GetStickMaxAcceleration();
      set => Limits.Stick.MaxAccelerationOverride = value;
    }

    public float BucketMaxAcceleration
    {
      get => GetBucketMaxAcceleration();
      set => Limits.Bucket.MaxAccelerationOverride = value;
    }

    private ExcavatorActuationLimits Limits => m_limits ?? ( m_limits = new ExcavatorActuationLimits() );

    public Excavator Excavator
    {
      get
      {
        ResolveReferences();
        return m_excavator;
      }
    }

    public global::ExcavatorE85 E85Excavator
    {
      get
      {
        ResolveReferences();
        return m_e85Excavator;
      }
    }

    public ExcavatorYuLong YuLongExcavator
    {
      get
      {
        ResolveReferences();
        return m_yuLongExcavator;
      }
    }

    public Component MachineComponent
    {
      get
      {
        ResolveReferences();
        return m_machineComponent;
      }
    }

    public ExcavatorMachineRigKind MachineKind
    {
      get
      {
        ResolveReferences();
        return m_machineKind;
      }
    }

    public Transform MachineRoot
    {
      get
      {
        ResolveReferences();
        if ( ExcavatorRigLocator.IsSelectable( m_machineRoot ) )
          return m_machineRoot;

        return m_machineComponent != null ? m_machineComponent.transform : transform;
      }
    }

    public Transform BucketReference
    {
      get
      {
        ResolveReferences();
        return m_bucketReference;
      }
    }

    public Constraint SwingConstraint
    {
      get
      {
        EnsureAxisActuators();
        return m_swingConstraint;
      }
    }

    public Constraint[] BoomConstraints
    {
      get
      {
        EnsureAxisActuators();
        return m_boomConstraints ?? new Constraint[0];
      }
    }

    public Constraint StickConstraint
    {
      get
      {
        EnsureAxisActuators();
        return m_stickConstraint;
      }
    }

    public Constraint BucketConstraint
    {
      get
      {
        EnsureAxisActuators();
        return m_bucketConstraint;
      }
    }

    public Constraint[] TrackConstraints
    {
      get
      {
        EnsureAxisActuators();
        if ( m_leftTrackConstraint == null && m_rightTrackConstraint == null )
          return new Constraint[0];

        return new[] { m_leftTrackConstraint, m_rightTrackConstraint };
      }
    }

    protected override bool Initialize()
    {
      ResolveReferences();
      if ( m_machineComponent == null ) {
        Debug.LogError( "Unable to initialize ExcavatorMachineController because no supported excavator component was found.", this );
        return false;
      }

      EnsureAxisActuators();
      return true;
    }

    private void Awake()
    {
      ResolveReferences();
      EnsureAxisActuators();
      EnsureNormalizationSoftLimitProfileLoaded();
      IsEngineRunning = m_startWithEngineRunning;

      if ( !IsEngineRunning )
        ApplyNeutralActuation( true );
    }

    public void ApplyActuationCommand( ExcavatorActuationCommand command )
    {
      if ( m_machineComponent == null && !ResolveReferences() )
        return;

      if ( !IsEngineRunning ) {
        LastActuationCommand = ExcavatorActuationCommand.Zero;
        ApplyNeutralActuation( true );
        return;
      }

      LastActuationCommand = command.ClampAxes();

      ApplyActuation( LastActuationCommand, false );
    }

    public void StartEngine()
    {
      if ( m_machineComponent == null && !ResolveReferences() )
        return;

      IsEngineRunning = true;
      if ( m_excavator != null )
        m_excavator.EngineEnabled = true;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
    }

    public void StopEngine()
    {
      if ( m_machineComponent == null && !ResolveReferences() )
        return;

      IsEngineRunning = false;
      if ( m_excavator != null )
        m_excavator.EngineEnabled = false;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      ApplyNeutralActuation( true );
    }

    public void StopMotion()
    {
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      ApplyNeutralActuation( true );
    }

    public void ReleaseParkingBrake()
    {
      ReleaseE85ParkingBrake();
    }

    public bool TryRealignNormalizedPose( float[] qposNorm, out string warning )
    {
      warning = string.Empty;
      if ( qposNorm == null || qposNorm.Length < 4 ) {
        warning = "qpos_dim_must_be_4";
        return false;
      }

      if ( m_machineComponent == null && !ResolveReferences() ) {
        warning = "machine_component_missing";
        return false;
      }

      if ( !EnsureAxisActuators() ) {
        warning = "axis_actuators_missing";
        return false;
      }

      EnsureNormalizationSoftLimitProfileLoaded();
      StopMotion();

      var warnings = new List<string>();
      var applied = false;
      applied |= TryLockAxisToNormalizedPosition(
        m_swingConstraint,
        m_softLimitSwingRange,
        qposNorm[ 0 ],
        "swing",
        warnings );
      applied |= TryLockAxisToNormalizedPosition(
        GetReferenceConstraint( m_boomConstraints ),
        m_softLimitBoomRange,
        qposNorm[ 1 ],
        "boom",
        warnings );
      applied |= TryLockAxisToNormalizedPosition(
        m_stickConstraint,
        m_softLimitStickRange,
        qposNorm[ 2 ],
        "stick",
        warnings );
      applied |= TryLockAxisToNormalizedPosition(
        m_bucketConstraint,
        m_softLimitBucketRange,
        qposNorm[ 3 ],
        "bucket",
        warnings );

      ZeroMachineRigidBodyVelocities();
      if ( warnings.Count > 0 )
        warning = string.Join( ",", warnings );
      return applied;
    }

    private void ApplyActuation( ExcavatorActuationCommand command, bool immediateConstraintStop )
    {
      if ( m_machineComponent == null && !ResolveReferences() )
        return;

      EnsureAxisActuators();
      SetThrottle( command.Throttle );
      ApplyDriveTrain( command.Drive, command.Steer );
      SetBoom( command.Boom, immediateConstraintStop );
      SetBucket( command.Bucket, immediateConstraintStop );
      SetStick( command.Stick, immediateConstraintStop );
      SetSwing( command.Swing, immediateConstraintStop );
    }

    private void ApplyNeutralActuation( bool immediateConstraintStop )
    {
      ApplyActuation( ExcavatorActuationCommand.Zero, immediateConstraintStop );
    }

    private void ApplyDriveTrain( float drive, float steer )
    {
      var leftTrack = ApplyTrackDeadZone( Mathf.Clamp( drive - steer, -1.0f, 1.0f ) );
      var rightTrack = ApplyTrackDeadZone( Mathf.Clamp( drive + steer, -1.0f, 1.0f ) );

      if ( m_machineKind == ExcavatorMachineRigKind.BobcatE85 ) {
        var trackSpeedScale = GetDirectTrackSpeedScale();
        var tracksNeutral = Mathf.Approximately( leftTrack, 0.0f ) &&
                            Mathf.Approximately( rightTrack, 0.0f );
        SetE85ParkingBrake( tracksNeutral );

        var parkLeftTrack = ShouldParkE85Track( leftTrack );
        var parkRightTrack = ShouldParkE85Track( rightTrack );
        m_leftTrackActuator?.Apply( -leftTrack * trackSpeedScale, parkLeftTrack );
        m_rightTrackActuator?.Apply( -rightTrack * trackSpeedScale, parkRightTrack );
        return;
      }

      if ( m_excavator == null )
        return;

      SetE85ParkingBrake( false );

      var clutch = new Vector2(
        Mathf.Abs( leftTrack ) > 0.0f ? 1.0f : 0.0f,
        Mathf.Abs( rightTrack ) > 0.0f ? 1.0f : 0.0f );

      m_excavator.ClutchEfficiency = clutch;
      m_excavator.BrakeEfficiency = Vector2.one - clutch;
      m_excavator.GearRatio = new Vector2( -leftTrack, -rightTrack ) * m_trackSpeedScale;
    }

    private void SetThrottle( float value )
    {
      if ( m_excavator != null )
        m_excavator.Throttle = Mathf.Clamp01( value );
    }

    private float ApplyTrackDeadZone( float command )
    {
      return Mathf.Abs( command ) >= m_trackCommandDeadZone ? command : 0.0f;
    }

    private bool ShouldParkE85Track( float trackCommand )
    {
      return m_e85TrackParkingBrake && Mathf.Approximately( trackCommand, 0.0f );
    }

    private void SetE85ParkingBrake( bool engage )
    {
      if ( !m_e85ChassisParkingBrake ||
           m_machineKind != ExcavatorMachineRigKind.BobcatE85 ||
           !engage ) {
        ReleaseE85ParkingBrake();
        return;
      }

      var parkingBody = ResolveE85ParkingBrakeBody();
      if ( parkingBody == null )
        return;

      if ( !m_e85ParkingBrakeEngaged ) {
        m_e85ParkingBrakeOriginalMotionControl = parkingBody.MotionControl;
        m_e85ParkingBrakeHasOriginalMotionControl = true;
        m_e85ParkingBrakeEngaged = true;
      }

      parkingBody.LinearVelocity = Vector3.zero;
      parkingBody.AngularVelocity = Vector3.zero;
      parkingBody.MotionControl = agx.RigidBody.MotionControl.KINEMATICS;
      ClearNativeForceAndTorque( parkingBody );
    }

    private void ReleaseE85ParkingBrake()
    {
      if ( !m_e85ParkingBrakeEngaged )
        return;

      var parkingBody = ResolveE85ParkingBrakeBody();
      if ( parkingBody != null ) {
        parkingBody.MotionControl = m_e85ParkingBrakeHasOriginalMotionControl ?
                                    m_e85ParkingBrakeOriginalMotionControl :
                                    agx.RigidBody.MotionControl.DYNAMICS;
        parkingBody.LinearVelocity = Vector3.zero;
        parkingBody.AngularVelocity = Vector3.zero;
        ClearNativeForceAndTorque( parkingBody );
      }

      m_e85ParkingBrakeEngaged = false;
      m_e85ParkingBrakeHasOriginalMotionControl = false;
    }

    private AGXUnity.RigidBody ResolveE85ParkingBrakeBody()
    {
      if ( ExcavatorRigLocator.IsSelectable( m_e85ParkingBrakeBody ) )
        return m_e85ParkingBrakeBody;

      if ( m_e85Excavator == null )
        return null;

      var bodyTransform = !string.IsNullOrWhiteSpace( m_e85ParkingBrakeBodyPath ) ?
                          m_e85Excavator.transform.Find( m_e85ParkingBrakeBodyPath ) :
                          null;
      if ( bodyTransform == null )
        bodyTransform = FindChildRecursive( m_e85Excavator.transform, "UnderCarriageBody" );

      m_e85ParkingBrakeBody = bodyTransform != null ?
                              bodyTransform.GetComponent<AGXUnity.RigidBody>() :
                              null;
      if ( !ExcavatorRigLocator.IsSelectable( m_e85ParkingBrakeBody ) )
        m_e85ParkingBrakeBody = FindRigidBodyByNameRecursive( m_e85Excavator.transform, "UnderCarriageBody" );

      return m_e85ParkingBrakeBody;
    }

    private static void ClearNativeForceAndTorque( AGXUnity.RigidBody body )
    {
      if ( body?.Native == null )
        return;

      body.Native.setForce( Vector3.zero.ToHandedVec3() );
      body.Native.setTorque( Vector3.zero.ToHandedVec3() );
    }

    private bool TryLockAxisToNormalizedPosition( Constraint constraint,
                                                  ActuatorNormalizationRange range,
                                                  float normalizedPosition,
                                                  string axisName,
                                                  List<string> warnings )
    {
      if ( float.IsNaN( normalizedPosition ) )
        return false;

      if ( constraint == null ) {
        warnings?.Add( $"{axisName}_constraint_missing" );
        return false;
      }

      if ( range == null ) {
        warnings?.Add( $"{axisName}_normalization_range_missing" );
        return false;
      }

      var targetPosition = range.Denormalize( normalizedPosition );
      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController != null ) {
        speedController.Speed = 0.0f;
        speedController.LockAtZeroSpeed = false;
        speedController.Enable = false;
      }

      var lockController = constraint.GetController<LockController>();
      if ( lockController == null ) {
        warnings?.Add( $"{axisName}_lock_controller_missing" );
        return false;
      }

      lockController.Position = targetPosition;
      lockController.Enable = true;
      return true;
    }

    private void ZeroMachineRigidBodyVelocities()
    {
      var root = MachineRoot;
      if ( root == null )
        return;

      foreach ( var body in root.GetComponentsInChildren<AGXUnity.RigidBody>( true ) ) {
        if ( body == null )
          continue;

        body.LinearVelocity = Vector3.zero;
        body.AngularVelocity = Vector3.zero;
        ClearNativeForceAndTorque( body );
      }
    }

    private void SetSwing( float value, bool immediateStop )
    {
      var lockNeutralSwing = ShouldLockYuLongSwingAtNeutral( value );
      ApplyLimitedAxisCommand( m_swingActuator,
                               m_swingConstraint,
                               m_softLimitSwingRange,
                               lockNeutralSwing ?
                                 0.0f :
                                 ApplyMachineAxisDirection( value, m_e85SwingDirection, m_yuLongSwingDirection ),
                               immediateStop || lockNeutralSwing );
    }

    private void SetBoom( float value, bool immediateStop )
    {
      ApplyLimitedAxisCommand( m_boomActuator,
                               GetReferenceConstraint( m_boomConstraints ),
                               m_softLimitBoomRange,
                               ApplyMachineAxisDirection( value, m_e85BoomDirection, m_yuLongBoomDirection ),
                               immediateStop );
    }

    private void SetStick( float value, bool immediateStop )
    {
      ApplyLimitedAxisCommand( m_stickActuator,
                               m_stickConstraint,
                               m_softLimitStickRange,
                               ApplyMachineAxisDirection( value, m_e85StickDirection, m_yuLongStickDirection ),
                               immediateStop );
    }

    private void SetBucket( float value, bool immediateStop )
    {
      ApplyLimitedAxisCommand( m_bucketActuator,
                               m_bucketConstraint,
                               m_softLimitBucketRange,
                               ApplyMachineAxisDirection( value, m_e85BucketDirection, m_yuLongBucketDirection ),
                               immediateStop );
    }

    private void ApplyLimitedAxisCommand( IExcavatorAxisActuator actuator,
                                          Constraint constraint,
                                          ActuatorNormalizationRange range,
                                          float command,
                                          bool immediateStop )
    {
      if ( actuator == null )
        return;

      var softLimitStop = ShouldStopAtNormalizationSoftLimit( constraint, range, command );
      actuator.Apply( softLimitStop ? 0.0f : command,
                      immediateStop || ( softLimitStop && m_lockAtNormalizationSoftLimit ) );
    }

    private bool ShouldStopAtNormalizationSoftLimit( Constraint constraint,
                                                    ActuatorNormalizationRange range,
                                                    float command )
    {
      if ( !ShouldUseNormalizationSoftLimits() ||
           constraint == null ||
           range == null ||
           !EnsureNormalizationSoftLimitProfileLoaded() )
        return false;

      var span = range.Max - range.Min;
      if ( Mathf.Abs( span ) < 1.0e-5f )
        return false;

      var normalizedPosition = ( constraint.GetCurrentAngle() - range.Min ) / span;
      var normalizedCommandDirection = command * Mathf.Sign( span );
      var tolerance = Mathf.Max( 0.0f, m_normalizationSoftLimitTolerance );

      if ( normalizedPosition <= 0.0f - tolerance &&
           normalizedCommandDirection <= SoftLimitCommandThreshold )
        return true;

      if ( normalizedPosition >= 1.0f + tolerance &&
           normalizedCommandDirection >= -SoftLimitCommandThreshold )
        return true;

      return false;
    }

    private bool ShouldUseNormalizationSoftLimits()
    {
      if ( !m_enableNormalizationSoftLimits )
        return false;

      return !m_normalizationSoftLimitsYuLongOnly ||
             m_machineKind == ExcavatorMachineRigKind.YuLong;
    }

    private float ApplyMachineAxisDirection( float value, float e85Direction, float yuLongDirection )
    {
      if ( m_machineKind == ExcavatorMachineRigKind.BobcatE85 )
        return value * NormalizeAxisDirection( e85Direction );

      if ( m_machineKind == ExcavatorMachineRigKind.YuLong )
        return value * NormalizeAxisDirection( yuLongDirection );

      return value;
    }

    private static float NormalizeAxisDirection( float direction )
    {
      return Mathf.Approximately( direction, 0.0f ) ? 1.0f : Mathf.Sign( direction );
    }

    private bool ShouldLockYuLongSwingAtNeutral( float command )
    {
      return m_machineKind == ExcavatorMachineRigKind.YuLong &&
             m_yuLongLockSwingAtNeutral &&
             Mathf.Abs( command ) <= Mathf.Max( 0.0f, m_yuLongSwingNeutralLockDeadZone );
    }

    private bool EnsureNormalizationSoftLimitProfileLoaded()
    {
      if ( !m_enableNormalizationSoftLimits )
        return false;

      if ( m_normalizationSoftLimitProfileLoaded )
        return true;

      if ( m_normalizationSoftLimitLoadAttempted )
        return false;

      m_normalizationSoftLimitLoadAttempted = true;
      var profilePath = string.IsNullOrWhiteSpace( m_normalizationSoftLimitProfilePath ) ?
                        YuLongDefaultNormalizationSoftLimitProfilePath :
                        m_normalizationSoftLimitProfilePath;
      var absolutePath = ResolveProjectPath( profilePath );
      if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) ) {
        LogNormalizationSoftLimitWarning( $"Normalization soft-limit profile not found: {profilePath}" );
        return false;
      }

      try {
        var profile = JsonUtility.FromJson<ActuatorNormalizationProfile>( File.ReadAllText( absolutePath ) );
        if ( profile == null ) {
          LogNormalizationSoftLimitWarning( $"Normalization soft-limit profile could not be parsed: {profilePath}" );
          return false;
        }

        ApplyNormalizationSoftLimitProfile( profile );
        m_normalizationSoftLimitProfileLoaded = true;
        return true;
      }
      catch ( System.Exception exception ) {
        LogNormalizationSoftLimitWarning( $"Normalization soft-limit profile load failed: {exception.Message}" );
        return false;
      }
    }

    private void ApplyNormalizationSoftLimitProfile( ActuatorNormalizationProfile profile )
    {
      ApplyNormalizationSoftLimitAxisProfile( m_softLimitSwingRange, profile.swing );
      ApplyNormalizationSoftLimitAxisProfile( m_softLimitBoomRange, profile.boom );
      ApplyNormalizationSoftLimitAxisProfile( m_softLimitStickRange, profile.stick );
      ApplyNormalizationSoftLimitAxisProfile( m_softLimitBucketRange, profile.bucket );
    }

    private static void ApplyNormalizationSoftLimitAxisProfile( ActuatorNormalizationRange range,
                                                               ActuatorNormalizationAxisProfile profile )
    {
      if ( range == null || profile == null || Mathf.Abs( profile.max - profile.min ) < 1.0e-5f )
        return;

      range.Set( profile.min, profile.max );
    }

    private void LogNormalizationSoftLimitWarning( string message )
    {
      if ( m_normalizationSoftLimitLoadWarningLogged )
        return;

      Debug.LogWarning( message, this );
      m_normalizationSoftLimitLoadWarningLogged = true;
    }

    private static string ResolveProjectPath( string path )
    {
      if ( string.IsNullOrWhiteSpace( path ) )
        return string.Empty;

      var normalized = path.Trim().Replace( '\\', '/' );
      if ( Path.IsPathRooted( normalized ) )
        return normalized;

      if ( normalized.Equals( "Assets", StringComparison.OrdinalIgnoreCase ) )
        return Application.dataPath;

      if ( normalized.StartsWith( "Assets/", StringComparison.OrdinalIgnoreCase ) ) {
        var relativeToAssets = normalized.Substring( "Assets/".Length );
        return Path.Combine( Application.dataPath,
                             relativeToAssets.Replace( '/', Path.DirectorySeparatorChar ) );
      }

      return Path.Combine( Application.dataPath,
                           normalized.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static Constraint GetReferenceConstraint( Constraint[] constraints )
    {
      if ( constraints == null )
        return null;

      foreach ( var constraint in constraints ) {
        if ( constraint != null )
          return constraint;
      }

      return null;
    }

    private bool EnsureAxisActuators()
    {
      if ( m_machineComponent == null && !ResolveReferences() )
        return false;

      if ( m_axisActuatorsInitialized && m_axisActuatorRig == m_machineComponent ) {
        if ( m_pendingHydraulicSwingActuatorRetry )
          RetryHydraulicSwingActuator();

        return true;
      }

      if ( m_limits == null )
        m_limits = new ExcavatorActuationLimits();

      ResolveRigConstraints();
      m_axisActuatorRig = m_machineComponent;
      m_swingActuator = CreateSwingActuator();
      m_boomActuator = m_boomConstraints != null && m_boomConstraints.Length > 0 ?
                       new TargetSpeedConstraintAxisActuator( m_boomConstraints,
                                                              () => Limits.Boom.MaxSpeed,
                                                              GetBoomMaxAcceleration,
                                                              GetSimulationDeltaTime ) :
                       null;
      m_stickActuator = m_stickConstraint != null ?
                        new TargetSpeedConstraintAxisActuator( m_stickConstraint,
                                                               () => Limits.Stick.MaxSpeed,
                                                               GetStickMaxAcceleration,
                                                               GetSimulationDeltaTime ) :
                        null;
      m_bucketActuator = m_bucketConstraint != null ?
                         new TargetSpeedConstraintAxisActuator( m_bucketConstraint,
                                                                () => Limits.Bucket.MaxSpeed,
                                                                GetBucketMaxAcceleration,
                                                                GetSimulationDeltaTime ) :
                         null;
      m_leftTrackActuator = m_leftTrackConstraint != null ?
                            new TargetSpeedConstraintAxisActuator( m_leftTrackConstraint,
                                                                   m_limits.MaxRotationalAcceleration,
                                                                   GetSimulationDeltaTime ) :
                            null;
      m_rightTrackActuator = m_rightTrackConstraint != null ?
                             new TargetSpeedConstraintAxisActuator( m_rightTrackConstraint,
                                                                    m_limits.MaxRotationalAcceleration,
                                                                    GetSimulationDeltaTime ) :
                             null;
      m_axisActuatorsInitialized = true;
      return true;
    }

    private IExcavatorAxisActuator CreateSwingActuator()
    {
      if ( m_swingConstraint == null )
        return null;

      if ( m_machineKind == ExcavatorMachineRigKind.Cat365 &&
           m_swingBackend == ExcavatorAxisActuatorBackend.Hydraulic ) {
        m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );
        if ( m_hydraulicSystem != null &&
             m_hydraulicSystem.TryCreateSwingActuator( m_swingConstraint, out var hydraulicActuator ) ) {
          m_pendingHydraulicSwingActuatorRetry = false;
          return hydraulicActuator;
        }

        m_pendingHydraulicSwingActuatorRetry = true;

        if ( m_hydraulicSystem == null && !m_hydraulicSwingFallbackWarningLogged ) {
          Debug.LogWarning(
            "Swing backend is set to Hydraulic, but no ExcavatorHydraulicSystem was found. Falling back to TargetSpeed.",
            this );
          m_hydraulicSwingFallbackWarningLogged = true;
        }
      }

      return new TargetSpeedConstraintAxisActuator( m_swingConstraint,
                                                   () => Limits.Swing.MaxSpeed,
                                                   GetSwingMaxAcceleration,
                                                   GetSimulationDeltaTime );
    }

    private void RetryHydraulicSwingActuator()
    {
      if ( m_swingBackend != ExcavatorAxisActuatorBackend.Hydraulic ||
           m_machineKind != ExcavatorMachineRigKind.Cat365 ||
           m_swingConstraint == null )
        return;

      m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );
      if ( m_hydraulicSystem == null )
        return;

      if ( m_hydraulicSystem.TryCreateSwingActuator( m_swingConstraint, out var hydraulicActuator ) ) {
        m_swingActuator = hydraulicActuator;
        m_pendingHydraulicSwingActuatorRetry = false;
      }
    }

    private float GetSwingMaxAcceleration()
    {
      return Limits.Swing.ResolveMaxAcceleration( Limits.MaxRotationalAcceleration );
    }

    private float GetBoomMaxAcceleration()
    {
      return Limits.Boom.ResolveMaxAcceleration( GetArmAccelerationFallback() );
    }

    private float GetStickMaxAcceleration()
    {
      return Limits.Stick.ResolveMaxAcceleration( GetArmAccelerationFallback() );
    }

    private float GetBucketMaxAcceleration()
    {
      return Limits.Bucket.ResolveMaxAcceleration( GetArmAccelerationFallback() );
    }

    private float GetArmAccelerationFallback()
    {
      return m_machineKind == ExcavatorMachineRigKind.YuLong ?
             Limits.MaxRotationalAcceleration :
             Limits.MaxLinearAcceleration;
    }

    private float GetDirectTrackSpeedScale()
    {
      return Mathf.Max( 0.0f, m_directTrackSpeedScale );
    }

    private void ResolveRigConstraints()
    {
      m_swingConstraint = null;
      m_boomConstraints = null;
      m_stickConstraint = null;
      m_bucketConstraint = null;
      m_leftTrackConstraint = null;
      m_rightTrackConstraint = null;

      if ( m_machineKind == ExcavatorMachineRigKind.BobcatE85 && m_e85Excavator != null ) {
        m_swingConstraint = m_e85Excavator.CabinHinge;
        m_boomConstraints = m_e85Excavator.ArmPrismatic != null ?
                            new[] { m_e85Excavator.ArmPrismatic } :
                            new Constraint[0];
        m_stickConstraint = m_e85Excavator.StickPrismatic;
        m_bucketConstraint = m_e85Excavator.BucketPrismatic;
        m_leftTrackConstraint = m_e85Excavator.LeftHinge;
        m_rightTrackConstraint = m_e85Excavator.RightHinge;
        return;
      }

      if ( m_machineKind == ExcavatorMachineRigKind.YuLong && m_yuLongExcavator != null ) {
        m_yuLongExcavator.ResolveReferences();
        m_swingConstraint = m_yuLongExcavator.SwingHinge;
        m_boomConstraints = m_yuLongExcavator.BoomConstraint != null ?
                            new[] { m_yuLongExcavator.BoomConstraint } :
                            new Constraint[0];
        m_stickConstraint = m_yuLongExcavator.StickConstraint;
        m_bucketConstraint = m_yuLongExcavator.BucketConstraint;
        return;
      }

      if ( m_excavator == null )
        return;

      m_swingConstraint = m_excavator.SwingHinge;
      m_boomConstraints = m_excavator.BoomPrismatics;
      m_stickConstraint = m_excavator.StickPrismatic;
      m_bucketConstraint = m_excavator.BucketPrismatic;

      foreach ( var sprocketHinge in m_excavator.SprocketHinges ) {
        if ( sprocketHinge == null )
          continue;

        if ( m_leftTrackConstraint == null )
          m_leftTrackConstraint = sprocketHinge;
        else if ( m_rightTrackConstraint == null )
          m_rightTrackConstraint = sprocketHinge;
      }
    }

    private float GetSimulationDeltaTime()
    {
      var simulation = GetSimulation();
      return simulation != null ? (float)simulation.getTimeStep() : Time.deltaTime;
    }

    private bool ResolveReferences()
    {
      var previousMachineComponent = m_machineComponent;
      var previousMachineKind = m_machineKind;

      var assignedCatExcavatorIsActive = ExcavatorRigLocator.IsSelectable( m_excavator );
      var assignedE85ExcavatorIsActive = ExcavatorRigLocator.IsSelectable( m_e85Excavator );
      var assignedYuLongExcavatorIsActive = ExcavatorRigLocator.IsSelectable( m_yuLongExcavator );

      if ( !ExcavatorRigLocator.IsSelectable( m_machineRoot ) ) {
        if ( assignedYuLongExcavatorIsActive )
          m_machineRoot = m_yuLongExcavator.transform;
        else if ( assignedE85ExcavatorIsActive )
          m_machineRoot = m_e85Excavator.transform;
        else if ( assignedCatExcavatorIsActive )
          m_machineRoot = m_excavator.transform;
      }

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

      m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );

      if ( assignedYuLongExcavatorIsActive ||
           ( !assignedCatExcavatorIsActive && !assignedE85ExcavatorIsActive && m_yuLongExcavator != null ) ) {
        m_machineComponent = m_yuLongExcavator;
        m_machineKind = ExcavatorMachineRigKind.YuLong;
      }
      else if ( assignedE85ExcavatorIsActive || ( !assignedCatExcavatorIsActive && m_e85Excavator != null ) ) {
        m_machineComponent = m_e85Excavator;
        m_machineKind = ExcavatorMachineRigKind.BobcatE85;
      }
      else if ( m_excavator != null ) {
        m_machineComponent = m_excavator;
        m_machineKind = ExcavatorMachineRigKind.Cat365;
      }
      else if ( m_e85Excavator != null ) {
        m_machineComponent = m_e85Excavator;
        m_machineKind = ExcavatorMachineRigKind.BobcatE85;
      }
      else if ( m_yuLongExcavator != null ) {
        m_machineComponent = m_yuLongExcavator;
        m_machineKind = ExcavatorMachineRigKind.YuLong;
      }
      else {
        m_machineComponent = null;
        m_machineKind = ExcavatorMachineRigKind.None;
      }

      if ( !ExcavatorRigLocator.IsSelectable( m_machineRoot ) && m_machineComponent != null )
        m_machineRoot = m_machineComponent.transform;

      if ( previousMachineComponent != m_machineComponent || previousMachineKind != m_machineKind )
        InvalidateAxisActuators();

      var semanticRoot = ExcavatorRigLocator.IsSelectable( m_machineRoot ) ?
                         m_machineRoot :
                         m_machineComponent != null ? m_machineComponent.transform : null;
      if ( semanticRoot != null )
        m_bucketReference = ExcavatorRigLocator.ResolveBucketReference( semanticRoot, m_bucketReference );
      else if ( !ExcavatorRigLocator.IsSelectable( m_bucketReference ) )
        m_bucketReference = null;

      return m_machineComponent != null;
    }

    private static Transform FindChildRecursive( Transform root, string targetName )
    {
      if ( root == null )
        return null;

      if ( root.name == targetName )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, targetName );
        if ( result != null )
          return result;
      }

      return null;
    }

    private static AGXUnity.RigidBody FindRigidBodyByNameRecursive( Transform root, string targetName )
    {
      if ( root == null )
        return null;

      foreach ( var body in root.GetComponentsInChildren<AGXUnity.RigidBody>( true ) ) {
        if ( body != null &&
             body.name == targetName &&
             ExcavatorRigLocator.IsSelectable( body ) )
          return body;
      }

      return null;
    }

    private void InvalidateAxisActuators()
    {
      ReleaseE85ParkingBrake();
      m_axisActuatorRig = null;
      m_axisActuatorsInitialized = false;
      m_swingActuator = null;
      m_boomActuator = null;
      m_stickActuator = null;
      m_bucketActuator = null;
      m_leftTrackActuator = null;
      m_rightTrackActuator = null;
      m_swingConstraint = null;
      m_boomConstraints = null;
      m_stickConstraint = null;
      m_bucketConstraint = null;
      m_leftTrackConstraint = null;
      m_rightTrackConstraint = null;
      m_pendingHydraulicSwingActuatorRetry = false;
    }
  }
}

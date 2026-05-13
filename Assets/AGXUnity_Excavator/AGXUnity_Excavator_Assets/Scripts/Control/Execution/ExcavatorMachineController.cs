using AGXUnity;
using AGXUnity.Utils;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public enum ExcavatorMachineRigKind
  {
    None,
    Cat365,
    BobcatE85
  }

  public class ExcavatorMachineController : ScriptComponent
  {
    [SerializeField]
    private Excavator m_excavator = null;

    [SerializeField]
    private global::ExcavatorE85 m_e85Excavator = null;

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
    private bool m_startWithEngineRunning = true;

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

    public ExcavatorActuationCommand LastActuationCommand { get; private set; }
    public bool IsEngineRunning { get; private set; } = true;
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

    private void SetSwing( float value, bool immediateStop )
    {
      m_swingActuator?.Apply( ApplyE85AxisDirection( value, m_e85SwingDirection ), immediateStop );
    }

    private void SetBoom( float value, bool immediateStop )
    {
      m_boomActuator?.Apply( ApplyE85AxisDirection( value, m_e85BoomDirection ), immediateStop );
    }

    private void SetStick( float value, bool immediateStop )
    {
      m_stickActuator?.Apply( ApplyE85AxisDirection( value, m_e85StickDirection ), immediateStop );
    }

    private void SetBucket( float value, bool immediateStop )
    {
      m_bucketActuator?.Apply( ApplyE85AxisDirection( value, m_e85BucketDirection ), immediateStop );
    }

    private float ApplyE85AxisDirection( float value, float e85Direction )
    {
      if ( m_machineKind != ExcavatorMachineRigKind.BobcatE85 )
        return value;

      return value * NormalizeAxisDirection( e85Direction );
    }

    private static float NormalizeAxisDirection( float direction )
    {
      return Mathf.Approximately( direction, 0.0f ) ? 1.0f : Mathf.Sign( direction );
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
                                                              m_limits.MaxLinearAcceleration,
                                                              GetSimulationDeltaTime ) :
                       null;
      m_stickActuator = m_stickConstraint != null ?
                        new TargetSpeedConstraintAxisActuator( m_stickConstraint,
                                                               m_limits.MaxLinearAcceleration,
                                                               GetSimulationDeltaTime ) :
                        null;
      m_bucketActuator = m_bucketConstraint != null ?
                         new TargetSpeedConstraintAxisActuator( m_bucketConstraint,
                                                                m_limits.MaxLinearAcceleration,
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
                                                   m_limits.MaxRotationalAcceleration,
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

      if ( !ExcavatorRigLocator.IsSelectable( m_machineRoot ) ) {
        if ( assignedE85ExcavatorIsActive )
          m_machineRoot = m_e85Excavator.transform;
        else if ( assignedCatExcavatorIsActive )
          m_machineRoot = m_excavator.transform;
      }

      if ( ExcavatorRigLocator.IsSelectable( m_machineRoot ) ) {
        m_excavator = ExcavatorRigLocator.ResolveActiveComponentInRoot( m_machineRoot, m_excavator );
        m_e85Excavator = ExcavatorRigLocator.ResolveActiveComponentInRoot( m_machineRoot, m_e85Excavator );
      }
      else {
        m_excavator = ExcavatorRigLocator.ResolveActiveComponent( this, m_excavator );
        m_e85Excavator = ExcavatorRigLocator.ResolveActiveComponent( this, m_e85Excavator );
      }

      m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );

      if ( assignedE85ExcavatorIsActive || ( !assignedCatExcavatorIsActive && m_e85Excavator != null ) ) {
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

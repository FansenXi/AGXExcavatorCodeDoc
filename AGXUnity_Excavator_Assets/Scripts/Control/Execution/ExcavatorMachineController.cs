using AGXUnity;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public class ExcavatorMachineController : ScriptComponent
  {
    [SerializeField]
    private Excavator m_excavator = null;

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
    private bool m_startWithEngineRunning = true;

    private Excavator m_axisActuatorRig = null;
    private bool m_axisActuatorsInitialized = false;
    private IExcavatorAxisActuator m_swingActuator = null;
    private IExcavatorAxisActuator m_boomActuator = null;
    private IExcavatorAxisActuator m_stickActuator = null;
    private IExcavatorAxisActuator m_bucketActuator = null;
    private bool m_pendingHydraulicSwingActuatorRetry = false;
    private bool m_hydraulicSwingFallbackWarningLogged = false;

    public ExcavatorActuationCommand LastActuationCommand { get; private set; }
    public bool IsEngineRunning { get; private set; } = true;

    public Transform BucketReference
    {
      get
      {
        ResolveReferences();
        return m_bucketReference;
      }
    }

    protected override bool Initialize()
    {
      ResolveReferences();
      if ( m_excavator == null ) {
        Debug.LogError( "Unable to initialize ExcavatorMachineController because no Excavator component was found.", this );
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
      if ( m_excavator == null && !ResolveReferences() )
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
      if ( m_excavator == null && !ResolveReferences() )
        return;

      IsEngineRunning = true;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
    }

    public void StopEngine()
    {
      if ( m_excavator == null && !ResolveReferences() )
        return;

      IsEngineRunning = false;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      ApplyNeutralActuation( true );
    }

    public void StopMotion()
    {
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      ApplyNeutralActuation( true );
    }

    private void ApplyActuation( ExcavatorActuationCommand command, bool immediateConstraintStop )
    {
      if ( m_excavator == null && !ResolveReferences() )
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
      var clutch = new Vector2(
        Mathf.Abs( leftTrack ) > 0.0f ? 1.0f : 0.0f,
        Mathf.Abs( rightTrack ) > 0.0f ? 1.0f : 0.0f );

      m_excavator.ClutchEfficiency = clutch;
      m_excavator.BrakeEfficiency = Vector2.one - clutch;
      m_excavator.GearRatio = new Vector2( -leftTrack, -rightTrack ) * m_trackSpeedScale;
    }

    private void SetThrottle( float value )
    {
      m_excavator.Throttle = Mathf.Clamp01( value );
    }

    private float ApplyTrackDeadZone( float command )
    {
      return Mathf.Abs( command ) >= m_trackCommandDeadZone ? command : 0.0f;
    }

    private void SetSwing( float value, bool immediateStop )
    {
      m_swingActuator?.Apply( value, immediateStop );
    }

    private void SetBoom( float value, bool immediateStop )
    {
      m_boomActuator?.Apply( value, immediateStop );
    }

    private void SetStick( float value, bool immediateStop )
    {
      m_stickActuator?.Apply( value, immediateStop );
    }

    private void SetBucket( float value, bool immediateStop )
    {
      m_bucketActuator?.Apply( value, immediateStop );
    }

    private bool EnsureAxisActuators()
    {
      if ( m_excavator == null && !ResolveReferences() )
        return false;

      if ( m_axisActuatorsInitialized && m_axisActuatorRig == m_excavator ) {
        if ( m_pendingHydraulicSwingActuatorRetry )
          RetryHydraulicSwingActuator();

        return true;
      }

      if ( m_limits == null )
        m_limits = new ExcavatorActuationLimits();

      m_axisActuatorRig = m_excavator;
      m_swingActuator = CreateSwingActuator();
      m_boomActuator = m_excavator.BoomPrismatics != null && m_excavator.BoomPrismatics.Length > 0 ?
                       new TargetSpeedConstraintAxisActuator( m_excavator.BoomPrismatics,
                                                              m_limits.MaxLinearAcceleration,
                                                              GetSimulationDeltaTime ) :
                       null;
      m_stickActuator = m_excavator.StickPrismatic != null ?
                        new TargetSpeedConstraintAxisActuator( m_excavator.StickPrismatic,
                                                               m_limits.MaxLinearAcceleration,
                                                               GetSimulationDeltaTime ) :
                        null;
      m_bucketActuator = m_excavator.BucketPrismatic != null ?
                         new TargetSpeedConstraintAxisActuator( m_excavator.BucketPrismatic,
                                                                m_limits.MaxLinearAcceleration,
                                                                GetSimulationDeltaTime ) :
                         null;
      m_axisActuatorsInitialized = true;
      return true;
    }

    private IExcavatorAxisActuator CreateSwingActuator()
    {
      if ( m_excavator.SwingHinge == null )
        return null;

      if ( m_swingBackend == ExcavatorAxisActuatorBackend.Hydraulic ) {
        m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );
        if ( m_hydraulicSystem != null &&
             m_hydraulicSystem.TryCreateSwingActuator( m_excavator.SwingHinge, out var hydraulicActuator ) ) {
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

      return new TargetSpeedConstraintAxisActuator( m_excavator.SwingHinge,
                                                   m_limits.MaxRotationalAcceleration,
                                                   GetSimulationDeltaTime );
    }

    private void RetryHydraulicSwingActuator()
    {
      if ( m_swingBackend != ExcavatorAxisActuatorBackend.Hydraulic ||
           m_excavator == null ||
           m_excavator.SwingHinge == null )
        return;

      m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );
      if ( m_hydraulicSystem == null )
        return;

      if ( m_hydraulicSystem.TryCreateSwingActuator( m_excavator.SwingHinge, out var hydraulicActuator ) ) {
        m_swingActuator = hydraulicActuator;
        m_pendingHydraulicSwingActuatorRetry = false;
      }
    }

    private float GetSimulationDeltaTime()
    {
      var simulation = GetSimulation();
      return simulation != null ? (float)simulation.getTimeStep() : Time.deltaTime;
    }

    private bool ResolveReferences()
    {
      var previousExcavator = m_excavator;
      m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );
      m_hydraulicSystem = ExcavatorRigLocator.ResolveComponent( this, m_hydraulicSystem );
      if ( previousExcavator != m_excavator )
        InvalidateAxisActuators();

      if ( m_bucketReference == null && m_excavator != null )
        m_bucketReference = FindChildRecursive( m_excavator.transform, "Bucket" );

      return m_excavator != null;
    }

    private void InvalidateAxisActuators()
    {
      m_axisActuatorRig = null;
      m_axisActuatorsInitialized = false;
      m_swingActuator = null;
      m_boomActuator = null;
      m_stickActuator = null;
      m_bucketActuator = null;
      m_pendingHydraulicSwingActuatorRetry = false;
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
  }
}

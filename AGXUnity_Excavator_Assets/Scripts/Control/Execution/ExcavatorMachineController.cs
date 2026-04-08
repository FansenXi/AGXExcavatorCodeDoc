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
    private ExcavatorActuationLimits m_limits = new ExcavatorActuationLimits();

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_trackSpeedScale = 0.2f;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_trackCommandDeadZone = 0.05f;

    [SerializeField]
    private bool m_startWithEngineRunning = true;

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

      return true;
    }

    private void Awake()
    {
      ResolveReferences();
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
      if ( m_excavator.SwingHinge == null )
        return;

      var currentSpeed = (float)m_excavator.SwingHinge.Native.asHinge().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxRotationalAcceleration );
      SetSpeed( m_excavator.SwingHinge, newSpeed, immediateStop );
    }

    private void SetBoom( float value, bool immediateStop )
    {
      if ( m_excavator.BoomPrismatics == null || m_excavator.BoomPrismatics.Length == 0 )
        return;

      var currentSpeed = (float)m_excavator.BoomPrismatics[ 0 ].Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      foreach ( var prismatic in m_excavator.BoomPrismatics )
        SetSpeed( prismatic, newSpeed, immediateStop );
    }

    private void SetStick( float value, bool immediateStop )
    {
      if ( m_excavator.StickPrismatic == null )
        return;

      var currentSpeed = (float)m_excavator.StickPrismatic.Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      SetSpeed( m_excavator.StickPrismatic, newSpeed, immediateStop );
    }

    private void SetBucket( float value, bool immediateStop )
    {
      if ( m_excavator.BucketPrismatic == null )
        return;

      var currentSpeed = (float)m_excavator.BucketPrismatic.Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      SetSpeed( m_excavator.BucketPrismatic, newSpeed, immediateStop );
    }

    private float CalculateSpeed( float desiredSpeed, float currentSpeed, float maxAcceleration )
    {
      var simulation = GetSimulation();
      var deltaTime = simulation != null ? (float)simulation.getTimeStep() : Time.deltaTime;
      var maxDeltaSpeed = Mathf.Abs( maxAcceleration * Mathf.Max( deltaTime, 0.0f ) );
      return Mathf.Clamp( desiredSpeed, currentSpeed - maxDeltaSpeed, currentSpeed + maxDeltaSpeed );
    }

    private void SetSpeed( Constraint constraint, float speed, bool immediateStop )
    {
      if ( constraint == null )
        return;

      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController == null )
        return;

      var lockController = constraint.GetController<LockController>();

      if ( immediateStop && Mathf.Abs( speed ) < 1.0e-4f ) {
        speedController.Speed = 0.0f;
        speedController.LockAtZeroSpeed = false;

        if ( lockController != null ) {
          speedController.Enable = false;
          lockController.Position = constraint.GetCurrentAngle();
          lockController.Enable = true;
        }
        else {
          speedController.Enable = true;
        }

        return;
      }

      speedController.LockAtZeroSpeed = false;
      speedController.Enable = true;
      speedController.Speed = Mathf.Abs( speed ) < 1.0e-4f ? 0.0f : speed;
      if ( lockController != null )
        lockController.Enable = false;
    }

    private bool ResolveReferences()
    {
      m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );

      if ( m_bucketReference == null && m_excavator != null )
        m_bucketReference = FindChildRecursive( m_excavator.transform, "Bucket" );

      return m_excavator != null;
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

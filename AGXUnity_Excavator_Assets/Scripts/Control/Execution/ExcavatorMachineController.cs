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

    public ExcavatorActuationCommand LastActuationCommand { get; private set; }

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
    }

    public void ApplyActuationCommand( ExcavatorActuationCommand command )
    {
      if ( m_excavator == null && !ResolveReferences() )
        return;

      LastActuationCommand = command.ClampAxes();

      SetThrottle( LastActuationCommand.Throttle );
      ApplyDriveTrain( LastActuationCommand.Drive, LastActuationCommand.Steer, LastActuationCommand.Throttle );
      SetBoom( LastActuationCommand.Boom );
      SetBucket( LastActuationCommand.Bucket );
      SetStick( LastActuationCommand.Stick );
      SetSwing( LastActuationCommand.Swing );
    }

    public void StopMotion()
    {
      ApplyActuationCommand( ExcavatorActuationCommand.Zero );
    }

    private void ApplyDriveTrain( float drive, float steer, float throttle )
    {
      if ( Mathf.Abs( throttle ) > 0.0f ) {
        m_excavator.ClutchEfficiency = new Vector2( 1.0f, 1.0f );
        m_excavator.BrakeEfficiency = new Vector2( 0.0f, 0.0f );
      }
      else {
        m_excavator.ClutchEfficiency = new Vector2( 0.0f, 0.0f );
        m_excavator.BrakeEfficiency = new Vector2( 1.0f, 1.0f );
      }

      var gear = Vector2.zero;
      if ( Mathf.Abs( drive ) > 0.0f ) {
        gear[ 0 ] = -Mathf.Sign( drive );
        gear[ 1 ] = -Mathf.Sign( drive );
      }

      var gearRatio = Mathf.Abs( steer ) > 0.0f ? 1.0f : 0.2f;
      if ( Mathf.Abs( steer ) > 0.0f ) {
        gear[ 0 ] = -Mathf.Sign( steer );
        gear[ 1 ] = Mathf.Sign( steer );
      }

      m_excavator.GearRatio = gear * gearRatio;
    }

    private void SetThrottle( float value )
    {
      m_excavator.Throttle = Mathf.Clamp01( value );
    }

    private void SetSwing( float value )
    {
      if ( m_excavator.SwingHinge == null )
        return;

      var currentSpeed = (float)m_excavator.SwingHinge.Native.asHinge().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxRotationalAcceleration );
      SetSpeed( m_excavator.SwingHinge, newSpeed );
    }

    private void SetBoom( float value )
    {
      if ( m_excavator.BoomPrismatics == null || m_excavator.BoomPrismatics.Length == 0 )
        return;

      var currentSpeed = (float)m_excavator.BoomPrismatics[ 0 ].Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      foreach ( var prismatic in m_excavator.BoomPrismatics )
        SetSpeed( prismatic, newSpeed );
    }

    private void SetStick( float value )
    {
      if ( m_excavator.StickPrismatic == null )
        return;

      var currentSpeed = (float)m_excavator.StickPrismatic.Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      SetSpeed( m_excavator.StickPrismatic, newSpeed );
    }

    private void SetBucket( float value )
    {
      if ( m_excavator.BucketPrismatic == null )
        return;

      var currentSpeed = (float)m_excavator.BucketPrismatic.Native.asPrismatic().getCurrentSpeed();
      var newSpeed = CalculateSpeed( value, currentSpeed, m_limits.MaxLinearAcceleration );
      SetSpeed( m_excavator.BucketPrismatic, newSpeed );
    }

    private float CalculateSpeed( float desiredSpeed, float currentSpeed, float maxAcceleration )
    {
      var simulation = GetSimulation();
      var deltaTime = simulation != null ? (float)simulation.getTimeStep() : Time.deltaTime;
      var maxDeltaSpeed = Mathf.Abs( maxAcceleration * Mathf.Max( deltaTime, 0.0f ) );
      return Mathf.Clamp( desiredSpeed, currentSpeed - maxDeltaSpeed, currentSpeed + maxDeltaSpeed );
    }

    private void SetSpeed( Constraint constraint, float speed )
    {
      if ( constraint == null )
        return;

      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController == null )
        return;

      speedController.Enable = true;
      speedController.Speed = Mathf.Abs( speed ) < 1.0e-4f ? 0.0f : speed;

      var lockController = constraint.GetController<LockController>();
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

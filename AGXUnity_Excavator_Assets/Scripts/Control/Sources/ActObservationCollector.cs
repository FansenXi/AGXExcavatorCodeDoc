using AGXUnity;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  [System.Serializable]
  public class ActuatorNormalizationRange
  {
    [SerializeField]
    private float m_min = -1.0f;

    [SerializeField]
    private float m_max = 1.0f;

    public float Normalize( float value )
    {
      if ( Mathf.Abs( m_max - m_min ) < 1.0e-5f )
        return 0.0f;

      return Mathf.Clamp01( Mathf.InverseLerp( m_min, m_max, value ) );
    }
  }

  public class ActObservationCollector : MonoBehaviour
  {
    private struct BaseVelocitySampleState
    {
      public Vector3 LastBasePosition;
      public Quaternion LastBaseRotation;
      public float LastBaseSampleTime;
      public Vector3 LastLinearVelocityLocal;
      public Vector3 LastAngularVelocityLocal;

      public static BaseVelocitySampleState ResetState => new BaseVelocitySampleState
      {
        LastBasePosition = Vector3.zero,
        LastBaseRotation = Quaternion.identity,
        LastBaseSampleTime = -1.0f,
        LastLinearVelocityLocal = Vector3.zero,
        LastAngularVelocityLocal = Vector3.zero
      };
    }

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Excavator m_excavator = null;

    [SerializeField]
    private global::MassVolumeCounter m_massVolumeCounter = null;

    [SerializeField]
    private ActuatorNormalizationRange m_boomRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_stickRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_bucketRange = new ActuatorNormalizationRange();

    private BaseVelocitySampleState m_defaultBaseVelocityState = BaseVelocitySampleState.ResetState;
    private BaseVelocitySampleState m_loggingBaseVelocityState = BaseVelocitySampleState.ResetState;

    private void Awake()
    {
      ResolveReferences();
    }

    public ActObservation Collect( OperatorCommand previousOperatorCommand )
    {
      return CollectInternal( previousOperatorCommand, ref m_defaultBaseVelocityState );
    }

    public ActObservation CollectForLogging( OperatorCommand previousOperatorCommand )
    {
      return CollectInternal( previousOperatorCommand, ref m_loggingBaseVelocityState );
    }

    public void ResetSampling()
    {
      m_defaultBaseVelocityState = BaseVelocitySampleState.ResetState;
      m_loggingBaseVelocityState = BaseVelocitySampleState.ResetState;
    }

    private ActObservation CollectInternal( OperatorCommand previousOperatorCommand,
                                           ref BaseVelocitySampleState baseVelocityState )
    {
      ResolveReferences();

      var observation = new ActObservation
      {
        sim_time_sec = Time.time,
        fixed_dt_sec = Time.fixedDeltaTime,
        previous_operator_command = ActWireOperatorCommand.FromOperatorCommand( previousOperatorCommand.WithoutEpisodeSignals() )
      };

      var baseTransform = m_excavator != null ? m_excavator.transform : transform;
      UpdateBaseVelocity( baseTransform, ref baseVelocityState );

      observation.base_pose_world.Set( baseTransform.position, baseTransform.rotation );
      observation.base_velocity_local.Set(
        baseVelocityState.LastLinearVelocityLocal,
        baseVelocityState.LastAngularVelocityLocal );

      var bucketReference = m_machineController != null ? m_machineController.BucketReference : null;
      if ( bucketReference != null )
        observation.bucket_pose_world.Set( bucketReference.position, bucketReference.rotation );

      observation.actuator_state.boom_position_norm = NormalizeConstraintPosition( m_excavator != null && m_excavator.BoomPrismatics.Length > 0 ? m_excavator.BoomPrismatics[ 0 ] : null, m_boomRange );
      observation.actuator_state.boom_speed = ReadConstraintSpeed( m_excavator != null && m_excavator.BoomPrismatics.Length > 0 ? m_excavator.BoomPrismatics[ 0 ] : null );
      observation.actuator_state.stick_position_norm = NormalizeConstraintPosition( m_excavator != null ? m_excavator.StickPrismatic : null, m_stickRange );
      observation.actuator_state.stick_speed = ReadConstraintSpeed( m_excavator != null ? m_excavator.StickPrismatic : null );
      observation.actuator_state.bucket_position_norm = NormalizeConstraintPosition( m_excavator != null ? m_excavator.BucketPrismatic : null, m_bucketRange );
      observation.actuator_state.bucket_speed = ReadConstraintSpeed( m_excavator != null ? m_excavator.BucketPrismatic : null );
      observation.actuator_state.swing_speed = ReadConstraintSpeed( m_excavator != null ? m_excavator.SwingHinge : null );

      if ( m_massVolumeCounter != null ) {
        observation.task_state.mass_in_bucket_kg = m_massVolumeCounter.MassInBucket;
      }

      return observation;
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );
      m_massVolumeCounter = ExcavatorRigLocator.ResolveComponent( this, m_massVolumeCounter );
    }

    private static void UpdateBaseVelocity( Transform baseTransform, ref BaseVelocitySampleState sampleState )
    {
      var sampleTime = Time.time;
      if ( baseTransform == null ) {
        sampleState.LastLinearVelocityLocal = Vector3.zero;
        sampleState.LastAngularVelocityLocal = Vector3.zero;
        return;
      }

      if ( sampleState.LastBaseSampleTime < 0.0f ) {
        sampleState.LastBasePosition = baseTransform.position;
        sampleState.LastBaseRotation = baseTransform.rotation;
        sampleState.LastBaseSampleTime = sampleTime;
        sampleState.LastLinearVelocityLocal = Vector3.zero;
        sampleState.LastAngularVelocityLocal = Vector3.zero;
        return;
      }

      var elapsedTime = sampleTime - sampleState.LastBaseSampleTime;
      if ( elapsedTime <= 1.0e-5f ) {
        sampleState.LastBasePosition = baseTransform.position;
        sampleState.LastBaseRotation = baseTransform.rotation;
        sampleState.LastBaseSampleTime = sampleTime;
        return;
      }

      var deltaTime = Mathf.Max( elapsedTime, 1.0e-5f );
      var worldLinearVelocity = ( baseTransform.position - sampleState.LastBasePosition ) / deltaTime;
      sampleState.LastLinearVelocityLocal = baseTransform.InverseTransformDirection( worldLinearVelocity );

      var deltaRotation = baseTransform.rotation * Quaternion.Inverse( sampleState.LastBaseRotation );
      deltaRotation.ToAngleAxis( out var angleDegrees, out var axis );
      if ( float.IsNaN( axis.x ) || float.IsInfinity( axis.x ) )
        axis = Vector3.zero;

      if ( angleDegrees > 180.0f )
        angleDegrees -= 360.0f;

      var angularVelocityWorld = axis.normalized * angleDegrees * Mathf.Deg2Rad / deltaTime;
      sampleState.LastAngularVelocityLocal = baseTransform.InverseTransformDirection( angularVelocityWorld );

      sampleState.LastBasePosition = baseTransform.position;
      sampleState.LastBaseRotation = baseTransform.rotation;
      sampleState.LastBaseSampleTime = sampleTime;
    }

    private static float ReadConstraintSpeed( Constraint constraint )
    {
      return constraint != null ? constraint.GetCurrentSpeed() : 0.0f;
    }

    private static float NormalizeConstraintPosition( Constraint constraint, ActuatorNormalizationRange range )
    {
      if ( constraint == null || range == null )
        return 0.0f;

      return range.Normalize( constraint.GetCurrentAngle() );
    }
  }
}

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

    private Vector3 m_lastBasePosition = Vector3.zero;
    private Quaternion m_lastBaseRotation = Quaternion.identity;
    private float m_lastBaseSampleTime = -1.0f;
    private Vector3 m_lastLinearVelocityLocal = Vector3.zero;
    private Vector3 m_lastAngularVelocityLocal = Vector3.zero;

    private void Awake()
    {
      ResolveReferences();
    }

    public ActObservation Collect( OperatorCommand previousOperatorCommand )
    {
      ResolveReferences();

      var observation = new ActObservation
      {
        sim_time_sec = Time.time,
        fixed_dt_sec = Time.fixedDeltaTime,
        previous_operator_command = ActWireOperatorCommand.FromOperatorCommand( previousOperatorCommand.WithoutEpisodeSignals() )
      };

      var baseTransform = m_excavator != null ? m_excavator.transform : transform;
      UpdateBaseVelocity( baseTransform );

      observation.base_pose_world.Set( baseTransform.position, baseTransform.rotation );
      observation.base_velocity_local.Set( m_lastLinearVelocityLocal, m_lastAngularVelocityLocal );

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
        observation.task_state.excavated_mass_kg = m_massVolumeCounter.ExcavatedMass;
        observation.task_state.excavated_volume_m3 = m_massVolumeCounter.ExcavatedVolume;
      }

      return observation;
    }

    private void ResolveReferences()
    {
      if ( m_machineController == null )
        m_machineController = GetComponent<ExcavatorMachineController>();

      if ( m_excavator == null )
        m_excavator = FindObjectOfType<Excavator>();

      if ( m_massVolumeCounter == null )
        m_massVolumeCounter = FindObjectOfType<global::MassVolumeCounter>();
    }

    private void UpdateBaseVelocity( Transform baseTransform )
    {
      var sampleTime = Time.time;
      if ( baseTransform == null ) {
        m_lastLinearVelocityLocal = Vector3.zero;
        m_lastAngularVelocityLocal = Vector3.zero;
        return;
      }

      if ( m_lastBaseSampleTime < 0.0f ) {
        m_lastBasePosition = baseTransform.position;
        m_lastBaseRotation = baseTransform.rotation;
        m_lastBaseSampleTime = sampleTime;
        m_lastLinearVelocityLocal = Vector3.zero;
        m_lastAngularVelocityLocal = Vector3.zero;
        return;
      }

      var deltaTime = Mathf.Max( sampleTime - m_lastBaseSampleTime, 1.0e-5f );
      var worldLinearVelocity = ( baseTransform.position - m_lastBasePosition ) / deltaTime;
      m_lastLinearVelocityLocal = baseTransform.InverseTransformDirection( worldLinearVelocity );

      var deltaRotation = baseTransform.rotation * Quaternion.Inverse( m_lastBaseRotation );
      deltaRotation.ToAngleAxis( out var angleDegrees, out var axis );
      if ( float.IsNaN( axis.x ) || float.IsInfinity( axis.x ) )
        axis = Vector3.zero;

      if ( angleDegrees > 180.0f )
        angleDegrees -= 360.0f;

      var angularVelocityWorld = axis.normalized * angleDegrees * Mathf.Deg2Rad / deltaTime;
      m_lastAngularVelocityLocal = baseTransform.InverseTransformDirection( angularVelocityWorld );

      m_lastBasePosition = baseTransform.position;
      m_lastBaseRotation = baseTransform.rotation;
      m_lastBaseSampleTime = sampleTime;
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

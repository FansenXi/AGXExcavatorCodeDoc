using AGXUnity;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;
using UnityEngine.Serialization;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  [System.Serializable]
  public class ActuatorNormalizationRange
  {
    [SerializeField]
    private float m_min = -1.0f;

    [SerializeField]
    private float m_max = 1.0f;

    public float Min => m_min;
    public float Max => m_max;

    public float Normalize( float value )
    {
      if ( Mathf.Abs( m_max - m_min ) < 1.0e-5f )
        return 0.0f;

      return Mathf.Clamp01( Mathf.InverseLerp( m_min, m_max, value ) );
    }
  }

  [System.Serializable]
  public class ActuatorCalibrationDebugInfo
  {
    public string label = string.Empty;
    public float configured_min = 0.0f;
    public float configured_max = 0.0f;
    public float current_raw = 0.0f;
    public float current_normalized = 0.0f;
    public float observed_raw_min = 0.0f;
    public float observed_raw_max = 0.0f;
    public float observed_normalized_min = 0.0f;
    public float observed_normalized_max = 0.0f;
    public bool has_sample = false;

    public void Reset()
    {
      configured_min = 0.0f;
      configured_max = 0.0f;
      current_raw = 0.0f;
      current_normalized = 0.0f;
      observed_raw_min = 0.0f;
      observed_raw_max = 0.0f;
      observed_normalized_min = 0.0f;
      observed_normalized_max = 0.0f;
      has_sample = false;
    }

    public void Update( float rawValue, float normalizedValue, ActuatorNormalizationRange range )
    {
      configured_min = range != null ? range.Min : 0.0f;
      configured_max = range != null ? range.Max : 0.0f;
      current_raw = rawValue;
      current_normalized = normalizedValue;

      if ( !has_sample ) {
        observed_raw_min = rawValue;
        observed_raw_max = rawValue;
        observed_normalized_min = normalizedValue;
        observed_normalized_max = normalizedValue;
        has_sample = true;
        return;
      }

      observed_raw_min = Mathf.Min( observed_raw_min, rawValue );
      observed_raw_max = Mathf.Max( observed_raw_max, rawValue );
      observed_normalized_min = Mathf.Min( observed_normalized_min, normalizedValue );
      observed_normalized_max = Mathf.Max( observed_normalized_max, normalizedValue );
    }

    public string ToSummaryString()
    {
      if ( !has_sample ) {
        return string.Format(
          "{0}: no samples yet  cfg_raw=[{1:0.###}, {2:0.###}]",
          label,
          configured_min,
          configured_max );
      }

      return string.Format(
        "{0}: norm={1:0.###} obs_norm=[{2:0.###}, {3:0.###}]  raw={4:0.###} cfg_raw=[{5:0.###}, {6:0.###}] obs_raw=[{7:0.###}, {8:0.###}]",
        label,
        current_normalized,
        observed_normalized_min,
        observed_normalized_max,
        current_raw,
        configured_min,
        configured_max,
        observed_raw_min,
        observed_raw_max );
    }
  }

  public class ActObservationCollector : MonoBehaviour
  {
    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Excavator m_excavator = null;

    [FormerlySerializedAs( "m_massVolumeCounter" )]
    [SerializeField]
    private global::ExcavationMassTracker m_massTracker = null;

    [FormerlySerializedAs( "m_targetBoxMassSensor" )]
    [SerializeField]
    private global::SwitchableTargetMassSensor m_targetMassSensor = null;

    [SerializeField]
    private ActuatorNormalizationRange m_boomRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_swingRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_stickRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_bucketRange = new ActuatorNormalizationRange();

    private Vector3 m_lastBasePosition = Vector3.zero;
    private Quaternion m_lastBaseRotation = Quaternion.identity;
    private float m_lastBaseSampleTime = -1.0f;
    private Vector3 m_lastLinearVelocityLocal = Vector3.zero;
    private Vector3 m_lastAngularVelocityLocal = Vector3.zero;
    private readonly ActuatorCalibrationDebugInfo m_swingCalibration = new ActuatorCalibrationDebugInfo { label = "Swing" };
    private readonly ActuatorCalibrationDebugInfo m_boomCalibration = new ActuatorCalibrationDebugInfo { label = "Boom" };
    private readonly ActuatorCalibrationDebugInfo m_stickCalibration = new ActuatorCalibrationDebugInfo { label = "Stick" };
    private readonly ActuatorCalibrationDebugInfo m_bucketCalibration = new ActuatorCalibrationDebugInfo { label = "Bucket" };

    private void Awake()
    {
      ResolveReferences();
      RefreshCalibrationConfiguredRanges();
    }

    public void ResetSampling()
    {
      ResolveReferences();

      m_lastBaseSampleTime = -1.0f;
      m_lastLinearVelocityLocal = Vector3.zero;
      m_lastAngularVelocityLocal = Vector3.zero;

      var baseTransform = m_excavator != null ? m_excavator.transform : transform;
      m_lastBasePosition = baseTransform != null ? baseTransform.position : Vector3.zero;
      m_lastBaseRotation = baseTransform != null ? baseTransform.rotation : Quaternion.identity;
    }

    [ContextMenu( "Reset Calibration Tracking" )]
    public void ResetCalibrationTracking()
    {
      m_swingCalibration.Reset();
      m_boomCalibration.Reset();
      m_stickCalibration.Reset();
      m_bucketCalibration.Reset();
      RefreshCalibrationConfiguredRanges();
    }

    public string[] GetCalibrationDebugLines()
    {
      RefreshCalibrationTrackingFromRigState();
      RefreshCalibrationConfiguredRanges();
      return new[]
      {
        m_swingCalibration.ToSummaryString(),
        m_boomCalibration.ToSummaryString(),
        m_stickCalibration.ToSummaryString(),
        m_bucketCalibration.ToSummaryString()
      };
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

      var swingConstraint = m_excavator != null ? m_excavator.SwingHinge : null;
      var boomConstraint = m_excavator != null && m_excavator.BoomPrismatics.Length > 0 ? m_excavator.BoomPrismatics[ 0 ] : null;
      var stickConstraint = m_excavator != null ? m_excavator.StickPrismatic : null;
      var bucketConstraint = m_excavator != null ? m_excavator.BucketPrismatic : null;

      var swingPositionRaw = ReadConstraintPosition( swingConstraint );
      var boomPositionRaw = ReadConstraintPosition( boomConstraint );
      var stickPositionRaw = ReadConstraintPosition( stickConstraint );
      var bucketPositionRaw = ReadConstraintPosition( bucketConstraint );

      observation.actuator_state.swing_position_norm = NormalizeConstraintPosition( swingPositionRaw, m_swingRange );
      observation.actuator_state.boom_position_norm = NormalizeConstraintPosition( boomPositionRaw, m_boomRange );
      observation.actuator_state.boom_speed = ReadConstraintSpeed( boomConstraint );
      observation.actuator_state.stick_position_norm = NormalizeConstraintPosition( stickPositionRaw, m_stickRange );
      observation.actuator_state.stick_speed = ReadConstraintSpeed( stickConstraint );
      observation.actuator_state.bucket_position_norm = NormalizeConstraintPosition( bucketPositionRaw, m_bucketRange );
      observation.actuator_state.bucket_speed = ReadConstraintSpeed( bucketConstraint );
      observation.actuator_state.swing_speed = ReadConstraintSpeed( swingConstraint );

      UpdateCalibrationDebug( m_swingCalibration, swingConstraint, swingPositionRaw, observation.actuator_state.swing_position_norm, m_swingRange );
      UpdateCalibrationDebug( m_boomCalibration, boomConstraint, boomPositionRaw, observation.actuator_state.boom_position_norm, m_boomRange );
      UpdateCalibrationDebug( m_stickCalibration, stickConstraint, stickPositionRaw, observation.actuator_state.stick_position_norm, m_stickRange );
      UpdateCalibrationDebug( m_bucketCalibration, bucketConstraint, bucketPositionRaw, observation.actuator_state.bucket_position_norm, m_bucketRange );

      if ( m_massTracker != null ) {
        observation.task_state.mass_in_bucket_kg = m_massTracker.MassInBucket;
        observation.task_state.excavated_mass_kg = m_massTracker.ExcavatedMass;
      }

      if ( m_targetMassSensor != null ) {
        observation.task_state.mass_in_target_box_kg = m_targetMassSensor.MassInBox;
        observation.task_state.deposited_mass_in_target_box_kg = m_targetMassSensor.DepositedMass;
      }

      observation.task_state.min_distance_to_target_m =
        m_targetMassSensor != null &&
        m_targetMassSensor.TryMeasureBucketDistance( bucketReference, out var minDistanceToTargetMeters ) ?
          minDistanceToTargetMeters :
          -1.0f;

      return observation;
    }

    private void RefreshCalibrationConfiguredRanges()
    {
      m_swingCalibration.configured_min = m_swingRange != null ? m_swingRange.Min : 0.0f;
      m_swingCalibration.configured_max = m_swingRange != null ? m_swingRange.Max : 0.0f;
      m_boomCalibration.configured_min = m_boomRange != null ? m_boomRange.Min : 0.0f;
      m_boomCalibration.configured_max = m_boomRange != null ? m_boomRange.Max : 0.0f;
      m_stickCalibration.configured_min = m_stickRange != null ? m_stickRange.Min : 0.0f;
      m_stickCalibration.configured_max = m_stickRange != null ? m_stickRange.Max : 0.0f;
      m_bucketCalibration.configured_min = m_bucketRange != null ? m_bucketRange.Min : 0.0f;
      m_bucketCalibration.configured_max = m_bucketRange != null ? m_bucketRange.Max : 0.0f;
    }

    private void RefreshCalibrationTrackingFromRigState()
    {
      ResolveReferences();

      var swingConstraint = m_excavator != null ? m_excavator.SwingHinge : null;
      var boomConstraint = m_excavator != null && m_excavator.BoomPrismatics.Length > 0 ? m_excavator.BoomPrismatics[ 0 ] : null;
      var stickConstraint = m_excavator != null ? m_excavator.StickPrismatic : null;
      var bucketConstraint = m_excavator != null ? m_excavator.BucketPrismatic : null;

      var swingPositionRaw = ReadConstraintPosition( swingConstraint );
      var boomPositionRaw = ReadConstraintPosition( boomConstraint );
      var stickPositionRaw = ReadConstraintPosition( stickConstraint );
      var bucketPositionRaw = ReadConstraintPosition( bucketConstraint );

      UpdateCalibrationDebug( m_swingCalibration, swingConstraint, swingPositionRaw, NormalizeConstraintPosition( swingPositionRaw, m_swingRange ), m_swingRange );
      UpdateCalibrationDebug( m_boomCalibration, boomConstraint, boomPositionRaw, NormalizeConstraintPosition( boomPositionRaw, m_boomRange ), m_boomRange );
      UpdateCalibrationDebug( m_stickCalibration, stickConstraint, stickPositionRaw, NormalizeConstraintPosition( stickPositionRaw, m_stickRange ), m_stickRange );
      UpdateCalibrationDebug( m_bucketCalibration, bucketConstraint, bucketPositionRaw, NormalizeConstraintPosition( bucketPositionRaw, m_bucketRange ), m_bucketRange );
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );
      m_massTracker = ExcavatorRigLocator.ResolveComponent( this, m_massTracker );
      m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( this, m_targetMassSensor );
      m_targetMassSensor?.RefreshTargets();
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

    private static float ReadConstraintPosition( Constraint constraint )
    {
      return constraint != null ? constraint.GetCurrentAngle() : 0.0f;
    }

    private static float NormalizeConstraintPosition( float rawPosition, ActuatorNormalizationRange range )
    {
      if ( range == null )
        return 0.0f;

      return range.Normalize( rawPosition );
    }

    private static void UpdateCalibrationDebug( ActuatorCalibrationDebugInfo debugInfo,
                                                Constraint constraint,
                                                float rawPosition,
                                                float normalizedPosition,
                                                ActuatorNormalizationRange range )
    {
      if ( debugInfo == null ) {
        return;
      }

      if ( constraint == null ) {
        debugInfo.configured_min = range != null ? range.Min : 0.0f;
        debugInfo.configured_max = range != null ? range.Max : 0.0f;
        return;
      }

      debugInfo.Update( rawPosition, normalizedPosition, range );
    }
  }
}

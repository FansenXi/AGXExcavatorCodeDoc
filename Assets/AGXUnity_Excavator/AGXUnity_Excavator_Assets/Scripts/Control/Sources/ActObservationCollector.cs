using System;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

    public void Set( float min, float max )
    {
      m_min = min;
      m_max = max;
    }

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

    public bool HasUsableObservedRange( float minWidth = 1.0e-5f )
    {
      return has_sample && Mathf.Abs( observed_raw_max - observed_raw_min ) > minWidth;
    }

    public void Update( float rawValue, float normalizedValue, ActuatorNormalizationRange range, bool trackObservedRange = true )
    {
      configured_min = range != null ? range.Min : 0.0f;
      configured_max = range != null ? range.Max : 0.0f;
      current_raw = rawValue;
      current_normalized = normalizedValue;

      if ( !trackObservedRange )
        return;

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
          "{0}: no samples yet  norm={1:0.###} raw={2:0.###} cfg_raw=[{3:0.###}, {4:0.###}]",
          label,
          current_normalized,
          current_raw,
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

  [System.Serializable]
  public class ActuatorNormalizationAxisProfile
  {
    public float min = -1.0f;
    public float max = 1.0f;

    public ActuatorNormalizationAxisProfile()
    {
    }

    public ActuatorNormalizationAxisProfile( float min, float max )
    {
      this.min = min;
      this.max = max;
    }
  }

  [System.Serializable]
  public class ActuatorNormalizationProfile
  {
    public string profile_name = string.Empty;
    public string machine_name = string.Empty;
    public string created_utc = string.Empty;
    public string qpos_order = "swing_position_norm,boom_position_norm,stick_position_norm,bucket_position_norm";
    public ActuatorNormalizationAxisProfile swing = new ActuatorNormalizationAxisProfile( -Mathf.PI, Mathf.PI );
    public ActuatorNormalizationAxisProfile boom = new ActuatorNormalizationAxisProfile();
    public ActuatorNormalizationAxisProfile stick = new ActuatorNormalizationAxisProfile();
    public ActuatorNormalizationAxisProfile bucket = new ActuatorNormalizationAxisProfile();
  }

  public class ActObservationCollector : MonoBehaviour
  {
    private const string DefaultNormalizationProfilePath =
      "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/CAT365_norm.json";
    private const string DefaultCalibrationSaveDirectory =
      "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration";

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

    [FormerlySerializedAs( "m_massVolumeCounter" )]
    [SerializeField]
    private global::ExcavationMassTracker m_massTracker = null;

    [FormerlySerializedAs( "m_targetBoxMassSensor" )]
    [SerializeField]
    private global::SwitchableTargetMassSensor m_targetMassSensor = null;

    [SerializeField]
    private global::ActiveTargetCollisionMonitor m_activeTargetCollisionMonitor = null;

    [SerializeField]
    private global::DigAreaMeasurement m_digAreaMeasurement = null;

    [SerializeField]
    private ActuatorNormalizationRange m_boomRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_swingRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_stickRange = new ActuatorNormalizationRange();

    [SerializeField]
    private ActuatorNormalizationRange m_bucketRange = new ActuatorNormalizationRange();

    [Header( "QPos Normalization Profiles" )]
    [SerializeField]
    private bool m_loadNormalizationProfileOnAwake = true;

    [SerializeField]
    private TextAsset m_normalizationProfileAsset = null;

    [SerializeField]
    private string m_normalizationProfilePath = DefaultNormalizationProfilePath;

    [SerializeField]
    private string m_calibrationSaveDirectory = DefaultCalibrationSaveDirectory;

    [SerializeField]
    private string m_calibrationProfileName = "E85 norm";

    [SerializeField]
    private bool m_keepSwingRangeAtPi = true;

    private Vector3 m_lastBasePosition = Vector3.zero;
    private Quaternion m_lastBaseRotation = Quaternion.identity;
    private float m_lastBaseSampleTime = -1.0f;
    private Vector3 m_lastLinearVelocityLocal = Vector3.zero;
    private Vector3 m_lastAngularVelocityLocal = Vector3.zero;
    private ActObservation m_lastCollectedObservation = null;
    private readonly ActuatorCalibrationDebugInfo m_swingCalibration = new ActuatorCalibrationDebugInfo { label = "Swing" };
    private readonly ActuatorCalibrationDebugInfo m_boomCalibration = new ActuatorCalibrationDebugInfo { label = "Boom" };
    private readonly ActuatorCalibrationDebugInfo m_stickCalibration = new ActuatorCalibrationDebugInfo { label = "Stick" };
    private readonly ActuatorCalibrationDebugInfo m_bucketCalibration = new ActuatorCalibrationDebugInfo { label = "Bucket" };
    private bool m_calibrationTrackingEnabled = false;
    private string m_loadedNormalizationProfileName = "serialized fallback";
    private string m_lastCalibrationMessage = string.Empty;

    public ActObservation LastCollectedObservation => m_lastCollectedObservation;
    public ActTaskState LastTaskState => m_lastCollectedObservation != null ? m_lastCollectedObservation.task_state : null;
    public bool IsCalibrationTrackingEnabled => m_calibrationTrackingEnabled;
    public string LoadedNormalizationProfileName => m_loadedNormalizationProfileName;
    public string NormalizationProfilePath => m_normalizationProfilePath;
    public string LastCalibrationMessage => m_lastCalibrationMessage;
    public string CalibrationProfileName
    {
      get => m_calibrationProfileName;
      set
      {
        if ( !string.IsNullOrWhiteSpace( value ) )
          m_calibrationProfileName = value.Trim();
      }
    }

    private void Awake()
    {
      ResolveReferences();
      if ( m_loadNormalizationProfileOnAwake )
        LoadNormalizationProfileFromConfiguredPath();
      RefreshCalibrationConfiguredRanges();
    }

    public void ResetSampling()
    {
      ResolveReferences();

      m_lastBaseSampleTime = -1.0f;
      m_lastLinearVelocityLocal = Vector3.zero;
      m_lastAngularVelocityLocal = Vector3.zero;
      m_lastCollectedObservation = null;
      m_activeTargetCollisionMonitor?.ResetMonitoring();

      var baseTransform = ResolveMachineRoot();
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

    public void BeginCalibrationTracking()
    {
      ResetCalibrationTracking();
      m_calibrationTrackingEnabled = true;
      m_lastCalibrationMessage = "Calibration tracking started.";
    }

    public void EndCalibrationTracking()
    {
      m_calibrationTrackingEnabled = false;
      m_lastCalibrationMessage = "Calibration tracking stopped.";
    }

    [ContextMenu( "Load Normalization Profile" )]
    public bool LoadNormalizationProfileFromConfiguredPath()
    {
      if ( m_normalizationProfileAsset != null ) {
        var assetPath = m_normalizationProfileAsset.name;
#if UNITY_EDITOR
        assetPath = AssetDatabase.GetAssetPath( m_normalizationProfileAsset );
#endif
        return LoadNormalizationProfileJson( m_normalizationProfileAsset.text, assetPath, assetPath );
      }

      return LoadNormalizationProfile( m_normalizationProfilePath );
    }

    public bool LoadNormalizationProfile( string profilePath )
    {
      var absolutePath = ResolveProfilePath( profilePath );
      if ( string.IsNullOrWhiteSpace( absolutePath ) || !File.Exists( absolutePath ) ) {
        m_lastCalibrationMessage = $"Normalization profile not found: {profilePath}";
        return false;
      }

      try {
        return LoadNormalizationProfileJson( File.ReadAllText( absolutePath ), profilePath, ToProjectRelativePath( absolutePath ) );
      }
      catch ( System.Exception exception ) {
        m_lastCalibrationMessage = $"Normalization profile load failed: {exception.Message}";
        return false;
      }
    }

    private bool LoadNormalizationProfileJson( string json, string sourceLabel, string projectRelativePath )
    {
      var profile = JsonUtility.FromJson<ActuatorNormalizationProfile>( json );
      if ( profile == null ) {
        m_lastCalibrationMessage = $"Normalization profile could not be parsed: {sourceLabel}";
        return false;
      }

      ApplyNormalizationProfile( profile );
      if ( !string.IsNullOrWhiteSpace( projectRelativePath ) )
        m_normalizationProfilePath = projectRelativePath;
      m_loadedNormalizationProfileName = string.IsNullOrWhiteSpace( profile.profile_name ) ?
                                         Path.GetFileNameWithoutExtension( sourceLabel ) :
                                         profile.profile_name;
      m_lastCalibrationMessage = $"Loaded normalization profile: {m_loadedNormalizationProfileName}";
      RefreshCalibrationConfiguredRanges();
      return true;
    }

    [ContextMenu( "Save Observed Normalization Profile" )]
    public bool SaveObservedNormalizationProfile()
    {
      return SaveObservedNormalizationProfile( m_calibrationProfileName );
    }

    public bool SaveObservedNormalizationProfile( string profileName )
    {
      RefreshCalibrationTrackingFromRigState();

      if ( string.IsNullOrWhiteSpace( profileName ) )
        profileName = "Actuator norm";

      if ( !TryBuildObservedNormalizationProfile( profileName.Trim(), out var profile ) )
        return false;

      try {
        var directory = ResolveProfilePath( m_calibrationSaveDirectory );
        if ( string.IsNullOrWhiteSpace( directory ) )
          directory = ResolveProfilePath( DefaultCalibrationSaveDirectory );

        Directory.CreateDirectory( directory );
        var fileName = SanitizeFileName( profile.profile_name );
        if ( string.IsNullOrWhiteSpace( fileName ) )
          fileName = "Actuator_norm";

        var path = Path.Combine( directory, fileName + ".json" );
        File.WriteAllText( path, JsonUtility.ToJson( profile, true ) );
        m_normalizationProfileAsset = null;
        m_normalizationProfilePath = ToProjectRelativePath( path );
        ApplyNormalizationProfile( profile );
        RefreshCalibrationConfiguredRanges();
        m_loadedNormalizationProfileName = profile.profile_name;
        m_calibrationTrackingEnabled = false;
        m_lastCalibrationMessage = $"Saved normalization profile: {m_normalizationProfilePath}";
#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
        return true;
      }
      catch ( System.Exception exception ) {
        m_lastCalibrationMessage = $"Normalization profile save failed: {exception.Message}";
        return false;
      }
    }

    public string GetCalibrationStatusLine()
    {
      return $"Profile: {m_loadedNormalizationProfileName}    File: {m_normalizationProfilePath}    Tracking: {m_calibrationTrackingEnabled}";
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

      var baseTransform = ResolveMachineRoot();
      UpdateBaseVelocity( baseTransform );

      observation.base_pose_world.Set( baseTransform.position, baseTransform.rotation );
      observation.base_velocity_local.Set( m_lastLinearVelocityLocal, m_lastAngularVelocityLocal );

      var bucketReference = m_machineController != null ? m_machineController.BucketReference : null;
      if ( bucketReference != null )
        observation.bucket_pose_world.Set( bucketReference.position, bucketReference.rotation );

      var swingConstraint = ResolveSwingConstraint();
      var boomConstraint = ResolveBoomConstraint();
      var stickConstraint = ResolveStickConstraint();
      var bucketConstraint = ResolveBucketConstraint();

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

      UpdateCalibrationDebug( m_swingCalibration, swingConstraint, swingPositionRaw, observation.actuator_state.swing_position_norm, m_swingRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_boomCalibration, boomConstraint, boomPositionRaw, observation.actuator_state.boom_position_norm, m_boomRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_stickCalibration, stickConstraint, stickPositionRaw, observation.actuator_state.stick_position_norm, m_stickRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_bucketCalibration, bucketConstraint, bucketPositionRaw, observation.actuator_state.bucket_position_norm, m_bucketRange, m_calibrationTrackingEnabled );

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
      if ( m_targetMassSensor != null &&
           m_targetMassSensor.TryMeasureBucketTargetGeometry( bucketReference,
                                                              out var targetGeometryMetrics ) &&
           targetGeometryMetrics.IsValid ) {
        observation.task_state.target_horizontal_distance_m = targetGeometryMetrics.TargetHorizontalDistanceMeters;
        observation.task_state.bucket_height_above_target_rim_m = targetGeometryMetrics.BucketHeightAboveTargetRimMeters;
        observation.task_state.bucket_over_target_footprint_mask = targetGeometryMetrics.BucketOverTargetFootprintMask;
        observation.task_state.dump_clearance_ok_mask = targetGeometryMetrics.DumpClearanceOkMask;
        observation.task_state.bucket_dump_area_relative_x_m = targetGeometryMetrics.BucketDumpAreaRelativeXMeters;
        observation.task_state.bucket_dump_area_relative_z_m = targetGeometryMetrics.BucketDumpAreaRelativeZMeters;
        observation.task_state.bucket_dump_area_footprint_outside_distance_m =
          targetGeometryMetrics.BucketDumpAreaFootprintOutsideDistanceMeters;
      }
      observation.task_state.target_hard_collision_count =
        m_activeTargetCollisionMonitor != null ?
          m_activeTargetCollisionMonitor.TargetHardCollisionCount :
          0.0f;
      observation.task_state.target_contact_max_normal_force_n =
        m_activeTargetCollisionMonitor != null ?
          m_activeTargetCollisionMonitor.TargetContactMaxNormalForceN :
          0.0f;
      if ( m_digAreaMeasurement != null &&
           m_digAreaMeasurement.TryMeasureBucketDigAreaMetrics( bucketReference,
                                                                out var minDistanceToDigAreaMeters,
                                                                out var bucketDepthBelowDigAreaPlaneMeters ) ) {
        observation.task_state.min_distance_to_dig_area_m = minDistanceToDigAreaMeters;
        observation.task_state.bucket_depth_below_dig_area_plane_m = bucketDepthBelowDigAreaPlaneMeters;
      }
      if ( m_digAreaMeasurement != null &&
           m_digAreaMeasurement.TryMeasureBucketCellMetrics( bucketReference,
                                                             out var cellMetrics ) ) {
        observation.task_state.dig_area_geometry_available =
          cellMetrics.GeometryAvailable ? 1.0f : 0.0f;
        observation.task_state.dig_area_long_axis = cellMetrics.LongAxis;
        observation.task_state.dig_area_grid_long_count = cellMetrics.GridLongCount;
        observation.task_state.dig_area_grid_short_count = cellMetrics.GridShortCount;
        observation.task_state.bucket_dig_area_relative_x_m =
          cellMetrics.BucketDigAreaLocalMeters.x;
        observation.task_state.bucket_dig_area_relative_y_m =
          cellMetrics.BucketDigAreaLocalMeters.y;
        observation.task_state.bucket_dig_area_relative_z_m =
          cellMetrics.BucketDigAreaLocalMeters.z;
        observation.task_state.bucket_dig_area_long_norm = cellMetrics.LongNorm;
        observation.task_state.bucket_dig_area_short_norm = cellMetrics.ShortNorm;
        observation.task_state.bucket_dig_area_long_index = cellMetrics.LongIndex;
        observation.task_state.bucket_dig_area_short_index = cellMetrics.ShortIndex;
        observation.task_state.bucket_dig_area_cell_id = cellMetrics.CellId;
      }

      m_lastCollectedObservation = observation;
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

      var swingConstraint = ResolveSwingConstraint();
      var boomConstraint = ResolveBoomConstraint();
      var stickConstraint = ResolveStickConstraint();
      var bucketConstraint = ResolveBucketConstraint();

      var swingPositionRaw = ReadConstraintPosition( swingConstraint );
      var boomPositionRaw = ReadConstraintPosition( boomConstraint );
      var stickPositionRaw = ReadConstraintPosition( stickConstraint );
      var bucketPositionRaw = ReadConstraintPosition( bucketConstraint );

      UpdateCalibrationDebug( m_swingCalibration, swingConstraint, swingPositionRaw, NormalizeConstraintPosition( swingPositionRaw, m_swingRange ), m_swingRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_boomCalibration, boomConstraint, boomPositionRaw, NormalizeConstraintPosition( boomPositionRaw, m_boomRange ), m_boomRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_stickCalibration, stickConstraint, stickPositionRaw, NormalizeConstraintPosition( stickPositionRaw, m_stickRange ), m_stickRange, m_calibrationTrackingEnabled );
      UpdateCalibrationDebug( m_bucketCalibration, bucketConstraint, bucketPositionRaw, NormalizeConstraintPosition( bucketPositionRaw, m_bucketRange ), m_bucketRange, m_calibrationTrackingEnabled );
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
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
      m_massTracker = ExcavatorRigLocator.ResolveComponent( this, m_massTracker );
      m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( this, m_targetMassSensor );
      m_activeTargetCollisionMonitor = ExcavatorRigLocator.ResolveComponent( this, m_activeTargetCollisionMonitor );
      m_digAreaMeasurement = ExcavatorRigLocator.ResolveComponent( this, m_digAreaMeasurement );
      if ( m_activeTargetCollisionMonitor == null ) {
        var machineRoot = ResolveMachineRoot();
        var monitorHost = machineRoot != null ? machineRoot.gameObject : gameObject;
        m_activeTargetCollisionMonitor = monitorHost.GetComponent<global::ActiveTargetCollisionMonitor>();
        if ( m_activeTargetCollisionMonitor == null )
          m_activeTargetCollisionMonitor = monitorHost.AddComponent<global::ActiveTargetCollisionMonitor>();
      }
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = global::DigAreaMeasurement.FindOrCreateInScene();
      else
        m_digAreaMeasurement.ResolveReferences();
      m_targetMassSensor?.RefreshTargets();
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

      if ( m_excavator != null )
        return m_excavator.transform;

      return transform;
    }

    private Constraint ResolveSwingConstraint()
    {
      if ( m_machineController != null )
        return m_machineController.SwingConstraint;

      if ( m_e85Excavator != null )
        return m_e85Excavator.CabinHinge;

      if ( m_yuLongExcavator != null )
        return m_yuLongExcavator.SwingHinge;

      return m_excavator != null ? m_excavator.SwingHinge : null;
    }

    private Constraint ResolveBoomConstraint()
    {
      if ( m_machineController != null ) {
        var boomConstraints = m_machineController.BoomConstraints;
        return boomConstraints != null && boomConstraints.Length > 0 ? boomConstraints[ 0 ] : null;
      }

      if ( m_e85Excavator != null )
        return m_e85Excavator.ArmPrismatic;

      if ( m_yuLongExcavator != null )
        return m_yuLongExcavator.BoomConstraint;

      return m_excavator != null && m_excavator.BoomPrismatics.Length > 0 ? m_excavator.BoomPrismatics[ 0 ] : null;
    }

    private Constraint ResolveStickConstraint()
    {
      if ( m_machineController != null )
        return m_machineController.StickConstraint;

      if ( m_e85Excavator != null )
        return m_e85Excavator.StickPrismatic;

      if ( m_yuLongExcavator != null )
        return m_yuLongExcavator.StickConstraint;

      return m_excavator != null ? m_excavator.StickPrismatic : null;
    }

    private Constraint ResolveBucketConstraint()
    {
      if ( m_machineController != null )
        return m_machineController.BucketConstraint;

      if ( m_e85Excavator != null )
        return m_e85Excavator.BucketPrismatic;

      if ( m_yuLongExcavator != null )
        return m_yuLongExcavator.BucketConstraint;

      return m_excavator != null ? m_excavator.BucketPrismatic : null;
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
                                                ActuatorNormalizationRange range,
                                                bool trackObservedRange )
    {
      if ( debugInfo == null ) {
        return;
      }

      if ( constraint == null ) {
        debugInfo.configured_min = range != null ? range.Min : 0.0f;
        debugInfo.configured_max = range != null ? range.Max : 0.0f;
        return;
      }

      debugInfo.Update( rawPosition, normalizedPosition, range, trackObservedRange );
    }

    private void ApplyNormalizationProfile( ActuatorNormalizationProfile profile )
    {
      if ( profile == null )
        return;

      ApplyAxisProfile( m_swingRange, profile.swing );
      ApplyAxisProfile( m_boomRange, profile.boom );
      ApplyAxisProfile( m_stickRange, profile.stick );
      ApplyAxisProfile( m_bucketRange, profile.bucket );
    }

    private static void ApplyAxisProfile( ActuatorNormalizationRange range, ActuatorNormalizationAxisProfile profile )
    {
      if ( range == null || profile == null )
        return;

      if ( Mathf.Abs( profile.max - profile.min ) < 1.0e-5f )
        return;

      range.Set( profile.min, profile.max );
    }

    private bool TryBuildObservedNormalizationProfile( string profileName, out ActuatorNormalizationProfile profile )
    {
      profile = null;

      if ( !TryCreateAxisProfile( m_boomCalibration, "boom", out var boom ) ||
           !TryCreateAxisProfile( m_stickCalibration, "stick", out var stick ) ||
           !TryCreateAxisProfile( m_bucketCalibration, "bucket", out var bucket ) )
        return false;

      var swing = m_keepSwingRangeAtPi ?
                  new ActuatorNormalizationAxisProfile( -Mathf.PI, Mathf.PI ) :
                  null;
      if ( swing == null && !TryCreateAxisProfile( m_swingCalibration, "swing", out swing ) )
        return false;

      var machineRoot = ResolveMachineRoot();
      profile = new ActuatorNormalizationProfile
      {
        profile_name = profileName,
        machine_name = machineRoot != null ? machineRoot.name : string.Empty,
        created_utc = DateTime.UtcNow.ToString( "o", CultureInfo.InvariantCulture ),
        qpos_order = "swing_position_norm,boom_position_norm,stick_position_norm,bucket_position_norm",
        swing = swing,
        boom = boom,
        stick = stick,
        bucket = bucket
      };
      return true;
    }

    private bool TryCreateAxisProfile( ActuatorCalibrationDebugInfo debugInfo,
                                       string axisName,
                                       out ActuatorNormalizationAxisProfile profile )
    {
      profile = null;
      if ( debugInfo == null || !debugInfo.HasUsableObservedRange() ) {
        m_lastCalibrationMessage =
          $"Cannot save normalization profile: {axisName} does not have a usable observed raw range.";
        return false;
      }

      profile = new ActuatorNormalizationAxisProfile( debugInfo.observed_raw_min, debugInfo.observed_raw_max );
      return true;
    }

    private static string ResolveProfilePath( string path )
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
        return Path.Combine( Application.dataPath, relativeToAssets.Replace( '/', Path.DirectorySeparatorChar ) );
      }

      return Path.Combine( Application.dataPath, normalized.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static string ToProjectRelativePath( string absolutePath )
    {
      if ( string.IsNullOrWhiteSpace( absolutePath ) )
        return string.Empty;

      var fullPath = Path.GetFullPath( absolutePath );
      var assetsPath = Path.GetFullPath( Application.dataPath );
      if ( fullPath.StartsWith( assetsPath, StringComparison.OrdinalIgnoreCase ) ) {
        var relative = fullPath.Substring( assetsPath.Length ).TrimStart( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
        return string.IsNullOrWhiteSpace( relative ) ?
               "Assets" :
               "Assets/" + relative.Replace( Path.DirectorySeparatorChar, '/' ).Replace( Path.AltDirectorySeparatorChar, '/' );
      }

      return fullPath;
    }

    private static string SanitizeFileName( string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return string.Empty;

      var chars = value.Trim().ToCharArray();
      for ( var index = 0; index < chars.Length; ++index ) {
        var character = chars[ index ];
        if ( char.IsLetterOrDigit( character ) || character == '-' || character == '_' )
          continue;

        chars[ index ] = '_';
      }

      return new string( chars ).Trim( '_' );
    }
  }
}

#if UNITY_EDITOR
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexMassTelemetrySnapshotUtility
  {
    private const string RequestPath = "Temp/CodexMassTelemetrySnapshot.request";
    private const string OutputDirectory = "Temp/CodexMassTelemetrySnapshot";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexMassTelemetrySnapshotUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Capture Mass Telemetry Snapshot" )]
    public static void CaptureFromMenu()
    {
      Capture( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 0.5;

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new MassTelemetrySnapshotResult {
          success = false,
          message = "Could not delete request file: " + exception.Message,
          source = "request-file"
        } );
        return;
      }

      Capture( "request-file" );
    }

    private static void Capture( string source )
    {
      s_isRunning = true;
      var result = new MassTelemetrySnapshotResult {
        source = source,
        unity_is_playing = EditorApplication.isPlaying,
        scene_path = SceneManager.GetActiveScene().path,
        realtime_since_startup_s = FormatFloat( Time.realtimeSinceStartup ),
        time_s = FormatFloat( Time.time )
      };

      try {
        var massTracker = UnityEngine.Object.FindFirstObjectByType<global::ExcavationMassTracker>();
        var targetRouter = UnityEngine.Object.FindFirstObjectByType<global::SwitchableTargetMassSensor>();
        var collector = UnityEngine.Object.FindFirstObjectByType<ActObservationCollector>();

        FillBucketTracker( result.bucket, massTracker );
        FillTargetRouter( result.target_router, targetRouter, massTracker );
        FillObservation( result.observation, collector );

        result.success = true;
        result.message = "Captured mass telemetry snapshot.";
        WriteResult( result );
      }
      catch ( System.Exception exception ) {
        result.success = false;
        result.message = exception.ToString();
        WriteResult( result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void FillBucketTracker( BucketTrackerSnapshot target, global::ExcavationMassTracker massTracker )
    {
      target.found = massTracker != null;
      if ( massTracker == null )
        return;

      target.path = GetHierarchyPath( massTracker.gameObject );
      target.mass_in_bucket_kg = FormatFloat( massTracker.MassInBucket );
      target.raw_mass_in_bucket_kg = FormatFloat( massTracker.RawMassInBucket );
      target.mass_in_bucket_deadband_kg = FormatFloat( massTracker.MassInBucketDeadbandKg );
      target.excavated_mass_kg = FormatFloat( massTracker.ExcavatedMass );
      target.bucket_measurement_frame = massTracker.BucketMeasurementFrame != null ?
                                        GetHierarchyPath( massTracker.BucketMeasurementFrame.gameObject ) :
                                        string.Empty;

      if ( massTracker.TryGetBucketMeasurementVolume( out _, out var bucketCenter, out var bucketHalfExtents ) ) {
        target.bucket_measurement_center_local = FormatVector( bucketCenter );
        target.bucket_measurement_half_extents = FormatVector( bucketHalfExtents );
      }

      if ( massTracker.TryGetTargetDistanceProxyVolume( out _, out var proxyCenter, out var proxyHalfExtents ) ) {
        target.target_distance_proxy_center_local = FormatVector( proxyCenter );
        target.target_distance_proxy_half_extents = FormatVector( proxyHalfExtents );
      }
    }

    private static void FillTargetRouter( TargetRouterSnapshot target,
                                          global::SwitchableTargetMassSensor router,
                                          global::ExcavationMassTracker massTracker )
    {
      target.found = router != null;
      if ( router == null )
        return;

      router.RefreshTargets();
      target.path = GetHierarchyPath( router.gameObject );
      target.available_target_count = router.AvailableTargetCount;
      target.current_target_index = router.CurrentTargetIndex;
      target.current_target_name = router.CurrentTargetName;
      target.mass_in_target_box_kg = FormatFloat( router.MassInBox );
      target.deposited_mass_in_target_box_kg = FormatFloat( router.DepositedMass );
      if ( massTracker != null &&
           router.TryMeasureBucketDistance( massTracker.BucketMeasurementFrame, out var distanceMeters ) )
        target.min_distance_to_target_m = FormatFloat( distanceMeters );
      else
        target.min_distance_to_target_m = "-1";

      for ( var index = 0; index < router.AvailableTargetCount; ++index ) {
        var targetSensor = GetRuntimeTarget( router, index );
        target.targets.Add( DescribeTargetSensor( index, router.CurrentTargetIndex == index, targetSensor, massTracker ) );
      }
    }

    private static TargetSensorSnapshot DescribeTargetSensor( int index,
                                                              bool isCurrent,
                                                              global::TargetMassSensorBase sensor,
                                                              global::ExcavationMassTracker massTracker )
    {
      var result = new TargetSensorSnapshot {
        index = index,
        is_current = isCurrent,
        found = sensor != null
      };
      if ( sensor == null )
        return result;

      result.name = sensor.TargetName;
      result.type = sensor.GetType().Name;
      result.path = GetHierarchyPath( sensor.gameObject );
      result.mass_in_box_kg = FormatFloat( sensor.MassInBox );
      result.deposited_mass_kg = FormatFloat( sensor.DepositedMass );

      if ( sensor.TryGetMeasurementVolume( out var frame, out var center, out var halfExtents ) ) {
        result.measurement_frame = frame != null ? GetHierarchyPath( frame.gameObject ) : string.Empty;
        result.measurement_center_local = FormatVector( center );
        result.measurement_half_extents = FormatVector( halfExtents );
      }

      if ( massTracker != null &&
           TryMeasureBucketTargetDistance( massTracker.BucketMeasurementFrame,
                                           sensor,
                                           out var distanceMeters ) )
        result.bucket_distance_m = FormatFloat( distanceMeters );
      else
        result.bucket_distance_m = "-1";

      FillTerrainParticleInternals( result.terrain_particle_internals, sensor );
      return result;
    }

    private static bool TryMeasureBucketTargetDistance( Transform bucketReference,
                                                        global::TargetMassSensorBase sensor,
                                                        out float distanceMeters )
    {
      distanceMeters = -1.0f;
      if ( bucketReference == null || sensor == null )
        return false;

      var utilityType = typeof( global::TargetMassSensorBase ).Assembly.GetType( "BucketTargetDistanceMeasurementUtility" );
      var method = utilityType != null ?
                   utilityType.GetMethod( "TryMeasureDistance",
                                          BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                          null,
                                          new[] {
                                            typeof( Transform ),
                                            typeof( global::TargetMassSensorBase ),
                                            typeof( float ).MakeByRefType()
                                          },
                                          null ) :
                   null;
      if ( method == null )
        return false;

      var arguments = new object[] { bucketReference, sensor, distanceMeters };
      var success = method.Invoke( null, arguments ) is bool boolResult && boolResult;
      if ( arguments[ 2 ] is float measuredDistance )
        distanceMeters = measuredDistance;

      return success;
    }

    private static void FillTerrainParticleInternals( TerrainParticleBoxMassSensorInternals target,
                                                      global::TargetMassSensorBase sensor )
    {
      if ( sensor == null || sensor.GetType() != typeof( global::TerrainParticleBoxMassSensor ) )
        return;

      target.available = true;
      target.mass_in_box_field_kg = FormatFloat( GetField<float>( sensor, "m_massInBox" ) );
      target.deposited_mass_field_kg = FormatFloat( GetField<float>( sensor, "m_depositedMass" ) );
      target.entered_particle_mass_kg = FormatFloat( GetField<float>( sensor, "m_enteredParticleMass" ) );
      target.bucket_unload_near_target_mass_kg = FormatFloat( GetField<float>( sensor, "m_bucketUnloadNearTargetMass" ) );
      target.previous_bucket_mass_kg = FormatFloat( GetField<float>( sensor, "m_previousBucketMass" ) );
      target.reset_baseline_mass_in_box_kg = FormatFloat( GetField<float>( sensor, "m_resetBaselineMassInBox" ) );
      target.reset_baseline_settled_compactor_mass_kg = FormatFloat( GetField<float>( sensor, "m_resetBaselineSettledCompactorMass" ) );
      target.accumulate_bucket_unload_near_target = GetField<bool>( sensor, "m_accumulateBucketUnloadNearTarget" );
      target.bucket_unload_target_distance_tolerance_m = FormatFloat( GetField<float>( sensor, "m_bucketUnloadTargetDistanceTolerance" ) );
      target.accumulate_entered_particle_mass = GetField<bool>( sensor, "m_accumulateEnteredParticleMass" );
      target.use_entered_particle_mass_for_deposited_mass = GetField<bool>( sensor, "m_useEnteredParticleMassForDepositedMass" );
      target.use_entered_particle_mass_for_mass_in_box = GetField<bool>( sensor, "m_useEnteredParticleMassForMassInBox" );
      target.use_settled_compactor_mass_for_deposited_mass = GetField<bool>( sensor, "m_useSettledCompactorMassForDepositedMass" );

      if ( TryInvoke<float>( sensor, "ReadLiveMassInBox", out var liveMassInBox ) ) {
        target.live_mass_in_box_raw_kg = FormatFloat( liveMassInBox );
        target.live_deposited_mass_from_baseline_kg =
          FormatFloat( Mathf.Max( 0.0f, liveMassInBox - GetField<float>( sensor, "m_resetBaselineMassInBox" ) ) );
      }

      if ( TryInvoke<float>( sensor, "ReadBucketMass", out var bucketMass ) )
        target.current_bucket_mass_read_by_target_kg = FormatFloat( bucketMass );

      if ( TryInvoke<bool>( sensor, "IsBucketNearTarget", out var isNearTarget ) )
        target.is_bucket_near_target = isNearTarget;

      if ( TryInvoke<float>( sensor, "ReadSettledCompactorMass", out var settledCompactorMass ) ) {
        target.settled_compactor_mass_raw_kg = FormatFloat( settledCompactorMass );
        target.settled_compactor_mass_from_baseline_kg =
          FormatFloat( Mathf.Max( 0.0f, settledCompactorMass - GetField<float>( sensor, "m_resetBaselineSettledCompactorMass" ) ) );
      }
    }

    private static void FillObservation( ObservationSnapshot target, ActObservationCollector collector )
    {
      target.found = collector != null;
      if ( collector == null )
        return;

      target.path = GetHierarchyPath( collector.gameObject );
      var observation = collector.Collect( OperatorCommand.Zero );
      var taskState = observation != null ? observation.task_state : null;
      if ( taskState == null )
        return;

      target.mass_in_bucket_kg = FormatFloat( taskState.mass_in_bucket_kg );
      target.excavated_mass_kg = FormatFloat( taskState.excavated_mass_kg );
      target.mass_in_target_box_kg = FormatFloat( taskState.mass_in_target_box_kg );
      target.deposited_mass_in_target_box_kg = FormatFloat( taskState.deposited_mass_in_target_box_kg );
      target.min_distance_to_target_m = FormatFloat( taskState.min_distance_to_target_m );
      target.target_hard_collision_count = FormatFloat( taskState.target_hard_collision_count );
      target.target_contact_max_normal_force_n = FormatFloat( taskState.target_contact_max_normal_force_n );
      target.min_distance_to_dig_area_m = FormatFloat( taskState.min_distance_to_dig_area_m );
      target.bucket_depth_below_dig_area_plane_m = FormatFloat( taskState.bucket_depth_below_dig_area_plane_m );
    }

    private static global::TargetMassSensorBase GetRuntimeTarget( global::SwitchableTargetMassSensor router, int index )
    {
      var field = typeof( global::SwitchableTargetMassSensor ).GetField( "m_runtimeTargets",
                                                                         BindingFlags.Instance | BindingFlags.NonPublic );
      var targets = field != null ? field.GetValue( router ) as global::TargetMassSensorBase[] : null;
      return targets != null && index >= 0 && index < targets.Length ? targets[ index ] : null;
    }

    private static T GetField<T>( object instance, string fieldName )
    {
      if ( instance == null )
        return default;

      var field = instance.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.NonPublic );
      if ( field == null )
        return default;

      var value = field.GetValue( instance );
      return value is T typedValue ? typedValue : default;
    }

    private static bool TryInvoke<T>( object instance, string methodName, out T value )
    {
      value = default;
      if ( instance == null )
        return false;

      var method = instance.GetType().GetMethod( methodName, BindingFlags.Instance | BindingFlags.NonPublic );
      if ( method == null )
        return false;

      var rawValue = method.Invoke( instance, null );
      if ( rawValue is T typedValue ) {
        value = typedValue;
        return true;
      }

      return false;
    }

    private static string GetHierarchyPath( GameObject gameObject )
    {
      if ( gameObject == null )
        return string.Empty;

      var names = new List<string>();
      var current = gameObject.transform;
      while ( current != null ) {
        names.Add( current.name );
        current = current.parent;
      }

      names.Reverse();
      return string.Join( "/", names );
    }

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture,
                            "({0:0.###}, {1:0.###}, {2:0.###})",
                            value.x,
                            value.y,
                            value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( MassTelemetrySnapshotResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    [Serializable]
    private sealed class MassTelemetrySnapshotResult
    {
      public bool success;
      public string message;
      public string source;
      public bool unity_is_playing;
      public string scene_path;
      public string realtime_since_startup_s;
      public string time_s;
      public BucketTrackerSnapshot bucket = new BucketTrackerSnapshot();
      public TargetRouterSnapshot target_router = new TargetRouterSnapshot();
      public ObservationSnapshot observation = new ObservationSnapshot();
    }

    [Serializable]
    private sealed class BucketTrackerSnapshot
    {
      public bool found;
      public string path;
      public string mass_in_bucket_kg;
      public string raw_mass_in_bucket_kg;
      public string mass_in_bucket_deadband_kg;
      public string excavated_mass_kg;
      public string bucket_measurement_frame;
      public string bucket_measurement_center_local;
      public string bucket_measurement_half_extents;
      public string target_distance_proxy_center_local;
      public string target_distance_proxy_half_extents;
    }

    [Serializable]
    private sealed class TargetRouterSnapshot
    {
      public bool found;
      public string path;
      public int available_target_count;
      public int current_target_index;
      public string current_target_name;
      public string mass_in_target_box_kg;
      public string deposited_mass_in_target_box_kg;
      public string min_distance_to_target_m;
      public List<TargetSensorSnapshot> targets = new List<TargetSensorSnapshot>();
    }

    [Serializable]
    private sealed class TargetSensorSnapshot
    {
      public int index;
      public bool is_current;
      public bool found;
      public string name;
      public string type;
      public string path;
      public string mass_in_box_kg;
      public string deposited_mass_kg;
      public string bucket_distance_m;
      public string measurement_frame;
      public string measurement_center_local;
      public string measurement_half_extents;
      public TerrainParticleBoxMassSensorInternals terrain_particle_internals = new TerrainParticleBoxMassSensorInternals();
    }

    [Serializable]
    private sealed class TerrainParticleBoxMassSensorInternals
    {
      public bool available;
      public string mass_in_box_field_kg;
      public string deposited_mass_field_kg;
      public string live_mass_in_box_raw_kg;
      public string live_deposited_mass_from_baseline_kg;
      public string entered_particle_mass_kg;
      public string bucket_unload_near_target_mass_kg;
      public string previous_bucket_mass_kg;
      public string current_bucket_mass_read_by_target_kg;
      public bool is_bucket_near_target;
      public string bucket_unload_target_distance_tolerance_m;
      public string reset_baseline_mass_in_box_kg;
      public string settled_compactor_mass_raw_kg;
      public string settled_compactor_mass_from_baseline_kg;
      public string reset_baseline_settled_compactor_mass_kg;
      public bool accumulate_bucket_unload_near_target;
      public bool accumulate_entered_particle_mass;
      public bool use_entered_particle_mass_for_deposited_mass;
      public bool use_entered_particle_mass_for_mass_in_box;
      public bool use_settled_compactor_mass_for_deposited_mass;
    }

    [Serializable]
    private sealed class ObservationSnapshot
    {
      public bool found;
      public string path;
      public string mass_in_bucket_kg;
      public string excavated_mass_kg;
      public string mass_in_target_box_kg;
      public string deposited_mass_in_target_box_kg;
      public string min_distance_to_target_m;
      public string target_hard_collision_count;
      public string target_contact_max_normal_force_n;
      public string min_distance_to_dig_area_m;
      public string bucket_depth_below_dig_area_plane_m;
    }
  }
}
#pragma warning restore 0649
#endif

#if UNITY_EDITOR
using System;
using System.IO;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexDistanceBindingDiagnosticsUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexDistanceBindingDiagnostics.request";
    private const string OutputDirectory = "Temp/CodexDistanceBindingDiagnostics";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexDistanceBindingDiagnosticsUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Diagnose Dig Target Bindings" )]
    public static void DiagnoseFromMenu()
    {
      Diagnose( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = AbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( Exception exception ) {
        WriteResult( false, exception.Message, null );
        return;
      }

      Diagnose( "request-file" );
    }

    private static void Diagnose( string source )
    {
      s_isRunning = true;
      var result = new DiagnosticsResult { Source = source };

      try {
        var scene = EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
        if ( !scene.IsValid() )
          throw new InvalidOperationException( $"Could not open scene: {ScenePath}" );

        var machineController = FindObject<ExcavatorMachineController>();
        var bucketReference = machineController != null ? machineController.BucketReference : null;
        result.MachineRoot = machineController != null && machineController.MachineRoot != null ? PathOf( machineController.MachineRoot ) : null;
        result.BucketReference = PathOf( bucketReference );
        result.BucketPosition = FormatVector( bucketReference != null ? bucketReference.position : Vector3.zero );

        var targetSensor = FindObject<SwitchableTargetMassSensor>();
        targetSensor?.RefreshTargets();
        result.CurrentTargetName = targetSensor != null ? targetSensor.CurrentTargetName : null;
        result.TargetCount = targetSensor != null ? targetSensor.AvailableTargetCount : 0;
        result.TargetIndex = targetSensor != null ? targetSensor.CurrentTargetIndex : -1;

        if ( targetSensor != null && targetSensor.CurrentTarget != null ) {
          result.TargetObject = PathOf( targetSensor.CurrentTarget.transform );
          result.TargetSensorType = targetSensor.CurrentTarget.GetType().Name;
          if ( targetSensor.CurrentTarget.TryGetTargetDistanceVolume( out var targetFrame,
                                                                      out var targetCenterLocal,
                                                                      out var targetHalfExtents ) ) {
            result.TargetFrame = PathOf( targetFrame );
            result.TargetCenterWorld = FormatVector( targetFrame.TransformPoint( targetCenterLocal ) );
            result.TargetHalfExtents = FormatVector( targetHalfExtents );
          }
        }

        var digMeasurement = FindObject<DigAreaMeasurement>();
        result.DigMeasurementObject = digMeasurement != null ? PathOf( digMeasurement.transform ) : null;
        var digBox = digMeasurement != null ? GetSerializedObjectReference<Box>( digMeasurement, "m_digAreaBox" ) : null;
        if ( digBox != null ) {
          result.DigBoxObject = PathOf( digBox.transform );
          result.DigBoxCenterWorld = FormatVector( digBox.transform.position );
          result.DigBoxHalfExtents = FormatVector( digBox.HalfExtents );
        }

        var digFootprint = FindSceneObject( "Dig_Footprint" );
        result.DigFootprint = FormatTransform( digFootprint );
        var dumpFootprint = FindSceneObject( "Dump_Footprint" );
        result.DumpFootprint = FormatTransform( dumpFootprint );
        var submergedBox = FindSceneObject( "SubmergedBox" );
        result.SubmergedBox = FormatTransform( submergedBox );

        if ( bucketReference != null && targetSensor != null )
          result.MinDistanceToTarget = targetSensor.TryMeasureBucketDistance( bucketReference, out var targetDistance ) ? targetDistance : -1.0f;

        if ( bucketReference != null && digMeasurement != null )
          result.MinDistanceToDig = digMeasurement.TryMeasureBucketDigAreaMetrics( bucketReference, out var digDistance, out var digDepth ) ? digDistance : -1.0f;

        WriteResult( true, "Distance binding diagnostics generated.", result );
      }
      catch ( Exception exception ) {
        WriteResult( false, exception.ToString(), result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static T FindObject<T>() where T : UnityEngine.Object
    {
      var objects = UnityEngine.Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      return objects != null && objects.Length > 0 ? objects[ 0 ] : null;
    }

    private static GameObject FindSceneObject( string objectName )
    {
      var objects = UnityEngine.Object.FindObjectsByType<GameObject>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      foreach ( var candidate in objects ) {
        if ( candidate != null && candidate.name == objectName )
          return candidate;
      }

      return null;
    }

    private static T GetSerializedObjectReference<T>( UnityEngine.Object target, string propertyName ) where T : UnityEngine.Object
    {
      var serialized = new SerializedObject( target );
      var property = serialized.FindProperty( propertyName );
      return property != null ? property.objectReferenceValue as T : null;
    }

    private static string FormatTransform( GameObject gameObject )
    {
      if ( gameObject == null )
        return null;

      return $"{PathOf( gameObject.transform )} pos={FormatVector( gameObject.transform.position )} scale={FormatVector( gameObject.transform.localScale )}";
    }

    private static string PathOf( Transform transform )
    {
      if ( transform == null )
        return null;

      var path = transform.name;
      var current = transform.parent;
      while ( current != null ) {
        path = $"{current.name}/{path}";
        current = current.parent;
      }

      return path;
    }

    private static string FormatVector( Vector3 value )
    {
      return $"{value.x:0.###}, {value.y:0.###}, {value.z:0.###}";
    }

    private static string AbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static void WriteResult( bool success, string message, DiagnosticsResult result )
    {
      var outputDirectory = AbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      var payload = new ResultEnvelope
      {
        Success = success,
        Message = message,
        Result = result
      };
      File.WriteAllText( Path.Combine( outputDirectory, "result.json" ), JsonUtility.ToJson( payload, true ) );
      Debug.Log( $"CodexDistanceBindingDiagnosticsUtility: {( success ? "success" : "failed" )}: {message}" );
    }

    [Serializable]
    private class ResultEnvelope
    {
      public bool Success;
      public string Message;
      public DiagnosticsResult Result;
    }

    [Serializable]
    private class DiagnosticsResult
    {
      public string Source;
      public string MachineRoot;
      public string BucketReference;
      public string BucketPosition;
      public string CurrentTargetName;
      public int TargetCount;
      public int TargetIndex;
      public string TargetObject;
      public string TargetSensorType;
      public string TargetFrame;
      public string TargetCenterWorld;
      public string TargetHalfExtents;
      public string DigMeasurementObject;
      public string DigBoxObject;
      public string DigBoxCenterWorld;
      public string DigBoxHalfExtents;
      public string DigFootprint;
      public string DumpFootprint;
      public string SubmergedBox;
      public float MinDistanceToTarget;
      public float MinDistanceToDig;
    }
  }
}
#endif

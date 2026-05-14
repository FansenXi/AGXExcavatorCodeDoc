#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexEnvironmentScaleOnePatchUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexEnvironmentScaleOnePatch.request";
    private const string OutputDirectory = "Temp/CodexEnvironmentScaleOnePatch";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexEnvironmentScaleOnePatchUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Apply Environment 1x Patch" )]
    public static void ApplyEnvironmentScaleOnePatchFromMenu()
    {
      ApplyEnvironmentScaleOnePatch( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}" );
        return;
      }

      ApplyEnvironmentScaleOnePatch( "request-file" );
    }

    private static void ApplyEnvironmentScaleOnePatch( string source )
    {
      s_isRunning = true;
      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely." );
          return;
        }

        PatchContainerBoxSensor();
        PatchSettledFloorCleaner();
        PatchDigAreaMeasurement();

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );
        WriteResult( true, $"Applied 1x environment range patch from {source}." );
      }
      catch ( Exception exception ) {
        WriteResult( false, exception.ToString() );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; environment 1x patch did not switch scenes." );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static void PatchContainerBoxSensor()
    {
      foreach ( var sensor in Resources.FindObjectsOfTypeAll<TerrainParticleBoxMassSensor>() ) {
        if ( sensor == null || !sensor.gameObject.scene.IsValid() )
          continue;

        var serializedObject = new SerializedObject( sensor );
        var targetName = serializedObject.FindProperty( "m_targetName" );
        if ( targetName == null || targetName.stringValue != "ContainerBox" )
          continue;

        SetFloat( serializedObject, "m_measurementHeight", 0.5833333f );
        SetFloat( serializedObject, "m_handledAsParticleRigidBodyPadding", 0.06666667f );
        SetFloat( serializedObject, "m_bucketUnloadTargetDistanceTolerance", 0.8333333f );
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( sensor );
      }
    }

    private static void PatchSettledFloorCleaner()
    {
      var cleaner = UnityEngine.Object.FindObjectOfType<SettledFloorParticleCleaner>();
      if ( cleaner == null )
        return;

      var serializedObject = new SerializedObject( cleaner );
      var excludedVolumes = serializedObject.FindProperty( "m_excludedVolumes" );
      if ( excludedVolumes == null )
        return;

      var preserved = new List<BoxVolumeSpec>();
      for ( var index = 0; index < excludedVolumes.arraySize; ++index ) {
        var element = excludedVolumes.GetArrayElementAtIndex( index );
        var frame = element.FindPropertyRelative( "m_frame" )?.objectReferenceValue as Transform;
        if ( frame == null || frame.name == "SubmergedBox" || frame.name == "Dig_Footprint" )
          continue;

        preserved.Add( new BoxVolumeSpec(
          frame,
          element.FindPropertyRelative( "m_centerLocal" )?.vector3Value ?? Vector3.zero,
          element.FindPropertyRelative( "m_halfExtents" )?.vector3Value ?? Vector3.zero ) );
      }

      var submergedBox = FindSceneObject( "SubmergedBox" )?.transform;
      if ( submergedBox != null )
        preserved.Add( new BoxVolumeSpec( submergedBox, Vector3.zero, new Vector3( 1.25f, 0.35f, 1.5f ) ) );

      var digFootprint = FindSceneObject( "Dig_Footprint" )?.transform;
      if ( digFootprint != null )
        preserved.Add( new BoxVolumeSpec( digFootprint, new Vector3( 0.0f, 0.35f, 0.0f ), new Vector3( 1.25f, 0.5f, 1.5f ) ) );

      excludedVolumes.arraySize = preserved.Count;
      for ( var index = 0; index < preserved.Count; ++index )
        SetBoxVolume( excludedVolumes.GetArrayElementAtIndex( index ), preserved[ index ] );

      serializedObject.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty( cleaner );
    }

    private static void PatchDigAreaMeasurement()
    {
      foreach ( var measurement in Resources.FindObjectsOfTypeAll<DigAreaMeasurement>() ) {
        if ( measurement == null || !measurement.gameObject.scene.IsValid() )
          continue;

        var serializedObject = new SerializedObject( measurement );
        SetFloat( serializedObject, "m_contourWidth", 0.05333333f );
        SetFloat( serializedObject, "m_depthHorizontalBlendDistance", 0.1666667f );
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( measurement );
      }
    }

    private static void SetFloat( SerializedObject serializedObject, string propertyName, float value )
    {
      var property = serializedObject.FindProperty( propertyName );
      if ( property != null )
        property.floatValue = value;
    }

    private static void SetBoxVolume( SerializedProperty element, BoxVolumeSpec spec )
    {
      element.FindPropertyRelative( "m_frame" ).objectReferenceValue = spec.Frame;
      element.FindPropertyRelative( "m_centerLocal" ).vector3Value = spec.CenterLocal;
      element.FindPropertyRelative( "m_halfExtents" ).vector3Value = spec.HalfExtents;
    }

    private static GameObject FindSceneObject( string name )
    {
      foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == name )
          return gameObject;
      }

      return null;
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static void WriteResult( bool success, string message )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         $"{{\"success\":{success.ToString().ToLowerInvariant()},\"message\":\"{message.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" )}\"}}" );
    }

    private readonly struct BoxVolumeSpec
    {
      public readonly Transform Frame;
      public readonly Vector3 CenterLocal;
      public readonly Vector3 HalfExtents;

      public BoxVolumeSpec( Transform frame, Vector3 centerLocal, Vector3 halfExtents )
      {
        Frame = frame;
        CenterLocal = centerLocal;
        HalfExtents = halfExtents;
      }
    }
  }
}
#endif

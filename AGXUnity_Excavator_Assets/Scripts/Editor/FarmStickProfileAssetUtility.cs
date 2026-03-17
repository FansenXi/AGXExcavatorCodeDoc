#if UNITY_EDITOR
using System;
using System.IO;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEditor;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Editor
{
  public static class FarmStickProfileAssetUtility
  {
    [MenuItem( "Tools/AGX Excavator/Create FarmStick Default Profiles" )]
    public static void CreateDefaultProfiles()
    {
      var targetDirectory = GetTargetDirectory();
      EnsureDirectoryExists( targetDirectory );

      var rightProfile = CreateOrUpdateProfileAsset( targetDirectory, "FarmStick_Excavator_Right_Default.asset", FarmStickHandedness.Right );
      CreateOrUpdateProfileAsset( targetDirectory, "FarmStick_Excavator_Left_Default.asset", FarmStickHandedness.Left );

      AssetDatabase.SaveAssets();
      AssetDatabase.Refresh();
      EditorUtility.FocusProjectWindow();
      Selection.activeObject = rightProfile;
    }

    private static FarmStickControlProfile CreateOrUpdateProfileAsset( string targetDirectory,
                                                                       string assetFileName,
                                                                       FarmStickHandedness handedness )
    {
      var assetPath = CombineAssetPath( targetDirectory, assetFileName );
      var profile = AssetDatabase.LoadAssetAtPath<FarmStickControlProfile>( assetPath );
      if ( profile == null )
        DeleteInvalidAssetAtPath( assetPath );

      profile = AssetDatabase.LoadAssetAtPath<FarmStickControlProfile>( assetPath );
      if ( profile == null ) {
        profile = ScriptableObject.CreateInstance<FarmStickControlProfile>();
        profile.ApplyDefaultExcavatorLayout( handedness );
        AssetDatabase.CreateAsset( profile, assetPath );
      }
      else {
        profile.ApplyDefaultExcavatorLayout( handedness );
        EditorUtility.SetDirty( profile );
      }

      return profile;
    }

    private static void DeleteInvalidAssetAtPath( string assetPath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName ?? Directory.GetCurrentDirectory();
      var normalizedAssetPath = assetPath.Replace( '/', Path.DirectorySeparatorChar );
      var absoluteAssetPath = Path.GetFullPath( Path.Combine( projectRoot, normalizedAssetPath ) );
      if ( !File.Exists( absoluteAssetPath ) )
        return;

      AssetDatabase.DeleteAsset( assetPath );
    }

    private static string GetTargetDirectory()
    {
      var instance = ScriptableObject.CreateInstance<FarmStickControlProfile>();
      try {
        var script = MonoScript.FromScriptableObject( instance );
        var scriptPath = AssetDatabase.GetAssetPath( script ).Replace( '\\', '/' );
        var scriptDirectory = Path.GetDirectoryName( scriptPath )?.Replace( '\\', '/' ) ?? "Assets";
        var marker = "/Scripts/";
        var markerIndex = scriptDirectory.IndexOf( marker, StringComparison.OrdinalIgnoreCase );
        var rootDirectory = markerIndex >= 0 ? scriptDirectory.Substring( 0, markerIndex ) : scriptDirectory;
        if ( !rootDirectory.StartsWith( "Assets", StringComparison.OrdinalIgnoreCase ) )
          rootDirectory = "Assets/AGXUnity_Excavator";
        return CombineAssetPath( rootDirectory, "Profiles/FarmStick" );
      }
      finally {
        UnityEngine.Object.DestroyImmediate( instance );
      }
    }

    private static void EnsureDirectoryExists( string assetDirectory )
    {
      var segments = assetDirectory.Split( new[] { '/' }, StringSplitOptions.RemoveEmptyEntries );
      if ( segments.Length == 0 )
        return;

      var currentPath = segments[ 0 ];
      for ( var segmentIndex = 1; segmentIndex < segments.Length; ++segmentIndex ) {
        var nextPath = CombineAssetPath( currentPath, segments[ segmentIndex ] );
        if ( !AssetDatabase.IsValidFolder( nextPath ) )
          AssetDatabase.CreateFolder( currentPath, segments[ segmentIndex ] );

        currentPath = nextPath;
      }
    }

    private static string CombineAssetPath( string left, string right )
    {
      return $"{left.TrimEnd( '/' )}/{right.TrimStart( '/' )}";
    }
  }
}
#endif

#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexFactoryCeilingLightUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexFactoryCeilingLight.request";
    private const string OutputDirectory = "Temp/CodexFactoryCeilingLight";
    private const string LayoutRootName = "CodexFactoryLayout";
    private const string MaterialDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials/CodexFactory";

    private const string AreaLightName = "FactoryCeilingTubeLight_01_RectAreaLight";
    private const string FillLightName = "FactoryCeilingTubeLight_01_DownFill";
    private const string TubeLightRootPrefix = "FactoryCeilingTubeLight_";
    private const int TubeLightCount = 3;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexFactoryCeilingLightUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Apply Factory Ceiling Tube Light" )]
    public static void ApplyFactoryCeilingTubeLightFromMenu()
    {
      ApplyFactoryCeilingTubeLight( "menu" );
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
        WriteResult( false, $"Could not delete request file: {exception.Message}", null );
        return;
      }

      ApplyFactoryCeilingTubeLight( "request-file" );
    }

    private static void ApplyFactoryCeilingTubeLight( string source )
    {
      s_isRunning = true;
      var result = new FactoryCeilingLightResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );
        EnsureMaterialDirectory();

        var layoutRoot = FindSceneObject( LayoutRootName );
        var xMinBeam = FindSceneObject( "FactoryTopBeam_XMin" );
        var xMaxBeam = FindSceneObject( "FactoryTopBeam_XMax" );
        if ( layoutRoot == null || xMinBeam == null || xMaxBeam == null ) {
          WriteResult( false, "Could not find CodexFactoryLayout and both X top beams.", result );
          return;
        }

        RemoveObjectsWithPrefix( TubeLightRootPrefix );
        RemoveObjectsNamed( AreaLightName );
        RemoveObjectsNamed( FillLightName );

        var tubeMaterial = GetOrCreateEmissiveMaterial( "CodexFactory_TubeLight_Emissive.mat",
                                                        new Color( 1.0f, 0.96f, 0.84f, 1.0f ),
                                                        new Color( 4.0f, 3.65f, 2.7f, 1.0f ) );
        var fixtureMaterial = GetOrCreateMaterial( "CodexFactory_TubeLight_Fixture.mat",
                                                   new Color( 0.10f, 0.10f, 0.095f, 1.0f ) );

        var xMin = xMinBeam.transform.localPosition.x;
        var xMax = xMaxBeam.transform.localPosition.x;
        if ( xMax < xMin ) {
          var temp = xMin;
          xMin = xMax;
          xMax = temp;
        }

        var zCenter = xMaxBeam.transform.localPosition.z;
        var depth = Mathf.Max( Mathf.Abs( xMinBeam.transform.localScale.z ),
                               Mathf.Abs( xMaxBeam.transform.localScale.z ) );
        var baseY = Mathf.Max( xMinBeam.transform.localPosition.y + Mathf.Abs( xMinBeam.transform.localScale.y ) * 0.5f,
                               xMaxBeam.transform.localPosition.y + Mathf.Abs( xMaxBeam.transform.localScale.y ) * 0.5f );
        var peakY = ResolveRoofPeakY( baseY );
        var roofThickness = ResolveRoofThickness();

        var lampLength = Mathf.Clamp( depth * 0.55f, 2.5f, 4.25f );
        const float tubeDiameter = 0.10f;
        var lightRange = Mathf.Clamp( lampLength * 1.9f, 6.0f, 9.5f );
        const float lightIntensity = 4.0f;
        var rootPaths = string.Empty;
        var tubePaths = string.Empty;
        var fixturePaths = string.Empty;
        var lightPaths = string.Empty;
        var positions = string.Empty;

        for ( var index = 0; index < TubeLightCount; ++index ) {
          var normalized = ( index + 1.0f ) / ( TubeLightCount + 1.0f );
          var x = Mathf.Lerp( xMin, xMax, normalized );
          var roofY = Mathf.Lerp( baseY, peakY, normalized );
          var lampY = roofY - roofThickness * 0.5f - 0.18f;
          var suffix = ( index + 1 ).ToString( "00", CultureInfo.InvariantCulture );
          var root = new GameObject( TubeLightRootPrefix + suffix );
          root.transform.SetParent( layoutRoot.transform, false );
          root.transform.localPosition = new Vector3( x, lampY, zCenter );
          root.transform.localRotation = Quaternion.identity;
          root.transform.localScale = Vector3.one;

          var fixture = GameObject.CreatePrimitive( PrimitiveType.Cube );
          fixture.name = $"{TubeLightRootPrefix}{suffix}_Fixture";
          fixture.transform.SetParent( root.transform, false );
          fixture.transform.localPosition = new Vector3( 0.0f, 0.075f, 0.0f );
          fixture.transform.localRotation = Quaternion.identity;
          fixture.transform.localScale = new Vector3( 0.24f, 0.045f, lampLength + 0.25f );
          ApplyMaterialAndRemoveCollider( fixture, fixtureMaterial );

          var tube = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
          tube.name = $"{TubeLightRootPrefix}{suffix}_Tube";
          tube.transform.SetParent( root.transform, false );
          tube.transform.localPosition = Vector3.zero;
          tube.transform.localRotation = Quaternion.Euler( 90.0f, 0.0f, 0.0f );
          tube.transform.localScale = new Vector3( tubeDiameter, lampLength * 0.5f, tubeDiameter );
          ApplyMaterialAndRemoveCollider( tube, tubeMaterial );

          var light = CreateTubeLightPoint( root.transform, $"{TubeLightRootPrefix}{suffix}_PointLight", lightIntensity, lightRange );
          AppendResultPath( ref rootPaths, GetHierarchyPath( root ) );
          AppendResultPath( ref tubePaths, GetHierarchyPath( tube ) );
          AppendResultPath( ref fixturePaths, GetHierarchyPath( fixture ) );
          AppendResultPath( ref lightPaths, GetHierarchyPath( light ) );
          AppendResultPath( ref positions, FormatVector( root.transform.localPosition ) );
        }

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.root = rootPaths;
        result.tube = tubePaths;
        result.fixture = fixturePaths;
        result.light = lightPaths;
        result.light_count = TubeLightCount;
        result.position = positions;
        result.tube_length = lampLength.ToString( "0.###", CultureInfo.InvariantCulture );
        result.light_range = lightRange.ToString( "0.###", CultureInfo.InvariantCulture );
        result.light_intensity = lightIntensity.ToString( "0.###", CultureInfo.InvariantCulture );
        result.message = $"Applied {TubeLightCount} bright ceiling tube lights from {source}. Tube length={lampLength:0.###}m, each with one point light at intensity={lightIntensity:0.###}.";
        WriteResult( true, result.message, result );
      }
      catch ( Exception exception ) {
        result.message = exception.ToString();
        WriteResult( false, result.message, result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static float ResolveRoofPeakY( float fallbackBaseY )
    {
      var extension = FindSceneObject( "FactoryRoof_XMax_Extension" );
      if ( extension == null )
        return fallbackBaseY;

      return Mathf.Max( fallbackBaseY,
                        extension.transform.localPosition.y + Mathf.Abs( extension.transform.localScale.y ) * 0.5f );
    }

    private static float ResolveRoofThickness()
    {
      var slab = FindSceneObject( "FactoryRoof_SlopedSlab" );
      if ( slab == null )
        return 0.125f;

      return Mathf.Max( Mathf.Abs( slab.transform.localScale.y ), 0.05f );
    }

    private static GameObject CreateTubeLightPoint( Transform parent, string name, float intensity, float range )
    {
      var lightObject = new GameObject( name );
      lightObject.transform.SetParent( parent, false );
      lightObject.transform.localPosition = new Vector3( 0.0f, -0.25f, 0.0f );
      lightObject.transform.localRotation = Quaternion.identity;
      lightObject.transform.localScale = Vector3.one;

      var light = lightObject.AddComponent<Light>();
      light.type = LightType.Point;
      light.color = new Color( 1.0f, 0.92f, 0.72f, 1.0f );
      light.intensity = intensity;
      light.range = range;
      light.shadows = LightShadows.Soft;
      light.shadowStrength = 0.25f;
      light.renderMode = LightRenderMode.ForcePixel;
      return lightObject;
    }

    private static void AppendResultPath( ref string output, string value )
    {
      if ( string.IsNullOrEmpty( output ) )
        output = value;
      else
        output += "; " + value;
    }

    private static void ApplyMaterialAndRemoveCollider( GameObject gameObject, Material material )
    {
      var renderer = gameObject.GetComponent<Renderer>();
      if ( renderer != null ) {
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
      }

      var collider = gameObject.GetComponent<Collider>();
      if ( collider != null )
        UnityEngine.Object.DestroyImmediate( collider );
    }

    private static Material GetOrCreateMaterial( string fileName, Color color )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
      var material = AssetDatabase.LoadAssetAtPath<Material>( assetPath );
      if ( material == null ) {
        var shader = Shader.Find( "Universal Render Pipeline/Lit" ) ??
                     Shader.Find( "Standard" );
        material = new Material( shader ) { name = Path.GetFileNameWithoutExtension( fileName ) };
        AssetDatabase.CreateAsset( material, assetPath );
      }

      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", color );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", color );

      material.DisableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
      material.DisableKeyword( "_ALPHABLEND_ON" );
      material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
      material.renderQueue = -1;
      EditorUtility.SetDirty( material );
      return material;
    }

    private static Material GetOrCreateEmissiveMaterial( string fileName, Color baseColor, Color emissionColor )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
      var material = AssetDatabase.LoadAssetAtPath<Material>( assetPath );
      if ( material == null ) {
        var shader = Shader.Find( "Universal Render Pipeline/Lit" ) ??
                     Shader.Find( "Standard" );
        material = new Material( shader ) { name = Path.GetFileNameWithoutExtension( fileName ) };
        AssetDatabase.CreateAsset( material, assetPath );
      }

      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", baseColor );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", baseColor );
      if ( material.HasProperty( "_EmissionColor" ) )
        material.SetColor( "_EmissionColor", emissionColor );

      material.EnableKeyword( "_EMISSION" );
      material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
      material.renderQueue = -1;
      EditorUtility.SetDirty( material );
      return material;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false, $"Active scene '{activeScene.path}' has unsaved changes; ceiling light utility did not switch scenes.", null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static string SaveCurrentSceneBackup( Scene scene )
    {
      if ( !scene.IsValid() )
        return string.Empty;

      EditorSceneManager.SaveScene( scene );

      var sceneAbsolutePath = GetProjectRelativeAbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = GetProjectRelativeAbsolutePath( "CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_ceiling_tube_light_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static void EnsureMaterialDirectory()
    {
      if ( AssetDatabase.IsValidFolder( MaterialDirectory ) )
        return;

      var parent = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials";
      if ( !AssetDatabase.IsValidFolder( parent ) )
        AssetDatabase.CreateFolder( "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets", "Materials" );

      AssetDatabase.CreateFolder( parent, "CodexFactory" );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      var transforms = Resources.FindObjectsOfTypeAll<Transform>();
      foreach ( var transform in transforms ) {
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name == objectName )
          return transform.gameObject;
      }

      return null;
    }

    private static void RemoveObjectsNamed( string objectName )
    {
      var transforms = Resources.FindObjectsOfTypeAll<Transform>();
      for ( var index = transforms.Length - 1; index >= 0; --index ) {
        var transform = transforms[ index ];
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name == objectName )
          UnityEngine.Object.DestroyImmediate( transform.gameObject );
      }
    }

    private static void RemoveObjectsWithPrefix( string objectNamePrefix )
    {
      var transforms = Resources.FindObjectsOfTypeAll<Transform>();
      for ( var index = transforms.Length - 1; index >= 0; --index ) {
        var transform = transforms[ index ];
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name.StartsWith( objectNamePrefix, StringComparison.Ordinal ) )
          UnityEngine.Object.DestroyImmediate( transform.gameObject );
      }
    }

    private static string GetHierarchyPath( GameObject gameObject )
    {
      if ( gameObject == null )
        return string.Empty;

      var path = gameObject.name;
      var current = gameObject.transform.parent;
      while ( current != null ) {
        path = current.name + "/" + path;
        current = current.parent;
      }

      return path;
    }

    private static string FormatVector( Vector3 vector )
    {
      return $"({vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###})";
    }

    private static void WriteResult( bool success, string message, FactoryCeilingLightResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new FactoryCeilingLightResult();

      result.success = success;
      result.message = message;

      File.WriteAllText( GetProjectRelativeAbsolutePath( Path.Combine( OutputDirectory, "result.json" ) ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string path )
    {
      return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), path ) );
    }

    [Serializable]
    private sealed class FactoryCeilingLightResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public string root;
      public string fixture;
      public string tube;
      public string light;
      public int light_count;
      public string position;
      public string tube_length;
      public string light_range;
      public string light_intensity;
    }
  }
}
#endif

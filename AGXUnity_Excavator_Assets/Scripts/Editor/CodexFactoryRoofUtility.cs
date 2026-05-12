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
  public static class CodexFactoryRoofUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexFactoryRoof.request";
    private const string OutputDirectory = "Temp/CodexFactoryRoof";
    private const string LayoutRootName = "CodexFactoryLayout";
    private const string MaterialDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials/CodexFactory";

    private const string ExtensionName = "FactoryRoof_XMax_Extension";
    private const string SlopedSlabName = "FactoryRoof_SlopedSlab";
    private const string SideZMinName = "FactoryRoofSide_ZMin";
    private const string SideZMaxName = "FactoryRoofSide_ZMax";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexFactoryRoofUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Apply Factory Roof" )]
    public static void ApplyFactoryRoofFromMenu()
    {
      ApplyFactoryRoof( "menu" );
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

      ApplyFactoryRoof( "request-file" );
    }

    private static void ApplyFactoryRoof( string source )
    {
      s_isRunning = true;
      var result = new FactoryRoofResult();

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

        RemoveObjectsNamed( ExtensionName );
        RemoveObjectsNamed( SlopedSlabName );
        RemoveObjectsNamed( SideZMinName );
        RemoveObjectsNamed( SideZMaxName );

        var material = GetOrCreateMaterial( "CodexFactory_RoofGreyBlack.mat", new Color( 0.12f, 0.12f, 0.12f, 1.0f ), false );
        const float roofExtensionHeight = 1.0f * 1.25f;
        const float roofThickness = 0.10f * 1.25f;

        var xMin = xMinBeam.transform.localPosition.x;
        var xMax = xMaxBeam.transform.localPosition.x;
        var zCenter = xMaxBeam.transform.localPosition.z;
        var depth = Mathf.Abs( xMaxBeam.transform.localScale.z );
        var zMin = zCenter - depth * 0.5f;
        var zMax = zCenter + depth * 0.5f;
        var baseY = Mathf.Max( xMinBeam.transform.localPosition.y + Mathf.Abs( xMinBeam.transform.localScale.y ) * 0.5f,
                               xMaxBeam.transform.localPosition.y + Mathf.Abs( xMaxBeam.transform.localScale.y ) * 0.5f );
        var peakY = baseY + roofExtensionHeight;
        var extensionThickness = Mathf.Abs( xMaxBeam.transform.localScale.x );
        var run = xMax - xMin;
        var roofLength = Mathf.Sqrt( run * run + roofExtensionHeight * roofExtensionHeight );
        var roofAngle = Mathf.Atan2( roofExtensionHeight, run ) * Mathf.Rad2Deg;

        var extension = CreateCube( layoutRoot.transform,
                                    ExtensionName,
                                    new Vector3( xMax, baseY + roofExtensionHeight * 0.5f, zCenter ),
                                    new Vector3( extensionThickness, roofExtensionHeight, depth ),
                                    material,
                                    Quaternion.identity );

        var slab = CreateCube( layoutRoot.transform,
                               SlopedSlabName,
                               new Vector3( ( xMin + xMax ) * 0.5f, ( baseY + peakY ) * 0.5f, zCenter ),
                               new Vector3( roofLength, roofThickness, depth ),
                               material,
                               Quaternion.Euler( 0.0f, 0.0f, roofAngle ) );

        var sideZMin = CreateRoofSidePanel( layoutRoot.transform,
                                            SideZMinName,
                                            new Vector3( xMin, baseY, zMin ),
                                            new Vector3( xMax, baseY, zMin ),
                                            new Vector3( xMax, peakY, zMin ),
                                            material );
        var sideZMax = CreateRoofSidePanel( layoutRoot.transform,
                                            SideZMaxName,
                                            new Vector3( xMin, baseY, zMax ),
                                            new Vector3( xMax, baseY, zMax ),
                                            new Vector3( xMax, peakY, zMax ),
                                            material );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.extension = GetHierarchyPath( extension );
        result.sloped_slab = GetHierarchyPath( slab );
        result.side_zmin = GetHierarchyPath( sideZMin );
        result.side_zmax = GetHierarchyPath( sideZMax );
        result.base_y = baseY.ToString( "0.###", CultureInfo.InvariantCulture );
        result.peak_y = peakY.ToString( "0.###", CultureInfo.InvariantCulture );
        result.roof_angle_degrees = roofAngle.ToString( "0.###", CultureInfo.InvariantCulture );
        result.message = $"Applied factory roof from {source}. XMax extension height={roofExtensionHeight:0.###}m, roof angle={roofAngle:0.###} degrees.";
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

    private static GameObject CreateCube( Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material, Quaternion localRotation )
    {
      var cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
      cube.name = name;
      cube.transform.SetParent( parent, false );
      cube.transform.localPosition = localPosition;
      cube.transform.localRotation = localRotation;
      cube.transform.localScale = localScale;

      var renderer = cube.GetComponent<Renderer>();
      if ( renderer != null )
        renderer.sharedMaterial = material;

      var collider = cube.GetComponent<Collider>();
      if ( collider != null )
        UnityEngine.Object.DestroyImmediate( collider );

      return cube;
    }

    private static GameObject CreateRoofSidePanel( Transform parent, string name, Vector3 a, Vector3 b, Vector3 c, Material material )
    {
      var panel = new GameObject( name );
      panel.transform.SetParent( parent, false );
      panel.transform.localPosition = Vector3.zero;
      panel.transform.localRotation = Quaternion.identity;
      panel.transform.localScale = Vector3.one;

      var mesh = new Mesh { name = name + "_Mesh" };
      mesh.vertices = new[] { a, b, c };
      mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 };
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();

      var meshFilter = panel.AddComponent<MeshFilter>();
      meshFilter.sharedMesh = mesh;
      var renderer = panel.AddComponent<MeshRenderer>();
      renderer.sharedMaterial = material;
      return panel;
    }

    private static Material GetOrCreateMaterial( string fileName, Color color, bool transparent )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
      var material = AssetDatabase.LoadAssetAtPath<Material>( assetPath );
      if ( material == null ) {
        var shader = Shader.Find( "Universal Render Pipeline/Lit" ) ??
                     Shader.Find( "Universal Render Pipeline/Unlit" ) ??
                     Shader.Find( "Standard" );
        material = new Material( shader ) { name = Path.GetFileNameWithoutExtension( fileName ) };
        AssetDatabase.CreateAsset( material, assetPath );
      }

      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", color );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", color );

      if ( material.HasProperty( "_Surface" ) )
        material.SetFloat( "_Surface", transparent ? 1.0f : 0.0f );
      if ( material.HasProperty( "_ZWrite" ) )
        material.SetFloat( "_ZWrite", transparent ? 0.0f : 1.0f );
      if ( material.HasProperty( "_Mode" ) )
        material.SetFloat( "_Mode", transparent ? 2.0f : 0.0f );

      if ( transparent ) {
        material.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
        material.EnableKeyword( "_ALPHABLEND_ON" );
        material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
      }
      else {
        material.DisableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
        material.DisableKeyword( "_ALPHABLEND_ON" );
        material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
        material.renderQueue = -1;
      }

      EditorUtility.SetDirty( material );
      return material;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false, $"Active scene '{activeScene.path}' has unsaved changes; factory roof utility did not switch scenes.", null );
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
                                     "AGXUnity_Excavator_before_factory_roof_" +
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

    private static void WriteResult( bool success, string message, FactoryRoofResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new FactoryRoofResult();

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
    private sealed class FactoryRoofResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public string extension;
      public string sloped_slab;
      public string side_zmin;
      public string side_zmax;
      public string base_y;
      public string peak_y;
      public string roof_angle_degrees;
    }
  }
}
#endif

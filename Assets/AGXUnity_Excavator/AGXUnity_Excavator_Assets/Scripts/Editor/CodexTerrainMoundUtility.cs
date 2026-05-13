#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity;
using AGXUnity.Collide;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexTerrainMoundUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexTerrainMound.request";
    private const string CameraCaptureRequestPath = "Temp/CodexCameraCapture.request";
    private const string OutputDirectory = "Temp/CodexTerrainMound";
    private const string ProbeMarkerName = "CodexSceneProbe_Marker";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexTerrainMoundUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Flatten Terrain And Build Large Soil Mound" )]
    public static void BuildTaskAreaSoilMoundFromMenu()
    {
      ReshapeTerrainForFlatTaskArea( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var cameraCaptureRequestPath = GetProjectRelativeAbsolutePath( CameraCaptureRequestPath );
      if ( File.Exists( cameraCaptureRequestPath ) ) {
        File.Delete( cameraCaptureRequestPath );
        CaptureInspectionViews( "camera-capture-request" );
        return;
      }

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}", null );
        return;
      }

      ReshapeTerrainForFlatTaskArea( "request-file" );
    }

    private static void CaptureInspectionViews( string source )
    {
      s_isRunning = true;
      var screenshots = new ScreenshotSet();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", null );
          return;
        }

        var terrain = ResolveTerrain();
        if ( terrain == null || !TryResolveDigArea( out var digAreaCenter, out _ ) ) {
          WriteResult( false, "Could not resolve terrain and DigArea for camera capture.", null );
          return;
        }

        var excavatorRoot = ResolveExcavatorRoot();
        var moundSurfaceCenter = new Vector3( digAreaCenter.x,
                                              TerrainWorldHeightAt( terrain, digAreaCenter ),
                                              digAreaCenter.z );
        CreateOrUpdateInspectionCameras( moundSurfaceCenter, excavatorRoot );
        CreateOrUpdateWideVerificationCameras( moundSurfaceCenter, excavatorRoot );

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        screenshots.task_area = CaptureNamedCamera( "CodexTaskAreaCamera", "task_area_mound.png" );
        screenshots.overview = CaptureNamedCamera( "CodexOverviewCamera", "overview.png" );
        screenshots.top_down = CaptureNamedCamera( "CodexTopDownCamera", "top_down.png" );
        screenshots.side_mound = CaptureNamedCamera( "CodexSideMoundCamera", "side_mound.png" );
        screenshots.wide_oblique = CaptureNamedCamera( "CodexWideObliqueCamera", "wide_oblique.png" );
        screenshots.main_camera = CaptureMainCamera();

        WriteResult( true, $"Captured inspection cameras from {source}.", screenshots );
      }
      catch ( System.Exception exception ) {
        WriteResult( false, exception.ToString(), screenshots );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void ReshapeTerrainForFlatTaskArea( string source )
    {
      s_isRunning = true;
      var screenshots = new ScreenshotSet();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", null );
          return;
        }

        RemoveObjectIfPresent( ProbeMarkerName );

        var terrain = ResolveTerrain();
        if ( terrain == null || terrain.terrainData == null ) {
          WriteResult( false, "Could not find the main Unity Terrain/TerrainData.", null );
          return;
        }

        if ( !TryResolveDigArea( out var digAreaCenter, out var digAreaHalfExtents ) ) {
          WriteResult( false, "Could not resolve AGXUnity.RigidBody.DigArea and its Box footprint.", null );
          return;
        }

        var flatWorldY = EstimateFlatGroundHeightFromDigAreaPerimeter( terrain, digAreaCenter, digAreaHalfExtents );
        var excavatorRoot = ResolveExcavatorRoot();
        var previousExcavatorGroundY = excavatorRoot != null ?
                                       EstimateTerrainSupportHeightUnderObject( terrain, excavatorRoot ) :
                                       float.NaN;

        var platformHeightMeters = float.IsNaN( previousExcavatorGroundY ) ?
                                   2.4f :
                                   Mathf.Max( 0.0f, previousExcavatorGroundY - flatWorldY );
        var moundPeakHeightMeters = Mathf.Clamp( Mathf.Max( platformHeightMeters + 0.25f, 2.4f ), 2.4f, 4.5f );

        FlattenTerrain( terrain, flatWorldY );
        var mound = ApplyLargeMoundToTerrain( terrain,
                                              digAreaCenter,
                                              digAreaHalfExtents,
                                              flatWorldY,
                                              moundPeakHeightMeters );

        var excavatorShiftY = 0.0f;
        if ( excavatorRoot != null && !float.IsNaN( previousExcavatorGroundY ) ) {
          excavatorShiftY = flatWorldY - previousExcavatorGroundY;
          excavatorRoot.transform.position += Vector3.up * excavatorShiftY;
          EditorUtility.SetDirty( excavatorRoot );
        }

        CreateOrUpdateInspectionCameras( mound.CenterWorld, excavatorRoot );

        EditorUtility.SetDirty( terrain.terrainData );
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        screenshots.task_area = CaptureNamedCamera( "CodexTaskAreaCamera", "task_area_mound.png" );
        screenshots.overview = CaptureNamedCamera( "CodexOverviewCamera", "overview.png" );
        screenshots.top_down = CaptureNamedCamera( "CodexTopDownCamera", "top_down.png" );
        screenshots.side_mound = CaptureNamedCamera( "CodexSideMoundCamera", "side_mound.png" );
        screenshots.wide_oblique = CaptureNamedCamera( "CodexWideObliqueCamera", "wide_oblique.png" );
        screenshots.main_camera = CaptureMainCamera();

        var message = $"Reshaped terrain from {source}: flat_y={flatWorldY:0.###}, " +
                      $"previous_excavator_ground_y={( float.IsNaN( previousExcavatorGroundY ) ? "unknown" : previousExcavatorGroundY.ToString( "0.###" ) )}, " +
                      $"excavator_shift_y={excavatorShiftY:0.###}, " +
                      $"mound_center={FormatVector( mound.CenterWorld )}, " +
                      $"mound_peak_height_m={mound.PeakHeightMeters:0.###}, " +
                      $"mound_radius_x_m={mound.RadiusXMeters:0.###}, " +
                      $"mound_radius_z_m={mound.RadiusZMeters:0.###}.";
        WriteResult( true, message, screenshots );
      }
      catch ( System.Exception exception ) {
        WriteResult( false, exception.ToString(), screenshots );
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
                     $"Active scene '{activeScene.path}' has unsaved changes; mound builder did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static Terrain ResolveTerrain()
    {
      var namedTerrain = GameObject.Find( "Terrain" );
      if ( namedTerrain != null ) {
        var terrain = namedTerrain.GetComponent<Terrain>();
        if ( terrain != null )
          return terrain;
      }

      return Terrain.activeTerrain ?? UnityEngine.Object.FindObjectOfType<Terrain>();
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var excavators = Resources.FindObjectsOfTypeAll<Excavator>();
      foreach ( var excavator in excavators ) {
        if ( excavator == null || !excavator.gameObject.scene.IsValid() )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( excavator.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() )
          return prefabRoot;

        return excavator.transform.root != null ? excavator.transform.root.gameObject : excavator.gameObject;
      }

      return FindSceneObject( "Excavator CAT 365 Tracked" ) ??
             FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
             FindSceneObject( "Excavator" );
    }

    private static bool TryResolveDigArea( out Vector3 centerWorld, out Vector3 halfExtents )
    {
      centerWorld = Vector3.zero;
      halfExtents = Vector3.zero;

      var digAreaRoot = GameObject.Find( "AGXUnity.RigidBody.DigArea" );
      if ( digAreaRoot == null )
        return false;

      var boxes = digAreaRoot.GetComponentsInChildren<Box>( true );
      if ( boxes == null || boxes.Length == 0 )
        return false;

      Box bestBox = null;
      var bestHalfHeight = float.PositiveInfinity;
      foreach ( var box in boxes ) {
        if ( box == null )
          continue;

        var candidateHalfExtents = box.HalfExtents;
        if ( candidateHalfExtents.x <= 0.0f || candidateHalfExtents.y <= 0.0f || candidateHalfExtents.z <= 0.0f )
          continue;

        if ( candidateHalfExtents.y < bestHalfHeight ) {
          bestBox = box;
          bestHalfHeight = candidateHalfExtents.y;
        }
      }

      if ( bestBox == null )
        return false;

      centerWorld = bestBox.transform.TransformPoint( Vector3.zero );
      halfExtents = bestBox.HalfExtents;
      return true;
    }

    private static float EstimateFlatGroundHeightFromDigAreaPerimeter( Terrain terrain,
                                                                       Vector3 digAreaCenterWorld,
                                                                       Vector3 digAreaHalfExtents )
    {
      var samples = new List<float>();
      var xValues = new[] { -0.95f, -0.55f, 0.0f, 0.55f, 0.95f };
      var zValues = new[] { -0.95f, -0.55f, 0.0f, 0.55f, 0.95f };

      foreach ( var x in xValues ) {
        foreach ( var z in zValues ) {
          var normalizedDistance = Mathf.Sqrt( x * x + z * z );
          if ( normalizedDistance < 0.78f )
            continue;

          var worldPoint = new Vector3( digAreaCenterWorld.x + x * digAreaHalfExtents.x,
                                        digAreaCenterWorld.y,
                                        digAreaCenterWorld.z + z * digAreaHalfExtents.z );
          samples.Add( TerrainWorldHeightAt( terrain, worldPoint ) );
        }
      }

      samples.Sort();
      if ( samples.Count == 0 )
        return TerrainWorldHeightAt( terrain, digAreaCenterWorld );

      return samples[ samples.Count / 2 ];
    }

    private static float EstimateTerrainSupportHeightUnderObject( Terrain terrain, GameObject root )
    {
      if ( terrain == null || root == null )
        return float.NaN;

      if ( TryGetRendererBounds( root, out var bounds ) ) {
        var supportSamples = new[] {
          new Vector3( bounds.center.x, 0.0f, bounds.center.z ),
          new Vector3( bounds.min.x, 0.0f, bounds.center.z ),
          new Vector3( bounds.max.x, 0.0f, bounds.center.z ),
          new Vector3( bounds.center.x, 0.0f, bounds.min.z ),
          new Vector3( bounds.center.x, 0.0f, bounds.max.z )
        };

        var maxHeight = float.NegativeInfinity;
        foreach ( var sample in supportSamples )
          maxHeight = Mathf.Max( maxHeight, TerrainWorldHeightAt( terrain, sample ) );

        if ( !float.IsNegativeInfinity( maxHeight ) )
          return maxHeight;
      }

      return TerrainWorldHeightAt( terrain, root.transform.position );
    }

    private static bool TryGetRendererBounds( GameObject root, out Bounds bounds )
    {
      bounds = default;
      if ( root == null )
        return false;

      var renderers = root.GetComponentsInChildren<Renderer>( true );
      var hasBounds = false;
      foreach ( var renderer in renderers ) {
        if ( renderer == null )
          continue;

        if ( !hasBounds ) {
          bounds = renderer.bounds;
          hasBounds = true;
        }
        else
          bounds.Encapsulate( renderer.bounds );
      }

      return hasBounds;
    }

    private static float TerrainWorldHeightAt( Terrain terrain, Vector3 worldPoint )
    {
      return terrain.transform.position.y + terrain.SampleHeight( worldPoint );
    }

    private static void FlattenTerrain( Terrain terrain, float flatWorldY )
    {
      var data = terrain.terrainData;
      var resolution = data.heightmapResolution;
      var terrainOrigin = terrain.transform.position;
      var flatNormalized = Mathf.InverseLerp( terrainOrigin.y, terrainOrigin.y + data.size.y, flatWorldY );
      var heights = new float[ resolution, resolution ];

      for ( var z = 0; z < resolution; ++z ) {
        for ( var x = 0; x < resolution; ++x )
          heights[ z, x ] = flatNormalized;
      }

      data.SetHeights( 0, 0, heights );
      terrain.Flush();
    }

    private static MoundInfo ApplyLargeMoundToTerrain( Terrain terrain,
                                                       Vector3 digAreaCenterWorld,
                                                       Vector3 digAreaHalfExtents,
                                                       float flatWorldY,
                                                       float peakHeightMeters )
    {
      var data = terrain.terrainData;
      var resolution = data.heightmapResolution;
      var size = data.size;
      var terrainOrigin = terrain.transform.position;

      var centerWorld = new Vector3( digAreaCenterWorld.x, flatWorldY, digAreaCenterWorld.z );
      var radiusX = Mathf.Clamp( digAreaHalfExtents.x * 0.9f, 3.8f, Mathf.Max( 3.8f, digAreaHalfExtents.x - 0.25f ) );
      var radiusZ = Mathf.Clamp( digAreaHalfExtents.z * 0.9f, 3.2f, Mathf.Max( 3.2f, digAreaHalfExtents.z - 0.25f ) );

      var minX = Mathf.Clamp( WorldToHeightmapX( centerWorld.x - radiusX, terrainOrigin, size, resolution ), 0, resolution - 1 );
      var maxX = Mathf.Clamp( WorldToHeightmapX( centerWorld.x + radiusX, terrainOrigin, size, resolution ), 0, resolution - 1 );
      var minZ = Mathf.Clamp( WorldToHeightmapZ( centerWorld.z - radiusZ, terrainOrigin, size, resolution ), 0, resolution - 1 );
      var maxZ = Mathf.Clamp( WorldToHeightmapZ( centerWorld.z + radiusZ, terrainOrigin, size, resolution ), 0, resolution - 1 );

      if ( maxX < minX )
        Swap( ref minX, ref maxX );
      if ( maxZ < minZ )
        Swap( ref minZ, ref maxZ );

      var width = Mathf.Max( 1, maxX - minX + 1 );
      var height = Mathf.Max( 1, maxZ - minZ + 1 );
      var heights = data.GetHeights( minX, minZ, width, height );
      var flatNormalized = Mathf.InverseLerp( terrainOrigin.y, terrainOrigin.y + size.y, flatWorldY );

      for ( var z = 0; z < height; ++z ) {
        for ( var x = 0; x < width; ++x ) {
          var worldX = HeightmapToWorldX( minX + x, terrainOrigin, size, resolution );
          var worldZ = HeightmapToWorldZ( minZ + z, terrainOrigin, size, resolution );
          var normalizedDistance = Mathf.Sqrt( Squared( ( worldX - centerWorld.x ) / radiusX ) +
                                               Squared( ( worldZ - centerWorld.z ) / radiusZ ) );
          if ( normalizedDistance > 1.0f )
            continue;

          var dome = Mathf.Pow( Mathf.Clamp01( 1.0f - normalizedDistance * normalizedDistance ), 1.15f );
          var shoulder = 0.22f * Mathf.SmoothStep( 1.0f, 0.0f, normalizedDistance );
          var ripple = 0.05f * Mathf.Sin( worldX * 1.9f + worldZ * 0.7f ) +
                       0.025f * Mathf.Sin( worldX * 4.4f - worldZ * 1.2f );
          var moundHeightMeters = peakHeightMeters * Mathf.Clamp01( dome + shoulder + ripple * dome );
          heights[ z, x ] = Mathf.Clamp01( flatNormalized + moundHeightMeters / size.y );
        }
      }

      data.SetHeights( minX, minZ, heights );
      terrain.Flush();

      return new MoundInfo
      {
        CenterWorld = centerWorld,
        PeakHeightMeters = peakHeightMeters,
        RadiusXMeters = radiusX,
        RadiusZMeters = radiusZ
      };
    }

    private static int WorldToHeightmapX( float worldX, Vector3 terrainOrigin, Vector3 terrainSize, int resolution )
    {
      return Mathf.RoundToInt( Mathf.InverseLerp( terrainOrigin.x, terrainOrigin.x + terrainSize.x, worldX ) * ( resolution - 1 ) );
    }

    private static int WorldToHeightmapZ( float worldZ, Vector3 terrainOrigin, Vector3 terrainSize, int resolution )
    {
      return Mathf.RoundToInt( Mathf.InverseLerp( terrainOrigin.z, terrainOrigin.z + terrainSize.z, worldZ ) * ( resolution - 1 ) );
    }

    private static float HeightmapToWorldX( int x, Vector3 terrainOrigin, Vector3 terrainSize, int resolution )
    {
      return terrainOrigin.x + terrainSize.x * ( x / (float)( resolution - 1 ) );
    }

    private static float HeightmapToWorldZ( int z, Vector3 terrainOrigin, Vector3 terrainSize, int resolution )
    {
      return terrainOrigin.z + terrainSize.z * ( z / (float)( resolution - 1 ) );
    }

    private static float Squared( float value )
    {
      return value * value;
    }

    private static void Swap( ref int left, ref int right )
    {
      var temporary = left;
      left = right;
      right = temporary;
    }

    private static void CreateOrUpdateInspectionCameras( Vector3 moundCenterWorld, GameObject excavatorRoot )
    {
      var focus = moundCenterWorld;
      if ( excavatorRoot != null && TryGetRendererBounds( excavatorRoot, out var excavatorBounds ) )
        focus = Vector3.Lerp( moundCenterWorld, excavatorBounds.center, 0.38f );

      ConfigurePerspectiveCamera( "CodexTaskAreaCamera",
                                  moundCenterWorld + new Vector3( 9.0f, 5.8f, -9.0f ),
                                  moundCenterWorld + Vector3.up * 0.55f,
                                  44.0f );

      ConfigurePerspectiveCamera( "CodexOverviewCamera",
                                  focus + new Vector3( 17.0f, 12.0f, -17.0f ),
                                  focus + Vector3.up * 0.8f,
                                  52.0f );

      ConfigureTopDownCamera( "CodexTopDownCamera",
                              focus + new Vector3( 0.0f, 28.0f, 0.0f ),
                              focus,
                              16.0f );

      CreateOrUpdateWideVerificationCameras( moundCenterWorld, excavatorRoot );
    }

    private static void CreateOrUpdateWideVerificationCameras( Vector3 moundCenterWorld, GameObject excavatorRoot )
    {
      var focus = moundCenterWorld;
      if ( excavatorRoot != null && TryGetRendererBounds( excavatorRoot, out var excavatorBounds ) )
        focus = Vector3.Lerp( moundCenterWorld, excavatorBounds.center, 0.25f );

      ConfigurePerspectiveCamera( "CodexSideMoundCamera",
                                  moundCenterWorld + new Vector3( 0.0f, 3.5f, -13.0f ),
                                  moundCenterWorld + Vector3.up * 0.75f,
                                  58.0f );

      ConfigurePerspectiveCamera( "CodexWideObliqueCamera",
                                  focus + new Vector3( 13.0f, 8.5f, -15.0f ),
                                  focus + Vector3.up * 0.65f,
                                  56.0f );
    }

    private static void ConfigurePerspectiveCamera( string cameraName, Vector3 position, Vector3 lookAt, float fieldOfView )
    {
      var camera = GetOrCreateCamera( cameraName );
      camera.orthographic = false;
      camera.fieldOfView = fieldOfView;
      camera.nearClipPlane = 0.05f;
      camera.farClipPlane = 500.0f;
      camera.clearFlags = CameraClearFlags.Skybox;
      camera.transform.position = position;
      camera.transform.LookAt( lookAt );
      camera.enabled = false;
      EditorUtility.SetDirty( camera.gameObject );
    }

    private static void ConfigureTopDownCamera( string cameraName, Vector3 position, Vector3 lookAt, float orthographicSize )
    {
      var camera = GetOrCreateCamera( cameraName );
      camera.orthographic = true;
      camera.orthographicSize = orthographicSize;
      camera.nearClipPlane = 0.05f;
      camera.farClipPlane = 500.0f;
      camera.clearFlags = CameraClearFlags.Skybox;
      camera.transform.position = position;
      camera.transform.LookAt( lookAt );
      camera.enabled = false;
      EditorUtility.SetDirty( camera.gameObject );
    }

    private static Camera GetOrCreateCamera( string cameraName )
    {
      var cameraObject = FindSceneObject( cameraName );
      if ( cameraObject == null )
        cameraObject = new GameObject( cameraName );

      var camera = cameraObject.GetComponent<Camera>();
      if ( camera == null )
        camera = cameraObject.AddComponent<Camera>();

      return camera;
    }

    private static string CaptureNamedCamera( string cameraName, string fileName )
    {
      var cameraObject = FindSceneObject( cameraName );
      var camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
      return camera == null ? null : CaptureCamera( camera, fileName );
    }

    private static string CaptureMainCamera()
    {
      var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
      return camera == null ? null : CaptureCamera( camera, "main_camera.png" );
    }

    private static string CaptureCamera( Camera camera, string fileName )
    {
      const int width = 1280;
      const int height = 720;

      var absolutePath = Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), fileName );
      var previousTarget = camera.targetTexture;
      var previousActive = RenderTexture.active;
      var renderTexture = new RenderTexture( width, height, 24 );
      var texture = new Texture2D( width, height, TextureFormat.RGB24, false );

      try {
        camera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        camera.Render();
        texture.ReadPixels( new Rect( 0, 0, width, height ), 0, 0 );
        texture.Apply();
        File.WriteAllBytes( absolutePath, texture.EncodeToPNG() );
        return absolutePath;
      }
      finally {
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate( texture );
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate( renderTexture );
      }
    }

    private static GameObject FindSceneObject( string objectName )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      for ( var index = 0; index < objects.Length; ++index ) {
        var candidate = objects[ index ];
        if ( candidate == null || candidate.name != objectName || !candidate.scene.IsValid() )
          continue;

        return candidate;
      }

      return null;
    }

    private static void RemoveObjectIfPresent( string objectName )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      for ( var index = 0; index < objects.Length; ++index ) {
        var candidate = objects[ index ];
        if ( candidate == null || candidate.name != objectName || !candidate.scene.IsValid() )
          continue;

        UnityEngine.Object.DestroyImmediate( candidate );
      }
    }

    private static void EnsureOutputDirectoryExists()
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
    }

    private static void WriteResult( bool success, string message, ScreenshotSet screenshots )
    {
      EnsureOutputDirectoryExists();
      var resultPath = Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" );
      var json = JsonUtility.ToJson( new MoundResult
      {
        success = success,
        message = message,
        task_area_screenshot = screenshots?.task_area,
        overview_screenshot = screenshots?.overview,
        top_down_screenshot = screenshots?.top_down,
        side_mound_screenshot = screenshots?.side_mound,
        wide_oblique_screenshot = screenshots?.wide_oblique,
        main_camera_screenshot = screenshots?.main_camera,
        timestamp_utc = DateTime.UtcNow.ToString( "O" )
      }, true );
      File.WriteAllText( resultPath, json );
      Debug.Log( $"Codex terrain mound: {message}" );
    }

    private static string GetProjectRelativeAbsolutePath( string relativePath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName ?? Directory.GetCurrentDirectory();
      return Path.GetFullPath( Path.Combine( projectRoot, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );
    }

    private static string FormatVector( Vector3 value )
    {
      return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }

    private struct MoundInfo
    {
      public Vector3 CenterWorld;
      public float PeakHeightMeters;
      public float RadiusXMeters;
      public float RadiusZMeters;
    }

    [Serializable]
    private class ScreenshotSet
    {
      public string task_area;
      public string overview;
      public string top_down;
      public string side_mound;
      public string wide_oblique;
      public string main_camera;
    }

    [Serializable]
    private class MoundResult
    {
      public bool success;
      public string message;
      public string task_area_screenshot;
      public string overview_screenshot;
      public string top_down_screenshot;
      public string side_mound_screenshot;
      public string wide_oblique_screenshot;
      public string main_camera_screenshot;
      public string timestamp_utc;
    }
  }
}
#endif

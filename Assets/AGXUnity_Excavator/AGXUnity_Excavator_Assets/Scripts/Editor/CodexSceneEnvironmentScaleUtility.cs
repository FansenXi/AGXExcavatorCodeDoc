#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexSceneEnvironmentScaleUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexSceneEnvironmentScale.request";
    private const string OutputDirectory = "Temp/CodexSceneEnvironmentScale";
    private const float MinimumScale = 0.05f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexSceneEnvironmentScaleUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Scale Scene Environment To Default 1.5x" )]
    public static void ScaleSceneEnvironmentToDefaultFromMenu()
    {
      ScaleSceneEnvironment( ScaleRequest.CreateDefault( "menu" ) );
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

      var rawRequest = string.Empty;
      try {
        rawRequest = File.ReadAllText( requestPath );
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new SceneEnvironmentScaleResult {
          success = false,
          message = "Could not read/delete request file: " + exception.Message
        } );
        return;
      }

      ScaleSceneEnvironment( ParseRequest( rawRequest, "request-file" ) );
    }

    private static void ScaleSceneEnvironment( ScaleRequest request )
    {
      s_isRunning = true;
      var result = new SceneEnvironmentScaleResult {
        target_scale = FormatFloat( request.TargetScale ),
        pivot_world = FormatVector( request.PivotWorld ),
        apply_environment_physics = request.ApplyEnvironmentPhysics
      };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          result.message = "Target scene is not open and could not be opened safely.";
          WriteResult( result );
          return;
        }

        if ( request.TargetScale < MinimumScale ) {
          result.message = $"Target scale must be >= {MinimumScale:0.###}.";
          WriteResult( result );
          return;
        }

        var excavatorRoot = ResolveExcavatorRoot();
        result.excavator_root = excavatorRoot != null ? GetHierarchyPath( excavatorRoot ) : "not-found";

        var currentScale = request.HasCurrentScale ?
                             request.CurrentScale :
                             DetectCurrentEnvironmentScale( result );
        if ( currentScale < MinimumScale ) {
          result.message = $"Current scale could not be resolved to a valid value: {currentScale:0.###}.";
          WriteResult( result );
          return;
        }

        result.current_scale = FormatFloat( currentScale );
        var scaleRatio = request.TargetScale / currentScale;
        result.applied_scale_ratio = FormatFloat( scaleRatio );

        if ( Mathf.Abs( scaleRatio - 1.0f ) < 0.0005f ) {
          result.success = true;
          result.message = $"Scene environment already appears to be at {result.target_scale}x.";
          WriteResult( result );
          return;
        }

        result.scene_backup_path = BackupSceneFile( scene );

        var scaleRoots = CollectSceneScaleRoots( scene, excavatorRoot != null ? excavatorRoot.transform : null, request, result );
        result.scale_root_count = scaleRoots.Count;

        var transformScaledShapes = new HashSet<Transform>();
        foreach ( var root in scaleRoots )
          ScaleRootTransform( root, scaleRatio, request.PivotWorld, transformScaledShapes, result );

        var terrainData = new HashSet<TerrainData>();
        foreach ( var root in scaleRoots ) {
          ScaleAgxShapes( root, scaleRatio, transformScaledShapes, result );
          ScaleAgxRigidBodies( root, scaleRatio, result );
          ScaleAgxConstraints( root, scaleRatio, result );
          ScaleTerrains( root, scaleRatio, terrainData, result );
          ScaleCamerasAndLights( root, scaleRatio, request, result );
        }

        if ( request.ApplyEnvironmentPhysics )
          CodexEnvironmentPhysicsUtility.ApplyEnvironmentPhysicsFromFactoryBuilder();

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.success = true;
        result.message = $"Scaled non-excavator scene environment from {result.current_scale}x to {result.target_scale}x.";
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

    private static ScaleRequest ParseRequest( string rawRequest, string source )
    {
      var request = ScaleRequest.CreateDefault( source );
      if ( string.IsNullOrWhiteSpace( rawRequest ) )
        return request;

      var lines = rawRequest.Replace( "\r", "\n" ).Split( new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries );
      foreach ( var rawLine in lines ) {
        var line = rawLine.Trim();
        if ( line.Length == 0 || line.StartsWith( "#", StringComparison.Ordinal ) )
          continue;

        var separator = line.IndexOf( '=' );
        if ( separator < 0 )
          separator = line.IndexOf( ':' );
        if ( separator < 0 )
          continue;

        var key = line.Substring( 0, separator ).Trim();
        var value = line.Substring( separator + 1 ).Trim();
        if ( key.Equals( "target_scale", StringComparison.OrdinalIgnoreCase ) ||
             key.Equals( "scale", StringComparison.OrdinalIgnoreCase ) ) {
          if ( TryParseFloat( value, out var targetScale ) )
            request.TargetScale = targetScale;
        }
        else if ( key.Equals( "current_scale", StringComparison.OrdinalIgnoreCase ) ||
                  key.Equals( "from_scale", StringComparison.OrdinalIgnoreCase ) ) {
          if ( TryParseFloat( value, out var currentScale ) ) {
            request.CurrentScale = currentScale;
            request.HasCurrentScale = true;
          }
        }
        else if ( key.Equals( "pivot", StringComparison.OrdinalIgnoreCase ) ||
                  key.Equals( "pivot_world", StringComparison.OrdinalIgnoreCase ) ) {
          if ( TryParseVector3( value, out var pivot ) )
            request.PivotWorld = pivot;
        }
        else if ( key.Equals( "apply_environment_physics", StringComparison.OrdinalIgnoreCase ) ) {
          if ( bool.TryParse( value, out var applyEnvironmentPhysics ) )
            request.ApplyEnvironmentPhysics = applyEnvironmentPhysics;
        }
        else if ( key.Equals( "scale_cameras", StringComparison.OrdinalIgnoreCase ) ) {
          if ( bool.TryParse( value, out var scaleCameras ) )
            request.ScaleCameras = scaleCameras;
        }
        else if ( key.Equals( "scale_lights", StringComparison.OrdinalIgnoreCase ) ) {
          if ( bool.TryParse( value, out var scaleLights ) )
            request.ScaleLights = scaleLights;
        }
      }

      return request;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( new SceneEnvironmentScaleResult {
          success = false,
          message = $"Active scene '{activeScene.path}' has unsaved changes; scene scale utility did not switch scenes."
        } );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static string BackupSceneFile( Scene scene )
    {
      if ( scene.IsValid() )
        EditorSceneManager.SaveScene( scene );

      var sceneAbsolutePath = GetProjectRelativeAbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = GetProjectRelativeAbsolutePath( "CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_scene_environment_scale_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static float DetectCurrentEnvironmentScale( SceneEnvironmentScaleResult result )
    {
      var factoryFloor = FindSceneObject( "FactoryFloor" );
      if ( factoryFloor != null ) {
        var floorWidth = Mathf.Abs( factoryFloor.transform.lossyScale.x );
        if ( floorWidth > 0.001f ) {
          result.detected_scale_source = "FactoryFloor.lossyScale.x";
          return floorWidth / CodexSceneScaleConfig.BaseRoomSizeX;
        }
      }

      var digArea = FindSceneObject( "AGXUnity.RigidBody.DigArea" );
      var digAreaBox = digArea != null ? digArea.GetComponentInChildren<Box>( true ) : null;
      if ( digAreaBox != null && digAreaBox.HalfExtents.x > 0.001f ) {
        result.detected_scale_source = "DigArea.Box.HalfExtents.x";
        return ( digAreaBox.HalfExtents.x * 2.0f ) / CodexSceneScaleConfig.BaseAreaSizeX;
      }

      var submergedBox = FindSceneObject( "SubmergedBox" );
      var submergedAgxBox = submergedBox != null ? submergedBox.GetComponentInChildren<Box>( true ) : null;
      if ( submergedAgxBox != null && submergedAgxBox.HalfExtents.x > 0.001f ) {
        result.detected_scale_source = "SubmergedBox.Box.HalfExtents.x";
        return ( submergedAgxBox.HalfExtents.x * 2.0f ) / CodexSceneScaleConfig.BaseAreaSizeX;
      }

      result.detected_scale_source = "legacy-default";
      result.warnings.Add( "Could not infer environment scale from known scene geometry; assumed legacy 1.25x." );
      return CodexSceneScaleConfig.LegacyEnvironmentScale;
    }

    private static List<Transform> CollectSceneScaleRoots( Scene scene,
                                                           Transform excavatorRoot,
                                                           ScaleRequest request,
                                                           SceneEnvironmentScaleResult result )
    {
      var roots = new List<Transform>();
      foreach ( var rootObject in scene.GetRootGameObjects() )
        CollectSceneScaleRootsRecursive( rootObject.transform, excavatorRoot, request, roots );

      roots.Sort( ( left, right ) => string.Compare( GetHierarchyPath( left.gameObject ),
                                                     GetHierarchyPath( right.gameObject ),
                                                     StringComparison.Ordinal ) );
      foreach ( var root in roots ) {
        if ( result.sample_scale_roots.Count < 80 )
          result.sample_scale_roots.Add( GetHierarchyPath( root.gameObject ) );
      }

      return roots;
    }

    private static void CollectSceneScaleRootsRecursive( Transform transform,
                                                         Transform excavatorRoot,
                                                         ScaleRequest request,
                                                         List<Transform> roots )
    {
      if ( transform == null )
        return;

      if ( excavatorRoot != null && ( transform == excavatorRoot || transform.IsChildOf( excavatorRoot ) ) )
        return;

      if ( excavatorRoot != null && excavatorRoot.IsChildOf( transform ) ) {
        foreach ( Transform child in transform )
          CollectSceneScaleRootsRecursive( child, excavatorRoot, request, roots );
        return;
      }

      if ( !HasScalableSpatialContent( transform, request ) )
        return;

      if ( transform.GetComponent<Terrain>() == null && ContainsTerrain( transform ) ) {
        foreach ( Transform child in transform )
          CollectSceneScaleRootsRecursive( child, excavatorRoot, request, roots );
        return;
      }

      roots.Add( transform );
    }

    private static bool HasScalableSpatialContent( Transform transform, ScaleRequest request )
    {
      if ( transform.GetComponentInChildren<Renderer>( true ) != null )
        return true;
      if ( transform.GetComponentInChildren<Terrain>( true ) != null )
        return true;
      if ( transform.GetComponentInChildren<Collider>( true ) != null )
        return true;
      if ( transform.GetComponentInChildren<Shape>( true ) != null )
        return true;
      if ( transform.GetComponentInChildren<ParticleSystem>( true ) != null )
        return true;
      if ( request.ScaleCameras && transform.GetComponentInChildren<Camera>( true ) != null )
        return true;
      if ( request.ScaleLights && transform.GetComponentInChildren<Light>( true ) != null )
        return true;

      return false;
    }

    private static bool ContainsTerrain( Transform transform )
    {
      return transform.GetComponentInChildren<Terrain>( true ) != null;
    }

    private static void ScaleRootTransform( Transform root,
                                            float scaleRatio,
                                            Vector3 pivotWorld,
                                            HashSet<Transform> transformScaledShapes,
                                            SceneEnvironmentScaleResult result )
    {
      Undo.RecordObject( root, "Scale scene environment root" );
      root.position = pivotWorld + ( root.position - pivotWorld ) * scaleRatio;
      var rootLocalScaleChanged = ScaleTransformLocalScaleIfNeeded( root, scaleRatio, transformScaledShapes, result );
      EditorUtility.SetDirty( root );
      result.scaled_transform_roots++;

      foreach ( Transform child in root )
        ScaleDescendantTransform( child, scaleRatio, rootLocalScaleChanged, transformScaledShapes, result );
    }

    private static void ScaleDescendantTransform( Transform transform,
                                                  float scaleRatio,
                                                  bool ancestorLocalScaleChanged,
                                                  HashSet<Transform> transformScaledShapes,
                                                  SceneEnvironmentScaleResult result )
    {
      Undo.RecordObject( transform, "Scale scene environment transform" );
      if ( !ancestorLocalScaleChanged ) {
        transform.localPosition *= scaleRatio;
        result.scaled_transform_local_positions++;
      }

      var localScaleChanged = !ancestorLocalScaleChanged &&
                              ScaleTransformLocalScaleIfNeeded( transform, scaleRatio, transformScaledShapes, result );
      EditorUtility.SetDirty( transform );

      foreach ( Transform child in transform )
        ScaleDescendantTransform( child, scaleRatio, ancestorLocalScaleChanged || localScaleChanged, transformScaledShapes, result );
    }

    private static bool ScaleTransformLocalScaleIfNeeded( Transform transform,
                                                          float scaleRatio,
                                                          HashSet<Transform> transformScaledShapes,
                                                          SceneEnvironmentScaleResult result )
    {
      if ( !ShouldScaleTransformLocalScale( transform ) )
        return false;

      transform.localScale *= scaleRatio;
      transformScaledShapes.Add( transform );
      result.scaled_transform_local_scales++;
      return true;
    }

    private static bool ShouldScaleTransformLocalScale( Transform transform )
    {
      if ( transform == null || transform.GetComponent<Terrain>() != null )
        return false;

      if ( transform.GetComponent<AGXUnity.Collide.Mesh>() != null )
        return true;
      if ( transform.GetComponent<Renderer>() != null )
        return true;
      if ( transform.GetComponent<ParticleSystem>() != null )
        return true;

      var collider = transform.GetComponent<Collider>();
      return collider != null && !( collider is TerrainCollider );
    }

    private static void ScaleAgxShapes( Transform root,
                                        float scaleRatio,
                                        HashSet<Transform> transformScaledShapes,
                                        SceneEnvironmentScaleResult result )
    {
      foreach ( var shape in root.GetComponentsInChildren<Shape>( true ) ) {
        if ( HasTransformScaleApplied( shape.transform, transformScaledShapes ) ) {
          result.skipped_agx_shapes_with_transform_scale++;
          continue;
        }

        if ( ScaleShapeDimensions( shape, scaleRatio ) ) {
          EditorUtility.SetDirty( shape );
          result.scaled_agx_shapes++;
        }
      }
    }

    private static bool HasTransformScaleApplied( Transform transform, HashSet<Transform> transformScaledShapes )
    {
      var current = transform;
      while ( current != null ) {
        if ( transformScaledShapes.Contains( current ) )
          return true;

        current = current.parent;
      }

      return false;
    }

    private static bool ScaleShapeDimensions( Shape shape, float scaleRatio )
    {
      if ( shape == null )
        return false;

      Undo.RecordObject( shape, "Scale scene AGX shape" );
      if ( shape is Box box ) {
        box.HalfExtents *= scaleRatio;
        return true;
      }
      if ( shape is Sphere sphere ) {
        sphere.Radius *= scaleRatio;
        return true;
      }
      if ( shape is Cylinder cylinder ) {
        cylinder.Radius *= scaleRatio;
        cylinder.Height *= scaleRatio;
        return true;
      }
      if ( shape is Capsule capsule ) {
        capsule.Radius *= scaleRatio;
        capsule.Height *= scaleRatio;
        return true;
      }
      if ( shape is Cone cone ) {
        cone.TopRadius *= scaleRatio;
        cone.BottomRadius *= scaleRatio;
        cone.Height *= scaleRatio;
        return true;
      }
      if ( shape is HollowCylinder hollowCylinder ) {
        hollowCylinder.Thickness *= scaleRatio;
        hollowCylinder.Radius *= scaleRatio;
        hollowCylinder.Height *= scaleRatio;
        return true;
      }
      if ( shape is HollowCone hollowCone ) {
        hollowCone.TopRadius *= scaleRatio;
        hollowCone.BottomRadius *= scaleRatio;
        hollowCone.Thickness *= scaleRatio;
        hollowCone.Height *= scaleRatio;
        return true;
      }

      return false;
    }

    private static void ScaleAgxRigidBodies( Transform root, float scaleRatio, SceneEnvironmentScaleResult result )
    {
      var massScale = scaleRatio * scaleRatio * scaleRatio;
      var inertiaScale = massScale * scaleRatio * scaleRatio;
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        if ( rigidBody == null || rigidBody.MassProperties == null )
          continue;

        Undo.RecordObject( rigidBody, "Scale scene AGX rigid body" );
        var properties = rigidBody.MassProperties;
        properties.Mass.DefaultValue *= massScale;
        properties.Mass.UserValue *= massScale;
        properties.InertiaDiagonal.DefaultValue *= inertiaScale;
        properties.InertiaDiagonal.UserValue *= inertiaScale;
        properties.InertiaOffDiagonal.DefaultValue *= inertiaScale;
        properties.InertiaOffDiagonal.UserValue *= inertiaScale;
        properties.CenterOfMassOffset.DefaultValue *= scaleRatio;
        properties.CenterOfMassOffset.UserValue *= scaleRatio;
        EditorUtility.SetDirty( rigidBody );
        result.scaled_agx_rigid_bodies++;
      }
    }

    private static void ScaleAgxConstraints( Transform root, float scaleRatio, SceneEnvironmentScaleResult result )
    {
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
        if ( constraint == null || constraint.AttachmentPair == null )
          continue;

        Undo.RecordObject( constraint, "Scale scene AGX constraint" );
        constraint.AttachmentPair.ReferenceFrame.LocalPosition *= scaleRatio;
        constraint.AttachmentPair.ConnectedFrame.LocalPosition *= scaleRatio;
        result.scaled_agx_constraint_frames += 2;

        var isLinearPrimaryConstraint = constraint.Type == ConstraintType.Prismatic ||
                                        constraint.Type == ConstraintType.DistanceJoint;
        foreach ( var controller in constraint.GetElementaryConstraintControllers() ) {
          if ( controller == null )
            continue;

          var isTranslationalController = isLinearPrimaryConstraint ||
                                          controller.GetControllerType() == Constraint.ControllerType.Translational;
          if ( !isTranslationalController )
            continue;

          if ( controller is LockController lockController ) {
            lockController.Position = ScaleFinite( lockController.Position, scaleRatio );
            result.scaled_agx_constraint_controllers++;
          }
          else if ( controller is RangeController rangeController ) {
            rangeController.Range = new RangeReal( ScaleFinite( rangeController.Range.Min, scaleRatio ),
                                                   ScaleFinite( rangeController.Range.Max, scaleRatio ) );
            result.scaled_agx_constraint_controllers++;
          }
          else if ( controller is TargetSpeedController speedController ) {
            speedController.Speed = ScaleFinite( speedController.Speed, scaleRatio );
            result.scaled_agx_constraint_controllers++;
          }
        }

        EditorUtility.SetDirty( constraint );
      }
    }

    private static void ScaleTerrains( Transform root,
                                       float scaleRatio,
                                       HashSet<TerrainData> scaledTerrainData,
                                       SceneEnvironmentScaleResult result )
    {
      foreach ( var terrain in root.GetComponentsInChildren<Terrain>( true ) ) {
        if ( terrain == null )
          continue;

        var terrainData = terrain.terrainData;
        if ( terrainData != null && scaledTerrainData.Add( terrainData ) ) {
          Undo.RecordObject( terrainData, "Scale scene terrain data" );
          terrainData.size *= scaleRatio;
          EditorUtility.SetDirty( terrainData );
          result.scaled_terrain_data++;
        }

        var deformableTerrain = terrain.GetComponent<DeformableTerrain>();
        if ( deformableTerrain != null ) {
          Undo.RecordObject( deformableTerrain, "Scale scene deformable terrain" );
          var scaledMaximumDepth = deformableTerrain.MaximumDepth * scaleRatio;
          deformableTerrain.MaximumDepth = scaledMaximumDepth;

          var serializedObject = new SerializedObject( deformableTerrain );
          var maximumDepth = serializedObject.FindProperty( "m_maximumDepth" );
          if ( maximumDepth != null )
            maximumDepth.floatValue = scaledMaximumDepth;
          serializedObject.ApplyModifiedPropertiesWithoutUndo();

          EditorUtility.SetDirty( deformableTerrain );
          result.scaled_deformable_terrains++;
        }

        EditorUtility.SetDirty( terrain );
      }
    }

    private static void ScaleCamerasAndLights( Transform root,
                                               float scaleRatio,
                                               ScaleRequest request,
                                               SceneEnvironmentScaleResult result )
    {
      if ( request.ScaleCameras ) {
        foreach ( var camera in root.GetComponentsInChildren<Camera>( true ) ) {
          if ( camera == null )
            continue;

          Undo.RecordObject( camera, "Scale scene camera" );
          if ( camera.orthographic )
            camera.orthographicSize *= scaleRatio;
          camera.nearClipPlane = Mathf.Max( 0.001f, camera.nearClipPlane * scaleRatio );
          camera.farClipPlane = Mathf.Max( camera.nearClipPlane + 0.01f, camera.farClipPlane * scaleRatio );
          EditorUtility.SetDirty( camera );
          result.scaled_cameras++;
        }
      }

      if ( request.ScaleLights ) {
        foreach ( var light in root.GetComponentsInChildren<Light>( true ) ) {
          if ( light == null || light.type == LightType.Directional )
            continue;

          Undo.RecordObject( light, "Scale scene light" );
          light.range *= scaleRatio;
          EditorUtility.SetDirty( light );
          result.scaled_lights++;
        }
      }
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var preferredBobcat = FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
                            FindSceneObject( "Excavator_BobcatE85" );
      if ( preferredBobcat != null )
        return preferredBobcat;

      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        var type = component.GetType();
        var typeName = type.FullName ?? type.Name;
        if ( typeName != "AGXUnity.Excavator" && type.Name != "ExcavatorE85" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          return prefabRoot;

        var transform = component.transform;
        while ( transform.parent != null &&
                transform.parent.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          transform = transform.parent;

        return transform.gameObject;
      }

      return FindSceneObject( "Excavator CAT 365 Tracked" ) ??
             FindSceneObject( "Excavator" );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      foreach ( var transform in Resources.FindObjectsOfTypeAll<Transform>() ) {
        if ( transform == null || transform.name != objectName || !transform.gameObject.scene.IsValid() )
          continue;

        return transform.gameObject;
      }

      return null;
    }

    private static bool TryParseFloat( string value, out float result )
    {
      return float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out result );
    }

    private static bool TryParseVector3( string value, out Vector3 result )
    {
      result = Vector3.zero;
      var parts = value.Split( new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries );
      if ( parts.Length != 3 )
        return false;

      if ( !TryParseFloat( parts[ 0 ], out var x ) ||
           !TryParseFloat( parts[ 1 ], out var y ) ||
           !TryParseFloat( parts[ 2 ], out var z ) )
        return false;

      result = new Vector3( x, y, z );
      return true;
    }

    private static float ScaleFinite( float value, float scaleRatio )
    {
      return float.IsNaN( value ) || float.IsInfinity( value ) ? value : value * scaleRatio;
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
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( SceneEnvironmentScaleResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private sealed class ScaleRequest
    {
      public string Source;
      public float TargetScale;
      public float CurrentScale;
      public bool HasCurrentScale;
      public Vector3 PivotWorld;
      public bool ApplyEnvironmentPhysics;
      public bool ScaleCameras;
      public bool ScaleLights;

      public static ScaleRequest CreateDefault( string source )
      {
        return new ScaleRequest {
          Source = source,
          TargetScale = CodexSceneScaleConfig.DefaultEnvironmentScale,
          CurrentScale = CodexSceneScaleConfig.LegacyEnvironmentScale,
          HasCurrentScale = false,
          PivotWorld = Vector3.zero,
          ApplyEnvironmentPhysics = true,
          ScaleCameras = true,
          ScaleLights = true
        };
      }
    }

    [Serializable]
    private sealed class SceneEnvironmentScaleResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public string excavator_root;
      public string current_scale;
      public string target_scale;
      public string applied_scale_ratio;
      public string detected_scale_source;
      public string pivot_world;
      public bool apply_environment_physics;
      public int scale_root_count;
      public int scaled_transform_roots;
      public int scaled_transform_local_positions;
      public int scaled_transform_local_scales;
      public int scaled_agx_shapes;
      public int skipped_agx_shapes_with_transform_scale;
      public int scaled_agx_rigid_bodies;
      public int scaled_agx_constraint_frames;
      public int scaled_agx_constraint_controllers;
      public int scaled_terrain_data;
      public int scaled_deformable_terrains;
      public int scaled_cameras;
      public int scaled_lights;
      public List<string> sample_scale_roots = new List<string>();
      public List<string> warnings = new List<string>();
    }
  }
}
#endif

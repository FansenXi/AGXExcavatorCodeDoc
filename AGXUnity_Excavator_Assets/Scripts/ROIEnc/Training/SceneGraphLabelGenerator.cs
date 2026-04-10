using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Training
{
  public sealed class SceneGraphLabelGenerator : IRoiProvider
  {
    private const float MinNormalizedArea = 0.002f;
    private static readonly Vector3[] s_boundsCorners = new Vector3[8];
    private static readonly Vector3[] s_digAreaFootprintCorners = new Vector3[4];

    private readonly Component m_context;
    private ExcavatorMachineController m_machineController = null;
    private SwitchableTargetMassSensor m_targetMassSensor = null;
    private DigAreaMeasurement m_digAreaMeasurement = null;

    public SceneGraphLabelGenerator( Component context,
                                     ExcavatorMachineController machineController,
                                     SwitchableTargetMassSensor targetMassSensor,
                                     DigAreaMeasurement digAreaMeasurement )
    {
      m_context = context;
      m_machineController = machineController;
      m_targetMassSensor = targetMassSensor;
      m_digAreaMeasurement = digAreaMeasurement;
    }

    public bool TryGetRois( RoiFrameSample frameSample, List<RoiDescriptor> results, out string error )
    {
      error = string.Empty;
      if ( results == null ) {
        error = "scene_graph_label_results_missing";
        return false;
      }

      results.Clear();
      ResolveReferences();
      if ( frameSample == null || frameSample.sourceCamera == null ) {
        error = "scene_graph_label_camera_missing";
        return false;
      }

      var camera = frameSample.sourceCamera;
      var bucketReference = m_machineController != null ? m_machineController.BucketReference : null;
      if ( bucketReference != null ) {
        TryAddRendererHierarchyRoi( camera, bucketReference, RoiCategory.Bucket, frameSample.frameId, results );

        var armRoot = ResolveArmRoot( bucketReference );
        if ( armRoot != null )
          TryAddRendererHierarchyRoi( camera, armRoot, RoiCategory.ExcavatorArm, frameSample.frameId, results );
      }

      var currentTarget = m_targetMassSensor != null ? m_targetMassSensor.CurrentTarget : null;
      if ( currentTarget != null &&
           currentTarget.TryGetMeasurementVolume( out var targetFrame, out var targetCenterLocal, out var targetHalfExtents ) ) {
        var targetCategory =
          currentTarget is TruckBedMassSensor ||
          currentTarget.TargetName.ToLowerInvariant().Contains( "truck" ) ?
            RoiCategory.Truck :
            RoiCategory.Container;
        TryAddMeasurementVolumeRoi( camera,
                                    targetFrame,
                                    targetCenterLocal,
                                    targetHalfExtents,
                                    targetCategory,
                                    frameSample.frameId,
                                    results );
      }

      if ( m_digAreaMeasurement != null &&
           m_digAreaMeasurement.TryGetFootprintCornersWorld( s_digAreaFootprintCorners ) ) {
        TryAddWorldPolygonRoi( camera,
                               s_digAreaFootprintCorners,
                               RoiCategory.DigArea,
                               frameSample.frameId,
                               results,
                               "dig_area_footprint" );
      }

      return true;
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( m_context, m_machineController );
      m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( m_context, m_targetMassSensor );
      m_digAreaMeasurement = ExcavatorRigLocator.ResolveComponent( m_context, m_digAreaMeasurement );
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = DigAreaMeasurement.FindOrCreateInScene();
    }

    private static Transform ResolveArmRoot( Transform bucketReference )
    {
      if ( bucketReference == null )
        return null;

      var candidate = bucketReference.parent;
      if ( candidate == null )
        return bucketReference;

      if ( candidate.parent != null )
        candidate = candidate.parent;

      return candidate;
    }

    private void TryAddRendererHierarchyRoi( Camera camera,
                                             Transform hierarchyRoot,
                                             RoiCategory category,
                                             long frameId,
                                             List<RoiDescriptor> results )
    {
      if ( hierarchyRoot == null )
        return;

      if ( !HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( hierarchyRoot, out var localBounds ) )
        return;

      var rect = ProjectLocalBoundsToTopLeftRect( camera, hierarchyRoot, localBounds.center, localBounds.extents );
      if ( rect.width * rect.height < MinNormalizedArea )
        return;

      results.Add( new RoiDescriptor
      {
        Category = category,
        Confidence = 1.0f,
        FrameId = frameId,
        NormalizedRect = rect,
        Priority = 200,
        Source = RoiSource.SceneGraphLabel,
        DebugText = "scene_graph"
      } );
    }

    private void TryAddMeasurementVolumeRoi( Camera camera,
                                             Transform frame,
                                             Vector3 centerLocal,
                                             Vector3 halfExtents,
                                             RoiCategory category,
                                             long frameId,
                                             List<RoiDescriptor> results )
    {
      if ( frame == null || halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        return;

      var rect = ProjectLocalBoundsToTopLeftRect( camera, frame, centerLocal, halfExtents );
      if ( rect.width * rect.height < MinNormalizedArea )
        return;

      results.Add( new RoiDescriptor
      {
        Category = category,
        Confidence = 1.0f,
        FrameId = frameId,
        NormalizedRect = rect,
        Priority = 180,
        Source = RoiSource.SceneGraphLabel,
        DebugText = "measurement_volume"
      } );
    }

    private void TryAddWorldPolygonRoi( Camera camera,
                                        IReadOnlyList<Vector3> worldCorners,
                                        RoiCategory category,
                                        long frameId,
                                        List<RoiDescriptor> results,
                                        string debugText )
    {
      if ( worldCorners == null || worldCorners.Count == 0 )
        return;

      var rect = ProjectWorldCornersToTopLeftRect( camera, worldCorners );
      if ( rect.width * rect.height < MinNormalizedArea )
        return;

      results.Add( new RoiDescriptor
      {
        Category = category,
        Confidence = 1.0f,
        FrameId = frameId,
        NormalizedRect = rect,
        Priority = 180,
        Source = RoiSource.SceneGraphLabel,
        DebugText = debugText ?? "world_polygon"
      } );
    }

    private static Rect ProjectLocalBoundsToTopLeftRect( Camera camera,
                                                         Transform frame,
                                                         Vector3 centerLocal,
                                                         Vector3 halfExtents )
    {
      FillBoundsCorners( centerLocal, halfExtents, s_boundsCorners );

      var validCornerCount = 0;
      var minX = float.PositiveInfinity;
      var minY = float.PositiveInfinity;
      var maxX = float.NegativeInfinity;
      var maxY = float.NegativeInfinity;
      for ( var cornerIndex = 0; cornerIndex < s_boundsCorners.Length; ++cornerIndex ) {
        var worldCorner = frame.TransformPoint( s_boundsCorners[ cornerIndex ] );
        var viewport = camera.WorldToViewportPoint( worldCorner );
        if ( viewport.z <= 0.0f )
          continue;

        validCornerCount += 1;
        minX = Mathf.Min( minX, viewport.x );
        minY = Mathf.Min( minY, viewport.y );
        maxX = Mathf.Max( maxX, viewport.x );
        maxY = Mathf.Max( maxY, viewport.y );
      }

      if ( validCornerCount == 0 )
        return default;

      return RoiMathUtility.ClampNormalizedRectTopLeft( new Rect(
        minX,
        1.0f - maxY,
        maxX - minX,
        maxY - minY ) );
    }

    private static Rect ProjectWorldCornersToTopLeftRect( Camera camera, IReadOnlyList<Vector3> worldCorners )
    {
      if ( camera == null || worldCorners == null || worldCorners.Count == 0 )
        return default;

      var validCornerCount = 0;
      var minX = float.PositiveInfinity;
      var minY = float.PositiveInfinity;
      var maxX = float.NegativeInfinity;
      var maxY = float.NegativeInfinity;
      for ( var cornerIndex = 0; cornerIndex < worldCorners.Count; ++cornerIndex ) {
        var viewport = camera.WorldToViewportPoint( worldCorners[ cornerIndex ] );
        if ( viewport.z <= 0.0f )
          continue;

        validCornerCount += 1;
        minX = Mathf.Min( minX, viewport.x );
        minY = Mathf.Min( minY, viewport.y );
        maxX = Mathf.Max( maxX, viewport.x );
        maxY = Mathf.Max( maxY, viewport.y );
      }

      if ( validCornerCount == 0 )
        return default;

      return RoiMathUtility.ClampNormalizedRectTopLeft( new Rect(
        minX,
        1.0f - maxY,
        maxX - minX,
        maxY - minY ) );
    }

    private static void FillBoundsCorners( Vector3 center, Vector3 extents, Vector3[] corners )
    {
      var min = center - extents;
      var max = center + extents;

      corners[ 0 ] = new Vector3( min.x, min.y, min.z );
      corners[ 1 ] = new Vector3( min.x, min.y, max.z );
      corners[ 2 ] = new Vector3( min.x, max.y, min.z );
      corners[ 3 ] = new Vector3( min.x, max.y, max.z );
      corners[ 4 ] = new Vector3( max.x, min.y, min.z );
      corners[ 5 ] = new Vector3( max.x, min.y, max.z );
      corners[ 6 ] = new Vector3( max.x, max.y, min.z );
      corners[ 7 ] = new Vector3( max.x, max.y, max.z );
    }
  }
}

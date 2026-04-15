using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary
{
  public sealed class RuleRoiProvider : IRoiProvider
  {
    private static readonly Vector3[] s_digAreaCorners = new Vector3[ 4 ];

    private readonly Component m_context;
    private DigAreaMeasurement m_digAreaMeasurement;
    private RoiEncConfiguration.RuleRoiOptions m_options;

    public RuleRoiProvider( Component context,
                            DigAreaMeasurement digAreaMeasurement,
                            RoiEncConfiguration.RuleRoiOptions options )
    {
      m_context = context;
      m_digAreaMeasurement = digAreaMeasurement;
      m_options = options ?? new RoiEncConfiguration.RuleRoiOptions();
    }

    public bool TryGetRois( RoiFrameSample frameSample, List<RoiDescriptor> results, out string error )
    {
      error = string.Empty;
      if ( results == null ) {
        error = "rule_roi_results_missing";
        return false;
      }

      results.Clear();
      ResolveReferences();
      if ( frameSample == null || frameSample.sourceCamera == null ) {
        error = "rule_roi_camera_missing";
        return false;
      }

      if ( !m_options.EnableDigAreaFootprint || m_digAreaMeasurement == null )
        return true;

      if ( !m_digAreaMeasurement.TryGetFootprintCornersWorld( s_digAreaCorners ) )
        return true;

      var rect = ProjectWorldCornersToTopLeftRect( frameSample.sourceCamera, s_digAreaCorners );
      if ( rect.width * rect.height < Mathf.Max( 0.0f, m_options.MinNormalizedArea ) )
        return true;

      results.Add( new RoiDescriptor
      {
        Category = RoiCategory.DigArea,
        Confidence = Mathf.Clamp01( m_options.DigAreaConfidence ),
        FrameId = frameSample.frameId,
        NormalizedRect = rect,
        Priority = 160,
        Source = RoiSource.RuleRoi,
        DebugText = "rule_dig_area"
      } );
      return true;
    }

    public void UpdateOptions( RoiEncConfiguration.RuleRoiOptions options )
    {
      m_options = options ?? new RoiEncConfiguration.RuleRoiOptions();
    }

    private void ResolveReferences()
    {
      m_digAreaMeasurement = ExcavatorRigLocator.ResolveComponent( m_context, m_digAreaMeasurement );
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = DigAreaMeasurement.FindOrCreateInScene();
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
  }
}

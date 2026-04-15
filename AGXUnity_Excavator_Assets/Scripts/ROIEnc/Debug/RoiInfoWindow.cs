using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  public class RoiInfoWindow : MonoBehaviour
  {
    [SerializeField]
    private bool m_visible = true;

    [SerializeField]
    private Rect m_windowRect = new Rect( 16.0f, 420.0f, 360.0f, 260.0f );

    private readonly List<RoiDescriptor> m_rois = new List<RoiDescriptor>();

    private long   m_frameId         = -1;
    private long   m_stepId          = -1;
    private float  m_inferenceMs     = 0.0f;
    private float  m_motionIntensity = 0.0f;
    private string m_statusText      = "idle";
    private readonly RoiLiveEvaluation m_liveEvaluation = new RoiLiveEvaluation();

    private GUIStyle m_labelStyle;
    private Vector2  m_scrollPos;

    public bool IsVisible
    {
      get => m_visible;
      set => m_visible = value;
    }

    public void UpdateInfo( long frameId,
                            long stepId,
                            IReadOnlyList<RoiDescriptor> rois,
                            float inferenceMs,
                            float motionIntensity,
                            string statusText,
                            RoiLiveEvaluation liveEvaluation )
    {
      m_frameId         = frameId;
      m_stepId          = stepId;
      m_inferenceMs     = inferenceMs;
      m_motionIntensity = motionIntensity;
      m_statusText      = string.IsNullOrWhiteSpace( statusText ) ? "ok" : statusText;

      m_rois.Clear();
      if ( rois != null ) {
        foreach ( var roi in rois ) {
          if ( roi != null )
            m_rois.Add( roi.Clone() );
        }
      }

      m_liveEvaluation.CopyFrom( liveEvaluation );
    }

    private void OnGUI()
    {
      if ( !m_visible )
        return;

      EnsureStyles();
      m_windowRect = GUI.Window( GetInstanceID(), m_windowRect, DrawWindow, "ROI Detection" );
    }

    private void DrawWindow( int windowId )
    {
      m_scrollPos = GUILayout.BeginScrollView( m_scrollPos );

      GUILayout.Label( $"Status: {m_statusText}", m_labelStyle );
      GUILayout.Label( $"Infer: {m_inferenceMs:F2} ms", m_labelStyle );
      GUILayout.Label( $"Frame: {m_frameId}  Step: {m_stepId}", m_labelStyle );
      GUILayout.Label( $"Motion: {m_motionIntensity:F3}", m_labelStyle );
      GUILayout.Label( $"ROI count: {m_rois.Count}", m_labelStyle );
      GUILayout.Label( $"Native: raw={m_liveEvaluation.RawCandidateCount} filter={m_liveEvaluation.ThresholdKeptCount} " +
                       $"nms={m_liveEvaluation.NmsKeptCount} final={m_liveEvaluation.FinalDetectionCount} " +
                       $"post={m_liveEvaluation.PostprocessMs:F2} ms unknown={m_liveEvaluation.UnknownCount}",
                       m_labelStyle );

      if ( m_liveEvaluation.ClassEvaluations.Count > 0 ) {
        GUILayout.Space( 4.0f );
        GUILayout.Label( "Per-class live match:", m_labelStyle );
        foreach ( var classEvaluation in m_liveEvaluation.ClassEvaluations ) {
          GUILayout.Label(
            $"{classEvaluation.Label}: pred={classEvaluation.PredictedCount} gt={classEvaluation.GroundTruthCount} " +
            $"match={classEvaluation.MatchedCount} miss={classEvaluation.MissedCount} fp={classEvaluation.FalsePositiveCount}",
            m_labelStyle );
        }
      }

      if ( m_rois.Count > 0 ) {
        GUILayout.Space( 4.0f );
        foreach ( var roi in m_rois ) {
          var r = roi.NormalizedRect;
          var color = RoiCategoryUtility.ColorFor( roi.Category );
          var hex   = ColorUtility.ToHtmlStringRGB( color );
          GUILayout.Label(
            $"<color=#{hex}>{roi.Label}</color> [{roi.Source}] " +
            $"conf={roi.Confidence:F2} " +
            $"rect=({r.x:F2},{r.y:F2},{r.width:F2},{r.height:F2})",
            m_labelStyle );
        }
      }

      GUILayout.EndScrollView();
      GUI.DragWindow();
    }

    private void EnsureStyles()
    {
      if ( m_labelStyle != null )
        return;

      m_labelStyle = new GUIStyle( GUI.skin.label )
      {
        richText  = true,
        wordWrap  = false,
        fontSize  = 12,
        normal    = { textColor = Color.white }
      };
    }
  }
}

using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  public class RoiOverlayVisualizer : MonoBehaviour
  {
    private readonly List<RoiDescriptor> m_rois = new List<RoiDescriptor>();

    private TrackedCameraWindow m_cameraWindow = null;
    private Rect m_fallbackOverlayRect = new Rect( 16.0f, 660.0f, 520.0f, 180.0f );
    private bool m_enabled = true;
    private bool m_drawLabels = true;
    private bool m_drawDebugPanel = true;
    private long m_frameId = -1;
    private long m_stepId = -1;
    private float m_motionIntensity = 0.0f;
    private string m_statusText = "idle";
    private GUIStyle m_panelStyle = null;
    private GUIStyle m_labelStyle = null;

    public void Configure( TrackedCameraWindow cameraWindow, RoiEncConfiguration.OverlayOptions options )
    {
      m_cameraWindow = cameraWindow;
      options = options ?? new RoiEncConfiguration.OverlayOptions();
      m_enabled = options.Enabled;
      m_drawLabels = options.DrawLabels;
      m_drawDebugPanel = options.DrawDebugPanel;
      m_fallbackOverlayRect = options.FallbackOverlayRect;
    }

    public void UpdateOverlay( long frameId,
                               long stepId,
                               IReadOnlyList<RoiDescriptor> rois,
                               float motionIntensity,
                               string statusText )
    {
      m_frameId = frameId;
      m_stepId = stepId;
      m_motionIntensity = motionIntensity;
      m_statusText = string.IsNullOrWhiteSpace( statusText ) ? "ok" : statusText;

      m_rois.Clear();
      if ( rois == null )
        return;

      foreach ( var roi in rois ) {
        if ( roi != null )
          m_rois.Add( roi.Clone() );
      }
    }

    private void OnGUI()
    {
      if ( !m_enabled )
        return;

      EnsureStyles();
      var contentRect = ResolveContentRect();
      if ( contentRect.width > 0.0f && contentRect.height > 0.0f )
        DrawRoiBoxes( contentRect );

      if ( m_drawDebugPanel )
        DrawDebugPanel();
    }

    private void EnsureStyles()
    {
      if ( m_panelStyle == null ) {
        m_panelStyle = new GUIStyle( GUI.skin.box )
        {
          alignment = TextAnchor.UpperLeft,
          richText = true,
          wordWrap = true
        };
      }

      if ( m_labelStyle == null ) {
        m_labelStyle = new GUIStyle( GUI.skin.label )
        {
          alignment = TextAnchor.UpperLeft,
          richText = true,
          normal = { textColor = Color.white }
        };
      }
    }

    private Rect ResolveContentRect()
    {
      if ( m_cameraWindow == null || !m_cameraWindow.IsVisible )
        return default;

      var windowRect = m_cameraWindow.WindowRect;
      return new Rect(
        windowRect.x + 8.0f,
        windowRect.y + 24.0f,
        Mathf.Max( 32.0f, windowRect.width - 16.0f ),
        Mathf.Max( 32.0f, windowRect.height - 32.0f ) );
    }

    private void DrawRoiBoxes( Rect contentRect )
    {
      foreach ( var roi in m_rois ) {
        if ( roi == null || !roi.IsValid )
          continue;

        var pixelRect = new Rect(
          contentRect.x + roi.NormalizedRect.x * contentRect.width,
          contentRect.y + roi.NormalizedRect.y * contentRect.height,
          roi.NormalizedRect.width * contentRect.width,
          roi.NormalizedRect.height * contentRect.height );
        DrawBoxOutline( pixelRect, RoiCategoryUtility.ColorFor( roi.Category ), 2.0f );

        if ( !m_drawLabels )
          continue;

        GUI.Label(
          new Rect( pixelRect.x + 2.0f, pixelRect.y - 18.0f, Mathf.Max( 120.0f, pixelRect.width ), 18.0f ),
          $"{roi.Label} {roi.Confidence:0.00}",
          m_labelStyle );
      }
    }

    private void DrawDebugPanel()
    {
      var panelRect = ResolveDebugPanelRect();
      GUILayout.BeginArea( panelRect, GUI.skin.box );
      GUILayout.Label( "<b>ROI Overlay</b>", m_labelStyle );
      GUILayout.Label( $"Status: {m_statusText}", m_labelStyle );
      GUILayout.Label( $"Frame: {m_frameId}    Step: {m_stepId}", m_labelStyle );
      GUILayout.Label( $"Motion intensity: {m_motionIntensity:0.000}", m_labelStyle );
      GUILayout.Label( $"ROI count: {m_rois.Count}", m_labelStyle );
      foreach ( var roi in m_rois )
        GUILayout.Label( $"{roi.Label} [{roi.Source}] conf={roi.Confidence:0.00} rect=({roi.NormalizedRect.x:0.00},{roi.NormalizedRect.y:0.00},{roi.NormalizedRect.width:0.00},{roi.NormalizedRect.height:0.00})", m_labelStyle );
      GUILayout.EndArea();
    }

    private Rect ResolveDebugPanelRect()
    {
      var width = Mathf.Min( m_fallbackOverlayRect.width, Mathf.Max( 240.0f, Screen.width - 16.0f ) );
      var height = Mathf.Min( m_fallbackOverlayRect.height, Mathf.Max( 120.0f, Screen.height - 16.0f ) );
      var maxX = Mathf.Max( 8.0f, Screen.width - width - 8.0f );
      var maxY = Mathf.Max( 8.0f, Screen.height - height - 8.0f );

      return new Rect(
        Mathf.Clamp( m_fallbackOverlayRect.x, 8.0f, maxX ),
        Mathf.Clamp( m_fallbackOverlayRect.y, 8.0f, maxY ),
        width,
        height );
    }

    private static void DrawBoxOutline( Rect rect, Color color, float thickness )
    {
      var previousColor = GUI.color;
      GUI.color = color;

      GUI.DrawTexture( new Rect( rect.xMin, rect.yMin, rect.width, thickness ), Texture2D.whiteTexture );
      GUI.DrawTexture( new Rect( rect.xMin, rect.yMax - thickness, rect.width, thickness ), Texture2D.whiteTexture );
      GUI.DrawTexture( new Rect( rect.xMin, rect.yMin, thickness, rect.height ), Texture2D.whiteTexture );
      GUI.DrawTexture( new Rect( rect.xMax - thickness, rect.yMin, thickness, rect.height ), Texture2D.whiteTexture );

      GUI.color = previousColor;
    }
  }
}

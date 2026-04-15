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
    private bool m_enabled = true;
    private GUIStyle m_labelStyle = null;
    private Texture2D m_whiteTexture = null;

    public void Configure( TrackedCameraWindow cameraWindow, RoiEncConfiguration.OverlayOptions options )
    {
      m_cameraWindow = cameraWindow;
      options = options ?? new RoiEncConfiguration.OverlayOptions();
      m_enabled = options.Enabled;
    }

    public void UpdateOverlay( long frameId,
                               long stepId,
                               IReadOnlyList<RoiDescriptor> rois,
                               float motionIntensity,
                               string statusText )
    {
      m_rois.Clear();
      if ( rois == null )
        return;

      foreach ( var roi in rois ) {
        if ( roi != null && roi.IsValid )
          m_rois.Add( roi.Clone() );
      }
    }

    private void OnGUI()
    {
      if ( !m_enabled || m_cameraWindow == null || !m_cameraWindow.IsVisible || m_rois.Count == 0 )
        return;

      EnsureStyles();
      var imageRect = ResolveImageRect();
      if ( imageRect.width <= 1.0f || imageRect.height <= 1.0f )
        return;

      foreach ( var roi in m_rois ) {
        DrawRoi( imageRect, roi );
      }
    }

    private Rect ResolveImageRect()
    {
      var windowRect = m_cameraWindow.WindowRect;
      var contentRect = new Rect(
        windowRect.x + 8.0f,
        windowRect.y + 24.0f,
        Mathf.Max( 32.0f, windowRect.width - 16.0f ),
        Mathf.Max( 32.0f, windowRect.height - 32.0f ) );

      var textureWidth = Mathf.Max( 1, m_cameraWindow.TextureWidth );
      var textureHeight = Mathf.Max( 1, m_cameraWindow.TextureHeight );
      var scale = Mathf.Min( contentRect.width / textureWidth, contentRect.height / textureHeight );
      var fittedWidth = textureWidth * scale;
      var fittedHeight = textureHeight * scale;
      var offsetX = contentRect.x + 0.5f * ( contentRect.width - fittedWidth );
      var offsetY = contentRect.y + 0.5f * ( contentRect.height - fittedHeight );
      return new Rect( offsetX, offsetY, fittedWidth, fittedHeight );
    }

    private void DrawRoi( Rect imageRect, RoiDescriptor roi )
    {
      var normalized = roi.NormalizedRect;
      var rect = new Rect(
        imageRect.x + normalized.x * imageRect.width,
        imageRect.y + normalized.y * imageRect.height,
        normalized.width * imageRect.width,
        normalized.height * imageRect.height );

      var color = RoiCategoryUtility.ColorFor( roi.Category );
      var borderThickness = roi.Source == RoiSource.RuleRoi ? 3.0f : 2.0f;
      DrawRectOutline( rect, color, borderThickness );

      var sourceTag = roi.Source == RoiSource.RuleRoi ? "Rule" : "Visual";
      var label = $"{roi.Label} [{sourceTag}] {roi.Confidence:F2}";
      DrawLabel( rect, label, color );
    }

    private void DrawRectOutline( Rect rect, Color color, float thickness )
    {
      DrawFilledRect( new Rect( rect.xMin, rect.yMin, rect.width, thickness ), color );
      DrawFilledRect( new Rect( rect.xMin, rect.yMax - thickness, rect.width, thickness ), color );
      DrawFilledRect( new Rect( rect.xMin, rect.yMin, thickness, rect.height ), color );
      DrawFilledRect( new Rect( rect.xMax - thickness, rect.yMin, thickness, rect.height ), color );
    }

    private void DrawLabel( Rect rect, string text, Color color )
    {
      var size = m_labelStyle.CalcSize( new GUIContent( text ) );
      var labelRect = new Rect(
        rect.xMin,
        Mathf.Max( 0.0f, rect.yMin - size.y - 4.0f ),
        size.x + 8.0f,
        size.y + 4.0f );

      DrawFilledRect( labelRect, color );
      GUI.Label( new Rect( labelRect.x + 4.0f, labelRect.y + 2.0f, size.x, size.y ), text, m_labelStyle );
    }

    private void DrawFilledRect( Rect rect, Color color )
    {
      var previous = GUI.color;
      GUI.color = color;
      GUI.DrawTexture( rect, m_whiteTexture );
      GUI.color = previous;
    }

    private void EnsureStyles()
    {
      if ( m_whiteTexture == null ) {
        m_whiteTexture = new Texture2D( 1, 1, TextureFormat.RGBA32, false )
        {
          hideFlags = HideFlags.HideAndDontSave
        };
        m_whiteTexture.SetPixel( 0, 0, Color.white );
        m_whiteTexture.Apply( false, true );
      }

      if ( m_labelStyle != null )
        return;

      m_labelStyle = new GUIStyle( GUI.skin.label )
      {
        fontSize = 11,
        richText = false,
        wordWrap = false,
        normal = { textColor = Color.black }
      };
    }

    private void OnDestroy()
    {
      if ( m_whiteTexture != null ) {
        Destroy( m_whiteTexture );
        m_whiteTexture = null;
      }
    }
  }
}

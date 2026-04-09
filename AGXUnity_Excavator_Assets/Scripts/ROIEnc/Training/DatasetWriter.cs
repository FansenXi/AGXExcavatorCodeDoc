using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Training
{
  public sealed class DatasetWriter : System.IDisposable
  {
    private Texture2D m_inputTexture = null;
    private Texture2D m_outputTexture = null;
    private RenderTexture m_resizeTexture = null;

    public int CurrentEpisodeIndex { get; private set; } = 0;

    public void AdvanceEpisode()
    {
      CurrentEpisodeIndex += 1;
    }

    public bool WriteSample( RoiFrameSample frameSample,
                             IReadOnlyList<RoiDescriptor> labels,
                             RoiEncConfiguration.DatasetOptions options,
                             out string imagePath,
                             out string labelPath,
                             out string error )
    {
      imagePath = string.Empty;
      labelPath = string.Empty;
      error = string.Empty;

      if ( frameSample == null || frameSample.rgb24 == null || frameSample.rgb24.Length == 0 || frameSample.width <= 0 || frameSample.height <= 0 ) {
        error = "dataset_writer_rgb24_sample_missing";
        return false;
      }

      options = options ?? new RoiEncConfiguration.DatasetOptions();
      var rootDirectory = RoiPathUtility.ResolveOutputDirectory( options.RootDirectory );
      var splitName = IsValidationEpisode( CurrentEpisodeIndex, options.ValidationSplit ) ? "val" : "train";
      var imagesDirectory = Path.Combine( rootDirectory, "images", splitName );
      var labelsDirectory = Path.Combine( rootDirectory, "labels", splitName );
      Directory.CreateDirectory( imagesDirectory );
      Directory.CreateDirectory( labelsDirectory );

      var fileStem = string.Format(
        CultureInfo.InvariantCulture,
        "episode_{0:0000}_step_{1:000000}_frame_{2:000000}",
        CurrentEpisodeIndex,
        System.Math.Max( 0L, frameSample.stepId ),
        System.Math.Max( 0L, frameSample.frameId ) );
      imagePath = Path.Combine( imagesDirectory, $"{fileStem}.jpg" );
      labelPath = Path.Combine( labelsDirectory, $"{fileStem}.txt" );

      if ( options.EnableImageExport ) {
        var jpegBytes = EncodeJpeg( frameSample.rgb24,
                                    frameSample.width,
                                    frameSample.height,
                                    Mathf.Max( 64, options.ExportWidth ),
                                    Mathf.Max( 64, options.ExportHeight ),
                                    Mathf.Clamp( options.JpegQuality, 1, 100 ) );
        File.WriteAllBytes( imagePath, jpegBytes );
      }

      File.WriteAllLines( labelPath, BuildYoloLines( labels ), new UTF8Encoding( false ) );
      return true;
    }

    public void Dispose()
    {
      if ( m_inputTexture != null ) {
        Object.Destroy( m_inputTexture );
        m_inputTexture = null;
      }

      if ( m_outputTexture != null ) {
        Object.Destroy( m_outputTexture );
        m_outputTexture = null;
      }

      if ( m_resizeTexture != null ) {
        if ( m_resizeTexture.IsCreated() )
          m_resizeTexture.Release();
        Object.Destroy( m_resizeTexture );
        m_resizeTexture = null;
      }
    }

    private byte[] EncodeJpeg( byte[] topDownRgb24,
                               int sourceWidth,
                               int sourceHeight,
                               int targetWidth,
                               int targetHeight,
                               int jpegQuality )
    {
      EnsureTextures( sourceWidth, sourceHeight, targetWidth, targetHeight );

      var pixels = new Color32[ sourceWidth * sourceHeight ];
      for ( var y = 0; y < sourceHeight; ++y ) {
        var sourceRow = y * sourceWidth * 3;
        var destinationRow = ( sourceHeight - 1 - y ) * sourceWidth;
        for ( var x = 0; x < sourceWidth; ++x ) {
          var sourceIndex = sourceRow + x * 3;
          pixels[ destinationRow + x ] = new Color32(
            topDownRgb24[ sourceIndex + 0 ],
            topDownRgb24[ sourceIndex + 1 ],
            topDownRgb24[ sourceIndex + 2 ],
            255 );
        }
      }

      m_inputTexture.SetPixels32( pixels );
      m_inputTexture.Apply( false, false );

      Graphics.Blit( m_inputTexture, m_resizeTexture );
      var previousActive = RenderTexture.active;
      RenderTexture.active = m_resizeTexture;
      m_outputTexture.ReadPixels( new Rect( 0.0f, 0.0f, targetWidth, targetHeight ), 0, 0, false );
      m_outputTexture.Apply( false, false );
      RenderTexture.active = previousActive;

      return m_outputTexture.EncodeToJPG( jpegQuality );
    }

    private void EnsureTextures( int sourceWidth, int sourceHeight, int targetWidth, int targetHeight )
    {
      if ( m_inputTexture == null || m_inputTexture.width != sourceWidth || m_inputTexture.height != sourceHeight ) {
        if ( m_inputTexture != null )
          Object.Destroy( m_inputTexture );

        m_inputTexture = new Texture2D( sourceWidth, sourceHeight, TextureFormat.RGB24, false, false );
      }

      if ( m_resizeTexture == null || m_resizeTexture.width != targetWidth || m_resizeTexture.height != targetHeight ) {
        if ( m_resizeTexture != null ) {
          if ( m_resizeTexture.IsCreated() )
            m_resizeTexture.Release();
          Object.Destroy( m_resizeTexture );
        }

        m_resizeTexture = new RenderTexture( targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32 );
        m_resizeTexture.Create();
      }

      if ( m_outputTexture == null || m_outputTexture.width != targetWidth || m_outputTexture.height != targetHeight ) {
        if ( m_outputTexture != null )
          Object.Destroy( m_outputTexture );

        m_outputTexture = new Texture2D( targetWidth, targetHeight, TextureFormat.RGB24, false, false );
      }
    }

    private static bool IsValidationEpisode( int episodeIndex, float validationSplit )
    {
      var validationBucketCount = Mathf.Clamp( Mathf.RoundToInt( validationSplit * 100.0f ), 0, 99 );
      if ( validationBucketCount <= 0 )
        return false;

      var hash = Mathf.Abs( episodeIndex.GetHashCode() ) % 100;
      return hash < validationBucketCount;
    }

    private static IEnumerable<string> BuildYoloLines( IReadOnlyList<RoiDescriptor> labels )
    {
      if ( labels == null || labels.Count == 0 )
        yield break;

      foreach ( var label in labels ) {
        if ( label == null || !label.IsValid )
          continue;

        var classId = Mathf.Max( 0, (int)label.Category - 1 );
        var rect = RoiMathUtility.ClampNormalizedRectTopLeft( label.NormalizedRect );
        var centerX = rect.x + 0.5f * rect.width;
        var centerY = rect.y + 0.5f * rect.height;
        yield return string.Format(
          CultureInfo.InvariantCulture,
          "{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######}",
          classId,
          centerX,
          centerY,
          rect.width,
          rect.height );
      }
    }
  }
}

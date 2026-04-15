using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Training
{
  public sealed class DatasetWriter : System.IDisposable
  {
    [Serializable]
    private sealed class EpisodeManifest
    {
      public int episode_index = 0;
      public int n_frames = 0;
      public string timestamp = string.Empty;
      public string capture_mode = string.Empty;
      public int step_interval = 0;
      public int export_width = 0;
      public int export_height = 0;
      public float validation_split = 0.0f;
      public bool enable_image_export = true;
      public int jpeg_quality = 90;
      public string[] class_labels = Array.Empty<string>();
      public string[] image_files = Array.Empty<string>();
      public string[] label_files = Array.Empty<string>();
    }

    private Texture2D m_inputTexture = null;
    private Texture2D m_outputTexture = null;
    private RenderTexture m_resizeTexture = null;
    private readonly List<string> m_currentEpisodeImagePaths = new List<string>();
    private readonly List<string> m_currentEpisodeLabelPaths = new List<string>();
    private long m_currentEpisodeFirstCaptureTimeNs = -1;
    private long m_currentEpisodeLastCaptureTimeNs = -1;
    private bool m_hasInitializedEpisodeIndex = false;
    private string m_initializedRootDirectory = string.Empty;

    private static readonly Regex EpisodeManifestPattern = new Regex( @"^episode_(\d+)_manifest\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
    private static readonly Regex EpisodeSamplePattern = new Regex( @"^episode_(\d+)_step_\d+_frame_\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase );

    public int CurrentEpisodeIndex { get; private set; } = 0;

    public void AdvanceEpisode( RoiEncConfiguration.DatasetOptions options = null )
    {
      EnsureEpisodeIndexInitialized( options );
      ResetEpisodeTracking();
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
      TrackWrittenSample( rootDirectory, imagePath, labelPath, frameSample.captureTimeNs, options.EnableImageExport );
      return true;
    }

    public bool TryWriteEpisodeManifest( RoiEncConfiguration.DatasetOptions options,
                                         string captureMode,
                                         string[] classLabels,
                                         out string manifestPath,
                                         out string error )
    {
      manifestPath = string.Empty;
      error = string.Empty;

      if ( m_currentEpisodeImagePaths.Count == 0 && m_currentEpisodeLabelPaths.Count == 0 )
        return true;

      options ??= new RoiEncConfiguration.DatasetOptions();
      var rootDirectory = RoiPathUtility.ResolveOutputDirectory( options.RootDirectory );
      var manifestDirectory = Path.Combine( rootDirectory, "manifests" );
      Directory.CreateDirectory( manifestDirectory );

      manifestPath = Path.Combine( manifestDirectory, $"episode_{CurrentEpisodeIndex:0000}_manifest.json" );
      var manifest = new EpisodeManifest
      {
        episode_index = CurrentEpisodeIndex,
        n_frames = Mathf.Max( m_currentEpisodeImagePaths.Count, m_currentEpisodeLabelPaths.Count ),
        timestamp = CaptureTimeNsToIsoString( m_currentEpisodeFirstCaptureTimeNs ),
        capture_mode = captureMode ?? string.Empty,
        step_interval = Mathf.Max( 1, options.AutomaticCaptureEverySteps ),
        export_width = Mathf.Max( 1, options.ExportWidth ),
        export_height = Mathf.Max( 1, options.ExportHeight ),
        validation_split = Mathf.Clamp01( options.ValidationSplit ),
        enable_image_export = options.EnableImageExport,
        jpeg_quality = Mathf.Clamp( options.JpegQuality, 1, 100 ),
        class_labels = classLabels ?? Array.Empty<string>(),
        image_files = m_currentEpisodeImagePaths.ToArray(),
        label_files = m_currentEpisodeLabelPaths.ToArray()
      };

      try {
        using var writer = new StreamWriter( manifestPath, false, new UTF8Encoding( false ) );
        writer.Write( JsonUtility.ToJson( manifest, true ) );
        return true;
      }
      catch ( Exception exception ) {
        error = exception.Message;
        return false;
      }
    }

    public void Dispose()
    {
      if ( m_inputTexture != null ) {
        UnityEngine.Object.Destroy( m_inputTexture );
        m_inputTexture = null;
      }

      if ( m_outputTexture != null ) {
        UnityEngine.Object.Destroy( m_outputTexture );
        m_outputTexture = null;
      }

      if ( m_resizeTexture != null ) {
        if ( m_resizeTexture.IsCreated() )
          m_resizeTexture.Release();
        UnityEngine.Object.Destroy( m_resizeTexture );
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
          UnityEngine.Object.Destroy( m_inputTexture );

        m_inputTexture = new Texture2D( sourceWidth, sourceHeight, TextureFormat.RGB24, false, false );
      }

      if ( m_resizeTexture == null || m_resizeTexture.width != targetWidth || m_resizeTexture.height != targetHeight ) {
        if ( m_resizeTexture != null ) {
          if ( m_resizeTexture.IsCreated() )
            m_resizeTexture.Release();
          UnityEngine.Object.Destroy( m_resizeTexture );
        }

        m_resizeTexture = new RenderTexture( targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32 );
        m_resizeTexture.Create();
      }

      if ( m_outputTexture == null || m_outputTexture.width != targetWidth || m_outputTexture.height != targetHeight ) {
        if ( m_outputTexture != null )
          UnityEngine.Object.Destroy( m_outputTexture );

        m_outputTexture = new Texture2D( targetWidth, targetHeight, TextureFormat.RGB24, false, false );
      }
    }

    private static bool IsValidationEpisode( int episodeIndex, float validationSplit )
    {
      var validationBucketCount = Mathf.Clamp( Mathf.RoundToInt( validationSplit * 100.0f ), 0, 99 );
      if ( validationBucketCount <= 0 )
        return false;

      return ComputeEpisodeSplitBucket( episodeIndex ) < validationBucketCount;
    }

    private static int ComputeEpisodeSplitBucket( int episodeIndex )
    {
      const int modulus = 100;
      const int multiplier = 61;
      const int offset = 17;

      var bucket = ( episodeIndex * multiplier + offset ) % modulus;
      return bucket < 0 ? bucket + modulus : bucket;
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

    private void TrackWrittenSample( string rootDirectory,
                                     string imagePath,
                                     string labelPath,
                                     long captureTimeNs,
                                     bool imageExportEnabled )
    {
      if ( imageExportEnabled && !string.IsNullOrWhiteSpace( imagePath ) ) {
        var relativeImagePath = Path.GetRelativePath( rootDirectory, imagePath );
        m_currentEpisodeImagePaths.Add( relativeImagePath.Replace( '\\', '/' ) );
      }

      if ( !string.IsNullOrWhiteSpace( labelPath ) ) {
        var relativeLabelPath = Path.GetRelativePath( rootDirectory, labelPath );
        m_currentEpisodeLabelPaths.Add( relativeLabelPath.Replace( '\\', '/' ) );
      }

      if ( captureTimeNs > 0 && m_currentEpisodeFirstCaptureTimeNs < 0 )
        m_currentEpisodeFirstCaptureTimeNs = captureTimeNs;

      if ( captureTimeNs > 0 )
        m_currentEpisodeLastCaptureTimeNs = captureTimeNs;
    }

    private void ResetEpisodeTracking()
    {
      m_currentEpisodeImagePaths.Clear();
      m_currentEpisodeLabelPaths.Clear();
      m_currentEpisodeFirstCaptureTimeNs = -1;
      m_currentEpisodeLastCaptureTimeNs = -1;
    }

    private void EnsureEpisodeIndexInitialized( RoiEncConfiguration.DatasetOptions options )
    {
      options ??= new RoiEncConfiguration.DatasetOptions();
      var rootDirectory = RoiPathUtility.ResolveOutputDirectory( options.RootDirectory );
      if ( m_hasInitializedEpisodeIndex &&
           string.Equals( m_initializedRootDirectory, rootDirectory, StringComparison.OrdinalIgnoreCase ) ) {
        return;
      }

      CurrentEpisodeIndex = DiscoverMaxEpisodeIndex( rootDirectory );
      m_initializedRootDirectory = rootDirectory;
      m_hasInitializedEpisodeIndex = true;
    }

    private static int DiscoverMaxEpisodeIndex( string rootDirectory )
    {
      if ( string.IsNullOrWhiteSpace( rootDirectory ) || !Directory.Exists( rootDirectory ) )
        return 0;

      var maxEpisodeIndex = 0;
      maxEpisodeIndex = Mathf.Max( maxEpisodeIndex, DiscoverMaxEpisodeIndexInDirectory( Path.Combine( rootDirectory, "manifests" ), EpisodeManifestPattern ) );
      maxEpisodeIndex = Mathf.Max( maxEpisodeIndex, DiscoverMaxEpisodeIndexInDirectory( Path.Combine( rootDirectory, "images" ), EpisodeSamplePattern ) );
      maxEpisodeIndex = Mathf.Max( maxEpisodeIndex, DiscoverMaxEpisodeIndexInDirectory( Path.Combine( rootDirectory, "labels" ), EpisodeSamplePattern ) );
      return maxEpisodeIndex;
    }

    private static int DiscoverMaxEpisodeIndexInDirectory( string directoryPath, Regex pattern )
    {
      if ( string.IsNullOrWhiteSpace( directoryPath ) || !Directory.Exists( directoryPath ) || pattern == null )
        return 0;

      var maxEpisodeIndex = 0;
      foreach ( var filePath in Directory.EnumerateFiles( directoryPath, "*", SearchOption.AllDirectories ) ) {
        var fileName = Path.GetFileName( filePath );
        if ( string.IsNullOrWhiteSpace( fileName ) )
          continue;

        var match = pattern.Match( fileName );
        if ( !match.Success || match.Groups.Count < 2 )
          continue;

        if ( int.TryParse( match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEpisodeIndex ) )
          maxEpisodeIndex = Mathf.Max( maxEpisodeIndex, parsedEpisodeIndex );
      }

      return maxEpisodeIndex;
    }

    private static string CaptureTimeNsToIsoString( long captureTimeNs )
    {
      if ( captureTimeNs <= 0 )
        return DateTimeOffset.UtcNow.ToString( "O", CultureInfo.InvariantCulture );

      var milliseconds = captureTimeNs / 1000000L;
      return DateTimeOffset.FromUnixTimeMilliseconds( milliseconds ).ToString( "O", CultureInfo.InvariantCulture );
    }
  }
}

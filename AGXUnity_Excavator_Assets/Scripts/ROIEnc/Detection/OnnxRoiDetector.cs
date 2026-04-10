using System;
using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection
{
  public sealed class OnnxRoiDetector : IRoiDetector
  {
    private readonly UnityEngine.Object m_modelAsset;
    private readonly RoiEncConfiguration m_configuration;
    private readonly IRoiDetectorBackend m_backend;
    private readonly List<RoiBackendTensor> m_backendOutputs = new List<RoiBackendTensor>();
    private readonly List<RoiDescriptor> m_parseScratchA = new List<RoiDescriptor>();
    private readonly List<RoiDescriptor> m_parseScratchB = new List<RoiDescriptor>();

    private RenderTexture m_preprocessTexture = null;
    private Texture2D m_rgbUploadTexture = null;
    private bool m_initialized = false;

    public string DetectorName => $"OnnxRoiDetector/{m_backend.BackendName}";
    public bool IsReady => m_initialized && m_backend.IsReady;

    public OnnxRoiDetector( UnityEngine.Object modelAsset, RoiEncConfiguration configuration )
    {
      m_modelAsset = modelAsset;
      m_configuration = configuration ?? new RoiEncConfiguration();
      m_backend = CreateBackend();
    }

    public bool TryGetRois( RoiFrameSample frameSample, List<RoiDescriptor> results, out string error )
    {
      error = string.Empty;
      if ( results == null ) {
        error = "roi_detector_results_list_missing";
        return false;
      }

      results.Clear();
      if ( frameSample == null ) {
        error = "roi_detector_frame_sample_missing";
        return false;
      }

      if ( !EnsureInitialized( out error ) )
        return false;

      if ( !TryPrepareInputTexture( frameSample, out var inputTexture, out error ) )
        return false;

      var request = new RoiBackendExecutionRequest
      {
        FlipY = true,
        Scale = m_configuration.Detection.InputScale,
        Bias = m_configuration.Detection.InputBias,
        Channels = 3
      };

      if ( !m_backend.TryExecute( inputTexture, request, m_backendOutputs, out error ) )
        return false;

      ParseDetections( frameSample.frameId, results );
      ApplyNms( results );
      return true;
    }

    public void Dispose()
    {
      m_backend.Dispose();

      if ( m_preprocessTexture != null ) {
        if ( m_preprocessTexture.IsCreated() )
          m_preprocessTexture.Release();
        UnityEngine.Object.Destroy( m_preprocessTexture );
        m_preprocessTexture = null;
      }

      if ( m_rgbUploadTexture != null ) {
        UnityEngine.Object.Destroy( m_rgbUploadTexture );
        m_rgbUploadTexture = null;
      }

      m_initialized = false;
    }

    private IRoiDetectorBackend CreateBackend()
    {
      return new NativeTensorRtDetectorBackend();
    }

    private bool EnsureInitialized( out string error )
    {
      error = string.Empty;
      if ( m_initialized && m_backend.IsReady )
        return true;

      m_initialized = m_backend.TryInitialize( m_modelAsset, m_configuration.Detection, out error );
      return m_initialized;
    }

    private bool TryPrepareInputTexture( RoiFrameSample frameSample, out Texture inputTexture, out string error )
    {
      inputTexture = null;
      error = string.Empty;

      if ( m_backend is NativeTensorRtDetectorBackend && frameSample.sourceTexture != null ) {
        inputTexture = frameSample.sourceTexture;
        return true;
      }

      var targetWidth = Mathf.Max( 32, m_configuration.Detection.ModelInputWidth );
      var targetHeight = Mathf.Max( 32, m_configuration.Detection.ModelInputHeight );
      EnsurePreprocessTexture( targetWidth, targetHeight );

      if ( frameSample.sourceTexture != null ) {
        Graphics.Blit( frameSample.sourceTexture, m_preprocessTexture );
        inputTexture = m_preprocessTexture;
        return true;
      }

      if ( frameSample.rgb24 == null || frameSample.rgb24.Length == 0 || frameSample.width <= 0 || frameSample.height <= 0 ) {
        error = "roi_detector_missing_texture_and_rgb24_input";
        return false;
      }

      EnsureRgbUploadTexture( frameSample.width, frameSample.height );
      UploadTopDownRgb24( m_rgbUploadTexture, frameSample.rgb24, frameSample.width, frameSample.height );
      Graphics.Blit( m_rgbUploadTexture, m_preprocessTexture );
      inputTexture = m_preprocessTexture;
      return true;
    }

    private void EnsurePreprocessTexture( int width, int height )
    {
      if ( m_preprocessTexture != null &&
           m_preprocessTexture.width == width &&
           m_preprocessTexture.height == height )
        return;

      if ( m_preprocessTexture != null ) {
        if ( m_preprocessTexture.IsCreated() )
          m_preprocessTexture.Release();
        UnityEngine.Object.Destroy( m_preprocessTexture );
      }

      m_preprocessTexture = new RenderTexture( width, height, 0, RenderTextureFormat.ARGB32 )
      {
        name = "ROIEnc_PreprocessRT"
      };
      m_preprocessTexture.Create();
    }

    private void EnsureRgbUploadTexture( int width, int height )
    {
      if ( m_rgbUploadTexture != null &&
           m_rgbUploadTexture.width == width &&
           m_rgbUploadTexture.height == height )
        return;

      if ( m_rgbUploadTexture != null )
        UnityEngine.Object.Destroy( m_rgbUploadTexture );

      m_rgbUploadTexture = new Texture2D( width, height, TextureFormat.RGB24, false, false )
      {
        name = "ROIEnc_RgbUpload"
      };
    }

    private static void UploadTopDownRgb24( Texture2D targetTexture, byte[] rgb24, int width, int height )
    {
      if ( targetTexture == null || rgb24 == null )
        return;

      var pixels = new Color32[ width * height ];
      for ( var y = 0; y < height; ++y ) {
        var sourceRow = y * width * 3;
        var destinationRow = ( height - 1 - y ) * width;
        for ( var x = 0; x < width; ++x ) {
          var sourceIndex = sourceRow + x * 3;
          pixels[ destinationRow + x ] = new Color32(
            rgb24[ sourceIndex + 0 ],
            rgb24[ sourceIndex + 1 ],
            rgb24[ sourceIndex + 2 ],
            255 );
        }
      }

      targetTexture.SetPixels32( pixels );
      targetTexture.Apply( false, false );
    }

    private void ParseDetections( long frameId, List<RoiDescriptor> results )
    {
      results.Clear();
      if ( m_backendOutputs.Count == 0 )
        return;

      var bestTensor = SelectBestTensor();
      if ( bestTensor == null || bestTensor.Data == null || bestTensor.Data.Length == 0 )
        return;

      if ( TryParseExplicitDetections( bestTensor, frameId, m_parseScratchA ) ) {
        CopyResults( m_parseScratchA, results );
        return;
      }

      if ( TryParseYoloTensorFromShape( bestTensor, frameId, results ) )
        return;

      var classCount = Mathf.Max( 1, m_configuration.Detection.ClassLabels != null ? m_configuration.Detection.ClassLabels.Length : 0 );
      var featureSizeWithObjectness = classCount + 5;
      var featureSizeWithoutObjectness = classCount + 4;

      var bestCount = -1;
      TryParseYoloTensor( bestTensor, frameId, featureSizeWithObjectness, true, false, m_parseScratchA, ref bestCount, results );
      TryParseYoloTensor( bestTensor, frameId, featureSizeWithObjectness, true, true, m_parseScratchB, ref bestCount, results );
      TryParseYoloTensor( bestTensor, frameId, featureSizeWithoutObjectness, false, false, m_parseScratchA, ref bestCount, results );
      TryParseYoloTensor( bestTensor, frameId, featureSizeWithoutObjectness, false, true, m_parseScratchB, ref bestCount, results );
    }

    private bool TryParseYoloTensorFromShape( RoiBackendTensor tensor, long frameId, List<RoiDescriptor> results )
    {
      results.Clear();
      if ( tensor == null || tensor.Shape == null || tensor.Shape.Length == 0 || tensor.Data == null || tensor.Data.Length == 0 )
        return false;

      var nonTrivialDims = new List<int>( 4 );
      foreach ( var dim in tensor.Shape ) {
        if ( dim > 1 )
          nonTrivialDims.Add( dim );
      }

      if ( nonTrivialDims.Count < 2 )
        return false;

      var bestCount = -1;
      var attempted = new HashSet<string>();
      for ( var firstIndex = 0; firstIndex < nonTrivialDims.Count - 1; ++firstIndex ) {
        for ( var secondIndex = firstIndex + 1; secondIndex < nonTrivialDims.Count; ++secondIndex ) {
          var firstDim = nonTrivialDims[ firstIndex ];
          var secondDim = nonTrivialDims[ secondIndex ];

          TryParseYoloTensorShapeCandidate( tensor, frameId, firstDim, secondDim, true, attempted, ref bestCount, results );
          TryParseYoloTensorShapeCandidate( tensor, frameId, secondDim, firstDim, false, attempted, ref bestCount, results );
        }
      }

      return bestCount > 0;
    }

    private void TryParseYoloTensorShapeCandidate( RoiBackendTensor tensor,
                                                   long frameId,
                                                   int featureSize,
                                                   int candidateCount,
                                                   bool featureMajor,
                                                   HashSet<string> attempted,
                                                   ref int bestCount,
                                                   List<RoiDescriptor> bestResults )
    {
      if ( featureSize <= 5 || candidateCount <= 0 || tensor == null || tensor.Data == null )
        return;

      if ( featureSize * candidateCount != tensor.Data.Length )
        return;

      var key = $"{featureSize}:{candidateCount}:{featureMajor}";
      if ( attempted != null && !attempted.Add( key ) )
        return;

      TryParseYoloTensor( tensor, frameId, featureSize, true, featureMajor, m_parseScratchA, ref bestCount, bestResults );
      TryParseYoloTensor( tensor, frameId, featureSize, false, featureMajor, m_parseScratchB, ref bestCount, bestResults );
    }

    private RoiBackendTensor SelectBestTensor()
    {
      RoiBackendTensor bestTensor = null;
      var bestLength = -1;
      foreach ( var output in m_backendOutputs ) {
        var length = output != null && output.Data != null ? output.Data.Length : 0;
        if ( length > bestLength ) {
          bestTensor = output;
          bestLength = length;
        }
      }

      return bestTensor;
    }

    private bool TryParseExplicitDetections( RoiBackendTensor tensor, long frameId, List<RoiDescriptor> results )
    {
      results.Clear();
      if ( tensor == null || tensor.Data == null || tensor.Data.Length < 6 || tensor.Data.Length % 6 != 0 )
        return false;

      var accepted = 0;
      var count = tensor.Data.Length / 6;
      for ( var index = 0; index < count; ++index ) {
        var offset = index * 6;
        var score = tensor.Data[ offset + 4 ];
        if ( score < m_configuration.Detection.ConfidenceThreshold )
          continue;

        var classIndex = Mathf.RoundToInt( tensor.Data[ offset + 5 ] );
        var rect = ResolveBoxRectTopLeft( tensor.Data[ offset + 0 ],
                                          tensor.Data[ offset + 1 ],
                                          tensor.Data[ offset + 2 ],
                                          tensor.Data[ offset + 3 ],
                                          assumeCorners: true );
        if ( rect.width <= 0.0f || rect.height <= 0.0f )
          continue;

        results.Add( CreateDetection( frameId, classIndex, score, rect ) );
        accepted += 1;
      }

      return accepted > 0;
    }

    private void TryParseYoloTensor( RoiBackendTensor tensor,
                                     long frameId,
                                     int featureSize,
                                     bool hasObjectness,
                                     bool featureMajor,
                                     List<RoiDescriptor> scratch,
                                     ref int bestCount,
                                     List<RoiDescriptor> bestResults )
    {
      scratch.Clear();
      if ( tensor == null || tensor.Data == null || featureSize <= 5 || tensor.Data.Length < featureSize || tensor.Data.Length % featureSize != 0 )
        return;

      var candidateCount = tensor.Data.Length / featureSize;
      for ( var candidateIndex = 0; candidateIndex < candidateCount; ++candidateIndex ) {
        var centerX = ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, 0, featureMajor );
        var centerY = ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, 1, featureMajor );
        var width = ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, 2, featureMajor );
        var height = ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, 3, featureMajor );
        if ( width <= 0.0f || height <= 0.0f )
          continue;

        var classStartIndex = hasObjectness ? 5 : 4;
        var objectness = hasObjectness ? Mathf.Max( 0.0f, ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, 4, featureMajor ) ) : 1.0f;
        var bestClassScore = 0.0f;
        var bestClassIndex = 0;
        for ( var classIndex = 0; classIndex < featureSize - classStartIndex; ++classIndex ) {
          var classScore = Mathf.Max( 0.0f, ReadTensorValue( tensor.Data, candidateCount, featureSize, candidateIndex, classStartIndex + classIndex, featureMajor ) );
          if ( classScore > bestClassScore ) {
            bestClassScore = classScore;
            bestClassIndex = classIndex;
          }
        }

        var score = objectness * bestClassScore;
        if ( score < m_configuration.Detection.ConfidenceThreshold )
          continue;

        var rect = ResolveBoxRectTopLeft( centerX, centerY, width, height, assumeCorners: false );
        if ( rect.width <= 0.0f || rect.height <= 0.0f )
          continue;

        scratch.Add( CreateDetection( frameId, bestClassIndex, score, rect ) );
      }

      if ( scratch.Count > bestCount ) {
        bestCount = scratch.Count;
        CopyResults( scratch, bestResults );
      }
    }

    private static float ReadTensorValue( float[] data,
                                          int candidateCount,
                                          int featureSize,
                                          int candidateIndex,
                                          int featureIndex,
                                          bool featureMajor )
    {
      return featureMajor ?
               data[ featureIndex * candidateCount + candidateIndex ] :
               data[ candidateIndex * featureSize + featureIndex ];
    }

    private Rect ResolveBoxRectTopLeft( float x0, float y0, float x1, float y1, bool assumeCorners )
    {
      var inputWidth = Mathf.Max( 1.0f, m_configuration.Detection.ModelInputWidth );
      var inputHeight = Mathf.Max( 1.0f, m_configuration.Detection.ModelInputHeight );

      if ( assumeCorners ) {
        var cornersLookLikePixels = Mathf.Max( Mathf.Abs( x0 ), Mathf.Abs( y0 ), Mathf.Abs( x1 ), Mathf.Abs( y1 ) ) > 1.5f;
        var xMin = cornersLookLikePixels ? x0 / inputWidth : x0;
        var yMin = cornersLookLikePixels ? y0 / inputHeight : y0;
        var xMax = cornersLookLikePixels ? x1 / inputWidth : x1;
        var yMax = cornersLookLikePixels ? y1 / inputHeight : y1;
        return RoiMathUtility.ClampNormalizedRectTopLeft( Rect.MinMaxRect( xMin, yMin, xMax, yMax ) );
      }

      var valuesLookLikePixels = Mathf.Max( Mathf.Abs( x0 ), Mathf.Abs( y0 ), Mathf.Abs( x1 ), Mathf.Abs( y1 ) ) > 1.5f;
      var centerX = valuesLookLikePixels ? x0 / inputWidth : x0;
      var centerY = valuesLookLikePixels ? y0 / inputHeight : y0;
      var width = valuesLookLikePixels ? x1 / inputWidth : x1;
      var height = valuesLookLikePixels ? y1 / inputHeight : y1;
      return RoiMathUtility.ClampNormalizedRectTopLeft( new Rect( centerX - 0.5f * width,
                                                                  centerY - 0.5f * height,
                                                                  width,
                                                                  height ) );
    }

    private RoiDescriptor CreateDetection( long frameId, int classIndex, float confidence, Rect rect )
    {
      var category = classIndex >= 0 &&
                     m_configuration.Detection.ClassLabels != null &&
                     classIndex < m_configuration.Detection.ClassLabels.Length ?
                       RoiCategoryUtility.FromLabel( m_configuration.Detection.ClassLabels[ classIndex ] ) :
                       RoiCategory.Unknown;
      return new RoiDescriptor
      {
        Category = category,
        Confidence = confidence,
        FrameId = frameId,
        NormalizedRect = rect,
        Priority = 100,
        Source = RoiSource.VisualModel,
        DebugText = "visual_model"
      };
    }

    private void ApplyNms( List<RoiDescriptor> detections )
    {
      if ( detections == null || detections.Count <= 1 )
        return;

      detections.Sort( CompareByScoreDescending );
      var kept = new List<RoiDescriptor>( detections.Count );
      foreach ( var candidate in detections ) {
        var shouldKeep = true;
        foreach ( var accepted in kept ) {
          if ( accepted.Category != candidate.Category )
            continue;

          if ( RoiMathUtility.IntersectionOverUnion( accepted.NormalizedRect, candidate.NormalizedRect ) >=
               m_configuration.Detection.NmsIouThreshold ) {
            shouldKeep = false;
            break;
          }
        }

        if ( shouldKeep )
          kept.Add( candidate );
      }

      detections.Clear();
      detections.AddRange( kept );
    }

    private static void CopyResults( List<RoiDescriptor> source, List<RoiDescriptor> destination )
    {
      destination.Clear();
      foreach ( var descriptor in source ) {
        if ( descriptor != null )
          destination.Add( descriptor.Clone() );
      }
    }

    private static int CompareByScoreDescending( RoiDescriptor left, RoiDescriptor right )
    {
      if ( ReferenceEquals( left, right ) )
        return 0;

      if ( left == null )
        return 1;

      if ( right == null )
        return -1;

      return right.Confidence.CompareTo( left.Confidence );
    }
  }
}

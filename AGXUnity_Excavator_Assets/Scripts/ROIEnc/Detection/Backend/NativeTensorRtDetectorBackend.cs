using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityDebug = UnityEngine.Debug;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend
{
  /// <summary>
  /// TensorRT backend: GPU Blit downscales to model input size, then a
  /// lightweight AsyncGPUReadback (~1.6 MB) feeds synchronous CUDA inference.
  /// Results are available one frame after the readback is requested.
  /// </summary>
  public sealed class NativeTensorRtDetectorBackend : IRoiDetectorBackend
  {
    private const string DllName = "trt_roi_backend";

    // ---- P/Invoke declarations ----

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_create( string enginePath,
                                              float confidenceThreshold,
                                              float nmsIouThreshold,
                                              int maxClassCount,
                                              int maxDetections,
                                              out int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern void trt_roi_destroy( int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_infer_cpu( int handle,
                                                  IntPtr rgbaPixels,
                                                  int width, int height,
                                                  [Out] float[] outputBuffer,
                                                  int outputBufferCapacity,
                                                  out int elementCount,
                                                  out float inferMs );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_get_output_size( int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_get_input_width( int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_get_input_height( int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern IntPtr trt_roi_get_last_error();

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_get_last_stats( int handle,
                                                      out int rawCandidates,
                                                      out int thresholdKept,
                                                      out int nmsKept,
                                                      out float postprocessMs );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_warmup( int handle, int iterations );

    // ---- State ----

    private int     m_handle       = -1;
    private float[] m_outputBuffer = null;
    private int     m_outputSize   = 0;
    private int     m_inputWidth   = 0;
    private int     m_inputHeight  = 0;
    private int     m_classCount   = 0;

    private RenderTexture               m_blitTarget;
    private AsyncGPUReadbackRequest     m_readbackReq;
    private bool                        m_readbackPending = false;
    private int                         m_maxDetections = 128;

    public string BackendName   => "NativeTensorRT";
    public bool   IsReady       => m_handle >= 0;
    public float  LastInferenceMs { get; private set; }
    public int    LastRawCandidateCount { get; private set; }
    public int    LastThresholdKeptCount { get; private set; }
    public int    LastNmsKeptCount { get; private set; }
    public float  LastPostprocessMs { get; private set; }

    // ---- IRoiDetectorBackend ----

    public bool TryInitialize( UnityEngine.Object modelAsset,
                               RoiEncConfiguration.DetectionOptions options,
                               out string error )
    {
      error = string.Empty;

      if ( m_handle >= 0 ) {
        trt_roi_destroy( m_handle );
        m_handle         = -1;
        m_readbackPending = false;
      }

      ReleaseBlitTarget();

      var enginePath = options != null && !string.IsNullOrWhiteSpace( options.TensorRtEnginePath )
                         ? options.TensorRtEnginePath
                         : string.Empty;

      if ( string.IsNullOrWhiteSpace( enginePath ) ) {
        error = "trt_engine_path_not_configured";
        return false;
      }

      m_classCount = options != null && options.ClassLabels != null ? options.ClassLabels.Length : 0;

      var resolvedPath = RoiPathUtility.ResolveConfiguredPath( enginePath );
      if ( !System.IO.File.Exists( resolvedPath ) ) {
        error = $"trt_engine_file_not_found:{resolvedPath}";
        return false;
      }

      m_maxDetections = 128;

      if ( trt_roi_create( resolvedPath,
                           options != null ? options.ConfidenceThreshold : 0.35f,
                           options != null ? options.NmsIouThreshold : 0.5f,
                           Mathf.Max( 1, m_classCount ),
                           m_maxDetections,
                           out m_handle ) != 0 ) {
        error = $"trt_create_failed:{GetLastNativeError()}";
        m_handle = -1;
        return false;
      }

      m_inputWidth  = trt_roi_get_input_width( m_handle );
      m_inputHeight = trt_roi_get_input_height( m_handle );
      m_outputSize  = trt_roi_get_output_size( m_handle );

      if ( options != null ) {
        if ( m_inputWidth > 0 )
          options.ModelInputWidth = m_inputWidth;
        if ( m_inputHeight > 0 )
          options.ModelInputHeight = m_inputHeight;
      }

      if ( m_outputSize <= 0 ) {
        error = "trt_output_size_zero";
        trt_roi_destroy( m_handle );
        m_handle = -1;
        return false;
      }

      m_outputBuffer = new float[ m_outputSize ];
      ResetDebugStats();

      EnsureBlitTarget();

      var warmup = options != null ? Mathf.Max( 1, options.WarmupIterations ) : 3;
      if ( trt_roi_warmup( m_handle, warmup ) != 0 )
        UnityDebug.LogWarning( $"TRT warmup failed: {GetLastNativeError()}" );

      return true;
    }

    /// <summary>
    /// Two-phase execute:
    ///   1. If previous readback is done -> run synchronous CUDA inference.
    ///   2. Blit input to model-sized RT, request lightweight readback (~1.6 MB).
    /// </summary>
    public bool TryExecute( Texture inputTexture,
                            RoiBackendExecutionRequest request,
                            List<RoiBackendTensor> outputs,
                            out string error )
    {
      error = string.Empty;
      if ( outputs != null )
        outputs.Clear();

      if ( m_handle < 0 ) {
        error = "trt_backend_not_initialized";
        return false;
      }

      if ( inputTexture == null ) {
        error = "trt_input_texture_null";
        return false;
      }

      // Phase 1: if previous readback completed, run inference
      if ( m_readbackPending && m_readbackReq.done ) {
        m_readbackPending = false;

        if ( m_readbackReq.hasError ) {
          UnityDebug.LogWarning( "TRT: AsyncGPUReadback error" );
        }
        else {
          var data = m_readbackReq.GetData<byte>();
          RunInferenceOnPixels( data, m_readbackReq.width, m_readbackReq.height, outputs, ref error );
        }
      }

      // Phase 2: GPU Blit downsample then request lightweight readback
      EnsureBlitTarget();
      if ( m_blitTarget != null ) {
        Graphics.Blit( inputTexture, m_blitTarget );
        m_readbackReq     = AsyncGPUReadback.Request( m_blitTarget, 0, TextureFormat.RGBA32 );
      }
      else {
        m_readbackReq = AsyncGPUReadback.Request( inputTexture, 0, TextureFormat.RGBA32 );
      }
      m_readbackPending = true;

      return string.IsNullOrEmpty( error );
    }

    public void Dispose()
    {
      if ( m_handle >= 0 ) {
        trt_roi_destroy( m_handle );
        m_handle = -1;
      }

      ReleaseBlitTarget();
      m_outputBuffer    = null;
      m_readbackPending = false;
      ResetDebugStats();
    }

    // ---- Internals ----

    private void EnsureBlitTarget()
    {
      if ( m_inputWidth <= 0 || m_inputHeight <= 0 )
        return;

      if ( m_blitTarget != null &&
           m_blitTarget.width == m_inputWidth &&
           m_blitTarget.height == m_inputHeight )
        return;

      ReleaseBlitTarget();
      m_blitTarget = new RenderTexture( m_inputWidth, m_inputHeight, 0,
                                         RenderTextureFormat.ARGB32,
                                         RenderTextureReadWrite.Linear );
      m_blitTarget.antiAliasing = 1;
      m_blitTarget.Create();
    }

    private void ReleaseBlitTarget()
    {
      if ( m_blitTarget != null ) {
        m_blitTarget.Release();
        UnityEngine.Object.Destroy( m_blitTarget );
        m_blitTarget = null;
      }
    }

    private unsafe void RunInferenceOnPixels( NativeArray<byte> pixels,
                                              int width, int height,
                                              List<RoiBackendTensor> outputs,
                                              ref string error )
    {
      var ptr = (IntPtr) NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr( pixels );

      int rc = trt_roi_infer_cpu( m_handle, ptr, width, height,
                                   m_outputBuffer, m_outputBuffer.Length,
                                   out int elementCount, out float inferMs );
      if ( rc != 0 ) {
        error = $"trt_infer_failed:{GetLastNativeError()}";
        UnityDebug.LogWarning( $"TRT inference error: {GetLastNativeError()}" );
        return;
      }

      LastInferenceMs = inferMs;
      UpdateLastStats();

      if ( outputs != null && elementCount > 0 ) {
        var tensor = new RoiBackendTensor
        {
          Name = "trt_output",
          Data = new float[ elementCount ]
        };
        Array.Copy( m_outputBuffer, tensor.Data, elementCount );
        tensor.Shape = BuildExplicitDetectionShape( elementCount );
        outputs.Add( tensor );
      }
    }

    private static string GetLastNativeError()
    {
      var ptr = trt_roi_get_last_error();
      return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi( ptr ) : "unknown";
    }

    private void UpdateLastStats()
    {
      if ( m_handle < 0 )
        return;

      if ( trt_roi_get_last_stats( m_handle,
                                   out var rawCandidates,
                                   out var thresholdKept,
                                   out var nmsKept,
                                   out var postprocessMs ) != 0 ) {
        return;
      }

      LastRawCandidateCount = rawCandidates;
      LastThresholdKeptCount = thresholdKept;
      LastNmsKeptCount = nmsKept;
      LastPostprocessMs = postprocessMs;
    }

    private void ResetDebugStats()
    {
      LastInferenceMs = 0.0f;
      LastRawCandidateCount = 0;
      LastThresholdKeptCount = 0;
      LastNmsKeptCount = 0;
      LastPostprocessMs = 0.0f;
    }

    private static int[] BuildExplicitDetectionShape( int elementCount )
    {
      if ( elementCount > 0 && elementCount % 6 == 0 )
        return new[] { elementCount / 6, 6 };

      return new[] { 0, 6 };
    }
  }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityDebug = UnityEngine.Debug;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend
{
  public sealed class NativeTensorRtDetectorBackend : IRoiDetectorBackend
  {
    private const string DllName = "trt_roi_backend";

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_create( string enginePath, out int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern void trt_roi_destroy( int handle );

    [DllImport( DllName, CallingConvention = CallingConvention.Cdecl )]
    private static extern int trt_roi_infer( int handle,
                                             IntPtr d3d11TexturePtr,
                                             int texWidth, int texHeight,
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
    private static extern int trt_roi_warmup( int handle, int iterations );

    private int m_handle = -1;
    private float[] m_outputBuffer = null;
    private int m_outputSize = 0;
    private int m_inputWidth = 0;
    private int m_inputHeight = 0;
    private int m_classCount = 0;

    public string BackendName => "NativeTensorRT";
    public bool IsReady => m_handle >= 0;

    public float LastInferenceMs { get; private set; }

    public bool TryInitialize( UnityEngine.Object modelAsset,
                               RoiEncConfiguration.DetectionOptions options,
                               out string error )
    {
      error = string.Empty;

      if ( m_handle >= 0 ) {
        trt_roi_destroy( m_handle );
        m_handle = -1;
      }

      var enginePath = options != null && !string.IsNullOrWhiteSpace( options.TensorRtEnginePath )
                         ? options.TensorRtEnginePath
                         : string.Empty;

      if ( string.IsNullOrWhiteSpace( enginePath ) ) {
        error = "trt_engine_path_not_configured";
        return false;
      }

      if ( SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 ) {
        error = $"trt_requires_d3d11:current={SystemInfo.graphicsDeviceType}";
        return false;
      }

      m_classCount = options != null && options.ClassLabels != null ? options.ClassLabels.Length : 0;

      var resolvedPath = RoiPathUtility.ResolveConfiguredPath( enginePath );
      if ( !System.IO.File.Exists( resolvedPath ) ) {
        error = $"trt_engine_file_not_found:{resolvedPath}";
        return false;
      }

      if ( trt_roi_create( resolvedPath, out m_handle ) != 0 ) {
        error = $"trt_create_failed:{GetLastNativeError()}";
        m_handle = -1;
        return false;
      }

      m_inputWidth = trt_roi_get_input_width( m_handle );
      m_inputHeight = trt_roi_get_input_height( m_handle );
      m_outputSize = trt_roi_get_output_size( m_handle );

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

      var warmup = options != null ? Mathf.Max( 1, options.WarmupIterations ) : 3;
      if ( trt_roi_warmup( m_handle, warmup ) != 0 ) {
        UnityDebug.LogWarning( $"TRT warmup failed: {GetLastNativeError()}" );
      }

      return true;
    }

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

      if ( SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 ) {
        error = $"trt_requires_d3d11:current={SystemInfo.graphicsDeviceType}";
        return false;
      }

      var nativePtr = inputTexture.GetNativeTexturePtr();
      if ( nativePtr == IntPtr.Zero ) {
        error = "trt_native_texture_ptr_null";
        return false;
      }

      int elementCount;
      float inferMs;
      if ( trt_roi_infer( m_handle, nativePtr,
                          inputTexture.width, inputTexture.height,
                          m_outputBuffer, m_outputBuffer.Length,
                          out elementCount, out inferMs ) != 0 ) {
        error = $"trt_infer_failed:{GetLastNativeError()}";
        return false;
      }

      LastInferenceMs = inferMs;

      if ( outputs != null && elementCount > 0 ) {
        var tensor = new RoiBackendTensor
        {
          Name = "trt_output",
          Data = new float[ elementCount ]
        };
        Array.Copy( m_outputBuffer, tensor.Data, elementCount );

        tensor.Shape = BuildTensorShape( elementCount );
        outputs.Add( tensor );
      }

      return true;
    }

    public void Dispose()
    {
      if ( m_handle >= 0 ) {
        trt_roi_destroy( m_handle );
        m_handle = -1;
      }

      m_outputBuffer = null;
    }

    private static string GetLastNativeError()
    {
      var ptr = trt_roi_get_last_error();
      return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi( ptr ) : "unknown";
    }

    private int[] BuildTensorShape( int elementCount )
    {
      if ( m_classCount > 0 ) {
        var featureSizeWithoutObjectness = m_classCount + 4;
        if ( featureSizeWithoutObjectness > 0 && elementCount % featureSizeWithoutObjectness == 0 )
          return new[] { 1, featureSizeWithoutObjectness, elementCount / featureSizeWithoutObjectness };

        var featureSizeWithObjectness = m_classCount + 5;
        if ( featureSizeWithObjectness > 0 && elementCount % featureSizeWithObjectness == 0 )
          return new[] { 1, featureSizeWithObjectness, elementCount / featureSizeWithObjectness };
      }

      return new[] { 1, elementCount };
    }
  }
}

using System.Collections.Generic;
using System.Diagnostics;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using AGXUnity_Excavator.Scripts.ROIEnc.Detection.Backend;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  public class InferenceBackendSmokeRunner : MonoBehaviour
  {
    [SerializeField]
    private TrackedCameraWindow m_cameraWindow = null;

    [SerializeField]
    private RoiEncConfiguration m_configuration = new RoiEncConfiguration();

    [SerializeField]
    private string m_lastResult = "not_run";

    [ContextMenu( "Run External ROI Self Test" )]
    public void RunExternalRoiSelfTest()
    {
      ResolveReferences();

      if ( !RoiExternalRuntimeLauncher.TryLaunch( m_configuration.ExternalRuntime,
                                                 selfTest: true,
                                                 out var process,
                                                 out var error ) ) {
        m_lastResult = $"launch_failed:{error}";
        UnityDebug.LogWarning( $"External ROI self-test launch failed: {error}", this );
        return;
      }

      try {
        if ( !process.WaitForExit( 60000 ) ) {
          try {
            process.Kill();
          }
          catch ( System.Exception ) {
          }

          m_lastResult = "self_test_timeout";
          UnityDebug.LogWarning( "External ROI self-test timed out after 60 seconds.", this );
          return;
        }

        if ( process.ExitCode != 0 ) {
          m_lastResult = $"self_test_failed:{process.ExitCode}";
          UnityDebug.LogWarning( $"External ROI self-test failed with exit code {process.ExitCode}.", this );
          return;
        }

        m_lastResult = "self_test_ok";
        UnityDebug.Log( "External ROI self-test succeeded.", this );
      }
      finally {
        process.Dispose();
      }
    }

    [ContextMenu( "Run Native TensorRT Self Test" )]
    public void RunNativeTensorRtSelfTest()
    {
      ResolveReferences();

      var backend = new NativeTensorRtDetectorBackend();
      try {
        if ( !backend.TryInitialize( null, m_configuration.Detection, out var initError ) ) {
          m_lastResult = $"native_init_failed:{initError}";
          UnityDebug.LogWarning( $"Native TensorRT self-test init failed: {initError}", this );
          return;
        }

        var testTexture = new RenderTexture(
            Mathf.Max( 32, m_configuration.Detection.ModelInputWidth ),
            Mathf.Max( 32, m_configuration.Detection.ModelInputHeight ),
            0, RenderTextureFormat.ARGB32 );
        testTexture.Create();

        try {
          var outputs = new List<RoiBackendTensor>();
          var request = new RoiBackendExecutionRequest
          {
            FlipY = false,
            Scale = Vector4.one,
            Bias = Vector4.zero,
            Channels = 3
          };

          const int warmupRuns = 3;
          const int benchmarkRuns = 10;

          for ( var i = 0; i < warmupRuns; ++i ) {
            if ( !backend.TryExecute( testTexture, request, outputs, out var warmupError ) ) {
              m_lastResult = $"native_warmup_failed:{warmupError}";
              UnityDebug.LogWarning( $"Native TensorRT warmup failed: {warmupError}", this );
              return;
            }
          }

          var stopwatch = Stopwatch.StartNew();
          for ( var i = 0; i < benchmarkRuns; ++i ) {
            if ( !backend.TryExecute( testTexture, request, outputs, out var benchError ) ) {
              m_lastResult = $"native_benchmark_failed:{benchError}";
              UnityDebug.LogWarning( $"Native TensorRT benchmark failed: {benchError}", this );
              return;
            }
          }

          stopwatch.Stop();
          var avgMs = stopwatch.Elapsed.TotalMilliseconds / benchmarkRuns;
          var outputElements = outputs.Count > 0 && outputs[ 0 ].Data != null ? outputs[ 0 ].Data.Length : 0;

          m_lastResult = $"native_self_test_ok:avg_ms={avgMs:F2},gpu_infer_ms={backend.LastInferenceMs:F2}," +
                         $"output_elements={outputElements}";
          UnityDebug.Log( $"Native TensorRT self-test OK. Average total={avgMs:F2}ms, " +
                          $"GPU infer={backend.LastInferenceMs:F2}ms, output={outputElements} floats.", this );
        }
        finally {
          testTexture.Release();
          Object.Destroy( testTexture );
        }
      }
      finally {
        backend.Dispose();
      }
    }

    private void ResolveReferences()
    {
      m_cameraWindow = ExcavatorRigLocator.ResolveComponent( this, m_cameraWindow );
    }
  }
}

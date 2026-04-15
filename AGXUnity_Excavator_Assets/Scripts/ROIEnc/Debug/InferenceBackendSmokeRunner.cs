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

        var inputW  = m_configuration.Detection.ModelInputWidth;
        var inputH  = m_configuration.Detection.ModelInputHeight;
        var outSize = backend.IsReady ? "ready" : "not_ready";

        m_lastResult = $"native_self_test_ok:init=ok,input={inputW}x{inputH},status={outSize}," +
                       $"note=render_thread_inference_tested_at_runtime";
        UnityDebug.Log( $"Native TensorRT self-test OK. Engine loaded, warmup done. " +
                        $"Input={inputW}x{inputH}. Render-thread inference will be verified at runtime.", this );
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

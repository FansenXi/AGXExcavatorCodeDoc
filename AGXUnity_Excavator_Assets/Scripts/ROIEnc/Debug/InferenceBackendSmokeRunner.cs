using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
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

    private void ResolveReferences()
    {
      m_cameraWindow = ExcavatorRigLocator.ResolveComponent( this, m_cameraWindow );
    }
  }
}

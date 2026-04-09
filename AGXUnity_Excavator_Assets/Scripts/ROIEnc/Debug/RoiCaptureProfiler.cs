using System.Diagnostics;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  public class RoiCaptureProfiler : MonoBehaviour
  {
    [SerializeField]
    private TrackedCameraWindow m_cameraWindow = null;

    [SerializeField]
    private AgxSimStepAckServer m_stepAckServer = null;

    [SerializeField]
    [Min( 1 )]
    private int m_sampleCount = 30;

    [SerializeField]
    private string m_lastSummary = "Not profiled yet.";

    [ContextMenu( "Profile Raw RGB Baseline" )]
    public void ProfileRawRgbBaseline()
    {
      ResolveReferences();

      if ( m_cameraWindow == null ) {
        m_lastSummary = "Profiling failed: camera window missing.";
        UnityDebug.LogWarning( m_lastSummary, this );
        return;
      }

      var totalCaptureMs = 0.0;
      var totalBytes = 0L;
      var successfulCaptureCount = 0;
      for ( var sampleIndex = 0; sampleIndex < Mathf.Max( 1, m_sampleCount ); ++sampleIndex ) {
        var stopwatch = Stopwatch.StartNew();
        var success = m_cameraWindow.TryCaptureRgb24( out var rgb24, out _, out _ );
        stopwatch.Stop();
        if ( !success || rgb24 == null )
          continue;

        successfulCaptureCount += 1;
        totalCaptureMs += stopwatch.Elapsed.TotalMilliseconds;
        totalBytes += rgb24.Length;
      }

      var totalServerExportMs = 0.0;
      var totalServerBytes = 0L;
      var successfulServerProfileCount = 0;
      if ( m_stepAckServer != null ) {
        for ( var sampleIndex = 0; sampleIndex < Mathf.Max( 1, m_sampleCount ); ++sampleIndex ) {
          if ( !m_stepAckServer.TryProfileFpvCapture( out var captureMs, out var payloadBytes ) )
            continue;

          successfulServerProfileCount += 1;
          totalServerExportMs += captureMs;
          totalServerBytes += payloadBytes;
        }
      }

      m_lastSummary =
        $"RawRGB avg capture={( successfulCaptureCount > 0 ? totalCaptureMs / successfulCaptureCount : 0.0 ):0.000} ms, " +
        $"avg payload={( successfulCaptureCount > 0 ? totalBytes / successfulCaptureCount : 0L )} bytes, " +
        $"stepAck image export avg={( successfulServerProfileCount > 0 ? totalServerExportMs / successfulServerProfileCount : 0.0 ):0.000} ms, " +
        $"stepAck payload={( successfulServerProfileCount > 0 ? totalServerBytes / successfulServerProfileCount : 0L )} bytes.";
      UnityDebug.Log( m_lastSummary, this );
    }

    private void ResolveReferences()
    {
      m_cameraWindow = ExcavatorRigLocator.ResolveComponent( this, m_cameraWindow );
      m_stepAckServer = ExcavatorRigLocator.ResolveComponent( this, m_stepAckServer );
    }
  }
}

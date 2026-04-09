using System;
using System.Globalization;
using System.IO;
using System.Text;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary
{
  public sealed class RoiDetectionLogger : IDisposable
  {
    private StreamWriter m_writer = null;

    public string CurrentPath { get; private set; } = string.Empty;

    public void BeginSession( RoiEncConfiguration.LoggingOptions options, RoiDetectionExperimentGroup experimentGroup )
    {
      Dispose();
      options = options ?? new RoiEncConfiguration.LoggingOptions();
      var logRoot = RoiPathUtility.ResolveOutputDirectory( options.LogDirectory );
      Directory.CreateDirectory( logRoot );

      var fileName = string.Format(
        CultureInfo.InvariantCulture,
        "roi_detection_{0}_{1}.csv",
        experimentGroup.ToString().ToLowerInvariant(),
        DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) );
      CurrentPath = Path.Combine( logRoot, fileName );
      m_writer = new StreamWriter( CurrentPath, false, new UTF8Encoding( false ) );
      m_writer.WriteLine( "time_sec,frame_id,step_id,experiment_group,motion_intensity,total_rois,bucket_detected,bucket_iou,bucket_confidence,used_kinematic_fallback" );
      m_writer.Flush();
    }

    public void LogFrame( float timeSeconds,
                          long frameId,
                          long stepId,
                          RoiDetectionExperimentGroup experimentGroup,
                          float motionIntensity,
                          bool bucketDetected,
                          float bucketIou,
                          float bucketConfidence,
                          int totalRois,
                          bool usedKinematicFallback )
    {
      if ( m_writer == null )
        return;

      m_writer.WriteLine( string.Join(
        ",",
        timeSeconds.ToString( "0.######", CultureInfo.InvariantCulture ),
        frameId.ToString( CultureInfo.InvariantCulture ),
        stepId.ToString( CultureInfo.InvariantCulture ),
        experimentGroup.ToString(),
        motionIntensity.ToString( "0.######", CultureInfo.InvariantCulture ),
        totalRois.ToString( CultureInfo.InvariantCulture ),
        bucketDetected ? "1" : "0",
        bucketIou.ToString( "0.######", CultureInfo.InvariantCulture ),
        bucketConfidence.ToString( "0.######", CultureInfo.InvariantCulture ),
        usedKinematicFallback ? "1" : "0" ) );
      m_writer.Flush();
    }

    public void Dispose()
    {
      if ( m_writer == null )
        return;

      m_writer.Dispose();
      m_writer = null;
    }
  }
}

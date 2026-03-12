using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class ExperimentLogger : MonoBehaviour
  {
    [SerializeField]
    private string m_logDirectory = "ExperimentLogs";

    private readonly List<string> m_rows = new List<string>();
    private string m_sourceName = "Unknown";
    private int m_episodeIndex = 0;

    public bool IsRecording { get; private set; }
    public string LastSavedPath { get; private set; } = string.Empty;

    public void BeginEpisode( int episodeIndex, string sourceName )
    {
      m_rows.Clear();
      m_rows.Add( "time,source,raw_left_x,raw_left_y,raw_right_x,raw_right_y,raw_drive,raw_steer,sim_left_x,sim_left_y,sim_right_x,sim_right_y,sim_drive,sim_steer,boom,bucket,stick,swing,drive,steer,throttle,bucket_pos_x,bucket_pos_y,bucket_pos_z,bucket_rot_x,bucket_rot_y,bucket_rot_z,bucket_rot_w,mass_in_bucket,excavated_mass,excavated_volume" );

      m_episodeIndex = episodeIndex;
      m_sourceName = sourceName;
      IsRecording = true;
      LastSavedPath = string.Empty;
    }

    public void RecordFrame( float timeSeconds,
                             OperatorCommand rawCommand,
                             OperatorCommand simulatedCommand,
                             ExcavatorActuationCommand actuationCommand,
                             Transform bucketReference,
                             global::MassVolumeCounter massVolumeCounter )
    {
      if ( !IsRecording )
        return;

      var bucketPosition = bucketReference != null ? bucketReference.position : Vector3.zero;
      var bucketRotation = bucketReference != null ? bucketReference.rotation : Quaternion.identity;
      var massInBucket = massVolumeCounter != null ? massVolumeCounter.MassInBucket : 0.0f;
      var excavatedMass = massVolumeCounter != null ? massVolumeCounter.ExcavatedMass : 0.0f;
      var excavatedVolume = massVolumeCounter != null ? massVolumeCounter.ExcavatedVolume : 0.0f;

      m_rows.Add(
        string.Join(
          ",",
          F( timeSeconds ),
          Sanitize( m_sourceName ),
          F( rawCommand.LeftStickX ),
          F( rawCommand.LeftStickY ),
          F( rawCommand.RightStickX ),
          F( rawCommand.RightStickY ),
          F( rawCommand.Drive ),
          F( rawCommand.Steer ),
          F( simulatedCommand.LeftStickX ),
          F( simulatedCommand.LeftStickY ),
          F( simulatedCommand.RightStickX ),
          F( simulatedCommand.RightStickY ),
          F( simulatedCommand.Drive ),
          F( simulatedCommand.Steer ),
          F( actuationCommand.Boom ),
          F( actuationCommand.Bucket ),
          F( actuationCommand.Stick ),
          F( actuationCommand.Swing ),
          F( actuationCommand.Drive ),
          F( actuationCommand.Steer ),
          F( actuationCommand.Throttle ),
          F( bucketPosition.x ),
          F( bucketPosition.y ),
          F( bucketPosition.z ),
          F( bucketRotation.x ),
          F( bucketRotation.y ),
          F( bucketRotation.z ),
          F( bucketRotation.w ),
          F( massInBucket ),
          F( excavatedMass ),
          F( excavatedVolume ) ) );
    }

    public string EndEpisode( string reason )
    {
      if ( !IsRecording )
        return LastSavedPath;

      IsRecording = false;
      if ( m_rows.Count <= 1 )
        return LastSavedPath;

      var directory = Path.GetFullPath( Path.Combine( Application.dataPath, "..", m_logDirectory ) );
      Directory.CreateDirectory( directory );

      var fileName = string.Format(
        CultureInfo.InvariantCulture,
        "episode_{0:000}_{1}_{2}_{3}.csv",
        m_episodeIndex,
        DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ),
        Sanitize( m_sourceName ),
        Sanitize( reason ) );

      LastSavedPath = Path.Combine( directory, fileName );
      File.WriteAllLines( LastSavedPath, m_rows );
      return LastSavedPath;
    }

    private static string F( float value )
    {
      return value.ToString( "0.######", CultureInfo.InvariantCulture );
    }

    private static string Sanitize( string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return "none";

      foreach ( var invalidChar in Path.GetInvalidFileNameChars() )
        value = value.Replace( invalidChar, '_' );

      return value.Replace( ' ', '_' );
    }
  }
}

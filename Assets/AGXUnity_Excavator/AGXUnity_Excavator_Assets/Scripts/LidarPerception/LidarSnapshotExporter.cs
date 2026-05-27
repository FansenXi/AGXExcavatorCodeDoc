using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.LidarPerception
{
  [Serializable]
  public class LidarSnapshot
  {
    public string schema_version = "lidar_snapshot_v0";
    public int frame = 0;
    public float sim_time_sec = 0.0f;
    public LidarMetadata metadata = new LidarMetadata();
    public float[] ranges_m = Array.Empty<float>();
    public int[] hit_mask = Array.Empty<int>();
    public float[] world_xyz = Array.Empty<float>();
  }

  [Serializable]
  public class LidarMetadata
  {
    public string sensor_name = string.Empty;
    public int rings = 16;
    public int azimuth_steps = 360;
    public float horizontal_fov_deg = 360.0f;
    public float vertical_min_deg = -15.0f;
    public float vertical_max_deg = 15.0f;
    public float max_distance_m = 40.0f;
    public int hit_count = 0;
    public float sensor_world_x = 0.0f;
    public float sensor_world_y = 0.0f;
    public float sensor_world_z = 0.0f;
    public float sensor_rotation_x = 0.0f;
    public float sensor_rotation_y = 0.0f;
    public float sensor_rotation_z = 0.0f;
    public float sensor_rotation_w = 1.0f;
    public string range_semantics = "ray_distance_m";
  }

  [AddComponentMenu( "AGXUnity Excavator/LiDAR Snapshot Exporter" )]
  public class LidarSnapshotExporter : MonoBehaviour
  {
    [SerializeField]
    private KeyCode m_exportKey = KeyCode.Alpha7;

    [SerializeField]
    private KeyCode m_secondaryExportKey = KeyCode.Keypad7;

    [SerializeField]
    private string m_outputDirectory = "LidarSnapshots";

    [SerializeField]
    private int m_rings = 16;

    [SerializeField]
    private int m_azimuthSteps = 360;

    [SerializeField]
    private float m_horizontalFovDegrees = 360.0f;

    [SerializeField]
    private float m_verticalMinDegrees = -15.0f;

    [SerializeField]
    private float m_verticalMaxDegrees = 15.0f;

    [SerializeField]
    private float m_maxDistanceMeters = 40.0f;

    [SerializeField]
    private LayerMask m_layerMask = ~0;

    [SerializeField]
    private QueryTriggerInteraction m_triggerInteraction = QueryTriggerInteraction.Ignore;

    [SerializeField]
    private bool m_exportOnStart = false;

    [SerializeField]
    private bool m_logExport = true;

    private void Start()
    {
      if ( m_exportOnStart )
        ExportSnapshot();
    }

    private void Update()
    {
      if ( Input.GetKeyDown( m_exportKey ) || Input.GetKeyDown( m_secondaryExportKey ) )
        ExportSnapshot();
    }

    [ContextMenu( "Export LiDAR Snapshot" )]
    public void ExportSnapshot()
    {
      var snapshot = Capture();
      var path = BuildOutputPath();
      var json = JsonUtility.ToJson( snapshot, prettyPrint: true );
      File.WriteAllText( path, json );

      if ( m_logExport ) {
        Debug.Log(
          $"LidarSnapshotExporter: exported {snapshot.metadata.rings}x{snapshot.metadata.azimuth_steps}" +
          $" LiDAR ({snapshot.metadata.hit_count} hits) -> {path}",
          this );
      }
    }

    public LidarSnapshot Capture()
    {
      var rings = Mathf.Max( 1, m_rings );
      var azimuthSteps = Mathf.Max( 1, m_azimuthSteps );
      var maxDistance = Mathf.Max( 0.01f, m_maxDistanceMeters );
      var count = rings * azimuthSteps;

      var snapshot = new LidarSnapshot {
        frame = Time.frameCount,
        sim_time_sec = Time.time,
        ranges_m = new float[ count ],
        hit_mask = new int[ count ],
        world_xyz = new float[ count * 3 ]
      };

      FillMetadata( snapshot.metadata, rings, azimuthSteps, maxDistance );

      var hitCount = 0;
      for ( var ring = 0; ring < rings; ++ring ) {
        var verticalT = rings == 1 ? 0.5f : ring / (float)( rings - 1 );
        var verticalDeg = Mathf.Lerp( m_verticalMinDegrees, m_verticalMaxDegrees, verticalT );
        var verticalRad = verticalDeg * Mathf.Deg2Rad;
        var cosVertical = Mathf.Cos( verticalRad );
        var sinVertical = Mathf.Sin( verticalRad );

        for ( var az = 0; az < azimuthSteps; ++az ) {
          var index = ring * azimuthSteps + az;
          var azimuthT = az / (float)azimuthSteps;
          var azimuthDeg = -0.5f * m_horizontalFovDegrees + azimuthT * m_horizontalFovDegrees;
          var azimuthRad = azimuthDeg * Mathf.Deg2Rad;

          var localDirection = new Vector3(
            Mathf.Sin( azimuthRad ) * cosVertical,
            sinVertical,
            Mathf.Cos( azimuthRad ) * cosVertical );
          var worldDirection = transform.TransformDirection( localDirection.normalized );
          var ray = new Ray( transform.position, worldDirection );

          if ( Physics.Raycast( ray, out var hit, maxDistance, m_layerMask, m_triggerInteraction ) ) {
            snapshot.ranges_m[ index ] = hit.distance;
            snapshot.hit_mask[ index ] = 1;
            snapshot.world_xyz[ index * 3 + 0 ] = hit.point.x;
            snapshot.world_xyz[ index * 3 + 1 ] = hit.point.y;
            snapshot.world_xyz[ index * 3 + 2 ] = hit.point.z;
            hitCount++;
          }
          else {
            snapshot.ranges_m[ index ] = maxDistance;
            snapshot.hit_mask[ index ] = 0;
            snapshot.world_xyz[ index * 3 + 0 ] = 0.0f;
            snapshot.world_xyz[ index * 3 + 1 ] = 0.0f;
            snapshot.world_xyz[ index * 3 + 2 ] = 0.0f;
          }
        }
      }

      snapshot.metadata.hit_count = hitCount;
      return snapshot;
    }

    private void FillMetadata( LidarMetadata metadata, int rings, int azimuthSteps, float maxDistance )
    {
      metadata.sensor_name = gameObject.name;
      metadata.rings = rings;
      metadata.azimuth_steps = azimuthSteps;
      metadata.horizontal_fov_deg = m_horizontalFovDegrees;
      metadata.vertical_min_deg = m_verticalMinDegrees;
      metadata.vertical_max_deg = m_verticalMaxDegrees;
      metadata.max_distance_m = maxDistance;
      metadata.sensor_world_x = transform.position.x;
      metadata.sensor_world_y = transform.position.y;
      metadata.sensor_world_z = transform.position.z;
      metadata.sensor_rotation_x = transform.rotation.x;
      metadata.sensor_rotation_y = transform.rotation.y;
      metadata.sensor_rotation_z = transform.rotation.z;
      metadata.sensor_rotation_w = transform.rotation.w;
    }

    private string BuildOutputPath()
    {
      var directory = m_outputDirectory;
      if ( string.IsNullOrWhiteSpace( directory ) )
        directory = "LidarSnapshots";

      if ( !Path.IsPathRooted( directory ) )
        directory = Path.Combine( Application.dataPath, "..", directory );

      Directory.CreateDirectory( directory );

      var stamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture );
      return Path.Combine( directory, $"lidar_{stamp}_frame{Time.frameCount}.json" );
    }
  }
}

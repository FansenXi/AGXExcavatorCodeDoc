using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.DepthPerception
{
  [Serializable]
  public class DepthCameraSnapshot
  {
    public string schema_version = "depth_camera_snapshot_v0";
    public int frame = 0;
    public float sim_time_sec = 0.0f;
    public DepthCameraMetadata metadata = new DepthCameraMetadata();
    public float[] depth_m = Array.Empty<float>();
    public float[] normalized_depth = Array.Empty<float>();
    public int[] hit_mask = Array.Empty<int>();
    public float[] world_xyz = Array.Empty<float>();
  }

  [Serializable]
  public class DepthCameraMetadata
  {
    public string camera_name = string.Empty;
    public int width = 64;
    public int height = 64;
    public float near_m = 0.05f;
    public float far_m = 25.0f;
    public int orthographic = 0;
    public float orthographic_size = 0.0f;
    public float field_of_view = 0.0f;
    public float camera_world_x = 0.0f;
    public float camera_world_y = 0.0f;
    public float camera_world_z = 0.0f;
    public float camera_rotation_x = 0.0f;
    public float camera_rotation_y = 0.0f;
    public float camera_rotation_z = 0.0f;
    public float camera_rotation_w = 1.0f;
    public int hit_count = 0;
    public string depth_semantics = "ray_distance_m";
  }

  [AddComponentMenu( "AGXUnity Excavator/Depth Camera Snapshot Exporter" )]
  [RequireComponent( typeof( Camera ) )]
  public class DepthCameraSnapshotExporter : MonoBehaviour
  {
    [SerializeField]
    private Camera m_depthCamera = null;

    [SerializeField]
    private KeyCode m_exportKey = KeyCode.Alpha8;

    [SerializeField]
    private KeyCode m_secondaryExportKey = KeyCode.Keypad8;

    [SerializeField]
    private string m_outputDirectory = "DepthCameraSnapshots";

    [SerializeField]
    private int m_width = 64;

    [SerializeField]
    private int m_height = 64;

    [SerializeField]
    private float m_maxDistanceMeters = 25.0f;

    [SerializeField]
    private LayerMask m_layerMask = ~0;

    [SerializeField]
    private QueryTriggerInteraction m_triggerInteraction = QueryTriggerInteraction.Ignore;

    [SerializeField]
    private bool m_exportOnStart = false;

    [SerializeField]
    private bool m_logExport = true;

    private void Awake()
    {
      ResolveCamera();
    }

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

    [ContextMenu( "Export Depth Camera Snapshot" )]
    public void ExportSnapshot()
    {
      ResolveCamera();
      if ( m_depthCamera == null ) {
        Debug.LogWarning( "DepthCameraSnapshotExporter: no Camera available.", this );
        return;
      }

      var snapshot = Capture();
      var path = BuildOutputPath();
      var json = JsonUtility.ToJson( snapshot, prettyPrint: true );
      File.WriteAllText( path, json );

      if ( m_logExport ) {
        Debug.Log(
          $"DepthCameraSnapshotExporter: exported {snapshot.metadata.width}x{snapshot.metadata.height}" +
          $" depth ({snapshot.metadata.hit_count} hits) -> {path}",
          this );
      }
    }

    public DepthCameraSnapshot Capture()
    {
      ResolveCamera();

      var width = Mathf.Max( 1, m_width );
      var height = Mathf.Max( 1, m_height );
      var maxDistance = Mathf.Max( 0.01f, m_maxDistanceMeters );
      var count = width * height;

      var snapshot = new DepthCameraSnapshot {
        frame = Time.frameCount,
        sim_time_sec = Time.time,
        depth_m = new float[ count ],
        normalized_depth = new float[ count ],
        hit_mask = new int[ count ],
        world_xyz = new float[ count * 3 ]
      };

      FillMetadata( snapshot.metadata, width, height, maxDistance );

      var hitCount = 0;
      for ( var y = 0; y < height; ++y ) {
        for ( var x = 0; x < width; ++x ) {
          var index = y * width + x;
          var viewport = new Vector3(
            ( x + 0.5f ) / width,
            ( y + 0.5f ) / height,
            0.0f );
          var ray = m_depthCamera.ViewportPointToRay( viewport );

          if ( Physics.Raycast( ray, out var hit, maxDistance, m_layerMask, m_triggerInteraction ) ) {
            snapshot.depth_m[ index ] = hit.distance;
            snapshot.normalized_depth[ index ] = Mathf.Clamp01(
              ( hit.distance - m_depthCamera.nearClipPlane ) /
              Mathf.Max( 0.001f, maxDistance - m_depthCamera.nearClipPlane ) );
            snapshot.hit_mask[ index ] = 1;
            snapshot.world_xyz[ index * 3 + 0 ] = hit.point.x;
            snapshot.world_xyz[ index * 3 + 1 ] = hit.point.y;
            snapshot.world_xyz[ index * 3 + 2 ] = hit.point.z;
            hitCount++;
          }
          else {
            snapshot.depth_m[ index ] = maxDistance;
            snapshot.normalized_depth[ index ] = 1.0f;
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

    private void FillMetadata( DepthCameraMetadata metadata, int width, int height, float maxDistance )
    {
      metadata.camera_name = m_depthCamera.name;
      metadata.width = width;
      metadata.height = height;
      metadata.near_m = m_depthCamera.nearClipPlane;
      metadata.far_m = maxDistance;
      metadata.orthographic = m_depthCamera.orthographic ? 1 : 0;
      metadata.orthographic_size = m_depthCamera.orthographicSize;
      metadata.field_of_view = m_depthCamera.fieldOfView;
      metadata.camera_world_x = m_depthCamera.transform.position.x;
      metadata.camera_world_y = m_depthCamera.transform.position.y;
      metadata.camera_world_z = m_depthCamera.transform.position.z;
      metadata.camera_rotation_x = m_depthCamera.transform.rotation.x;
      metadata.camera_rotation_y = m_depthCamera.transform.rotation.y;
      metadata.camera_rotation_z = m_depthCamera.transform.rotation.z;
      metadata.camera_rotation_w = m_depthCamera.transform.rotation.w;
    }

    private string BuildOutputPath()
    {
      var directory = m_outputDirectory;
      if ( string.IsNullOrWhiteSpace( directory ) )
        directory = "DepthCameraSnapshots";

      if ( !Path.IsPathRooted( directory ) )
        directory = Path.Combine( Application.dataPath, "..", directory );

      Directory.CreateDirectory( directory );

      var stamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture );
      return Path.Combine( directory, $"depth_camera_{stamp}_frame{Time.frameCount}.json" );
    }

    private void ResolveCamera()
    {
      if ( m_depthCamera == null )
        m_depthCamera = GetComponent<Camera>();
    }
  }
}

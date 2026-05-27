using System;
using System.Globalization;
using System.IO;
using AGXUnity_Excavator.Scripts.GraphPerception;
using UnityEngine;

[AddComponentMenu( "AGXUnity Excavator/Terrain Graph Snapshot Exporter" )]
/// <summary>
/// Triggers an offline terrain graph export by delegating sampling to a
/// <see cref="TerrainGraphObservationProvider"/>. Output JSON is written to
/// <see cref="m_outputDirectory"/> (default: TerrainGraphSnapshots/).
/// </summary>
public class TerrainGraphSnapshotExporter : MonoBehaviour
{
  [SerializeField]
  private TerrainGraphObservationProvider m_provider = null;

  [SerializeField]
  private KeyCode m_exportKey = KeyCode.Alpha9;

  [SerializeField]
  private string m_outputDirectory = "TerrainGraphSnapshots";

  [SerializeField]
  private bool m_exportOnStart = false;

  [SerializeField]
  private bool m_logExport = true;

  [SerializeField]
  [Tooltip( "If no provider is assigned, automatically create one on this GameObject when an export is requested." )]
  private bool m_autoCreateProvider = true;

  private void Start()
  {
    ResolveProvider();

    if ( m_exportOnStart )
      ExportSnapshot();
  }

  private void Update()
  {
    if ( Input.GetKeyDown( m_exportKey ) )
      ExportSnapshot();
  }

  [ContextMenu( "Export Terrain Graph Snapshot" )]
  public void ExportSnapshot()
  {
    ResolveProvider();
    if ( m_provider == null ) {
      Debug.LogWarning( "TerrainGraphSnapshotExporter: no TerrainGraphObservationProvider available.", this );
      return;
    }

    if ( m_provider.Terrain == null && !m_provider.ResolveReferences() ) {
      Debug.LogWarning( "TerrainGraphSnapshotExporter: provider could not resolve a DeformableTerrain.", this );
      return;
    }

    var observation = m_provider.Collect();
    var path = BuildOutputPath();
    var json = JsonUtility.ToJson( observation, prettyPrint: true );
    File.WriteAllText( path, json );

    if ( m_logExport ) {
      var nodeCount = observation.nodes != null ? observation.nodes.Length : 0;
      var edgeCount = observation.edges != null ? observation.edges.Length : 0;
      var legacySurface = observation.surface_nodes != null ? observation.surface_nodes.Length : 0;
      var legacyParticles = observation.particles != null ? observation.particles.Length : 0;
      Debug.Log(
        $"TerrainGraphSnapshotExporter: exported {nodeCount} nodes ({legacySurface} surface, {legacyParticles} particles)," +
        $" {edgeCount} edges -> {path}",
        this );
    }
  }

  private string BuildOutputPath()
  {
    var directory = m_outputDirectory;
    if ( string.IsNullOrWhiteSpace( directory ) )
      directory = "TerrainGraphSnapshots";

    if ( !Path.IsPathRooted( directory ) )
      directory = Path.Combine( Application.dataPath, "..", directory );

    Directory.CreateDirectory( directory );

    var stamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture );
    return Path.Combine( directory, $"terrain_graph_{stamp}_frame{Time.frameCount}.json" );
  }

  private void ResolveProvider()
  {
    if ( m_provider != null )
      return;

    m_provider = GetComponent<TerrainGraphObservationProvider>();
    if ( m_provider != null )
      return;

    m_provider = FindObjectOfType<TerrainGraphObservationProvider>();
    if ( m_provider != null )
      return;

    if ( m_autoCreateProvider )
      m_provider = gameObject.AddComponent<TerrainGraphObservationProvider>();
  }
}

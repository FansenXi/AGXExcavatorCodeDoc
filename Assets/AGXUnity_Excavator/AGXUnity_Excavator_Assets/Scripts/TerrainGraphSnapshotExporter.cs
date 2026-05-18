using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity.Model;
using AGXUnity.Utils;
using UnityEngine;

[AddComponentMenu( "AGXUnity Excavator/Terrain Graph Snapshot Exporter" )]
/// <summary>
/// Exports three soil-state views for representation experiments:
/// static surface heightmap samples, dynamic AGX soil particles, and a hybrid
/// node list that can be converted into a graph offline.
/// </summary>
public class TerrainGraphSnapshotExporter : MonoBehaviour
{
  [SerializeField]
  private DeformableTerrain m_terrain = null;

  [SerializeField]
  private KeyCode m_exportKey = KeyCode.G;

  [SerializeField]
  private string m_outputDirectory = "TerrainGraphSnapshots";

  [SerializeField]
  [Min( 1 )]
  private int m_surfaceStride = 4;

  [SerializeField]
  [Min( 1 )]
  private int m_maxDynamicParticles = 20000;

  [SerializeField]
  private bool m_exportOnStart = false;

  [SerializeField]
  private bool m_logExport = true;

  private void Start()
  {
    ResolveTerrain();

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
    ResolveTerrain();
    if ( m_terrain == null || m_terrain.TerrainData == null ) {
      Debug.LogWarning( "TerrainGraphSnapshotExporter: no DeformableTerrain found.", this );
      return;
    }

    var snapshot = BuildSnapshot();
    var path = BuildOutputPath();
    var json = JsonUtility.ToJson( snapshot, prettyPrint: true );
    File.WriteAllText( path, json );

    if ( m_logExport )
      Debug.Log( $"TerrainGraphSnapshotExporter: exported {snapshot.surface_nodes.Length} surface nodes, " +
                 $"{snapshot.particles.Length} dynamic particles -> {path}", this );
  }

  private TerrainGraphSnapshot BuildSnapshot()
  {
    var terrainData = m_terrain.TerrainData;
    var resolution = m_terrain.TerrainDataResolution;
    var stride = Mathf.Max( 1, m_surfaceStride );
    var heights = m_terrain.GetHeights( 0, 0, resolution - 1, resolution - 1 );
    var surfaceNodes = new List<SurfaceNode>( ( resolution / stride + 1 ) * ( resolution / stride + 1 ) );

    for ( var y = 0; y < heights.GetLength( 0 ); y += stride ) {
      for ( var x = 0; x < heights.GetLength( 1 ); x += stride ) {
        surfaceNodes.Add( BuildSurfaceNode( terrainData, resolution, x, y, heights[ y, x ] ) );
      }
    }

    var particleNodes = ReadDynamicParticles();

    return new TerrainGraphSnapshot {
      schema_version = "terrain_graph_snapshot_v0",
      frame = Time.frameCount,
      time = Time.time,
      terrain_name = m_terrain.name,
      terrain_resolution = resolution,
      terrain_width = terrainData.size.x,
      terrain_length = terrainData.size.z,
      terrain_height = terrainData.size.y,
      element_size = m_terrain.ElementSize,
      maximum_depth = m_terrain.MaximumDepth,
      surface_stride = stride,
      surface_nodes = surfaceNodes.ToArray(),
      particles = particleNodes.ToArray()
    };
  }

  private SurfaceNode BuildSurfaceNode( TerrainData terrainData, int resolution, int x, int y, float height )
  {
    var denom = Mathf.Max( 1, resolution - 1 );
    var local = new Vector3(
      terrainData.size.x * x / denom,
      height,
      terrainData.size.z * y / denom );
    var world = m_terrain.transform.TransformPoint( local );

    return new SurfaceNode {
      i = x,
      j = y,
      x = world.x,
      y = world.y,
      z = world.z,
      height = height
    };
  }

  private List<ParticleNode> ReadDynamicParticles()
  {
    var result = new List<ParticleNode>();
    var particles = m_terrain.GetParticles();
    if ( particles == null )
      return result;

    var count = particles.size();
    var maxCount = Mathf.Max( 1, m_maxDynamicParticles );
    var step = System.Math.Max( 1, (int)System.Math.Ceiling( count / (double)maxCount ) );

    for ( uint particleIndex = 0; particleIndex < count; particleIndex += (uint)step ) {
      var particle = particles.at( particleIndex );
      if ( particle == null )
        continue;

      var position = particle.getPosition().ToHandedVector3();
      result.Add( new ParticleNode {
        index = (int)particleIndex,
        x = position.x,
        y = position.y,
        z = position.z,
        radius = (float)particle.getRadius(),
        mass = (float)particle.getMass()
      } );
      particle.ReturnToPool();
    }

    return result;
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

  private void ResolveTerrain()
  {
    if ( m_terrain == null )
      m_terrain = GetComponent<DeformableTerrain>();

    if ( m_terrain == null )
      m_terrain = FindObjectOfType<DeformableTerrain>();
  }

  [Serializable]
  private class TerrainGraphSnapshot
  {
    public string schema_version;
    public int frame;
    public float time;
    public string terrain_name;
    public int terrain_resolution;
    public float terrain_width;
    public float terrain_length;
    public float terrain_height;
    public float element_size;
    public float maximum_depth;
    public int surface_stride;
    public SurfaceNode[] surface_nodes;
    public ParticleNode[] particles;
  }

  [Serializable]
  private class SurfaceNode
  {
    public int i;
    public int j;
    public float x;
    public float y;
    public float z;
    public float height;
  }

  [Serializable]
  private class ParticleNode
  {
    public int index;
    public float x;
    public float y;
    public float z;
    public float radius;
    public float mass;
  }
}

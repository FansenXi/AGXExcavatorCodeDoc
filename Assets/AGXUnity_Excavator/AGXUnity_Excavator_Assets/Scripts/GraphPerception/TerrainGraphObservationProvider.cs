using System;
using System.Collections.Generic;
using AGXUnity.Model;
using AGXUnity.Utils;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.GraphPerception
{
  public enum TerrainGraphRoiCenterMode
  {
    Bucket = 0,
    DigArea = 1,
    TerrainCenter = 2,
    WorldOrigin = 3,
    Custom = 4
  }

  [AddComponentMenu( "AGXUnity Excavator/Terrain Graph Observation Provider" )]
  public class TerrainGraphObservationProvider : MonoBehaviour
  {
    [SerializeField]
    private DeformableTerrain m_terrain = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    [Tooltip( "Optional bucket transform override. Falls back to ExcavatorMachineController.BucketReference." )]
    private Transform m_bucketReferenceOverride = null;

    [SerializeField]
    [Tooltip( "Optional companion component supplying multi-point bucket samples. When null, tool emission falls back to a single bucket-reference node." )]
    private BucketSamplePointProvider m_bucketSampleProvider = null;

    [SerializeField]
    [Tooltip( "Optional custom transform used as the RoI center when RoI mode is Custom." )]
    private Transform m_customRoiCenter = null;

    [SerializeField]
    private TerrainGraphRoiCenterMode m_roiCenterMode = TerrainGraphRoiCenterMode.Bucket;

    [SerializeField]
    [Min( 0.0f )]
    [Tooltip( "Radius (XZ) around RoI center used to filter surface/particles. <=0 disables the radius filter." )]
    private float m_roiRadiusMeters = 0.0f;

    [SerializeField]
    [Min( 0.0f )]
    [Tooltip( "Vertical margin around RoI center used to filter dynamic particles by height. <=0 disables the height filter." )]
    private float m_roiHeightMarginMeters = 0.0f;

    [SerializeField]
    [Tooltip( "When true, nodes between RoI radius and context radius are emitted with is_in_roi=0 as background/context. Mirrors the half-transparent particle cloud in Liu et al. 2026." )]
    private bool m_includeContextNodes = false;

    [SerializeField]
    [Min( 0.0f )]
    [Tooltip( "XZ radius around RoI center beyond which context nodes are dropped. Only effective when greater than m_roiRadiusMeters and m_includeContextNodes is true." )]
    private float m_roiContextRadiusMeters = 0.0f;

    [SerializeField]
    [Min( 1 )]
    private int m_surfaceStride = 4;

    [SerializeField]
    [Min( 0 )]
    [Tooltip( "Cap on emitted surface nodes (in-RoI + context combined). 0 disables the cap." )]
    private int m_maxSurfaceNodes = 0;

    [SerializeField]
    [Min( 1 )]
    private int m_maxDynamicParticles = 20000;

    [SerializeField]
    private bool m_includeSurfaceNodes = true;

    [SerializeField]
    private bool m_includeDynamicParticles = true;

    [SerializeField]
    private bool m_includeToolNodes = false;

    [SerializeField]
    [Tooltip( "Emit synthetic volumetric soil nodes under each sampled surface cell, down to subsurface_depth_m. Closes the gap to the Liu et al. 2026 volumetric representation when AGX has not yet failed those particles." )]
    private bool m_includeSubsurfaceSoil = false;

    [SerializeField]
    [Min( 0.01f )]
    [Tooltip( "Vertical spacing (meters) between subsurface_soil nodes along each XZ column." )]
    private float m_subsurfaceSpacingMeters = 0.15f;

    [SerializeField]
    [Min( 0.0f )]
    [Tooltip( "How far below each surface cell (meters) to generate subsurface_soil nodes. 0 disables emission even when include_subsurface_soil is true." )]
    private float m_subsurfaceDepthMeters = 1.5f;

    [SerializeField]
    [Min( 0 )]
    [Tooltip( "Hard cap on emitted subsurface_soil nodes (in-RoI + context combined). 0 disables the cap." )]
    private int m_maxSubsurfaceNodes = 0;

    [SerializeField]
    private bool m_includeEdges = false;

    [SerializeField]
    [Min( 0.0f )]
    private float m_edgeRadiusMeters = 0.5f;

    [SerializeField]
    [Min( 0 )]
    private int m_maxEdges = 5000;

    [SerializeField]
    [Tooltip( "Cap on the number of nodes the radius edge builder considers; 0 means all active (in-RoI, non-tool) nodes." )]
    [Min( 0 )]
    private int m_maxEdgeBuilderNodes = 1500;

    [SerializeField]
    [Tooltip( "Emit one set of K-nearest edges from each tool node to active soil nodes. Encodes the bucket-soil contact topology that the L-GBND figure highlights in its localized graph view." )]
    private bool m_includeToolSoilEdges = false;

    [SerializeField]
    [Min( 1 )]
    [Tooltip( "K nearest active soil nodes each tool node connects to." )]
    private int m_toolSoilEdgeK = 4;

    [SerializeField]
    [Min( 0.0f )]
    [Tooltip( "Maximum tool-to-soil distance (meters) for tool-soil edges. <=0 disables the cutoff (K nearest regardless of distance)." )]
    private float m_toolSoilEdgeMaxDistance = 1.5f;

    [SerializeField]
    [Min( 0 )]
    [Tooltip( "Hard cap on emitted tool-soil edges. 0 disables emission." )]
    private int m_maxToolSoilEdges = 200;

    [SerializeField]
    private string m_scenarioId = string.Empty;

    public DeformableTerrain Terrain => m_terrain;
    public ExcavatorMachineController MachineController => m_machineController;
    public BucketSamplePointProvider BucketSampleProvider => m_bucketSampleProvider;
    public TerrainGraphObservation LastObservation { get; private set; }

    private void Awake()
    {
      ResolveReferences();
    }

    public void SetScenarioId( string scenarioId )
    {
      m_scenarioId = scenarioId ?? string.Empty;
    }

    public bool ResolveReferences()
    {
      if ( m_terrain == null )
        m_terrain = GetComponent<DeformableTerrain>();
      if ( m_terrain == null )
        m_terrain = FindObjectOfType<DeformableTerrain>();

      if ( m_machineController == null )
        m_machineController = FindObjectOfType<ExcavatorMachineController>();

      if ( m_bucketSampleProvider == null )
        m_bucketSampleProvider = FindObjectOfType<BucketSamplePointProvider>();

      return m_terrain != null;
    }

    public TerrainGraphObservation Collect()
    {
      ResolveReferences();

      var observation = new TerrainGraphObservation {
        schema_version = TerrainGraphSchema.SchemaVersion,
        frame = Time.frameCount,
        sim_time_sec = Time.time,
        scenario_id = m_scenarioId ?? string.Empty,
        metadata = new TerrainGraphMetadata()
      };

      if ( m_terrain == null || m_terrain.TerrainData == null ) {
        observation.nodes = Array.Empty<TerrainGraphNode>();
        observation.edges = Array.Empty<TerrainGraphEdge>();
        observation.surface_nodes = Array.Empty<TerrainGraphLegacySurfaceNode>();
        observation.particles = Array.Empty<TerrainGraphLegacyParticleNode>();
        LastObservation = observation;
        return observation;
      }

      var bucketReference = ResolveBucketReference();
      var roiCenterWorld = ResolveRoiCenterWorld( bucketReference );

      var metadata = observation.metadata;
      PopulateStaticMetadata( metadata, bucketReference, roiCenterWorld );

      var nodes = new List<TerrainGraphNode>( 1024 );
      var legacySurface = new List<TerrainGraphLegacySurfaceNode>( 1024 );
      var legacyParticles = new List<TerrainGraphLegacyParticleNode>( 256 );

      var nextId = 0;

      if ( m_includeSurfaceNodes )
        SampleSurfaceAndSubsurfaceNodes( nodes, legacySurface, ref nextId, roiCenterWorld, metadata );
      else {
        metadata.surface_node_count = 0;
        metadata.subsurface_node_count = 0;
        metadata.surface_context_node_count = 0;
        metadata.subsurface_context_node_count = 0;
      }

      if ( m_includeDynamicParticles )
        SampleDynamicParticles( nodes, legacyParticles, ref nextId, roiCenterWorld, metadata );
      else
        metadata.dynamic_particle_count = 0;

      if ( m_includeToolNodes )
        AppendToolNodes( nodes, ref nextId, bucketReference, roiCenterWorld, metadata );
      else
        metadata.tool_node_count = 0;

      var radiusEdges = ( m_includeEdges && m_edgeRadiusMeters > 0.0f && m_maxEdges > 0 )
        ? BuildRadiusEdges( nodes, metadata )
        : Array.Empty<TerrainGraphEdge>();
      if ( !m_includeEdges )
        metadata.radius_edge_count = 0;

      var toolSoilEdges = ( m_includeToolSoilEdges && m_toolSoilEdgeK > 0 && m_maxToolSoilEdges > 0 )
        ? BuildToolSoilEdges( nodes, metadata )
        : Array.Empty<TerrainGraphEdge>();
      if ( !m_includeToolSoilEdges )
        metadata.tool_soil_edge_count = 0;

      TerrainGraphEdge[] edges;
      if ( radiusEdges.Length == 0 && toolSoilEdges.Length == 0 ) {
        edges = Array.Empty<TerrainGraphEdge>();
      }
      else if ( toolSoilEdges.Length == 0 ) {
        edges = radiusEdges;
      }
      else if ( radiusEdges.Length == 0 ) {
        edges = toolSoilEdges;
      }
      else {
        edges = new TerrainGraphEdge[ radiusEdges.Length + toolSoilEdges.Length ];
        Array.Copy( radiusEdges, 0, edges, 0, radiusEdges.Length );
        Array.Copy( toolSoilEdges, 0, edges, radiusEdges.Length, toolSoilEdges.Length );
      }
      metadata.edge_count = edges.Length;

      observation.nodes = nodes.ToArray();
      observation.edges = edges;
      observation.surface_nodes = legacySurface.ToArray();
      observation.particles = legacyParticles.ToArray();

      LastObservation = observation;
      return observation;
    }

    private Transform ResolveBucketReference()
    {
      if ( m_bucketReferenceOverride != null )
        return m_bucketReferenceOverride;
      if ( m_machineController != null )
        return m_machineController.BucketReference;
      return null;
    }

    private Vector3 ResolveRoiCenterWorld( Transform bucketReference )
    {
      switch ( m_roiCenterMode ) {
        case TerrainGraphRoiCenterMode.Bucket:
          if ( bucketReference != null )
            return bucketReference.position;
          return ResolveTerrainCenterWorld();
        case TerrainGraphRoiCenterMode.DigArea:
          var digArea = global::DigAreaMeasurement.FindOrCreateInScene();
          if ( digArea != null )
            return digArea.transform.position;
          return ResolveTerrainCenterWorld();
        case TerrainGraphRoiCenterMode.TerrainCenter:
          return ResolveTerrainCenterWorld();
        case TerrainGraphRoiCenterMode.WorldOrigin:
          return Vector3.zero;
        case TerrainGraphRoiCenterMode.Custom:
          if ( m_customRoiCenter != null )
            return m_customRoiCenter.position;
          if ( bucketReference != null )
            return bucketReference.position;
          return ResolveTerrainCenterWorld();
      }

      return Vector3.zero;
    }

    private Vector3 ResolveTerrainCenterWorld()
    {
      if ( m_terrain == null || m_terrain.TerrainData == null )
        return Vector3.zero;

      var size = m_terrain.TerrainData.size;
      var localCenter = new Vector3( size.x * 0.5f, 0.0f, size.z * 0.5f );
      return m_terrain.transform.TransformPoint( localCenter );
    }

    private void PopulateStaticMetadata( TerrainGraphMetadata metadata,
                                         Transform bucketReference,
                                         Vector3 roiCenterWorld )
    {
      var terrainData = m_terrain.TerrainData;
      metadata.terrain_name = m_terrain.name;
      metadata.terrain_resolution = m_terrain.TerrainDataResolution;
      metadata.terrain_width = terrainData.size.x;
      metadata.terrain_length = terrainData.size.z;
      metadata.terrain_height = terrainData.size.y;
      metadata.element_size = m_terrain.ElementSize;
      metadata.maximum_depth = m_terrain.MaximumDepth;
      metadata.surface_stride = Mathf.Max( 1, m_surfaceStride );

      metadata.roi_center_mode = RoiModeToString( m_roiCenterMode );
      metadata.roi_center_world_x = roiCenterWorld.x;
      metadata.roi_center_world_y = roiCenterWorld.y;
      metadata.roi_center_world_z = roiCenterWorld.z;
      metadata.roi_radius_m = m_roiRadiusMeters;
      metadata.roi_height_margin_m = m_roiHeightMarginMeters;
      metadata.roi_enabled = m_roiRadiusMeters > 0.0f ? 1 : 0;
      metadata.roi_context_radius_m = m_roiContextRadiusMeters;

      metadata.coordinate_frame = TerrainGraphSchema.CoordinateFrameGraphLocal;
      metadata.world_from_graph_origin_x = roiCenterWorld.x;
      metadata.world_from_graph_origin_y = roiCenterWorld.y;
      metadata.world_from_graph_origin_z = roiCenterWorld.z;
      metadata.world_from_graph_rotation_x = 0.0f;
      metadata.world_from_graph_rotation_y = 0.0f;
      metadata.world_from_graph_rotation_z = 0.0f;
      metadata.world_from_graph_rotation_w = 1.0f;

      metadata.include_surface_nodes = m_includeSurfaceNodes ? 1 : 0;
      metadata.include_dynamic_particles = m_includeDynamicParticles ? 1 : 0;
      metadata.include_tool_nodes = m_includeToolNodes ? 1 : 0;
      metadata.include_subsurface_soil = m_includeSubsurfaceSoil ? 1 : 0;
      metadata.include_context_nodes = m_includeContextNodes ? 1 : 0;
      metadata.include_edges = m_includeEdges ? 1 : 0;
      metadata.include_tool_soil_edges = m_includeToolSoilEdges ? 1 : 0;
      metadata.edge_radius_m = m_edgeRadiusMeters;
      metadata.max_edges = m_maxEdges;
      metadata.max_surface_nodes = m_maxSurfaceNodes;
      metadata.max_dynamic_particles = m_maxDynamicParticles;
      metadata.subsurface_spacing_m = m_subsurfaceSpacingMeters;
      metadata.subsurface_depth_m = m_subsurfaceDepthMeters;
      metadata.tool_soil_edge_k = m_toolSoilEdgeK;
      metadata.tool_soil_edge_max_distance_m = m_toolSoilEdgeMaxDistance;
      metadata.max_tool_soil_edges = m_maxToolSoilEdges;

      metadata.bucket_sample_provider_present = m_bucketSampleProvider != null ? 1 : 0;
      metadata.bucket_sample_provider_point_count =
        m_bucketSampleProvider != null ? m_bucketSampleProvider.ConfiguredPointCount : 0;

      metadata.bucket_reference_valid = bucketReference != null ? 1 : 0;
      if ( bucketReference != null ) {
        metadata.bucket_reference_name = bucketReference.name;
        metadata.bucket_world_x = bucketReference.position.x;
        metadata.bucket_world_y = bucketReference.position.y;
        metadata.bucket_world_z = bucketReference.position.z;
      }
    }

    /// <summary>
    /// Returns 1 (in-RoI), 0 (context, only when context emission is enabled), or -1 (skip entirely).
    /// </summary>
    private int ClassifyXzAgainstRoi( float worldX, float worldZ, Vector3 roiCenterWorld )
    {
      if ( m_roiRadiusMeters <= 0.0f )
        return 1; // RoI filter disabled — everything is "in RoI"

      var dx = worldX - roiCenterWorld.x;
      var dz = worldZ - roiCenterWorld.z;
      var distSq = dx * dx + dz * dz;

      var roiRadiusSq = m_roiRadiusMeters * m_roiRadiusMeters;
      if ( distSq <= roiRadiusSq )
        return 1;

      if ( !m_includeContextNodes || m_roiContextRadiusMeters <= m_roiRadiusMeters )
        return -1;

      var contextRadiusSq = m_roiContextRadiusMeters * m_roiContextRadiusMeters;
      return distSq <= contextRadiusSq ? 0 : -1;
    }

    private void SampleSurfaceAndSubsurfaceNodes( List<TerrainGraphNode> nodes,
                                                  List<TerrainGraphLegacySurfaceNode> legacy,
                                                  ref int nextId,
                                                  Vector3 roiCenterWorld,
                                                  TerrainGraphMetadata metadata )
    {
      var terrainData = m_terrain.TerrainData;
      var resolution = m_terrain.TerrainDataResolution;
      var stride = Mathf.Max( 1, m_surfaceStride );
      var heights = m_terrain.GetHeights( 0, 0, resolution - 1, resolution - 1 );
      if ( heights == null ) {
        metadata.surface_node_count = 0;
        metadata.subsurface_node_count = 0;
        metadata.surface_context_node_count = 0;
        metadata.subsurface_context_node_count = 0;
        return;
      }

      var heightsY = heights.GetLength( 0 );
      var heightsX = heights.GetLength( 1 );

      var subsurfaceEnabled = m_includeSubsurfaceSoil
                              && m_subsurfaceSpacingMeters > 0.0f
                              && m_subsurfaceDepthMeters > 0.0f;
      var subsurfaceSpacing = m_subsurfaceSpacingMeters;
      var subsurfaceDepth = m_subsurfaceDepthMeters;
      var subsurfaceRadius = subsurfaceSpacing * 0.5f;

      var surfaceInRoi = 0;
      var surfaceContext = 0;
      var subsurfaceInRoi = 0;
      var subsurfaceContext = 0;

      for ( var y = 0; y < heightsY; y += stride ) {
        for ( var x = 0; x < heightsX; x += stride ) {
          var totalSurface = surfaceInRoi + surfaceContext;
          if ( m_maxSurfaceNodes > 0 && totalSurface >= m_maxSurfaceNodes )
            break;

          var height = heights[ y, x ];
          var denom = Mathf.Max( 1, resolution - 1 );
          var local = new Vector3(
            terrainData.size.x * x / denom,
            height,
            terrainData.size.z * y / denom );
          var world = m_terrain.transform.TransformPoint( local );

          var classification = ClassifyXzAgainstRoi( world.x, world.z, roiCenterWorld );
          if ( classification < 0 )
            continue;
          var isInRoi = classification == 1;

          // Legacy back-compat: only in-RoI samples populate the legacy projection arrays,
          // so the Python visualizers under Tools/TerrainGraph keep producing the same view
          // they did before context emission existed.
          if ( isInRoi ) {
            legacy.Add( new TerrainGraphLegacySurfaceNode {
              i = x,
              j = y,
              x = world.x,
              y = world.y,
              z = world.z,
              height = height
            } );
          }

          var graphLocal = world - roiCenterWorld;
          nodes.Add( new TerrainGraphNode {
            id = nextId++,
            kind = TerrainGraphSchema.NodeKindSurfaceSoil,
            x = graphLocal.x,
            y = graphLocal.y,
            z = graphLocal.z,
            radius = m_terrain.ElementSize * 0.5f,
            mass = 0.0f,
            height = height,
            depth_below_surface = 0.0f,
            surface_i = x,
            surface_j = y,
            source_index = -1,
            is_dynamic = 0,
            is_tool = 0,
            is_in_roi = isInRoi ? 1 : 0
          } );

          if ( isInRoi ) surfaceInRoi++; else surfaceContext++;

          if ( !subsurfaceEnabled )
            continue;

          var depth = subsurfaceSpacing;
          while ( depth <= subsurfaceDepth + 1e-6f ) {
            var totalSubsurface = subsurfaceInRoi + subsurfaceContext;
            if ( m_maxSubsurfaceNodes > 0 && totalSubsurface >= m_maxSubsurfaceNodes )
              break;

            nodes.Add( new TerrainGraphNode {
              id = nextId++,
              kind = TerrainGraphSchema.NodeKindSubsurfaceSoil,
              x = graphLocal.x,
              y = graphLocal.y - depth,
              z = graphLocal.z,
              radius = subsurfaceRadius,
              mass = 0.0f,
              height = world.y - depth,
              depth_below_surface = depth,
              surface_i = x,
              surface_j = y,
              source_index = -1,
              is_dynamic = 0,
              is_tool = 0,
              is_in_roi = isInRoi ? 1 : 0
            } );

            if ( isInRoi ) subsurfaceInRoi++; else subsurfaceContext++;
            depth += subsurfaceSpacing;
          }
        }

        if ( m_maxSurfaceNodes > 0 && surfaceInRoi + surfaceContext >= m_maxSurfaceNodes )
          break;
      }

      metadata.surface_node_count = surfaceInRoi;
      metadata.subsurface_node_count = subsurfaceInRoi;
      metadata.surface_context_node_count = surfaceContext;
      metadata.subsurface_context_node_count = subsurfaceContext;
    }

    private void SampleDynamicParticles( List<TerrainGraphNode> nodes,
                                         List<TerrainGraphLegacyParticleNode> legacy,
                                         ref int nextId,
                                         Vector3 roiCenterWorld,
                                         TerrainGraphMetadata metadata )
    {
      var particles = m_terrain.GetParticles();
      if ( particles == null ) {
        metadata.dynamic_particle_count = 0;
        metadata.dynamic_particle_context_count = 0;
        metadata.dynamic_particles_available = 0;
        return;
      }

      var total = particles.size();
      metadata.dynamic_particles_available = (int)System.Math.Min( (uint)int.MaxValue, total );
      if ( total == 0 ) {
        metadata.dynamic_particle_count = 0;
        metadata.dynamic_particle_context_count = 0;
        return;
      }

      var maxCount = Mathf.Max( 1, m_maxDynamicParticles );
      var step = System.Math.Max( 1, (int)System.Math.Ceiling( total / (double)maxCount ) );

      var heightEnabled = m_roiHeightMarginMeters > 0.0f;
      var heightMargin = m_roiHeightMarginMeters;

      var emittedInRoi = 0;
      var emittedContext = 0;
      var skippedOutsideRoi = 0;
      var skippedOutsideContext = 0;

      for ( uint particleIndex = 0; particleIndex < total; particleIndex += (uint)step ) {
        var particle = particles.at( particleIndex );
        if ( particle == null )
          continue;

        var position = particle.getPosition().ToHandedVector3();
        var radius = (float)particle.getRadius();
        var mass = (float)particle.getMass();
        particle.ReturnToPool();

        var classification = ClassifyXzAgainstRoi( position.x, position.z, roiCenterWorld );
        if ( classification < 0 ) {
          // Outside the (RoI + context) XZ envelope.
          if ( m_roiRadiusMeters > 0.0f && m_includeContextNodes && m_roiContextRadiusMeters > m_roiRadiusMeters )
            skippedOutsideContext++;
          else
            skippedOutsideRoi++;
          continue;
        }
        var isInRoi = classification == 1;

        if ( heightEnabled ) {
          var dyAbs = Mathf.Abs( position.y - roiCenterWorld.y );
          if ( dyAbs > heightMargin ) {
            if ( isInRoi )
              skippedOutsideRoi++;
            else
              skippedOutsideContext++;
            continue;
          }
        }

        if ( isInRoi ) {
          legacy.Add( new TerrainGraphLegacyParticleNode {
            index = (int)particleIndex,
            x = position.x,
            y = position.y,
            z = position.z,
            radius = radius,
            mass = mass
          } );
        }

        var graphLocal = position - roiCenterWorld;
        nodes.Add( new TerrainGraphNode {
          id = nextId++,
          kind = TerrainGraphSchema.NodeKindDynamicSoil,
          x = graphLocal.x,
          y = graphLocal.y,
          z = graphLocal.z,
          radius = radius,
          mass = mass,
          height = position.y,
          depth_below_surface = 0.0f,
          surface_i = -1,
          surface_j = -1,
          source_index = (int)particleIndex,
          is_dynamic = 1,
          is_tool = 0,
          is_in_roi = isInRoi ? 1 : 0
        } );

        if ( isInRoi ) emittedInRoi++; else emittedContext++;
      }

      metadata.dynamic_particle_count = emittedInRoi;
      metadata.dynamic_particle_context_count = emittedContext;
      metadata.dynamic_particles_skipped_outside_roi = skippedOutsideRoi;
      metadata.dynamic_particles_skipped_outside_context = skippedOutsideContext;
    }

    private void AppendToolNodes( List<TerrainGraphNode> nodes,
                                  ref int nextId,
                                  Transform bucketReference,
                                  Vector3 roiCenterWorld,
                                  TerrainGraphMetadata metadata )
    {
      var positions = new List<Vector3>( 4 );
      var labels = new List<string>( 4 );
      var emitted = 0;

      if ( m_bucketSampleProvider != null ) {
        emitted = m_bucketSampleProvider.EmitSamplePoints( positions, labels, bucketReference );
      }
      else if ( bucketReference != null ) {
        positions.Add( bucketReference.position );
        labels.Add( "bucket_origin" );
        emitted = 1;
      }

      for ( var i = 0; i < positions.Count; ++i ) {
        var graphLocal = positions[ i ] - roiCenterWorld;
        // Tool nodes are always treated as "in-RoI" for edge-building purposes; they
        // represent the active bucket and should never be classified as context.
        nodes.Add( new TerrainGraphNode {
          id = nextId++,
          kind = TerrainGraphSchema.NodeKindTool,
          x = graphLocal.x,
          y = graphLocal.y,
          z = graphLocal.z,
          radius = 0.0f,
          mass = 0.0f,
          height = positions[ i ].y,
          depth_below_surface = 0.0f,
          surface_i = -1,
          surface_j = -1,
          source_index = i,
          is_dynamic = 0,
          is_tool = 1,
          is_in_roi = 1
        } );
      }

      metadata.tool_node_count = emitted;
    }

    private TerrainGraphEdge[] BuildRadiusEdges( List<TerrainGraphNode> nodes, TerrainGraphMetadata metadata )
    {
      if ( nodes.Count < 2 ) {
        metadata.radius_edge_count = 0;
        return Array.Empty<TerrainGraphEdge>();
      }

      // Active soil only: tool nodes are handled by BuildToolSoilEdges, context nodes are
      // background-only and not used for the localized graph the GNN sees.
      var candidateIndices = new List<int>( nodes.Count );
      for ( var i = 0; i < nodes.Count; ++i ) {
        var n = nodes[ i ];
        if ( n.is_tool != 0 )
          continue;
        if ( n.is_in_roi == 0 )
          continue;
        candidateIndices.Add( i );
      }

      if ( candidateIndices.Count < 2 ) {
        metadata.radius_edge_count = 0;
        return Array.Empty<TerrainGraphEdge>();
      }

      var candidateCount = candidateIndices.Count;
      if ( m_maxEdgeBuilderNodes > 0 && candidateCount > m_maxEdgeBuilderNodes ) {
        var sampled = new List<int>( m_maxEdgeBuilderNodes );
        for ( var i = 0; i < m_maxEdgeBuilderNodes; ++i ) {
          var pick = (int)System.Math.Round( i * ( candidateCount - 1 ) / (double)System.Math.Max( 1, m_maxEdgeBuilderNodes - 1 ) );
          sampled.Add( candidateIndices[ pick ] );
        }
        candidateIndices = sampled;
        candidateCount = candidateIndices.Count;
      }

      var edgeRadiusSq = m_edgeRadiusMeters * m_edgeRadiusMeters;
      var edges = new List<TerrainGraphEdge>( System.Math.Min( m_maxEdges, candidateCount * 8 ) );

      for ( var i = 0; i < candidateCount; ++i ) {
        if ( edges.Count >= m_maxEdges )
          break;
        var a = nodes[ candidateIndices[ i ] ];
        for ( var j = i + 1; j < candidateCount; ++j ) {
          if ( edges.Count >= m_maxEdges )
            break;
          var b = nodes[ candidateIndices[ j ] ];
          var dx = b.x - a.x;
          var dy = b.y - a.y;
          var dz = b.z - a.z;
          var distSq = dx * dx + dy * dy + dz * dz;
          if ( distSq > edgeRadiusSq )
            continue;

          edges.Add( new TerrainGraphEdge {
            src = a.id,
            dst = b.id,
            dx = dx,
            dy = dy,
            dz = dz,
            distance = Mathf.Sqrt( distSq ),
            kind_pair = a.kind + "|" + b.kind
          } );
        }
      }

      metadata.radius_edge_count = edges.Count;
      return edges.ToArray();
    }

    private TerrainGraphEdge[] BuildToolSoilEdges( List<TerrainGraphNode> nodes, TerrainGraphMetadata metadata )
    {
      if ( nodes.Count == 0 ) {
        metadata.tool_soil_edge_count = 0;
        return Array.Empty<TerrainGraphEdge>();
      }

      var toolIndices = new List<int>( 8 );
      var soilIndices = new List<int>( nodes.Count );
      for ( var i = 0; i < nodes.Count; ++i ) {
        var n = nodes[ i ];
        if ( n.is_tool != 0 ) {
          toolIndices.Add( i );
          continue;
        }
        if ( n.is_in_roi == 0 )
          continue;
        if ( n.kind == TerrainGraphSchema.NodeKindSurfaceSoil
             || n.kind == TerrainGraphSchema.NodeKindSubsurfaceSoil
             || n.kind == TerrainGraphSchema.NodeKindDynamicSoil ) {
          soilIndices.Add( i );
        }
      }

      if ( toolIndices.Count == 0 || soilIndices.Count == 0 ) {
        metadata.tool_soil_edge_count = 0;
        return Array.Empty<TerrainGraphEdge>();
      }

      var soilCount = soilIndices.Count;
      var maxDistSq = m_toolSoilEdgeMaxDistance > 0.0f
        ? m_toolSoilEdgeMaxDistance * m_toolSoilEdgeMaxDistance
        : float.PositiveInfinity;

      var edges = new List<TerrainGraphEdge>( System.Math.Min(
        m_maxToolSoilEdges,
        System.Math.Max( 1, toolIndices.Count ) * System.Math.Max( 1, m_toolSoilEdgeK ) ) );

      var distSqs = new float[ soilCount ];
      var sortedIdx = new int[ soilCount ];

      for ( var ti = 0; ti < toolIndices.Count; ++ti ) {
        if ( edges.Count >= m_maxToolSoilEdges )
          break;
        var t = nodes[ toolIndices[ ti ] ];

        for ( var k = 0; k < soilCount; ++k ) {
          var s = nodes[ soilIndices[ k ] ];
          var dx = s.x - t.x;
          var dy = s.y - t.y;
          var dz = s.z - t.z;
          distSqs[ k ] = dx * dx + dy * dy + dz * dz;
          sortedIdx[ k ] = soilIndices[ k ];
        }

        // Sort soil candidates by squared distance to this tool node; carry the soil
        // node indices along with the same permutation.
        Array.Sort( distSqs, sortedIdx );

        for ( var k = 0; k < soilCount && k < m_toolSoilEdgeK; ++k ) {
          if ( edges.Count >= m_maxToolSoilEdges )
            break;
          if ( distSqs[ k ] > maxDistSq )
            break;

          var s = nodes[ sortedIdx[ k ] ];
          var dx = s.x - t.x;
          var dy = s.y - t.y;
          var dz = s.z - t.z;
          edges.Add( new TerrainGraphEdge {
            src = t.id,
            dst = s.id,
            dx = dx,
            dy = dy,
            dz = dz,
            distance = Mathf.Sqrt( distSqs[ k ] ),
            kind_pair = t.kind + "|" + s.kind
          } );
        }
      }

      metadata.tool_soil_edge_count = edges.Count;
      return edges.ToArray();
    }

    private static string RoiModeToString( TerrainGraphRoiCenterMode mode )
    {
      switch ( mode ) {
        case TerrainGraphRoiCenterMode.Bucket:
          return TerrainGraphSchema.RoiCenterModeBucket;
        case TerrainGraphRoiCenterMode.DigArea:
          return TerrainGraphSchema.RoiCenterModeDigArea;
        case TerrainGraphRoiCenterMode.TerrainCenter:
          return TerrainGraphSchema.RoiCenterModeTerrainCenter;
        case TerrainGraphRoiCenterMode.WorldOrigin:
          return TerrainGraphSchema.RoiCenterModeWorldOrigin;
        case TerrainGraphRoiCenterMode.Custom:
          return TerrainGraphSchema.RoiCenterModeCustom;
      }

      return TerrainGraphSchema.RoiCenterModeBucket;
    }
  }
}

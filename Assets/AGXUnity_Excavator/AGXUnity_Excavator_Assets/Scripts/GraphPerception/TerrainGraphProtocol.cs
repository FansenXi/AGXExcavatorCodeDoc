using System;

namespace AGXUnity_Excavator.Scripts.GraphPerception
{
  public static class TerrainGraphSchema
  {
    public const string SchemaVersion = "terrain_graph_observation_v0";
    public const string LegacySchemaVersion = "terrain_graph_snapshot_v0";

    public const string NodeKindSurfaceSoil = "surface_soil";
    public const string NodeKindSubsurfaceSoil = "subsurface_soil";
    public const string NodeKindDynamicSoil = "dynamic_soil";
    public const string NodeKindTool = "tool";
    public const string NodeKindDynamicRigidbody = "dynamic_rigidbody";

    public const string RoiCenterModeBucket = "bucket";
    public const string RoiCenterModeDigArea = "dig_area";
    public const string RoiCenterModeTerrainCenter = "terrain_center";
    public const string RoiCenterModeWorldOrigin = "world_origin";
    public const string RoiCenterModeCustom = "custom";

    public const string CoordinateFrameGraphLocal = "graph_local_translated";
    public const string CoordinateFrameWorld = "world";
  }

  [Serializable]
  public class TerrainGraphObservation
  {
    public string schema_version = TerrainGraphSchema.SchemaVersion;
    public int frame = 0;
    public float sim_time_sec = 0.0f;
    public string scenario_id = string.Empty;

    public TerrainGraphMetadata metadata = new TerrainGraphMetadata();
    public TerrainGraphNode[] nodes = Array.Empty<TerrainGraphNode>();
    public TerrainGraphEdge[] edges = Array.Empty<TerrainGraphEdge>();

    public TerrainGraphLegacySurfaceNode[] surface_nodes = Array.Empty<TerrainGraphLegacySurfaceNode>();
    public TerrainGraphLegacyParticleNode[] particles = Array.Empty<TerrainGraphLegacyParticleNode>();
  }

  [Serializable]
  public class TerrainGraphNode
  {
    public int id = 0;
    public string kind = TerrainGraphSchema.NodeKindSurfaceSoil;

    public float x = 0.0f;
    public float y = 0.0f;
    public float z = 0.0f;

    public float vx = 0.0f;
    public float vy = 0.0f;
    public float vz = 0.0f;

    public float radius = 0.0f;
    public float mass = 0.0f;
    public float height = 0.0f;

    // Distance from the local surface (heightfield Y) at this node's XZ column,
    // in meters. 0 for surface_soil, increases downward for subsurface_soil,
    // signed for dynamic_soil/tool (free-fall above / contact below the local
    // surface). Default 0 so existing readers see no change.
    public float depth_below_surface = 0.0f;

    public int surface_i = -1;
    public int surface_j = -1;
    public int source_index = -1;

    public int is_dynamic = 0;
    public int is_tool = 0;
    public int is_in_roi = 0;

    public float boundary_normal_x = 0.0f;
    public float boundary_normal_y = 0.0f;
    public float boundary_normal_z = 0.0f;
  }

  [Serializable]
  public class TerrainGraphEdge
  {
    public int src = 0;
    public int dst = 0;
    public float dx = 0.0f;
    public float dy = 0.0f;
    public float dz = 0.0f;
    public float distance = 0.0f;
    public string kind_pair = string.Empty;
  }

  [Serializable]
  public class TerrainGraphMetadata
  {
    public string terrain_name = string.Empty;
    public int terrain_resolution = 0;
    public float terrain_width = 0.0f;
    public float terrain_length = 0.0f;
    public float terrain_height = 0.0f;
    public float element_size = 0.0f;
    public float maximum_depth = 0.0f;
    public int surface_stride = 1;

    public string roi_center_mode = TerrainGraphSchema.RoiCenterModeBucket;
    public float roi_center_world_x = 0.0f;
    public float roi_center_world_y = 0.0f;
    public float roi_center_world_z = 0.0f;
    public float roi_radius_m = 0.0f;
    public float roi_height_margin_m = 0.0f;
    public int roi_enabled = 0;

    public string coordinate_frame = TerrainGraphSchema.CoordinateFrameGraphLocal;
    public float world_from_graph_origin_x = 0.0f;
    public float world_from_graph_origin_y = 0.0f;
    public float world_from_graph_origin_z = 0.0f;
    public float world_from_graph_rotation_x = 0.0f;
    public float world_from_graph_rotation_y = 0.0f;
    public float world_from_graph_rotation_z = 0.0f;
    public float world_from_graph_rotation_w = 1.0f;

    public int include_surface_nodes = 1;
    public int include_dynamic_particles = 1;
    public int include_tool_nodes = 0;
    public int include_subsurface_soil = 0;
    public int include_context_nodes = 0;
    public int include_edges = 0;
    public int include_tool_soil_edges = 0;

    public float edge_radius_m = 0.0f;
    public int max_edges = 0;
    public int max_surface_nodes = 0;
    public int max_dynamic_particles = 0;

    public float subsurface_spacing_m = 0.0f;
    public float subsurface_depth_m = 0.0f;

    public float roi_context_radius_m = 0.0f;
    public int tool_soil_edge_k = 0;
    public float tool_soil_edge_max_distance_m = 0.0f;
    public int max_tool_soil_edges = 0;

    public int surface_node_count = 0;
    public int subsurface_node_count = 0;
    public int dynamic_particle_count = 0;
    public int tool_node_count = 0;
    public int edge_count = 0;

    public int surface_context_node_count = 0;
    public int subsurface_context_node_count = 0;
    public int dynamic_particle_context_count = 0;
    public int radius_edge_count = 0;
    public int tool_soil_edge_count = 0;

    public int dynamic_particles_available = 0;
    public int dynamic_particles_skipped_outside_roi = 0;
    public int dynamic_particles_skipped_outside_context = 0;

    public int bucket_sample_provider_present = 0;
    public int bucket_sample_provider_point_count = 0;

    public string bucket_reference_name = string.Empty;
    public float bucket_world_x = 0.0f;
    public float bucket_world_y = 0.0f;
    public float bucket_world_z = 0.0f;
    public int bucket_reference_valid = 0;
  }

  [Serializable]
  public class TerrainGraphLegacySurfaceNode
  {
    public int i = 0;
    public int j = 0;
    public float x = 0.0f;
    public float y = 0.0f;
    public float z = 0.0f;
    public float height = 0.0f;
  }

  [Serializable]
  public class TerrainGraphLegacyParticleNode
  {
    public int index = 0;
    public float x = 0.0f;
    public float y = 0.0f;
    public float z = 0.0f;
    public float radius = 0.0f;
    public float mass = 0.0f;
  }
}

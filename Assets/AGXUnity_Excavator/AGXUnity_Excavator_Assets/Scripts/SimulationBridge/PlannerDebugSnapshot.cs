using System;

namespace AGXUnity_Excavator.Scripts.SimulationBridge
{
  [Serializable]
  public class PlannerDebugCorridor
  {
    public int corridor_id = -1;
    public int cell_id = -1;
    public float entry_x_m = 0.0f;
    public float entry_z_m = 0.0f;
    public float exit_x_m = 0.0f;
    public float exit_z_m = 0.0f;
    public float score = 0.0f;
    public int attempts = 0;
    public int depleted = 0;
    public int low_productivity_streak = 0;
    public float last_payload_gain_kg = 0.0f;
    public float last_effective_deposit_delta_kg = 0.0f;
    public float last_remaining_depth_m = 0.0f;
    public string last_reason = string.Empty;
  }

  [Serializable]
  public class PlannerDebugSnapshot
  {
    public bool valid = false;
    public string mode = string.Empty;
    public int cycle = -1;
    public string skill = string.Empty;
    public int selected_corridor_id = -1;
    public float entry_x_m = 0.0f;
    public float entry_z_m = 0.0f;
    public float exit_x_m = 0.0f;
    public float exit_z_m = 0.0f;
    public float score = 0.0f;
    public int depleted_count = 0;
    public float last_payload_gain_kg = 0.0f;
    public float last_effective_deposit_delta_kg = 0.0f;
    public int global_low_productivity_streak = 0;
    public string stop_reason = string.Empty;
    public bool terminal_stop_requested = false;
    public string token_source = string.Empty;
    public string prior_id = string.Empty;
    public float[] dig_cut_tokens = Array.Empty<float>();
    public bool pre_dig_align_enabled = false;
    public int pre_dig_align_step_count = 0;
    public int pre_dig_align_hold_count = 0;
    public float pre_dig_align_entry_error_m = 0.0f;
    public float[] pre_dig_align_target_qpos = Array.Empty<float>();
    public PlannerDebugCorridor[] corridors = Array.Empty<PlannerDebugCorridor>();

    [NonSerialized]
    public string parse_warning = string.Empty;

    public bool HasCoverageDecision =>
      valid &&
      string.Equals( mode, "operator_prior_coverage", StringComparison.Ordinal ) &&
      selected_corridor_id >= 0;

    public static PlannerDebugSnapshot Empty()
    {
      return new PlannerDebugSnapshot();
    }

    public static PlannerDebugSnapshot ParseWarning( string warning )
    {
      return new PlannerDebugSnapshot
      {
        valid = false,
        parse_warning = warning ?? string.Empty
      };
    }

    public void Normalize()
    {
      mode = mode ?? string.Empty;
      skill = skill ?? string.Empty;
      stop_reason = stop_reason ?? string.Empty;
      token_source = token_source ?? string.Empty;
      prior_id = prior_id ?? string.Empty;
      dig_cut_tokens = dig_cut_tokens ?? Array.Empty<float>();
      pre_dig_align_target_qpos = pre_dig_align_target_qpos ?? Array.Empty<float>();
      corridors = corridors ?? Array.Empty<PlannerDebugCorridor>();
      for ( var index = 0; index < corridors.Length; ++index ) {
        if ( corridors[ index ] == null )
          corridors[ index ] = new PlannerDebugCorridor();
        corridors[ index ].last_reason = corridors[ index ].last_reason ?? string.Empty;
      }
    }
  }
}

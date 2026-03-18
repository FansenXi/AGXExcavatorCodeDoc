using System;

namespace AGXUnity_Excavator.Scripts.SimulationBridge
{
  internal static class AgxSimProtocolConstants
  {
    public const string ProtocolVersion = "agx-sim/v0";
    public const string ActionSemantics = "actuator_speed_cmd";
  }

  [Serializable]
  public class AgxSimRequestEnvelope
  {
    public string protocol_version = AgxSimProtocolConstants.ProtocolVersion;
    public string type = string.Empty;
    public AgxSimRequestPayload payload = new AgxSimRequestPayload();
  }

  [Serializable]
  public class AgxSimRequestPayload
  {
    public long step_id = 0;
    public float[] action = Array.Empty<float>();
    public int seed = 0;
    public bool reset_terrain = true;
    public bool reset_pose = true;
  }

  [Serializable]
  public class AgxSimResponseEnvelope
  {
    public string protocol_version = AgxSimProtocolConstants.ProtocolVersion;
    public string type = string.Empty;
    public string status = "ok";
    public string error = string.Empty;
    public AgxSimResponsePayload payload = new AgxSimResponsePayload();
  }

  [Serializable]
  public class AgxSimResponsePayload
  {
    public float dt = 0.02f;
    public float control_hz = 50.0f;
    public string action_semantics = AgxSimProtocolConstants.ActionSemantics;

    public string[] action_order = Array.Empty<string>();
    public string[] qpos_order = Array.Empty<string>();
    public string[] qvel_order = Array.Empty<string>();
    public string[] env_state_order = Array.Empty<string>();
    public string[] camera_names = Array.Empty<string>();
    public string[] warnings = Array.Empty<string>();

    public bool supports_reset_pose = false;
    public bool supports_images = false;

    public long step_id = 0;
    public float[] qpos = Array.Empty<float>();
    public float[] qvel = Array.Empty<float>();
    public float[] env_state = Array.Empty<float>();
    public float reward = 0.0f;
    public float sim_time_sec = 0.0f;
  }
}

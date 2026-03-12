using System;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  internal static class ActProtocolConstants
  {
    public const string ApiVersion = "act-operator/v1";
  }

  [Serializable]
  public class ActEpisodeConfig
  {
    public string task_name = "excavator_dig_v1";
    public int seed = 0;
    public float fixed_dt_sec = 0.02f;
    public float observation_rate_hz = 20.0f;
    public int command_timeout_ms = 200;
  }

  [Serializable]
  public class ActPose
  {
    public float[] position = new float[ 3 ];
    public float[] rotation_xyzw = new float[ 4 ] { 0.0f, 0.0f, 0.0f, 1.0f };

    public void Set( Vector3 newPosition, Quaternion newRotation )
    {
      position[ 0 ] = newPosition.x;
      position[ 1 ] = newPosition.y;
      position[ 2 ] = newPosition.z;

      rotation_xyzw[ 0 ] = newRotation.x;
      rotation_xyzw[ 1 ] = newRotation.y;
      rotation_xyzw[ 2 ] = newRotation.z;
      rotation_xyzw[ 3 ] = newRotation.w;
    }
  }

  [Serializable]
  public class ActVelocity
  {
    public float[] linear = new float[ 3 ];
    public float[] angular = new float[ 3 ];

    public void Set( Vector3 newLinear, Vector3 newAngular )
    {
      linear[ 0 ] = newLinear.x;
      linear[ 1 ] = newLinear.y;
      linear[ 2 ] = newLinear.z;

      angular[ 0 ] = newAngular.x;
      angular[ 1 ] = newAngular.y;
      angular[ 2 ] = newAngular.z;
    }
  }

  [Serializable]
  public class ActActuatorState
  {
    public float boom_position_norm = 0.0f;
    public float boom_speed = 0.0f;
    public float stick_position_norm = 0.0f;
    public float stick_speed = 0.0f;
    public float bucket_position_norm = 0.0f;
    public float bucket_speed = 0.0f;
    public float swing_speed = 0.0f;
  }

  [Serializable]
  public class ActTaskState
  {
    public float mass_in_bucket_kg = 0.0f;
    public float excavated_mass_kg = 0.0f;
    public float excavated_volume_m3 = 0.0f;
  }

  [Serializable]
  public class ActWireOperatorCommand
  {
    public float left_stick_x = 0.0f;
    public float left_stick_y = 0.0f;
    public float right_stick_x = 0.0f;
    public float right_stick_y = 0.0f;
    public float drive = 0.0f;
    public float steer = 0.0f;

    public static ActWireOperatorCommand FromOperatorCommand( Control.Core.OperatorCommand command )
    {
      return new ActWireOperatorCommand
      {
        left_stick_x = command.LeftStickX,
        left_stick_y = command.LeftStickY,
        right_stick_x = command.RightStickX,
        right_stick_y = command.RightStickY,
        drive = command.Drive,
        steer = command.Steer
      };
    }

    public Control.Core.OperatorCommand ToOperatorCommand()
    {
      return new Control.Core.OperatorCommand
      {
        LeftStickX = left_stick_x,
        LeftStickY = left_stick_y,
        RightStickX = right_stick_x,
        RightStickY = right_stick_y,
        Drive = drive,
        Steer = steer
      }.ClampAxes();
    }
  }

  [Serializable]
  public class ActObservation
  {
    public float sim_time_sec = 0.0f;
    public float fixed_dt_sec = 0.02f;
    public ActPose base_pose_world = new ActPose();
    public ActVelocity base_velocity_local = new ActVelocity();
    public ActPose bucket_pose_world = new ActPose();
    public ActActuatorState actuator_state = new ActActuatorState();
    public ActTaskState task_state = new ActTaskState();
    public ActWireOperatorCommand previous_operator_command = new ActWireOperatorCommand();
  }

  public struct ActStepRequest
  {
    public string SessionId;
    public int Seq;
    public ActObservation Observation;
  }

  public struct ActStepResponse
  {
    public string SessionId;
    public int Seq;
    public string Status;
    public Control.Core.OperatorCommand OperatorCommand;
    public float InferenceTimeMs;
    public float ModelTimeSec;
    public bool HasValue;
  }

  [Serializable]
  internal class ActWireEnvelopeBase
  {
    public string api_version = string.Empty;
    public string type = string.Empty;
    public string session_id = string.Empty;
    public int seq = 0;
  }

  [Serializable]
  internal class ActHelloPayload
  {
    public string client = "unity";
    public string project = "AGXUnity_Excavator";
  }

  [Serializable]
  internal class ActHelloMessage
  {
    public string api_version = ActProtocolConstants.ApiVersion;
    public string type = "hello";
    public string session_id = "bootstrap";
    public int seq = 0;
    public ActHelloPayload payload = new ActHelloPayload();
  }

  [Serializable]
  internal class ActResetPayload
  {
    public string task_name = "excavator_dig_v1";
    public int seed = 0;
    public float fixed_dt_sec = 0.02f;
    public float observation_rate_hz = 20.0f;
    public int command_timeout_ms = 200;
  }

  [Serializable]
  internal class ActResetMessage
  {
    public string api_version = ActProtocolConstants.ApiVersion;
    public string type = "reset";
    public string session_id = string.Empty;
    public int seq = 0;
    public ActResetPayload payload = new ActResetPayload();
  }

  [Serializable]
  internal class ActStepPayload
  {
    public float sim_time_sec = 0.0f;
    public float fixed_dt_sec = 0.02f;
    public ActObservation observation = new ActObservation();
  }

  [Serializable]
  internal class ActStepMessage
  {
    public string api_version = ActProtocolConstants.ApiVersion;
    public string type = "step";
    public string session_id = string.Empty;
    public int seq = 0;
    public ActStepPayload payload = new ActStepPayload();
  }

  [Serializable]
  internal class ActClosePayload
  {
    public string reason = "episode_end";
  }

  [Serializable]
  internal class ActCloseMessage
  {
    public string api_version = ActProtocolConstants.ApiVersion;
    public string type = "close";
    public string session_id = string.Empty;
    public int seq = 0;
    public ActClosePayload payload = new ActClosePayload();
  }

  [Serializable]
  internal class ActStepResultPayload
  {
    public string status = string.Empty;
    public ActWireOperatorCommand operator_command = new ActWireOperatorCommand();
    public float inference_time_ms = 0.0f;
    public float model_time_sec = 0.0f;
  }

  [Serializable]
  internal class ActStepResultMessage
  {
    public string api_version = string.Empty;
    public string type = string.Empty;
    public string session_id = string.Empty;
    public int seq = 0;
    public ActStepResultPayload payload = new ActStepResultPayload();
  }
}

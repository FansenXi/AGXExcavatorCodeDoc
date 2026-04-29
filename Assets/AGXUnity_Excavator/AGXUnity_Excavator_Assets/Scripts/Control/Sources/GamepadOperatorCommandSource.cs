using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public class GamepadOperatorCommandSource : OperatorCommandSourceBehaviour
  {
    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_stickDeadzone = 0.15f;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_triggerDeadzone = 0.05f;

    public override string SourceName => "Gamepad";

#if ENABLE_INPUT_SYSTEM
    private InputAction m_leftStickAction;
    private InputAction m_rightStickAction;
    private InputAction m_driveAction;
    private InputAction m_steerAction;
    private InputAction m_resetAction;
    private InputAction m_startEpisodeAction;
    private InputAction m_stopEpisodeAction;
    private bool m_actionsInitialized = false;
#endif

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
      InitializeActions();
      EnableActions();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
      DisableActions();
#endif
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
      m_leftStickAction?.Dispose();
      m_rightStickAction?.Dispose();
      m_driveAction?.Dispose();
      m_steerAction?.Dispose();
      m_resetAction?.Dispose();
      m_startEpisodeAction?.Dispose();
      m_stopEpisodeAction?.Dispose();
#endif
    }

    public override OperatorCommand ReadCommand()
    {
      var command = OperatorCommand.Zero;

#if ENABLE_INPUT_SYSTEM
      var leftStick = ReadVector2( m_leftStickAction, m_stickDeadzone );
      var rightStick = ReadVector2( m_rightStickAction, m_stickDeadzone );

      command.LeftStickX = leftStick.x;
      command.LeftStickY = leftStick.y;
      command.RightStickX = rightStick.x;
      command.RightStickY = rightStick.y;
      command.Drive = ReadAxis( m_driveAction, m_triggerDeadzone );
      command.Steer = ReadAxis( m_steerAction, m_triggerDeadzone );
      command.ResetRequested = m_resetAction != null && m_resetAction.WasPressedThisFrame();
      command.StartEpisodeRequested = m_startEpisodeAction != null && m_startEpisodeAction.WasPressedThisFrame();
      command.StopEpisodeRequested = m_stopEpisodeAction != null && m_stopEpisodeAction.WasPressedThisFrame();
#endif

      return command.ClampAxes();
    }

#if ENABLE_INPUT_SYSTEM
    private void InitializeActions()
    {
      if ( m_actionsInitialized )
        return;

      m_leftStickAction = new InputAction( "LeftStick", InputActionType.Value, "<Gamepad>/leftStick" );
      m_rightStickAction = new InputAction( "RightStick", InputActionType.Value, "<Gamepad>/rightStick" );

      m_driveAction = new InputAction( "Drive", InputActionType.Value );
      m_driveAction.AddCompositeBinding( "1DAxis" )
                   .With( "Negative", "<Gamepad>/leftTrigger" )
                   .With( "Positive", "<Gamepad>/rightTrigger" );

      m_steerAction = new InputAction( "Steer", InputActionType.Value );
      m_steerAction.AddCompositeBinding( "1DAxis" )
                   .With( "Negative", "<Gamepad>/leftShoulder" )
                   .With( "Positive", "<Gamepad>/rightShoulder" );

      m_resetAction = new InputAction( "ResetEpisode", InputActionType.Button, "<Gamepad>/buttonNorth" );
      m_startEpisodeAction = new InputAction( "StartEpisode", InputActionType.Button, "<Gamepad>/start" );
      m_stopEpisodeAction = new InputAction( "StopEpisode", InputActionType.Button, "<Gamepad>/select" );

      m_actionsInitialized = true;
    }

    private void EnableActions()
    {
      m_leftStickAction?.Enable();
      m_rightStickAction?.Enable();
      m_driveAction?.Enable();
      m_steerAction?.Enable();
      m_resetAction?.Enable();
      m_startEpisodeAction?.Enable();
      m_stopEpisodeAction?.Enable();
    }

    private void DisableActions()
    {
      m_leftStickAction?.Disable();
      m_rightStickAction?.Disable();
      m_driveAction?.Disable();
      m_steerAction?.Disable();
      m_resetAction?.Disable();
      m_startEpisodeAction?.Disable();
      m_stopEpisodeAction?.Disable();
    }

    private static float ReadAxis( InputAction action, float deadzone )
    {
      if ( action == null )
        return 0.0f;

      var value = action.ReadValue<float>();
      return Mathf.Abs( value ) >= deadzone ? value : 0.0f;
    }

    private static Vector2 ReadVector2( InputAction action, float deadzone )
    {
      if ( action == null )
        return Vector2.zero;

      var value = action.ReadValue<Vector2>();
      return value.sqrMagnitude >= deadzone * deadzone ? value : Vector2.zero;
    }
#endif
  }
}

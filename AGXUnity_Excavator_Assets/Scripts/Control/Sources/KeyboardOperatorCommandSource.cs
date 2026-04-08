using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public class KeyboardOperatorCommandSource : OperatorCommandSourceBehaviour
  {
    public override string SourceName => "Keyboard";

#if ENABLE_INPUT_SYSTEM
    private InputAction m_leftStickXAction;
    private InputAction m_leftStickYAction;
    private InputAction m_rightStickXAction;
    private InputAction m_rightStickYAction;
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
      m_leftStickXAction?.Dispose();
      m_leftStickYAction?.Dispose();
      m_rightStickXAction?.Dispose();
      m_rightStickYAction?.Dispose();
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
      command.LeftStickX = ReadAxis( m_leftStickXAction );
      command.LeftStickY = ReadAxis( m_leftStickYAction );
      command.RightStickX = ReadAxis( m_rightStickXAction );
      command.RightStickY = ReadAxis( m_rightStickYAction );
      command.Drive = ReadAxis( m_driveAction );
      command.Steer = ReadAxis( m_steerAction );
      command.ResetRequested = m_resetAction != null && m_resetAction.WasPressedThisFrame();
      command.StartEpisodeRequested = m_startEpisodeAction != null && m_startEpisodeAction.WasPressedThisFrame();
      command.StopEpisodeRequested = m_stopEpisodeAction != null && m_stopEpisodeAction.WasPressedThisFrame();
#else
      command.LeftStickX = ReadAxis( KeyCode.T, KeyCode.U );
      command.LeftStickY = ReadAxis( KeyCode.End, KeyCode.Home );
      command.RightStickX = ReadAxis( KeyCode.Delete, KeyCode.Insert );
      command.RightStickY = ReadAxis( KeyCode.PageDown, KeyCode.PageUp );
      command.Drive = ReadAxis( KeyCode.UpArrow, KeyCode.DownArrow );
      command.Steer = ReadAxis( KeyCode.RightArrow, KeyCode.LeftArrow );
      command.ResetRequested = Input.GetKeyDown( KeyCode.R );
      command.StartEpisodeRequested = Input.GetKeyDown( KeyCode.Return );
      command.StopEpisodeRequested = Input.GetKeyDown( KeyCode.Backspace );
#endif

      return command.ClampAxes();
    }

#if ENABLE_INPUT_SYSTEM
    private void InitializeActions()
    {
      if ( m_actionsInitialized )
        return;

      m_leftStickXAction = CreateAxisAction( "LeftStickX", "<Keyboard>/t", "<Keyboard>/u" );
      m_leftStickYAction = CreateAxisAction( "LeftStickY", "<Keyboard>/end", "<Keyboard>/home" );
      m_rightStickXAction = CreateAxisAction( "RightStickX", "<Keyboard>/delete", "<Keyboard>/insert" );
      m_rightStickYAction = CreateAxisAction( "RightStickY", "<Keyboard>/pageDown", "<Keyboard>/pageUp" );
      m_driveAction = CreateAxisAction( "Drive", "<Keyboard>/upArrow", "<Keyboard>/downArrow" );
      m_steerAction = CreateAxisAction( "Steer", "<Keyboard>/rightArrow", "<Keyboard>/leftArrow" );

      m_resetAction = new InputAction( "ResetEpisode", InputActionType.Button, "<Keyboard>/r" );
      m_startEpisodeAction = new InputAction( "StartEpisode", InputActionType.Button, "<Keyboard>/enter" );
      m_stopEpisodeAction = new InputAction( "StopEpisode", InputActionType.Button, "<Keyboard>/backspace" );

      m_actionsInitialized = true;
    }

    private void EnableActions()
    {
      m_leftStickXAction?.Enable();
      m_leftStickYAction?.Enable();
      m_rightStickXAction?.Enable();
      m_rightStickYAction?.Enable();
      m_driveAction?.Enable();
      m_steerAction?.Enable();
      m_resetAction?.Enable();
      m_startEpisodeAction?.Enable();
      m_stopEpisodeAction?.Enable();
    }

    private void DisableActions()
    {
      m_leftStickXAction?.Disable();
      m_leftStickYAction?.Disable();
      m_rightStickXAction?.Disable();
      m_rightStickYAction?.Disable();
      m_driveAction?.Disable();
      m_steerAction?.Disable();
      m_resetAction?.Disable();
      m_startEpisodeAction?.Disable();
      m_stopEpisodeAction?.Disable();
    }

    private static InputAction CreateAxisAction( string actionName, string negativeBinding, string positiveBinding )
    {
      var action = new InputAction( actionName, InputActionType.Value );
      action.AddCompositeBinding( "1DAxis" )
            .With( "Negative", negativeBinding )
            .With( "Positive", positiveBinding );
      return action;
    }

    private static float ReadAxis( InputAction action )
    {
      return action == null ? 0.0f : action.ReadValue<float>();
    }
#else
    private static float ReadAxis( KeyCode negative, KeyCode positive )
    {
      float value = 0.0f;
      if ( Input.GetKey( negative ) )
        value -= 1.0f;
      if ( Input.GetKey( positive ) )
        value += 1.0f;
      return value;
    }
#endif
  }
}

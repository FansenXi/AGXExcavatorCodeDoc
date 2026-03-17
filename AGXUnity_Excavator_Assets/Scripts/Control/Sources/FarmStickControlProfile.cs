using System;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public enum FarmStickHandedness
  {
    Right,
    Left
  }

  public enum FarmStickAxisBindingMode
  {
    SingleAxis,
    TwoButtonComposite
  }

  public enum FarmStickControlModeSwitchBehavior
  {
    Toggle,
    Hold
  }

  [Serializable]
  public sealed class FarmStickAxisBinding
  {
    [SerializeField]
    private string m_displayName = "Axis";

    [SerializeField]
    private bool m_enabled = true;

    [SerializeField]
    private FarmStickAxisBindingMode m_mode = FarmStickAxisBindingMode.SingleAxis;

    [SerializeField]
    private string m_controlPath = string.Empty;

    [SerializeField]
    private string m_negativeControlPath = string.Empty;

    [SerializeField]
    private string m_positiveControlPath = string.Empty;

    [SerializeField]
    private bool m_invert = false;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_deadzone = 0.05f;

    [SerializeField]
    private float m_rawMin = -1.0f;

    [SerializeField]
    private float m_rawMax = 1.0f;

    [SerializeField]
    private float m_center = 0.0f;

    public string DisplayName => m_displayName;
    public bool Enabled => m_enabled;
    public FarmStickAxisBindingMode Mode => m_mode;
    public string ControlPath => m_controlPath;
    public string NegativeControlPath => m_negativeControlPath;
    public string PositiveControlPath => m_positiveControlPath;
    public bool Invert => m_invert;
    public float Deadzone => m_deadzone;
    public float RawMin => m_rawMin;
    public float RawMax => m_rawMax;
    public float Center => m_center;

    public static FarmStickAxisBinding CreateSingleAxis( string displayName,
                                                         string controlPath,
                                                         bool invert = false,
                                                         float deadzone = 0.05f,
                                                         float rawMin = -1.0f,
                                                         float rawMax = 1.0f,
                                                         float center = 0.0f )
    {
      return new FarmStickAxisBinding
      {
        m_displayName = displayName,
        m_enabled = true,
        m_mode = FarmStickAxisBindingMode.SingleAxis,
        m_controlPath = controlPath,
        m_negativeControlPath = string.Empty,
        m_positiveControlPath = string.Empty,
        m_invert = invert,
        m_deadzone = deadzone,
        m_rawMin = rawMin,
        m_rawMax = rawMax,
        m_center = center
      };
    }

    public static FarmStickAxisBinding CreateTwoButtonComposite( string displayName,
                                                                 string negativeControlPath,
                                                                 string positiveControlPath,
                                                                 bool invert = false,
                                                                 float deadzone = 0.0f,
                                                                 float rawMin = -1.0f,
                                                                 float rawMax = 1.0f,
                                                                 float center = 0.0f )
    {
      return new FarmStickAxisBinding
      {
        m_displayName = displayName,
        m_enabled = true,
        m_mode = FarmStickAxisBindingMode.TwoButtonComposite,
        m_controlPath = string.Empty,
        m_negativeControlPath = negativeControlPath,
        m_positiveControlPath = positiveControlPath,
        m_invert = invert,
        m_deadzone = deadzone,
        m_rawMin = rawMin,
        m_rawMax = rawMax,
        m_center = center
      };
    }

    public static FarmStickAxisBinding CreateDisabled( string displayName )
    {
      return new FarmStickAxisBinding
      {
        m_displayName = displayName,
        m_enabled = false
      };
    }
  }

  [Serializable]
  public sealed class FarmStickButtonBinding
  {
    [SerializeField]
    private string m_displayName = "Button";

    [SerializeField]
    private bool m_enabled = true;

    [SerializeField]
    private string m_controlPath = string.Empty;

    public string DisplayName => m_displayName;
    public bool Enabled => m_enabled;
    public string ControlPath => m_controlPath;

    public static FarmStickButtonBinding Create( string displayName, string controlPath, bool enabled = true )
    {
      return new FarmStickButtonBinding
      {
        m_displayName = displayName,
        m_enabled = enabled,
        m_controlPath = controlPath
      };
    }

    public static FarmStickButtonBinding CreateDisabled( string displayName )
    {
      return new FarmStickButtonBinding
      {
        m_displayName = displayName,
        m_enabled = false,
        m_controlPath = string.Empty
      };
    }
  }

  [CreateAssetMenu( fileName = "FarmStickControlProfile", menuName = "AGX Excavator/Input/FarmStick Control Profile" )]
  public sealed class FarmStickControlProfile : ScriptableObject
  {
    [SerializeField]
    private string m_displayName = "FarmStick Excavator Right Default";

    [SerializeField]
    [TextArea( 4, 10 )]
    private string m_setupNotes = string.Empty;

    [SerializeField]
    private string m_manufacturerContains = "Thrustmaster";

    [SerializeField]
    private string m_productContains = "SimTask FarmStick";

    [SerializeField]
    private FarmStickHandedness m_handedness = FarmStickHandedness.Right;

    [SerializeField]
    private FarmStickAxisBinding m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "Stick/x" );

    [SerializeField]
    private FarmStickAxisBinding m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "Stick/y" );

    [SerializeField]
    private FarmStickAxisBinding m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "RotateX" );

    [SerializeField]
    private FarmStickAxisBinding m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "RotateY" );

    [SerializeField]
    private FarmStickAxisBinding m_drive = FarmStickAxisBinding.CreateSingleAxis( "Drive / Left Rocker", "Throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );

    [SerializeField]
    private FarmStickAxisBinding m_steer = FarmStickAxisBinding.CreateSingleAxis( "Steer / Right Rocker", "Rudder", false, 0.02f, -1.0f, 1.0f, 0.0f );

    [SerializeField]
    private FarmStickButtonBinding m_controlModeSwitch = FarmStickButtonBinding.Create( "Stick Mode Switch", "Unknown" );

    [SerializeField]
    private FarmStickControlModeSwitchBehavior m_controlModeSwitchBehavior = FarmStickControlModeSwitchBehavior.Toggle;

    [SerializeField]
    private FarmStickButtonBinding m_resetEpisode = FarmStickButtonBinding.CreateDisabled( "Reset Episode" );

    [SerializeField]
    private FarmStickButtonBinding m_startEpisode = FarmStickButtonBinding.CreateDisabled( "Start Episode" );

    [SerializeField]
    private FarmStickButtonBinding m_stopEpisode = FarmStickButtonBinding.CreateDisabled( "Stop Episode" );

    public string DisplayName => string.IsNullOrWhiteSpace( m_displayName ) ? name : m_displayName;
    public string SetupNotes => m_setupNotes;
    public string ManufacturerContains => m_manufacturerContains;
    public string ProductContains => m_productContains;
    public FarmStickHandedness Handedness => m_handedness;
    public FarmStickAxisBinding LeftStickX => m_leftStickX;
    public FarmStickAxisBinding LeftStickY => m_leftStickY;
    public FarmStickAxisBinding RightStickX => m_rightStickX;
    public FarmStickAxisBinding RightStickY => m_rightStickY;
    public FarmStickAxisBinding Drive => m_drive;
    public FarmStickAxisBinding Steer => m_steer;
    public FarmStickButtonBinding ControlModeSwitch => m_controlModeSwitch;
    public FarmStickControlModeSwitchBehavior ControlModeSwitchBehavior => m_controlModeSwitchBehavior;
    public FarmStickButtonBinding ResetEpisode => m_resetEpisode;
    public FarmStickButtonBinding StartEpisode => m_startEpisode;
    public FarmStickButtonBinding StopEpisode => m_stopEpisode;

    private void Reset()
    {
      ApplyDefaultExcavatorLayout( m_handedness );
    }

    private void OnEnable()
    {
      EnsureFields();
      if ( string.IsNullOrWhiteSpace( m_setupNotes ) )
        SetDefaultSetupNotes();
    }

    private void OnValidate()
    {
      EnsureFields();
    }

    [ContextMenu( "Apply Right-Hand Excavator Defaults" )]
    private void ApplyRightHandDefaults()
    {
      ApplyDefaultExcavatorLayout( FarmStickHandedness.Right );
    }

    [ContextMenu( "Apply Left-Hand Excavator Defaults" )]
    private void ApplyLeftHandDefaults()
    {
      ApplyDefaultExcavatorLayout( FarmStickHandedness.Left );
    }

    public void ApplyDefaultExcavatorLayout( FarmStickHandedness handedness )
    {
      m_handedness = handedness;
      m_displayName = handedness == FarmStickHandedness.Right ?
                      "FarmStick Excavator Right Default" :
                      "FarmStick Excavator Left Default";
      m_manufacturerContains = "Thrustmaster";
      m_productContains = "SimTask FarmStick";
      m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "Stick/x" );
      m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "Stick/y" );
      m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "RotateX" );
      m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "RotateY" );
      m_drive = FarmStickAxisBinding.CreateSingleAxis( "Drive / Left Rocker", "Throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );
      m_steer = FarmStickAxisBinding.CreateSingleAxis( "Steer / Right Rocker", "Rudder", false, 0.02f, -1.0f, 1.0f, 0.0f );
      m_controlModeSwitch = FarmStickButtonBinding.Create( "Stick Mode Switch", "Unknown" );
      m_controlModeSwitchBehavior = FarmStickControlModeSwitchBehavior.Toggle;
      m_resetEpisode = FarmStickButtonBinding.CreateDisabled( "Reset Episode" );
      m_startEpisode = FarmStickButtonBinding.CreateDisabled( "Start Episode" );
      m_stopEpisode = FarmStickButtonBinding.CreateDisabled( "Stop Episode" );
      SetDefaultSetupNotes();
    }

    private void EnsureFields()
    {
      if ( m_leftStickX == null )
        m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "Stick/x" );
      if ( m_leftStickY == null )
        m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "Stick/y" );
      if ( m_rightStickX == null )
        m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "RotateX" );
      if ( m_rightStickY == null )
        m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "RotateY" );
      if ( m_drive == null )
        m_drive = FarmStickAxisBinding.CreateSingleAxis( "Drive / Left Rocker", "Throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );
      if ( m_steer == null )
        m_steer = FarmStickAxisBinding.CreateSingleAxis( "Steer / Right Rocker", "Rudder", false, 0.02f, -1.0f, 1.0f, 0.0f );
      if ( m_controlModeSwitch == null )
        m_controlModeSwitch = FarmStickButtonBinding.Create( "Stick Mode Switch", "Unknown" );
      if ( m_resetEpisode == null )
        m_resetEpisode = FarmStickButtonBinding.CreateDisabled( "Reset Episode" );
      if ( m_startEpisode == null )
        m_startEpisode = FarmStickButtonBinding.CreateDisabled( "Start Episode" );
      if ( m_stopEpisode == null )
        m_stopEpisode = FarmStickButtonBinding.CreateDisabled( "Stop Episode" );
    }

    private void SetDefaultSetupNotes()
    {
      m_setupNotes =
        "Template profile for Thrustmaster SimTask FarmStick.\n" +
        "Official hardware mapping exposes 33 buttons and 8 axes: Axis 1/2 main stick, Axis 3 twist, Axis 4/5 mini-stick, Axis 6/7/8 thumb controls.\n" +
        "This Linux default profile uses the control names currently reported by Unity Input System: Stick/x, Stick/y, RotateX, RotateY, Throttle and Rudder.\n" +
        "If your platform reports different control names, use the diagnostics menu to inspect the live controls and retarget the bindings.\n" +
        "The default excavator travel mapping uses the two rocker axes in Work mode: Axis 7 (Throttle) for Drive and Axis 8 (Rudder) for Steer.\n" +
        "Switch the FarmStick to Work mode if you want analog rocker axes; in Drive mode those controls become buttons 27-30 and Linux may not expose them individually.\n" +
        "The FarmStick MODE button is official button 31. On Linux it may appear as an Unknown button or may not be exposed; this profile defaults the software stick-mode switch to Unknown so you can test that path quickly.\n" +
        "When the stick-mode switch is active, the main stick and mini-stick roles are swapped so the main stick can drive boom/bucket instead of swing/stick.\n" +
        "Episode buttons are disabled by default because Unity's Linux joystick layout does not expose the FarmStick's official button 19-33 numbering directly; bind them manually to the actual control names reported by the diagnostics menu if needed.\n" +
        "Run Thrustmaster hardware calibration first; use this profile only for residual deadzone, inversion and range tuning.";
    }
  }
}

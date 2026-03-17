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
    private FarmStickAxisBinding m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "stick/x" );

    [SerializeField]
    private FarmStickAxisBinding m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "stick/y" );

    [SerializeField]
    private FarmStickAxisBinding m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "rx" );

    [SerializeField]
    private FarmStickAxisBinding m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "ry" );

    [SerializeField]
    private FarmStickAxisBinding m_drive = FarmStickAxisBinding.CreateSingleAxis( "Hand Throttle", "throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );

    [SerializeField]
    private FarmStickAxisBinding m_steer = FarmStickAxisBinding.CreateSingleAxis( "Thumbwheel", "slider", false, 0.02f, -1.0f, 1.0f, 0.0f );

    [SerializeField]
    private FarmStickButtonBinding m_resetEpisode = FarmStickButtonBinding.Create( "Reset Episode", "button1" );

    [SerializeField]
    private FarmStickButtonBinding m_startEpisode = FarmStickButtonBinding.Create( "Start Episode", "button2" );

    [SerializeField]
    private FarmStickButtonBinding m_stopEpisode = FarmStickButtonBinding.Create( "Stop Episode", "button3" );

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
      m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "stick/x" );
      m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "stick/y" );
      m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "rx" );
      m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "ry" );
      m_drive = FarmStickAxisBinding.CreateSingleAxis( "Hand Throttle", "throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );
      m_steer = FarmStickAxisBinding.CreateSingleAxis( "Thumbwheel", "slider", false, 0.02f, -1.0f, 1.0f, 0.0f );
      m_resetEpisode = FarmStickButtonBinding.Create( "Reset Episode", "button1" );
      m_startEpisode = FarmStickButtonBinding.Create( "Start Episode", "button2" );
      m_stopEpisode = FarmStickButtonBinding.Create( "Stop Episode", "button3" );
      SetDefaultSetupNotes();
    }

    private void EnsureFields()
    {
      if ( m_leftStickX == null )
        m_leftStickX = FarmStickAxisBinding.CreateSingleAxis( "Main Stick X", "stick/x" );
      if ( m_leftStickY == null )
        m_leftStickY = FarmStickAxisBinding.CreateSingleAxis( "Main Stick Y", "stick/y" );
      if ( m_rightStickX == null )
        m_rightStickX = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick X", "rx" );
      if ( m_rightStickY == null )
        m_rightStickY = FarmStickAxisBinding.CreateSingleAxis( "Mini-stick Y", "ry" );
      if ( m_drive == null )
        m_drive = FarmStickAxisBinding.CreateSingleAxis( "Hand Throttle", "throttle", false, 0.02f, -1.0f, 1.0f, 0.0f );
      if ( m_steer == null )
        m_steer = FarmStickAxisBinding.CreateSingleAxis( "Thumbwheel", "slider", false, 0.02f, -1.0f, 1.0f, 0.0f );
      if ( m_resetEpisode == null )
        m_resetEpisode = FarmStickButtonBinding.Create( "Reset Episode", "button1" );
      if ( m_startEpisode == null )
        m_startEpisode = FarmStickButtonBinding.Create( "Start Episode", "button2" );
      if ( m_stopEpisode == null )
        m_stopEpisode = FarmStickButtonBinding.Create( "Stop Episode", "button3" );
    }

    private void SetDefaultSetupNotes()
    {
      m_setupNotes =
        "Template profile for Thrustmaster SimTask FarmStick.\n" +
        "Use Unity Input Debugger on the target machine to confirm the actual control paths exposed by the device.\n" +
        "Defaults assume generic joystick naming for the main stick and common HID names for extra axes.\n" +
        "If the thumbwheel is not exposed as a continuous axis, change Steer to TwoButtonComposite and bind the rocker pair instead.\n" +
        "If mini-stick paths differ, update RightStickX/RightStickY without changing runtime code.\n" +
        "Run Thrustmaster hardware calibration first; use this profile only for residual deadzone, inversion and range tuning.";
    }
  }
}

using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public enum ExcavatorJoystickPattern
  {
    ISO,
    SAE
  }

  [System.Serializable]
  public class ExcavatorCommandInterpreter
  {
    [SerializeField]
    private ExcavatorJoystickPattern m_joystickPattern = ExcavatorJoystickPattern.ISO;

    [SerializeField]
    private float m_boomScale = 0.3f;

    [SerializeField]
    private float m_bucketScale = 0.7f;

    [SerializeField]
    private float m_stickScale = -0.7f;

    [SerializeField]
    private float m_swingScale = 0.6f;

    [SerializeField]
    private float m_swingDeadZone = 0.3f;

    public ExcavatorJoystickPattern JoystickPattern => m_joystickPattern;

    public string LayoutDescription => m_joystickPattern == ExcavatorJoystickPattern.ISO ?
                                       "ISO: left joystick = swing/stick, right joystick = boom/bucket, rockers = left/right tracks" :
                                       "SAE: left joystick = swing/boom, right joystick = bucket/stick, rockers = left/right tracks";

    public ExcavatorActuationCommand Interpret( OperatorCommand command )
    {
      var actuation = new ExcavatorActuationCommand
      {
        Drive = command.Drive,
        Steer = command.Steer
      };

      switch ( m_joystickPattern ) {
        case ExcavatorJoystickPattern.SAE:
          actuation.Boom = -command.LeftStickY * m_boomScale;
          actuation.Bucket = command.RightStickX * m_bucketScale;
          actuation.Stick = command.RightStickY * m_stickScale;
          actuation.Swing = command.LeftStickX * m_swingScale;
          break;

        case ExcavatorJoystickPattern.ISO:
        default:
          actuation.Boom = -command.RightStickY * m_boomScale;
          actuation.Bucket = command.RightStickX * m_bucketScale;
          actuation.Stick = command.LeftStickY * m_stickScale;
          actuation.Swing = command.LeftStickX * m_swingScale;
          break;
      }

      if ( Mathf.Abs( actuation.Swing ) < m_swingDeadZone )
        actuation.Swing = 0.0f;

      var leftTrack = Mathf.Clamp( actuation.Drive - actuation.Steer, -1.0f, 1.0f );
      var rightTrack = Mathf.Clamp( actuation.Drive + actuation.Steer, -1.0f, 1.0f );
      actuation.Throttle = Mathf.Max( Mathf.Abs( leftTrack ), Mathf.Abs( rightTrack ) );
      return actuation.ClampAxes();
    }
  }
}

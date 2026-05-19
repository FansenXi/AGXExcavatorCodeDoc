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
    private float m_boomScale = 0.2f;

    [SerializeField]
    // Keep bucket scale negative so right-stick left maps to bucket curl-in,
    // which matches the expected ISO/SAE excavator convention.
    private float m_bucketScale = -0.45f;

    [SerializeField]
    private float m_stickScale = -0.45f;

    [SerializeField]
    private float m_swingScale = 0.4f;

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
          actuation.Boom = -command.LeftStickY * GetAxisDirection( m_boomScale, 1.0f );
          actuation.Bucket = command.RightStickX * GetAxisDirection( m_bucketScale, -1.0f );
          actuation.Stick = command.RightStickY * GetAxisDirection( m_stickScale, -1.0f );
          actuation.Swing = command.LeftStickX * GetAxisDirection( m_swingScale, 1.0f );
          break;

        case ExcavatorJoystickPattern.ISO:
        default:
          actuation.Boom = -command.RightStickY * GetAxisDirection( m_boomScale, 1.0f );
          actuation.Bucket = command.RightStickX * GetAxisDirection( m_bucketScale, -1.0f );
          actuation.Stick = command.LeftStickY * GetAxisDirection( m_stickScale, -1.0f );
          actuation.Swing = command.LeftStickX * GetAxisDirection( m_swingScale, 1.0f );
          break;
      }

      if ( Mathf.Abs( actuation.Swing ) < m_swingDeadZone )
        actuation.Swing = 0.0f;

      var leftTrack = Mathf.Clamp( actuation.Drive - actuation.Steer, -1.0f, 1.0f );
      var rightTrack = Mathf.Clamp( actuation.Drive + actuation.Steer, -1.0f, 1.0f );
      actuation.Throttle = Mathf.Max( Mathf.Abs( leftTrack ), Mathf.Abs( rightTrack ) );
      return actuation.ClampAxes();
    }

    private static float GetAxisDirection( float configuredScale, float defaultSign )
    {
      if ( Mathf.Abs( configuredScale ) > 1.0e-5f )
        return Mathf.Sign( configuredScale );

      return Mathf.Approximately( defaultSign, 0.0f ) ? 1.0f : Mathf.Sign( defaultSign );
    }
  }
}

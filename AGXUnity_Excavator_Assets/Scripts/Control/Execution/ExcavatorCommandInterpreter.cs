using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  [System.Serializable]
  public class ExcavatorCommandInterpreter
  {
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

    public ExcavatorActuationCommand Interpret( OperatorCommand command )
    {
      var actuation = new ExcavatorActuationCommand
      {
        Boom = command.RightStickY * m_boomScale,
        Bucket = command.RightStickX * m_bucketScale,
        Stick = command.LeftStickY * m_stickScale,
        Swing = command.LeftStickX * m_swingScale,
        Drive = command.Drive,
        Steer = command.Steer
      };

      if ( Mathf.Abs( actuation.Swing ) < m_swingDeadZone )
        actuation.Swing = 0.0f;

      actuation.Throttle = Mathf.Abs( actuation.Drive ) + Mathf.Abs( actuation.Steer ) > 0.0f ? 1.0f : 0.0f;
      return actuation.ClampAxes();
    }
  }
}

using System.Globalization;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  [System.Serializable]
  public struct OperatorCommand
  {
    public float LeftStickX;
    public float LeftStickY;
    public float RightStickX;
    public float RightStickY;
    public float Drive;
    public float Steer;

    public bool ResetRequested;
    public bool StartEpisodeRequested;
    public bool StopEpisodeRequested;

    public static OperatorCommand Zero => new OperatorCommand();

    public OperatorCommand ClampAxes()
    {
      LeftStickX = Mathf.Clamp( LeftStickX, -1.0f, 1.0f );
      LeftStickY = Mathf.Clamp( LeftStickY, -1.0f, 1.0f );
      RightStickX = Mathf.Clamp( RightStickX, -1.0f, 1.0f );
      RightStickY = Mathf.Clamp( RightStickY, -1.0f, 1.0f );
      Drive = Mathf.Clamp( Drive, -1.0f, 1.0f );
      Steer = Mathf.Clamp( Steer, -1.0f, 1.0f );
      return this;
    }

    public OperatorCommand WithoutEpisodeSignals()
    {
      ResetRequested = false;
      StartEpisodeRequested = false;
      StopEpisodeRequested = false;
      return this;
    }

    public string ToCompactString()
    {
      return string.Format(
        CultureInfo.InvariantCulture,
        "LX {0:+0.00;-0.00;0.00}  LY {1:+0.00;-0.00;0.00}  RX {2:+0.00;-0.00;0.00}  RY {3:+0.00;-0.00;0.00}  D {4:+0.00;-0.00;0.00}  S {5:+0.00;-0.00;0.00}",
        LeftStickX,
        LeftStickY,
        RightStickX,
        RightStickY,
        Drive,
        Steer );
    }
  }
}

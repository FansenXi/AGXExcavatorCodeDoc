using System.Globalization;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  [System.Serializable]
  public struct ExcavatorActuationCommand
  {
    public float Boom;
    public float Bucket;
    public float Stick;
    public float Swing;
    public float Drive;
    public float Steer;
    public float Throttle;

    public static ExcavatorActuationCommand Zero => new ExcavatorActuationCommand();

    public ExcavatorActuationCommand ClampAxes()
    {
      Boom = Mathf.Clamp( Boom, -1.0f, 1.0f );
      Bucket = Mathf.Clamp( Bucket, -1.0f, 1.0f );
      Stick = Mathf.Clamp( Stick, -1.0f, 1.0f );
      Swing = Mathf.Clamp( Swing, -1.0f, 1.0f );
      Drive = Mathf.Clamp( Drive, -1.0f, 1.0f );
      Steer = Mathf.Clamp( Steer, -1.0f, 1.0f );
      Throttle = Mathf.Clamp01( Throttle );
      return this;
    }

    public string ToCompactString()
    {
      return string.Format(
        CultureInfo.InvariantCulture,
        "Boom {0:+0.00;-0.00;0.00}  Bucket {1:+0.00;-0.00;0.00}  Stick {2:+0.00;-0.00;0.00}  Swing {3:+0.00;-0.00;0.00}  Drive {4:+0.00;-0.00;0.00}  Steer {5:+0.00;-0.00;0.00}  Thr {6:0.00}",
        Boom,
        Bucket,
        Stick,
        Swing,
        Drive,
        Steer,
        Throttle );
    }
  }
}

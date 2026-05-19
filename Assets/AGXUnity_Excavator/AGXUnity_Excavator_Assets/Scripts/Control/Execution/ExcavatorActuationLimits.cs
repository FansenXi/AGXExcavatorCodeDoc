using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  [System.Serializable]
  public class ExcavatorActuationLimits
  {
    [System.Serializable]
    public class AxisResponseLimit
    {
      [SerializeField]
      [Min( 0.0f )]
      private float m_maxSpeed = 1.0f;

      [SerializeField]
      private float m_maxAccelerationOverride = -1.0f;

      public AxisResponseLimit()
      {
      }

      public AxisResponseLimit( float maxSpeed )
      {
        m_maxSpeed = Mathf.Max( 0.0f, maxSpeed );
      }

      public float MaxSpeed
      {
        get => Mathf.Max( 0.0f, m_maxSpeed );
        set => m_maxSpeed = Mathf.Max( 0.0f, value );
      }

      public bool HasAccelerationOverride => m_maxAccelerationOverride >= 0.0f;

      public float MaxAccelerationOverride
      {
        get => m_maxAccelerationOverride;
        set => m_maxAccelerationOverride = Mathf.Max( 0.0f, value );
      }

      public float ResolveMaxAcceleration( float fallback )
      {
        return HasAccelerationOverride ?
               Mathf.Max( 0.0f, m_maxAccelerationOverride ) :
               Mathf.Max( 0.0f, fallback );
      }
    }

    [SerializeField]
    private float m_maxRotationalAcceleration = 0.65f;

    [SerializeField]
    private float m_maxLinearAcceleration = 0.45f;

    [SerializeField]
    private AxisResponseLimit m_swing = new AxisResponseLimit( 0.4f );

    [SerializeField]
    private AxisResponseLimit m_boom = new AxisResponseLimit( 0.2f );

    [SerializeField]
    private AxisResponseLimit m_stick = new AxisResponseLimit( 0.45f );

    [SerializeField]
    private AxisResponseLimit m_bucket = new AxisResponseLimit( 0.45f );

    public float MaxRotationalAcceleration => m_maxRotationalAcceleration;
    public float MaxLinearAcceleration => m_maxLinearAcceleration;
    public AxisResponseLimit Swing => m_swing ?? ( m_swing = new AxisResponseLimit() );
    public AxisResponseLimit Boom => m_boom ?? ( m_boom = new AxisResponseLimit() );
    public AxisResponseLimit Stick => m_stick ?? ( m_stick = new AxisResponseLimit() );
    public AxisResponseLimit Bucket => m_bucket ?? ( m_bucket = new AxisResponseLimit() );
  }
}

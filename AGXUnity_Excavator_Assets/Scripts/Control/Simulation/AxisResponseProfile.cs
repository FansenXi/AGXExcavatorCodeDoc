using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Simulation
{
  [System.Serializable]
  public class AxisResponseProfile
  {
    [SerializeField]
    private float m_deadZone = 0.05f;

    [SerializeField]
    private float m_attackRate = 4.0f;

    [SerializeField]
    private float m_releaseRate = 6.0f;

    [SerializeField]
    private float m_recenterRate = 7.0f;

    [SerializeField]
    private float m_exponent = 1.0f;

    [SerializeField]
    private bool m_invert = false;

    public float Apply( float rawValue, float currentValue, float deltaTime )
    {
      var targetValue = Remap( rawValue );
      var rate = Mathf.Abs( targetValue ) < 1.0e-4f ?
                 m_recenterRate :
                 ( Mathf.Abs( targetValue ) > Mathf.Abs( currentValue ) ? m_attackRate : m_releaseRate );

      return Mathf.MoveTowards( currentValue, targetValue, Mathf.Max( rate, 0.0f ) * Mathf.Max( deltaTime, 0.0f ) );
    }

    private float Remap( float rawValue )
    {
      var value = m_invert ? -rawValue : rawValue;
      var magnitude = Mathf.Abs( value );
      if ( magnitude <= m_deadZone )
        return 0.0f;

      var normalized = Mathf.InverseLerp( m_deadZone, 1.0f, magnitude );
      var curved = Mathf.Pow( normalized, Mathf.Max( m_exponent, 0.01f ) );
      return Mathf.Sign( value ) * curved;
    }
  }
}

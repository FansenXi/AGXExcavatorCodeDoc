using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  [System.Serializable]
  public class ExcavatorActuationLimits
  {
    [SerializeField]
    private float m_maxRotationalAcceleration = 1.0f;

    [SerializeField]
    private float m_maxLinearAcceleration = 0.7f;

    public float MaxRotationalAcceleration => m_maxRotationalAcceleration;
    public float MaxLinearAcceleration => m_maxLinearAcceleration;
  }
}

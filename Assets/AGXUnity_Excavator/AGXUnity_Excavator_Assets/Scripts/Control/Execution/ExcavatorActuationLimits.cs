using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  [System.Serializable]
  public class ExcavatorActuationLimits
  {
    [SerializeField]
    private float m_maxRotationalAcceleration = 0.65f;

    [SerializeField]
    private float m_maxLinearAcceleration = 0.45f;

    public float MaxRotationalAcceleration => m_maxRotationalAcceleration;
    public float MaxLinearAcceleration => m_maxLinearAcceleration;
  }
}

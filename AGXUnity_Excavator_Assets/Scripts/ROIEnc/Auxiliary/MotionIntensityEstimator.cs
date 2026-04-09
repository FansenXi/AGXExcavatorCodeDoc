using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary
{
  public sealed class MotionIntensityEstimator
  {
    private Vector3 m_lastPosition = Vector3.zero;
    private Quaternion m_lastRotation = Quaternion.identity;
    private float m_lastSampleTime = -1.0f;
    private bool m_hasSample = false;

    public float LastIntensity { get; private set; } = 0.0f;

    public float Update( Transform referenceTransform, float sampleTimeSeconds )
    {
      if ( referenceTransform == null ) {
        LastIntensity = 0.0f;
        return LastIntensity;
      }

      if ( !m_hasSample ) {
        m_lastPosition = referenceTransform.position;
        m_lastRotation = referenceTransform.rotation;
        m_lastSampleTime = sampleTimeSeconds;
        m_hasSample = true;
        LastIntensity = 0.0f;
        return LastIntensity;
      }

      var deltaTime = Mathf.Max( 1.0e-4f, sampleTimeSeconds - m_lastSampleTime );
      var linearSpeed = Vector3.Distance( m_lastPosition, referenceTransform.position ) / deltaTime;
      var angularSpeed = Quaternion.Angle( m_lastRotation, referenceTransform.rotation ) / deltaTime;

      m_lastPosition = referenceTransform.position;
      m_lastRotation = referenceTransform.rotation;
      m_lastSampleTime = sampleTimeSeconds;

      LastIntensity = Mathf.Clamp01( linearSpeed * 0.4f + angularSpeed / 180.0f );
      return LastIntensity;
    }
  }
}

using AGXUnity;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ExcavationMassTracker : ScriptComponent
{
  public AGXUnity.Model.DeformableTerrainShovel shovel;
  public AGXUnity.Model.DeformableTerrain m_terrain;

  [SerializeField]
  private Transform m_bucketMeasurementFrame = null;

  [SerializeField]
  private Vector3 m_bucketMeasurementCenterOffset = Vector3.zero;

  [SerializeField]
  private Vector3 m_bucketMeasurementExtraHalfExtents = new Vector3( 0.15f, 0.15f, 0.15f );

  [SerializeField]
  [Min( 0.0f )]
  private float m_handledAsParticleRigidBodyPadding = 0.1f;

  float m_excavatedMass = 0;
  float m_massInBucket = 0;
  float m_previousMassInBucket = 0;

  Text m_infoText;
  Transform m_cachedBucketMeasurementFrame = null;
  Bounds m_autoBucketLocalBounds = default;
  bool m_hasAutoBucketLocalBounds = false;

  // The current scene doesn't provide the particle/contact sensor that the
  // original excavated-volume pipeline depended on. Keep the excavated output
  // by accumulating positive changes in bucket load across the episode.
  public float ExcavatedMass => m_excavatedMass;
  public float MassInBucket => m_massInBucket;


  protected override bool Initialize()
  {
    var texts = GetComponentsInChildren<Text>();
    m_infoText = texts.FirstOrDefault( t => t.name == "Information" );
    ResetMeasurements();

    return base.Initialize();
  }

  public void ResetMeasurements()
  {
    m_excavatedMass = 0;
    m_massInBucket = ReadMassInBucket();
    m_previousMassInBucket = m_massInBucket;
    UpdateInfoText();
  }

  void Update()
  {
    m_massInBucket = ReadMassInBucket();
    m_excavatedMass += Mathf.Max( 0.0f, m_massInBucket - m_previousMassInBucket );
    m_previousMassInBucket = m_massInBucket;
    UpdateInfoText();
  }

  private float ReadMassInBucket()
  {
    var terrainDynamicMass = 0.0f;
    if ( m_terrain != null && m_terrain.Native != null && shovel != null && shovel.Native != null )
      terrainDynamicMass = (float)m_terrain.Native.getDynamicMass( shovel.Native );

    return terrainDynamicMass + ReadHandledAsParticleRigidBodyMassInBucket();
  }

  private float ReadHandledAsParticleRigidBodyMassInBucket()
  {
    if ( !TryGetBucketMeasurement( out var measurementCenter, out var measurementHalfExtents ) )
      return 0.0f;

    var excludedBody = shovel != null ? shovel.RigidBody : null;
    return HandledAsParticleRigidBodyMassUtility.SumMassInOrientedBox( ResolveBucketMeasurementFrame(),
                                                                      measurementCenter,
                                                                      measurementHalfExtents,
                                                                      excludedBody,
                                                                      m_handledAsParticleRigidBodyPadding );
  }

  private Transform ResolveBucketMeasurementFrame()
  {
    if ( m_bucketMeasurementFrame != null )
      return m_bucketMeasurementFrame;

    if ( shovel != null && shovel.RigidBody != null )
      return shovel.RigidBody.transform;

    return shovel != null ? shovel.transform : null;
  }

  private bool TryGetBucketMeasurement( out Vector3 measurementCenter, out Vector3 measurementHalfExtents )
  {
    measurementCenter = Vector3.zero;
    measurementHalfExtents = Vector3.zero;

    var measurementFrame = ResolveBucketMeasurementFrame();
    if ( measurementFrame == null )
      return false;

    if ( m_cachedBucketMeasurementFrame != measurementFrame ) {
      m_cachedBucketMeasurementFrame = measurementFrame;
      m_hasAutoBucketLocalBounds = HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( measurementFrame, out m_autoBucketLocalBounds );
    }

    if ( !m_hasAutoBucketLocalBounds )
      return false;

    measurementCenter = m_autoBucketLocalBounds.center + m_bucketMeasurementCenterOffset;
    measurementHalfExtents = m_autoBucketLocalBounds.extents + m_bucketMeasurementExtraHalfExtents;

    return measurementHalfExtents.x > 0.0f &&
           measurementHalfExtents.y > 0.0f &&
           measurementHalfExtents.z > 0.0f;
  }

  private void UpdateInfoText()
  {
    if ( m_infoText == null )
      return;

    string info = string.Format( "Mass in bucket: \t\t{0:f} kg\n", m_massInBucket );
    info += string.Format( "Excavated mass: \t{0:f} kg\n", m_excavatedMass );
    m_infoText.text = info;
  }
}

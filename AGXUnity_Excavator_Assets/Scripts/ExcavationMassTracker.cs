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

  [Header( "Target Distance Proxy" )]
  [SerializeField]
  private bool m_useDedicatedTargetDistanceProxy = true;

  [SerializeField]
  private Vector3 m_targetDistanceProxyCenterOffset = Vector3.zero;

  [SerializeField]
  private Vector3 m_targetDistanceProxyHalfExtentsScale = new Vector3( 0.4f, 0.35f, 0.4f );

  [SerializeField]
  private Vector3 m_targetDistanceProxyExtraHalfExtents = Vector3.zero;

  [SerializeField]
  private bool m_drawTargetDistanceProxyGizmo = true;

  [SerializeField]
  private Color m_targetDistanceProxyGizmoColor = new Color( 0.96f, 0.37f, 0.12f, 0.95f );

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
  public Transform BucketMeasurementFrame => ResolveBucketMeasurementFrame();


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

  private void OnDrawGizmosSelected()
  {
    if ( !m_drawTargetDistanceProxyGizmo )
      return;

    if ( !TryGetTargetDistanceProxyVolume( out var measurementFrame,
                                           out var measurementCenter,
                                           out var measurementHalfExtents ) )
      return;

    var previousColor = Gizmos.color;
    var previousMatrix = Gizmos.matrix;

    Gizmos.color = m_targetDistanceProxyGizmoColor;
    Gizmos.matrix = measurementFrame.localToWorldMatrix;
    Gizmos.DrawWireCube( measurementCenter, 2.0f * measurementHalfExtents );

    Gizmos.matrix = previousMatrix;
    Gizmos.color = previousColor;
  }

  [ContextMenu( "Reset Target Distance Proxy To Tight Default" )]
  private void ResetTargetDistanceProxyToTightDefault()
  {
    m_useDedicatedTargetDistanceProxy = true;
    m_targetDistanceProxyCenterOffset = Vector3.zero;
    m_targetDistanceProxyHalfExtentsScale = new Vector3( 0.4f, 0.35f, 0.4f );
    m_targetDistanceProxyExtraHalfExtents = Vector3.zero;
  }

  [ContextMenu( "Match Target Distance Proxy To Bucket Measurement" )]
  private void MatchTargetDistanceProxyToBucketMeasurement()
  {
    m_useDedicatedTargetDistanceProxy = true;
    m_targetDistanceProxyCenterOffset = m_bucketMeasurementCenterOffset;
    m_targetDistanceProxyHalfExtentsScale = Vector3.one;
    m_targetDistanceProxyExtraHalfExtents = Vector3.zero;
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

    if ( !TryGetAutoBucketLocalBounds( measurementFrame, out var autoBucketLocalBounds ) )
      return false;

    measurementCenter = autoBucketLocalBounds.center + m_bucketMeasurementCenterOffset;
    measurementHalfExtents = autoBucketLocalBounds.extents + m_bucketMeasurementExtraHalfExtents;

    return measurementHalfExtents.x > 0.0f &&
           measurementHalfExtents.y > 0.0f &&
           measurementHalfExtents.z > 0.0f;
  }

  public bool TryGetBucketMeasurementVolume( out Transform measurementFrame,
                                             out Vector3 measurementCenter,
                                             out Vector3 measurementHalfExtents )
  {
    measurementFrame = ResolveBucketMeasurementFrame();
    if ( measurementFrame == null )
    {
      measurementCenter = Vector3.zero;
      measurementHalfExtents = Vector3.zero;
      return false;
    }

    return TryGetBucketMeasurement( out measurementCenter, out measurementHalfExtents );
  }

  public bool TryGetTargetDistanceProxyVolume( out Transform measurementFrame,
                                               out Vector3 measurementCenter,
                                               out Vector3 measurementHalfExtents )
  {
    measurementFrame = ResolveBucketMeasurementFrame();
    if ( measurementFrame == null ) {
      measurementCenter = Vector3.zero;
      measurementHalfExtents = Vector3.zero;
      return false;
    }

    if ( !m_useDedicatedTargetDistanceProxy )
      return TryGetBucketMeasurement( out measurementCenter, out measurementHalfExtents );

    if ( !TryGetAutoBucketLocalBounds( measurementFrame, out var autoBucketLocalBounds ) )
      return TryGetBucketMeasurement( out measurementCenter, out measurementHalfExtents );

    var baseHalfExtents = autoBucketLocalBounds.extents + m_bucketMeasurementExtraHalfExtents;
    var nonNegativeScale = new Vector3(
      Mathf.Max( 0.0f, m_targetDistanceProxyHalfExtentsScale.x ),
      Mathf.Max( 0.0f, m_targetDistanceProxyHalfExtentsScale.y ),
      Mathf.Max( 0.0f, m_targetDistanceProxyHalfExtentsScale.z ) );

    measurementCenter = autoBucketLocalBounds.center + m_targetDistanceProxyCenterOffset;
    measurementHalfExtents = Vector3.Scale( baseHalfExtents, nonNegativeScale ) + m_targetDistanceProxyExtraHalfExtents;
    measurementHalfExtents = new Vector3(
      Mathf.Max( 0.01f, measurementHalfExtents.x ),
      Mathf.Max( 0.01f, measurementHalfExtents.y ),
      Mathf.Max( 0.01f, measurementHalfExtents.z ) );

    return true;
  }

  public static bool TryGetBucketMeasurementVolumeForFrame( Transform measurementFrame,
                                                            out Vector3 measurementCenter,
                                                            out Vector3 measurementHalfExtents )
  {
    measurementCenter = Vector3.zero;
    measurementHalfExtents = Vector3.zero;
    if ( measurementFrame == null )
      return false;

    var trackers = Object.FindObjectsByType<ExcavationMassTracker>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( trackers == null || trackers.Length == 0 )
      return false;

    foreach ( var tracker in trackers ) {
      if ( tracker == null || tracker.BucketMeasurementFrame != measurementFrame )
        continue;

      return tracker.TryGetBucketMeasurementVolume( out _, out measurementCenter, out measurementHalfExtents );
    }

    return false;
  }

  public static bool TryGetTargetDistanceProxyVolumeForFrame( Transform measurementFrame,
                                                              out Vector3 measurementCenter,
                                                              out Vector3 measurementHalfExtents )
  {
    measurementCenter = Vector3.zero;
    measurementHalfExtents = Vector3.zero;
    if ( measurementFrame == null )
      return false;

    var trackers = Object.FindObjectsByType<ExcavationMassTracker>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( trackers == null || trackers.Length == 0 )
      return false;

    foreach ( var tracker in trackers ) {
      if ( tracker == null || tracker.BucketMeasurementFrame != measurementFrame )
        continue;

      return tracker.TryGetTargetDistanceProxyVolume( out _, out measurementCenter, out measurementHalfExtents );
    }

    return false;
  }

  private bool TryGetAutoBucketLocalBounds( Transform measurementFrame, out Bounds autoBucketLocalBounds )
  {
    autoBucketLocalBounds = default;
    if ( measurementFrame == null )
      return false;

    if ( m_cachedBucketMeasurementFrame != measurementFrame ) {
      m_cachedBucketMeasurementFrame = measurementFrame;
      m_hasAutoBucketLocalBounds = HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( measurementFrame, out m_autoBucketLocalBounds );
    }

    if ( !m_hasAutoBucketLocalBounds )
      return false;

    autoBucketLocalBounds = m_autoBucketLocalBounds;
    return true;
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

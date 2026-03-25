using AGXUnity.Collide;
using AGXUnity.Model;
using AGXUnity.Utils;
using System;
using UnityEngine;

public class TruckBedMassSensor : TargetMassSensorBase
{
  [SerializeField]
  private string m_targetName = "TruckBed";

  [SerializeField]
  public DeformableTerrain m_terrain = null;

  [SerializeField]
  private MovableTerrain m_bedTerrain = null;

  [SerializeField]
  private Transform m_truckRoot = null;

  [SerializeField]
  private string m_truckRootName = "BedTruck";

  [SerializeField]
  private Transform m_bedTransform = null;

  [SerializeField]
  private string m_bedTransformName = "Bed";

  [SerializeField]
  private string m_bedTerrainName = "BedTerrain";

  [SerializeField]
  [Min( 0.05f )]
  private float m_measurementHeight = 1.2f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_measurementTopHeadroom = 0.8f;

  [SerializeField]
  private Vector3 m_localCenterOffset = Vector3.zero;

  [SerializeField]
  private Vector3 m_additionalHalfExtents = new Vector3( -0.1f, 0.0f, -0.1f );

  [SerializeField]
  [Min( 0.01f )]
  private float m_updateIntervalSeconds = 0.05f;

  [SerializeField]
  private bool m_disableBedTerrainObject = true;

  [SerializeField]
  private bool m_enableBedSupportBoxes = true;

  [SerializeField]
  private bool m_measureRelativeToReset = true;

  [SerializeField]
  private bool m_includeHandledAsParticleRigidBodies = true;

  [SerializeField]
  [Min( 0.0f )]
  private float m_handledAsParticleRigidBodyPadding = 0.1f;

  [SerializeField]
  private bool m_drawSensorBoundsGizmo = true;

  [Header( "Target Distance / Collision Shape Filter" )]
  [SerializeField]
  private string[] m_excludedHelperShapeNames = { "DumpFailureVolume", "TopFailureVolume" };

  private float m_massInBox = 0.0f;
  private float m_depositedMass = 0.0f;
  private float m_nextSampleTime = 0.0f;
  private float m_resetBaselineMassInBox = 0.0f;
  private Transform m_cachedBoundsTransform = null;
  private Bounds m_cachedBedLocalBounds = default;
  private bool m_hasCachedBedLocalBounds = false;
  private Transform m_cachedTargetDistanceBoundsTransform = null;
  private Bounds m_cachedTargetDistanceLocalBounds = default;
  private bool m_hasCachedTargetDistanceLocalBounds = false;

  public override string TargetName => string.IsNullOrWhiteSpace( m_targetName ) ? "TruckBed" : m_targetName;
  public override float MassInBox => m_massInBox;
  public override float DepositedMass => m_depositedMass;
  public override Shape[] GetCollisionShapes()
  {
    ResolveReferences();
    var collisionRoot = m_truckRoot != null ? m_truckRoot : m_bedTransform;
    if ( collisionRoot == null )
      return System.Array.Empty<Shape>();

    // For hard-collision penalty we want the whole truck hard body, not only
    // the bed measurement area. Keep excluding the disabled BedTerrain helper.
    var shapes = collisionRoot.GetComponentsInChildren<Shape>( true );
    if ( shapes == null || shapes.Length == 0 )
      return System.Array.Empty<Shape>();

    var filteredShapes = new System.Collections.Generic.List<Shape>( shapes.Length );
    var excludedRoot = m_bedTerrain != null ? m_bedTerrain.transform : null;

    foreach ( var shape in shapes ) {
      if ( shape == null || !shape.CollisionsEnabled )
        continue;

      if ( excludedRoot != null && shape.transform.IsChildOf( excludedRoot ) )
        continue;

      if ( IsExcludedHelperShape( shape ) )
        continue;

      if ( !filteredShapes.Contains( shape ) )
        filteredShapes.Add( shape );
    }

    return filteredShapes.ToArray();
  }

  protected override void OnAwake()
  {
    ResolveReferences();
    DisableBedTerrainObjectIfRequested();
    EnableBedSupportBoxesIfRequested();
  }

  protected override bool Initialize()
  {
    ResolveReferences();
    ResetMeasurements();

    return base.Initialize();
  }

  public override void ResetMeasurements()
  {
    ResolveReferences();

    var rawMassInBox = ReadRawMassInBox();
    m_resetBaselineMassInBox = m_measureRelativeToReset ? rawMassInBox : 0.0f;
    m_massInBox = NormalizeMeasuredMass( rawMassInBox );
    m_depositedMass = m_massInBox;
    m_nextSampleTime = Time.time + Mathf.Max( 0.01f, m_updateIntervalSeconds );
  }

  private void Update()
  {
    if ( !Application.isPlaying )
      return;

    if ( Time.time + 1.0e-5f < m_nextSampleTime )
      return;

    SampleMeasurements();
    m_nextSampleTime = Time.time + Mathf.Max( 0.01f, m_updateIntervalSeconds );
  }

  private void OnDrawGizmosSelected()
  {
    if ( !m_drawSensorBoundsGizmo )
      return;

    if ( !TryGetMeasurementVolume( out var measurementFrame, out var measurementCenterLocal, out var measurementHalfExtents ) )
      return;

    var previousColor = Gizmos.color;
    var previousMatrix = Gizmos.matrix;

    Gizmos.color = new Color( 0.18f, 0.74f, 0.96f, 1.0f );
    Gizmos.matrix = measurementFrame.localToWorldMatrix;
    Gizmos.DrawWireCube( measurementCenterLocal, 2.0f * measurementHalfExtents );

    Gizmos.matrix = previousMatrix;
    Gizmos.color = previousColor;
  }

  private void SampleMeasurements()
  {
    m_massInBox = NormalizeMeasuredMass( ReadRawMassInBox() );
    m_depositedMass = m_massInBox;
  }

  private float ReadRawMassInBox()
  {
    if ( !TryGetMeasurementVolume( out var measurementFrame, out var measurementCenterLocal, out var measurementHalfExtents ) )
      return 0.0f;

    // Disable the bed MovableTerrain before AGX initialization so dumped soil
    // remains dynamic particles. Aggregate those particles across all active
    // terrain instances without modifying the truck prefab.
    var totalMass = DeformableTerrainParticleMassUtility.SumMassInOrientedBox( measurementFrame,
                                                                               measurementCenterLocal,
                                                                               measurementHalfExtents,
                                                                               m_terrain );

    if ( m_includeHandledAsParticleRigidBodies )
      totalMass += HandledAsParticleRigidBodyMassUtility.SumMassInOrientedBox( measurementFrame,
                                                                               measurementCenterLocal,
                                                                               measurementHalfExtents,
                                                                               null,
                                                                               m_handledAsParticleRigidBodyPadding );

    return totalMass;
  }

  private float NormalizeMeasuredMass( float rawMassInBox )
  {
    return Mathf.Max( 0.0f, rawMassInBox - m_resetBaselineMassInBox );
  }

  public override bool TryGetMeasurementVolume( out Transform measurementFrame,
                                                out Vector3 measurementCenterLocal,
                                                out Vector3 measurementHalfExtents )
  {
    measurementFrame = null;
    measurementCenterLocal = Vector3.zero;
    measurementHalfExtents = Vector3.zero;

    ResolveReferences();
    if ( m_bedTransform == null )
      return false;

    RefreshCachedBedBounds();
    if ( !m_hasCachedBedLocalBounds )
      return false;

    measurementFrame = m_bedTransform;

    var bedBounds = m_cachedBedLocalBounds;
    var measurementBottomLocalY = bedBounds.min.y;
    var measurementTopLocalY = Mathf.Max( bedBounds.max.y + Mathf.Max( 0.0f, m_measurementTopHeadroom ),
                                          measurementBottomLocalY + Mathf.Max( 0.05f, m_measurementHeight ) );
    var measurementCenterLocalY = 0.5f * ( measurementBottomLocalY + measurementTopLocalY );
    var measurementHalfExtentY = 0.5f * ( measurementTopLocalY - measurementBottomLocalY );
    measurementCenterLocal = new Vector3(
      bedBounds.center.x,
      measurementCenterLocalY,
      bedBounds.center.z ) + m_localCenterOffset;
    measurementHalfExtents = new Vector3(
      Mathf.Max( 0.01f, bedBounds.extents.x + m_additionalHalfExtents.x ),
      Mathf.Max( 0.01f, measurementHalfExtentY + m_additionalHalfExtents.y ),
      Mathf.Max( 0.01f, bedBounds.extents.z + m_additionalHalfExtents.z ) );

    return measurementHalfExtents.x > 0.0f &&
           measurementHalfExtents.y > 0.0f &&
           measurementHalfExtents.z > 0.0f;
  }

  public override bool TryGetTargetDistanceVolume( out Transform measurementFrame,
                                                   out Vector3 measurementCenterLocal,
                                                   out Vector3 measurementHalfExtents )
  {
    measurementFrame = null;
    measurementCenterLocal = Vector3.zero;
    measurementHalfExtents = Vector3.zero;

    ResolveReferences();
    var collisionRoot = m_truckRoot != null ? m_truckRoot : m_bedTransform;
    if ( collisionRoot == null )
      return TryGetMeasurementVolume( out measurementFrame, out measurementCenterLocal, out measurementHalfExtents );

    if ( m_cachedTargetDistanceBoundsTransform != collisionRoot ) {
      m_cachedTargetDistanceBoundsTransform = collisionRoot;
      m_hasCachedTargetDistanceLocalBounds =
        TargetDistanceVolumeUtility.TryCalculateLocalBoxBounds( collisionRoot,
                                                                GetCollisionShapes(),
                                                                out m_cachedTargetDistanceLocalBounds );
    }

    if ( !m_hasCachedTargetDistanceLocalBounds )
      return TryGetMeasurementVolume( out measurementFrame, out measurementCenterLocal, out measurementHalfExtents );

    measurementFrame = collisionRoot;
    measurementCenterLocal = m_cachedTargetDistanceLocalBounds.center;
    measurementHalfExtents = new Vector3(
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.x ),
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.y ),
      Mathf.Max( 0.01f, m_cachedTargetDistanceLocalBounds.extents.z ) );
    return true;
  }

  private void RefreshCachedBedBounds()
  {
    if ( m_cachedBoundsTransform == m_bedTransform )
      return;

    m_cachedBoundsTransform = m_bedTransform;

    if ( m_bedTransform == null ) {
      m_hasCachedBedLocalBounds = false;
      m_cachedBedLocalBounds = default;
      return;
    }

    if ( TargetDistanceVolumeUtility.TryCalculateLocalBoxBounds( m_bedTransform, (Transform)null, out m_cachedBedLocalBounds ) ) {
      m_hasCachedBedLocalBounds = true;
      return;
    }

    m_hasCachedBedLocalBounds = HandledAsParticleRigidBodyMassUtility.TryCalculateLocalRendererBounds( m_bedTransform, out m_cachedBedLocalBounds );
  }

  private void ResolveReferences()
  {
    if ( m_terrain == null )
      m_terrain = FindObjectOfType<DeformableTerrain>();

    if ( m_truckRoot == null && !string.IsNullOrWhiteSpace( m_truckRootName ) )
      m_truckRoot = FindTransformByName( m_truckRootName );

    if ( m_bedTransform == null ) {
      if ( m_truckRoot != null )
        m_bedTransform = FindChildByName( m_truckRoot, m_bedTransformName );

      if ( m_bedTransform == null && !string.IsNullOrWhiteSpace( m_bedTransformName ) )
        m_bedTransform = FindTransformByName( m_bedTransformName );
    }

    if ( m_bedTerrain == null ) {
      if ( m_truckRoot != null ) {
        if ( !string.IsNullOrWhiteSpace( m_bedTerrainName ) ) {
          foreach ( var terrain in m_truckRoot.GetComponentsInChildren<MovableTerrain>( true ) ) {
            if ( terrain != null && terrain.name == m_bedTerrainName ) {
              m_bedTerrain = terrain;
              break;
            }
          }
        }

        if ( m_bedTerrain == null )
          m_bedTerrain = m_truckRoot.GetComponentInChildren<MovableTerrain>( true );
      }

      if ( m_bedTerrain == null && !string.IsNullOrWhiteSpace( m_bedTerrainName ) ) {
        var terrainTransform = FindTransformByName( m_bedTerrainName );
        if ( terrainTransform != null )
          m_bedTerrain = terrainTransform.GetComponent<MovableTerrain>();
      }
    }
  }

  private bool IsExcludedHelperShape( Shape shape )
  {
    if ( shape == null )
      return true;

    var shapeName = shape.transform != null ? shape.transform.name : shape.name;
    if ( string.IsNullOrWhiteSpace( shapeName ) )
      return false;

    if ( shapeName.IndexOf( "FailureVolume", StringComparison.OrdinalIgnoreCase ) >= 0 )
      return true;

    if ( m_excludedHelperShapeNames == null || m_excludedHelperShapeNames.Length == 0 )
      return false;

    foreach ( var excludedName in m_excludedHelperShapeNames ) {
      if ( string.IsNullOrWhiteSpace( excludedName ) )
        continue;

      if ( string.Equals( shapeName, excludedName, StringComparison.OrdinalIgnoreCase ) )
        return true;
    }

    return false;
  }

  private void DisableBedTerrainObjectIfRequested()
  {
    if ( !m_disableBedTerrainObject )
      return;

    ResolveReferences();
    if ( m_bedTerrain != null && m_bedTerrain.gameObject.activeSelf )
      m_bedTerrain.gameObject.SetActive( false );
  }

  private void EnableBedSupportBoxesIfRequested()
  {
    if ( !m_enableBedSupportBoxes )
      return;

    ResolveReferences();
    if ( m_bedTransform == null )
      return;

    foreach ( var box in m_bedTransform.GetComponentsInChildren<Box>( true ) ) {
      if ( box == null || box.CollisionsEnabled )
        continue;

      box.CollisionsEnabled = true;
    }
  }

  private static Transform FindTransformByName( string transformName )
  {
    if ( string.IsNullOrWhiteSpace( transformName ) )
      return null;

    var candidates = FindObjectsOfType<Transform>( true );
    foreach ( var candidate in candidates ) {
      if ( candidate != null && candidate.name == transformName )
        return candidate;
    }

    return null;
  }

  private static Transform FindChildByName( Transform root, string childName )
  {
    if ( root == null || string.IsNullOrWhiteSpace( childName ) )
      return null;

    foreach ( var child in root.GetComponentsInChildren<Transform>( true ) ) {
      if ( child != null && child.name == childName )
        return child;
    }

    return null;
  }
}

using AGXUnity.Collide;
using UnityEngine;
using UnityEngine.Rendering;

public class DigAreaMeasurement : MonoBehaviour
{
  private const string DefaultDigAreaRootName = "AGXUnity.RigidBody.DigArea";

  [SerializeField]
  private Transform m_digAreaRoot = null;

  [SerializeField]
  private Box m_digAreaBox = null;

  [SerializeField]
  private string m_digAreaRootName = DefaultDigAreaRootName;

  [SerializeField]
  private MeshRenderer m_fillRenderer = null;

  [SerializeField]
  private LineRenderer m_contourRenderer = null;

  [SerializeField]
  private Color m_fillColor = new Color( 1.0f, 0.55f, 0.20f, 0.12f );

  [SerializeField]
  private Color m_contourColor = new Color( 1.0f, 0.45f, 0.05f, 0.98f );

  [SerializeField]
  [Min( 0.001f )]
  private float m_contourWidth = 0.08f;

  [SerializeField]
  [Min( 0.0f )]
  private float m_contourHeightOffset = 0.015f;

  private Material m_runtimeFillMaterial = null;
  private Material m_runtimeContourMaterial = null;

  private void OnEnable()
  {
    ResolveReferences();
    ApplyVisuals();
  }

  private void LateUpdate()
  {
    ResolveReferences();
    RefreshContourGeometry();
  }

  private void OnDestroy()
  {
    DestroyRuntimeMaterials();
  }

  public static DigAreaMeasurement FindOrCreateInScene()
  {
    var existingMeasurements = Object.FindObjectsByType<DigAreaMeasurement>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( existingMeasurements != null ) {
      foreach ( var existingMeasurement in existingMeasurements ) {
        if ( existingMeasurement == null )
          continue;

        existingMeasurement.ResolveReferences();
        return existingMeasurement;
      }
    }

    var digAreaRoot = FindDigAreaRoot( DefaultDigAreaRootName );
    if ( digAreaRoot == null )
      return null;

    var measurement = digAreaRoot.GetComponent<DigAreaMeasurement>();
    if ( measurement == null )
      measurement = digAreaRoot.gameObject.AddComponent<DigAreaMeasurement>();

    measurement.ResolveReferences();
    return measurement;
  }

  public void ResolveReferences()
  {
    if ( m_digAreaRoot == null )
      m_digAreaRoot = FindDigAreaRoot( m_digAreaRootName );

    if ( m_digAreaRoot == null ) {
      m_digAreaBox = null;
      m_fillRenderer = null;
      return;
    }

    if ( m_digAreaBox == null || !m_digAreaBox.transform.IsChildOf( m_digAreaRoot ) )
      m_digAreaBox = ResolveDigAreaBox( m_digAreaRoot );

    if ( m_fillRenderer == null || !m_fillRenderer.transform.IsChildOf( m_digAreaRoot ) )
      m_fillRenderer = ResolveFillRenderer( m_digAreaRoot );

    if ( m_contourRenderer == null || !m_contourRenderer.transform.IsChildOf( m_digAreaRoot ) )
      m_contourRenderer = ResolveContourRenderer( m_digAreaRoot );

    ApplyVisuals();
  }

  public bool TryMeasureBucketDigAreaMetrics( Transform bucketReference,
                                              out float minDistanceMeters,
                                              out float bucketDepthBelowPlaneMeters )
  {
    minDistanceMeters = -1.0f;
    bucketDepthBelowPlaneMeters = 0.0f;

    ResolveReferences();
    if ( bucketReference == null || m_digAreaBox == null )
      return false;

    if ( !BucketTargetDistanceMeasurementUtility.TryGetMeasurementBox( bucketReference, out var bucketBox ) )
      return false;

    var digAreaBox = new OrientedMeasurementBox
    {
      Frame = m_digAreaBox.transform,
      CenterLocal = Vector3.zero,
      HalfExtents = m_digAreaBox.HalfExtents
    };
    if ( !digAreaBox.IsValid )
      return false;

    minDistanceMeters = BucketTargetDistanceMeasurementUtility.MeasureApproximateDistance( bucketBox, digAreaBox );

    var digPlaneWorldY = m_digAreaBox.transform.position.y;
    bucketDepthBelowPlaneMeters = Mathf.Max( 0.0f, digPlaneWorldY - bucketBox.GetWorldMinY() );
    return true;
  }

  private static Transform FindDigAreaRoot( string digAreaRootName )
  {
    if ( string.IsNullOrWhiteSpace( digAreaRootName ) )
      return null;

    var allTransforms = Object.FindObjectsByType<Transform>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( allTransforms == null )
      return null;

    foreach ( var candidate in allTransforms ) {
      if ( candidate != null && candidate.name == digAreaRootName )
        return candidate;
    }

    return null;
  }

  private static Box ResolveDigAreaBox( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var boxes = digAreaRoot.GetComponentsInChildren<Box>( true );
    if ( boxes == null || boxes.Length == 0 )
      return null;

    Box bestBox = null;
    var bestHalfHeight = float.PositiveInfinity;
    foreach ( var candidate in boxes ) {
      if ( candidate == null )
        continue;

      var halfExtents = candidate.HalfExtents;
      if ( halfExtents.x <= 0.0f || halfExtents.y <= 0.0f || halfExtents.z <= 0.0f )
        continue;

      if ( halfExtents.y < bestHalfHeight ) {
        bestBox = candidate;
        bestHalfHeight = halfExtents.y;
      }
    }

    return bestBox;
  }

  private void ApplyVisuals()
  {
    ApplyFillVisual();
    ApplyContourVisual();
    RefreshContourGeometry();
  }

  private void ApplyFillVisual()
  {
    if ( m_fillRenderer == null )
      return;

    if ( m_runtimeFillMaterial == null ) {
      var fillShader = FindFirstAvailableShader(
        "Legacy Shaders/Transparent/Diffuse",
        "Sprites/Default",
        "Unlit/Transparent",
        "Standard" );
      if ( fillShader == null )
        return;

      m_runtimeFillMaterial = new Material( fillShader )
      {
        name = "DigAreaFillRuntime"
      };
      ConfigureStandardTransparentMaterial( m_runtimeFillMaterial );
    }

    if ( m_runtimeFillMaterial.HasProperty( "_Color" ) )
      m_runtimeFillMaterial.color = m_fillColor;

    m_runtimeFillMaterial.renderQueue = 3000;
    m_fillRenderer.sharedMaterial = m_runtimeFillMaterial;
    m_fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
    m_fillRenderer.receiveShadows = false;
  }

  private void ApplyContourVisual()
  {
    if ( m_contourRenderer == null )
      return;

    if ( m_runtimeContourMaterial == null ) {
      var contourShader = FindFirstAvailableShader(
        "Sprites/Default",
        "Legacy Shaders/Particles/Alpha Blended Premultiply",
        "Unlit/Color" );
      if ( contourShader == null )
        return;

      m_runtimeContourMaterial = new Material( contourShader )
      {
        name = "DigAreaContourRuntime"
      };
      if ( m_runtimeContourMaterial.HasProperty( "_Color" ) )
        m_runtimeContourMaterial.color = m_contourColor;
      m_runtimeContourMaterial.renderQueue = 3100;
    }

    m_contourRenderer.sharedMaterial = m_runtimeContourMaterial;
    m_contourRenderer.loop = true;
    m_contourRenderer.useWorldSpace = true;
    m_contourRenderer.positionCount = 4;
    m_contourRenderer.startWidth = m_contourWidth;
    m_contourRenderer.endWidth = m_contourWidth;
    m_contourRenderer.startColor = m_contourColor;
    m_contourRenderer.endColor = m_contourColor;
    m_contourRenderer.shadowCastingMode = ShadowCastingMode.Off;
    m_contourRenderer.receiveShadows = false;
    m_contourRenderer.textureMode = LineTextureMode.Stretch;
    m_contourRenderer.numCornerVertices = 2;
    m_contourRenderer.numCapVertices = 2;
    m_contourRenderer.sortingOrder = 10;
    m_contourRenderer.enabled = true;
  }

  private void RefreshContourGeometry()
  {
    if ( m_digAreaBox == null || m_contourRenderer == null )
      return;

    var halfExtents = m_digAreaBox.HalfExtents;
    if ( halfExtents.x <= 0.0f || halfExtents.z <= 0.0f )
      return;

    m_contourRenderer.SetPosition( 0, DigAreaPlaneCornerWorld( -halfExtents.x, -halfExtents.z ) );
    m_contourRenderer.SetPosition( 1, DigAreaPlaneCornerWorld( -halfExtents.x,  halfExtents.z ) );
    m_contourRenderer.SetPosition( 2, DigAreaPlaneCornerWorld(  halfExtents.x,  halfExtents.z ) );
    m_contourRenderer.SetPosition( 3, DigAreaPlaneCornerWorld(  halfExtents.x, -halfExtents.z ) );
  }

  private Vector3 DigAreaPlaneCornerWorld( float localX, float localZ )
  {
    var worldPoint = m_digAreaBox.transform.TransformPoint( new Vector3( localX, 0.0f, localZ ) );
    worldPoint += Vector3.up * m_contourHeightOffset;
    return worldPoint;
  }

  private static MeshRenderer ResolveFillRenderer( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var meshRenderers = digAreaRoot.GetComponentsInChildren<MeshRenderer>( true );
    if ( meshRenderers == null || meshRenderers.Length == 0 )
      return null;

    foreach ( var meshRenderer in meshRenderers ) {
      if ( meshRenderer != null )
        return meshRenderer;
    }

    return null;
  }

  private static LineRenderer ResolveContourRenderer( Transform digAreaRoot )
  {
    if ( digAreaRoot == null )
      return null;

    var existingContour = digAreaRoot.Find( "DigAreaContour" );
    if ( existingContour != null ) {
      var existingLineRenderer = existingContour.GetComponent<LineRenderer>();
      if ( existingLineRenderer != null )
        return existingLineRenderer;
    }

    var contourObject = new GameObject( "DigAreaContour" );
    contourObject.transform.SetParent( digAreaRoot, false );
    return contourObject.AddComponent<LineRenderer>();
  }

  private static Shader FindFirstAvailableShader( params string[] shaderNames )
  {
    if ( shaderNames == null || shaderNames.Length == 0 )
      return null;

    foreach ( var shaderName in shaderNames ) {
      if ( string.IsNullOrWhiteSpace( shaderName ) )
        continue;

      var shader = Shader.Find( shaderName );
      if ( shader != null )
        return shader;
    }

    return null;
  }

  private static void ConfigureStandardTransparentMaterial( Material material )
  {
    if ( material == null || !material.HasProperty( "_Mode" ) )
      return;

    material.SetFloat( "_Mode", 3.0f );
    material.SetInt( "_SrcBlend", (int)BlendMode.SrcAlpha );
    material.SetInt( "_DstBlend", (int)BlendMode.OneMinusSrcAlpha );
    material.SetInt( "_ZWrite", 0 );
    material.DisableKeyword( "_ALPHATEST_ON" );
    material.EnableKeyword( "_ALPHABLEND_ON" );
    material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
  }

  private void DestroyRuntimeMaterials()
  {
    DestroyRuntimeMaterial( ref m_runtimeFillMaterial );
    DestroyRuntimeMaterial( ref m_runtimeContourMaterial );
  }

  private static void DestroyRuntimeMaterial( ref Material material )
  {
    if ( material == null )
      return;

    if ( Application.isPlaying )
      Object.Destroy( material );
    else
      Object.DestroyImmediate( material );

    material = null;
  }
}

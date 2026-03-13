using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [RequireComponent( typeof( Camera ) )]
  public class TrackedCameraWindow : MonoBehaviour
  {
    public enum AnchorMode
    {
      BucketReference,
      MachineRoot,
      CustomTransform
    }

    [SerializeField]
    private string m_viewName = "Bucket View";

    [SerializeField]
    private bool m_visible = true;

    [SerializeField]
    private Rect m_windowRect = new Rect( 496.0f, 16.0f, 360.0f, 232.0f );

    [SerializeField]
    [Min( 128 )]
    private int m_textureWidth = 512;

    [SerializeField]
    [Min( 72 )]
    private int m_textureHeight = 288;

    [SerializeField]
    private bool m_allowWindowDrag = true;

    [SerializeField]
    private AnchorMode m_anchorMode = AnchorMode.BucketReference;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Transform m_customTarget = null;

    [SerializeField]
    private Vector3 m_localCameraOffset = new Vector3( 0.0f, 1.2f, -0.4f );

    [SerializeField]
    private Vector3 m_localLookAtPoint = new Vector3( 0.0f, -0.1f, 1.2f );

    [SerializeField]
    private bool m_followTargetRotation = true;

    private Camera m_camera = null;
    private RenderTexture m_renderTexture = null;
    private Transform m_runtimeTarget = null;

    public string ViewName => string.IsNullOrWhiteSpace( m_viewName ) ? gameObject.name : m_viewName;

    public bool IsVisible
    {
      get => m_visible;
      set
      {
        if ( m_visible == value )
          return;

        m_visible = value;
        UpdateCameraState();
      }
    }

    public void ToggleVisible()
    {
      IsVisible = !IsVisible;
    }

    private void Awake()
    {
      ResolveReferences();
      EnsureRenderTexture();
      UpdateTrackingPose();
      UpdateCameraState();
    }

    private void OnEnable()
    {
      ResolveReferences();
      EnsureRenderTexture();
      UpdateTrackingPose();
      UpdateCameraState();
    }

    private void LateUpdate()
    {
      ResolveReferences();
      EnsureRenderTexture();
      UpdateTrackingPose();
      UpdateCameraState();
    }

    private void OnDisable()
    {
      if ( m_camera != null ) {
        m_camera.enabled = false;
        m_camera.targetTexture = null;
      }
    }

    private void OnDestroy()
    {
      ReleaseRenderTexture();
    }

    private void OnGUI()
    {
      if ( !m_visible )
        return;

      m_windowRect = GUI.Window( GetInstanceID(), m_windowRect, DrawWindowContents, ViewName );
    }

    private void ResolveReferences()
    {
      if ( m_camera == null )
        m_camera = GetComponent<Camera>();

      var audioListener = GetComponent<AudioListener>();
      if ( audioListener != null && audioListener.enabled )
        audioListener.enabled = false;

      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
    }

    private void UpdateTrackingPose()
    {
      m_runtimeTarget = ResolveTarget();
      if ( m_runtimeTarget == null )
        return;

      var cameraPosition = m_followTargetRotation ?
                           m_runtimeTarget.TransformPoint( m_localCameraOffset ) :
                           m_runtimeTarget.position + m_localCameraOffset;
      var lookAtPoint = m_followTargetRotation ?
                        m_runtimeTarget.TransformPoint( m_localLookAtPoint ) :
                        m_runtimeTarget.position + m_localLookAtPoint;

      transform.position = cameraPosition;

      var viewDirection = lookAtPoint - cameraPosition;
      if ( viewDirection.sqrMagnitude < 1.0e-6f )
        viewDirection = m_followTargetRotation ? m_runtimeTarget.forward : Vector3.forward;

      var upDirection = m_followTargetRotation ? m_runtimeTarget.up : Vector3.up;
      transform.rotation = Quaternion.LookRotation( viewDirection.normalized, upDirection );
    }

    private Transform ResolveTarget()
    {
      switch ( m_anchorMode ) {
        case AnchorMode.CustomTransform:
          return m_customTarget;
        case AnchorMode.MachineRoot:
          return m_machineController != null ? m_machineController.transform : null;
        case AnchorMode.BucketReference:
        default:
          return m_machineController != null ? m_machineController.BucketReference : null;
      }
    }

    private void EnsureRenderTexture()
    {
      if ( m_camera == null )
        return;

      var width = Mathf.Max( 128, m_textureWidth );
      var height = Mathf.Max( 72, m_textureHeight );
      if ( m_renderTexture != null &&
           m_renderTexture.width == width &&
           m_renderTexture.height == height ) {
        if ( m_camera.targetTexture != m_renderTexture )
          m_camera.targetTexture = m_renderTexture;
        return;
      }

      ReleaseRenderTexture();

      m_renderTexture = new RenderTexture( width, height, 24, RenderTextureFormat.ARGB32 )
      {
        name = $"{name}_{ViewName}_WindowRT"
      };
      m_renderTexture.Create();
      m_camera.targetTexture = m_renderTexture;
    }

    private void ReleaseRenderTexture()
    {
      if ( m_camera != null && m_camera.targetTexture == m_renderTexture )
        m_camera.targetTexture = null;

      if ( m_renderTexture == null )
        return;

      if ( m_renderTexture.IsCreated() )
        m_renderTexture.Release();

      Destroy( m_renderTexture );
      m_renderTexture = null;
    }

    private void UpdateCameraState()
    {
      if ( m_camera == null )
        return;

      m_camera.enabled = m_visible && m_runtimeTarget != null && m_renderTexture != null;
      if ( m_camera.enabled && m_camera.targetTexture != m_renderTexture )
        m_camera.targetTexture = m_renderTexture;
    }

    private void DrawWindowContents( int windowId )
    {
      var closeButtonRect = new Rect( m_windowRect.width - 24.0f, 2.0f, 20.0f, 18.0f );
      if ( GUI.Button( closeButtonRect, "x" ) ) {
        IsVisible = false;
        return;
      }

      var contentRect = new Rect(
        8.0f,
        24.0f,
        Mathf.Max( 32.0f, m_windowRect.width - 16.0f ),
        Mathf.Max( 32.0f, m_windowRect.height - 32.0f ) );

      if ( m_renderTexture != null && m_runtimeTarget != null )
        GUI.DrawTexture( contentRect, m_renderTexture, ScaleMode.ScaleToFit, false );
      else
        GUI.Label( contentRect, "Camera target not resolved." );

      if ( m_allowWindowDrag )
        GUI.DragWindow( new Rect( 0.0f, 0.0f, Mathf.Max( 0.0f, m_windowRect.width - 28.0f ), 20.0f ) );
    }
  }
}

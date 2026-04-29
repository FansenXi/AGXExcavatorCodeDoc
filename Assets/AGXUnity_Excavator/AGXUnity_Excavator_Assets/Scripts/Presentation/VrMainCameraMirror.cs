using Unity.XR.CoreUtils;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [DisallowMultipleComponent]
  public class VrMainCameraMirror : MonoBehaviour
  {
    [SerializeField]
    private Camera m_sourceCamera = null;

    [SerializeField]
    private XROrigin m_xrOrigin = null;

    [SerializeField]
    private Camera m_xrCamera = null;

    public void Configure( Camera sourceCamera, XROrigin xrOrigin, Camera xrCamera )
    {
      m_sourceCamera = sourceCamera;
      m_xrOrigin = xrOrigin;
      m_xrCamera = xrCamera;
      enabled = HasRequiredReferences();
      SyncAll();
    }

    private void OnEnable()
    {
      Application.onBeforeRender += HandleBeforeRender;
      SyncAll();
    }

    private void OnDisable()
    {
      Application.onBeforeRender -= HandleBeforeRender;
    }

    private void LateUpdate()
    {
      SyncAll();
    }

    private void HandleBeforeRender()
    {
      SyncOriginTransform();
    }

    private bool HasRequiredReferences()
    {
      return m_sourceCamera != null && m_xrOrigin != null && m_xrCamera != null;
    }

    private void SyncAll()
    {
      if ( !HasRequiredReferences() )
        return;

      SyncOriginTransform();
      SyncCameraRenderingState();
    }

    private void SyncOriginTransform()
    {
      var originTransform = m_xrOrigin.Origin != null ? m_xrOrigin.Origin.transform : m_xrOrigin.transform;
      originTransform.SetPositionAndRotation( m_sourceCamera.transform.position, m_sourceCamera.transform.rotation );
    }

    private void SyncCameraRenderingState()
    {
      m_xrCamera.clearFlags = m_sourceCamera.clearFlags;
      m_xrCamera.backgroundColor = m_sourceCamera.backgroundColor;
      m_xrCamera.nearClipPlane = m_sourceCamera.nearClipPlane;
      m_xrCamera.farClipPlane = m_sourceCamera.farClipPlane;
      m_xrCamera.cullingMask = m_sourceCamera.cullingMask;
      m_xrCamera.allowHDR = m_sourceCamera.allowHDR;
      m_xrCamera.allowMSAA = m_sourceCamera.allowMSAA;
      m_xrCamera.orthographic = m_sourceCamera.orthographic;
      m_xrCamera.orthographicSize = m_sourceCamera.orthographicSize;
      m_xrCamera.depthTextureMode = m_sourceCamera.depthTextureMode;
      m_xrCamera.useOcclusionCulling = m_sourceCamera.useOcclusionCulling;
    }
  }
}

using UnityEngine;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [DefaultExecutionOrder( 1001 )]
  public class TrackedHmdPoseDriver : MonoBehaviour
  {
    [SerializeField]
    private bool m_trackPosition = true;

    [SerializeField]
    private bool m_trackRotation = true;

    [SerializeField]
    private bool m_recenterOnEnable = true;

    [SerializeField]
    private KeyCode m_resetPoseKey = KeyCode.F2;

#if ENABLE_INPUT_SYSTEM
    private InputAction m_hmdPositionAction;
    private InputAction m_hmdRotationAction;
    private InputAction m_hmdTrackingStateAction;
    private InputAction m_resetPoseAction;
#endif

    private bool m_hasPositionBaseline = true;
    private bool m_hasRotationBaseline = true;
    private Vector3 m_positionBaseline = Vector3.zero;
    private Quaternion m_rotationBaseline = Quaternion.identity;
    private Camera m_cameraComponent = null;
    private bool m_autoXrCameraTrackingDisabled = false;

    private void Awake()
    {
      m_cameraComponent = GetComponent<Camera>();
      DisableAutoXrCameraTracking();
    }

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
      EnsureInputActions();
      m_hmdPositionAction.Enable();
      m_hmdRotationAction.Enable();
      m_hmdTrackingStateAction.Enable();
      m_resetPoseAction.Enable();
#endif

      if ( m_recenterOnEnable )
        ClearBaselines();

      DisableAutoXrCameraTracking();
      Application.onBeforeRender += HandleBeforeRender;
      ApplyTrackedPose();
    }

    private void OnDisable()
    {
      Application.onBeforeRender -= HandleBeforeRender;
      RestoreAutoXrCameraTracking();

#if ENABLE_INPUT_SYSTEM
      if ( m_hmdPositionAction != null )
        m_hmdPositionAction.Disable();

      if ( m_hmdRotationAction != null )
        m_hmdRotationAction.Disable();

      if ( m_hmdTrackingStateAction != null )
        m_hmdTrackingStateAction.Disable();

      if ( m_resetPoseAction != null )
        m_resetPoseAction.Disable();
#endif
    }

    private void OnDestroy()
    {
      RestoreAutoXrCameraTracking();

#if ENABLE_INPUT_SYSTEM
      m_hmdPositionAction?.Dispose();
      m_hmdRotationAction?.Dispose();
      m_hmdTrackingStateAction?.Dispose();
      m_resetPoseAction?.Dispose();
#endif
    }

    private void LateUpdate()
    {
      ApplyTrackedPose();
    }

    private void HandleBeforeRender()
    {
      ApplyTrackedPose();
    }

    private void ApplyTrackedPose()
    {
      var resetPressed = WasResetPosePressed();
      if ( resetPressed )
        ClearBaselines();

      if ( !TryReadTrackedPose( out var trackedPosition, out var trackedRotation, out var trackingState ) ) {
        if ( resetPressed )
          ResetLocalPose();

        return;
      }

      if ( m_trackRotation && ( trackingState & InputTrackingState.Rotation ) != 0 ) {
        if ( !m_hasRotationBaseline ) {
          m_rotationBaseline = trackedRotation;
          m_hasRotationBaseline = true;
        }

        transform.localRotation = NormalizeRotation( Quaternion.Inverse( m_rotationBaseline ) * trackedRotation );
      }
      else if ( resetPressed || !m_trackRotation ) {
        transform.localRotation = Quaternion.identity;
      }

      if ( m_trackPosition && ( trackingState & InputTrackingState.Position ) != 0 ) {
        if ( !m_hasPositionBaseline ) {
          m_positionBaseline = trackedPosition;
          m_hasPositionBaseline = true;
        }

        transform.localPosition = trackedPosition - m_positionBaseline;
      }
      else if ( resetPressed || !m_trackPosition ) {
        transform.localPosition = Vector3.zero;
      }
    }

    private void ClearBaselines()
    {
      m_hasPositionBaseline = false;
      m_hasRotationBaseline = false;
      m_positionBaseline = Vector3.zero;
      m_rotationBaseline = Quaternion.identity;
    }

    private void DisableAutoXrCameraTracking()
    {
#if ENABLE_VR
      if ( m_autoXrCameraTrackingDisabled )
        return;

      if ( m_cameraComponent == null )
        m_cameraComponent = GetComponent<Camera>();

      if ( m_cameraComponent == null ||
           m_cameraComponent.stereoTargetEye == StereoTargetEyeMask.None )
        return;

      XRDevice.DisableAutoXRCameraTracking( m_cameraComponent, true );
      m_autoXrCameraTrackingDisabled = true;
#endif
    }

    private void RestoreAutoXrCameraTracking()
    {
#if ENABLE_VR
      if ( !m_autoXrCameraTrackingDisabled || m_cameraComponent == null )
        return;

      XRDevice.DisableAutoXRCameraTracking( m_cameraComponent, false );
      m_autoXrCameraTrackingDisabled = false;
#endif
    }

    private void ResetLocalPose()
    {
      transform.localPosition = Vector3.zero;
      transform.localRotation = Quaternion.identity;
    }

    private static Quaternion NormalizeRotation( Quaternion rotation )
    {
      var magnitude = Mathf.Sqrt(
        rotation.x * rotation.x +
        rotation.y * rotation.y +
        rotation.z * rotation.z +
        rotation.w * rotation.w );

      if ( magnitude < 1.0e-6f )
        return Quaternion.identity;

      return new Quaternion(
        rotation.x / magnitude,
        rotation.y / magnitude,
        rotation.z / magnitude,
        rotation.w / magnitude );
    }

    private bool TryReadTrackedPose( out Vector3 trackedPosition, out Quaternion trackedRotation, out InputTrackingState trackingState )
    {
      trackedPosition = Vector3.zero;
      trackedRotation = Quaternion.identity;
      trackingState = InputTrackingState.None;

#if ENABLE_INPUT_SYSTEM
      if ( m_hmdTrackingStateAction == null ||
           m_hmdTrackingStateAction.controls.Count == 0 )
        return false;

      trackingState = (InputTrackingState)m_hmdTrackingStateAction.ReadValue<int>();
      if ( trackingState == InputTrackingState.None )
        return false;

      if ( m_hmdRotationAction != null &&
           m_hmdRotationAction.controls.Count > 0 &&
           ( trackingState & InputTrackingState.Rotation ) != 0 ) {
        trackedRotation = NormalizeRotation( m_hmdRotationAction.ReadValue<Quaternion>() );
      }

      if ( m_hmdPositionAction != null &&
           m_hmdPositionAction.controls.Count > 0 &&
           ( trackingState & InputTrackingState.Position ) != 0 ) {
        trackedPosition = m_hmdPositionAction.ReadValue<Vector3>();
      }

      return true;
#else
      trackedPosition = InputTracking.GetLocalPosition( XRNode.CenterEye );
      trackedRotation = InputTracking.GetLocalRotation( XRNode.CenterEye );
      trackingState = InputTrackingState.Position | InputTrackingState.Rotation;
      return true;
#endif
    }

    private bool WasResetPosePressed()
    {
#if ENABLE_INPUT_SYSTEM
      if ( m_resetPoseAction == null )
        return false;

      return m_resetPoseKey == KeyCode.F2 ?
             m_resetPoseAction.WasPressedThisFrame() :
             Input.GetKeyDown( m_resetPoseKey );
#else
      return Input.GetKeyDown( m_resetPoseKey );
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private void EnsureInputActions()
    {
      if ( m_hmdPositionAction == null )
        m_hmdPositionAction = new InputAction( "HmdPosition", InputActionType.PassThrough, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3" );

      if ( m_hmdRotationAction == null )
        m_hmdRotationAction = new InputAction( "HmdRotation", InputActionType.PassThrough, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion" );

      if ( m_hmdTrackingStateAction == null )
        m_hmdTrackingStateAction = new InputAction( "HmdTrackingState", InputActionType.PassThrough, "<XRHMD>/trackingState", expectedControlType: "Integer" );

      if ( m_resetPoseAction == null )
        m_resetPoseAction = new InputAction( "ResetPose", binding: "<Keyboard>/f2" );
    }
#endif
  }
}

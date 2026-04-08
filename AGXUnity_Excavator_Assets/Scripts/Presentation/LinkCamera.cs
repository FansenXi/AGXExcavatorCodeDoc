using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.XR;
#endif

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [RequireComponent(typeof(Camera))]
  public class LinkCamera : MonoBehaviour
  {
    [SerializeField]
    public bool Enabled = true;

#if ENABLE_INPUT_SYSTEM
  private InputAction m_enableAction;
  private InputAction m_lookAction;
  private InputAction m_lookModifierAction;
  private InputAction m_hmdPositionAction;
  private InputAction m_hmdRotationAction;
  private InputAction m_hmdTrackingStateAction;
  private InputAction m_resetHmdOffsetAction;
#else
  [SerializeField]
  public KeyCode toggleEnableKey = KeyCode.F1;

  [SerializeField]
  private KeyCode m_lookModifierKey = KeyCode.Mouse1;
#endif

  public Vector3 Forward = new Vector3(0.0f, 0.0f, 1.0f);
  public float Distance = 0.5f;
  public Vector3 RelativePosition;

  [SerializeField]
  private bool m_allowRuntimeLook = true;

  [SerializeField]
  private float m_cursorSensitivity = 0.015f;

  [SerializeField]
  private bool m_invertY = false;

  [SerializeField]
  private float m_pitchDegrees = 0.0f;

  [SerializeField]
  private float m_yawDegrees = 0.0f;

  [SerializeField]
  private float m_minPitchDegrees = -80.0f;

  [SerializeField]
  private float m_maxPitchDegrees = 80.0f;

  [SerializeField]
  private float m_fieldOfView = 0.0f;

  [SerializeField]
  private GameObject m_follow_object = null;

#if ENABLE_INPUT_SYSTEM
  [SerializeField]
  private bool m_enableHmdRotationOffset = true;

  [SerializeField]
  private KeyCode m_resetHmdOffsetKey = KeyCode.F2;

  [SerializeField]
  private float m_hmdPitchOffsetDegrees = 0.0f;

  [SerializeField]
  private float m_hmdYawOffsetDegrees = 0.0f;

  [SerializeField]
  private float m_hmdRollOffsetDegrees = 0.0f;

  private bool m_hasTrackedHmdPose = false;
  private bool m_hasHmdRotationBaseline = false;
  private Vector3 m_trackedHmdPosition = Vector3.zero;
  private Quaternion m_trackedHmdRotation = Quaternion.identity;
  private Quaternion m_hmdRotationBaseline = Quaternion.identity;
  private Quaternion m_hmdRotationOffset = Quaternion.identity;
#endif

  private Camera m_camera = null;

  public GameObject Target
  {
    get { return m_follow_object; }
    set
    {
      m_follow_object = value;
    }
  }

  public LinkCamera()
  {
  }

#if ENABLE_INPUT_SYSTEM
  public bool HasTrackedHmdPose => m_hasTrackedHmdPose;
  public Vector3 TrackedHmdPosition => m_trackedHmdPosition;
  public Quaternion TrackedHmdRotation => m_trackedHmdRotation;
  public Quaternion HmdRotationOffset => m_hmdRotationOffset;
  public Vector3 HmdRotationOffsetEuler => new Vector3( m_hmdPitchOffsetDegrees, m_hmdYawOffsetDegrees, m_hmdRollOffsetDegrees );
#endif

  private void Awake()
  {
    EnsureCamera();
    SyncFieldOfViewFromCamera();
    ApplyFieldOfView();
  }

  private void OnEnable()
  {
#if ENABLE_INPUT_SYSTEM
    EnsureInputActions();
    m_enableAction.Enable();
    m_lookAction.Enable();
    m_lookModifierAction.Enable();
    m_hmdPositionAction.Enable();
    m_hmdRotationAction.Enable();
    m_hmdTrackingStateAction.Enable();
    m_resetHmdOffsetAction.Enable();
#endif

    ApplyFieldOfView();
  }

  private void OnDisable()
  {
#if ENABLE_INPUT_SYSTEM
    if (m_enableAction != null)
      m_enableAction.Disable();

    if (m_lookAction != null)
      m_lookAction.Disable();

    if (m_lookModifierAction != null)
      m_lookModifierAction.Disable();

    if (m_hmdPositionAction != null)
      m_hmdPositionAction.Disable();

    if (m_hmdRotationAction != null)
      m_hmdRotationAction.Disable();

    if (m_hmdTrackingStateAction != null)
      m_hmdTrackingStateAction.Disable();

    if (m_resetHmdOffsetAction != null)
      m_resetHmdOffsetAction.Disable();
#endif
  }

  private void OnDestroy()
  {
#if ENABLE_INPUT_SYSTEM
    m_enableAction?.Dispose();
    m_lookAction?.Dispose();
    m_lookModifierAction?.Dispose();
    m_hmdPositionAction?.Dispose();
    m_hmdRotationAction?.Dispose();
    m_hmdTrackingStateAction?.Dispose();
    m_resetHmdOffsetAction?.Dispose();
#endif
  }

  private void OnValidate()
  {
    EnsureCamera();
    ClampPitchRange();
    SyncFieldOfViewFromCamera();
    ApplyFieldOfView();
  }

  private void LateUpdate()
  {
    ApplyFieldOfView();
    UpdateToggleState();

    if (Target == null || !Enabled)
      return;

    UpdateRuntimeLook();

    var targetTransform = Target.transform;
    var baseForward = targetTransform.TransformDirection(Forward);
    if (baseForward.sqrMagnitude < 1.0e-6f)
      baseForward = targetTransform.forward;
    baseForward.Normalize();

    var baseRotation = Quaternion.LookRotation(baseForward, ResolveUpDirection(baseForward));
    var runtimeLookRotation = Quaternion.Euler( m_pitchDegrees, m_yawDegrees, 0.0f );
    var hmdRotationOffset = UpdateHmdRotationOffset();

    transform.position = targetTransform.TransformPoint(RelativePosition);
    transform.rotation = baseRotation * runtimeLookRotation * hmdRotationOffset;
  }

  private void EnsureCamera()
  {
    if (m_camera == null)
      m_camera = GetComponent<Camera>();
  }

  private void ClampPitchRange()
  {
    if (m_minPitchDegrees > m_maxPitchDegrees) {
      var swap = m_minPitchDegrees;
      m_minPitchDegrees = m_maxPitchDegrees;
      m_maxPitchDegrees = swap;
    }

    m_pitchDegrees = Mathf.Clamp(m_pitchDegrees, m_minPitchDegrees, m_maxPitchDegrees);
  }

  private void SyncFieldOfViewFromCamera()
  {
    if (m_camera == null || m_fieldOfView > 0.0f)
      return;

    m_fieldOfView = m_camera.fieldOfView;
  }

  private void ApplyFieldOfView()
  {
    EnsureCamera();
    if (m_camera == null)
      return;

    SyncFieldOfViewFromCamera();
    m_fieldOfView = Mathf.Clamp(m_fieldOfView, 1.0f, 179.0f);
    if (!Mathf.Approximately(m_camera.fieldOfView, m_fieldOfView))
      m_camera.fieldOfView = m_fieldOfView;
  }

  private void UpdateToggleState()
  {
#if ENABLE_INPUT_SYSTEM
    if (m_enableAction != null && m_enableAction.triggered)
#else
    if (Input.GetKeyDown(toggleEnableKey))
#endif
      Enabled = !Enabled;
  }

  private void UpdateRuntimeLook()
  {
    if ( !m_allowRuntimeLook || !IsLookModifierPressed() )
      return;

    var lookDelta = ReadLookDelta();
    if (lookDelta.sqrMagnitude < 1.0e-6f)
      return;

    var deltaScale = 359.0f * m_cursorSensitivity;
    var pitchSign = m_invertY ? 1.0f : -1.0f;

    m_yawDegrees = Mathf.Repeat(m_yawDegrees + lookDelta.x * deltaScale + 180.0f, 360.0f) - 180.0f;
    m_pitchDegrees = Mathf.Clamp(
      m_pitchDegrees + lookDelta.y * deltaScale * pitchSign,
      m_minPitchDegrees,
      m_maxPitchDegrees);
  }

  private static Vector3 ResolveUpDirection(Vector3 forward)
  {
    if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.999f)
      return Vector3.up;

    return Vector3.forward;
  }

  private bool IsLookModifierPressed()
  {
#if ENABLE_INPUT_SYSTEM
    return m_lookModifierAction != null && m_lookModifierAction.ReadValue<float>() > 0.5f;
#else
    return Input.GetKey(m_lookModifierKey);
#endif
  }

  private Vector2 ReadLookDelta()
  {
#if ENABLE_INPUT_SYSTEM
    if (m_lookAction == null)
      return Vector2.zero;

    return m_lookAction.ReadValue<Vector2>() * Time.deltaTime;
#else
    return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
  }

#if ENABLE_INPUT_SYSTEM
  private Quaternion UpdateHmdRotationOffset()
  {
    if ( !m_enableHmdRotationOffset ) {
      ClearHmdPoseState();
      return Quaternion.identity;
    }

    var resetPressed = WasResetHmdOffsetPressed();
    if ( !TryReadTrackedHmdPose( out var trackedPosition, out var trackedRotation ) ) {
      if ( resetPressed )
        ClearHmdPoseState();
      else
        ClearTransientHmdOffset();
      return Quaternion.identity;
    }

    m_hasTrackedHmdPose = true;
    m_trackedHmdPosition = trackedPosition;
    m_trackedHmdRotation = trackedRotation;

    if ( resetPressed || !m_hasHmdRotationBaseline )
      m_hmdRotationBaseline = trackedRotation;

    m_hasHmdRotationBaseline = true;
    m_hmdRotationOffset = NormalizeRotation( Quaternion.Inverse( m_hmdRotationBaseline ) * trackedRotation );

    var hmdOffsetEuler = NormalizeEulerAngles( m_hmdRotationOffset.eulerAngles );
    m_hmdPitchOffsetDegrees = hmdOffsetEuler.x;
    m_hmdYawOffsetDegrees = hmdOffsetEuler.y;
    m_hmdRollOffsetDegrees = hmdOffsetEuler.z;

    return m_hmdRotationOffset;
  }

  private bool TryReadTrackedHmdPose( out Vector3 trackedPosition, out Quaternion trackedRotation )
  {
    trackedPosition = Vector3.zero;
    trackedRotation = Quaternion.identity;

    if ( m_hmdRotationAction == null ||
         m_hmdTrackingStateAction == null ||
         m_hmdRotationAction.controls.Count == 0 )
      return false;

    var trackingState = (InputTrackingState)m_hmdTrackingStateAction.ReadValue<int>();
    if ( ( trackingState & InputTrackingState.Rotation ) == 0 )
      return false;

    trackedRotation = NormalizeRotation( m_hmdRotationAction.ReadValue<Quaternion>() );
    if ( m_hmdPositionAction != null &&
         m_hmdPositionAction.controls.Count > 0 &&
         ( trackingState & InputTrackingState.Position ) != 0 ) {
      trackedPosition = m_hmdPositionAction.ReadValue<Vector3>();
    }

    return true;
  }

  private void ClearTransientHmdOffset()
  {
    m_hasTrackedHmdPose = false;
    m_trackedHmdPosition = Vector3.zero;
    m_trackedHmdRotation = Quaternion.identity;
    m_hmdRotationOffset = Quaternion.identity;
    m_hmdPitchOffsetDegrees = 0.0f;
    m_hmdYawOffsetDegrees = 0.0f;
    m_hmdRollOffsetDegrees = 0.0f;
    m_hasHmdRotationBaseline = false;
  }

  private void ClearHmdPoseState()
  {
    ClearTransientHmdOffset();
    m_hmdRotationBaseline = Quaternion.identity;
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

  private static Vector3 NormalizeEulerAngles( Vector3 eulerAngles )
  {
    return new Vector3(
      NormalizeEulerAxis( eulerAngles.x ),
      NormalizeEulerAxis( eulerAngles.y ),
      NormalizeEulerAxis( eulerAngles.z ) );
  }

  private static float NormalizeEulerAxis( float degrees )
  {
    var normalized = Mathf.Repeat( degrees + 180.0f, 360.0f ) - 180.0f;
    return Mathf.Approximately( normalized, -180.0f ) ? 180.0f : normalized;
  }

  private void EnsureInputActions()
  {
    if (m_enableAction == null)
      m_enableAction = new InputAction("Enable", binding: "<Keyboard>/F1");

    if (m_lookAction == null)
      m_lookAction = new InputAction("Look", binding: "<Mouse>/delta");

    if (m_lookModifierAction == null)
      m_lookModifierAction = new InputAction("LookModifier", binding: "<Mouse>/rightButton");

    if (m_hmdPositionAction == null)
      m_hmdPositionAction = new InputAction("HmdPosition", InputActionType.PassThrough, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");

    if (m_hmdRotationAction == null)
      m_hmdRotationAction = new InputAction("HmdRotation", InputActionType.PassThrough, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");

    if (m_hmdTrackingStateAction == null)
      m_hmdTrackingStateAction = new InputAction("HmdTrackingState", InputActionType.PassThrough, "<XRHMD>/trackingState", expectedControlType: "Integer");

    if (m_resetHmdOffsetAction == null)
      m_resetHmdOffsetAction = new InputAction("ResetHmdOffset", binding: "<Keyboard>/f2");
  }

  private bool WasResetHmdOffsetPressed()
  {
    if ( m_resetHmdOffsetAction == null )
      return false;

    return m_resetHmdOffsetKey == KeyCode.F2 ?
           m_resetHmdOffsetAction.WasPressedThisFrame() :
           Input.GetKeyDown( m_resetHmdOffsetKey );
  }
#endif
  }
}

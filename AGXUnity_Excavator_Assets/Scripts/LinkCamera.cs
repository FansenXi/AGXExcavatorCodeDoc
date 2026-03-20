using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public class LinkCamera : MonoBehaviour
{
  [SerializeField]
  public bool Enabled = true;

#if ENABLE_INPUT_SYSTEM
  private InputAction m_enableAction;
  private InputAction m_lookAction;
  private InputAction m_lookModifierAction;
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
    var viewForward = baseRotation * Quaternion.Euler(m_pitchDegrees, m_yawDegrees, 0.0f) * Vector3.forward;

    transform.position = targetTransform.TransformPoint(RelativePosition);
    transform.rotation = Quaternion.LookRotation(viewForward.normalized, ResolveUpDirection(viewForward));
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
    if (!m_allowRuntimeLook || !IsLookModifierPressed())
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
  private void EnsureInputActions()
  {
    if (m_enableAction == null)
      m_enableAction = new InputAction("Enable", binding: "<Keyboard>/F1");

    if (m_lookAction == null)
      m_lookAction = new InputAction("Look", binding: "<Mouse>/delta");

    if (m_lookModifierAction == null)
      m_lookModifierAction = new InputAction("LookModifier", binding: "<Mouse>/rightButton");
  }
#endif
}

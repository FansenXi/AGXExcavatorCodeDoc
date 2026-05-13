using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [DefaultExecutionOrder( 1000 )]
  public class InterpolatedFollowAnchor : MonoBehaviour
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

    public Vector3 Forward = new Vector3( 0.0f, 0.0f, 1.0f );
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
    private float m_sourcePoseIntervalSeconds = 0.02f;

    [SerializeField]
    private float m_positionSnapDistance = 0.5f;

    [SerializeField]
    private float m_rotationSnapDegrees = 20.0f;

    [SerializeField]
    private GameObject m_followObject = null;

    private bool m_hasFollowPose = false;
    private bool m_appliedChildCameraFallback = false;
    private FollowPose m_previousFollowPose;
    private FollowPose m_currentFollowPose;
    private float m_currentFollowPoseTime = 0.0f;

    private struct FollowPose
    {
      public Vector3 Position;
      public Quaternion Rotation;

      public FollowPose( Vector3 position, Quaternion rotation )
      {
        Position = position;
        Rotation = rotation;
      }
    }

    public GameObject Target
    {
      get { return m_followObject; }
      set
      {
        if ( m_followObject == value )
          return;

        m_followObject = value;
        ResetInterpolation();
        SyncToTargetPose( Time.unscaledTime );
      }
    }

    private void Awake()
    {
      ClampPitchRange();
      ClampInterpolationSettings();
      ResolveFallbackTarget();
      SyncToTargetPose( Time.unscaledTime );
    }

    private void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
      EnsureInputActions();
      m_enableAction.Enable();
      m_lookAction.Enable();
      m_lookModifierAction.Enable();
#endif

      Application.onBeforeRender += HandleBeforeRender;
      ResolveFallbackTarget();
      SyncToTargetPose( Time.unscaledTime );
    }

    private void OnDisable()
    {
      Application.onBeforeRender -= HandleBeforeRender;

#if ENABLE_INPUT_SYSTEM
      if ( m_enableAction != null )
        m_enableAction.Disable();

      if ( m_lookAction != null )
        m_lookAction.Disable();

      if ( m_lookModifierAction != null )
        m_lookModifierAction.Disable();
#endif
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
      m_enableAction?.Dispose();
      m_lookAction?.Dispose();
      m_lookModifierAction?.Dispose();
#endif
    }

    private void OnValidate()
    {
      ClampPitchRange();
      ClampInterpolationSettings();
    }

    private void LateUpdate()
    {
      UpdateToggleState();
      ResolveFallbackTarget();

      if ( Target == null || !Enabled )
        return;

      UpdateRuntimeLook();
      SampleFollowPose( Time.unscaledTime );
      ApplyInterpolatedPose( Time.unscaledTime );
    }

    private void HandleBeforeRender()
    {
      ResolveFallbackTarget();
      if ( Target == null || !Enabled )
        return;

      SampleFollowPose( Time.unscaledTime );
      ApplyInterpolatedPose( Time.unscaledTime );
    }

    private void ResolveFallbackTarget()
    {
      if ( Target != null )
        return;

      var childCameraTarget = TryResolveChildCameraTarget();
      if ( childCameraTarget == null )
        return;

      if ( !m_appliedChildCameraFallback &&
           RelativePosition == Vector3.zero &&
           ApproximatelyDefaultForward( Forward ) ) {
        RelativePosition = childCameraTarget.transform.InverseTransformPoint( transform.position );
        var localForward = childCameraTarget.transform.InverseTransformDirection( transform.forward );
        if ( localForward.sqrMagnitude > 1.0e-6f )
          Forward = localForward.normalized;
        m_appliedChildCameraFallback = true;
      }

      Target = childCameraTarget;
    }

    private GameObject TryResolveChildCameraTarget()
    {
      foreach ( var linkCamera in GetComponentsInChildren<LinkCamera>( true ) ) {
        if ( linkCamera != null && linkCamera.Target != null )
          return linkCamera.Target;
      }

      return null;
    }

    private static bool ApproximatelyDefaultForward( Vector3 forward )
    {
      return ( forward - new Vector3( 0.0f, 0.0f, 1.0f ) ).sqrMagnitude <= 1.0e-6f;
    }

    private void SyncToTargetPose( float sampleTime )
    {
      if ( Target == null )
        return;

      SampleFollowPose( sampleTime, true );
      ApplyInterpolatedPose( sampleTime, true );
    }

    private void ResetInterpolation()
    {
      m_hasFollowPose = false;
      m_previousFollowPose = default;
      m_currentFollowPose = default;
      m_currentFollowPoseTime = 0.0f;
    }

    private void ClampPitchRange()
    {
      if ( m_minPitchDegrees > m_maxPitchDegrees ) {
        var swap = m_minPitchDegrees;
        m_minPitchDegrees = m_maxPitchDegrees;
        m_maxPitchDegrees = swap;
      }

      m_pitchDegrees = Mathf.Clamp( m_pitchDegrees, m_minPitchDegrees, m_maxPitchDegrees );
    }

    private void ClampInterpolationSettings()
    {
      m_sourcePoseIntervalSeconds = Mathf.Max( 0.0f, m_sourcePoseIntervalSeconds );
      m_positionSnapDistance = Mathf.Max( 0.0f, m_positionSnapDistance );
      m_rotationSnapDegrees = Mathf.Clamp( m_rotationSnapDegrees, 0.0f, 180.0f );
    }

    private void UpdateToggleState()
    {
#if ENABLE_INPUT_SYSTEM
      if ( m_enableAction != null && m_enableAction.triggered )
#else
      if ( Input.GetKeyDown( toggleEnableKey ) )
#endif
      {
        Enabled = !Enabled;
        if ( Enabled )
          SyncToTargetPose( Time.unscaledTime );
      }
    }

    private void UpdateRuntimeLook()
    {
      if ( !m_allowRuntimeLook || !IsLookModifierPressed() )
        return;

      var lookDelta = ReadLookDelta();
      if ( lookDelta.sqrMagnitude < 1.0e-6f )
        return;

      var deltaScale = 359.0f * m_cursorSensitivity;
      var pitchSign = m_invertY ? 1.0f : -1.0f;

      m_yawDegrees = Mathf.Repeat( m_yawDegrees + lookDelta.x * deltaScale + 180.0f, 360.0f ) - 180.0f;
      m_pitchDegrees = Mathf.Clamp(
        m_pitchDegrees + lookDelta.y * deltaScale * pitchSign,
        m_minPitchDegrees,
        m_maxPitchDegrees );
    }

    private bool IsLookModifierPressed()
    {
#if ENABLE_INPUT_SYSTEM
      return m_lookModifierAction != null && m_lookModifierAction.ReadValue<float>() > 0.5f;
#else
      return Input.GetKey( m_lookModifierKey );
#endif
    }

    private Vector2 ReadLookDelta()
    {
#if ENABLE_INPUT_SYSTEM
      if ( m_lookAction == null )
        return Vector2.zero;

      return m_lookAction.ReadValue<Vector2>() * Time.deltaTime;
#else
      return new Vector2( Input.GetAxis( "Mouse X" ), Input.GetAxis( "Mouse Y" ) );
#endif
    }

    private void SampleFollowPose( float sampleTime, bool forceSnap = false )
    {
      if ( Target == null )
        return;

      var observedFollowPose = BuildFollowPose( Target.transform );
      if ( !m_hasFollowPose ) {
        m_previousFollowPose = observedFollowPose;
        m_currentFollowPose = observedFollowPose;
        m_currentFollowPoseTime = sampleTime;
        m_hasFollowPose = true;
        return;
      }

      var positionDelta = Vector3.Distance( m_currentFollowPose.Position, observedFollowPose.Position );
      var rotationDelta = Quaternion.Angle( m_currentFollowPose.Rotation, observedFollowPose.Rotation );

      if ( !forceSnap &&
           positionDelta < 1.0e-4f &&
           rotationDelta < 1.0e-3f )
        return;

      if ( forceSnap ||
           positionDelta > m_positionSnapDistance ||
           rotationDelta > m_rotationSnapDegrees ) {
        m_previousFollowPose = observedFollowPose;
        m_currentFollowPose = observedFollowPose;
        m_currentFollowPoseTime = sampleTime;
        return;
      }

      m_previousFollowPose = m_currentFollowPose;
      m_currentFollowPose = observedFollowPose;
      m_currentFollowPoseTime = sampleTime;
    }

    private FollowPose BuildFollowPose( Transform targetTransform )
    {
      var baseForward = targetTransform.TransformDirection( Forward );
      if ( baseForward.sqrMagnitude < 1.0e-6f )
        baseForward = targetTransform.forward;
      baseForward.Normalize();

      var baseRotation = Quaternion.LookRotation( baseForward, ResolveUpDirection( baseForward ) );
      return new FollowPose(
        targetTransform.TransformPoint( RelativePosition ),
        NormalizeRotation( baseRotation ) );
    }

    private void ApplyInterpolatedPose( float renderTime, bool snapImmediately = false )
    {
      if ( !m_hasFollowPose )
        return;

      var followPose = snapImmediately ? m_currentFollowPose : EvaluateFollowPose( renderTime );
      var runtimeLookRotation = Quaternion.Euler( m_pitchDegrees, m_yawDegrees, 0.0f );
      transform.SetPositionAndRotation(
        followPose.Position,
        NormalizeRotation( followPose.Rotation * runtimeLookRotation ) );
    }

    private FollowPose EvaluateFollowPose( float renderTime )
    {
      if ( m_sourcePoseIntervalSeconds <= 1.0e-6f )
        return m_currentFollowPose;

      var alpha = Mathf.Clamp01( ( renderTime - m_currentFollowPoseTime ) / m_sourcePoseIntervalSeconds );
      return new FollowPose(
        Vector3.Lerp( m_previousFollowPose.Position, m_currentFollowPose.Position, alpha ),
        Quaternion.Slerp( m_previousFollowPose.Rotation, m_currentFollowPose.Rotation, alpha ) );
    }

    private static Vector3 ResolveUpDirection( Vector3 forward )
    {
      if ( Mathf.Abs( Vector3.Dot( forward, Vector3.up ) ) < 0.999f )
        return Vector3.up;

      return Vector3.forward;
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

#if ENABLE_INPUT_SYSTEM
    private void EnsureInputActions()
    {
      if ( m_enableAction == null )
        m_enableAction = new InputAction( "Enable", binding: "<Keyboard>/F1" );

      if ( m_lookAction == null )
        m_lookAction = new InputAction( "Look", binding: "<Mouse>/delta" );

      if ( m_lookModifierAction == null )
        m_lookModifierAction = new InputAction( "LookModifier", binding: "<Mouse>/rightButton" );
    }
#endif
  }
}

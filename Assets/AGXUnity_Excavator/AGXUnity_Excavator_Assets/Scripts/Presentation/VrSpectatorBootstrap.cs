using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [RequireComponent( typeof( Camera ) )]
  [DisallowMultipleComponent]
  public class VrSpectatorBootstrap : MonoBehaviour
  {
    private const float DisplayStartupTimeoutSeconds = 1.0f;

    [SerializeField]
    private bool m_enableVrSpectator = true;

    [SerializeField]
    [Min( 0.0f )]
    private float m_startupDelaySeconds = 0.25f;

    [SerializeField]
    private bool m_logStartup = true;

    [SerializeField]
    private Camera m_desktopMainCamera = null;

    [SerializeField]
    private AudioListener m_desktopAudioListener = null;

    private XRManagerSettings m_runtimeManager = null;
    private OpenXRLoader m_runtimeLoader = null;
    private GameObject m_xrOriginObject = null;
    private XROrigin m_xrOrigin = null;
    private Camera m_xrCamera = null;
    private AudioListener m_xrAudioListener = null;
    private VrMainCameraMirror m_cameraMirror = null;
    private Coroutine m_startupRoutine = null;
    private StereoTargetEyeMask m_originalDesktopTargetEye = StereoTargetEyeMask.Both;
    private bool m_originalDesktopAudioEnabled = true;
    private bool m_hasCachedDesktopState = false;
    private bool m_ownsXrLifecycle = false;
    private bool m_vrActive = false;

    private static readonly List<XRDisplaySubsystem> s_DisplaySubsystems = new List<XRDisplaySubsystem>();

    private void Awake()
    {
      ResolveReferences();
      CacheDesktopState();
    }

    private void OnEnable()
    {
      ResolveReferences();
      CacheDesktopState();

      if ( Application.isPlaying && m_startupRoutine == null )
        m_startupRoutine = StartCoroutine( BootstrapRoutine() );
    }

    private void OnDisable()
    {
      if ( m_startupRoutine != null ) {
        StopCoroutine( m_startupRoutine );
        m_startupRoutine = null;
      }

      ShutdownVr();
    }

    private void OnDestroy()
    {
      ShutdownVr();
    }

    private IEnumerator BootstrapRoutine()
    {
      if ( !m_enableVrSpectator )
        yield break;

      if ( m_desktopMainCamera == null ) {
        LogWarning( "VR spectator bootstrap skipped because the desktop Main Camera reference is missing." );
        yield break;
      }

      if ( m_startupDelaySeconds > 0.0f )
        yield return new WaitForSecondsRealtime( m_startupDelaySeconds );

      yield return null;
      m_startupRoutine = null;

      if ( !EnsureXrRunning() ) {
        LogWarning( "OpenXR did not start. Desktop rendering remains unchanged." );
        yield break;
      }

      var deadline = Time.realtimeSinceStartup + DisplayStartupTimeoutSeconds;
      while ( !HasRunningDisplaySubsystem() && Time.realtimeSinceStartup < deadline )
        yield return null;

      if ( !HasRunningDisplaySubsystem() ) {
        LogWarning( "OpenXR started, but no XR display subsystem became active within the startup timeout. Desktop rendering remains unchanged." );
        ShutdownVr();
        yield break;
      }

#if !ENABLE_INPUT_SYSTEM
      LogWarning( "VR spectator requires the Unity Input System to drive the HMD pose." );
      ShutdownVr();
      yield break;
#else
      CreateOrUpdateRig();
      SetVrActive( true );
      LogInfo( "VR spectator mode is active. Desktop rendering stays on the existing Main Camera while the HMD follows it through a dedicated XR camera." );
#endif
    }

    private void ResolveReferences()
    {
      if ( m_desktopMainCamera == null )
        m_desktopMainCamera = GetComponent<Camera>();

      if ( m_desktopAudioListener == null )
        m_desktopAudioListener = GetComponent<AudioListener>();
    }

    private void CacheDesktopState()
    {
      if ( m_hasCachedDesktopState || m_desktopMainCamera == null )
        return;

      m_originalDesktopTargetEye = m_desktopMainCamera.stereoTargetEye;
      m_originalDesktopAudioEnabled = m_desktopAudioListener == null || m_desktopAudioListener.enabled;
      m_hasCachedDesktopState = true;
    }

    private bool EnsureXrRunning()
    {
      if ( HasRunningDisplaySubsystem() )
        return true;

      return CreateAndStartRuntimeManager();
    }

    private bool CreateAndStartRuntimeManager()
    {
      CleanupRuntimeManager();

      // Keep the spectator path self-contained inside this repo instead of depending on
      // project-level XR management assets that currently live outside the repo root.
      m_runtimeManager = ScriptableObject.CreateInstance<XRManagerSettings>();
      m_runtimeManager.hideFlags = HideFlags.DontSave;
      m_runtimeManager.automaticLoading = false;
      m_runtimeManager.automaticRunning = false;

      m_runtimeLoader = ScriptableObject.CreateInstance<OpenXRLoader>();
      m_runtimeLoader.hideFlags = HideFlags.DontSave;

#pragma warning disable CS0618
      m_runtimeManager.loaders.Clear();
      m_runtimeManager.loaders.Add( m_runtimeLoader );
#pragma warning restore CS0618

      m_runtimeManager.InitializeLoaderSync();
      if ( m_runtimeManager.activeLoader == null ) {
        LogWarning( "OpenXR loader initialization failed. SteamVR must be available as the active OpenXR runtime before entering Play Mode." );
        CleanupRuntimeManager();
        return false;
      }

      m_runtimeManager.StartSubsystems();
      m_ownsXrLifecycle = true;
      return true;
    }

    private void CreateOrUpdateRig()
    {
      if ( m_xrOriginObject == null ) {
        m_xrOriginObject = new GameObject( "VR Spectator Origin" );
        m_xrOrigin = m_xrOriginObject.AddComponent<XROrigin>();
        m_cameraMirror = m_xrOriginObject.AddComponent<VrMainCameraMirror>();

        var cameraOffsetObject = new GameObject( "Camera Offset" );
        cameraOffsetObject.transform.SetParent( m_xrOriginObject.transform, false );

        var xrCameraObject = new GameObject( "VR Spectator Camera" );
        xrCameraObject.transform.SetParent( cameraOffsetObject.transform, false );
        xrCameraObject.tag = "Untagged";

        m_xrCamera = xrCameraObject.AddComponent<Camera>();
        m_xrCamera.enabled = true;
        m_xrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        m_xrCamera.targetDisplay = 0;

        m_xrAudioListener = xrCameraObject.AddComponent<AudioListener>();
        m_xrAudioListener.enabled = false;

#if ENABLE_INPUT_SYSTEM
        ConfigureTrackedPoseDriver( xrCameraObject.AddComponent<TrackedPoseDriver>() );
#endif

        m_xrOrigin.Camera = m_xrCamera;
        m_xrOrigin.Origin = m_xrOriginObject;
        m_xrOrigin.CameraFloorOffsetObject = cameraOffsetObject;
        m_xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        m_xrOrigin.CameraYOffset = 0.0f;

        xrCameraObject.transform.localPosition = Vector3.zero;
        xrCameraObject.transform.localRotation = Quaternion.identity;
      }

      m_cameraMirror.Configure( m_desktopMainCamera, m_xrOrigin, m_xrCamera );
      m_xrOriginObject.SetActive( false );
    }

#if ENABLE_INPUT_SYSTEM
    private static void ConfigureTrackedPoseDriver( TrackedPoseDriver trackedPoseDriver )
    {
      trackedPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
      trackedPoseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
      trackedPoseDriver.ignoreTrackingState = false;
      trackedPoseDriver.positionInput = CreateInputActionProperty( "HMD Position", "<XRHMD>/centerEyePosition", "Vector3" );
      trackedPoseDriver.rotationInput = CreateInputActionProperty( "HMD Rotation", "<XRHMD>/centerEyeRotation", "Quaternion" );
      trackedPoseDriver.trackingStateInput = CreateInputActionProperty( "HMD Tracking State", "<XRHMD>/trackingState", "Integer" );
    }

    private static InputActionProperty CreateInputActionProperty( string actionName, string bindingPath, string expectedControlType )
    {
      var action = new InputAction(
        actionName,
        InputActionType.PassThrough,
        bindingPath,
        expectedControlType: expectedControlType );
      return new InputActionProperty( action );
    }
#endif

    private void SetVrActive( bool active )
    {
      if ( m_desktopMainCamera != null )
        m_desktopMainCamera.stereoTargetEye = active ? StereoTargetEyeMask.None : m_originalDesktopTargetEye;

      if ( m_desktopAudioListener != null )
        m_desktopAudioListener.enabled = active ? false : m_originalDesktopAudioEnabled;

      if ( m_xrAudioListener != null )
        m_xrAudioListener.enabled = active;

      if ( m_xrOriginObject != null )
        m_xrOriginObject.SetActive( active );

      m_vrActive = active;
    }

    private void ShutdownVr()
    {
      if ( m_vrActive )
        SetVrActive( false );

      if ( m_xrOriginObject != null ) {
        DestroyRuntimeObject( m_xrOriginObject );
        m_xrOriginObject = null;
        m_xrOrigin = null;
        m_xrCamera = null;
        m_xrAudioListener = null;
        m_cameraMirror = null;
      }

      if ( m_ownsXrLifecycle && m_runtimeManager != null ) {
        if ( m_runtimeManager.activeLoader != null ) {
          m_runtimeManager.StopSubsystems();
          m_runtimeManager.DeinitializeLoader();
        }

        CleanupRuntimeManager();
      }

      if ( m_desktopMainCamera != null )
        m_desktopMainCamera.stereoTargetEye = m_originalDesktopTargetEye;

      if ( m_desktopAudioListener != null )
        m_desktopAudioListener.enabled = m_originalDesktopAudioEnabled;

      m_vrActive = false;
      m_ownsXrLifecycle = false;
    }

    private void CleanupRuntimeManager()
    {
      m_ownsXrLifecycle = false;

      if ( m_runtimeLoader != null ) {
        DestroyRuntimeObject( m_runtimeLoader );
        m_runtimeLoader = null;
      }

      if ( m_runtimeManager != null ) {
        DestroyRuntimeObject( m_runtimeManager );
        m_runtimeManager = null;
      }
    }

    private static bool HasRunningDisplaySubsystem()
    {
      s_DisplaySubsystems.Clear();
      SubsystemManager.GetSubsystems( s_DisplaySubsystems );

      foreach ( var displaySubsystem in s_DisplaySubsystems ) {
        if ( displaySubsystem != null && displaySubsystem.running )
          return true;
      }

      return false;
    }

    private void LogInfo( string message )
    {
      if ( m_logStartup )
        Debug.Log( $"[VrSpectatorBootstrap] {message}", this );
    }

    private void LogWarning( string message )
    {
      if ( m_logStartup )
        Debug.LogWarning( $"[VrSpectatorBootstrap] {message}", this );
    }

    private static void DestroyRuntimeObject( Object instance )
    {
      if ( instance == null )
        return;

      if ( Application.isPlaying )
        Object.Destroy( instance );
      else
        Object.DestroyImmediate( instance );
    }
  }
}

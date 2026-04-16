using System;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Experiment;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  [DisallowMultipleComponent]
  public sealed class CabControlStickVisualizer : MonoBehaviour
  {
    [Serializable]
    private sealed class StickBinding
    {
      [SerializeField]
      public string NameContains = string.Empty;

      [SerializeField]
      public Transform Transform = null;

      [SerializeField]
      public bool InvertHorizontal = false;

      [SerializeField]
      public bool InvertVertical = false;

      [SerializeField]
      public bool BaseAtPositiveLocalY = false;

      [SerializeField]
      public float HorizontalAngle = 12.0f;

      [SerializeField]
      public float VerticalAngle = 14.0f;

      [NonSerialized]
      public Vector3 RestBaseLocal = Vector3.zero;

      [NonSerialized]
      public Quaternion RestRotationLocal = Quaternion.identity;

      [NonSerialized]
      public float HalfHeight = 0.25f;

      [NonSerialized]
      public bool Initialized = false;
    }

    private const string AutoInstallObjectName = "controlStick";

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private StickBinding m_leftStick = new StickBinding
    {
      NameContains = "stickLeft",
      HorizontalAngle = 12.0f,
      VerticalAngle = 14.0f
    };

    [SerializeField]
    private StickBinding m_rightStick = new StickBinding
    {
      NameContains = "stickRight",
      HorizontalAngle = 12.0f,
      VerticalAngle = 14.0f
    };

    private bool m_missingReferencesLogged = false;

    [RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.AfterSceneLoad )]
    private static void AutoInstall()
    {
      SceneManager.sceneLoaded -= HandleSceneLoaded;
      SceneManager.sceneLoaded += HandleSceneLoaded;
      InstallIntoLoadedScenes();
    }

    private static void HandleSceneLoaded( Scene scene, LoadSceneMode mode )
    {
      InstallIntoScene( scene );
    }

    private static void InstallIntoLoadedScenes()
    {
      for ( var index = 0; index < SceneManager.sceneCount; ++index )
        InstallIntoScene( SceneManager.GetSceneAt( index ) );
    }

    private static void InstallIntoScene( Scene scene )
    {
      if ( !scene.isLoaded )
        return;

      foreach ( var root in scene.GetRootGameObjects() ) {
        foreach ( var candidate in root.GetComponentsInChildren<Transform>( true ) ) {
          if ( !string.Equals( candidate.name, AutoInstallObjectName, StringComparison.Ordinal ) )
            continue;

          if ( candidate.GetComponent<CabControlStickVisualizer>() == null )
            candidate.gameObject.AddComponent<CabControlStickVisualizer>();
        }
      }
    }

    private void Awake()
    {
      ResolveReferences();
      EnsureRig();
    }

    private void OnEnable()
    {
      ResolveReferences();
      EnsureRig();
    }

    private void LateUpdate()
    {
      ResolveReferences();
      if ( !EnsureRig() )
        return;

      var command = GetCurrentCommand();
      ApplyStickPose( m_leftStick, command.LeftStickX, command.LeftStickY );
      ApplyStickPose( m_rightStick, command.RightStickX, command.RightStickY );
    }

    private OperatorCommand GetCurrentCommand()
    {
      if ( m_episodeManager != null &&
           m_episodeManager.isActiveAndEnabled &&
           m_episodeManager.IsEpisodeRunning ) {
        return m_episodeManager.LastSimulatedCommand;
      }

      return m_machineController != null ?
             ConvertActuationToIsoOperatorCommand( m_machineController.LastActuationCommand ) :
             OperatorCommand.Zero;
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );

      if ( m_leftStick.Transform == null )
        m_leftStick.Transform = FindStickTransform( m_leftStick.NameContains );

      if ( m_rightStick.Transform == null )
        m_rightStick.Transform = FindStickTransform( m_rightStick.NameContains );
    }

    private bool EnsureRig()
    {
      if ( m_leftStick.Transform == null || m_rightStick.Transform == null ) {
        if ( !m_missingReferencesLogged ) {
          Debug.LogWarning( "CabControlStickVisualizer could not resolve the control stick transforms.", this );
          m_missingReferencesLogged = true;
        }

        return false;
      }

      m_missingReferencesLogged = false;
      EnsureStickInitialized( m_leftStick );
      EnsureStickInitialized( m_rightStick );
      return m_leftStick.Initialized && m_rightStick.Initialized;
    }

    private void EnsureStickInitialized( StickBinding stick )
    {
      if ( stick.Initialized && stick.Transform != null )
        return;

      if ( stick.Transform == null )
        return;

      var cylinder = stick.Transform.GetComponent<Cylinder>();
      stick.HalfHeight = cylinder != null ? 0.5f * cylinder.Height : EstimateHalfHeight( stick.Transform );

      var baseDirection = stick.BaseAtPositiveLocalY ? Vector3.up : Vector3.down;
      stick.RestRotationLocal = stick.Transform.localRotation;
      stick.RestBaseLocal = stick.Transform.localPosition + stick.RestRotationLocal * ( baseDirection * stick.HalfHeight );
      stick.Initialized = true;
    }

    private void ApplyStickPose( StickBinding stick, float horizontalAxis, float verticalAxis )
    {
      if ( !stick.Initialized || stick.Transform == null )
        return;

      var mappedHorizontalAxis = -Mathf.Clamp( verticalAxis, -1.0f, 1.0f );
      var mappedVerticalAxis = Mathf.Clamp( horizontalAxis, -1.0f, 1.0f );
      var horizontalAngle = stick.HorizontalAngle * mappedHorizontalAxis * ( stick.InvertHorizontal ? -1.0f : 1.0f );
      var verticalAngle = stick.VerticalAngle * mappedVerticalAxis * ( stick.InvertVertical ? -1.0f : 1.0f );
      var localTilt = Quaternion.AngleAxis( horizontalAngle, Vector3.forward ) *
                      Quaternion.AngleAxis( verticalAngle, Vector3.right );
      var localRotation = stick.RestRotationLocal * localTilt;
      var baseToCenterLocal = stick.BaseAtPositiveLocalY ? Vector3.down * stick.HalfHeight : Vector3.up * stick.HalfHeight;
      var localPosition = stick.RestBaseLocal + localRotation * baseToCenterLocal;

      stick.Transform.localRotation = localRotation;
      stick.Transform.localPosition = localPosition;
    }

    private Transform FindStickTransform( string nameContains )
    {
      if ( string.IsNullOrWhiteSpace( nameContains ) )
        return null;

      foreach ( var candidate in GetComponentsInChildren<Transform>( true ) ) {
        if ( candidate == transform )
          continue;

        if ( candidate.name.IndexOf( nameContains, StringComparison.OrdinalIgnoreCase ) >= 0 )
          return candidate;
      }

      return null;
    }

    private static float EstimateHalfHeight( Transform stickTransform )
    {
      var renderers = stickTransform.GetComponentsInChildren<Renderer>( true );
      if ( renderers == null || renderers.Length == 0 )
        return 0.25f;

      var bounds = renderers[ 0 ].bounds;
      for ( var index = 1; index < renderers.Length; ++index )
        bounds.Encapsulate( renderers[ index ].bounds );

      return Mathf.Max( 0.01f, 0.5f * bounds.size.y );
    }

    private static OperatorCommand ConvertActuationToIsoOperatorCommand( ExcavatorActuationCommand actuation )
    {
      return new OperatorCommand
      {
        LeftStickX = actuation.Swing / 0.6f,
        LeftStickY = actuation.Stick / -0.7f,
        RightStickX = actuation.Bucket / 0.7f,
        RightStickY = -actuation.Boom / 0.3f,
        Drive = actuation.Drive,
        Steer = actuation.Steer
      }.ClampAxes();
    }
  }
}

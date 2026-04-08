using System;
using AGXUnity;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.SimulationBridge;
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
      public RigidBody Body = null;

      [NonSerialized]
      public AGXUnity.Constraint Joint = null;

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
    private const string ChassieObserverName = "ChassieObserver";

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private AgxSimStepAckServer m_stepAckServer = null;

    [SerializeField]
    private ObserverFrame m_anchorObserver = null;

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
      if ( m_stepAckServer != null && m_stepAckServer.IsListening && m_stepAckServer.HasReceivedStepCommand ) {
        if ( m_episodeManager != null )
          return m_episodeManager.ConvertActuationToOperatorCommand( m_stepAckServer.LastRequestedActuationCommand );

        return ConvertActuationToIsoOperatorCommand( m_stepAckServer.LastRequestedActuationCommand );
      }

      if ( m_episodeManager == null || !m_episodeManager.isActiveAndEnabled || !m_episodeManager.IsEpisodeRunning )
        return OperatorCommand.Zero;

      return m_episodeManager.LastSimulatedCommand;
    }

    private void ResolveReferences()
    {
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );
      m_stepAckServer = ExcavatorRigLocator.ResolveComponent( this, m_stepAckServer );

      if ( m_leftStick.Transform == null )
        m_leftStick.Transform = FindStickTransform( m_leftStick.NameContains );

      if ( m_rightStick.Transform == null )
        m_rightStick.Transform = FindStickTransform( m_rightStick.NameContains );

      if ( m_anchorObserver == null )
        m_anchorObserver = FindAnchorObserver();
    }

    private bool EnsureRig()
    {
      if ( m_anchorObserver == null || m_leftStick.Transform == null || m_rightStick.Transform == null ) {
        if ( !m_missingReferencesLogged ) {
          Debug.LogWarning( "CabControlStickVisualizer could not resolve the control sticks or the ChassieObserver anchor.", this );
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
      if ( stick.Initialized && stick.Transform != null && stick.Body != null && stick.Joint != null )
        return;

      if ( stick.Transform == null )
        return;

      var cylinder = stick.Transform.GetComponent<Cylinder>();
      stick.HalfHeight = cylinder != null ? 0.5f * cylinder.Height : EstimateHalfHeight( stick.Transform );

      stick.Body = stick.Transform.GetComponent<RigidBody>();
      if ( stick.Body == null )
        stick.Body = stick.Transform.gameObject.AddComponent<RigidBody>();

      stick.Body.MotionControl = agx.RigidBody.MotionControl.KINEMATICS;
      DisableStickCollisions( stick.Transform );

      var baseWorld = GetStickBaseWorld( stick );
      stick.RestBaseLocal = m_anchorObserver.transform.InverseTransformPoint( baseWorld );
      stick.RestRotationLocal = Quaternion.Inverse( m_anchorObserver.transform.rotation ) * stick.Transform.rotation;

      stick.Joint = FindExistingJoint( stick.Transform );
      if ( stick.Joint == null ) {
        stick.Joint = AGXUnity.Constraint.Create( AGXUnity.ConstraintType.BallJoint );
        stick.Joint.name = $"{stick.Transform.name}.BallJoint";
        stick.Joint.transform.SetParent( transform, false );
      }

      ConfigureJoint( stick, baseWorld );
      stick.Initialized = true;
    }

    private void ConfigureJoint( StickBinding stick, Vector3 baseWorld )
    {
      if ( stick.Joint == null || m_anchorObserver == null || stick.Transform == null )
        return;

      stick.Joint.CollisionsState = AGXUnity.Constraint.ECollisionsState.DisableRigidBody1VsRigidBody2;
      stick.Joint.AttachmentPair.Synchronized = false;
      stick.Joint.AttachmentPair.ReferenceObject = m_anchorObserver.gameObject;
      stick.Joint.AttachmentPair.ConnectedObject = stick.Transform.gameObject;
      stick.Joint.AttachmentPair.ReferenceFrame.Position = baseWorld;
      stick.Joint.AttachmentPair.ReferenceFrame.Rotation = m_anchorObserver.transform.rotation;
      stick.Joint.AttachmentPair.ConnectedFrame.Position = baseWorld;
      stick.Joint.AttachmentPair.ConnectedFrame.Rotation = stick.Transform.rotation;
    }

    private void ApplyStickPose( StickBinding stick, float horizontalAxis, float verticalAxis )
    {
      if ( !stick.Initialized || stick.Transform == null )
        return;

      var anchorTransform = m_anchorObserver.transform;
      var baseWorld = anchorTransform.TransformPoint( stick.RestBaseLocal );
      var mappedHorizontalAxis = -Mathf.Clamp( verticalAxis, -1.0f, 1.0f );
      var mappedVerticalAxis = Mathf.Clamp( horizontalAxis, -1.0f, 1.0f );
      var horizontalAngle = stick.HorizontalAngle * mappedHorizontalAxis * ( stick.InvertHorizontal ? -1.0f : 1.0f );
      var verticalAngle = stick.VerticalAngle * mappedVerticalAxis * ( stick.InvertVertical ? -1.0f : 1.0f );
      var localTilt = Quaternion.AngleAxis( horizontalAngle, Vector3.forward ) *
                      Quaternion.AngleAxis( verticalAngle, Vector3.right );
      var worldRotation = anchorTransform.rotation * stick.RestRotationLocal * localTilt;
      var baseToCenterLocal = stick.BaseAtPositiveLocalY ? Vector3.down * stick.HalfHeight : Vector3.up * stick.HalfHeight;
      var worldPosition = baseWorld + worldRotation * baseToCenterLocal;

      if ( stick.Body != null )
        stick.Body.MotionControl = agx.RigidBody.MotionControl.KINEMATICS;

      stick.Transform.SetPositionAndRotation( worldPosition, worldRotation );
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

    private ObserverFrame FindAnchorObserver()
    {
      var searchRoot = transform.parent != null ? transform.parent : transform.root;
      if ( searchRoot == null )
        return null;

      ObserverFrame bestObserver = null;
      var bestScore = float.PositiveInfinity;

      foreach ( var observer in searchRoot.GetComponentsInChildren<ObserverFrame>( true ) ) {
        if ( observer == null || observer.transform.IsChildOf( transform ) )
          continue;

        if ( observer.name.IndexOf( ChassieObserverName, StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        if ( observer.GetComponentInParent<RigidBody>() == null )
          continue;

        var score = Vector3.SqrMagnitude( observer.transform.position - transform.position );
        if ( score < bestScore ) {
          bestObserver = observer;
          bestScore = score;
        }
      }

      if ( bestObserver != null )
        return bestObserver;

      foreach ( var observer in searchRoot.GetComponentsInChildren<ObserverFrame>( true ) ) {
        if ( observer != null &&
             !observer.transform.IsChildOf( transform ) &&
             observer.GetComponentInParent<RigidBody>() != null )
          return observer;
      }

      return null;
    }

    private AGXUnity.Constraint FindExistingJoint( Transform stickTransform )
    {
      foreach ( var constraint in GetComponentsInChildren<AGXUnity.Constraint>( true ) ) {
        if ( constraint == null )
          continue;

        if ( constraint.Type != AGXUnity.ConstraintType.BallJoint )
          continue;

        if ( constraint.AttachmentPair.ConnectedObject == stickTransform.gameObject )
          return constraint;
      }

      return null;
    }

    private void DisableStickCollisions( Transform stickTransform )
    {
      foreach ( var shape in stickTransform.GetComponentsInChildren<Shape>( true ) )
        shape.CollisionsEnabled = false;
    }

    private Vector3 GetStickBaseWorld( StickBinding stick )
    {
      var direction = stick.BaseAtPositiveLocalY ? Vector3.up : Vector3.down;
      return stick.Transform.TransformPoint( direction * stick.HalfHeight );
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

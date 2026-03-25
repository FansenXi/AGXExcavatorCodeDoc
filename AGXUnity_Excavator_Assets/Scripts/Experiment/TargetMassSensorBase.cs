using System;
using System.Collections.Generic;
using AGXUnity;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

public abstract class TargetMassSensorBase : ScriptComponent
{
  public abstract string TargetName { get; }
  public abstract float MassInBox { get; }
  public abstract float DepositedMass { get; }
  public abstract Shape[] GetCollisionShapes();

  public abstract bool TryGetMeasurementVolume( out Transform measurementFrame,
                                                out Vector3 measurementCenterLocal,
                                                out Vector3 measurementHalfExtents );

  public abstract void ResetMeasurements();
}

public class ActiveTargetCollisionMonitor : MonoBehaviour
{
  [SerializeField]
  private Excavator m_excavator = null;

  [SerializeField]
  private global::SwitchableTargetMassSensor m_targetMassSensor = null;

  [SerializeField]
  [Min( 0.0f )]
  private float m_hardCollisionNormalForceThreshN = 5000.0f;

  private readonly HashSet<int> m_sourceShapeIds = new HashSet<int>();
  private readonly HashSet<int> m_currentTargetShapeIds = new HashSet<int>();
  private Shape[] m_sourceShapes = Array.Empty<Shape>();
  private bool m_callbacksRegistered = false;
  private bool m_hadTargetContactThisStep = false;
  private bool m_hadHardTargetCollisionThisStep = false;
  private bool m_isTargetContactActive = false;
  private bool m_contactSessionAlreadyCounted = false;

  public float HardCollisionNormalForceThresholdN => m_hardCollisionNormalForceThreshN;
  public int TargetHardCollisionCount { get; private set; } = 0;
  public float TargetContactMaxNormalForceN { get; private set; } = 0.0f;

  private void Awake()
  {
    ResolveReferences();
  }

  private void OnEnable()
  {
    ResolveReferences();
    TryInitializeMonitor();
  }

  private void Update()
  {
    if ( !m_callbacksRegistered )
      TryInitializeMonitor();
  }

  private void OnDisable()
  {
    UnregisterCallbacks();
    ResetMonitoring();
  }

  private void OnDestroy()
  {
    UnregisterCallbacks();
  }

  public void ResetMonitoring()
  {
    TargetHardCollisionCount = 0;
    TargetContactMaxNormalForceN = 0.0f;
    m_hadTargetContactThisStep = false;
    m_hadHardTargetCollisionThisStep = false;
    m_isTargetContactActive = false;
    m_contactSessionAlreadyCounted = false;
  }

  private void ResolveReferences()
  {
    m_excavator = ExcavatorRigLocator.ResolveComponent( this, m_excavator );
    m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( this, m_targetMassSensor );
    m_targetMassSensor?.RefreshTargets();
  }

  private void TryInitializeMonitor()
  {
    if ( m_callbacksRegistered || !Simulation.HasInstance || Simulation.Instance == null )
      return;

    ResolveReferences();
    CacheSourceShapes();
    if ( m_sourceShapes == null || m_sourceShapes.Length == 0 )
      return;

    foreach ( var sourceShape in m_sourceShapes ) {
      if ( sourceShape == null || sourceShape.NativeGeometry == null )
        return;
    }

    var simulation = Simulation.Instance;
    simulation.StepCallbacks.PreStepForward += OnPreStepForward;
    simulation.StepCallbacks.PostStepForward += OnPostStepForward;

    foreach ( var sourceShape in m_sourceShapes )
      simulation.ContactCallbacks.OnForce( OnSourceShapeForce, sourceShape );

    m_callbacksRegistered = true;
  }

  private void UnregisterCallbacks()
  {
    if ( !m_callbacksRegistered )
      return;

    if ( Simulation.HasInstance && Simulation.Instance != null ) {
      Simulation.Instance.StepCallbacks.PreStepForward -= OnPreStepForward;
      Simulation.Instance.StepCallbacks.PostStepForward -= OnPostStepForward;
      Simulation.Instance.ContactCallbacks.Remove( OnSourceShapeForce );
    }

    m_callbacksRegistered = false;
  }

  private void OnPreStepForward()
  {
    TargetContactMaxNormalForceN = 0.0f;
    m_hadTargetContactThisStep = false;
    m_hadHardTargetCollisionThisStep = false;
    RefreshCurrentTargetShapeIds();
  }

  private void OnPostStepForward()
  {
    if ( m_hadTargetContactThisStep ) {
      if ( !m_isTargetContactActive ) {
        m_isTargetContactActive = true;
        m_contactSessionAlreadyCounted = false;
      }

      if ( m_hadHardTargetCollisionThisStep && !m_contactSessionAlreadyCounted ) {
        TargetHardCollisionCount += 1;
        m_contactSessionAlreadyCounted = true;
      }

      return;
    }

    if ( m_isTargetContactActive ) {
      m_isTargetContactActive = false;
      m_contactSessionAlreadyCounted = false;
    }
  }

  private bool OnSourceShapeForce( ref ContactData contactData )
  {
    if ( !contactData.HasContactPointForceData || m_currentTargetShapeIds.Count == 0 )
      return false;

    var component1Shape = contactData.Component1 as Shape;
    var component2Shape = contactData.Component2 as Shape;
    if ( component1Shape == null || component2Shape == null )
      return false;

    if ( !TryResolveSourceTargetPair( component1Shape, component2Shape, out _, out _ ) )
      return false;

    m_hadTargetContactThisStep = true;

    var normalForceMagnitude = contactData.TotalNormalForce.magnitude;
    if ( normalForceMagnitude > TargetContactMaxNormalForceN )
      TargetContactMaxNormalForceN = normalForceMagnitude;

    if ( normalForceMagnitude < m_hardCollisionNormalForceThreshN )
      return false;

    m_hadHardTargetCollisionThisStep = true;

    return false;
  }

  private void CacheSourceShapes()
  {
    ResolveReferences();

    m_sourceShapeIds.Clear();
    m_sourceShapes = Array.Empty<Shape>();

    if ( m_excavator == null )
      return;

    var discoveredShapes = m_excavator.GetComponentsInChildren<Shape>( true );
    if ( discoveredShapes == null || discoveredShapes.Length == 0 )
      return;

    var filteredShapes = new List<Shape>( discoveredShapes.Length );
    foreach ( var discoveredShape in discoveredShapes ) {
      if ( !ShouldIncludeShape( discoveredShape ) || filteredShapes.Contains( discoveredShape ) )
        continue;

      filteredShapes.Add( discoveredShape );
      m_sourceShapeIds.Add( discoveredShape.GetInstanceID() );
    }

    m_sourceShapes = filteredShapes.ToArray();
  }

  private void RefreshCurrentTargetShapeIds()
  {
    m_currentTargetShapeIds.Clear();

    var currentTarget = m_targetMassSensor != null ? m_targetMassSensor.CurrentTarget : null;
    var targetShapes = currentTarget != null ? currentTarget.GetCollisionShapes() : null;
    if ( targetShapes == null || targetShapes.Length == 0 )
      return;

    foreach ( var targetShape in targetShapes ) {
      if ( !ShouldIncludeShape( targetShape ) )
        continue;

      m_currentTargetShapeIds.Add( targetShape.GetInstanceID() );
    }
  }

  private bool TryResolveSourceTargetPair( Shape component1Shape,
                                           Shape component2Shape,
                                           out Shape sourceShape,
                                           out Shape targetShape )
  {
    sourceShape = null;
    targetShape = null;

    var component1IsSource = m_sourceShapeIds.Contains( component1Shape.GetInstanceID() );
    var component2IsSource = m_sourceShapeIds.Contains( component2Shape.GetInstanceID() );
    var component1IsTarget = m_currentTargetShapeIds.Contains( component1Shape.GetInstanceID() );
    var component2IsTarget = m_currentTargetShapeIds.Contains( component2Shape.GetInstanceID() );

    if ( component1IsSource && component2IsTarget ) {
      sourceShape = component1Shape;
      targetShape = component2Shape;
      return true;
    }

    if ( component2IsSource && component1IsTarget ) {
      sourceShape = component2Shape;
      targetShape = component1Shape;
      return true;
    }

    return false;
  }

  private static bool ShouldIncludeShape( Shape shape )
  {
    return shape != null &&
           shape.CollisionsEnabled &&
           shape.NativeGeometry != null;
  }
}

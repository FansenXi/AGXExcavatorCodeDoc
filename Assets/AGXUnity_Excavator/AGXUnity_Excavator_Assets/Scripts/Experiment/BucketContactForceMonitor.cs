using System;
using System.Collections.Generic;
using AGXUnity;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class BucketContactForceMonitor : MonoBehaviour
  {
    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Transform m_machineRoot = null;

    [SerializeField]
    private Transform m_bucketReference = null;

    private readonly HashSet<int> m_bucketShapeIds = new HashSet<int>();
    private readonly HashSet<int> m_machineShapeIds = new HashSet<int>();
    private Shape[] m_bucketShapes = Array.Empty<Shape>();
    private bool m_callbacksRegistered = false;

    public bool IsMonitoring => m_callbacksRegistered;
    public int BucketContactCountThisStep { get; private set; } = 0;
    public float BucketContactMaxNormalForceN { get; private set; } = 0.0f;

    private void Awake()
    {
      ResolveReferences();
    }

    private void OnEnable()
    {
      EnsureMonitoring();
    }

    private void Update()
    {
      if ( !m_callbacksRegistered )
        EnsureMonitoring();
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

    public void EnsureMonitoring()
    {
      if ( m_callbacksRegistered )
        return;

      if ( !Simulation.HasInstance || Simulation.Instance == null )
        return;

      ResolveReferences();
      CacheShapeIds();
      if ( m_bucketShapes == null || m_bucketShapes.Length == 0 )
        return;

      foreach ( var bucketShape in m_bucketShapes ) {
        if ( bucketShape == null || bucketShape.NativeGeometry == null )
          return;
      }

      var simulation = Simulation.Instance;
      simulation.StepCallbacks.PreStepForward += OnPreStepForward;
      foreach ( var bucketShape in m_bucketShapes )
        simulation.ContactCallbacks.OnForce( OnBucketShapeForce, bucketShape );

      m_callbacksRegistered = true;
    }

    public void ResetMonitoring()
    {
      BucketContactCountThisStep = 0;
      BucketContactMaxNormalForceN = 0.0f;
    }

    private void UnregisterCallbacks()
    {
      if ( !m_callbacksRegistered )
        return;

      if ( Simulation.HasInstance && Simulation.Instance != null ) {
        Simulation.Instance.StepCallbacks.PreStepForward -= OnPreStepForward;
        Simulation.Instance.ContactCallbacks.Remove( OnBucketShapeForce );
      }

      m_callbacksRegistered = false;
    }

    private void OnPreStepForward()
    {
      ResetMonitoring();
    }

    private bool OnBucketShapeForce( ref ContactData contactData )
    {
      if ( !contactData.HasContactPointForceData )
        return false;

      var component1Shape = contactData.Component1 as Shape;
      var component2Shape = contactData.Component2 as Shape;
      if ( component1Shape == null || component2Shape == null )
        return false;

      if ( !TryResolveBucketExternalPair( component1Shape, component2Shape, out _ ) )
        return false;

      ++BucketContactCountThisStep;
      var normalForceMagnitude = contactData.TotalNormalForce.magnitude;
      if ( normalForceMagnitude > BucketContactMaxNormalForceN )
        BucketContactMaxNormalForceN = normalForceMagnitude;

      return false;
    }

    private bool TryResolveBucketExternalPair( Shape component1Shape,
                                               Shape component2Shape,
                                               out Shape externalShape )
    {
      externalShape = null;

      var component1IsBucket = m_bucketShapeIds.Contains( component1Shape.GetInstanceID() );
      var component2IsBucket = m_bucketShapeIds.Contains( component2Shape.GetInstanceID() );
      if ( component1IsBucket == component2IsBucket )
        return false;

      externalShape = component1IsBucket ? component2Shape : component1Shape;
      if ( externalShape == null )
        return false;

      return !m_machineShapeIds.Contains( externalShape.GetInstanceID() );
    }

    private void CacheShapeIds()
    {
      m_bucketShapeIds.Clear();
      m_machineShapeIds.Clear();
      m_bucketShapes = Array.Empty<Shape>();

      var bucketRoot = ResolveBucketRoot();
      if ( bucketRoot == null )
        return;

      var bucketShapes = new List<Shape>();
      foreach ( var shape in bucketRoot.GetComponentsInChildren<Shape>( true ) ) {
        if ( !ShouldIncludeShape( shape ) || bucketShapes.Contains( shape ) )
          continue;

        bucketShapes.Add( shape );
        m_bucketShapeIds.Add( shape.GetInstanceID() );
      }
      m_bucketShapes = bucketShapes.ToArray();

      var machineRoot = ResolveMachineRoot();
      if ( machineRoot == null )
        return;

      foreach ( var shape in machineRoot.GetComponentsInChildren<Shape>( true ) ) {
        if ( ShouldIncludeShape( shape ) )
          m_machineShapeIds.Add( shape.GetInstanceID() );
      }
    }

    private void ResolveReferences()
    {
      var previousMachineRoot = m_machineRoot;
      var previousBucketReference = m_bucketReference;

      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      if ( !ExcavatorRigLocator.IsSelectable( m_machineRoot ) && m_machineController != null )
        m_machineRoot = m_machineController.MachineRoot;

      if ( !ExcavatorRigLocator.IsSelectable( m_bucketReference ) && m_machineController != null )
        m_bucketReference = m_machineController.BucketReference;

      if ( !ExcavatorRigLocator.IsSelectable( m_bucketReference ) )
        m_bucketReference = ExcavatorRigLocator.ResolveBucketReference( ResolveMachineRoot(), m_bucketReference );

      if ( ( previousMachineRoot != null && previousMachineRoot != m_machineRoot ) ||
           ( previousBucketReference != null && previousBucketReference != m_bucketReference ) ) {
        UnregisterCallbacks();
        ResetMonitoring();
      }
    }

    private Transform ResolveMachineRoot()
    {
      if ( m_machineController != null )
        return m_machineController.MachineRoot;

      return ExcavatorRigLocator.IsSelectable( m_machineRoot ) ? m_machineRoot : null;
    }

    private Transform ResolveBucketRoot()
    {
      if ( ExcavatorRigLocator.IsSelectable( m_bucketReference ) )
        return m_bucketReference;

      return ExcavatorRigLocator.ResolveBucketReference( ResolveMachineRoot(), null );
    }

    private static bool ShouldIncludeShape( Shape shape )
    {
      return shape != null &&
             shape.CollisionsEnabled &&
             shape.NativeGeometry != null;
    }
  }
}

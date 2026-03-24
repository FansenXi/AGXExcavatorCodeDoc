using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

public class SwitchableTargetMassSensor : MonoBehaviour
{
  [SerializeField]
  private TargetMassSensorBase[] m_targetSensors = Array.Empty<TargetMassSensorBase>();

  [SerializeField]
  [Min( 0 )]
  private int m_defaultTargetIndex = 0;

  [SerializeField]
  private bool m_listenForSwitchHotkeys = true;

  [SerializeField]
  private KeyCode m_previousTargetKey = KeyCode.F8;

  [SerializeField]
  private KeyCode m_nextTargetKey = KeyCode.F9;

  private TargetMassSensorBase[] m_runtimeTargets = Array.Empty<TargetMassSensorBase>();
  private int m_currentTargetIndex = 0;

  public int AvailableTargetCount => m_runtimeTargets != null ? m_runtimeTargets.Length : 0;
  public int CurrentTargetIndex => Mathf.Clamp( m_currentTargetIndex, 0, Mathf.Max( AvailableTargetCount - 1, 0 ) );
  public string CurrentTargetName => CurrentTarget != null ? CurrentTarget.TargetName : "None";
  public float MassInBox => CurrentTarget != null ? CurrentTarget.MassInBox : 0.0f;
  public float DepositedMass => CurrentTarget != null ? CurrentTarget.DepositedMass : 0.0f;
  public TargetMassSensorBase CurrentTarget => AvailableTargetCount > 0 ? m_runtimeTargets[ CurrentTargetIndex ] : null;

  private void Awake()
  {
    RefreshTargets();
  }

  private void Update()
  {
    if ( !m_listenForSwitchHotkeys || AvailableTargetCount <= 1 )
      return;

    var cycleDirection = GetTargetCycleDirectionHotkey();
    if ( cycleDirection != 0 )
      CycleTarget( cycleDirection );
  }

  public void RefreshTargets()
  {
    var previousTarget = CurrentTarget;
    var discoveredTargets = BuildRuntimeTargetList();
    m_runtimeTargets = discoveredTargets;

    if ( AvailableTargetCount == 0 ) {
      m_currentTargetIndex = 0;
      return;
    }

    if ( previousTarget != null ) {
      for ( var targetIndex = 0; targetIndex < m_runtimeTargets.Length; ++targetIndex ) {
        if ( m_runtimeTargets[ targetIndex ] == previousTarget ) {
          m_currentTargetIndex = targetIndex;
          return;
        }
      }
    }

    m_currentTargetIndex = Mathf.Clamp( m_defaultTargetIndex, 0, m_runtimeTargets.Length - 1 );
  }

  public bool SetActiveTargetByIndex( int index )
  {
    RefreshTargets();
    if ( index < 0 || index >= AvailableTargetCount || index == CurrentTargetIndex )
      return false;

    m_currentTargetIndex = index;
    return true;
  }

  public bool CycleTarget( int direction )
  {
    RefreshTargets();
    if ( AvailableTargetCount <= 1 )
      return false;

    var normalizedDirection = direction < 0 ? -1 : 1;
    var nextIndex = ( CurrentTargetIndex + normalizedDirection + AvailableTargetCount ) % AvailableTargetCount;
    m_currentTargetIndex = nextIndex;
    return true;
  }

  public string GetAvailableTargetDisplayName( int index )
  {
    RefreshTargets();
    return index >= 0 && index < AvailableTargetCount && m_runtimeTargets[ index ] != null ?
           m_runtimeTargets[ index ].TargetName :
           string.Empty;
  }

  public void ResetMeasurements()
  {
    RefreshTargets();
    foreach ( var targetSensor in m_runtimeTargets ) {
      if ( targetSensor != null )
        targetSensor.ResetMeasurements();
    }
  }

  private TargetMassSensorBase[] BuildRuntimeTargetList()
  {
    if ( HasAssignedEntries( m_targetSensors ) )
      return FilterAssignedTargets( m_targetSensors );

    var discoveredTargets = FindObjectsByType<TargetMassSensorBase>(
      FindObjectsInactive.Include,
      FindObjectsSortMode.None );
    if ( discoveredTargets == null || discoveredTargets.Length == 0 )
      return Array.Empty<TargetMassSensorBase>();

    Array.Sort( discoveredTargets, CompareTargets );
    return FilterAssignedTargets( discoveredTargets );
  }

  private static TargetMassSensorBase[] FilterAssignedTargets( TargetMassSensorBase[] sourceTargets )
  {
    if ( sourceTargets == null || sourceTargets.Length == 0 )
      return Array.Empty<TargetMassSensorBase>();

    var filteredTargets = new List<TargetMassSensorBase>( sourceTargets.Length );
    foreach ( var sourceTarget in sourceTargets ) {
      if ( sourceTarget == null || filteredTargets.Contains( sourceTarget ) )
        continue;

      filteredTargets.Add( sourceTarget );
    }

    return filteredTargets.ToArray();
  }

  private static bool HasAssignedEntries( TargetMassSensorBase[] sensors )
  {
    if ( sensors == null || sensors.Length == 0 )
      return false;

    foreach ( var sensor in sensors ) {
      if ( sensor != null )
        return true;
    }

    return false;
  }

  private static int CompareTargets( TargetMassSensorBase left, TargetMassSensorBase right )
  {
    if ( left == right )
      return 0;

    if ( left == null )
      return 1;

    if ( right == null )
      return -1;

    return string.Compare( left.TargetName, right.TargetName, StringComparison.Ordinal );
  }

  private int GetTargetCycleDirectionHotkey()
  {
#if ENABLE_INPUT_SYSTEM
    var keyboard = Keyboard.current;
    if ( keyboard != null ) {
      if ( IsKeyPressedThisFrame( keyboard, m_previousTargetKey ) )
        return -1;

      if ( IsKeyPressedThisFrame( keyboard, m_nextTargetKey ) )
        return 1;

      return 0;
    }
#endif

    if ( Input.GetKeyDown( m_previousTargetKey ) )
      return -1;

    if ( Input.GetKeyDown( m_nextTargetKey ) )
      return 1;

    return 0;
  }

#if ENABLE_INPUT_SYSTEM
  private static bool IsKeyPressedThisFrame( Keyboard keyboard, KeyCode keyCode )
  {
    if ( keyboard == null )
      return false;

    var key = KeyControlFor( keyboard, keyCode );
    return key != null && key.wasPressedThisFrame;
  }

  private static KeyControl KeyControlFor( Keyboard keyboard, KeyCode keyCode )
  {
    return keyCode switch
    {
      KeyCode.F8 => keyboard.f8Key,
      KeyCode.F9 => keyboard.f9Key,
      _ => null
    };
  }
#endif
}

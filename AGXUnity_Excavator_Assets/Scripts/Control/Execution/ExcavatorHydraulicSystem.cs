using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  /// <summary>
  /// Shared AGX hydraulics network. The current V1 swing circuit is:
  /// FixedVelocityEngine -> Pump -> Supply Pipe -> NeedleValve -> HydraulicMotorActuator(SwingHinge).
  /// Future boom/stick/bucket branches should be attached to the same PowerLine and supply network.
  /// </summary>
  public class ExcavatorHydraulicSystem : ScriptComponent
  {
    /// <summary>Shared supply: a fixed velocity engine drives a pump, and the pump outlet feeds a supply pipe.</summary>
    private sealed class SharedSupply
    {
      public readonly agxDriveTrain.FixedVelocityEngine Engine = null;
      public readonly agxHydraulics.Pump Pump = null;
      public readonly agxHydraulics.Pipe SupplyPipe = null;

      public SharedSupply( agxDriveTrain.FixedVelocityEngine engine,
                           agxHydraulics.Pump pump,
                           agxHydraulics.Pipe supplyPipe )
      {
        Engine = engine;
        Pump = pump;
        SupplyPipe = supplyPipe;
      }

      public float PumpPressure => Pump != null ? (float)Pump.getPressure() : 0.0f;
      public float SupplyFlowRate => SupplyPipe != null ? (float)SupplyPipe.getFlowRate() : 0.0f;
      public float PumpRpm => Engine != null ? (float)Engine.getRPM() : 0.0f;

      public void ApplyPumpCommand( float commandSign, float baseDisplacement, float targetRpm )
      {
        if ( Pump != null )
          Pump.setDisplacement( Mathf.Abs( baseDisplacement ) );

        if ( Engine != null )
          Engine.setTargetRpm( Mathf.Abs( commandSign ) > 0.0f ? Mathf.Sign( commandSign ) * Mathf.Abs( targetRpm ) : 0.0f );
      }

      public void Stop()
      {
        if ( Pump != null )
          Pump.setDisplacement( 0.0 );

        if ( Engine != null )
          Engine.setTargetRpm( 0.0 );
      }
    }

    /// <summary>Swing branch: NeedleValve opening is the swing valve command, and the motor is coupled to SwingHinge.</summary>
    private sealed class SwingBranch
    {
      private readonly agxHydraulics.NeedleValve m_valve = null;
      private readonly agxHydraulics.HydraulicMotorActuator m_motor = null;

      public SwingBranch( agxHydraulics.NeedleValve valve,
                          agxHydraulics.HydraulicMotorActuator motor )
      {
        m_valve = valve;
        m_motor = motor;
      }

      public float ValveOpeningFraction { get; private set; }
      public float BranchFlowRate => m_valve != null ? (float)m_valve.getFlowRate() : 0.0f;
      public float ValveOpeningArea => m_valve != null ? (float)m_valve.getOpeningArea() : 0.0f;

      public void ApplyValveCommand( float command, float deadZone, float maxOpeningFraction )
      {
        var normalized = Mathf.Clamp( command, -1.0f, 1.0f );
        if ( Mathf.Abs( normalized ) < deadZone )
          normalized = 0.0f;

        ValveOpeningFraction = Mathf.Clamp01( Mathf.Abs( normalized ) * Mathf.Clamp01( maxOpeningFraction ) );
        if ( m_valve != null )
          m_valve.setOpeningFraction( ValveOpeningFraction );
      }

      public void Stop()
      {
        ValveOpeningFraction = 0.0f;
        if ( m_valve != null )
          m_valve.setOpeningFraction( 0.0 );
      }
    }

    [SerializeField]
    [Tooltip( "Fluid density in kg/m^3. Captured when the native pipe/valve/motor are created." )]
    [Min( 0.0f )]
    private float m_fluidDensity = 850.0f;

    [SerializeField]
    [Tooltip( "Target RPM for the fixed velocity pump engine. Sign is taken from the swing command." )]
    [Min( 0.0f )]
    private float m_pumpTargetRpm = 1500.0f;

    [SerializeField]
    [Tooltip( "Base pump displacement. V1 keeps displacement positive and flips pump RPM sign from the swing command." )]
    private float m_pumpDisplacement = 0.01f;

    [SerializeField]
    [Tooltip( "Supply pipe length in meters." )]
    [Min( 0.0001f )]
    private float m_supplyPipeLength = 1.0f;

    [SerializeField]
    [Tooltip( "Supply pipe cross section area in m^2." )]
    [Min( 0.000001f )]
    private float m_supplyPipeArea = 0.005f;

    [SerializeField]
    [Tooltip( "Maximum opening area of the swing NeedleValve in m^2." )]
    [Min( 0.000001f )]
    private float m_swingValveMaxOpeningArea = 0.002f;

    [SerializeField]
    [Tooltip( "Maximum opening fraction applied to the swing NeedleValve at full command." )]
    [Range( 0.0f, 1.0f )]
    private float m_swingValveMaxOpeningFraction = 1.0f;

    [SerializeField]
    [Tooltip( "Swing input dead zone." )]
    [Range( 0.0f, 1.0f )]
    private float m_swingCommandDeadZone = 0.05f;

    [SerializeField]
    [Tooltip( "Swing hydraulic motor chamber length in meters." )]
    [Min( 0.0001f )]
    private float m_swingMotorChamberLength = 0.5f;

    [SerializeField]
    [Tooltip( "Swing hydraulic motor chamber area in m^2." )]
    [Min( 0.000001f )]
    private float m_swingMotorChamberArea = 0.002f;

    [SerializeField]
    [Tooltip( "Read-only: current normalized swing command." )]
    private float m_debugSwingCommand = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: swing valve opening fraction." )]
    private float m_debugSwingValveOpeningFraction = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: swing valve opening area." )]
    private float m_debugSwingValveOpeningArea = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: swing branch flow rate." )]
    private float m_debugSwingBranchFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: pump pressure." )]
    private float m_debugPumpPressure = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: supply pipe flow rate." )]
    private float m_debugSupplyFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: fixed velocity pump engine RPM." )]
    private float m_debugPumpRpm = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: current SwingHinge speed." )]
    private float m_debugSwingSpeed = 0.0f;

    public agxPowerLine.PowerLine Native { get; private set; } = null;

    private SharedSupply m_sharedSupply = null;
    private Constraint m_swingConstraint = null;
    private SwingBranch m_swingBranch = null;

    protected override bool Initialize()
    {
      if ( Native != null )
        return true;

      Native = new agxPowerLine.PowerLine();
      var simulation = GetSimulation();
      if ( simulation != null )
        simulation.add( Native );

      return true;
    }

    internal bool TryCreateSwingActuator( Constraint swingHinge, out IExcavatorAxisActuator actuator )
    {
      actuator = null;

      var supply = GetOrCreateSharedSupply();
      var branch = GetOrCreateSwingBranch( swingHinge, supply );
      if ( branch == null )
        return false;

      actuator = new HydraulicSwingAxisActuator( swingHinge, branch, this );
      return true;
    }

    public void StopSwing()
    {
      m_swingBranch?.Stop();
      m_sharedSupply?.Stop();
    }

    private SharedSupply GetOrCreateSharedSupply()
    {
      if ( m_sharedSupply != null )
        return m_sharedSupply;

      if ( Native == null && !Initialize() )
        return null;

      if ( Native == null )
        return null;

      var engine = new agxDriveTrain.FixedVelocityEngine();
      engine.setTargetRpm( 0.0 );

      var pump = new agxHydraulics.Pump( m_pumpDisplacement );
      var supplyPipe = new agxHydraulics.Pipe( m_supplyPipeLength, m_supplyPipeArea, m_fluidDensity );

      engine.connect( pump );
      pump.connect( supplyPipe );

      Native.add( engine );
      Native.add( supplyPipe );

      m_sharedSupply = new SharedSupply( engine, pump, supplyPipe );
      return m_sharedSupply;
    }

    private SwingBranch GetOrCreateSwingBranch( Constraint swingHinge, SharedSupply supply )
    {
      if ( swingHinge == null || swingHinge.Native == null || supply == null || supply.SupplyPipe == null )
        return null;

      if ( m_swingBranch != null && m_swingConstraint == swingHinge )
        return m_swingBranch;

      var nativeHinge = swingHinge.Native.asHinge();
      if ( nativeHinge == null )
        return null;

      var valve = new agxHydraulics.NeedleValve( m_swingValveMaxOpeningArea, m_fluidDensity );
      var motor = new agxHydraulics.HydraulicMotorActuator(
        nativeHinge,
        m_swingMotorChamberLength,
        m_swingMotorChamberArea,
        m_fluidDensity );

      valve.setOpeningFraction( 0.0 );
      supply.SupplyPipe.connect( valve );
      valve.connect( motor );
      Native.add( valve );

      m_swingConstraint = swingHinge;
      m_swingBranch = new SwingBranch( valve, motor );
      return m_swingBranch;
    }

    private void ApplySwingCommand( float command, Constraint swingConstraint, SwingBranch branch, bool immediateStop )
    {
      var nextCommand = immediateStop ? 0.0f : Mathf.Clamp( command, -1.0f, 1.0f );
      if ( Mathf.Abs( nextCommand ) < m_swingCommandDeadZone )
        nextCommand = 0.0f;

      branch.ApplyValveCommand( nextCommand, m_swingCommandDeadZone, m_swingValveMaxOpeningFraction );
      m_sharedSupply?.ApplyPumpCommand( nextCommand, m_pumpDisplacement, m_pumpTargetRpm );
      UpdateSwingDebug( nextCommand, swingConstraint, branch );
    }

    private void UpdateSwingDebug( float command, Constraint swingConstraint, SwingBranch branch )
    {
      m_debugSwingCommand = command;
      m_debugSwingValveOpeningFraction = branch != null ? branch.ValveOpeningFraction : 0.0f;
      m_debugSwingValveOpeningArea = branch != null ? branch.ValveOpeningArea : 0.0f;
      m_debugSwingBranchFlowRate = branch != null ? branch.BranchFlowRate : 0.0f;
      m_debugPumpPressure = m_sharedSupply != null ? m_sharedSupply.PumpPressure : 0.0f;
      m_debugSupplyFlowRate = m_sharedSupply != null ? m_sharedSupply.SupplyFlowRate : 0.0f;
      m_debugPumpRpm = m_sharedSupply != null ? m_sharedSupply.PumpRpm : 0.0f;
      m_debugSwingSpeed = swingConstraint != null ? swingConstraint.GetCurrentSpeed() : 0.0f;
    }

    protected override void OnDestroy()
    {
      StopSwing();

      if ( Native != null ) {
        if ( AGXUnity.Simulation.HasInstance )
          GetSimulation().remove( Native );

        Native.Dispose();
        Native = null;
      }

      m_sharedSupply = null;
      m_swingConstraint = null;
      m_swingBranch = null;

      base.OnDestroy();
    }

    private sealed class HydraulicSwingAxisActuator : IExcavatorAxisActuator
    {
      private readonly Constraint m_constraint = null;
      private readonly SwingBranch m_branch = null;
      private readonly ExcavatorHydraulicSystem m_system = null;

      public HydraulicSwingAxisActuator( Constraint constraint,
                                         SwingBranch branch,
                                         ExcavatorHydraulicSystem system )
      {
        m_constraint = constraint;
        m_branch = branch;
        m_system = system;
      }

      public void Apply( float command, bool immediateStop )
      {
        DisableConstraintControllers();

        if ( m_system == null )
          return;

        m_system.ApplySwingCommand( command, m_constraint, m_branch, immediateStop );
      }

      private void DisableConstraintControllers()
      {
        if ( m_constraint == null )
          return;

        var speedController = m_constraint.GetController<TargetSpeedController>();
        if ( speedController != null ) {
          speedController.Speed = 0.0f;
          speedController.Enable = false;
        }

        var lockController = m_constraint.GetController<LockController>();
        if ( lockController != null )
          lockController.Enable = false;
      }
    }
  }
}

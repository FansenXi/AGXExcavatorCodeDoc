using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public class ExcavatorHydraulicSystem : ScriptComponent
  {
    //共享供油系统
    private sealed class SharedSupply
    {
      public readonly agxDriveTrain.FixedVelocityEngine Engine = null;
      public readonly agxHydraulics.Pump Pump = null;
      public readonly agxHydraulics.Pipe SupplyPipe = null;
      public readonly agxHydraulics.ReliefValve ReliefValve = null;
      public readonly agxHydraulics.NeedleValve MeteringValve = null;
      public readonly agxHydraulics.Pipe PressurePortPipe = null;
      public readonly agxHydraulics.Pipe TankPipe = null;

      public SharedSupply( agxDriveTrain.FixedVelocityEngine engine,
                           agxHydraulics.Pump pump,
                           agxHydraulics.Pipe supplyPipe,
                           agxHydraulics.ReliefValve reliefValve,
                           agxHydraulics.NeedleValve meteringValve,
                           agxHydraulics.Pipe pressurePortPipe,
                           agxHydraulics.Pipe tankPipe )
      {
        Engine = engine;
        Pump = pump;
        SupplyPipe = supplyPipe;
        ReliefValve = reliefValve;
        MeteringValve = meteringValve;
        PressurePortPipe = pressurePortPipe;
        TankPipe = tankPipe;
      }

      public float PumpPressure => Pump != null ? (float)Pump.getPressure() : 0.0f;
      public float SupplyFlowRate => SupplyPipe != null ? (float)SupplyPipe.getFlowRate() : 0.0f;
      public float PressurePortFlowRate => PressurePortPipe != null ? (float)PressurePortPipe.getFlowRate() : 0.0f;
      public float TankFlowRate => TankPipe != null ? (float)TankPipe.getFlowRate() : 0.0f;
      public float PumpRpm => Engine != null ? (float)Engine.getRPM() : 0.0f;
      public float ReliefOpeningFraction => ReliefValve != null ? (float)ReliefValve.getOpeningFraction() : 0.0f;
      public float ReliefFlowRate => ReliefValve != null ? (float)ReliefValve.getDrainFlowRate() : 0.0f;
      public float MeteringOpeningArea => MeteringValve != null ? (float)MeteringValve.getOpeningArea() : 0.0f;

      public void ApplyPumpCommand( float throttle, float baseDisplacement, float targetRpm )
      {
        if ( Pump != null )
          Pump.setDisplacement( Mathf.Abs( baseDisplacement ) );

        if ( Engine != null )
          Engine.setTargetRpm( Mathf.Clamp01( throttle ) * Mathf.Abs( targetRpm ) );
      }

      public void ApplyMeteringCommand( float openingFraction )
      {
        if ( MeteringValve != null )
          MeteringValve.setOpeningFraction( Mathf.Clamp01( openingFraction ) );
      }

      public void Stop()
      {
        if ( Pump != null )
          Pump.setDisplacement( 0.0 );

        if ( Engine != null )
          Engine.setTargetRpm( 0.0 );

        if ( MeteringValve != null )
          MeteringValve.setOpeningFraction( 0.0 );
      }
    }
    //多路阀的swing分支，使用原生 SpoolValve 做离散换向。
    private sealed class SwingBranch
    {
      private readonly agxHydraulics.SpoolValve m_spoolValve = null;
      private readonly agxHydraulics.Pipe m_pressurePortPipe = null;
      private readonly agxHydraulics.Pipe m_motorInputPortPipe = null;
      private readonly agxHydraulics.Pipe m_motorOutputPortPipe = null;
      private readonly agxHydraulics.Pipe m_tankPipe = null;
      private readonly agxHydraulics.HydraulicMotorActuator m_motor = null;

      public SwingBranch( agxHydraulics.SpoolValve spoolValve,
                          agxHydraulics.Pipe pressurePortPipe,
                          agxHydraulics.Pipe motorInputPortPipe,
                          agxHydraulics.Pipe motorOutputPortPipe,
                          agxHydraulics.Pipe tankPipe,
                          agxHydraulics.HydraulicMotorActuator motor )
      {
        m_spoolValve = spoolValve;
        m_pressurePortPipe = pressurePortPipe;
        m_motorInputPortPipe = motorInputPortPipe;
        m_motorOutputPortPipe = motorOutputPortPipe;
        m_tankPipe = tankPipe;
        m_motor = motor;
      }

      public float ValveOpeningFraction { get; private set; }
      public float Direction { get; private set; }
      public float BranchFlowRate => m_motorInputPortPipe != null ? (float)m_motorInputPortPipe.getFlowRate() : 0.0f;
      public float ForwardSupplyFlowRate => Direction > 0.0f && m_motorInputPortPipe != null ? (float)m_motorInputPortPipe.getFlowRate() : 0.0f;
      public float ReverseSupplyFlowRate => Direction < 0.0f && m_motorOutputPortPipe != null ? (float)m_motorOutputPortPipe.getFlowRate() : 0.0f;
      public float ValveOpeningArea => 0.0f;

      public void ApplyValveCommand( float command, float deadZone, float maxOpeningFraction )
      {
        var normalized = Mathf.Clamp( command, -1.0f, 1.0f );
        if ( Mathf.Abs( normalized ) < deadZone )
          normalized = 0.0f;

        if ( m_spoolValve != null )
          m_spoolValve.unlinkAll();

        Direction = Mathf.Sign( normalized );
        ValveOpeningFraction = Mathf.Clamp01( Mathf.Abs( normalized ) * Mathf.Clamp01( maxOpeningFraction ) );
        if ( m_spoolValve == null || Direction == 0.0f )
          return;

        if ( Direction > 0.0f ) {
          m_spoolValve.link( m_pressurePortPipe, m_motorInputPortPipe );
          m_spoolValve.link( m_motorOutputPortPipe, m_tankPipe );
        }
        else {
          m_spoolValve.link( m_pressurePortPipe, m_motorOutputPortPipe );
          m_spoolValve.link( m_motorInputPortPipe, m_tankPipe );
        }
      }

      public void Stop()
      {
        Direction = 0.0f;
        ValveOpeningFraction = 0.0f;
        if ( m_spoolValve != null )
          m_spoolValve.unlinkAll();
      }
    }

    #region 参数配置
    [SerializeField]
    [Tooltip( "Fluid density in kg/m^3. Captured when the native pipe/valve/motor are created." )]
    [Min( 0.0f )]
    private float m_fluidDensity = 850.0f;

    [SerializeField]
    [Tooltip( "Fixed throttle for the hydraulic pump engine in [0,1]. V2 keeps pump power independent from swing command." )]
    [Range( 0.0f, 1.0f )]
    private float m_pumpThrottle = 1.0f;

    [SerializeField]
    [Tooltip( "Target RPM at full pump throttle. Direction is now handled by the swing valve branch." )]
    [Min( 0.0f )]
    private float m_pumpTargetRpm = 1500.0f;

    [SerializeField]
    [Tooltip( "Base pump displacement. V2 keeps displacement and RPM direction positive; the swing valve handles direction." )]
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
    [Tooltip( "Tank/return pipe length in meters. This is a simplified low-pressure reference, not a full reservoir model." )]
    [Min( 0.0001f )]
    private float m_tankPipeLength = 1.0f;

    [SerializeField]
    [Tooltip( "Tank/return pipe cross section area in m^2. Larger values make the return side softer and less restrictive." )]
    [Min( 0.000001f )]
    private float m_tankPipeArea = 0.02f;

    [SerializeField]
    [Tooltip( "Relief valve fully-open drain area in m^2." )]
    [Min( 0.000001f )]
    private float m_reliefValveMaxOpeningArea = 0.004f;

    [SerializeField]
    [Tooltip( "Relief cracking pressure. Units follow AGX native pressure units." )]
    [Min( 0.0f )]
    private float m_reliefCrackingPressure = 10000000.0f;

    [SerializeField]
    [Tooltip( "Pressure interval above cracking pressure where the native relief valve becomes fully open." )]
    [Min( 1.0f )]
    private float m_reliefPressureBand = 2000000.0f;

    [SerializeField]
    [Tooltip( "Maximum opening area of the swing metering NeedleValve in m^2. SpoolValve handles direction." )]
    [Min( 0.000001f )]
    private float m_swingValveMaxOpeningArea = 0.002f;

    [SerializeField]
    [Tooltip( "Maximum opening fraction applied to the swing metering NeedleValve at full command." )]
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
    [Tooltip( "Read-only: swing directional valve sign. +1 opens P-A/B-T, -1 opens P-B/A-T." )]
    private float m_debugSwingValveDirection = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: forward supply valve flow rate." )]
    private float m_debugSwingForwardSupplyFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: reverse supply valve flow rate." )]
    private float m_debugSwingReverseSupplyFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: pump pressure." )]
    private float m_debugPumpPressure = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: supply pipe flow rate." )]
    private float m_debugSupplyFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: tank/return pipe flow rate." )]
    private float m_debugTankFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: relief valve opening fraction." )]
    private float m_debugReliefOpeningFraction = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: relief bypass flow rate." )]
    private float m_debugReliefFlowRate = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: fixed velocity pump engine RPM." )]
    private float m_debugPumpRpm = 0.0f;

    [SerializeField]
    [Tooltip( "Read-only: current SwingHinge speed." )]
    private float m_debugSwingSpeed = 0.0f;
#endregion
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
    //关键入口，创建供油系统，创建swing分支，返回适配器
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
      var reliefValve = new agxHydraulics.ReliefValve(
        m_reliefCrackingPressure,
        m_reliefCrackingPressure + Mathf.Max( m_reliefPressureBand, 1.0f ),
        m_reliefValveMaxOpeningArea );
      var meteringValve = new agxHydraulics.NeedleValve( m_swingValveMaxOpeningArea, m_fluidDensity );
      var pressurePortPipe = new agxHydraulics.Pipe( m_supplyPipeLength, m_supplyPipeArea, m_fluidDensity );
      var tankPipe = new agxHydraulics.Pipe( m_tankPipeLength, m_tankPipeArea, m_fluidDensity );

      engine.connect( pump );
      pump.connect( supplyPipe );
      supplyPipe.connect( reliefValve );
      reliefValve.connect( meteringValve );
      meteringValve.connect( pressurePortPipe );
      meteringValve.setOpeningFraction( 0.0 );

      Native.add( engine );
      Native.add( supplyPipe );
      Native.add( reliefValve );
      Native.add( meteringValve );
      Native.add( pressurePortPipe );
      Native.add( tankPipe );

      m_sharedSupply = new SharedSupply( engine, pump, supplyPipe, reliefValve, meteringValve, pressurePortPipe, tankPipe );
      return m_sharedSupply;
    }

    private SwingBranch GetOrCreateSwingBranch( Constraint swingHinge, SharedSupply supply )
    {
      if ( swingHinge == null ||
           swingHinge.Native == null ||
           supply == null ||
           supply.PressurePortPipe == null ||
           supply.TankPipe == null )
        return null;

      if ( m_swingBranch != null && m_swingConstraint == swingHinge )
        return m_swingBranch;

      var nativeHinge = swingHinge.Native.asHinge();
      if ( nativeHinge == null )
        return null;

      var spoolValve = new agxHydraulics.SpoolValve();
      var motorInputPortPipe = new agxHydraulics.Pipe( m_swingMotorChamberLength, m_swingMotorChamberArea, m_fluidDensity );
      var motorOutputPortPipe = new agxHydraulics.Pipe( m_swingMotorChamberLength, m_swingMotorChamberArea, m_fluidDensity );
      var motor = new agxHydraulics.HydraulicMotorActuator(
        nativeHinge,
        m_swingMotorChamberLength,
        m_swingMotorChamberArea,
        m_fluidDensity );

      motorInputPortPipe.connect( agxPowerLine.Side.OUTPUT, agxPowerLine.Side.INPUT, motor );
      motor.connect( agxPowerLine.Side.OUTPUT, agxPowerLine.Side.INPUT, motorOutputPortPipe );

      spoolValve.connect( agxPowerLine.Side.INPUT, agxPowerLine.Side.OUTPUT, supply.PressurePortPipe );
      spoolValve.connect( agxPowerLine.Side.INPUT, agxPowerLine.Side.INPUT, motorInputPortPipe );
      spoolValve.connect( agxPowerLine.Side.INPUT, agxPowerLine.Side.OUTPUT, motorOutputPortPipe );
      spoolValve.connect( agxPowerLine.Side.INPUT, agxPowerLine.Side.INPUT, supply.TankPipe );

      Native.add( spoolValve );
      Native.add( motorInputPortPipe );
      Native.add( motorOutputPortPipe );

      m_swingConstraint = swingHinge;
      m_swingBranch = new SwingBranch(
        spoolValve,
        supply.PressurePortPipe,
        motorInputPortPipe,
        motorOutputPortPipe,
        supply.TankPipe,
        motor );
      return m_swingBranch;
    }

    private void ApplySwingCommand( float command, Constraint swingConstraint, SwingBranch branch, bool immediateStop )
    {
      var nextCommand = immediateStop ? 0.0f : Mathf.Clamp( command, -1.0f, 1.0f );
      if ( Mathf.Abs( nextCommand ) < m_swingCommandDeadZone )
        nextCommand = 0.0f;

      branch.ApplyValveCommand( nextCommand, m_swingCommandDeadZone, m_swingValveMaxOpeningFraction );
      m_sharedSupply?.ApplyPumpCommand( m_pumpThrottle, m_pumpDisplacement, m_pumpTargetRpm );
      m_sharedSupply?.ApplyMeteringCommand( branch.ValveOpeningFraction );
      UpdateSwingDebug( nextCommand, swingConstraint, branch );
    }

    private void UpdateSwingDebug( float command, Constraint swingConstraint, SwingBranch branch )
    {
      m_debugSwingCommand = command;
      m_debugSwingValveOpeningFraction = branch != null ? branch.ValveOpeningFraction : 0.0f;
      m_debugSwingValveOpeningArea = m_sharedSupply != null ? m_sharedSupply.MeteringOpeningArea : 0.0f;
      m_debugSwingBranchFlowRate = branch != null ? branch.BranchFlowRate : 0.0f;
      m_debugSwingValveDirection = branch != null ? branch.Direction : 0.0f;
      m_debugSwingForwardSupplyFlowRate = branch != null ? branch.ForwardSupplyFlowRate : 0.0f;
      m_debugSwingReverseSupplyFlowRate = branch != null ? branch.ReverseSupplyFlowRate : 0.0f;
      m_debugPumpPressure = m_sharedSupply != null ? m_sharedSupply.PumpPressure : 0.0f;
      m_debugSupplyFlowRate = m_sharedSupply != null ? m_sharedSupply.SupplyFlowRate : 0.0f;
      m_debugTankFlowRate = m_sharedSupply != null ? m_sharedSupply.TankFlowRate : 0.0f;
      m_debugReliefOpeningFraction = m_sharedSupply != null ? m_sharedSupply.ReliefOpeningFraction : 0.0f;
      m_debugReliefFlowRate = m_sharedSupply != null ? m_sharedSupply.ReliefFlowRate : 0.0f;
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

    //适配器，把“控制系统”接到“液压系统”，外部只调用Apply(command)
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

using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public enum ExcavatorHydraulicSwingNeutralMode
  {
    BrakeAtZeroFlow,
    CoastAfterFlowDecay
  }

  public class ExcavatorHydraulicSystem : ScriptComponent
  {
    private sealed class SwingHydraulicBranch
    {
      private readonly agxHydraulics.ConstantFlowValve m_flowSource = null;
      private float m_commandedFlowRate = 0.0f;
      private bool m_hasDrivenSinceNeutral = false;

      public SwingHydraulicBranch( agxHydraulics.ConstantFlowValve flowSource )
      {
        m_flowSource = flowSource;
      }

      public float TargetFlowRate { get; private set; }
      public float CommandedFlowRate => m_commandedFlowRate;
      public float ActualFlowRate => m_flowSource != null ? (float)m_flowSource.getFlowRate() : 0.0f;
      public bool IsFlowSourceEnabled => m_flowSource != null && m_flowSource.getEnable();

      public void ApplyNormalizedCommand( float command,
                                          float maxFlowRate,
                                          float deadZone,
                                          float flowRiseRate,
                                          float flowFallRate,
                                          ExcavatorHydraulicSwingNeutralMode neutralMode,
                                          float coastDisableFlowThreshold,
                                          float coastStopSpeed,
                                          float currentSpeed,
                                          float deltaTime,
                                          bool immediateStop )
      {
        if ( m_flowSource == null )
          return;

        var normalized = Mathf.Clamp( command, -1.0f, 1.0f );
        if ( Mathf.Abs( normalized ) < deadZone )
          normalized = 0.0f;

        if ( Mathf.Abs( normalized ) > 0.0f )
          m_hasDrivenSinceNeutral = true;

        TargetFlowRate = immediateStop ? 0.0f : normalized * Mathf.Abs( maxFlowRate );
        var flowRateLimit = Mathf.Abs( TargetFlowRate ) > Mathf.Abs( m_commandedFlowRate ) ?
                            flowRiseRate :
                            flowFallRate;
        var maxDeltaFlow = Mathf.Abs( flowRateLimit ) * Mathf.Max( deltaTime, 0.0f );
        m_commandedFlowRate = immediateStop ?
                              0.0f :
                              Mathf.MoveTowards( m_commandedFlowRate, TargetFlowRate, maxDeltaFlow );

        if ( immediateStop || Mathf.Abs( currentSpeed ) <= Mathf.Abs( coastStopSpeed ) )
          m_hasDrivenSinceNeutral = false;

        var shouldCoast = m_hasDrivenSinceNeutral &&
                          neutralMode == ExcavatorHydraulicSwingNeutralMode.CoastAfterFlowDecay &&
                          Mathf.Abs( TargetFlowRate ) < 1.0e-6f &&
                          Mathf.Abs( m_commandedFlowRate ) <= Mathf.Abs( coastDisableFlowThreshold );

        m_flowSource.setTargetFlowRate( m_commandedFlowRate );
        m_flowSource.setEnable( !shouldCoast );
      }

      public void Stop()
      {
        TargetFlowRate = 0.0f;
        m_commandedFlowRate = 0.0f;
        m_hasDrivenSinceNeutral = false;

        if ( m_flowSource == null )
          return;

        m_flowSource.setTargetFlowRate( 0.0 );
        m_flowSource.setEnable( true );
      }
    }

    [SerializeField]
    [Min( 0.0f )]
    private float m_fluidDensity = 850.0f;

    [SerializeField]
    [Min( 0.0001f )]
    private float m_swingChamberLength = 0.5f;

    [SerializeField]
    [Min( 0.000001f )]
    private float m_swingChamberArea = 0.002f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_swingMaxFlowRate = 0.01f;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_swingCommandDeadZone = 0.05f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_swingFlowRiseRate = 1.0f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_swingFlowFallRate = 0.25f;

    [SerializeField]
    private ExcavatorHydraulicSwingNeutralMode m_swingNeutralMode = ExcavatorHydraulicSwingNeutralMode.CoastAfterFlowDecay;

    [SerializeField]
    [Min( 0.0f )]
    private float m_swingCoastDisableFlowThreshold = 0.002f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_swingCoastStopSpeed = 0.02f;

    [SerializeField]
    private float m_debugSwingCommand = 0.0f;

    [SerializeField]
    private float m_debugSwingTargetFlowRate = 0.0f;

    [SerializeField]
    private float m_debugSwingCommandedFlowRate = 0.0f;

    [SerializeField]
    private float m_debugSwingActualFlowRate = 0.0f;

    [SerializeField]
    private float m_debugSwingSpeed = 0.0f;

    [SerializeField]
    private bool m_debugSwingFlowSourceEnabled = false;

    public agxPowerLine.PowerLine Native { get; private set; } = null;

    private Constraint m_swingConstraint = null;
    private agxHydraulics.HydraulicMotorActuator m_swingMotor = null;
    private SwingHydraulicBranch m_swingBranch = null;

    public float SwingMaxFlowRate => m_swingMaxFlowRate;
    public float SwingCommandDeadZone => m_swingCommandDeadZone;
    public float SwingFlowRiseRate => m_swingFlowRiseRate;
    public float SwingFlowFallRate => m_swingFlowFallRate;
    public ExcavatorHydraulicSwingNeutralMode SwingNeutralMode => m_swingNeutralMode;
    public float SwingCoastDisableFlowThreshold => m_swingCoastDisableFlowThreshold;
    public float SwingCoastStopSpeed => m_swingCoastStopSpeed;

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

      var branch = GetOrCreateSwingBranch( swingHinge );
      if ( branch == null )
        return false;

      actuator = new HydraulicSwingAxisActuator( swingHinge, branch, this );
      return true;
    }

    public void StopSwing()
    {
      m_swingBranch?.Stop();
    }

    private SwingHydraulicBranch GetOrCreateSwingBranch( Constraint swingHinge )
    {
      if ( swingHinge == null || swingHinge.Native == null )
        return null;

      if ( m_swingBranch != null && m_swingConstraint == swingHinge )
        return m_swingBranch;

      if ( Native == null && !Initialize() )
        return null;

      if ( Native == null )
        return null;

      var nativeHinge = swingHinge.Native.asHinge();
      if ( nativeHinge == null )
        return null;

      var flowSource = new agxHydraulics.ConstantFlowValve(
        m_swingChamberLength,
        m_swingChamberArea,
        m_fluidDensity,
        0.0,
        true );
      var motor = new agxHydraulics.HydraulicMotorActuator(
        nativeHinge,
        m_swingChamberLength,
        m_swingChamberArea,
        m_fluidDensity );

      flowSource.connect( motor );
      Native.add( flowSource );

      flowSource.setAllowPumping( true );
      flowSource.setEnable( true );
      flowSource.setTargetFlowRate( 0.0 );

      m_swingConstraint = swingHinge;
      m_swingMotor = motor;
      m_swingBranch = new SwingHydraulicBranch( flowSource );
      return m_swingBranch;
    }

    private float GetSimulationDeltaTime()
    {
      var simulation = GetSimulation();
      return simulation != null ? (float)simulation.getTimeStep() : Time.deltaTime;
    }

    private void UpdateSwingDebug( float command, Constraint swingConstraint, SwingHydraulicBranch branch )
    {
      m_debugSwingCommand = command;

      if ( branch == null ) {
        m_debugSwingTargetFlowRate = 0.0f;
        m_debugSwingCommandedFlowRate = 0.0f;
        m_debugSwingActualFlowRate = 0.0f;
        m_debugSwingFlowSourceEnabled = false;
      }
      else {
        m_debugSwingTargetFlowRate = branch.TargetFlowRate;
        m_debugSwingCommandedFlowRate = branch.CommandedFlowRate;
        m_debugSwingActualFlowRate = branch.ActualFlowRate;
        m_debugSwingFlowSourceEnabled = branch.IsFlowSourceEnabled;
      }

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

      m_swingConstraint = null;
      m_swingMotor = null;
      m_swingBranch = null;

      base.OnDestroy();
    }

    private sealed class HydraulicSwingAxisActuator : IExcavatorAxisActuator
    {
      private readonly Constraint m_constraint = null;
      private readonly SwingHydraulicBranch m_branch = null;
      private readonly ExcavatorHydraulicSystem m_system = null;

      public HydraulicSwingAxisActuator( Constraint constraint,
                                         SwingHydraulicBranch branch,
                                         ExcavatorHydraulicSystem system )
      {
        m_constraint = constraint;
        m_branch = branch;
        m_system = system;
      }

      public void Apply( float command, bool immediateStop )
      {
        DisableConstraintControllers();

        var nextCommand = immediateStop ? 0.0f : command;
        m_branch.ApplyNormalizedCommand(
          nextCommand,
          m_system != null ? m_system.SwingMaxFlowRate : 0.0f,
          m_system != null ? m_system.SwingCommandDeadZone : 0.0f,
          m_system != null ? m_system.SwingFlowRiseRate : 0.0f,
          m_system != null ? m_system.SwingFlowFallRate : 0.0f,
          m_system != null ? m_system.SwingNeutralMode : ExcavatorHydraulicSwingNeutralMode.BrakeAtZeroFlow,
          m_system != null ? m_system.SwingCoastDisableFlowThreshold : 0.0f,
          m_system != null ? m_system.SwingCoastStopSpeed : 0.0f,
          m_constraint != null ? m_constraint.GetCurrentSpeed() : 0.0f,
          m_system != null ? m_system.GetSimulationDeltaTime() : Time.deltaTime,
          immediateStop );
        if ( m_system != null )
          m_system.UpdateSwingDebug( nextCommand, m_constraint, m_branch );
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

using AGXUnity;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Execution
{
  public class ExcavatorHydraulicSystem : ScriptComponent
  {
    private sealed class SwingHydraulicBranch
    {
      private readonly agxHydraulics.ConstantFlowValve m_flowSource = null;

      public SwingHydraulicBranch( agxHydraulics.ConstantFlowValve flowSource )
      {
        m_flowSource = flowSource;
      }

      public void ApplyNormalizedCommand( float command, float maxFlowRate, float deadZone )
      {
        if ( m_flowSource == null )
          return;

        var normalized = Mathf.Clamp( command, -1.0f, 1.0f );
        if ( Mathf.Abs( normalized ) < deadZone )
          normalized = 0.0f;

        m_flowSource.setEnable( true );
        m_flowSource.setTargetFlowRate( normalized * Mathf.Abs( maxFlowRate ) );
      }

      public void Stop()
      {
        ApplyNormalizedCommand( 0.0f, 0.0f, 0.0f );
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

    public agxPowerLine.PowerLine Native { get; private set; } = null;

    private Constraint m_swingConstraint = null;
    private agxHydraulics.HydraulicMotorActuator m_swingMotor = null;
    private SwingHydraulicBranch m_swingBranch = null;

    public float SwingMaxFlowRate => m_swingMaxFlowRate;
    public float SwingCommandDeadZone => m_swingCommandDeadZone;

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
          m_system != null ? m_system.SwingCommandDeadZone : 0.0f );
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

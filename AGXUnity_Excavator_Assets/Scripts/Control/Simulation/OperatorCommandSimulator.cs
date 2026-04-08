using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Simulation
{
  public class OperatorCommandSimulator : MonoBehaviour
  {
    [SerializeField]
    private AxisResponseProfile m_leftStickX = new AxisResponseProfile();

    [SerializeField]
    private AxisResponseProfile m_leftStickY = new AxisResponseProfile();

    [SerializeField]
    private AxisResponseProfile m_rightStickX = new AxisResponseProfile();

    [SerializeField]
    private AxisResponseProfile m_rightStickY = new AxisResponseProfile();

    [SerializeField]
    private AxisResponseProfile m_drive = new AxisResponseProfile();

    [SerializeField]
    private AxisResponseProfile m_steer = new AxisResponseProfile();

    public OperatorCommand CurrentCommand { get; private set; }

    public OperatorCommand Simulate( OperatorCommand rawCommand, float deltaTime )
    {
      CurrentCommand = new OperatorCommand
      {
        LeftStickX = m_leftStickX.Apply( rawCommand.LeftStickX, CurrentCommand.LeftStickX, deltaTime ),
        LeftStickY = m_leftStickY.Apply( rawCommand.LeftStickY, CurrentCommand.LeftStickY, deltaTime ),
        RightStickX = m_rightStickX.Apply( rawCommand.RightStickX, CurrentCommand.RightStickX, deltaTime ),
        RightStickY = m_rightStickY.Apply( rawCommand.RightStickY, CurrentCommand.RightStickY, deltaTime ),
        Drive = m_drive.Apply( rawCommand.Drive, CurrentCommand.Drive, deltaTime ),
        Steer = m_steer.Apply( rawCommand.Steer, CurrentCommand.Steer, deltaTime ),
        ResetRequested = rawCommand.ResetRequested,
        StartEpisodeRequested = rawCommand.StartEpisodeRequested,
        StopEpisodeRequested = rawCommand.StopEpisodeRequested
      }.ClampAxes();

      return CurrentCommand;
    }

    public void ResetState()
    {
      CurrentCommand = OperatorCommand.Zero;
    }
  }
}

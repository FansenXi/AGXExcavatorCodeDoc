using System.Globalization;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  public struct EpisodeCommandSourceContext
  {
    public int EpisodeIndex;
    public float FixedDeltaTimeSec;
  }

  public interface IEpisodeLifecycleAware
  {
    void OnEpisodeStarted( EpisodeCommandSourceContext context );

    void OnEpisodeStopped( string reason );
  }

  public interface IActCommandDiagnostics
  {
    bool IsBackendReady { get; }
    bool IsCommandTimedOut { get; }
    int LastResponseSequence { get; }
    float LastInferenceTimeMs { get; }
    string CurrentSessionId { get; }
    string LastBackendStatus { get; }
  }

  [System.Serializable]
  public struct HardwareInputSnapshot
  {
    public float LeftStickX;
    public float LeftStickY;
    public float RightStickX;
    public float RightStickY;
    public float Drive;
    public float Steer;
    public float ResetButton;
    public float StartEpisodeButton;
    public float StopEpisodeButton;

    public static HardwareInputSnapshot Zero => new HardwareInputSnapshot();

    public string ToCompactString()
    {
      return string.Format(
        CultureInfo.InvariantCulture,
        "LX {0:+0.00;-0.00;0.00}  LY {1:+0.00;-0.00;0.00}  RX {2:+0.00;-0.00;0.00}  RY {3:+0.00;-0.00;0.00}  D {4:+0.00;-0.00;0.00}  S {5:+0.00;-0.00;0.00}",
        LeftStickX,
        LeftStickY,
        RightStickX,
        RightStickY,
        Drive,
        Steer );
    }
  }

  public interface IHardwareCommandDiagnostics
  {
    bool DeviceConnected { get; }
    string DeviceDisplayName { get; }
    string ProfileName { get; }
    string BindingStatus { get; }
    string LastRawInputSummary { get; }
    HardwareInputSnapshot LastRawInputSnapshot { get; }
  }

  public abstract class OperatorCommandSourceBehaviour : MonoBehaviour, IOperatorCommandSource
  {
    public abstract string SourceName { get; }

    public abstract OperatorCommand ReadCommand();
  }
}

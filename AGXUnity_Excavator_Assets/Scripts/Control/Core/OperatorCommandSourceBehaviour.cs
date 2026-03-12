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

  public abstract class OperatorCommandSourceBehaviour : MonoBehaviour, IOperatorCommandSource
  {
    public abstract string SourceName { get; }

    public abstract OperatorCommand ReadCommand();
  }
}

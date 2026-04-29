using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public interface IActBackendClient
  {
    bool IsReady { get; }

    void BeginEpisode( ActEpisodeConfig config, string sessionId );

    void EndEpisode( string reason, string sessionId, int seq );

    void SubmitObservation( ActStepRequest request );

    bool TryGetLatestResult( out ActStepResponse response );
  }

  public abstract class ActBackendClientBehaviour : MonoBehaviour, IActBackendClient
  {
    public abstract bool IsReady { get; }

    public abstract void BeginEpisode( ActEpisodeConfig config, string sessionId );

    public abstract void EndEpisode( string reason, string sessionId, int seq );

    public abstract void SubmitObservation( ActStepRequest request );

    public abstract bool TryGetLatestResult( out ActStepResponse response );
  }
}

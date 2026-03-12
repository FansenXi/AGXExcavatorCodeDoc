using AGXUnity_Excavator.Scripts.Experiment;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  public class ExperimentHUD : MonoBehaviour
  {
    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private Rect m_rect = new Rect( 16.0f, 16.0f, 460.0f, 270.0f );

    private GUIStyle m_style = null;

    private void Awake()
    {
      if ( m_episodeManager == null )
        m_episodeManager = GetComponent<EpisodeManager>();
    }

    private void OnGUI()
    {
      if ( m_episodeManager == null )
        return;

      if ( m_style == null ) {
        m_style = new GUIStyle( GUI.skin.label )
        {
          alignment = TextAnchor.UpperLeft,
          richText = true,
          wordWrap = true
        };
      }

      GUILayout.BeginArea( m_rect, GUI.skin.box );
      GUILayout.Label( "<b>Experiment HUD</b>", m_style );
      GUILayout.Label( $"Episode: {m_episodeManager.CurrentEpisodeIndex}    Running: {m_episodeManager.IsEpisodeRunning}", m_style );
      GUILayout.Label( $"Source: {m_episodeManager.CurrentSourceName}", m_style );
      GUILayout.Label( "Controls: R reset, Enter start, Backspace stop", m_style );
      if ( m_episodeManager.CurrentSourceName == "ACT" ) {
        GUILayout.Label( $"Backend ready: {m_episodeManager.CurrentSourceBackendReady}    Timeout: {m_episodeManager.CurrentSourceTimedOut}", m_style );
        GUILayout.Label( $"ACT seq: {m_episodeManager.CurrentSourceSequence}    Infer: {m_episodeManager.CurrentSourceInferenceTimeMs:0.0} ms", m_style );
        GUILayout.Label( $"ACT session: {m_episodeManager.CurrentSourceSessionId}    Status: {m_episodeManager.CurrentSourceBackendStatus}", m_style );
      }

      GUILayout.Space( 6.0f );
      GUILayout.Label( $"Raw: {m_episodeManager.LastRawCommand.ToCompactString()}", m_style );
      GUILayout.Label( $"Sim: {m_episodeManager.LastSimulatedCommand.ToCompactString()}", m_style );
      GUILayout.Label( $"Act: {m_episodeManager.LastActuationCommand.ToCompactString()}", m_style );
      GUILayout.Space( 6.0f );
      GUILayout.Label( $"Mass in bucket: {m_episodeManager.MassInBucket:0.00} kg", m_style );
      GUILayout.Label( $"Excavated mass: {m_episodeManager.ExcavatedMass:0.00} kg", m_style );
      GUILayout.Label( $"Excavated volume: {m_episodeManager.ExcavatedVolume:0.000} m^3", m_style );
      GUILayout.Space( 6.0f );
      GUILayout.Label( $"Last log: {m_episodeManager.LastSavedPath}", m_style );
      GUILayout.EndArea();
    }
  }
}

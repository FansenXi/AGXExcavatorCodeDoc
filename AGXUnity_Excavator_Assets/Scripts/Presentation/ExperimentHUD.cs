using System;
using AGXUnity_Excavator.Scripts.Experiment;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  public class ExperimentHUD : MonoBehaviour
  {
    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private Rect m_rect = new Rect( 16.0f, 16.0f, 460.0f, 380.0f );

    [SerializeField]
    private bool m_showRuntimeConfig = true;

    private GUIStyle m_style = null;
    private TrackedCameraWindow[] m_cameraWindows = Array.Empty<TrackedCameraWindow>();
    private float m_nextRuntimeRefreshTime = 0.0f;

    private void Awake()
    {
      if ( m_episodeManager == null )
        m_episodeManager = GetComponent<EpisodeManager>();

      RefreshRuntimeTargets();
    }

    private void Update()
    {
      if ( Time.unscaledTime < m_nextRuntimeRefreshTime )
        return;

      RefreshRuntimeTargets();
      m_nextRuntimeRefreshTime = Time.unscaledTime + 1.0f;
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
      GUILayout.BeginHorizontal();
      GUILayout.Label( "<b>Experiment HUD</b>", m_style );
      if ( GUILayout.Button( m_showRuntimeConfig ? "Hide Menu" : "Show Menu", GUILayout.Width( 96.0f ) ) )
        m_showRuntimeConfig = !m_showRuntimeConfig;
      GUILayout.EndHorizontal();
      GUILayout.Label( $"Episode: {m_episodeManager.CurrentEpisodeIndex}    Running: {m_episodeManager.IsEpisodeRunning}", m_style );
      GUILayout.Label( $"Source: {m_episodeManager.CurrentSourceName}", m_style );
      GUILayout.Label( "Controls: R reset, Enter start, Backspace stop", m_style );
      if ( m_showRuntimeConfig )
        DrawRuntimeConfig();

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

    private void DrawRuntimeConfig()
    {
      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>Runtime Config</b>", m_style );

      if ( m_episodeManager.AvailableSourceCount > 0 ) {
        GUILayout.Label( "Control source (switching restarts the active episode):", m_style );
        for ( var sourceIndex = 0; sourceIndex < m_episodeManager.AvailableSourceCount; ++sourceIndex ) {
          var isCurrentSource = sourceIndex == m_episodeManager.CurrentSourceIndex;
          var buttonLabel = isCurrentSource ?
                            $"[{m_episodeManager.GetAvailableSourceDisplayName( sourceIndex )}] Active" :
                            m_episodeManager.GetAvailableSourceDisplayName( sourceIndex );

          GUI.enabled = !isCurrentSource;
          if ( GUILayout.Button( buttonLabel ) )
            m_episodeManager.SetCommandSourceByIndex( sourceIndex );
          GUI.enabled = true;
        }
      }
      else {
        GUILayout.Label( "No operator command sources found in the current rig.", m_style );
      }

      if ( m_cameraWindows.Length > 0 ) {
        GUILayout.Space( 4.0f );
        GUILayout.Label( "Auxiliary views:", m_style );
        for ( var windowIndex = 0; windowIndex < m_cameraWindows.Length; ++windowIndex ) {
          var cameraWindow = m_cameraWindows[ windowIndex ];
          if ( cameraWindow == null )
            continue;

          var isVisible = GUILayout.Toggle( cameraWindow.IsVisible, cameraWindow.ViewName );
          if ( isVisible != cameraWindow.IsVisible )
            cameraWindow.IsVisible = isVisible;
        }
      }
    }

    private void RefreshRuntimeTargets()
    {
      if ( m_episodeManager == null )
        m_episodeManager = GetComponent<EpisodeManager>();

      m_episodeManager?.RefreshAvailableSources();

      m_cameraWindows = FindObjectsByType<TrackedCameraWindow>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );
      Array.Sort( m_cameraWindows, CompareCameraWindows );
    }

    private static int CompareCameraWindows( TrackedCameraWindow left, TrackedCameraWindow right )
    {
      if ( left == right )
        return 0;

      if ( left == null )
        return 1;

      if ( right == null )
        return -1;

      return string.Compare( left.ViewName, right.ViewName, StringComparison.Ordinal );
    }
  }
}

using System;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  public class ExperimentHUD : MonoBehaviour
  {
    private const string GoodColor = "#7CFC00";
    private const string WarnColor = "#FFD166";
    private const string BadColor = "#F4A261";
    private const string NeutralColor = "#B0B0B0";
    private const int HudWindowId = 0xE85A11;
    private const float WindowMargin = 16.0f;
    private const float MinWindowWidth = 620.0f;
    private const float MinWindowHeight = 360.0f;
    private const float MinimizedWindowHeight = 72.0f;
    private const float DragHandleHeight = 44.0f;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    [SerializeField]
    private AgxSimStepAckServer m_stepAckServer = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private Rect m_rect = new Rect( 24.0f, 24.0f, 760.0f, 900.0f );

    [SerializeField]
    private bool m_showRuntimeConfig = true;

    [SerializeField]
    private bool m_showStepAckDebug = true;

    [SerializeField]
    private bool m_showPlannerDebug = true;

    [SerializeField]
    private bool m_showCalibrationDebug = true;

    [SerializeField]
    private bool m_isMinimized = false;

    [SerializeField]
    [Min( 12.0f )]
    private float m_fontSize = 20.0f;

    [SerializeField]
    [Min( 14.0f )]
    private float m_titleFontSize = 24.0f;

    [SerializeField]
    [Min( 0.05f )]
    private float m_speedSliderMax = 1.5f;

    [SerializeField]
    [Min( 0.05f )]
    private float m_accelerationSliderMax = 2.0f;

    private GUIStyle m_style = null;
    private GUIStyle m_titleStyle = null;
    private GUIStyle m_windowStyle = null;
    private GUIStyle m_buttonStyle = null;
    private GUIStyle m_toggleStyle = null;
    private GUIStyle m_textFieldStyle = null;
    private GUIStyle m_popupTitleStyle = null;
    private GUIStyle m_popupBodyStyle = null;
    private TrackedCameraWindow[] m_cameraWindows = Array.Empty<TrackedCameraWindow>();
    private float m_nextRuntimeRefreshTime = 0.0f;
    private string m_calibrationProfileNameDraft = string.Empty;
    private Vector2 m_scrollPosition = Vector2.zero;
    private float m_restoredWindowHeight = 900.0f;
    private float m_currentVisibleWindowHeight = 900.0f;
    private int m_cachedFontSize = -1;
    private int m_cachedTitleFontSize = -1;
    private PlannerDecisionVisualizer m_plannerDecisionVisualizer = null;

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
      if ( m_episodeManager == null && m_observationCollector == null )
        return;

      EnsureStyles();
      if ( !m_isMinimized )
        m_restoredWindowHeight = Mathf.Max( MinWindowHeight, m_rect.height );

      var windowRect = m_rect;
      windowRect.height = GetCurrentWindowHeight();
      windowRect = ClampWindowRect( windowRect );
      m_currentVisibleWindowHeight = windowRect.height;
      m_rect = GUILayout.Window(
        HudWindowId,
        windowRect,
        DrawHudWindow,
        string.Empty,
        m_windowStyle,
        GUILayout.Width( windowRect.width ),
        GUILayout.Height( windowRect.height ) );

      if ( m_isMinimized )
        m_rect.height = m_restoredWindowHeight;
      else
        m_restoredWindowHeight = Mathf.Max( MinWindowHeight, m_rect.height );

      var visibleStoredRect = m_rect;
      visibleStoredRect.height = GetCurrentWindowHeight();
      visibleStoredRect = ClampWindowRect( visibleStoredRect );
      m_rect.x = visibleStoredRect.x;
      m_rect.y = visibleStoredRect.y;
      m_rect.width = visibleStoredRect.width;
      if ( !m_isMinimized )
        m_rect.height = visibleStoredRect.height;

      if ( m_episodeManager != null && m_episodeManager.ShouldShowTransitionInputCutWarning )
        DrawReleaseInputPopup();
    }

    private void DrawHudWindow( int windowId )
    {
      var collectorTaskState = m_observationCollector != null ? m_observationCollector.LastTaskState : null;
      var useStepAckTelemetry = ShouldUseStepAckTelemetry( collectorTaskState );
      var displayedMassInBucket = GetDisplayedMassInBucket( useStepAckTelemetry, collectorTaskState );
      var displayedMassInTargetBox = GetDisplayedMassInTargetBox( useStepAckTelemetry, collectorTaskState );
      var displayedDepositedMassInTargetBox = GetDisplayedDepositedMassInTargetBox( useStepAckTelemetry, collectorTaskState );
      var displayedMinDistanceToTarget = GetDisplayedMinDistanceToTarget( useStepAckTelemetry, collectorTaskState );
      var displayedMinDistanceToDigArea = GetDisplayedMinDistanceToDigArea( useStepAckTelemetry, collectorTaskState );
      var displayedBucketDepthBelowDigAreaPlane = GetDisplayedBucketDepthBelowDigAreaPlane( useStepAckTelemetry, collectorTaskState );
      var displayedTargetHardCollisionCount = GetDisplayedTargetHardCollisionCount( useStepAckTelemetry, collectorTaskState );
      var displayedTargetContactMaxNormalForceN = GetDisplayedTargetContactMaxNormalForceN( useStepAckTelemetry, collectorTaskState );
      var displayedBucketTouchingDigArea =
        displayedMinDistanceToDigArea >= 0.0f &&
        displayedMinDistanceToDigArea <= GetDigAreaTouchTolerance();
      var displayedBucketBelowDigAreaPlane =
        displayedBucketDepthBelowDigAreaPlane >= GetDigAreaBelowPlaneTolerance();

      GUILayout.BeginHorizontal();
      GUILayout.Label( "<b>Experiment HUD</b>", m_titleStyle );
      if ( GUILayout.Button( m_showRuntimeConfig ? "Hide Menu" : "Show Menu", m_buttonStyle, GUILayout.Width( 132.0f ) ) )
        m_showRuntimeConfig = !m_showRuntimeConfig;
      if ( GUILayout.Button( m_isMinimized ? "Restore" : "Minimize", m_buttonStyle, GUILayout.Width( 132.0f ) ) )
        ToggleMinimized();
      GUILayout.EndHorizontal();

      if ( m_isMinimized ) {
        GUILayout.Label( FormatMinimizedStatusLine( useStepAckTelemetry ), m_style );
        GUI.DragWindow( new Rect( 0.0f, 0.0f, Mathf.Max( 0.0f, m_rect.width - 280.0f ), DragHandleHeight ) );
        return;
      }

      var scrollHeight = Mathf.Max( 120.0f, m_currentVisibleWindowHeight - DragHandleHeight - 24.0f );
      m_scrollPosition = GUILayout.BeginScrollView( m_scrollPosition, GUILayout.Height( scrollHeight ) );
      if ( useStepAckTelemetry )
        GUILayout.Label( $"Telemetry: {Colorize( "step-ack collector", GoodColor )}    EpisodeManager: {Colorize( "disabled while serving", WarnColor )}", m_style );
      else if ( m_stepAckServer != null && m_stepAckServer.IsListening && m_episodeManager != null && !m_episodeManager.isActiveAndEnabled )
        GUILayout.Label( $"Telemetry: {Colorize( "waiting for first step-ack sample", WarnColor )}", m_style );

      if ( m_episodeManager != null ) {
        GUILayout.Label( $"Episode: {m_episodeManager.CurrentEpisodeIndex}    Running: {m_episodeManager.IsEpisodeRunning}", m_style );
        GUILayout.Label( $"Source: {m_episodeManager.CurrentSourceName}", m_style );
        GUILayout.Label( $"Target: {m_episodeManager.CurrentTargetName}", m_style );
        if ( !string.IsNullOrWhiteSpace( m_episodeManager.CurrentControlLayout ) )
          GUILayout.Label( $"Layout: {m_episodeManager.CurrentControlLayout}", m_style );
        GUILayout.Label( "Controls: R reset, Enter start, Backspace stop, F6/F7 switch source, 1-9 select source, F8/F9 switch target", m_style );
      }
      else {
        GUILayout.Label( "EpisodeManager: n/a", m_style );
      }

      if ( m_showRuntimeConfig && m_episodeManager != null )
        DrawRuntimeConfig();

      if ( m_episodeManager != null && m_episodeManager.CurrentSourceName == "ACT" ) {
        GUILayout.Label( $"Backend ready: {m_episodeManager.CurrentSourceBackendReady}    Timeout: {m_episodeManager.CurrentSourceTimedOut}", m_style );
        GUILayout.Label( $"ACT seq: {m_episodeManager.CurrentSourceSequence}    Infer: {m_episodeManager.CurrentSourceInferenceTimeMs:0.0} ms", m_style );
        GUILayout.Label( $"ACT session: {m_episodeManager.CurrentSourceSessionId}    Status: {m_episodeManager.CurrentSourceBackendStatus}", m_style );
      }

      if ( m_episodeManager != null && m_episodeManager.CurrentSourceHasHardwareDiagnostics ) {
        GUILayout.Label( $"Device connected: {m_episodeManager.CurrentSourceDeviceConnected}    Status: {m_episodeManager.CurrentSourceBindingStatus}", m_style );
        GUILayout.Label( $"Device: {m_episodeManager.CurrentSourceDeviceDisplayName}", m_style );
        GUILayout.Label( $"Profile: {m_episodeManager.CurrentSourceProfileName}", m_style );
        GUILayout.Label( $"HW Raw: {m_episodeManager.CurrentSourceHardwareInputSnapshot.ToCompactString()}", m_style );
        if ( !string.IsNullOrWhiteSpace( m_episodeManager.CurrentSourceRawInputSummary ) )
          GUILayout.Label( $"Bindings: {m_episodeManager.CurrentSourceRawInputSummary}", m_style );
      }

      GUILayout.Space( 6.0f );
      if ( m_episodeManager != null ) {
        GUILayout.Label( $"Raw: {m_episodeManager.LastRawCommand.ToCompactString()}", m_style );
        GUILayout.Label( $"Sim: {m_episodeManager.LastSimulatedCommand.ToCompactString()}", m_style );
        GUILayout.Label( $"Act: {m_episodeManager.LastActuationCommand.ToCompactString()}", m_style );
      }
      GUILayout.Space( 6.0f );
      GUILayout.Label( $"Mass in bucket: {displayedMassInBucket:0.00} kg", m_style );
      GUILayout.Label( $"Mass in target box: {displayedMassInTargetBox:0.00} kg", m_style );
      GUILayout.Label( $"Deposited in target box: {displayedDepositedMassInTargetBox:0.00} kg", m_style );
      GUILayout.Label( displayedMinDistanceToTarget >= 0.0f ?
                         $"Min distance to target: {displayedMinDistanceToTarget:0.000} m" :
                         "Min distance to target: n/a",
                       m_style );
      GUILayout.Label( FormatGoodDigStartLine( useStepAckTelemetry, displayedBucketTouchingDigArea, displayedBucketBelowDigAreaPlane ), m_style );
      GUILayout.Label( FormatDigAreaTouchLine( displayedMinDistanceToDigArea, displayedBucketTouchingDigArea ), m_style );
      GUILayout.Label( FormatDigAreaDepthLine( displayedMinDistanceToDigArea, displayedBucketDepthBelowDigAreaPlane, displayedBucketBelowDigAreaPlane ), m_style );
      GUILayout.Label( $"Target hard collisions (episode): {displayedTargetHardCollisionCount}", m_style );
      GUILayout.Label( $"Target max normal force (step): {displayedTargetContactMaxNormalForceN:0.0} N", m_style );
      if ( m_showStepAckDebug )
        DrawStepAckDebug();
      if ( m_showPlannerDebug )
        DrawPlannerDebug();
      if ( m_showCalibrationDebug )
        DrawCalibrationDebug();
      GUILayout.Space( 6.0f );
      GUILayout.Label( $"Last log: {( m_episodeManager != null ? m_episodeManager.LastSavedPath : string.Empty )}", m_style );
      GUILayout.EndScrollView();
      GUI.DragWindow( new Rect( 0.0f, 0.0f, Mathf.Max( 0.0f, m_rect.width - 280.0f ), DragHandleHeight ) );
    }

    private void EnsureStyles()
    {
      var fontSize = Mathf.RoundToInt( Mathf.Max( 12.0f, m_fontSize ) );
      var titleFontSize = Mathf.RoundToInt( Mathf.Max( fontSize + 2.0f, m_titleFontSize ) );
      if ( m_style != null &&
           m_cachedFontSize == fontSize &&
           m_cachedTitleFontSize == titleFontSize )
        return;

      m_cachedFontSize = fontSize;
      m_cachedTitleFontSize = titleFontSize;
      m_style = new GUIStyle( GUI.skin.label )
      {
        alignment = TextAnchor.UpperLeft,
        fontSize = fontSize,
        richText = true,
        wordWrap = true
      };

      m_titleStyle = new GUIStyle( GUI.skin.label )
      {
        alignment = TextAnchor.MiddleLeft,
        fontSize = titleFontSize,
        fontStyle = FontStyle.Bold,
        richText = true,
        wordWrap = true
      };

      m_windowStyle = new GUIStyle( GUI.skin.window )
      {
        fontSize = fontSize,
        padding = new RectOffset( 16, 16, 14, 16 )
      };

      m_buttonStyle = new GUIStyle( GUI.skin.button )
      {
        fontSize = fontSize,
        fixedHeight = Mathf.Max( 34.0f, fontSize + 14.0f ),
        wordWrap = true
      };

      m_toggleStyle = new GUIStyle( GUI.skin.toggle )
      {
        fontSize = fontSize,
        fixedHeight = Mathf.Max( 30.0f, fontSize + 12.0f ),
        wordWrap = true
      };

      m_textFieldStyle = new GUIStyle( GUI.skin.textField )
      {
        fontSize = fontSize,
        fixedHeight = Mathf.Max( 32.0f, fontSize + 12.0f )
      };

      m_popupTitleStyle = new GUIStyle( GUI.skin.label )
      {
        alignment = TextAnchor.MiddleCenter,
        fontStyle = FontStyle.Bold,
        fontSize = titleFontSize,
        richText = true,
        wordWrap = true
      };

      m_popupBodyStyle = new GUIStyle( GUI.skin.label )
      {
        alignment = TextAnchor.MiddleCenter,
        fontSize = fontSize,
        richText = true,
        wordWrap = true
      };
    }

    private void ToggleMinimized()
    {
      if ( m_isMinimized ) {
        m_isMinimized = false;
        m_rect.height = Mathf.Max( MinWindowHeight, m_restoredWindowHeight );
        return;
      }

      m_restoredWindowHeight = Mathf.Max( MinWindowHeight, m_rect.height );
      m_isMinimized = true;
    }

    private float GetCurrentWindowHeight()
    {
      return m_isMinimized ? MinimizedWindowHeight : Mathf.Max( MinWindowHeight, m_rect.height );
    }

    private static Rect ClampWindowRect( Rect rect )
    {
      var maxWidth = Mathf.Max( MinWindowWidth, Screen.width - WindowMargin * 2.0f );
      var maxHeight = Mathf.Max( MinimizedWindowHeight, Screen.height - WindowMargin * 2.0f );
      rect.width = Mathf.Clamp( rect.width, MinWindowWidth, maxWidth );
      rect.height = Mathf.Clamp( rect.height, MinimizedWindowHeight, maxHeight );

      var maxX = Mathf.Max( WindowMargin, Screen.width - rect.width - WindowMargin );
      var maxY = Mathf.Max( WindowMargin, Screen.height - rect.height - WindowMargin );
      rect.x = Mathf.Clamp( rect.x, WindowMargin, maxX );
      rect.y = Mathf.Clamp( rect.y, WindowMargin, maxY );
      return rect;
    }

    private string FormatMinimizedStatusLine( bool useStepAckTelemetry )
    {
      if ( useStepAckTelemetry )
        return $"Telemetry: {Colorize( "step-ack collector", GoodColor )}";

      if ( m_episodeManager == null )
        return "EpisodeManager: n/a";

      return $"Episode: {m_episodeManager.CurrentEpisodeIndex}    Running: {m_episodeManager.IsEpisodeRunning}    Source: {m_episodeManager.CurrentSourceName}";
    }

    private void DrawRuntimeConfig()
    {
      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>Runtime Config</b>", m_style );
      DrawMachineResponseControls();

      if ( m_episodeManager.AvailableSourceCount > 0 ) {
        GUILayout.Space( 6.0f );
        GUILayout.Label( "Control source (F6/F7 cycle, 1-9 select, switching restarts the active episode):", m_style );
        for ( var sourceIndex = 0; sourceIndex < m_episodeManager.AvailableSourceCount; ++sourceIndex ) {
          var isCurrentSource = sourceIndex == m_episodeManager.CurrentSourceIndex;
          var sourceDisplayName = m_episodeManager.GetAvailableSourceDisplayName( sourceIndex );
          var hotkeyPrefix = GetSourceHotkeyLabel( sourceIndex );
          var buttonLabel = isCurrentSource ?
                            $"{hotkeyPrefix}{sourceDisplayName} Active" :
                            $"{hotkeyPrefix}{sourceDisplayName}";

          GUI.enabled = !isCurrentSource;
          if ( GUILayout.Button( buttonLabel, m_buttonStyle ) )
            m_episodeManager.SetCommandSourceByIndex( sourceIndex );
          GUI.enabled = true;
        }
      }
      else {
        GUILayout.Label( "No operator command sources found in the current rig.", m_style );
      }

      if ( m_episodeManager.AvailableTargetCount > 0 ) {
        GUILayout.Space( 4.0f );
        GUILayout.Label( "Measurement target (F8/F9 cycle, switching does not reset the episode):", m_style );
        for ( var targetIndex = 0; targetIndex < m_episodeManager.AvailableTargetCount; ++targetIndex ) {
          var isCurrentTarget = targetIndex == m_episodeManager.CurrentTargetIndex;
          var targetDisplayName = m_episodeManager.GetAvailableTargetDisplayName( targetIndex );
          var buttonLabel = isCurrentTarget ?
                            $"{targetDisplayName} Active" :
                            targetDisplayName;

          GUI.enabled = !isCurrentTarget;
          if ( GUILayout.Button( buttonLabel, m_buttonStyle ) )
            m_episodeManager.SetTargetByIndex( targetIndex );
          GUI.enabled = true;
        }
      }

      if ( m_cameraWindows.Length > 0 ) {
        GUILayout.Space( 4.0f );
        GUILayout.Label( "Auxiliary views:", m_style );
        for ( var windowIndex = 0; windowIndex < m_cameraWindows.Length; ++windowIndex ) {
          var cameraWindow = m_cameraWindows[ windowIndex ];
          if ( cameraWindow == null )
            continue;

          var isVisible = GUILayout.Toggle( cameraWindow.IsVisible, cameraWindow.ViewName, m_toggleStyle );
          if ( isVisible != cameraWindow.IsVisible )
            cameraWindow.IsVisible = isVisible;
        }
      }
    }

    private void DrawMachineResponseControls()
    {
      if ( m_machineController == null ) {
        GUILayout.Label( "Machine response: no ExcavatorMachineController found.", m_style );
        return;
      }

      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>Machine response</b>", m_style );
      GUILayout.Label( "Action remains normalized; these settings define what full command means at the machine actuator.", m_style );
      DrawAxisResponseRow(
        "Swing",
        m_machineController.SwingMaxTargetSpeed,
        value => m_machineController.SwingMaxTargetSpeed = value,
        m_machineController.SwingMaxAcceleration,
        value => m_machineController.SwingMaxAcceleration = value );
      DrawAxisResponseRow(
        "Boom",
        m_machineController.BoomMaxTargetSpeed,
        value => m_machineController.BoomMaxTargetSpeed = value,
        m_machineController.BoomMaxAcceleration,
        value => m_machineController.BoomMaxAcceleration = value );
      DrawAxisResponseRow(
        "Stick",
        m_machineController.StickMaxTargetSpeed,
        value => m_machineController.StickMaxTargetSpeed = value,
        m_machineController.StickMaxAcceleration,
        value => m_machineController.StickMaxAcceleration = value );
      DrawAxisResponseRow(
        "Bucket",
        m_machineController.BucketMaxTargetSpeed,
        value => m_machineController.BucketMaxTargetSpeed = value,
        m_machineController.BucketMaxAcceleration,
        value => m_machineController.BucketMaxAcceleration = value );
    }

    private void DrawAxisResponseRow( string label,
                                      float speed,
                                      Action<float> setSpeed,
                                      float acceleration,
                                      Action<float> setAcceleration )
    {
      GUILayout.Label( label, m_style );
      setSpeed( DrawSlider( "max speed", speed, Mathf.Max( m_speedSliderMax, 0.05f ) ) );
      setAcceleration( DrawSlider( "accel", acceleration, Mathf.Max( m_accelerationSliderMax, 0.05f ) ) );
    }

    private float DrawSlider( string label, float value, float sliderMax )
    {
      var clampedValue = Mathf.Clamp( value, 0.0f, sliderMax );
      GUILayout.BeginHorizontal();
      GUILayout.Label( label, m_style, GUILayout.Width( 110.0f ) );
      var nextValue = GUILayout.HorizontalSlider(
        clampedValue,
        0.0f,
        sliderMax,
        GUILayout.MinWidth( 240.0f ),
        GUILayout.Height( 32.0f ) );
      GUILayout.Label( $"{nextValue:0.00}", m_style, GUILayout.Width( 72.0f ) );
      GUILayout.EndHorizontal();
      return Mathf.Clamp( nextValue, 0.0f, sliderMax );
    }

    private void RefreshRuntimeTargets()
    {
      if ( m_episodeManager == null )
        m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );

      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
      m_stepAckServer = ExcavatorRigLocator.ResolveComponent( this, m_stepAckServer );
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      EnsurePlannerVisualizer();

      m_episodeManager?.RefreshAvailableSources();

      m_cameraWindows = FindObjectsByType<TrackedCameraWindow>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );
      Array.Sort( m_cameraWindows, CompareCameraWindows );
    }

    private void DrawStepAckDebug()
    {
      if ( m_stepAckServer == null )
        return;

      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>AGX Step-Ack</b>", m_style );
      GUILayout.Label( $"Listening: {m_stepAckServer.IsListening}    Last msg: {m_stepAckServer.LastRequestTypeName}    Step: {m_stepAckServer.LastRequestStepId}", m_style );
      GUILayout.Label( $"Success: {m_stepAckServer.LastResponseSuccess}    Reset applied: {m_stepAckServer.LastResetApplied}", m_style );
      GUILayout.Label( $"FPV: {m_stepAckServer.LastImageWidth}x{m_stepAckServer.LastImageHeight}    Payload: {m_stepAckServer.LastImagePayloadBytes} bytes", m_style );
      GUILayout.Label( $"Warnings: {m_stepAckServer.LastWarningsSummary}", m_style );
      if ( !string.IsNullOrWhiteSpace( m_stepAckServer.LastError ) )
        GUILayout.Label( $"Error: {m_stepAckServer.LastError}", m_style );
    }

    private void DrawPlannerDebug()
    {
      if ( m_stepAckServer == null )
        return;

      var debug = m_stepAckServer.LastPlannerDebug;
      if ( debug == null || !debug.valid ) {
        if ( debug != null && !string.IsNullOrWhiteSpace( debug.parse_warning ) ) {
          GUILayout.Space( 6.0f );
          GUILayout.Label( "<b>Planner</b>", m_style );
          GUILayout.Label( $"Parse warning: {debug.parse_warning}", m_style );
        }
        return;
      }

      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>Planner</b>", m_style );
      GUILayout.Label( $"Mode: {debug.mode}    Skill: {debug.skill}    Cycle: {debug.cycle}", m_style );
      GUILayout.Label( $"Corridor: {debug.selected_corridor_id}    Score: {debug.score:0.000}    Depleted: {debug.depleted_count}", m_style );
      GUILayout.Label( $"Entry: ({debug.entry_x_m:0.000}, {debug.entry_z_m:0.000})    Exit: ({debug.exit_x_m:0.000}, {debug.exit_z_m:0.000})", m_style );
      GUILayout.Label( $"Last payload: {debug.last_payload_gain_kg:0.0} kg    Last deposit: {debug.last_effective_deposit_delta_kg:0.0} kg", m_style );
      GUILayout.Label( $"Token: {debug.token_source}    Prior: {debug.prior_id}", m_style );
      if ( debug.pre_dig_align_enabled )
        GUILayout.Label( $"Pre-align: step {debug.pre_dig_align_step_count}    hold {debug.pre_dig_align_hold_count}    entry err {debug.pre_dig_align_entry_error_m:0.000} m", m_style );
      if ( debug.terminal_stop_requested || !string.IsNullOrWhiteSpace( debug.stop_reason ) )
        GUILayout.Label( $"Stop: {Colorize( debug.stop_reason, WarnColor )}", m_style );
    }

    private void EnsurePlannerVisualizer()
    {
      if ( m_plannerDecisionVisualizer == null )
        m_plannerDecisionVisualizer = GetComponent<PlannerDecisionVisualizer>();
      if ( m_plannerDecisionVisualizer == null )
        m_plannerDecisionVisualizer = gameObject.AddComponent<PlannerDecisionVisualizer>();
      m_plannerDecisionVisualizer.Configure( m_stepAckServer );
    }

    private void DrawCalibrationDebug()
    {
      if ( m_observationCollector == null )
        return;

      GUILayout.Space( 6.0f );
      GUILayout.Label( "<b>Actuator Calibration</b>", m_style );
      GUILayout.Label( m_observationCollector.GetCalibrationStatusLine(), m_style );
      if ( !string.IsNullOrWhiteSpace( m_observationCollector.LastCalibrationMessage ) )
        GUILayout.Label( m_observationCollector.LastCalibrationMessage, m_style );

      if ( string.IsNullOrWhiteSpace( m_calibrationProfileNameDraft ) )
        m_calibrationProfileNameDraft = m_observationCollector.CalibrationProfileName;

      GUILayout.BeginHorizontal();
      GUILayout.Label( "Profile name:", m_style, GUILayout.Width( 128.0f ) );
      m_calibrationProfileNameDraft = GUILayout.TextField( m_calibrationProfileNameDraft ?? string.Empty, m_textFieldStyle );
      m_observationCollector.CalibrationProfileName = m_calibrationProfileNameDraft;
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      var trackingButtonLabel = m_observationCollector.IsCalibrationTrackingEnabled ? "Stop Tracking" : "Start Tracking";
      if ( GUILayout.Button( trackingButtonLabel, m_buttonStyle ) ) {
        if ( m_observationCollector.IsCalibrationTrackingEnabled )
          m_observationCollector.EndCalibrationTracking();
        else
          m_observationCollector.BeginCalibrationTracking();
      }

      if ( GUILayout.Button( "Reset Samples", m_buttonStyle ) )
        m_observationCollector.ResetCalibrationTracking();

      if ( GUILayout.Button( "Save JSON", m_buttonStyle ) )
        m_observationCollector.SaveObservedNormalizationProfile( m_calibrationProfileNameDraft );

      if ( GUILayout.Button( "Reload JSON", m_buttonStyle ) )
        m_observationCollector.LoadNormalizationProfileFromConfiguredPath();
      GUILayout.EndHorizontal();

      var debugLines = m_observationCollector.GetCalibrationDebugLines();
      if ( debugLines == null || debugLines.Length == 0 ) {
        GUILayout.Label( "No actuator calibration samples yet.", m_style );
        return;
      }

      for ( var lineIndex = 0; lineIndex < debugLines.Length; ++lineIndex ) {
        var debugLine = debugLines[ lineIndex ];
        if ( string.IsNullOrWhiteSpace( debugLine ) )
          continue;

        GUILayout.Label( debugLine, m_style );
      }
    }

    private bool ShouldUseStepAckTelemetry( ActTaskState collectorTaskState )
    {
      return collectorTaskState != null &&
             m_stepAckServer != null &&
             m_stepAckServer.IsListening &&
             ( m_episodeManager == null || !m_episodeManager.isActiveAndEnabled );
    }

    private float GetDisplayedMassInBucket( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.mass_in_bucket_kg;

      return m_episodeManager != null ? m_episodeManager.MassInBucket : 0.0f;
    }

    private float GetDisplayedMassInTargetBox( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.mass_in_target_box_kg;

      return m_episodeManager != null ? m_episodeManager.MassInTargetBox : 0.0f;
    }

    private float GetDisplayedDepositedMassInTargetBox( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.deposited_mass_in_target_box_kg;

      return m_episodeManager != null ? m_episodeManager.DepositedMassInTargetBox : 0.0f;
    }

    private float GetDisplayedMinDistanceToTarget( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.min_distance_to_target_m;

      return m_episodeManager != null ? m_episodeManager.MinDistanceToTarget : -1.0f;
    }

    private float GetDisplayedMinDistanceToDigArea( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.min_distance_to_dig_area_m;

      return m_episodeManager != null ? m_episodeManager.MinDistanceToDigArea : -1.0f;
    }

    private float GetDisplayedBucketDepthBelowDigAreaPlane( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.bucket_depth_below_dig_area_plane_m;

      return m_episodeManager != null ? m_episodeManager.BucketDepthBelowDigAreaPlane : 0.0f;
    }

    private int GetDisplayedTargetHardCollisionCount( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return Mathf.RoundToInt( collectorTaskState.target_hard_collision_count );

      return m_episodeManager != null ? m_episodeManager.TargetHardCollisionCount : 0;
    }

    private float GetDisplayedTargetContactMaxNormalForceN( bool useStepAckTelemetry, ActTaskState collectorTaskState )
    {
      if ( useStepAckTelemetry && collectorTaskState != null )
        return collectorTaskState.target_contact_max_normal_force_n;

      return m_episodeManager != null ? m_episodeManager.TargetContactMaxNormalForceN : 0.0f;
    }

    private float GetDigAreaTouchTolerance()
    {
      return m_episodeManager != null ? m_episodeManager.GoodDigAreaTouchToleranceM : 0.05f;
    }

    private float GetDigAreaBelowPlaneTolerance()
    {
      return m_episodeManager != null ? m_episodeManager.GoodDigBelowPlaneDepthToleranceM : 0.02f;
    }

    private string FormatGoodDigStartLine( bool useStepAckTelemetry,
                                           bool isBucketTouchingDigArea,
                                           bool isBucketBelowDigAreaPlane )
    {
      if ( useStepAckTelemetry ) {
        if ( isBucketTouchingDigArea && isBucketBelowDigAreaPlane )
          return $"Good dig start: {Colorize( "step-ack ready, waiting for load", WarnColor )}";

        return $"Good dig start: {Colorize( "step-ack mode", NeutralColor )}";
      }

      if ( m_episodeManager == null )
        return "Good dig start: n/a";

      if ( m_episodeManager.GoodDigStartThisFrame )
        return $"Good dig start: {Colorize( "latched this frame", GoodColor )}";

      if ( m_episodeManager.GoodDigStartLatched )
        return $"Good dig start: {Colorize( "latched", GoodColor )}";

      if ( !m_episodeManager.IsEpisodeRunning )
        return $"Good dig start: {Colorize( "idle", NeutralColor )}";

      if ( m_episodeManager.IsBucketTouchingDigArea &&
           m_episodeManager.IsBucketBelowDigAreaPlane )
        return $"Good dig start: {Colorize( "armed, waiting for load", WarnColor )}";

      return $"Good dig start: {Colorize( "waiting", BadColor )}";
    }

    private string FormatDigAreaTouchLine( float minDistanceToDigArea, bool isBucketTouchingDigArea )
    {
      if ( minDistanceToDigArea < 0.0f )
        return $"DigArea touch: {Colorize( "n/a", NeutralColor )}    Min distance to DigArea: n/a";

      return $"DigArea touch: {FormatBooleanState( isBucketTouchingDigArea )}    " +
             $"Min distance to DigArea: {minDistanceToDigArea:0.000} m";
    }

    private string FormatDigAreaDepthLine( float minDistanceToDigArea,
                                           float bucketDepthBelowDigAreaPlane,
                                           bool isBucketBelowDigAreaPlane )
    {
      if ( minDistanceToDigArea < 0.0f )
        return $"Below DigArea plane: {Colorize( "n/a", NeutralColor )}    Effective depth below plane: n/a";

      return $"Below DigArea plane: {FormatBooleanState( isBucketBelowDigAreaPlane )}    " +
             $"Effective depth below plane: {bucketDepthBelowDigAreaPlane:0.000} m";
    }

    private static string FormatBooleanState( bool value )
    {
      return value ? Colorize( "yes", GoodColor ) : Colorize( "no", BadColor );
    }

    private static string Colorize( string text, string colorHex )
    {
      return $"<color={colorHex}>{text}</color>";
    }

    private static string GetSourceHotkeyLabel( int sourceIndex )
    {
      return sourceIndex >= 0 && sourceIndex < 9 ? $"[{sourceIndex + 1}] " : string.Empty;
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

    private void DrawReleaseInputPopup()
    {
      var popupWidth = Mathf.Min( 460.0f, Screen.width - 32.0f );
      var popupHeight = 140.0f;
      var popupRect = new Rect(
        0.5f * ( Screen.width - popupWidth ),
        Mathf.Max( 24.0f, 0.18f * Screen.height ),
        popupWidth,
        popupHeight );

      GUILayout.BeginArea( popupRect, m_windowStyle );
      GUILayout.FlexibleSpace();
      GUILayout.Label( "Release Controls", m_popupTitleStyle );
      GUILayout.Space( 8.0f );
      GUILayout.Label( m_episodeManager.TransitionInputCutHint, m_popupBodyStyle );
      GUILayout.Space( 4.0f );
      GUILayout.Label( "The next episode will accept input after all axes return to neutral.", m_popupBodyStyle );
      GUILayout.FlexibleSpace();
      GUILayout.EndArea();
    }
  }
}

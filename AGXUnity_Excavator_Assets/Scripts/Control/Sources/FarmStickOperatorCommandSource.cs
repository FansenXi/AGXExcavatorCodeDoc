using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public sealed class FarmStickOperatorCommandSource : OperatorCommandSourceBehaviour, IHardwareCommandDiagnostics
  {
    private const string ReadyStatus = "Ready";
    private const string NoProfileStatus = "No Profile";
    private const string NoLeftProfileStatus = "No Left Profile";
    private const string NoRightProfileStatus = "No Right Profile";
    private const string NoDeviceStatus = "No Device";
    private const string AmbiguousDeviceStatus = "Ambiguous Device";
    private const string InputSystemDisabledStatus = "Input System Disabled";
    private const string NoResolvedBindingsStatus = "No bindings resolved";
    private const float DefaultStickSensitivity = 1.0f;

    [SerializeField]
    private FarmStickControlProfile m_profile = null;

    [SerializeField]
    private FarmStickControlProfile m_leftProfile = null;

    [SerializeField]
    private bool m_swapDualStickAssignments = false;

    [Header( "Joystick Sensitivity" )]
    [SerializeField]
    [Range( 0.1f, 2.0f )]
    private float m_leftStickXSensitivity = DefaultStickSensitivity;

    [SerializeField]
    [Range( 0.1f, 2.0f )]
    private float m_leftStickYSensitivity = DefaultStickSensitivity;

    [SerializeField]
    [Range( 0.1f, 2.0f )]
    private float m_rightStickXSensitivity = DefaultStickSensitivity;

    [SerializeField]
    [Range( 0.1f, 2.0f )]
    private float m_rightStickYSensitivity = DefaultStickSensitivity;

    private bool m_deviceConnected = false;
    private string m_deviceDisplayName = string.Empty;
    private string m_bindingStatus = NoProfileStatus;
    private string m_lastRawInputSummary = string.Empty;
    private HardwareInputSnapshot m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;
    private bool m_stickModeSwapActive = false;
    private bool m_previousControlModeSwitchPressed = false;
    private bool m_usedStableDualDeviceAssignmentFallback = false;

    private bool IsDualStickConfigured => m_leftProfile != null && m_profile != null;

    public override string SourceName => IsDualStickConfigured ? "FarmStick Dual" : "FarmStick";

    public bool DeviceConnected => m_deviceConnected;
    public string DeviceDisplayName => m_deviceDisplayName;
    public string ProfileName => IsDualStickConfigured ?
                                 $"Left: {m_leftProfile.DisplayName} | Right: {m_profile.DisplayName}" :
                                 m_profile != null ? m_profile.DisplayName : string.Empty;
    public string BindingStatus => m_bindingStatus;
    public string LastRawInputSummary => m_lastRawInputSummary;
    public HardwareInputSnapshot LastRawInputSnapshot => m_lastRawInputSnapshot;

    private void OnEnable()
    {
      EnsureSensitivityDefaults();
      ResetDiagnostics( GetDefaultStatus() );
    }

    private void OnDisable()
    {
      ResetDiagnostics( GetDefaultStatus() );
    }

    private void OnValidate()
    {
      EnsureSensitivityDefaults();
      ResetDiagnostics( GetDefaultStatus() );
    }

    public override OperatorCommand ReadCommand()
    {
      m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;
      m_lastRawInputSummary = string.Empty;
      m_usedStableDualDeviceAssignmentFallback = false;

      if ( IsDualStickConfigured )
        return ReadDualStickCommand();

      if ( m_profile == null ) {
        ResetDiagnostics( GetDefaultStatus() );
        return OperatorCommand.Zero;
      }

#if ENABLE_INPUT_SYSTEM
      if ( !TryResolveDevice( m_profile, out var device, out var status ) ) {
        ResetDiagnostics( status );
        return OperatorCommand.Zero;
      }

      m_deviceConnected = true;
      m_deviceDisplayName = GetDeviceDisplayName( device );

      if ( !TryResolveBindings( device, m_profile, out var bindings, out status ) ) {
        m_bindingStatus = status;
        return OperatorCommand.Zero;
      }

      var summaryBuilder = new StringBuilder( 256 );
      var command = OperatorCommand.Zero;
      var mainStickX = ReadAxisBinding( bindings.LeftStickX, ref m_lastRawInputSnapshot.LeftStickX, summaryBuilder );
      var mainStickY = ReadAxisBinding( bindings.LeftStickY, ref m_lastRawInputSnapshot.LeftStickY, summaryBuilder );
      var miniStickX = ReadAxisBinding( bindings.RightStickX, ref m_lastRawInputSnapshot.RightStickX, summaryBuilder );
      var miniStickY = ReadAxisBinding( bindings.RightStickY, ref m_lastRawInputSnapshot.RightStickY, summaryBuilder );
      var controlModeSwitchRaw = 0.0f;
      var controlModeSwitchPressed = ReadButtonBinding( bindings.ControlModeSwitch, ref controlModeSwitchRaw, summaryBuilder );
      UpdateStickModeSwitchState( bindings.ControlModeSwitch.Config, controlModeSwitchPressed );
      ApplyStickRouting( mainStickX, mainStickY, miniStickX, miniStickY, ref command );
      ApplyStickSensitivity( ref command );
      command.Drive = ReadAxisBinding( bindings.Drive, ref m_lastRawInputSnapshot.Drive, summaryBuilder );
      command.Steer = ReadAxisBinding( bindings.Steer, ref m_lastRawInputSnapshot.Steer, summaryBuilder );
      command.ResetRequested = ReadButtonBinding( bindings.ResetEpisode, ref m_lastRawInputSnapshot.ResetButton, summaryBuilder );
      command.StartEpisodeRequested = ReadButtonBinding( bindings.StartEpisode, ref m_lastRawInputSnapshot.StartEpisodeButton, summaryBuilder );
      command.StopEpisodeRequested = ReadButtonBinding( bindings.StopEpisode, ref m_lastRawInputSnapshot.StopEpisodeButton, summaryBuilder );

      AppendRoutingSummary( summaryBuilder );
      m_bindingStatus = BuildBindingStatus( status );
      m_lastRawInputSummary = summaryBuilder.ToString();
      return command.ClampAxes();
#else
      ResetDiagnostics( InputSystemDisabledStatus );
      return OperatorCommand.Zero;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private OperatorCommand ReadDualStickCommand()
    {
      if ( m_leftProfile == null || m_profile == null ) {
        ResetDiagnostics( GetDefaultStatus() );
        return OperatorCommand.Zero;
      }

      m_stickModeSwapActive = false;
      m_previousControlModeSwitchPressed = false;

      if ( !TryResolveDualDevices( out var leftDevice, out var rightDevice, out var status ) ) {
        ResetDiagnostics( status );
        return OperatorCommand.Zero;
      }

      m_deviceConnected = true;
      m_deviceDisplayName = $"Left: {GetDeviceDisplayName( leftDevice )} | Right: {GetDeviceDisplayName( rightDevice )}";

      if ( !TryResolveBindings( leftDevice, m_leftProfile, out var leftBindings, out var leftStatus ) ) {
        m_bindingStatus = BuildBindingStatus( $"Left {leftStatus}" );
        return OperatorCommand.Zero;
      }

      if ( !TryResolveBindings( rightDevice, m_profile, out var rightBindings, out var rightStatus ) ) {
        m_bindingStatus = BuildBindingStatus( $"Right {rightStatus}" );
        return OperatorCommand.Zero;
      }

      var summaryBuilder = new StringBuilder( 512 );
      var command = OperatorCommand.Zero;

      command.LeftStickX = ReadAxisBinding( leftBindings.LeftStickX, ref m_lastRawInputSnapshot.LeftStickX, summaryBuilder, "Left" );
      command.LeftStickY = ReadAxisBinding( leftBindings.LeftStickY, ref m_lastRawInputSnapshot.LeftStickY, summaryBuilder, "Left" );
      command.RightStickX = ReadAxisBinding( rightBindings.LeftStickX, ref m_lastRawInputSnapshot.RightStickX, summaryBuilder, "Right" );
      command.RightStickY = ReadAxisBinding( rightBindings.LeftStickY, ref m_lastRawInputSnapshot.RightStickY, summaryBuilder, "Right" );
      ApplyStickSensitivity( ref command );

      var leftTrackSnapshot = 0.0f;
      var rightTrackSnapshot = 0.0f;
      var leftTrack = ReadAxisBinding( leftBindings.Drive, ref leftTrackSnapshot, summaryBuilder, "Left" );
      var rightTrack = ReadAxisBinding( rightBindings.Drive, ref rightTrackSnapshot, summaryBuilder, "Right" );
      command.Drive = 0.5f * ( leftTrack + rightTrack );
      command.Steer = 0.5f * ( rightTrack - leftTrack );
      m_lastRawInputSnapshot.Drive = command.Drive;
      m_lastRawInputSnapshot.Steer = command.Steer;

      var leftResetRaw = 0.0f;
      var rightResetRaw = 0.0f;
      var leftStartRaw = 0.0f;
      var rightStartRaw = 0.0f;
      var leftStopRaw = 0.0f;
      var rightStopRaw = 0.0f;
      command.ResetRequested = ReadButtonBinding( leftBindings.ResetEpisode, ref leftResetRaw, summaryBuilder, "Left" ) ||
                               ReadButtonBinding( rightBindings.ResetEpisode, ref rightResetRaw, summaryBuilder, "Right" );
      command.StartEpisodeRequested = ReadButtonBinding( leftBindings.StartEpisode, ref leftStartRaw, summaryBuilder, "Left" ) ||
                                      ReadButtonBinding( rightBindings.StartEpisode, ref rightStartRaw, summaryBuilder, "Right" );
      command.StopEpisodeRequested = ReadButtonBinding( leftBindings.StopEpisode, ref leftStopRaw, summaryBuilder, "Left" ) ||
                                     ReadButtonBinding( rightBindings.StopEpisode, ref rightStopRaw, summaryBuilder, "Right" );
      m_lastRawInputSnapshot.ResetButton = Mathf.Max( leftResetRaw, rightResetRaw );
      m_lastRawInputSnapshot.StartEpisodeButton = Mathf.Max( leftStartRaw, rightStartRaw );
      m_lastRawInputSnapshot.StopEpisodeButton = Mathf.Max( leftStopRaw, rightStopRaw );

      AppendRoutingSummary( summaryBuilder );
      m_bindingStatus = BuildBindingStatus( CombineDualBindingStatus( status, leftStatus, rightStatus ) );
      m_lastRawInputSummary = summaryBuilder.ToString();
      return command.ClampAxes();
    }
#else
    private OperatorCommand ReadDualStickCommand()
    {
      ResetDiagnostics( InputSystemDisabledStatus );
      return OperatorCommand.Zero;
    }
#endif

    private string GetDefaultStatus()
    {
      if ( m_leftProfile != null && m_profile == null )
        return NoRightProfileStatus;

      if ( m_profile == null )
        return NoProfileStatus;

      return NoDeviceStatus;
    }

    private void EnsureSensitivityDefaults()
    {
      if ( m_leftStickXSensitivity <= 0.0f )
        m_leftStickXSensitivity = DefaultStickSensitivity;
      if ( m_leftStickYSensitivity <= 0.0f )
        m_leftStickYSensitivity = DefaultStickSensitivity;
      if ( m_rightStickXSensitivity <= 0.0f )
        m_rightStickXSensitivity = DefaultStickSensitivity;
      if ( m_rightStickYSensitivity <= 0.0f )
        m_rightStickYSensitivity = DefaultStickSensitivity;
    }

    private void ResetDiagnostics( string status )
    {
      m_deviceConnected = false;
      m_deviceDisplayName = string.Empty;
      m_bindingStatus = status;
      m_lastRawInputSummary = string.Empty;
      m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;
      m_stickModeSwapActive = false;
      m_previousControlModeSwitchPressed = false;
      m_usedStableDualDeviceAssignmentFallback = false;
    }

#if ENABLE_INPUT_SYSTEM
    private struct ScoredInputDevice
    {
      public InputDevice Device;
      public int Score;
    }

    private struct ResolvedAxisBinding
    {
      public FarmStickAxisBinding Config;
      public InputControl SingleControl;
      public InputControl NegativeControl;
      public InputControl PositiveControl;
    }

    private struct ResolvedButtonBinding
    {
      public FarmStickButtonBinding Config;
      public InputControl Control;
    }

    private struct ResolvedFarmStickBindings
    {
      public ResolvedAxisBinding LeftStickX;
      public ResolvedAxisBinding LeftStickY;
      public ResolvedAxisBinding RightStickX;
      public ResolvedAxisBinding RightStickY;
      public ResolvedAxisBinding Drive;
      public ResolvedAxisBinding Steer;
      public ResolvedButtonBinding ControlModeSwitch;
      public ResolvedButtonBinding ResetEpisode;
      public ResolvedButtonBinding StartEpisode;
      public ResolvedButtonBinding StopEpisode;
    }

    private bool TryResolveDevice( FarmStickControlProfile profile, out InputDevice device, out string status )
    {
      device = null;
      status = NoDeviceStatus;

      var matchedDevices = new List<InputDevice>();
      var bestMatchScore = 0;
      foreach ( var candidate in InputSystem.devices ) {
        var matchScore = GetDeviceMatchScore( candidate, profile );
        if ( matchScore <= 0 )
          continue;

        if ( matchScore > bestMatchScore ) {
          bestMatchScore = matchScore;
          matchedDevices.Clear();
        }

        if ( matchScore == bestMatchScore )
          matchedDevices.Add( candidate );
      }

      if ( matchedDevices.Count == 0 ) {
        device = null;
        return false;
      }

      if ( matchedDevices.Count > 1 ) {
        device = null;
        status = $"{AmbiguousDeviceStatus}: {BuildDeviceSummary( matchedDevices )}";
        return false;
      }

      device = matchedDevices[ 0 ];
      status = ReadyStatus;
      return true;
    }

    private bool TryResolveDualDevices( out InputDevice leftDevice, out InputDevice rightDevice, out string status )
    {
      leftDevice = null;
      rightDevice = null;
      status = NoDeviceStatus;

      if ( m_leftProfile == null && m_profile == null ) {
        status = NoProfileStatus;
        return false;
      }

      if ( m_leftProfile == null ) {
        status = NoLeftProfileStatus;
        return false;
      }

      if ( m_profile == null ) {
        status = NoRightProfileStatus;
        return false;
      }

      var leftCandidates = GetMatchedDevices( m_leftProfile );
      var rightCandidates = GetMatchedDevices( m_profile );
      var fallbackCandidates = GetMatchedDevicesIgnoringHandedness( m_leftProfile, m_profile );

      if ( TryResolveUniqueBestDevice( leftCandidates, out leftDevice ) &&
           TryResolveUniqueBestDevice( rightCandidates, out rightDevice ) &&
           leftDevice != null &&
           rightDevice != null &&
           leftDevice != rightDevice ) {
        status = ReadyStatus;
        return true;
      }

      if ( fallbackCandidates.Count >= 2 ) {
        var firstDevice = fallbackCandidates[ 0 ].Device;
        var secondDevice = fallbackCandidates[ 1 ].Device;
        if ( m_swapDualStickAssignments ) {
          leftDevice = secondDevice;
          rightDevice = firstDevice;
        }
        else {
          leftDevice = firstDevice;
          rightDevice = secondDevice;
        }

        m_usedStableDualDeviceAssignmentFallback = true;
        status = $"{ReadyStatus} (stable device order)";
        return true;
      }

      if ( fallbackCandidates.Count == 1 ) {
        status = $"Need two FarmStick devices; found {GetDeviceDisplayName( fallbackCandidates[ 0 ].Device )}";
        return false;
      }

      if ( leftCandidates.Count == 0 && rightCandidates.Count == 0 ) {
        status = NoDeviceStatus;
        return false;
      }

      if ( leftCandidates.Count == 0 ) {
        status = $"No left FarmStick device matched profile '{m_leftProfile.DisplayName}'";
        return false;
      }

      if ( rightCandidates.Count == 0 ) {
        status = $"No right FarmStick device matched profile '{m_profile.DisplayName}'";
        return false;
      }

      status = $"{AmbiguousDeviceStatus}: {BuildDeviceSummary( fallbackCandidates )}";
      return false;
    }

    private static bool TryResolveBindings( InputDevice device,
                                            FarmStickControlProfile profile,
                                            out ResolvedFarmStickBindings bindings,
                                            out string status )
    {
      bindings = new ResolvedFarmStickBindings();
      var unresolvedBindings = new List<string>();
      var resolvedBindingCount = 0;

      ResolveAxisBinding( device, profile.LeftStickX, out bindings.LeftStickX, unresolvedBindings, ref resolvedBindingCount );
      ResolveAxisBinding( device, profile.LeftStickY, out bindings.LeftStickY, unresolvedBindings, ref resolvedBindingCount );
      ResolveAxisBinding( device, profile.RightStickX, out bindings.RightStickX, unresolvedBindings, ref resolvedBindingCount );
      ResolveAxisBinding( device, profile.RightStickY, out bindings.RightStickY, unresolvedBindings, ref resolvedBindingCount );
      ResolveAxisBinding( device, profile.Drive, out bindings.Drive, unresolvedBindings, ref resolvedBindingCount );
      ResolveAxisBinding( device, profile.Steer, out bindings.Steer, unresolvedBindings, ref resolvedBindingCount );
      ResolveButtonBinding( device, profile.ControlModeSwitch, out bindings.ControlModeSwitch, unresolvedBindings, ref resolvedBindingCount );
      ResolveButtonBinding( device, profile.ResetEpisode, out bindings.ResetEpisode, unresolvedBindings, ref resolvedBindingCount );
      ResolveButtonBinding( device, profile.StartEpisode, out bindings.StartEpisode, unresolvedBindings, ref resolvedBindingCount );
      ResolveButtonBinding( device, profile.StopEpisode, out bindings.StopEpisode, unresolvedBindings, ref resolvedBindingCount );

      if ( resolvedBindingCount == 0 ) {
        status = unresolvedBindings.Count > 0 ?
                 $"{NoResolvedBindingsStatus}: {string.Join( "; ", unresolvedBindings )}" :
                 NoResolvedBindingsStatus;
        return false;
      }

      status = unresolvedBindings.Count > 0 ?
               $"{ReadyStatus} (unresolved: {string.Join( "; ", unresolvedBindings )})" :
               ReadyStatus;
      return true;
    }

    private List<ScoredInputDevice> GetMatchedDevices( FarmStickControlProfile profile )
    {
      var matches = new List<ScoredInputDevice>();
      if ( profile == null )
        return matches;

      foreach ( var candidate in InputSystem.devices ) {
        var score = GetDeviceMatchScore( candidate, profile );
        if ( score <= 0 )
          continue;

        matches.Add( new ScoredInputDevice
        {
          Device = candidate,
          Score = score
        } );
      }

      matches.Sort( CompareScoredDevices );
      return matches;
    }

    private List<ScoredInputDevice> GetMatchedDevicesIgnoringHandedness( FarmStickControlProfile leftProfile,
                                                                         FarmStickControlProfile rightProfile )
    {
      var matches = new List<ScoredInputDevice>();
      foreach ( var candidate in InputSystem.devices ) {
        var score = Math.Max(
          GetDeviceMatchScore( candidate, leftProfile, true ),
          GetDeviceMatchScore( candidate, rightProfile, true ) );
        if ( score <= 0 )
          continue;

        matches.Add( new ScoredInputDevice
        {
          Device = candidate,
          Score = score
        } );
      }

      matches.Sort( CompareScoredDevices );
      return matches;
    }

    private static bool TryResolveUniqueBestDevice( List<ScoredInputDevice> candidates, out InputDevice device )
    {
      device = null;
      if ( candidates == null || candidates.Count == 0 )
        return false;

      var bestCandidate = candidates[ 0 ];
      var ambiguousTopMatch = candidates.Count > 1 && candidates[ 1 ].Score == bestCandidate.Score;
      if ( ambiguousTopMatch )
        return false;

      device = bestCandidate.Device;
      return device != null;
    }

    private static void ResolveAxisBinding( InputDevice device,
                                            FarmStickAxisBinding binding,
                                            out ResolvedAxisBinding resolvedBinding,
                                            List<string> unresolvedBindings,
                                            ref int resolvedBindingCount )
    {
      if ( TryResolveAxisBinding( device, binding, out resolvedBinding, out var resolutionStatus ) ) {
        if ( binding != null && binding.Enabled )
          ++resolvedBindingCount;
        return;
      }

      resolvedBinding = new ResolvedAxisBinding();
      unresolvedBindings?.Add( resolutionStatus );
    }

    private static void ResolveButtonBinding( InputDevice device,
                                              FarmStickButtonBinding binding,
                                              out ResolvedButtonBinding resolvedBinding,
                                              List<string> unresolvedBindings,
                                              ref int resolvedBindingCount )
    {
      if ( TryResolveButtonBinding( device, binding, out resolvedBinding, out var resolutionStatus ) ) {
        if ( binding != null && binding.Enabled )
          ++resolvedBindingCount;
        return;
      }

      resolvedBinding = new ResolvedButtonBinding();
      unresolvedBindings?.Add( resolutionStatus );
    }

    private static bool TryResolveAxisBinding( InputDevice device,
                                               FarmStickAxisBinding binding,
                                               out ResolvedAxisBinding resolvedBinding,
                                               out string status )
    {
      resolvedBinding = new ResolvedAxisBinding
      {
        Config = binding
      };

      if ( binding == null || !binding.Enabled ) {
        status = ReadyStatus;
        return true;
      }

      if ( binding.Mode == FarmStickAxisBindingMode.SingleAxis ) {
        if ( string.IsNullOrWhiteSpace( binding.ControlPath ) ) {
          status = $"{binding.DisplayName}: missing controlPath";
          return false;
        }

        resolvedBinding.SingleControl = TryFindControl( device, binding.ControlPath );
        if ( resolvedBinding.SingleControl == null ) {
          status = $"{binding.DisplayName}: unresolved '{binding.ControlPath}'";
          return false;
        }
      }
      else {
        if ( string.IsNullOrWhiteSpace( binding.NegativeControlPath ) || string.IsNullOrWhiteSpace( binding.PositiveControlPath ) ) {
          status = $"{binding.DisplayName}: missing composite control paths";
          return false;
        }

        resolvedBinding.NegativeControl = TryFindControl( device, binding.NegativeControlPath );
        resolvedBinding.PositiveControl = TryFindControl( device, binding.PositiveControlPath );
        if ( resolvedBinding.NegativeControl == null || resolvedBinding.PositiveControl == null ) {
          status = $"{binding.DisplayName}: unresolved composite '{binding.NegativeControlPath}'/'{binding.PositiveControlPath}'";
          return false;
        }
      }

      status = ReadyStatus;
      return true;
    }

    private static bool TryResolveButtonBinding( InputDevice device,
                                                 FarmStickButtonBinding binding,
                                                 out ResolvedButtonBinding resolvedBinding,
                                                 out string status )
    {
      resolvedBinding = new ResolvedButtonBinding
      {
        Config = binding
      };

      if ( binding == null || !binding.Enabled ) {
        status = ReadyStatus;
        return true;
      }

      if ( string.IsNullOrWhiteSpace( binding.ControlPath ) ) {
        status = $"{binding.DisplayName}: missing controlPath";
        return false;
      }

      resolvedBinding.Control = TryFindControl( device, binding.ControlPath );
      if ( resolvedBinding.Control == null ) {
        status = $"{binding.DisplayName}: unresolved '{binding.ControlPath}'";
        return false;
      }

      status = ReadyStatus;
      return true;
    }

    private int GetDeviceMatchScore( InputDevice device, FarmStickControlProfile profile, bool ignoreHandedness = false )
    {
      if ( device == null || profile == null )
        return 0;

      var description = device.description;
      var productScore = GetBestTextMatchScore(
        profile.ProductContains,
        description.product,
        device.displayName,
        device.name );
      if ( productScore <= 0 )
        return 0;

      var manufacturerScore = GetBestTextMatchScore(
        profile.ManufacturerContains,
        description.manufacturer,
        device.displayName,
        device.name );
      if ( !string.IsNullOrWhiteSpace( profile.ManufacturerContains ) &&
           manufacturerScore <= 0 &&
           !string.IsNullOrWhiteSpace( description.manufacturer ) )
        return 0;

      var handednessScore = 0;
      if ( !ignoreHandedness ) {
        handednessScore = GetHandednessMatchScore( device, profile.Handedness );
        if ( handednessScore < 0 )
          return 0;
      }

      return productScore * 100 + manufacturerScore * 10 + handednessScore;
    }

    private static int CompareScoredDevices( ScoredInputDevice left, ScoredInputDevice right )
    {
      var scoreComparison = right.Score.CompareTo( left.Score );
      if ( scoreComparison != 0 )
        return scoreComparison;

      return CompareDevicesStable( left.Device, right.Device );
    }

    private static int CompareDevicesStable( InputDevice left, InputDevice right )
    {
      if ( left == right )
        return 0;

      if ( left == null )
        return 1;

      if ( right == null )
        return -1;

      var nameComparison = string.Compare( GetDeviceDisplayName( left ), GetDeviceDisplayName( right ), StringComparison.Ordinal );
      if ( nameComparison != 0 )
        return nameComparison;

      return left.deviceId.CompareTo( right.deviceId );
    }

    private static bool ContainsIgnoreCase( string candidate, string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return true;

      return !string.IsNullOrWhiteSpace( candidate ) &&
             candidate.IndexOf( value, StringComparison.OrdinalIgnoreCase ) >= 0;
    }

    private static int GetBestTextMatchScore( string expectedValue, params string[] candidates )
    {
      if ( string.IsNullOrWhiteSpace( expectedValue ) )
        return 1;

      var trimmedExpectedValue = expectedValue.Trim();
      var bestScore = 0;
      for ( var candidateIndex = 0; candidateIndex < candidates.Length; ++candidateIndex ) {
        var candidate = candidates[ candidateIndex ];
        if ( string.IsNullOrWhiteSpace( candidate ) )
          continue;

        if ( string.Equals( candidate.Trim(), trimmedExpectedValue, StringComparison.OrdinalIgnoreCase ) )
          bestScore = Math.Max( bestScore, 3 );
        else if ( candidate.IndexOf( trimmedExpectedValue, StringComparison.OrdinalIgnoreCase ) >= 0 )
          bestScore = Math.Max( bestScore, 2 );
      }

      return bestScore;
    }

    private static int GetHandednessMatchScore( InputDevice device, FarmStickHandedness handedness )
    {
      if ( device == null )
        return 0;

      var expectedToken = handedness == FarmStickHandedness.Right ? "Right" : "Left";
      var oppositeToken = handedness == FarmStickHandedness.Right ? "Left" : "Right";
      var description = device.description;

      var expectedMatched =
        ContainsIgnoreCase( description.product, expectedToken ) ||
        ContainsIgnoreCase( device.displayName, expectedToken ) ||
        ContainsIgnoreCase( device.name, expectedToken );
      if ( expectedMatched )
        return 5;

      var oppositeMatched =
        ContainsIgnoreCase( description.product, oppositeToken ) ||
        ContainsIgnoreCase( device.displayName, oppositeToken ) ||
        ContainsIgnoreCase( device.name, oppositeToken );
      return oppositeMatched ? -1 : 0;
    }

    private static string BuildDeviceSummary( List<InputDevice> devices )
    {
      if ( devices == null || devices.Count == 0 )
        return string.Empty;

      var builder = new StringBuilder( 128 );
      for ( var deviceIndex = 0; deviceIndex < devices.Count; ++deviceIndex ) {
        if ( deviceIndex > 0 )
          builder.Append( ", " );

        builder.Append( GetDeviceDisplayName( devices[ deviceIndex ] ) );
      }

      return builder.ToString();
    }

    private static string BuildDeviceSummary( List<ScoredInputDevice> devices )
    {
      if ( devices == null || devices.Count == 0 )
        return string.Empty;

      var builder = new StringBuilder( 128 );
      for ( var deviceIndex = 0; deviceIndex < devices.Count; ++deviceIndex ) {
        if ( deviceIndex > 0 )
          builder.Append( ", " );

        builder.Append( GetDeviceDisplayName( devices[ deviceIndex ].Device ) );
      }

      return builder.ToString();
    }

    private static string GetDeviceDisplayName( InputDevice device )
    {
      if ( device == null )
        return string.Empty;

      if ( !string.IsNullOrWhiteSpace( device.displayName ) )
        return device.displayName;

      var manufacturer = device.description.manufacturer;
      var product = device.description.product;
      if ( !string.IsNullOrWhiteSpace( manufacturer ) || !string.IsNullOrWhiteSpace( product ) )
        return string.Format( CultureInfo.InvariantCulture, "{0} {1}", manufacturer, product ).Trim();

      return device.name;
    }

    private static InputControl TryFindControl( InputDevice device, string controlPath )
    {
      if ( device == null || string.IsNullOrWhiteSpace( controlPath ) )
        return null;

      var candidatePaths = GetCandidateControlPaths( controlPath );
      for ( var candidateIndex = 0; candidateIndex < candidatePaths.Length; ++candidateIndex ) {
        var candidatePath = candidatePaths[ candidateIndex ];
        if ( string.IsNullOrWhiteSpace( candidatePath ) )
          continue;

        var control = TryFindControlDirect( device, candidatePath );
        if ( control != null )
          return control;
      }

      return null;
    }

    private static InputControl TryFindControlDirect( InputDevice device, string controlPath )
    {
      var sanitizedPath = controlPath.Trim();
      var control = InputControlPath.TryFindControl( device, sanitizedPath );
      if ( control != null )
        return control;

      var separatorIndex = sanitizedPath.IndexOf( '/' );
      if ( sanitizedPath.StartsWith( "<", StringComparison.Ordinal ) && separatorIndex >= 0 )
        return TryFindControlDirect( device, sanitizedPath.Substring( separatorIndex + 1 ) );

      for ( var controlIndex = 0; controlIndex < device.allControls.Count; ++controlIndex ) {
        var candidate = device.allControls[ controlIndex ];
        if ( IsMatchingControlPath( candidate, sanitizedPath ) )
          return candidate;
      }

      return null;
    }

    private static string[] GetCandidateControlPaths( string controlPath )
    {
      if ( string.IsNullOrWhiteSpace( controlPath ) )
        return Array.Empty<string>();

      switch ( controlPath.Trim().ToLowerInvariant() ) {
        case "axis1":
          return new[] { "Stick/x", "stick/x", "x" };
        case "axis2":
          return new[] { "Stick/y", "stick/y", "y" };
        case "axis3":
          return new[] { "Z", "z", "twist" };
        case "axis4":
          return new[] { "RotateX", "rotatex", "rx", "hat/x", "hatswitch/x" };
        case "axis5":
          return new[] { "RotateY", "rotatey", "ry", "hat/y", "hatswitch/y" };
        case "axis6":
          return new[] { "RotateZ", "rotatez", "rz" };
        case "axis7":
          return new[] { "Throttle", "throttle", "slider" };
        case "axis8":
          return new[] { "Rudder", "rudder", "dial" };
        default:
          return new[] { controlPath };
      }
    }

    private float ReadAxisBinding( ResolvedAxisBinding binding,
                                   ref float rawSnapshotValue,
                                   StringBuilder summaryBuilder,
                                   string scopeLabel = null )
    {
      if ( binding.Config == null || !binding.Config.Enabled ) {
        rawSnapshotValue = 0.0f;
        return 0.0f;
      }

      if ( binding.Config.Mode == FarmStickAxisBindingMode.SingleAxis ) {
        var rawValue = ReadRawScalar( binding.SingleControl );
        rawSnapshotValue = rawValue;
        AppendSingleAxisSummary(
          summaryBuilder,
          GetScopedDisplayName( scopeLabel, binding.Config.DisplayName ),
          GetSummaryPath( binding.Config.ControlPath, binding.SingleControl ),
          rawValue );
        return NormalizeAxis( binding.Config, rawValue );
      }

      var negativeValue = ReadRawScalar( binding.NegativeControl );
      var positiveValue = ReadRawScalar( binding.PositiveControl );
      var rawCompositeValue = positiveValue - negativeValue;
      rawSnapshotValue = rawCompositeValue;
      AppendCompositeAxisSummary(
        summaryBuilder,
        GetScopedDisplayName( scopeLabel, binding.Config.DisplayName ),
        GetSummaryPath( binding.Config.NegativeControlPath, binding.NegativeControl ),
        negativeValue,
        GetSummaryPath( binding.Config.PositiveControlPath, binding.PositiveControl ),
        positiveValue );
      return NormalizeAxis( binding.Config, rawCompositeValue );
    }

    private bool ReadButtonBinding( ResolvedButtonBinding binding,
                                    ref float rawSnapshotValue,
                                    StringBuilder summaryBuilder,
                                    string scopeLabel = null )
    {
      if ( binding.Config == null || !binding.Config.Enabled ) {
        rawSnapshotValue = 0.0f;
        return false;
      }

      var rawValue = Mathf.Clamp01( ReadRawScalar( binding.Control ) );
      rawSnapshotValue = rawValue;
      AppendSingleAxisSummary(
        summaryBuilder,
        GetScopedDisplayName( scopeLabel, binding.Config.DisplayName ),
        GetSummaryPath( binding.Config.ControlPath, binding.Control ),
        rawValue );
      return rawValue >= 0.5f;
    }

    private static float NormalizeAxis( FarmStickAxisBinding binding, float rawValue )
    {
      var normalizedValue = 0.0f;
      if ( rawValue >= binding.Center ) {
        var positiveRange = binding.RawMax - binding.Center;
        if ( Mathf.Abs( positiveRange ) > 1.0e-5f )
          normalizedValue = Mathf.Clamp01( ( rawValue - binding.Center ) / positiveRange );
      }
      else {
        var negativeRange = binding.Center - binding.RawMin;
        if ( Mathf.Abs( negativeRange ) > 1.0e-5f )
          normalizedValue = -Mathf.Clamp01( ( binding.Center - rawValue ) / negativeRange );
      }

      if ( binding.Invert )
        normalizedValue = -normalizedValue;

      if ( Mathf.Abs( normalizedValue ) <= binding.Deadzone )
        return 0.0f;

      return Mathf.Clamp( normalizedValue, -1.0f, 1.0f );
    }

    private static float ReadRawScalar( InputControl control )
    {
      if ( control == null )
        return 0.0f;

      if ( control is AxisControl axisControl )
        return SanitizeFiniteValue( axisControl.ReadValue() );

      if ( control is ButtonControl buttonControl )
        return SanitizeFiniteValue( buttonControl.ReadValue() );

      if ( TryReadNumericValue( control, out var numericValue ) )
        return SanitizeFiniteValue( numericValue );

      return SanitizeFiniteValue( control.EvaluateMagnitude() );
    }

    private static bool TryReadNumericValue( InputControl control, out float value )
    {
      value = 0.0f;
      if ( control == null )
        return false;

      try {
        var rawValue = control.ReadValueAsObject();
        switch ( rawValue ) {
          case float floatValue:
            value = floatValue;
            return true;
          case double doubleValue:
            value = (float)doubleValue;
            return true;
          case int intValue:
            value = intValue;
            return true;
          case uint uintValue:
            value = uintValue;
            return true;
          case short shortValue:
            value = shortValue;
            return true;
          case ushort ushortValue:
            value = ushortValue;
            return true;
          case byte byteValue:
            value = byteValue;
            return true;
          case sbyte sbyteValue:
            value = sbyteValue;
            return true;
          case bool boolValue:
            value = boolValue ? 1.0f : 0.0f;
            return true;
          case IConvertible convertible:
            value = convertible.ToSingle( CultureInfo.InvariantCulture );
            return true;
          default:
            return false;
        }
      }
      catch {
        value = 0.0f;
        return false;
      }
    }

    private static float SanitizeFiniteValue( float value )
    {
      return float.IsNaN( value ) || float.IsInfinity( value ) ? 0.0f : value;
    }

    private static bool IsMatchingControlPath( InputControl control, string controlPath )
    {
      if ( control == null || string.IsNullOrWhiteSpace( controlPath ) )
        return false;

      var normalizedRequestedPath = NormalizeControlPath( controlPath );
      if ( normalizedRequestedPath.Length == 0 )
        return false;

      if ( string.Equals( NormalizeControlPath( control.name ), normalizedRequestedPath, StringComparison.OrdinalIgnoreCase ) )
        return true;

      if ( string.Equals( NormalizeControlPath( control.displayName ), normalizedRequestedPath, StringComparison.OrdinalIgnoreCase ) )
        return true;

      var relativePath = NormalizeControlPath( GetRelativeControlPath( control ) );
      if ( string.Equals( relativePath, normalizedRequestedPath, StringComparison.OrdinalIgnoreCase ) )
        return true;

      return relativePath.EndsWith( "/" + normalizedRequestedPath, StringComparison.OrdinalIgnoreCase );
    }

    private static string GetSummaryPath( string configuredPath, InputControl control )
    {
      var resolvedPath = GetRelativeControlPath( control );
      if ( string.IsNullOrWhiteSpace( resolvedPath ) ||
           string.Equals( resolvedPath, configuredPath, StringComparison.OrdinalIgnoreCase ) )
        return configuredPath;

      return $"{configuredPath}->{resolvedPath}";
    }

    private static string GetRelativeControlPath( InputControl control )
    {
      if ( control == null || string.IsNullOrWhiteSpace( control.path ) )
        return string.Empty;

      var path = control.path.Trim();
      if ( path.Length == 0 )
        return string.Empty;

      var firstSeparatorIndex = path.IndexOf( '/', 1 );
      if ( firstSeparatorIndex < 0 || firstSeparatorIndex + 1 >= path.Length )
        return path.Trim( '/' );

      return path.Substring( firstSeparatorIndex + 1 );
    }

    private static string NormalizeControlPath( string path )
    {
      return string.IsNullOrWhiteSpace( path ) ? string.Empty : path.Trim().Trim( '/' );
    }

    private void UpdateStickModeSwitchState( FarmStickButtonBinding binding, bool isPressed )
    {
      if ( binding == null || !binding.Enabled || m_profile == null ) {
        m_stickModeSwapActive = false;
        m_previousControlModeSwitchPressed = false;
        return;
      }

      if ( m_profile.ControlModeSwitchBehavior == FarmStickControlModeSwitchBehavior.Hold )
        m_stickModeSwapActive = isPressed;
      else if ( isPressed && !m_previousControlModeSwitchPressed )
        m_stickModeSwapActive = !m_stickModeSwapActive;

      m_previousControlModeSwitchPressed = isPressed;
    }

    private void ApplyStickRouting( float mainStickX,
                                    float mainStickY,
                                    float miniStickX,
                                    float miniStickY,
                                    ref OperatorCommand command )
    {
      if ( m_stickModeSwapActive ) {
        command.LeftStickX = miniStickX;
        command.LeftStickY = miniStickY;
        command.RightStickX = mainStickX;
        command.RightStickY = mainStickY;
      }
      else {
        command.LeftStickX = mainStickX;
        command.LeftStickY = mainStickY;
        command.RightStickX = miniStickX;
        command.RightStickY = miniStickY;
      }
    }

    private void ApplyStickSensitivity( ref OperatorCommand command )
    {
      command.LeftStickX = ApplySensitivity( command.LeftStickX, m_leftStickXSensitivity );
      command.LeftStickY = ApplySensitivity( command.LeftStickY, m_leftStickYSensitivity );
      command.RightStickX = ApplySensitivity( command.RightStickX, m_rightStickXSensitivity );
      command.RightStickY = ApplySensitivity( command.RightStickY, m_rightStickYSensitivity );
    }

    private static float ApplySensitivity( float value, float sensitivity )
    {
      return Mathf.Clamp( value * sensitivity, -1.0f, 1.0f );
    }

    private string BuildBindingStatus( string status )
    {
      var modeLabel = IsDualStickConfigured ?
                      m_usedStableDualDeviceAssignmentFallback ? "Dual Main Sticks + Track Levers (stable device order)" :
                                                                  "Dual Main Sticks + Track Levers" :
                      m_stickModeSwapActive ? "Main->Boom/Bucket" :
                                              "Main->Swing/Stick";
      return string.IsNullOrWhiteSpace( status ) ? modeLabel : $"{status} | {modeLabel}";
    }

    private void AppendRoutingSummary( StringBuilder builder )
    {
      AppendSummaryPrefix( builder );
      if ( IsDualStickConfigured ) {
        builder.Append( "Routing=Left main->Swing/Stick, Right main->Boom/Bucket, Left/Right drive levers->tracks" );
        if ( m_usedStableDualDeviceAssignmentFallback ) {
          builder.Append( " (stable device order" );
          if ( m_swapDualStickAssignments )
            builder.Append( ", swapped" );
          builder.Append( ')' );
        }

        return;
      }

      builder.Append( "Stick Mode=" );
      builder.Append( m_stickModeSwapActive ? "Main->Boom/Bucket" : "Main->Swing/Stick" );
    }

    private static string CombineDualBindingStatus( string deviceStatus, string leftStatus, string rightStatus )
    {
      var statusParts = new List<string>();
      AppendStatusPart( statusParts, deviceStatus );
      AppendStatusPart( statusParts, leftStatus, "Left " );
      AppendStatusPart( statusParts, rightStatus, "Right " );
      return statusParts.Count == 0 ? ReadyStatus : string.Join( " | ", statusParts );
    }

    private static void AppendStatusPart( List<string> statusParts, string status, string prefix = "" )
    {
      if ( string.IsNullOrWhiteSpace( status ) || string.Equals( status, ReadyStatus, StringComparison.Ordinal ) )
        return;

      statusParts.Add( prefix + status );
    }

    private static string GetScopedDisplayName( string scopeLabel, string displayName )
    {
      return string.IsNullOrWhiteSpace( scopeLabel ) ? displayName : $"{scopeLabel} {displayName}";
    }

    private static void AppendSingleAxisSummary( StringBuilder builder, string displayName, string path, float rawValue )
    {
      AppendSummaryPrefix( builder );
      builder.Append( displayName );
      builder.Append( '[' );
      builder.Append( path );
      builder.Append( "]=" );
      builder.Append( rawValue.ToString( "0.###", CultureInfo.InvariantCulture ) );
    }

    private static void AppendCompositeAxisSummary( StringBuilder builder,
                                                    string displayName,
                                                    string negativePath,
                                                    float negativeValue,
                                                    string positivePath,
                                                    float positiveValue )
    {
      AppendSummaryPrefix( builder );
      builder.Append( displayName );
      builder.Append( "[-" );
      builder.Append( negativePath );
      builder.Append( '=' );
      builder.Append( negativeValue.ToString( "0.###", CultureInfo.InvariantCulture ) );
      builder.Append( ", +" );
      builder.Append( positivePath );
      builder.Append( '=' );
      builder.Append( positiveValue.ToString( "0.###", CultureInfo.InvariantCulture ) );
      builder.Append( ']' );
    }

    private static void AppendSummaryPrefix( StringBuilder builder )
    {
      if ( builder.Length > 0 )
        builder.Append( " | " );
    }
#endif
  }
}

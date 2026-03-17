using System;
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
    private const string NoDeviceStatus = "No Device";
    private const string AmbiguousDeviceStatus = "Ambiguous Device";
    private const string InputSystemDisabledStatus = "Input System Disabled";

    [SerializeField]
    private FarmStickControlProfile m_profile = null;

    private bool m_deviceConnected = false;
    private string m_deviceDisplayName = string.Empty;
    private string m_bindingStatus = NoProfileStatus;
    private string m_lastRawInputSummary = string.Empty;
    private HardwareInputSnapshot m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;

    public override string SourceName => "FarmStick";

    public bool DeviceConnected => m_deviceConnected;
    public string DeviceDisplayName => m_deviceDisplayName;
    public string ProfileName => m_profile != null ? m_profile.DisplayName : string.Empty;
    public string BindingStatus => m_bindingStatus;
    public string LastRawInputSummary => m_lastRawInputSummary;
    public HardwareInputSnapshot LastRawInputSnapshot => m_lastRawInputSnapshot;

    private void OnEnable()
    {
      ResetDiagnostics( m_profile != null ? NoDeviceStatus : NoProfileStatus );
    }

    private void OnDisable()
    {
      ResetDiagnostics( m_profile != null ? NoDeviceStatus : NoProfileStatus );
    }

    private void OnValidate()
    {
      ResetDiagnostics( m_profile != null ? NoDeviceStatus : NoProfileStatus );
    }

    public override OperatorCommand ReadCommand()
    {
      m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;
      m_lastRawInputSummary = string.Empty;

      if ( m_profile == null ) {
        ResetDiagnostics( NoProfileStatus );
        return OperatorCommand.Zero;
      }

#if ENABLE_INPUT_SYSTEM
      if ( !TryResolveDevice( out var device, out var status ) ) {
        ResetDiagnostics( status );
        return OperatorCommand.Zero;
      }

      m_deviceConnected = true;
      m_deviceDisplayName = GetDeviceDisplayName( device );

      if ( !TryResolveBindings( device, out var bindings, out status ) ) {
        m_bindingStatus = status;
        return OperatorCommand.Zero;
      }

      var summaryBuilder = new StringBuilder( 256 );
      var command = OperatorCommand.Zero;
      command.LeftStickX = ReadAxisBinding( bindings.LeftStickX, ref m_lastRawInputSnapshot.LeftStickX, summaryBuilder );
      command.LeftStickY = ReadAxisBinding( bindings.LeftStickY, ref m_lastRawInputSnapshot.LeftStickY, summaryBuilder );
      command.RightStickX = ReadAxisBinding( bindings.RightStickX, ref m_lastRawInputSnapshot.RightStickX, summaryBuilder );
      command.RightStickY = ReadAxisBinding( bindings.RightStickY, ref m_lastRawInputSnapshot.RightStickY, summaryBuilder );
      command.Drive = ReadAxisBinding( bindings.Drive, ref m_lastRawInputSnapshot.Drive, summaryBuilder );
      command.Steer = ReadAxisBinding( bindings.Steer, ref m_lastRawInputSnapshot.Steer, summaryBuilder );
      command.ResetRequested = ReadButtonBinding( bindings.ResetEpisode, ref m_lastRawInputSnapshot.ResetButton, summaryBuilder );
      command.StartEpisodeRequested = ReadButtonBinding( bindings.StartEpisode, ref m_lastRawInputSnapshot.StartEpisodeButton, summaryBuilder );
      command.StopEpisodeRequested = ReadButtonBinding( bindings.StopEpisode, ref m_lastRawInputSnapshot.StopEpisodeButton, summaryBuilder );

      m_bindingStatus = ReadyStatus;
      m_lastRawInputSummary = summaryBuilder.ToString();
      return command.ClampAxes();
#else
      ResetDiagnostics( InputSystemDisabledStatus );
      return OperatorCommand.Zero;
#endif
    }

    private void ResetDiagnostics( string status )
    {
      m_deviceConnected = false;
      m_deviceDisplayName = string.Empty;
      m_bindingStatus = status;
      m_lastRawInputSummary = string.Empty;
      m_lastRawInputSnapshot = HardwareInputSnapshot.Zero;
    }

#if ENABLE_INPUT_SYSTEM
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
      public ResolvedButtonBinding ResetEpisode;
      public ResolvedButtonBinding StartEpisode;
      public ResolvedButtonBinding StopEpisode;
    }

    private bool TryResolveDevice( out InputDevice device, out string status )
    {
      device = null;
      status = NoDeviceStatus;

      var matchedDeviceCount = 0;
      foreach ( var candidate in InputSystem.devices ) {
        if ( candidate == null || !MatchesDevice( candidate ) )
          continue;

        ++matchedDeviceCount;
        if ( matchedDeviceCount == 1 )
          device = candidate;
      }

      if ( matchedDeviceCount == 0 ) {
        device = null;
        return false;
      }

      if ( matchedDeviceCount > 1 ) {
        device = null;
        status = AmbiguousDeviceStatus;
        return false;
      }

      status = ReadyStatus;
      return true;
    }

    private bool TryResolveBindings( InputDevice device, out ResolvedFarmStickBindings bindings, out string status )
    {
      bindings = new ResolvedFarmStickBindings();

      if ( !TryResolveAxisBinding( device, m_profile.LeftStickX, out bindings.LeftStickX, out status ) ||
           !TryResolveAxisBinding( device, m_profile.LeftStickY, out bindings.LeftStickY, out status ) ||
           !TryResolveAxisBinding( device, m_profile.RightStickX, out bindings.RightStickX, out status ) ||
           !TryResolveAxisBinding( device, m_profile.RightStickY, out bindings.RightStickY, out status ) ||
           !TryResolveAxisBinding( device, m_profile.Drive, out bindings.Drive, out status ) ||
           !TryResolveAxisBinding( device, m_profile.Steer, out bindings.Steer, out status ) ||
           !TryResolveButtonBinding( device, m_profile.ResetEpisode, out bindings.ResetEpisode, out status ) ||
           !TryResolveButtonBinding( device, m_profile.StartEpisode, out bindings.StartEpisode, out status ) ||
           !TryResolveButtonBinding( device, m_profile.StopEpisode, out bindings.StopEpisode, out status ) )
        return false;

      status = ReadyStatus;
      return true;
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

    private bool MatchesDevice( InputDevice device )
    {
      if ( device == null || m_profile == null )
        return false;

      var description = device.description;
      return ContainsIgnoreCase( description.manufacturer, m_profile.ManufacturerContains ) &&
             ( ContainsIgnoreCase( description.product, m_profile.ProductContains ) ||
               ContainsIgnoreCase( device.displayName, m_profile.ProductContains ) ||
               ContainsIgnoreCase( device.name, m_profile.ProductContains ) );
    }

    private static bool ContainsIgnoreCase( string candidate, string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return true;

      return !string.IsNullOrWhiteSpace( candidate ) &&
             candidate.IndexOf( value, StringComparison.OrdinalIgnoreCase ) >= 0;
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

      var sanitizedPath = controlPath.Trim();
      var control = InputControlPath.TryFindControl( device, sanitizedPath );
      if ( control != null )
        return control;

      var separatorIndex = sanitizedPath.IndexOf( '/' );
      if ( sanitizedPath.StartsWith( "<", StringComparison.Ordinal ) && separatorIndex >= 0 )
        return InputControlPath.TryFindControl( device, sanitizedPath.Substring( separatorIndex + 1 ) );

      return null;
    }

    private float ReadAxisBinding( ResolvedAxisBinding binding, ref float rawSnapshotValue, StringBuilder summaryBuilder )
    {
      if ( binding.Config == null || !binding.Config.Enabled ) {
        rawSnapshotValue = 0.0f;
        return 0.0f;
      }

      if ( binding.Config.Mode == FarmStickAxisBindingMode.SingleAxis ) {
        var rawValue = ReadRawScalar( binding.SingleControl );
        rawSnapshotValue = rawValue;
        AppendSingleAxisSummary( summaryBuilder, binding.Config.DisplayName, binding.Config.ControlPath, rawValue );
        return NormalizeAxis( binding.Config, rawValue );
      }

      var negativeValue = ReadRawScalar( binding.NegativeControl );
      var positiveValue = ReadRawScalar( binding.PositiveControl );
      var rawCompositeValue = positiveValue - negativeValue;
      rawSnapshotValue = rawCompositeValue;
      AppendCompositeAxisSummary(
        summaryBuilder,
        binding.Config.DisplayName,
        binding.Config.NegativeControlPath,
        negativeValue,
        binding.Config.PositiveControlPath,
        positiveValue );
      return NormalizeAxis( binding.Config, rawCompositeValue );
    }

    private bool ReadButtonBinding( ResolvedButtonBinding binding, ref float rawSnapshotValue, StringBuilder summaryBuilder )
    {
      if ( binding.Config == null || !binding.Config.Enabled ) {
        rawSnapshotValue = 0.0f;
        return false;
      }

      var rawValue = Mathf.Clamp01( ReadRawScalar( binding.Control ) );
      rawSnapshotValue = rawValue;
      AppendSingleAxisSummary( summaryBuilder, binding.Config.DisplayName, binding.Config.ControlPath, rawValue );
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

      return SanitizeFiniteValue( control.EvaluateMagnitude() );
    }

    private static float SanitizeFiniteValue( float value )
    {
      return float.IsNaN( value ) || float.IsInfinity( value ) ? 0.0f : value;
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

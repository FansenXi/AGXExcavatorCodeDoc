#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AGXUnity_Excavator.Scripts.Editor
{
  public static class FarmStickInputDiagnosticsUtility
  {
    [MenuItem( "Tools/AGX Excavator/FarmStick/Log Input System Devices" )]
    public static void LogInputSystemDevices()
    {
      var builder = new StringBuilder( 2048 );
      builder.AppendLine( $"Input System devices: {InputSystem.devices.Count}" );

      for ( var deviceIndex = 0; deviceIndex < InputSystem.devices.Count; ++deviceIndex )
        AppendDeviceSummary( builder, InputSystem.devices[ deviceIndex ] );

      Debug.Log( builder.ToString() );
    }

    [MenuItem( "Tools/AGX Excavator/FarmStick/Log FarmStick Devices And Controls" )]
    public static void LogFarmStickDevicesAndControls()
    {
      var builder = new StringBuilder( 4096 );
      var matchCount = 0;

      for ( var deviceIndex = 0; deviceIndex < InputSystem.devices.Count; ++deviceIndex ) {
        var device = InputSystem.devices[ deviceIndex ];
        if ( !LooksLikeFarmStick( device ) )
          continue;

        ++matchCount;
        AppendDeviceSummary( builder, device );
        AppendControlSummary( builder, device );
      }

      if ( matchCount == 0 )
        builder.AppendLine( "No FarmStick-like Input System devices were found." );

      Debug.Log( builder.ToString() );
    }

    [MenuItem( "Tools/AGX Excavator/FarmStick/Log FarmStick Current Values" )]
    public static void LogFarmStickCurrentValues()
    {
      var builder = new StringBuilder( 4096 );
      var matchCount = 0;

      for ( var deviceIndex = 0; deviceIndex < InputSystem.devices.Count; ++deviceIndex ) {
        var device = InputSystem.devices[ deviceIndex ];
        if ( !LooksLikeFarmStick( device ) )
          continue;

        ++matchCount;
        AppendDeviceSummary( builder, device );
        AppendCurrentValueSummary( builder, device );
      }

      if ( matchCount == 0 )
        builder.AppendLine( "No FarmStick-like Input System devices were found." );

      Debug.Log( builder.ToString() );
    }

    private static bool LooksLikeFarmStick( InputDevice device )
    {
      if ( device == null )
        return false;

      var description = device.description;
      return ContainsIgnoreCase( description.product, "FarmStick" ) ||
             ContainsIgnoreCase( description.manufacturer, "Thrust" ) ||
             ContainsIgnoreCase( device.displayName, "FarmStick" ) ||
             ContainsIgnoreCase( device.name, "farmstick" );
    }

    private static void AppendDeviceSummary( StringBuilder builder, InputDevice device )
    {
      if ( device == null )
        return;

      var description = device.description;
      builder.Append( "- " );
      builder.AppendLine( string.IsNullOrWhiteSpace( device.displayName ) ? device.name : device.displayName );
      builder.AppendLine( $"  name: {device.name}" );
      builder.AppendLine( $"  layout: {device.layout}" );
      builder.AppendLine( $"  interface: {description.interfaceName}" );
      builder.AppendLine( $"  manufacturer: {description.manufacturer}" );
      builder.AppendLine( $"  product: {description.product}" );
      builder.AppendLine( $"  deviceId: {device.deviceId}" );
    }

    private static void AppendControlSummary( StringBuilder builder, InputDevice device )
    {
      builder.AppendLine( "  controls:" );
      for ( var controlIndex = 0; controlIndex < device.allControls.Count; ++controlIndex ) {
        var control = device.allControls[ controlIndex ];
        if ( control == null || control == device )
          continue;

        builder.Append( "    - " );
        builder.Append( control.path );
        builder.Append( " [" );
        builder.Append( control.layout );
        builder.AppendLine( "]" );
      }
    }

    private static void AppendCurrentValueSummary( StringBuilder builder, InputDevice device )
    {
      builder.AppendLine( "  live values:" );
      var foundActuatedControl = false;
      for ( var controlIndex = 0; controlIndex < device.allControls.Count; ++controlIndex ) {
        var control = device.allControls[ controlIndex ];
        if ( control == null || control == device )
          continue;

        if ( !TryReadNumericValue( control, out var value ) )
          continue;

        if ( Mathf.Abs( value ) <= 1.0e-4f )
          continue;

        foundActuatedControl = true;
        builder.Append( "    - " );
        builder.Append( control.path );
        builder.Append( " = " );
        builder.Append( value.ToString( "0.###", System.Globalization.CultureInfo.InvariantCulture ) );
        builder.Append( " [" );
        builder.Append( control.layout );
        builder.AppendLine( "]" );
      }

      if ( !foundActuatedControl )
        builder.AppendLine( "    (all numeric/button controls are at their default value right now)" );
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
          case System.IConvertible convertible:
            value = convertible.ToSingle( System.Globalization.CultureInfo.InvariantCulture );
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

    private static bool ContainsIgnoreCase( string candidate, string value )
    {
      return !string.IsNullOrWhiteSpace( candidate ) &&
             candidate.IndexOf( value, System.StringComparison.OrdinalIgnoreCase ) >= 0;
    }
  }
}
#endif

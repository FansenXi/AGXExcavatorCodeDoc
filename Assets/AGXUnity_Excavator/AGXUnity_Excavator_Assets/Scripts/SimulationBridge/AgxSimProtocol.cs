using System;
using System.IO;
using System.Text;

namespace AGXUnity_Excavator.Scripts.SimulationBridge
{
  internal static class AgxSimProtocolConstants
  {
    public const uint Magic = 0xA6A6A6A6u;
    public const ushort HeaderVersion = 1;
    public const int HeaderSizeBytes = 16;
    public const int MaxPayloadBytes = 128 * 1024 * 1024;

    public const string ProtocolVersion = "agx-sim/v0";
    public const string ActionSemantics = "actuator_speed_cmd";
    public const string ImagePixelFormat = "raw_rgb";
    public const string ImageRowOrder = "top_to_bottom";
  }

  internal enum AgxSimMessageType : ushort
  {
    GetInfoReq = 1,
    GetInfoResp = 2,
    ResetReq = 3,
    ResetResp = 4,
    StepReq = 5,
    StepResp = 6
  }

  [Serializable]
  public class AgxSimCameraDescriptor
  {
    public string name = string.Empty;
    public int width = 0;
    public int height = 0;
    public float fps = 0.0f;
    public string pixel_format = AgxSimProtocolConstants.ImagePixelFormat;
    public string row_order = AgxSimProtocolConstants.ImageRowOrder;
  }

  [Serializable]
  public class AgxSimImageFrame
  {
    public string name = string.Empty;
    public int width = 0;
    public int height = 0;
    public string pixel_format = AgxSimProtocolConstants.ImagePixelFormat;
    public string row_order = AgxSimProtocolConstants.ImageRowOrder;
    public byte[] data = Array.Empty<byte>();
  }

  [Serializable]
  public class AgxSimRequestPayload
  {
    public long step_id = 0;
    public float[] action = Array.Empty<float>();
    public int seed = 0;
    public string scenario_id = string.Empty;
    public long client_time_ns = -1;
    public bool reset_terrain = true;
    public bool reset_pose = true;
  }

  [Serializable]
  public class AgxSimResponsePayload
  {
    public string protocol_version = AgxSimProtocolConstants.ProtocolVersion;
    public bool success = true;
    public bool reset_applied = false;
    public string error = string.Empty;

    public float dt = 0.02f;
    public float control_hz = 50.0f;
    public string action_semantics = AgxSimProtocolConstants.ActionSemantics;

    public string[] action_order = Array.Empty<string>();
    public string[] qpos_order = Array.Empty<string>();
    public string[] qvel_order = Array.Empty<string>();
    public string[] env_state_order = Array.Empty<string>();
    public string[] camera_names = Array.Empty<string>();
    public AgxSimCameraDescriptor[] cameras = Array.Empty<AgxSimCameraDescriptor>();
    public string[] warnings = Array.Empty<string>();

    public bool supports_reset_pose = false;
    public bool supports_images = false;

    public long step_id = 0;
    public float[] qpos = Array.Empty<float>();
    public float[] qvel = Array.Empty<float>();
    public float[] env_state = Array.Empty<float>();
    public AgxSimImageFrame image_fpv = null;
    public float reward = 0.0f;
    public long sim_time_ns = -1;
  }

  internal static class AgxSimBinaryProtocol
  {
    private static readonly uint[] s_crcTable = CreateCrc32Table();

    public static bool TryReadFrame( Stream stream,
                                     out AgxSimMessageType messageType,
                                     out byte[] payload,
                                     out string error )
    {
      messageType = 0;
      payload = Array.Empty<byte>();
      error = string.Empty;

      var headerBytes = new byte[ AgxSimProtocolConstants.HeaderSizeBytes ];
      if ( !TryReadExactly( stream, headerBytes, 0, headerBytes.Length ) ) {
        error = "stream_closed";
        return false;
      }

      uint magic;
      ushort version;
      ushort rawMessageType;
      uint payloadLength;
      uint expectedCrc32;

      using ( var headerStream = new MemoryStream( headerBytes, false ) )
      using ( var reader = new BinaryReader( headerStream, Encoding.UTF8 ) ) {
        magic = reader.ReadUInt32();
        version = reader.ReadUInt16();
        rawMessageType = reader.ReadUInt16();
        payloadLength = reader.ReadUInt32();
        expectedCrc32 = reader.ReadUInt32();
      }

      if ( magic != AgxSimProtocolConstants.Magic ) {
        error = "invalid_magic";
        return false;
      }

      if ( version != AgxSimProtocolConstants.HeaderVersion ) {
        error = "unsupported_header_version";
        return false;
      }

      if ( payloadLength > AgxSimProtocolConstants.MaxPayloadBytes ) {
        error = "payload_too_large";
        return false;
      }

      messageType = (AgxSimMessageType)rawMessageType;
      payload = new byte[ payloadLength ];
      if ( payloadLength > 0 && !TryReadExactly( stream, payload, 0, (int)payloadLength ) ) {
        error = "payload_read_failed";
        return false;
      }

      var actualCrc32 = ComputeCrc32( payload );
      if ( actualCrc32 != expectedCrc32 ) {
        error = "crc_mismatch";
        return false;
      }

      return true;
    }

    public static bool TryDeserializeRequest( AgxSimMessageType messageType,
                                              byte[] payloadBytes,
                                              out AgxSimRequestPayload payload,
                                              out string error )
    {
      payload = new AgxSimRequestPayload();
      error = string.Empty;

      try {
        using ( var payloadStream = new MemoryStream( payloadBytes ?? Array.Empty<byte>(), false ) )
        using ( var reader = new BinaryReader( payloadStream, Encoding.UTF8 ) ) {
          switch ( messageType ) {
            case AgxSimMessageType.GetInfoReq:
              return true;
            case AgxSimMessageType.ResetReq:
              if ( payloadStream.Length == 0 )
                return true;

              payload.seed = reader.ReadInt32();
              payload.reset_terrain = ReadBoolean( reader );
              payload.reset_pose = ReadBoolean( reader );
              payload.client_time_ns = payloadStream.Position + sizeof(long) <= payloadStream.Length ?
                                       reader.ReadInt64() :
                                       -1;
              payload.scenario_id = payloadStream.Position < payloadStream.Length ?
                                    ReadString( reader ) :
                                    string.Empty;
              return true;
            case AgxSimMessageType.StepReq:
              payload.step_id = reader.ReadInt64();
              payload.action = ReadFloatArray( reader );
              payload.client_time_ns = payloadStream.Position + sizeof(long) <= payloadStream.Length ?
                                       reader.ReadInt64() :
                                       -1;
              return true;
            default:
              error = "unsupported_request_type";
              return false;
          }
        }
      }
      catch ( EndOfStreamException ) {
        error = "payload_truncated";
        return false;
      }
      catch ( Exception exception ) {
        error = $"payload_decode_failed:{exception.Message}";
        return false;
      }
    }

    public static byte[] SerializeResponse( AgxSimMessageType messageType, AgxSimResponsePayload payload )
    {
      payload = payload ?? new AgxSimResponsePayload();

      byte[] payloadBytes;
      using ( var payloadStream = new MemoryStream() )
      using ( var writer = new BinaryWriter( payloadStream, Encoding.UTF8 ) ) {
        switch ( messageType ) {
          case AgxSimMessageType.GetInfoResp:
            WriteGetInfoResponsePayload( writer, payload );
            break;
          case AgxSimMessageType.ResetResp:
            WriteResetResponsePayload( writer, payload );
            break;
          case AgxSimMessageType.StepResp:
            WriteStepResponsePayload( writer, payload );
            break;
          default:
            throw new InvalidOperationException( $"Unsupported response type: {messageType}" );
        }

        writer.Flush();
        payloadBytes = payloadStream.ToArray();
      }

      var frameBytes = new byte[ AgxSimProtocolConstants.HeaderSizeBytes + payloadBytes.Length ];
      using ( var frameStream = new MemoryStream( frameBytes, true ) )
      using ( var writer = new BinaryWriter( frameStream, Encoding.UTF8 ) ) {
        writer.Write( AgxSimProtocolConstants.Magic );
        writer.Write( AgxSimProtocolConstants.HeaderVersion );
        writer.Write( (ushort)messageType );
        writer.Write( (uint)payloadBytes.Length );
        writer.Write( ComputeCrc32( payloadBytes ) );
        writer.Write( payloadBytes );
        writer.Flush();
      }

      return frameBytes;
    }

    private static void WriteGetInfoResponsePayload( BinaryWriter writer, AgxSimResponsePayload payload )
    {
      WriteCommonResponsePrefix( writer, payload );
      WriteString( writer, payload.protocol_version );
      writer.Write( payload.dt );
      writer.Write( payload.control_hz );
      WriteString( writer, payload.action_semantics );
      WriteStringArray( writer, payload.action_order );
      WriteStringArray( writer, payload.qpos_order );
      WriteStringArray( writer, payload.qvel_order );
      WriteStringArray( writer, payload.env_state_order );
      WriteStringArray( writer, payload.camera_names );
      WriteBoolean( writer, payload.supports_reset_pose );
      WriteBoolean( writer, payload.supports_images );
      WriteCameraDescriptors( writer, payload.cameras );
      WriteStringArray( writer, payload.warnings );
    }

    private static void WriteResetResponsePayload( BinaryWriter writer, AgxSimResponsePayload payload )
    {
      WriteCommonResponsePrefix( writer, payload );
      WriteBoolean( writer, payload.reset_applied );
      writer.Write( payload.dt );
      writer.Write( payload.control_hz );
      WriteStringArray( writer, payload.warnings );
    }

    private static void WriteStepResponsePayload( BinaryWriter writer, AgxSimResponsePayload payload )
    {
      WriteCommonResponsePrefix( writer, payload );
      writer.Write( payload.step_id );
      WriteFloatArray( writer, payload.qpos );
      WriteFloatArray( writer, payload.qvel );
      WriteFloatArray( writer, payload.env_state );

      var image = payload.image_fpv;
      WriteString( writer, image != null ? image.pixel_format : string.Empty );
      writer.Write( image != null ? image.width : 0 );
      writer.Write( image != null ? image.height : 0 );
      WriteBytes( writer, image != null ? image.data : Array.Empty<byte>() );

      writer.Write( payload.reward );
      writer.Write( payload.sim_time_ns );
      WriteStringArray( writer, payload.warnings );
    }

    private static void WriteCommonResponsePrefix( BinaryWriter writer, AgxSimResponsePayload payload )
    {
      WriteBoolean( writer, payload.success );
      WriteString( writer, payload.error );
    }

    private static void WriteCameraDescriptors( BinaryWriter writer, AgxSimCameraDescriptor[] cameras )
    {
      cameras = cameras ?? Array.Empty<AgxSimCameraDescriptor>();
      writer.Write( cameras.Length );
      for ( var cameraIndex = 0; cameraIndex < cameras.Length; ++cameraIndex ) {
        var camera = cameras[ cameraIndex ] ?? new AgxSimCameraDescriptor();
        WriteString( writer, camera.name );
        writer.Write( camera.width );
        writer.Write( camera.height );
        writer.Write( camera.fps );
        WriteString( writer, camera.pixel_format );
        WriteString( writer, camera.row_order );
      }
    }

    private static void WriteFloatArray( BinaryWriter writer, float[] values )
    {
      values = values ?? Array.Empty<float>();
      writer.Write( values.Length );
      for ( var valueIndex = 0; valueIndex < values.Length; ++valueIndex )
        writer.Write( values[ valueIndex ] );
    }

    private static float[] ReadFloatArray( BinaryReader reader )
    {
      var length = reader.ReadInt32();
      if ( length < 0 || length > 1024 * 1024 )
        throw new InvalidDataException( "float_array_length_invalid" );

      var values = new float[ length ];
      for ( var valueIndex = 0; valueIndex < length; ++valueIndex )
        values[ valueIndex ] = reader.ReadSingle();
      return values;
    }

    private static void WriteStringArray( BinaryWriter writer, string[] values )
    {
      values = values ?? Array.Empty<string>();
      writer.Write( values.Length );
      for ( var valueIndex = 0; valueIndex < values.Length; ++valueIndex )
        WriteString( writer, values[ valueIndex ] );
    }

    private static void WriteBytes( BinaryWriter writer, byte[] bytes )
    {
      bytes = bytes ?? Array.Empty<byte>();
      writer.Write( bytes.Length );
      if ( bytes.Length > 0 )
        writer.Write( bytes );
    }

    private static void WriteString( BinaryWriter writer, string value )
    {
      var bytes = string.IsNullOrEmpty( value ) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes( value );
      writer.Write( bytes.Length );
      if ( bytes.Length > 0 )
        writer.Write( bytes );
    }

    private static string ReadString( BinaryReader reader )
    {
      var length = reader.ReadInt32();
      if ( length < 0 || length > AgxSimProtocolConstants.MaxPayloadBytes )
        throw new InvalidDataException( "string_length_invalid" );

      if ( length == 0 )
        return string.Empty;

      var bytes = reader.ReadBytes( length );
      if ( bytes.Length != length )
        throw new EndOfStreamException();

      return Encoding.UTF8.GetString( bytes );
    }

    private static void WriteBoolean( BinaryWriter writer, bool value )
    {
      writer.Write( (byte)( value ? 1 : 0 ) );
    }

    private static bool ReadBoolean( BinaryReader reader )
    {
      return reader.ReadByte() != 0;
    }

    private static bool TryReadExactly( Stream stream, byte[] buffer, int offset, int count )
    {
      var totalRead = 0;
      while ( totalRead < count ) {
        var read = stream.Read( buffer, offset + totalRead, count - totalRead );
        if ( read <= 0 )
          return false;

        totalRead += read;
      }

      return true;
    }

    private static uint ComputeCrc32( byte[] data )
    {
      var crc = 0xFFFFFFFFu;
      if ( data != null ) {
        for ( var byteIndex = 0; byteIndex < data.Length; ++byteIndex )
          crc = ( crc >> 8 ) ^ s_crcTable[ ( crc ^ data[ byteIndex ] ) & 0xFFu ];
      }

      return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] CreateCrc32Table()
    {
      var table = new uint[ 256 ];
      for ( uint tableIndex = 0; tableIndex < table.Length; ++tableIndex ) {
        var crc = tableIndex;
        for ( var bitIndex = 0; bitIndex < 8; ++bitIndex )
          crc = ( crc & 1u ) != 0u ? 0xEDB88320u ^ ( crc >> 1 ) : crc >> 1;
        table[ tableIndex ] = crc;
      }

      return table;
    }
  }
}

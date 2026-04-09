using System.Diagnostics;
using System.IO;
using System.Text;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  public static class RoiExternalRuntimeLauncher
  {
    public static bool TryLaunch( RoiEncConfiguration.ExternalRuntimeOptions options,
                                  bool selfTest,
                                  out Process process,
                                  out string error )
    {
      process = null;
      error = string.Empty;

      if ( !TryCreateStartInfo( options, selfTest, out var startInfo, out error ) )
        return false;

      try {
        process = Process.Start( startInfo );
        if ( process == null ) {
          error = "roi_external_runtime_process_start_returned_null";
          return false;
        }

        return true;
      }
      catch ( System.Exception exception ) {
        error = exception.Message;
        return false;
      }
    }

    public static bool TryCreateStartInfo( RoiEncConfiguration.ExternalRuntimeOptions options,
                                           bool selfTest,
                                           out ProcessStartInfo startInfo,
                                           out string error )
    {
      startInfo = null;
      error = string.Empty;
      options ??= new RoiEncConfiguration.ExternalRuntimeOptions();

      var pythonPath = RoiPathUtility.ResolveConfiguredPath( options.PythonExecutablePath );
      if ( !File.Exists( pythonPath ) ) {
        error = $"roi_external_runtime_python_missing:{pythonPath}";
        return false;
      }

      var entryScriptPath = RoiPathUtility.ResolveConfiguredPath( options.EntryScriptPath );
      if ( !File.Exists( entryScriptPath ) ) {
        error = $"roi_external_runtime_entry_script_missing:{entryScriptPath}";
        return false;
      }

      var configPath = string.IsNullOrWhiteSpace( options.ConfigPath ) ?
                       string.Empty :
                       RoiPathUtility.ResolveConfiguredPath( options.ConfigPath );
      if ( !selfTest && !string.IsNullOrWhiteSpace( configPath ) && !File.Exists( configPath ) ) {
        error = $"roi_external_runtime_config_missing:{configPath}";
        return false;
      }

      var argumentsBuilder = new StringBuilder();
      argumentsBuilder.Append( Quote( entryScriptPath ) );
      if ( !string.IsNullOrWhiteSpace( configPath ) ) {
        argumentsBuilder.Append( " --config " );
        argumentsBuilder.Append( Quote( configPath ) );
      }

      if ( selfTest )
        argumentsBuilder.Append( " --self-test" );

      if ( !string.IsNullOrWhiteSpace( options.ExtraArguments ) ) {
        argumentsBuilder.Append( " " );
        argumentsBuilder.Append( options.ExtraArguments.Trim() );
      }

      startInfo = new ProcessStartInfo
      {
        FileName = pythonPath,
        Arguments = argumentsBuilder.ToString(),
        WorkingDirectory = RoiPathUtility.ResolveRepoRoot(),
        UseShellExecute = false,
        CreateNoWindow = options.CreateNoWindow
      };
      return true;
    }

    private static string Quote( string value )
    {
      return "\"" + ( value ?? string.Empty ).Replace( "\"", "\\\"" ) + "\"";
    }
  }
}
